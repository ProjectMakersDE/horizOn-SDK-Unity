using System;
using System.Diagnostics;
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
using PM.horizOn.Cloud.Transport;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>Six item gift App routes. Claiming stays the Inbox claim. Sends go once per explicit call from a persisted original intent.</summary>
    public sealed class ItemGiftsManager : BaseManager<ItemGiftsManager>
    {
        private const string Endpoint = "/api/v1/app/item-gifts";
        private readonly SemaphoreSlim _requests = new SemaphoreSlim(1, 1);
        private readonly Stopwatch _clock = new Stopwatch();
        private NetworkService _network;
        private string _identity;
        private long _epoch, _serverMillis = -1, _stateRevision = -1, _configRevision = -1;
        private ItemGiftConfigView _config;
        private ItemGiftStateView _state;
        private ItemGiftView _point;
        public string LastErrorCode { get; private set; }
        /// <summary>Raised after a send or decline. Read Inventory, Player Values and gift state again; never build an item locally.</summary>
        public event Action GiftStateInvalidated;
        public ItemGiftConfigView CurrentConfig { get { SyncIdentity(); return ItemGiftsJson.Clone(_config); } }
        public ItemGiftStateView CurrentState { get { SyncIdentity(); return ItemGiftsJson.Clone(_state); } }
        public ItemGiftView CurrentGift { get { SyncIdentity(); return ItemGiftsJson.Clone(_point); } }
        public long ServerNowMilliseconds { get { SyncIdentity(); return _serverMillis < 0 ? -1 : _serverMillis + _clock.ElapsedMilliseconds; } }
        protected override void RegisterEvents()
        {
            RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => ResetIdentity());
            RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => SyncIdentity());
        }
        protected override void OnDisable() { ResetIdentity(); base.OnDisable(); }
        public void ClearCache() { SyncIdentity(); _epoch++; _config = null; _state = null; _point = null; }
        private static string Query(string user) => "?userId=" + Uri.EscapeDataString(user ?? "");
        /// <summary>Rules: enabled flag, fee currency and amount, config revision and effective limits.</summary>
        public Task<ItemGiftsResult<ItemGiftConfigView>> GetConfig(string userId)
            => Read<ItemGiftConfigView>(userId, Endpoint + "/config" + Query(userId), value => true, value => { _configRevision = value.config.revision; _config = ItemGiftsJson.Clone(value); }, value => value.config.revision >= _configRevision);
        /// <summary>Own sent gifts and pending returns, retained history and open received offers. Bounded by the limits, no cursor.</summary>
        public Task<ItemGiftsResult<ItemGiftStateView>> GetState(string userId)
            => Read<ItemGiftStateView>(userId, Endpoint + Query(userId), value => value.state.userId == userId, value => { _stateRevision = value.state.revision; _state = ItemGiftsJson.Clone(value); }, value => value.state.revision >= _stateRevision);
        public Task<ItemGiftsResult<ItemGiftView>> GetGift(string userId, string giftId)
        {
            if (!MarketplaceJson.Uuid(giftId)) return Task.FromResult(Failure<ItemGiftView>("INVALID_REQUEST"));
            return Read<ItemGiftView>(userId, Endpoint + "/" + giftId + Query(userId), value => value.gift.id == giftId && (value.gift.senderId == userId || value.gift.recipientId == userId || value.gift.returnOwnerId == userId),
                value => _point = ItemGiftsJson.Clone(value), value => _point?.gift.id != giftId || value.gift.revision >= _point.gift.revision);
        }
        /// <summary>Looks up one own original send. OPERATION_NOT_FOUND never proves that the send did not happen.</summary>
        public Task<ItemGiftsResult<ItemGiftOperation>> GetOperation(string userId, string operationId)
        {
            if (!MarketplaceJson.Uuid(operationId)) return Task.FromResult(Failure<ItemGiftOperation>("INVALID_REQUEST"));
            return Read<ItemGiftOperation>(userId, Endpoint + "/operations/" + operationId + Query(userId), value => value.receipt.operationId == operationId);
        }
        /// <summary>Builds the original intent and sends it once. Prefer persisting <see cref="ItemGiftIntent.Send"/> first and calling <see cref="SubmitPrepared"/>.</summary>
        public Task<ItemGiftsResult<ItemGiftWritten>> SendGift(string userId, string operationId, string issuedAt, string recipientId, string instanceId, long expectedInventoryRevision, long expectedConfigRevision)
        {
            try { return SubmitPrepared(ItemGiftIntent.Send(userId, operationId, issuedAt, recipientId, instanceId, expectedInventoryRevision, expectedConfigRevision)); }
            catch (Exception e) when (e is FormatException || e is ArgumentException) { return Task.FromResult(Failure<ItemGiftWritten>("INVALID_REQUEST")); }
        }
        /// <summary>Sends the exact original bytes once. After uncertainty call this again or <see cref="GetOperation"/>; never prepare a new operation.</summary>
        public Task<ItemGiftsResult<ItemGiftWritten>> SubmitPrepared(ItemGiftIntent original)
        {
            if (original == null) return Task.FromResult(Failure<ItemGiftWritten>("INVALID_REQUEST"));
            return Request(original.UserId, Endpoint, new ItemGiftsJsonRequest(original.Json), value => original.Matches(value.receipt), (ItemGiftWritten value) => Invalidate());
        }
        /// <summary>Recipient only. A repeated decline replays the declined state.</summary>
        public Task<ItemGiftsResult<ItemGiftDeclined>> Decline(string userId, string giftId, long expectedGiftRevision)
        {
            if (!MarketplaceJson.Uuid(userId) || !MarketplaceJson.Uuid(giftId) || !ItemsJson.Revision(expectedGiftRevision)) return Task.FromResult(Failure<ItemGiftDeclined>("INVALID_REQUEST"));
            string body = "{\"userId\":" + ItemsJson.Quote(userId) + ",\"expectedGiftRevision\":" + expectedGiftRevision.ToString(CultureInfo.InvariantCulture) + "}";
            return Request(userId, Endpoint + "/" + giftId + "/decline", new ItemGiftsJsonRequest(body), value => value.gift.id == giftId && value.gift.recipientId == userId, (ItemGiftDeclined value) => Invalidate());
        }
        private void Invalidate() { _state = null; _point = null; GiftStateInvalidated?.Invoke(); }
        private Task<ItemGiftsResult<T>> Read<T>(string user, string path, Func<T, bool> valid, Action<T> accept = null, Func<T, bool> fresh = null) where T : class
            => Request(user, path, null, valid, accept, fresh);
        private async Task<ItemGiftsResult<T>> Request<T>(string user, string path, ItemGiftsJsonRequest body, Func<T, bool> valid, Action<T> accept, Func<T, bool> fresh = null) where T : class
        {
            if (!Begin(user, out string identity, out long epoch)) return Failure<T>(LastErrorCode);
            await _requests.WaitAsync();
            try
            {
                if (!Current(identity, epoch)) return Failure<T>("STALE_RESPONSE");
                var response = body == null ? await _network.GetAsync<T>(path, true) : await _network.PostOnceAsync<T>(path, body, true);
                if (!Current(identity, epoch)) return Failure<T>("STALE_RESPONSE");
                if (!response.IsSuccess) return Failure<T>(response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode), response.Error);
                if (response.Data == null || !valid(response.Data)) return Failure<T>("INVALID_RESPONSE");
                long millis = MarketplaceJson.Millis(ServerTime(response.Data));
                if (millis < _serverMillis || fresh != null && !fresh(response.Data)) return Failure<T>("STALE_RESPONSE");
                _serverMillis = millis; _clock.Restart(); accept?.Invoke(response.Data);
                if (!Current(identity, epoch)) return Failure<T>("STALE_RESPONSE");
                LastErrorCode = null; return new ItemGiftsResult<T> { IsSuccess = true, Data = ItemGiftsJson.Clone(response.Data) };
            }
            finally { _requests.Release(); }
        }
        private static string ServerTime<T>(T value) => value is ItemGiftConfigView config ? config.serverTime : value is ItemGiftStateView state ? state.serverTime : value is ItemGiftView point ? point.serverTime
            : value is ItemGiftOperation operation ? operation.serverTime : value is ItemGiftWritten written ? written.serverTime : ((ItemGiftDeclined)(object)value).serverTime;
        private bool Begin(string user, out string identity, out long epoch)
        {
            SyncIdentity(); identity = _identity; epoch = _epoch; var current = UserManager.Instance.CurrentUser;
            LastErrorCode = current == null || !current.IsValid() || string.IsNullOrEmpty(_network.GetSessionToken()) || current.AccessToken != _network.GetSessionToken() ? "SESSION_REQUIRED" : current.UserId != user ? "SESSION_FORBIDDEN" : !MarketplaceJson.Uuid(user) ? "INVALID_REQUEST" : null;
            return LastErrorCode == null;
        }
        private void SyncIdentity()
        {
            var network = HorizonApp.Network;
            string identity = UserManager.Instance.CurrentUser?.UserId + "|" + network.GetSessionToken() + "|" + network.GetApiKey() + "|" + network.GetActiveHost() + "|" + network.ContextGeneration;
            if (ReferenceEquals(network, _network) && identity == _identity) return;
            ResetIdentity(); _network = network; _identity = identity;
        }
        private void ResetIdentity() { _epoch++; _network = null; _identity = null; _config = null; _state = null; _point = null; _serverMillis = _stateRevision = _configRevision = -1; _clock.Reset(); }
        private bool Current(string identity, long epoch) { SyncIdentity(); return _identity == identity && _epoch == epoch; }
        private ItemGiftsResult<T> Failure<T>(string code, string error = null) where T : class { LastErrorCode = code; return new ItemGiftsResult<T> { ErrorCode = code, Error = error ?? code }; }
    }
}
