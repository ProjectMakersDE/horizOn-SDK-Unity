using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
namespace PM.horizOn.Cloud.Manager
{
    /// <summary>Explicit player achievements. Rewards are delivered to Inbox and credited only by its claim.</summary>
    public class AchievementsManager : BaseManager<AchievementsManager>
    {
        private const string Endpoint = "/api/v1/app/achievements";
        private readonly SemaphoreSlim _requests = new SemaphoreSlim(1, 1);
        private NetworkService _network;
        private string _identity, _definitionVersion;
        private long _epoch, _revision = -1, _serverMillis = -1;
        private AchievementDefinition[] _definitions;
        private AchievementsSnapshot _current;
        public string LastErrorCode { get; private set; }
        public AchievementsSnapshot CurrentAchievements { get { SyncIdentity(); return AchievementsJson.Clone(_current); } }
        protected override void RegisterEvents()
        {
            RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => ResetIdentity());
            RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => SyncIdentity());
        }
        protected override void OnDisable() { ResetIdentity(); base.OnDisable(); }
        /// <summary>Invalidate on own-player realtime metadata or reconnect; no automatic read or poll.</summary>
        public void ClearCache()
        { SyncIdentity(); _epoch++; _current = null; _definitions = null; _definitionVersion = null; }
        public async Task<AchievementsResult> GetAchievements(string userId, string definitionVersion = null)
        {
            if (!Begin(userId, out var identity, out var epoch)) return ReadFailure(LastErrorCode);
            if (!string.IsNullOrEmpty(definitionVersion) && !AchievementsJson.ValidVersion(definitionVersion)) return ReadFailure("INVALID_REQUEST");
            await _requests.WaitAsync();
            try
            {
                if (!IsCurrent(identity, epoch)) return ReadFailure("STALE_RESPONSE");
                // Send a conditional version only with metadata belonging to this exact scope/version.
                string known = _definitions != null && (string.IsNullOrEmpty(definitionVersion) || definitionVersion == _definitionVersion) ? _definitionVersion : null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    string url = Endpoint + "?userId=" + Uri.EscapeDataString(userId);
                    if (known != null) url += "&definitionVersion=" + Uri.EscapeDataString(known);
                    var response = await HorizonApp.Network.GetAsync<AchievementsSnapshot>(url, true);
                    if (!IsCurrent(identity, epoch)) return ReadFailure("STALE_RESPONSE");
                    if (!response.IsSuccess) return ReadFailure(ErrorCode(response), response.Error);
                    string error = Accept(response.Data, userId, known);
                    if (error == "METADATA_REQUIRED" && attempt == 0) { known = null; continue; }
                    return error == null ? new AchievementsResult { IsSuccess = true, Data = CurrentAchievements } : ReadFailure(error == "METADATA_REQUIRED" ? "INVALID_RESPONSE" : error);
                }
                return ReadFailure("INVALID_RESPONSE");
            }
            finally { _requests.Release(); }
        }
        /// <summary>Retry the same manual key. The server owns its permanent unlock/reward identity.</summary>
        public async Task<AchievementUnlockResult> Unlock(string userId, string key)
        {
            if (!Begin(userId, out var identity, out var epoch)) return UnlockFailure(LastErrorCode);
            if (!AchievementsJson.ValidKey(key)) return UnlockFailure("INVALID_REQUEST");
            var command = new UnlockRequest { userId = userId, key = key };
            await _requests.WaitAsync();
            try
            {
                if (!IsCurrent(identity, epoch)) return UnlockFailure("STALE_RESPONSE");
                _current = null;
                var response = await HorizonApp.Network.PostAsync<AchievementUnlockResponse>(Endpoint + "/unlock", command, true);
                if (!IsCurrent(identity, epoch)) return UnlockFailure("STALE_RESPONSE");
                if (!response.IsSuccess) return UnlockFailure(ErrorCode(response), response.Error);
                var data = response.Data;
                if (data?.achievements == null || data.newlyUnlocked == null || data.deliveryResults == null
                    || (data.replayed && data.newlyUnlocked.Length != 0) || data.newlyUnlocked.Length > 1
                    || (data.newlyUnlocked.Length == 1 && data.newlyUnlocked[0] != key)
                    || !MatchesOutcomes(data.deliveryResults, new[] { key })) return UnlockFailure("INVALID_RESPONSE");
                string error = Accept(data.achievements, userId, null);
                if (error != null) return UnlockFailure(error == "METADATA_REQUIRED" ? "INVALID_RESPONSE" : error);
                InvalidateDeliveredInbox(data.deliveryResults);
                data.achievements = CurrentAchievements;
                LastErrorCode = null;
                return new AchievementUnlockResult { IsSuccess = true, Data = AchievementsJson.Clone(data) };
            }
            finally { _requests.Release(); }
        }
        /// <summary>Explicitly repeat the original at-most-eight pending keys; never locally credit rewards.</summary>
        public async Task<AchievementDeliveryResult> DeliverRewards(string userId, IList<string> keys)
        {
            if (!Begin(userId, out var identity, out var epoch)) return DeliveryFailure(LastErrorCode);
            if (keys == null || keys.Count < 1 || keys.Count > 8) return DeliveryFailure("INVALID_REQUEST");
            var selected = new string[keys.Count]; var distinct = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            { if (!AchievementsJson.ValidKey(keys[i]) || !distinct.Add(keys[i])) return DeliveryFailure("INVALID_REQUEST"); selected[i] = keys[i]; }
            var command = new DeliveryRequest { userId = userId, keys = selected };
            await _requests.WaitAsync();
            try
            {
                if (!IsCurrent(identity, epoch)) return DeliveryFailure("STALE_RESPONSE");
                _current = null;
                var response = await HorizonApp.Network.PostAsync<AchievementDeliveryResponse>(Endpoint + "/rewards/deliver", command, true);
                if (!IsCurrent(identity, epoch)) return DeliveryFailure("STALE_RESPONSE");
                if (!response.IsSuccess) return DeliveryFailure(ErrorCode(response), response.Error);
                var data = response.Data;
                if (data?.achievements == null || !MatchesOutcomes(data.results, selected)) return DeliveryFailure("INVALID_RESPONSE");
                string error = Accept(data.achievements, userId, null);
                if (error != null) return DeliveryFailure(error == "METADATA_REQUIRED" ? "INVALID_RESPONSE" : error);
                InvalidateDeliveredInbox(data.results);
                data.achievements = CurrentAchievements; LastErrorCode = null;
                return new AchievementDeliveryResult { IsSuccess = true, Data = AchievementsJson.Clone(data) };
            }
            finally { _requests.Release(); }
        }
        private string Accept(AchievementsSnapshot data, string userId, string requestedVersion)
        {
            if (data == null || !string.Equals(data.userId, userId, StringComparison.OrdinalIgnoreCase) || !AchievementsJson.ValidVersion(data.definitionVersion)
                || data.revision < 0 || data.progress == null || data.limits == null
                || !DateTimeOffset.TryParse(data.serverTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)) return "INVALID_RESPONSE";
            long millis = time.ToUnixTimeMilliseconds();
            if (data.revision < _revision || millis < _serverMillis) return "STALE_RESPONSE";
            if (data.definitions == null)
            {
                if (_definitions == null || requestedVersion != data.definitionVersion || _definitionVersion != data.definitionVersion) return "METADATA_REQUIRED";
                // Opaque versions have only equality semantics, never numeric/lexical ordering.
                data.definitions = AchievementsJson.Clone(new Definitions { rows = _definitions }).rows;
            }
            _current = AchievementsJson.Clone(data); _definitions = _current.definitions;
            _definitionVersion = data.definitionVersion; _revision = data.revision; _serverMillis = millis; LastErrorCode = null;
            return null;
        }
        private static bool MatchesOutcomes(AchievementDeliveryOutcome[] outcomes, string[] requested)
        {
            if (outcomes == null || outcomes.Length != requested.Length) return false;
            var keys = new HashSet<string>(requested, StringComparer.Ordinal);
            foreach (var outcome in outcomes) if (outcome == null || !keys.Remove(outcome.key)) return false;
            return keys.Count == 0;
        }
        private static void InvalidateDeliveredInbox(AchievementDeliveryOutcome[] outcomes)
        { foreach (var result in outcomes) if (result.state == "delivered") { InboxManager.Instance.ClearCache(); return; } }
        private bool Begin(string userId, out string identity, out long epoch)
        {
            SyncIdentity(); identity = _identity; epoch = _epoch;
            var user = UserManager.Instance.CurrentUser;
            LastErrorCode = user == null || !user.IsValid() || string.IsNullOrEmpty(HorizonApp.Network.GetSessionToken()) || user.AccessToken != HorizonApp.Network.GetSessionToken()
                ? "SESSION_REQUIRED" : !string.Equals(user.UserId, userId, StringComparison.OrdinalIgnoreCase) ? "SESSION_FORBIDDEN"
                : !Guid.TryParseExact(userId, "D", out _) ? "INVALID_REQUEST" : null;
            return LastErrorCode == null;
        }
        private void SyncIdentity()
        {
            var network = HorizonApp.Network;
            string identity = UserManager.Instance.CurrentUser?.UserId + "|" + network.GetSessionToken() + "|" + network.GetApiKey() + "|" + network.GetActiveHost() + "|" + network.ContextGeneration;
            if (ReferenceEquals(_network, network) && _identity == identity) return;
            ResetIdentity(); _network = network; _identity = identity;
        }
        private void ResetIdentity()
        { _epoch++; _identity = null; _network = null; _current = null; _definitions = null; _definitionVersion = null; _revision = _serverMillis = -1; LastErrorCode = null; }
        private bool IsCurrent(string identity, long epoch) { SyncIdentity(); return _identity == identity && _epoch == epoch; }
        private static string ErrorCode<T>(NetworkResponse<T> response) where T : class => response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode);
        private AchievementsResult ReadFailure(string code, string error = null) { LastErrorCode = code; return new AchievementsResult { ErrorCode = code, Error = error ?? code }; }
        private AchievementUnlockResult UnlockFailure(string code, string error = null) { LastErrorCode = code; return new AchievementUnlockResult { ErrorCode = code, Error = error ?? code }; }
        private AchievementDeliveryResult DeliveryFailure(string code, string error = null) { LastErrorCode = code; return new AchievementDeliveryResult { ErrorCode = code, Error = error ?? code }; }
        [Serializable] private sealed class Definitions { public AchievementDefinition[] rows; }
        [Serializable] private sealed class UnlockRequest { public string userId, key; }
        [Serializable] private sealed class DeliveryRequest { public string userId; public string[] keys; }
    }
}
