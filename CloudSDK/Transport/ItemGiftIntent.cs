using System.Globalization;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Transport
{
    /// <summary>Immutable original send body with all seven fields. Persist before sending; restoring never refreshes any field.</summary>
    public sealed class ItemGiftIntent
    {
        public string Json { get; }
        public string UserId { get; }
        public string OperationId { get; }
        public string IssuedAt { get; }
        public string RecipientId { get; }
        public string InstanceId { get; }
        public long ExpectedInventoryRevision { get; }
        public long ExpectedConfigRevision { get; }
        private ItemGiftIntent(string json)
        {
            ItemGiftsJson.ValidateIntent(json);
            var fields = JsonFragments.Object(json);
            Json = json;
            UserId = JsonFragments.String(fields["userId"]); OperationId = JsonFragments.String(fields["operationId"]); IssuedAt = JsonFragments.String(fields["issuedAt"]);
            RecipientId = JsonFragments.String(fields["recipientId"]); InstanceId = JsonFragments.String(fields["instanceId"]);
            ExpectedInventoryRevision = ItemsJson.Integer(fields["expectedInventoryRevision"]); ExpectedConfigRevision = ItemsJson.Integer(fields["expectedConfigRevision"]);
        }
        public static ItemGiftIntent Restore(string originalJson) => new ItemGiftIntent(originalJson);
        public static ItemGiftIntent Send(string userId, string operationId, string issuedAt, string recipientId, string instanceId, long expectedInventoryRevision, long expectedConfigRevision)
            => Restore("{\"userId\":" + ItemsJson.Quote(userId) + ",\"operationId\":" + ItemsJson.Quote(operationId) + ",\"issuedAt\":" + ItemsJson.Quote(issuedAt)
                + ",\"recipientId\":" + ItemsJson.Quote(recipientId) + ",\"instanceId\":" + ItemsJson.Quote(instanceId)
                + ",\"expectedInventoryRevision\":" + expectedInventoryRevision.ToString(CultureInfo.InvariantCulture)
                + ",\"expectedConfigRevision\":" + expectedConfigRevision.ToString(CultureInfo.InvariantCulture) + "}");
        public bool Matches(ItemGiftReceipt receipt) => ItemGiftsJson.Matches(this, receipt);
    }
}
