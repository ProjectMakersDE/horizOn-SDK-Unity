using System;
using System.Threading.Tasks;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Read explicitly on opening the screen. Bind ClaimEntry to a player button, never to refresh.</summary>
    public class InboxExample : MonoBehaviour
    {
        private string _userId, _broadcastTopic, _snapshotScope;
        private int _generation;
        private RealtimeManager _realtime;
        private InboxSnapshot _currentInbox;
        public InboxSnapshot CurrentInbox
        {
            get { if (_snapshotScope != Scope()) _currentInbox = null; return _currentInbox; }
            private set { _currentInbox = value; _snapshotScope = Scope(); }
        }
        public async Task<bool> ConnectPlayer()
        {
            int generation = _generation;
            if (!isActiveAndEnabled) return false;
            HorizonApp.Initialize();
            CurrentInbox = null;
            if (!await new HorizonServer().Connect()) return false;
            var auth = UserManager.Instance;
            if (auth.CurrentUser == null || !auth.CurrentUser.IsValid())
            {
                if (PlayerPrefs.HasKey("horizOn_AnonymousToken")) { if (!await auth.RestoreAnonymousSession()) return false; }
                else if (!await auth.SignUpAnonymous("InboxPlayer")) return false;
            }
            if (generation != _generation || !isActiveAndEnabled) return false;
            _userId = auth.CurrentUser.UserId; return true;
        }
        public async Task Refresh()
        {
            if (string.IsNullOrEmpty(_userId) || !isActiveAndEnabled) return;
            int generation = _generation; string scope = Scope();
            var result = await InboxManager.Instance.GetInbox(_userId);
            if (generation != _generation || scope != Scope() || !isActiveAndEnabled) return;
            if (result.IsSuccess)
            {
                CurrentInbox = result.Data;
                // On another lifecycle-driven refresh, wait at least CurrentInbox.pollAfterSeconds.
                foreach (var entry in CurrentInbox.entries) HorizonApp.Log.Info($"{entry.id}: {entry.textKey}; overdue={entry.overdue}; claimed={entry.claimed}");
            }
            else HorizonApp.Log.Warning($"Inbox: {result.ErrorCode}");
        }
        public async Task ClaimEntry(string originalEntryId)
        {
            if (string.IsNullOrEmpty(_userId) || !isActiveAndEnabled) return;
            int generation = _generation; string scope = Scope();
            // The screen persists this original entry ID with the player's action. A timeout
            // reuses it. The server chooses all rewards; this method never creates a new grant.
            var result = await InboxManager.Instance.Claim(_userId, originalEntryId);
            if (generation != _generation || scope != Scope() || !isActiveAndEnabled) return;
            if (!result.IsSuccess) { HorizonApp.Log.Warning($"Claim retained for retry: {result.ErrorCode}"); return; }
            CurrentInbox = result.Data.inbox;
            if (result.Data.playerValues != null)
                foreach (var value in result.Data.playerValues.values) HorizonApp.Log.Info($"Server {value.key}: {value.balance}");
        }
        public void ListenForInvalidations(string projectApiKeyId = null)
        {
            if (string.IsNullOrEmpty(_userId) || _realtime != null) return;
            _realtime = RealtimeManager.Instance;
            _realtime.EventReceived += OnInvalidation;
            _realtime.ResyncRequired += OnResync;
            _realtime.Subscribe("inbox:" + _userId);
            if (Guid.TryParse(projectApiKeyId, out var id)) { _broadcastTopic = "inbox:project." + id.ToString("D"); _realtime.Subscribe(_broadcastTopic); }
        }
        private void OnInvalidation(RealtimeFrame frame)
        {
            if (frame.topic == "inbox:" + _userId || frame.topic == _broadcastTopic) OnResync("inbox changed");
        }
        private void OnResync(string reason) { InboxManager.Instance.ClearCache(); CurrentInbox = null; }
        private string Scope() => HorizonApp.Network.GetActiveHost() + "|" + HorizonApp.Network.GetApiKey() + "|" + HorizonApp.Network.GetSessionToken() + "|" + UserManager.Instance.CurrentUser?.UserId;
        private void OnDisable()
        {
            _generation++;
            if (_realtime != null)
            {
                _realtime.EventReceived -= OnInvalidation; _realtime.ResyncRequired -= OnResync;
                _realtime.Unsubscribe("inbox:" + _userId);
                if (_broadcastTopic != null) _realtime.Unsubscribe(_broadcastTopic);
            }
            _realtime = null; _broadcastTopic = null; CurrentInbox = null; _userId = null;
        }
    }
}
