using System;
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
    /// <summary>Project-scoped friends. Every mutation sends its original revision/request identity exactly once.</summary>
    public class FriendsManager : BaseManager<FriendsManager>
    {
        private const string Endpoint = "/api/v1/app/friends";
        private readonly SemaphoreSlim _requests = new SemaphoreSlim(1, 1);
        private NetworkService _network;
        private string _identity;
        private long _epoch, _revision = -1, _serverMillis = -1;
        private FriendsSnapshot _current;
        public string LastErrorCode { get; private set; }
        public FriendsSnapshot CurrentFriends { get { SyncIdentity(); return FriendsJson.Clone(_current); } }
        protected override void RegisterEvents()
        { RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => ResetIdentity()); RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => SyncIdentity()); }
        protected override void OnDisable() { ResetIdentity(); base.OnDisable(); }
        /// <summary>Own-player friends:userId realtime metadata only invalidates; HTTP refresh is an explicit game action.</summary>
        public void ClearCache() { SyncIdentity(); _epoch++; _current = null; }
        public async Task<FriendsResult> GetFriends(string userId)
        {
            if (!Begin(userId, out var identity, out var epoch)) return Failure(LastErrorCode);
            await _requests.WaitAsync();
            try
            {
                if (!IsCurrent(identity, epoch)) return Failure("STALE_RESPONSE");
                var response = await HorizonApp.Network.GetAsync<FriendsSnapshot>(Endpoint + "?userId=" + Uri.EscapeDataString(userId), true);
                if (!IsCurrent(identity, epoch)) return Failure("STALE_RESPONSE");
                if (!response.IsSuccess) return Failure(ErrorCode(response), response.Error);
                string error = Accept(response.Data, userId);
                return error == null ? new FriendsResult { IsSuccess = true, Data = CurrentFriends } : Failure(error);
            }
            finally { _requests.Release(); }
        }
        /// <summary>Exact UUID or canonical H-code only. Never searches names, emails or substrings.</summary>
        public async Task<FriendLookupResult> FindPlayer(string userId, string query)
        {
            if (!Begin(userId, out var identity, out var epoch)) return LookupFailure(LastErrorCode);
            if (!FriendsJson.TryResolve(query, out var target)) return LookupFailure("INVALID_REQUEST");
            await _requests.WaitAsync();
            try
            {
                if (!IsCurrent(identity, epoch)) return LookupFailure("STALE_RESPONSE");
                var response = await HorizonApp.Network.GetAsync<FriendLookup>(Endpoint + "/find?userId=" + Uri.EscapeDataString(userId) + "&query=" + Uri.EscapeDataString(query), true);
                if (!IsCurrent(identity, epoch)) return LookupFailure("STALE_RESPONSE");
                if (!response.IsSuccess) return LookupFailure(ErrorCode(response), response.Error);
                if (response.Data == null || !string.Equals(response.Data.userId, target, StringComparison.OrdinalIgnoreCase)) return LookupFailure("INVALID_RESPONSE");
                LastErrorCode = null; return new FriendLookupResult { IsSuccess = true, Data = FriendsJson.Clone(response.Data) };
            }
            finally { _requests.Release(); }
        }
        public Task<FriendsResult> SendRequest(string userId, string targetUserId, long expectedRevision) => Mutate("request", userId, targetUserId, expectedRevision);
        public Task<FriendsResult> AcceptRequest(string userId, string targetUserId, long expectedRevision, string requestId) => Mutate("accept", userId, targetUserId, expectedRevision, requestId);
        public Task<FriendsResult> DeclineRequest(string userId, string targetUserId, long expectedRevision, string requestId) => Mutate("decline", userId, targetUserId, expectedRevision, requestId);
        public Task<FriendsResult> CancelRequest(string userId, string targetUserId, long expectedRevision, string requestId) => Mutate("cancel", userId, targetUserId, expectedRevision, requestId);
        public Task<FriendsResult> RemoveFriend(string userId, string targetUserId, long expectedRevision) => Mutate("remove", userId, targetUserId, expectedRevision);
        public Task<FriendsResult> BlockPlayer(string userId, string targetUserId, long expectedRevision) => Mutate("block", userId, targetUserId, expectedRevision);
        public Task<FriendsResult> UnblockPlayer(string userId, string targetUserId, long expectedRevision) => Mutate("unblock", userId, targetUserId, expectedRevision);
        private async Task<FriendsResult> Mutate(string action, string userId, string target, long revision, string requestId = null)
        {
            if (!Begin(userId, out var identity, out var epoch)) return Failure(LastErrorCode);
            bool needsRequest = action == "accept" || action == "decline" || action == "cancel";
            if (!FriendsJson.IsUuid(target) || string.Equals(userId, target, StringComparison.OrdinalIgnoreCase)
                || revision < 0 || revision > PlayerValuesManager.MaxSafeInteger || (needsRequest && !FriendsJson.IsUuid(requestId))) return Failure("INVALID_REQUEST");
            var command = new ActionRequest { userId = userId, targetUserId = target, expectedRevision = revision, requestId = requestId };
            await _requests.WaitAsync();
            try
            {
                if (!IsCurrent(identity, epoch)) return Failure("STALE_RESPONSE");
                _current = null;
                // A successful mutation with a lost response cannot be safely retried against a fresh revision.
                var response = await HorizonApp.Network.PostOnceAsync<FriendsSnapshot>(Endpoint + "/" + action, command, true);
                if (!IsCurrent(identity, epoch)) return Failure("STALE_RESPONSE");
                if (!response.IsSuccess) return Failure(ErrorCode(response), response.Error);
                string error = Accept(response.Data, userId);
                return error == null ? new FriendsResult { IsSuccess = true, Data = CurrentFriends } : Failure(error);
            }
            finally { _requests.Release(); }
        }
        private string Accept(FriendsSnapshot data, string userId)
        {
            if (data == null || !string.Equals(data.userId, userId, StringComparison.OrdinalIgnoreCase)
                || !DateTimeOffset.TryParse(data.serverTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)) return "INVALID_RESPONSE";
            long millis = time.ToUnixTimeMilliseconds();
            if (data.revision < _revision || millis < _serverMillis) return "STALE_RESPONSE";
            _current = FriendsJson.Clone(data); _revision = data.revision; _serverMillis = millis; LastErrorCode = null; return null;
        }
        private bool Begin(string userId, out string identity, out long epoch)
        {
            SyncIdentity(); identity = _identity; epoch = _epoch; var user = UserManager.Instance.CurrentUser;
            LastErrorCode = user == null || !user.IsValid() || string.IsNullOrEmpty(HorizonApp.Network.GetSessionToken()) || user.AccessToken != HorizonApp.Network.GetSessionToken()
                ? "SESSION_REQUIRED" : !string.Equals(user.UserId, userId, StringComparison.OrdinalIgnoreCase) ? "SESSION_FORBIDDEN"
                : !FriendsJson.IsUuid(userId) ? "INVALID_REQUEST" : null;
            return LastErrorCode == null;
        }
        private void SyncIdentity()
        {
            var network = HorizonApp.Network;
            string identity = UserManager.Instance.CurrentUser?.UserId + "|" + network.GetSessionToken() + "|" + network.GetApiKey() + "|" + network.GetActiveHost() + "|" + network.ContextGeneration;
            if (ReferenceEquals(_network, network) && _identity == identity) return;
            ResetIdentity(); _network = network; _identity = identity;
        }
        private void ResetIdentity() { _epoch++; _identity = null; _network = null; _current = null; _revision = _serverMillis = -1; LastErrorCode = null; }
        private bool IsCurrent(string identity, long epoch) { SyncIdentity(); return _identity == identity && _epoch == epoch; }
        private static string ErrorCode<T>(NetworkResponse<T> response) where T:class => response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode);
        private FriendsResult Failure(string code, string error = null) { LastErrorCode = code; return new FriendsResult { ErrorCode = code, Error = error ?? code }; }
        private FriendLookupResult LookupFailure(string code, string error = null) { LastErrorCode = code; return new FriendLookupResult { ErrorCode = code, Error = error ?? code }; }
        [Serializable] private sealed class ActionRequest { public string userId, targetUserId, requestId; public long expectedRevision; }
    }
}
