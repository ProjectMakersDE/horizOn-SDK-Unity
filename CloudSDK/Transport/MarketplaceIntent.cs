using System;
using System.Globalization;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Transport
{
    /// <summary>Immutable original HTTP tuple. Persist before sending; restoring never refreshes any field.</summary>
    public sealed class MarketplaceIntent
    {
        public string Kind { get; }
        public string ListingId { get; }
        public string Json { get; }
        public string UserId { get; }
        public string OperationId { get; }
        public string IssuedAt { get; }
        private MarketplaceIntent(string kind, string listing, string json)
        {
            MarketplaceJson.ValidateIntent(kind, listing, json);
            var fields = JsonFragments.Object(json);
            Kind = kind; ListingId = listing; Json = json;
            UserId = JsonFragments.String(fields["userId"]); OperationId = JsonFragments.String(fields["operationId"]); IssuedAt = JsonFragments.String(fields["issuedAt"]);
        }
        public static MarketplaceIntent Restore(string kind, string listingId, string originalJson) => new MarketplaceIntent(kind, listingId ?? "", originalJson);
        private static string Head(string user, string operation, string issued) => "{\"userId\":" + ItemsJson.Quote(user) + ",\"operationId\":" + ItemsJson.Quote(operation) + ",\"issuedAt\":" + ItemsJson.Quote(issued);
        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        public static MarketplaceIntent Create(string userId, string operationId, string issuedAt, string instanceId, long expectedInventoryRevision, long expectedConfigRevision, long price, long durationSeconds)
            => Restore("create", "", Head(userId, operationId, issuedAt) + ",\"instanceId\":" + ItemsJson.Quote(instanceId) + ",\"expectedInventoryRevision\":" + Number(expectedInventoryRevision) + ",\"expectedConfigRevision\":" + Number(expectedConfigRevision) + ",\"price\":" + Number(price) + ",\"durationSeconds\":" + Number(durationSeconds) + "}");
        public static MarketplaceIntent Buy(string userId, string listingId, string operationId, string issuedAt, long expectedListingRevision, long expectedPrice, string expectedCurrencyKey)
            => Restore("buy", listingId, Head(userId, operationId, issuedAt) + ",\"expectedListingRevision\":" + Number(expectedListingRevision) + ",\"expectedPrice\":" + Number(expectedPrice) + ",\"expectedCurrencyKey\":" + ItemsJson.Quote(expectedCurrencyKey) + "}");
        public static MarketplaceIntent Cancel(string userId, string listingId, string operationId, string issuedAt, long expectedListingRevision)
            => Restore("cancel", listingId, Head(userId, operationId, issuedAt) + ",\"expectedListingRevision\":" + Number(expectedListingRevision) + "}");
        public bool Matches(MarketplaceReceipt receipt) => MarketplaceJson.Matches(this, receipt);
    }
}
