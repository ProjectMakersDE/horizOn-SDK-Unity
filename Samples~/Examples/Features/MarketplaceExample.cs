using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using PM.horizOn.Cloud.Transport;
using UnityEngine;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Explicit screen actions. One durable original per project/player; restart confirmation uses current GETs only.</summary>
    public sealed class MarketplaceExample : MonoBehaviour
    {
        [SerializeField] private string projectId;
        [SerializeField] private string journalDirectory;
        private sealed class Review
        {
            internal string kind, listing, scope, instance, currency;
            internal long inventoryRevision, configRevision, listingRevision, price, duration;
        }
        private Review _review;
        private int _generation;
        private bool _busy;
        public string LastErrorCode { get; private set; }
        public long ReviewedPrice { get; private set; }
        public long ReviewedFee { get; private set; }
        public long ReviewedSellerNet => ReviewedPrice - ReviewedFee;
        private string User => this && isActiveAndEnabled && UserManager.Instance.CurrentUser?.IsValid() == true && UserManager.Instance.CurrentUser.AccessToken == HorizonApp.Network.GetSessionToken() ? UserManager.Instance.CurrentUser.UserId : null;
        private string Scope => projectId + "|" + User + "|" + HorizonApp.Network.GetApiKey() + "|" + HorizonApp.Network.GetActiveHost() + "|" + HorizonApp.Network.GetSessionToken() + "|" + HorizonApp.Network.ContextGeneration + "|" + _generation;
        private void OnDisable() { _generation++; _review = null; }
        private MarketplaceSampleStore Store(string user) => new MarketplaceSampleStore(string.IsNullOrEmpty(journalDirectory) ? Application.persistentDataPath : journalDirectory, projectId, user);
        private bool Current(string scope, NetworkService network) => User != null && Scope == scope && ReferenceEquals(network, HorizonApp.Network);
        private bool Failed(string code) { LastErrorCode = code; return false; }
        public Task<MarketplaceResult<MarketplaceLimitsView>> ReadLimits() => MarketplaceManager.Instance.GetLimits(User);
        public Task<MarketplaceResult<MarketplaceRulesPage>> ReadRulesPage(string after = null, long? revision = null) => MarketplaceManager.Instance.GetRules(User, after, 50, revision);
        public Task<MarketplaceResult<MarketplaceStateView>> ReadMarketplace() => MarketplaceManager.Instance.GetMarketplace(User);
        public Task<MarketplaceResult<MarketplaceListingPage>> ReadListings(string itemKey = null, long minPrice = 0, long maxPrice = PlayerValuesManager.MaxSafeInteger, string cursor = null) => MarketplaceManager.Instance.QueryListings(User, itemKey, minPrice, maxPrice, cursor);
        public Task<MarketplaceResult<MarketplaceListingView>> ReadListing(string listingId) => MarketplaceManager.Instance.GetListing(User, listingId);
        public Task<ItemsResult<InventorySnapshot>> ReadInventory() => ItemsManager.Instance.GetInventory(User);
        public bool ReviewCreate(string instanceId, long price, long durationSeconds)
        {
            string user = User; var manager = MarketplaceManager.Instance;
            var bounds = manager.CurrentLimits; var page = manager.CurrentRulesPage; var inventory = ItemsManager.Instance.CurrentInventory;
            var item = inventory?.instances.FirstOrDefault(x => x.instanceId == instanceId);
            var rule = page?.rules.FirstOrDefault(x => x.itemKey == item?.itemKey);
            if (user == null || bounds?.config.enabled != true || page?.revision != bounds.config.revision || item == null || item.equipped || rule?.active != true || price < 1 || price < rule.minPrice || price > rule.maxPrice || durationSeconds < bounds.limits.minDurationSeconds || durationSeconds > bounds.limits.maxDurationSeconds || !bounds.config.feeBps.HasValue) return Failed("REVIEW_REQUIRED");
            _review = new Review { kind = "create", listing = "", scope = Scope, instance = instanceId, inventoryRevision = inventory.revision, configRevision = bounds.config.revision, price = price, duration = durationSeconds };
            ReviewedPrice = price; ReviewedFee = Fee(price, bounds.config.feeBps.Value); LastErrorCode = null; return true;
        }
        public bool ReviewBuy(string listingId) => ReviewListing("buy", listingId);
        public bool ReviewCancel(string listingId) => ReviewListing("cancel", listingId);
        private bool ReviewListing(string kind, string listing)
        {
            string user = User; var row = MarketplaceManager.Instance.CurrentListing?.listing;
            if (user == null || row == null || row.id != listing || row.status != "active" || (kind == "cancel") != (row.sellerId == user)) return Failed("REVIEW_REQUIRED");
            _review = new Review { kind = kind, listing = listing, scope = Scope, listingRevision = row.revision, price = row.price, currency = row.currencyKey };
            ReviewedPrice = row.price; ReviewedFee = row.feeAmount; LastErrorCode = null; return true;
        }
        private static long Fee(long price, int basisPoints) => price / 10000 * basisPoints + price % 10000 * basisPoints / 10000;
        public async Task<bool> ConfirmReviewedTrade()
        {
            string user = User;
            if (_busy || user == null || _review == null || _review.scope != Scope) return Failed("REVIEW_REQUIRED");
            long now = MarketplaceManager.Instance.ServerNowMilliseconds; if (now < 0) return Failed("REVIEW_REQUIRED");
            string operation = Guid.NewGuid().ToString("D"), issued = DateTimeOffset.FromUnixTimeMilliseconds(now).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            var terms = _review;
            var original = terms.kind == "create" ? MarketplaceIntent.Create(user, operation, issued, terms.instance, terms.inventoryRevision, terms.configRevision, terms.price, terms.duration)
                : terms.kind == "buy" ? MarketplaceIntent.Buy(user, terms.listing, operation, issued, terms.listingRevision, terms.price, terms.currency)
                : MarketplaceIntent.Cancel(user, terms.listing, operation, issued, terms.listingRevision);
            _busy = true;
            try { using (var lease = Store(user).BeginNew(original)) return await Apply(lease, false); }
            finally { _busy = false; }
        }
        public Task<bool> RetryPending() => Resume(false);
        public Task<bool> RecoverOriginalOperation() => Resume(true);
        private async Task<bool> Resume(bool lookupOnly)
        {
            string user = User; if (_busy || user == null) return Failed("SESSION_REQUIRED");
            _busy = true;
            try { using (var lease = Store(user).AcquireSend()) return await Apply(lease, lookupOnly); }
            finally { _busy = false; }
        }
        private async Task<bool> Apply(MarketplaceSampleStore.SendLease lease, bool lookupOnly)
        {
            string scope = Scope; var network = HorizonApp.Network; var original = lease.Original.Intent; string user = original.UserId;
            if (!lease.Original.Confirmed)
            {
                MarketplaceReceipt receipt;
                if (lookupOnly)
                {
                    var found = await MarketplaceManager.Instance.GetOperation(user, original.OperationId);
                    if (!Current(scope, network)) return Failed("STALE_RESPONSE");
                    if (!found.IsSuccess) return Failed(found.ErrorCode); receipt = found.Data.receipt;
                }
                else
                {
                    var written = await MarketplaceManager.Instance.SubmitPrepared(original);
                    if (!Current(scope, network)) return Failed("STALE_RESPONSE");
                    if (!written.IsSuccess) return Failed(written.ErrorCode); receipt = written.Data.receipt;
                }
                // The matching identity is flushed before the first fallible current-state read. Never archive item/balance results.
                lease.MarkConfirmed(receipt);
            }
            var inventory = await ItemsManager.Instance.GetInventory(user);
            if (!Current(scope, network)) return Failed("STALE_RESPONSE"); if (!inventory.IsSuccess) return Failed(inventory.ErrorCode);
            var values = await PlayerValuesManager.Instance.GetState(user);
            if (!Current(scope, network)) return Failed("STALE_RESPONSE"); if (!values.IsSuccess) return Failed(values.ErrorCode);
            var market = await MarketplaceManager.Instance.GetMarketplace(user);
            if (!Current(scope, network)) return Failed("STALE_RESPONSE"); if (!market.IsSuccess) return Failed(market.ErrorCode);
            lease.AcknowledgeAfterRefresh(); _review = null; LastErrorCode = null; return true;
        }
        public Task<MarketplaceResult<MarketplaceReturn>> RetryReturnDelivery(string listingId) => MarketplaceManager.Instance.RetryReturn(User, listingId);
        public Task<InboxClaimResult> ClaimReturn(string entryId) => InboxManager.Instance.Claim(User, entryId);
    }
}
