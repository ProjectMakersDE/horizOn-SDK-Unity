using System;
using System.Threading.Tasks;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Read on opening the screen. New selections use ClaimNewEntry; saved claims use ClaimEntry.</summary>
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
                foreach (var entry in CurrentInbox.entries)
                {
                    // Render free text as plain text (disable rich text in your UI). Other entries use localization keys.
                    string text = entry.TryGetFreeText(out var plainText) ? plainText : entry.textKey;
                    HorizonApp.Log.Info($"{entry.id}: {text}; overdue={entry.overdue}; claimed={entry.claimed}");
                    if (entry.TryGetSource(out var source)) HorizonApp.Log.Info($"Source {source.Kind}: {source.SourceId}");
                }
            }
            else HorizonApp.Log.Warning($"Inbox: {result.ErrorCode}");
        }
        /// <summary>
        /// Validate a new selection before the game saves an intent. The synchronous callback must
        /// durably save/verify this exact entry ID for the current project/player, refuse conflicting
        /// pending claims and return true only after successful persistence. It must never replace one.
        /// </summary>
        public async Task ClaimNewEntry(string selectedEntryId, Func<string, bool> persistOriginalIntent)
        {
            if (persistOriginalIntent == null || !CanStartNewClaim(InboxManager.Instance.CurrentInbox, selectedEntryId)) return;
            int generation = _generation; string scope = Scope();
            bool saved;
            try { saved = persistOriginalIntent(selectedEntryId); }
            catch (Exception) { HorizonApp.Log.Warning("Claim was not sent: intent persistence failed."); return; }
            // A failed write, invalidation or identity change keeps any saved intent for reconciliation.
            // Re-read the SDK cache: the screen's display copy is not an authorization source.
            if (!saved || generation != _generation || scope != Scope()
                || !CanStartNewClaim(InboxManager.Instance.CurrentInbox, selectedEntryId)) return;
            await ClaimEntry(selectedEntryId);
        }
        private bool CanStartNewClaim(InboxSnapshot snapshot, string entryId)
        {
            var user = UserManager.Instance.CurrentUser;
            if (!isActiveAndEnabled || string.IsNullOrEmpty(_userId) || user == null || !user.IsValid()
                || user.UserId != _userId || user.AccessToken != HorizonApp.Network.GetSessionToken()
                || snapshot == null || snapshot.userId != _userId || snapshot.entries == null || string.IsNullOrEmpty(entryId)) return false;
            foreach (var entry in snapshot.entries)
                if (entry != null && entry.id == entryId)
                    return entry.CanClaim;
            return false;
        }
        /// <summary>Replay only an already durably saved original entry ID, even if absent or claimed in the current snapshot.</summary>
        public async Task ClaimEntry(string originalEntryId)
        {
            if (string.IsNullOrEmpty(_userId) || !isActiveAndEnabled) return;
            int generation = _generation; string scope = Scope();
            // The caller already saved this original ID. Never re-run new-selection validation on
            // a retry: an earlier successful response may have been lost. The server chooses rewards.
            var result = await InboxManager.Instance.Claim(_userId, originalEntryId);
            if (generation != _generation || scope != Scope() || !isActiveAndEnabled) return;
            if (!result.IsSuccess) { HorizonApp.Log.Warning($"Claim retained for retry: {result.ErrorCode}"); return; }
            CurrentInbox = result.Data.inbox;
            if (result.Data.playerValues != null)
                foreach (var value in result.Data.playerValues.values) HorizonApp.Log.Info($"Server {value.key}: {value.balance}");
            // Item returns are references. Refresh the authoritative inventory; never construct a local item from this descriptor.
            foreach (var item in result.Data.items) HorizonApp.Log.Info($"Returned item {item.itemKey}: {item.instanceId}");
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
