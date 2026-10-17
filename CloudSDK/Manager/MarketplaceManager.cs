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
    /// <summary>Ten App routes. Explicit pages and original intents; no purchase history or automatic write retry.</summary>
    public sealed class MarketplaceManager : BaseManager<MarketplaceManager>
    {
        private const string Endpoint = "/api/v1/app/marketplace";
        private readonly SemaphoreSlim _requests = new SemaphoreSlim(1, 1);
        private readonly Stopwatch _clock = new Stopwatch();
        private NetworkService _network;
        private string _identity;
        private long _epoch, _serverMillis = -1, _stateRevision = -1, _configRevision = -1;
        private MarketplaceLimitsView _limits;
        private MarketplaceRulesPage _rules;
        private MarketplaceStateView _state;
        private MarketplaceListingPage _query;
        private MarketplaceListingView _point;
        public string LastErrorCode { get; private set; }
        public event Action TradingStateInvalidated;
        public MarketplaceLimitsView CurrentLimits { get { SyncIdentity(); return MarketplaceJson.Clone(_limits); } }
        public MarketplaceRulesPage CurrentRulesPage { get { SyncIdentity(); return MarketplaceJson.Clone(_rules); } }
        public MarketplaceStateView CurrentMarketplace { get { SyncIdentity(); return MarketplaceJson.Clone(_state); } }
        public MarketplaceListingPage CurrentListingsPage { get { SyncIdentity(); return MarketplaceJson.Clone(_query); } }
        public MarketplaceListingView CurrentListing { get { SyncIdentity(); return MarketplaceJson.Clone(_point); } }
        public long ServerNowMilliseconds { get { SyncIdentity(); return _serverMillis < 0 ? -1 : _serverMillis + _clock.ElapsedMilliseconds; } }
        protected override void RegisterEvents()
        {
            RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => ResetIdentity());
            RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => SyncIdentity());
        }
        protected override void OnDisable() { ResetIdentity(); base.OnDisable(); }
        public void ClearCache() { SyncIdentity(); _epoch++; _limits = null; _rules = null; _state = null; _query = null; _point = null; }
        private static string Query(string user) => "?userId=" + Uri.EscapeDataString(user ?? "");
        private static string N(long n) => n.ToString(CultureInfo.InvariantCulture);
        public Task<MarketplaceResult<MarketplaceLimitsView>> GetLimits(string userId)
            => Read<MarketplaceLimitsView>(userId, Endpoint + "/limits" + Query(userId), value => true, value => { _configRevision = value.config.revision; _limits = MarketplaceJson.Clone(value); }, value => value.config.revision >= _configRevision);
        public Task<MarketplaceResult<MarketplaceRulesPage>> GetRules(string userId, string after = null, int limit = 50, long? catalogRevision = null)
        {
            if (limit < 1 || limit > 50 || after != null && (!ItemsJson.Key(after) || !catalogRevision.HasValue) || catalogRevision.HasValue && !ItemsJson.Revision(catalogRevision.Value)) return Task.FromResult(Failure<MarketplaceRulesPage>("INVALID_REQUEST"));
            string path = Endpoint + "/rules" + Query(userId) + "&limit=" + N(limit) + (after == null ? "" : "&after=" + Uri.EscapeDataString(after)) + (catalogRevision.HasValue ? "&catalogRevision=" + N(catalogRevision.Value) : "");
            return Read<MarketplaceRulesPage>(userId, path, value =>
            {
                if (value.rules.Length > limit || catalogRevision.HasValue && value.revision != catalogRevision.Value) return false;
                string previous = after;
                foreach (var rule in value.rules) { if (previous != null && string.CompareOrdinal(rule.itemKey, previous) <= 0) return false; previous = rule.itemKey; }
                return value.nextCursor == null || value.rules.Length > 0 && value.nextCursor == previous;
            }, value => { _configRevision = value.revision; _rules = MarketplaceJson.Clone(value); }, value => value.revision >= _configRevision);
        }
        public Task<MarketplaceResult<MarketplaceStateView>> GetMarketplace(string userId)
            => Read<MarketplaceStateView>(userId, Endpoint + Query(userId), value => value.state.userId == userId, value => { _stateRevision = value.state.revision; _state = MarketplaceJson.Clone(value); }, value => value.state.revision >= _stateRevision);
        public Task<MarketplaceResult<MarketplaceListingPage>> QueryListings(string userId, string itemKey = null, long minPrice = 0, long maxPrice = PlayerValuesManager.MaxSafeInteger, string cursor = null, int limit = 50)
        {
            if (limit < 1 || limit > 50 || itemKey != null && !ItemsJson.Key(itemKey) || !ItemsJson.Revision(minPrice) || !ItemsJson.Revision(maxPrice) || minPrice > maxPrice || cursor != null && !MarketplaceJson.Cursor(cursor)) return Task.FromResult(Failure<MarketplaceListingPage>("INVALID_REQUEST"));
            string path = Endpoint + "/listings" + Query(userId) + "&minPrice=" + N(minPrice) + "&maxPrice=" + N(maxPrice) + "&limit=" + N(limit) + (itemKey == null ? "" : "&itemKey=" + Uri.EscapeDataString(itemKey)) + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            return Read<MarketplaceListingPage>(userId, path, value =>
            {
                if (value.listings.Length > limit) return false;
                MarketplaceListing previous = null;
                foreach (var row in value.listings)
                {
                    if (row.status != "active" || itemKey != null && row.itemReference.itemKey != itemKey || row.price < minPrice || row.price > maxPrice || previous != null && (row.price < previous.price || row.price == previous.price && string.CompareOrdinal(row.id, previous.id) <= 0)) return false;
                    previous = row;
                }
                return true;
            }, value => _query = MarketplaceJson.Clone(value));
        }
        public Task<MarketplaceResult<MarketplaceListingView>> GetListing(string userId, string listingId)
        {
            if (!MarketplaceJson.Uuid(listingId)) return Task.FromResult(Failure<MarketplaceListingView>("INVALID_REQUEST"));
            return Read<MarketplaceListingView>(userId, Endpoint + "/listings/" + listingId + Query(userId), value => value.listing.id == listingId, value => _point = MarketplaceJson.Clone(value), value => _point?.listing.id != listingId || value.listing.revision >= _point.listing.revision);
        }
        public Task<MarketplaceResult<MarketplaceOperation>> GetOperation(string userId, string operationId)
        {
            if (!MarketplaceJson.Uuid(operationId)) return Task.FromResult(Failure<MarketplaceOperation>("INVALID_REQUEST"));
            return Read<MarketplaceOperation>(userId, Endpoint + "/operations/" + operationId + Query(userId), value => value.receipt.operationId == operationId);
        }
        public Task<MarketplaceResult<MarketplaceWritten>> CreateListing(string userId, string operationId, string issuedAt, string instanceId, long inventoryRevision, long configRevision, long price, long durationSeconds)
            => Prepare(() => MarketplaceIntent.Create(userId, operationId, issuedAt, instanceId, inventoryRevision, configRevision, price, durationSeconds));
        public Task<MarketplaceResult<MarketplaceWritten>> BuyListing(string userId, string listingId, string operationId, string issuedAt, long listingRevision, long expectedPrice, string currencyKey)
            => Prepare(() => MarketplaceIntent.Buy(userId, listingId, operationId, issuedAt, listingRevision, expectedPrice, currencyKey));
        public Task<MarketplaceResult<MarketplaceWritten>> CancelListing(string userId, string listingId, string operationId, string issuedAt, long listingRevision)
            => Prepare(() => MarketplaceIntent.Cancel(userId, listingId, operationId, issuedAt, listingRevision));
        private Task<MarketplaceResult<MarketplaceWritten>> Prepare(Func<MarketplaceIntent> prepare)
        {
            try { return SubmitPrepared(prepare()); }
            catch (Exception e) when (e is FormatException || e is ArgumentException) { return Task.FromResult(Failure<MarketplaceWritten>("INVALID_REQUEST")); }
        }
        public Task<MarketplaceResult<MarketplaceWritten>> SubmitPrepared(MarketplaceIntent original)
        {
            if (original == null) return Task.FromResult(Failure<MarketplaceWritten>("INVALID_REQUEST"));
            string path = Endpoint + "/listings" + (original.Kind == "create" ? "" : "/" + original.ListingId + "/" + original.Kind);
            return Request(original.UserId, path, new MarketplaceJsonRequest(original.Json), value => original.Matches(value.receipt), (MarketplaceWritten value) => InvalidateTrade());
        }
        public Task<MarketplaceResult<MarketplaceReturn>> RetryReturn(string userId, string listingId)
        {
            if (!MarketplaceJson.Uuid(userId) || !MarketplaceJson.Uuid(listingId)) return Task.FromResult(Failure<MarketplaceReturn>("INVALID_REQUEST"));
            return Request(userId, Endpoint + "/listings/" + listingId + "/retry-return", new MarketplaceJsonRequest("{\"userId\":" + ItemsJson.Quote(userId) + "}"), value => value.listing.id == listingId && value.listing.returnOwnerId == userId, (MarketplaceReturn value) => InvalidateTrade());
        }
        private void InvalidateTrade() { _state = null; _query = null; _point = null; TradingStateInvalidated?.Invoke(); }
        private Task<MarketplaceResult<T>> Read<T>(string user, string path, Func<T, bool> valid, Action<T> accept = null, Func<T, bool> fresh = null) where T : class
            => Request(user, path, null, valid, accept, fresh);
        private async Task<MarketplaceResult<T>> Request<T>(string user, string path, MarketplaceJsonRequest body, Func<T, bool> valid, Action<T> accept, Func<T, bool> fresh = null) where T : class
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
                LastErrorCode = null; return new MarketplaceResult<T> { IsSuccess = true, Data = MarketplaceJson.Clone(response.Data) };
            }
            finally { _requests.Release(); }
        }
        private static string ServerTime<T>(T value) => value is MarketplaceLimitsView limits ? limits.serverTime : value is MarketplaceRulesPage rules ? rules.serverTime : value is MarketplaceStateView state ? state.serverTime : value is MarketplaceListingPage page ? page.serverTime : value is MarketplaceListingView point ? point.serverTime : value is MarketplaceOperation operation ? operation.serverTime : value is MarketplaceWritten written ? written.serverTime : ((MarketplaceReturn)(object)value).serverTime;
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
        private void ResetIdentity() { _epoch++; _network = null; _identity = null; _limits = null; _rules = null; _state = null; _query = null; _point = null; _serverMillis = _stateRevision = _configRevision = -1; _clock.Reset(); }
        private bool Current(string identity, long epoch) { SyncIdentity(); return _identity == identity && _epoch == epoch; }
        private MarketplaceResult<T> Failure<T>(string code, string error = null) where T : class { LastErrorCode = code; return new MarketplaceResult<T> { ErrorCode = code, Error = error ?? code }; }
    }
}
