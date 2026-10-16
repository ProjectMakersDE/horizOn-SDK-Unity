using System;
using System.Globalization;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
namespace PM.horizOn.Cloud.Manager
{
    /// <summary>Explicit bounded Inbox snapshots. Retry a claim with its original entry ID.</summary>
    public class InboxManager : BaseManager<InboxManager>
    {
        private const string Endpoint = "/api/v1/app/inbox";
        private readonly InboxRefreshQueue _reads = new InboxRefreshQueue();
        private InboxSnapshot _current;
        private string _identity;
        private long _epoch, _revision = -1, _broadcastRevision = -1, _serverMillis = -1;
        public string LastErrorCode { get; private set; }
        public InboxSnapshot CurrentInbox { get { SyncIdentity(); return InboxJson.Clone(_current); } }
        protected override void RegisterEvents()
        {
            RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => ResetIdentity());
            RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => SyncIdentity());
        }
        /// <summary>Invalidate on matching realtime metadata or reconnect. Watermarks survive a clear.</summary>
        public void ClearCache() { SyncIdentity(); _current = null; _epoch++; }
        public async Task<InboxResult> GetInbox(string userId, string since = null)
        {
            if (!Begin(userId, out var identity, out var epoch)) return Failure(LastErrorCode);
            if (since != null && since.Length > 128) return Failure("INVALID_REQUEST");
            var result = await _reads.Enqueue(() => Read(userId, since, identity, epoch));
            return IsCurrent(identity, epoch) ? new InboxResult { IsSuccess = result.IsSuccess, Data = InboxJson.Clone(result.Data), ErrorCode = result.ErrorCode, Error = result.Error } : Failure("STALE_RESPONSE");
        }
        private async Task<InboxResult> Read(string userId, string since, string identity, long epoch)
        {
            if (!IsCurrent(identity, epoch)) return Failure("STALE_RESPONSE");
            string cursor = since ?? _current?.cursor;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string url = Endpoint + "?userId=" + Uri.EscapeDataString(userId);
                if (!string.IsNullOrEmpty(cursor)) url += "&since=" + Uri.EscapeDataString(cursor);
                var response = await HorizonApp.Network.GetAsync<InboxSnapshot>(url, true);
                if (!IsCurrent(identity, epoch)) return Failure("STALE_RESPONSE");
                if (!response.IsSuccess) return Failure(response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode), response.Error);
                if (response.Data?.unchanged == true && _current == null && attempt == 0) { cursor = null; continue; }
                return Accept(response.Data, userId) ? Success() : Failure("STALE_RESPONSE");
            }
            return Failure("INVALID_RESPONSE");
        }
        public Task<InboxResult> MarkRead(string userId, string entryId) => Mutate(userId, entryId, "/read");
        public Task<InboxResult> Remove(string userId, string entryId) => Mutate(userId, entryId, "/remove");
        private async Task<InboxResult> Mutate(string userId, string entryId, string route)
        {
            if (!Begin(userId, out _, out _) || !ValidEntry(entryId)) return Failure(LastErrorCode ?? "INVALID_REQUEST");
            ClearCache(); string identity = _identity; long epoch = _epoch;
            var response = await HorizonApp.Network.PostAsync<InboxSnapshot>(Endpoint + route, new ActionRequest { userId = userId, entryId = entryId }, true);
            if (!IsCurrent(identity, epoch)) return Failure("STALE_RESPONSE");
            if (!response.IsSuccess) return Failure(response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode), response.Error);
            return Accept(response.Data, userId) ? Success() : Failure("STALE_RESPONSE");
        }
        public async Task<InboxClaimResult> Claim(string userId, string entryId)
        {
            if (!Begin(userId, out _, out _) || !ValidEntry(entryId)) return ClaimFailure(LastErrorCode ?? "INVALID_REQUEST");
            ClearCache(); string identity = _identity; long epoch = _epoch;
            var values = PlayerValuesManager.Instance; long valuesEpoch = values.CacheEpoch;
            var response = await HorizonApp.Network.PostAsync<InboxClaimResponse>(Endpoint + "/claim", new ActionRequest { userId = userId, entryId = entryId }, true);
            if (!IsCurrent(identity, epoch) || values.CacheEpoch != valuesEpoch) return ClaimFailure("STALE_RESPONSE");
            if (!response.IsSuccess) return ClaimFailure(response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode), response.Error);
            var data = response.Data;
            if (data == null || data.entryId != entryId || !Accept(data.inbox, userId)) return ClaimFailure("STALE_RESPONSE");
            if (data.playerValues != null && !values.AcceptServerState(userId, data.playerValues)) return ClaimFailure("STALE_RESPONSE");
            data.inbox = CurrentInbox;
            if (data.playerValues != null) data.playerValues = PlayerValuesManager.Instance.CurrentState;
            LastErrorCode = null;
            return new InboxClaimResult { IsSuccess = true, Data = data };
        }
        private bool Accept(InboxSnapshot data, string userId)
        {
            if (data == null || !string.Equals(data.userId, userId, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(data.cursor) || data.cursor.Length > 128
                || data.pollAfterSeconds <= 0 || data.revision < _revision || data.broadcastRevision < _broadcastRevision || data.revision < 0 || data.broadcastRevision < 0
                || data.entries == null || data.entries.Length > 550 || data.limits == null || !DateTimeOffset.TryParse(data.serverTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
                || time.ToUnixTimeMilliseconds() < _serverMillis) return false;
            if (data.unchanged)
            {
                if (_current == null || data.cursor != _current.cursor || data.entries.Length != 0) return false;
                data.entries = InboxJson.Clone(_current).entries;
            }
            _revision = data.revision; _broadcastRevision = data.broadcastRevision; _serverMillis = time.ToUnixTimeMilliseconds();
            _current = InboxJson.Clone(data); LastErrorCode = null; return true;
        }
        private bool Begin(string userId, out string identity, out long epoch)
        {
            SyncIdentity(); identity = _identity; epoch = _epoch;
            var user = UserManager.Instance.CurrentUser;
            LastErrorCode = user == null || !user.IsValid() || string.IsNullOrEmpty(HorizonApp.Network.GetSessionToken()) || user.AccessToken != HorizonApp.Network.GetSessionToken()
                ? "SESSION_REQUIRED" : !string.Equals(user.UserId, userId, StringComparison.OrdinalIgnoreCase) ? "SESSION_FORBIDDEN" : null;
            return LastErrorCode == null;
        }
        private void SyncIdentity()
        {
            string identity = UserManager.Instance.CurrentUser?.UserId + "|" + HorizonApp.Network.GetSessionToken() + "|" + HorizonApp.Network.GetApiKey() + "|" + HorizonApp.Network.GetActiveHost()
                + "|" + HorizonApp.Network.ContextGeneration;
            if (_identity == identity) return;
            ResetIdentity(); _identity = identity;
        }
        private void ResetIdentity() { _identity = null; _epoch++; _current = null; _revision = _broadcastRevision = _serverMillis = -1; LastErrorCode = null; }
        private bool IsCurrent(string identity, long epoch) { SyncIdentity(); return identity == _identity && epoch == _epoch; }
        private InboxResult Success() => new InboxResult { IsSuccess = true, Data = CurrentInbox };
        private InboxResult Failure(string code, string message = null) { LastErrorCode = code; return new InboxResult { ErrorCode = code, Error = message ?? code }; }
        private InboxClaimResult ClaimFailure(string code, string message = null) { LastErrorCode = code; return new InboxClaimResult { ErrorCode = code, Error = message ?? code }; }
        private static bool ValidEntry(string value)
        {
            if (value == null) return false; var parts = value.Split(':');
            return parts.Length == 2 && (parts[0] == "message" || parts[0] == "broadcast") && Guid.TryParseExact(parts[1], "D", out _);
        }
        [Serializable] private sealed class ActionRequest { public string userId, entryId; }
    }
}
