using System;
using System.Globalization;
using PM.horizOn.Cloud.Helper;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Objects.Data;
using UnityEngine;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>Server-owned balances. Reuse one operation ID for every retry of the same action.</summary>
    public class PlayerValuesManager : BaseManager<PlayerValuesManager>
    {
        public const long MaxSafeInteger = 9007199254740991;
        private const string Endpoint = "/api/v1/app/player-values";
        private PlayerValuesState _state;
        private PlayerValuesOffers _offers;
        private string _identity;
        private long _epoch, _revision = -1, _definition = -1, _serverMillis = -1;
        private long _clockObservedAt;
        public string LastErrorCode { get; private set; }
        internal long CacheEpoch { get { SyncIdentity(); return _epoch; } }
        public bool HasServerClock { get { SyncIdentity(); return _serverMillis >= 0; } }
        public PlayerValuesState CurrentState { get { SyncIdentity(); return Clone(_state); } }
        public PlayerValuesOffers CurrentOffers { get { SyncIdentity(); return Clone(_offers); } }
        protected override void RegisterEvents()
        {
            RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => ResetIdentity());
            RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => SyncIdentity());
        }
        public void ClearCache() { SyncIdentity(); _epoch++; _state = null; _offers = null; }
        public string CreateOperationId()
        {
            SyncIdentity();
            long now = _serverMillis >= 0 ? _serverMillis + (System.Diagnostics.Stopwatch.GetTimestamp() - _clockObservedAt) * 1000 / System.Diagnostics.Stopwatch.Frequency : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return now.ToString(CultureInfo.InvariantCulture) + ":" + Guid.NewGuid().ToString("D");
        }
        public Task<PlayerValuesResult> GetState(string userId) => StateCall(userId, null, null);
        public Task<PlayerValuesResult> Purchase(string userId, string offerKey, string operationId)
        {
            if (string.IsNullOrWhiteSpace(offerKey) || !ValidOperationId(operationId)) return Task.FromResult(Failure("INVALID_REQUEST"));
            return StateCall(userId, "/purchase", new PurchaseRequest { userId = userId, offerKey = offerKey, operationId = operationId });
        }
        public Task<PlayerValuesResult> Adjust(string userId, string key, long amount, string reason, string operationId)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(reason) || amount == 0 || amount < -MaxSafeInteger || amount > MaxSafeInteger || !ValidOperationId(operationId)) return Task.FromResult(Failure("INVALID_REQUEST"));
            return StateCall(userId, "/adjust", new AdjustRequest { userId = userId, key = key, amount = amount, reason = reason, operationId = operationId });
        }
        public async Task<PlayerValuesOffersResult> GetOffers(string userId)
        {
            if (!TryBegin(userId, out var identity, out var epoch)) return new PlayerValuesOffersResult { ErrorCode = LastErrorCode, Error = LastErrorCode };
            var response = await HorizonApp.Network.GetAsync<PlayerValuesOffers>(Endpoint + "/offers?userId=" + Uri.EscapeDataString(userId), true);
            if (!IsCurrent(identity, epoch)) return new PlayerValuesOffersResult { ErrorCode = "STALE_RESPONSE", Error = "Session or cache changed" };
            var data = response.Data;
            if (!response.IsSuccess || data == null || data.definitions == null || data.offers == null || data.definitionVersion < 0 || data.definitionVersion < _definition || !AcceptClock(data.serverTime))
            { LastErrorCode = response.IsSuccess ? "STALE_RESPONSE" : response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode); return new PlayerValuesOffersResult { ErrorCode = LastErrorCode, Error = response.Error ?? LastErrorCode }; }
            if (data.definitionVersion > _definition) { _definition = data.definitionVersion; _state = null; }
            _offers = Clone(data); LastErrorCode = null;
            return new PlayerValuesOffersResult { IsSuccess = true, Data = data };
        }
        private async Task<PlayerValuesResult> StateCall(string userId, string route, object request)
        {
            if (!TryBegin(userId, out var identity, out var epoch)) return Failure(LastErrorCode);
            var response = route == null
                ? await HorizonApp.Network.GetAsync<PlayerValuesState>(Endpoint + "?userId=" + Uri.EscapeDataString(userId), true)
                : await HorizonApp.Network.PostAsync<PlayerValuesState>(Endpoint + route, request, true);
            if (!IsCurrent(identity, epoch)) return new PlayerValuesResult { ErrorCode = "STALE_RESPONSE", Error = "Session or cache changed" };
            PlayerValuesState state = response.Data;
            if (!response.IsSuccess && !string.IsNullOrEmpty(response.ErrorBody))
            { try { state = JsonUtility.FromJson<ErrorEnvelope>(PrepareJson(response.ErrorBody))?.state; } catch { state = null; } }
            if (state != null && !AcceptState(state, userId)) return Failure("STALE_RESPONSE");
            if (response.IsSuccess && state == null) return Failure("INVALID_RESPONSE");
            LastErrorCode = response.IsSuccess ? null : response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode);
            return new PlayerValuesResult { IsSuccess = response.IsSuccess, State = state, ErrorCode = LastErrorCode, Error = response.Error };
        }
        /// <summary>Accept a complete authorized snapshot from Inbox/gift rewards, never add deltas.</summary>
        public bool AcceptServerState(string userId, PlayerValuesState state)
        {
            if (!TryBegin(userId, out _, out _) || state == null || !AcceptState(state, userId)) { LastErrorCode = "STALE_RESPONSE"; return false; }
            LastErrorCode = null; return true;
        }
        private bool AcceptState(PlayerValuesState state, string userId)
        {
            if (state.userId != userId || state.values == null || state.revision < 0 || state.definitionVersion < 0 || state.revision < _revision || state.definitionVersion < _definition ||
                state.operationWindow == null || !TryMillis(state.operationWindow.serverTime, out var millis) || millis < _serverMillis) return false;
            foreach (var value in state.values) if (value == null || value.balance < -MaxSafeInteger || value.balance > MaxSafeInteger || value.earnedToday < 0 || value.earnedToday > MaxSafeInteger) return false;
            if (state.definitionVersion > _definition) _offers = null;
            _revision = state.revision; _definition = state.definitionVersion; AcceptClock(state.operationWindow.serverTime); _state = Clone(state); return true;
        }
        private bool AcceptClock(string value)
        {
            if (!TryMillis(value, out var millis) || millis < _serverMillis) return false;
            _serverMillis = millis; _clockObservedAt = System.Diagnostics.Stopwatch.GetTimestamp(); return true;
        }
        private bool TryBegin(string userId, out string identity, out long epoch)
        {
            SyncIdentity(); identity = _identity; epoch = _epoch;
            var user = UserManager.Instance.CurrentUser;
            LastErrorCode = user == null || !user.IsValid() || string.IsNullOrEmpty(HorizonApp.Network.GetSessionToken()) || user.AccessToken != HorizonApp.Network.GetSessionToken() ? "SESSION_REQUIRED" : user.UserId != userId ? "SESSION_FORBIDDEN" : null;
            return LastErrorCode == null;
        }
        private bool IsCurrent(string identity, long epoch) { SyncIdentity(); return identity == _identity && epoch == _epoch; }
        private void ResetIdentity() { _identity = null; _epoch++; _state = null; _offers = null; _revision = _definition = _serverMillis = -1; _clockObservedAt = 0; LastErrorCode = null; }
        private void SyncIdentity()
        {
            string identity = UserManager.Instance.CurrentUser?.UserId + "|" + HorizonApp.Network.GetSessionToken() + "|" + HorizonApp.Network.GetApiKey() + "|" + HorizonApp.Network.GetActiveHost();
            if (identity == _identity) return;
            ResetIdentity(); _identity = identity;
        }
        private PlayerValuesResult Failure(string code) { LastErrorCode = code; return new PlayerValuesResult { ErrorCode = code, Error = code }; }
        private static bool TryMillis(string value, out long millis) { millis = 0; if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)) return false; millis = time.ToUnixTimeMilliseconds(); return true; }
        private static bool ValidOperationId(string value) { var parts = value?.Split(':'); return parts?.Length == 2 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var time) && time > 0 && Guid.TryParseExact(parts[1], "D", out _); }
        internal static string PrepareJson(string json) => PlayerValuesJson.Prepare(json);
        private static T Clone<T>(T value) where T : class => value == null ? null : JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        [Serializable] private class ErrorEnvelope { public PlayerValuesState state; }
        [Serializable] private class PurchaseRequest { public string userId, offerKey, operationId; }
        [Serializable] private class AdjustRequest { public string userId, key, reason, operationId; public long amount; }
    }
}
