using System;
using System.Collections.Generic;
using System.Linq;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Transport;

namespace PM.horizOn.Cloud.Helper
{
    /// <summary>Exact item gift wire tokens. Retained lists are independent of configured quotas.</summary>
    internal static class ItemGiftsJson
    {
        internal const string IntentFields = "userId,operationId,issuedAt,recipientId,instanceId,expectedInventoryRevision,expectedConfigRevision";
        private static readonly string[] Statuses = { "offered", "claimed", "return_pending", "return_delivered", "returned" };
        private static readonly Type[] Responses = { typeof(ItemGiftConfigView), typeof(ItemGiftStateView), typeof(ItemGiftView), typeof(ItemGiftWritten), typeof(ItemGiftOperation), typeof(ItemGiftDeclined) };
        internal static bool IsResponse<T>() => Array.IndexOf(Responses, typeof(T)) >= 0;
        private static Dictionary<string, string> Object(string raw) => JsonFragments.Object(raw, int.MaxValue);
        private static string Text(string raw) => JsonFragments.String(raw);
        private static string Id(string raw) { string value = Text(raw); if (!MarketplaceJson.Uuid(value)) throw new FormatException("Item gift UUID must be canonical"); return value; }
        private static long Number(string raw, long minimum = 0) { long n = ItemsJson.Integer(raw); if (n < minimum) throw new FormatException("Item gift integer invalid"); return n; }
        private static int Quota(string raw) { long n = Number(raw, 1); if (n > int.MaxValue) throw new FormatException("Item gift quota bound"); return (int)n; }
        private static bool Boolean(string raw) => raw == "true" ? true : raw == "false" ? false : throw new FormatException("Item gift boolean invalid");
        private static string Choice(string raw, params string[] options) { string value = Text(raw); if (!options.Contains(value)) throw new FormatException("Item gift state invalid"); return value; }
        private static string Instant(string raw) { string value = Text(raw); MarketplaceJson.Millis(value); return value; }
        private static string Nullable(string raw, Func<string, string> parse) => raw == "null" ? null : parse(raw);
        private static string Currency(string raw) { string value = Text(raw); if (!MarketplaceJson.Currency(value)) throw new FormatException("Item gift currency invalid"); return value; }
        private static string Entry(string raw) { string value = Text(raw); if (!value.StartsWith("message:", StringComparison.Ordinal) || !MarketplaceJson.Uuid(value.Substring(8))) throw new FormatException("Item gift inbox entry invalid"); return value; }
        private static void Exact(Dictionary<string, string> fields, string names)
        {
            var expected = names.Split(',');
            if (fields.Count != expected.Length || expected.Any(x => !fields.ContainsKey(x))) throw new FormatException("Item gift fields invalid");
        }
        private static ItemReference Reference(string raw)
        {
            var f = Object(raw); string key = Text(f["itemKey"]);
            if (!ItemsJson.Key(key)) throw new FormatException("Item gift item key invalid");
            return new ItemReference { instanceId = Id(f["instanceId"]), itemKey = key };
        }
        private static void Fee(string currency, long fee) { if ((fee == 0) != (currency == null)) throw new FormatException("Item gift fee and currency must match"); }
        private static ItemGiftLimits Limits(string raw)
        {
            var f = Object(raw);
            return new ItemGiftLimits { openGiftsPerPlayer = Quota(f["openGiftsPerPlayer"]), retainedGiftsPerProject = Quota(f["retainedGiftsPerProject"]), projectWritesPerMinute = Quota(f["projectWritesPerMinute"]), historyPerPlayer = Quota(f["historyPerPlayer"]), receivedPageSize = Quota(f["receivedPageSize"]), playerWriteIntervalMillis = Number(f["playerWriteIntervalMillis"]), expirySeconds = Number(f["expirySeconds"], 1), terminalRetentionSeconds = Number(f["terminalRetentionSeconds"], 1), operationRetentionSeconds = Number(f["operationRetentionSeconds"], 1) };
        }
        private static ItemGiftConfig Config(string raw)
        {
            var f = Object(raw);
            var value = new ItemGiftConfig { revision = Number(f["revision"]), enabled = Boolean(f["enabled"]), currencyKey = Nullable(f["currencyKey"], Currency), fee = Number(f["fee"]) };
            if (value.fee > 0 && value.currencyKey == null) throw new FormatException("Item gift fee without currency"); return value;
        }
        private static ItemGift Gift(string raw)
        {
            var f = Object(raw);
            var value = new ItemGift { id = Id(f["id"]), revision = Number(f["revision"], 1), senderId = Id(f["senderId"]), recipientId = Id(f["recipientId"]), returnOwnerId = Id(f["returnOwnerId"]), itemReference = Reference(f["itemReference"]), currencyKey = Nullable(f["currencyKey"], Currency), fee = Number(f["fee"]), configRevision = Number(f["configRevision"]), status = Choice(f["status"], Statuses), createdAt = Instant(f["createdAt"]), expiresAt = Instant(f["expiresAt"]), terminalAt = Nullable(f["terminalAt"], Instant), declinedAt = Nullable(f["declinedAt"], Instant), offerEntryId = Nullable(f["offerEntryId"], Entry), returnEntryId = Nullable(f["returnEntryId"], Entry), ownerUnavailable = Boolean(f["ownerUnavailable"]) };
            Fee(value.currencyKey, value.fee);
            bool terminal = value.status == "claimed" || value.status == "returned";
            if (value.senderId == value.recipientId || terminal != (value.terminalAt != null) || MarketplaceJson.Millis(value.expiresAt) < MarketplaceJson.Millis(value.createdAt)
                || value.declinedAt != null && (value.status == "offered" || value.status == "claimed")) throw new FormatException("Item gift lifecycle invalid");
            return value;
        }
        private static ItemGift[] Rows(string raw)
        {
            var input = JsonFragments.Array(raw, int.MaxValue); var seen = new HashSet<string>(StringComparer.Ordinal); var values = new ItemGift[input.Count];
            for (int i = 0; i < input.Count; i++) { values[i] = Gift(input[i]); if (!seen.Add(values[i].id)) throw new FormatException("Item gift duplicate identity"); }
            return values;
        }
        private static ItemGiftReceipt Receipt(string raw)
        {
            var f = Object(raw);
            var value = new ItemGiftReceipt { operationId = Id(f["operationId"]), issuedAt = Instant(f["issuedAt"]), giftId = Id(f["giftId"]), giftRevision = Number(f["giftRevision"], 1), recordedAt = Instant(f["recordedAt"]), recipientId = Id(f["recipientId"]), itemReference = Reference(f["itemReference"]), currencyKey = Nullable(f["currencyKey"], Currency), fee = Number(f["fee"]), configRevision = Number(f["configRevision"]) };
            Fee(value.currencyKey, value.fee); return value;
        }
        private static ItemGiftState State(string raw)
        {
            var f = Object(raw); var w = Object(f["operationWindow"]);
            var value = new ItemGiftState { userId = Id(f["userId"]), revision = Number(f["revision"]), sent = Rows(f["sent"]), history = Rows(f["history"]), received = Rows(f["received"]), operationWindow = new ItemGiftWindow { serverTime = Instant(w["serverTime"]), acceptAfter = Instant(w["acceptAfter"]) }, limits = Limits(f["limits"]) };
            var seen = new HashSet<string>(StringComparer.Ordinal); string user = value.userId;
            if (value.sent.Any(x => x.status == "claimed" || x.status == "returned" || x.returnOwnerId != user || !seen.Add(x.id))
                || value.history.Any(x => x.status != "claimed" && x.status != "returned" || x.senderId != user && x.recipientId != user && x.returnOwnerId != user || !seen.Add(x.id))
                || value.received.Any(x => x.status != "offered" || x.recipientId != user)) throw new FormatException("Item gift state ownership or lifecycle invalid");
            return value;
        }
        private static bool Same(ItemGift gift, ItemGiftReceipt receipt) => gift.id == receipt.giftId && gift.revision >= receipt.giftRevision && gift.recipientId == receipt.recipientId
            && gift.itemReference.instanceId == receipt.itemReference.instanceId && gift.itemReference.itemKey == receipt.itemReference.itemKey && gift.currencyKey == receipt.currencyKey && gift.fee == receipt.fee && gift.configRevision == receipt.configRevision;
        internal static T Read<T>(string raw) where T : class
        {
            var f = Object(raw); string server = Instant(f["serverTime"]); object result;
            if (typeof(T) == typeof(ItemGiftConfigView)) result = new ItemGiftConfigView { config = Config(f["config"]), limits = Limits(f["limits"]), serverTime = server };
            else if (typeof(T) == typeof(ItemGiftStateView)) result = new ItemGiftStateView { state = State(f["state"]), serverTime = server };
            else if (typeof(T) == typeof(ItemGiftView)) result = new ItemGiftView { gift = Gift(f["gift"]), serverTime = server };
            else if (typeof(T) == typeof(ItemGiftOperation)) result = new ItemGiftOperation { receipt = Receipt(f["receipt"]), serverTime = server };
            else if (typeof(T) == typeof(ItemGiftDeclined))
            {
                var value = new ItemGiftDeclined { gift = Gift(f["gift"]), replayed = Boolean(f["replayed"]), returnDelivery = Choice(f["returnDelivery"], "pending", "delivered", "returned", "claimed"), serverTime = server };
                if (value.gift.declinedAt == null) throw new FormatException("Declined item gift has no decline time");
                result = value;
            }
            else if (typeof(T) == typeof(ItemGiftWritten))
            {
                var value = new ItemGiftWritten { receipt = Receipt(f["receipt"]), gift = f["gift"] == "null" ? null : Gift(f["gift"]), replayed = Boolean(f["replayed"]), serverTime = server };
                if (value.gift != null && !Same(value.gift, value.receipt) || !value.replayed && (value.gift == null || value.gift.revision != value.receipt.giftRevision || value.gift.status != "offered"))
                    throw new FormatException("Item gift does not match its original receipt");
                result = value;
            }
            else throw new ArgumentException("Unsupported item gift response type");
            return (T)result;
        }
        internal static void ValidateIntent(string raw)
        {
            var f = Object(raw); Exact(f, IntentFields);
            if (Id(f["userId"]) == Id(f["recipientId"])) throw new FormatException("Item gift recipient must be another player");
            Id(f["operationId"]); Instant(f["issuedAt"]); Id(f["instanceId"]); Number(f["expectedInventoryRevision"]); Number(f["expectedConfigRevision"]);
        }
        internal static bool Matches(ItemGiftIntent original, ItemGiftReceipt receipt)
            => original != null && receipt != null && receipt.itemReference != null && MarketplaceJson.Uuid(receipt.giftId) && receipt.operationId == original.OperationId && receipt.issuedAt == original.IssuedAt
                && receipt.recipientId == original.RecipientId && receipt.itemReference.instanceId == original.InstanceId && receipt.configRevision == original.ExpectedConfigRevision;
        private static ItemGift[] Copy(ItemGift[] values) => Array.ConvertAll(values, Clone);
        private static ItemReference RefCopy(ItemReference x) => x == null ? null : new ItemReference { instanceId = x.instanceId, itemKey = x.itemKey };
        internal static T Clone<T>(T value) where T : class
        {
            if (value == null) return null;
            var copy = ((ItemGiftsDto)(object)value).ShallowCopy();
            if (copy is ItemGiftConfigView config) { config.config = Clone(config.config); config.limits = Clone(config.limits); }
            else if (copy is ItemGift gift) gift.itemReference = RefCopy(gift.itemReference);
            else if (copy is ItemGiftReceipt receipt) receipt.itemReference = RefCopy(receipt.itemReference);
            else if (copy is ItemGiftState state) { state.sent = Copy(state.sent); state.history = Copy(state.history); state.received = Copy(state.received); state.operationWindow = Clone(state.operationWindow); state.limits = Clone(state.limits); }
            else if (copy is ItemGiftStateView view) view.state = Clone(view.state);
            else if (copy is ItemGiftView point) point.gift = Clone(point.gift);
            else if (copy is ItemGiftWritten written) { written.receipt = Clone(written.receipt); written.gift = Clone(written.gift); }
            else if (copy is ItemGiftOperation operation) operation.receipt = Clone(operation.receipt);
            else if (copy is ItemGiftDeclined declined) declined.gift = Clone(declined.gift);
            return (T)(object)copy;
        }
    }
}
