using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    [Serializable] public abstract class MarketplaceDto
    {
        internal MarketplaceDto ShallowCopy() => (MarketplaceDto)MemberwiseClone();
    }
    /// <summary>Five current positive Int32 operator quotas; remaining fields describe the wire protocol.</summary>
    [Serializable] public sealed class MarketplaceLimits : MarketplaceDto
    {
        public int openListingsPerPlayer, retainedListingsPerProject, receiptsPerPlayer, historyPerPlayer, projectWritesPerMinute;
        public long playerWriteIntervalMillis, minDurationSeconds, maxDurationSeconds, terminalRetentionSeconds, operationRetentionSeconds;
        public int queryCandidates, pageSize;
    }
    [Serializable] public sealed class MarketplaceConfig : MarketplaceDto { public long revision; public bool enabled; public string currencyKey; public int? feeBps; }
    [Serializable] public sealed class MarketplaceCapabilities : MarketplaceDto { public bool marketplace, realtime; }
    [Serializable] public sealed class MarketplaceLimitsView : MarketplaceDto { public MarketplaceLimits limits; public MarketplaceConfig config; public MarketplaceCapabilities capabilities; public string serverTime; }
    [Serializable] public sealed class MarketplaceRule : MarketplaceDto { public string itemKey; public bool active; public long minPrice, maxPrice; }
    [Serializable] public sealed class MarketplaceRulesPage : MarketplaceDto { public long revision; public MarketplaceRule[] rules = Array.Empty<MarketplaceRule>(); public string nextCursor, serverTime; }
    [Serializable] public sealed class MarketplaceListing : MarketplaceDto
    {
        public string id, sellerId, buyerId, returnOwnerId, currencyKey, status, createdAt, expiresAt, terminalAt, returnEntryId;
        public long revision, price, feeAmount, sellerNet;
        public int feeBps;
        public ItemInstance item;
        public ItemReference itemReference;
        public bool ownerUnavailable;
    }
    [Serializable] public sealed class MarketplaceWindow : MarketplaceDto { public string serverTime, acceptAfter; }
    [Serializable] public sealed class MarketplaceModeration : MarketplaceDto { public string banUntil, reason; }
    [Serializable] public sealed class MarketplaceState : MarketplaceDto
    {
        public string userId;
        public long revision;
        public MarketplaceListing[] openListings = Array.Empty<MarketplaceListing>(), history = Array.Empty<MarketplaceListing>();
        public MarketplaceWindow operationWindow;
        public MarketplaceModeration moderation;
        public MarketplaceLimits limits;
    }
    [Serializable] public sealed class MarketplaceStateView : MarketplaceDto { public MarketplaceState state; public string serverTime; }
    [Serializable] public sealed class MarketplaceListingPage : MarketplaceDto { public MarketplaceListing[] listings = Array.Empty<MarketplaceListing>(); public string nextCursor, serverTime; }
    [Serializable] public sealed class MarketplaceListingView : MarketplaceDto { public MarketplaceListing listing; public string serverTime; }
    /// <summary>Reconstructed listing-owned receipt, without inventory or balance snapshots.</summary>
    [Serializable] public sealed class MarketplaceReceipt : MarketplaceDto
    {
        public string operationId, issuedAt, kind, listingId, recordedAt, currencyKey;
        public long listingRevision, price, feeAmount, sellerNet;
        public ItemReference itemReference;
    }
    [Serializable] public sealed class MarketplaceWritten : MarketplaceDto { public MarketplaceReceipt receipt; public MarketplaceListing listing; public bool replayed; public string serverTime; }
    [Serializable] public sealed class MarketplaceOperation : MarketplaceDto { public MarketplaceReceipt receipt; public string serverTime; }
    [Serializable] public sealed class MarketplaceReturn : MarketplaceDto { public MarketplaceListing listing; public string delivery, serverTime; }
    public sealed class MarketplaceResult<T> where T : class
    {
        public bool IsSuccess { get; internal set; }
        public T Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }
    internal sealed class MarketplaceJsonRequest
    {
        internal string Json { get; }
        internal MarketplaceJsonRequest(string json) { Json = json; }
    }
}
