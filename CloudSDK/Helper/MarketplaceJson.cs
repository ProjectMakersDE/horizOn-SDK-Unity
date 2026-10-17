using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Transport;

namespace PM.horizOn.Cloud.Helper
{
    /// <summary>Exact wire tokens. Current retained lists are independent of configured quotas.</summary>
    internal static class MarketplaceJson
    {
        private static readonly Regex CurrencyPattern = new Regex(@"\A[a-z0-9][a-z0-9._-]{0,23}\z");
        private static readonly Regex CursorPattern = new Regex(@"\A[A-Za-z0-9_-]{1,512}\z");
        private static readonly Type[] Responses = { typeof(MarketplaceLimitsView), typeof(MarketplaceRulesPage), typeof(MarketplaceStateView), typeof(MarketplaceListingPage), typeof(MarketplaceListingView), typeof(MarketplaceWritten), typeof(MarketplaceOperation), typeof(MarketplaceReturn) };
        internal static bool IsResponse<T>() => Array.IndexOf(Responses, typeof(T)) >= 0;
        // Keep the shared8MiB/depth24 safety frame, without its default2048 collection-element cap.
        private static Dictionary<string, string> Object(string raw) => JsonFragments.Object(raw, int.MaxValue);
        private static string Text(string raw) => JsonFragments.String(raw);
        internal static bool Uuid(string value) => value != null && Guid.TryParseExact(value, "D", out var parsed) && parsed.ToString("D") == value;
        private static string Id(string raw) { string value = Text(raw); if (!Uuid(value)) throw new FormatException("Marketplace UUID must be canonical"); return value; }
        internal static bool Currency(string value) => value != null && CurrencyPattern.IsMatch(value);
        private static string MoneyKey(string raw) { string value = Text(raw); if (!Currency(value)) throw new FormatException("Marketplace currency invalid"); return value; }
        internal static bool Cursor(string value) => value != null && CursorPattern.IsMatch(value);
        private static long Number(string raw, long minimum = 0) { long n = ItemsJson.Integer(raw); if (n < minimum) throw new FormatException("Marketplace integer invalid"); return n; }
        private static int Bound(string raw, int min, int max) { long n = Number(raw, min); if (n > max) throw new FormatException("Marketplace integer bound"); return (int)n; }
        private static bool Boolean(string raw) => raw == "true" ? true : raw == "false" ? false : throw new FormatException("Marketplace boolean invalid");
        private static string Choice(string raw, params string[] options) { string value = Text(raw); if (!options.Contains(value)) throw new FormatException("Marketplace state invalid"); return value; }
        internal static long Millis(string value)
        {
            if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant)) throw new FormatException("Marketplace UTC milliseconds required");
            long n = instant.ToUnixTimeMilliseconds(); if (n < 0 || n > 9007199254740991L) throw new FormatException("Marketplace time invalid"); return n;
        }
        private static string Instant(string raw) { string value = Text(raw); Millis(value); return value; }
        internal static string Timestamp(long millis) => DateTimeOffset.FromUnixTimeMilliseconds(millis).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        private static string Nullable(string raw, Func<string, string> parse) => raw == "null" ? null : parse(raw);
        private static string Entry(string raw) { string value = Text(raw); if (!value.StartsWith("message:", StringComparison.Ordinal) || !Uuid(value.Substring(8))) throw new FormatException("Marketplace return entry invalid"); return value; }
        private static T[] Rows<T>(string raw, int maximum, Func<string, T> parse, Func<T, string> identity)
        {
            var input = JsonFragments.Array(raw, int.MaxValue);
            if (input.Count > maximum) throw new FormatException("Marketplace page size invalid");
            var seen = new HashSet<string>(StringComparer.Ordinal); var values = new T[input.Count];
            for (int i = 0; i < input.Count; i++) { values[i] = parse(input[i]); if (!seen.Add(identity(values[i]))) throw new FormatException("Marketplace duplicate identity"); }
            return values;
        }
        private static MarketplaceLimits Limits(string raw)
        {
            var f = Object(raw);
            var value = new MarketplaceLimits { openListingsPerPlayer = Bound(f["openListingsPerPlayer"], 1, int.MaxValue), retainedListingsPerProject = Bound(f["retainedListingsPerProject"], 1, int.MaxValue), receiptsPerPlayer = Bound(f["receiptsPerPlayer"], 1, int.MaxValue), historyPerPlayer = Bound(f["historyPerPlayer"], 1, int.MaxValue), projectWritesPerMinute = Bound(f["projectWritesPerMinute"], 1, int.MaxValue), playerWriteIntervalMillis = Number(f["playerWriteIntervalMillis"], 1), minDurationSeconds = Number(f["minDurationSeconds"], 1), maxDurationSeconds = Number(f["maxDurationSeconds"], 1), terminalRetentionSeconds = Number(f["terminalRetentionSeconds"], 1), operationRetentionSeconds = Number(f["operationRetentionSeconds"], 1), queryCandidates = Bound(f["queryCandidates"], 1, int.MaxValue), pageSize = Bound(f["pageSize"], 1, int.MaxValue) };
            if (value.minDurationSeconds > value.maxDurationSeconds) throw new FormatException("Marketplace duration bounds invalid"); return value;
        }
        private static MarketplaceConfig Config(string raw)
        {
            var f = Object(raw); var value = new MarketplaceConfig { revision = Number(f["revision"]), enabled = Boolean(f["enabled"]), currencyKey = Nullable(f["currencyKey"], MoneyKey), feeBps = f["feeBps"] == "null" ? (int?)null : Bound(f["feeBps"], 0, 10000) };
            if (value.enabled && (value.currencyKey == null || !value.feeBps.HasValue)) throw new FormatException("Marketplace enabled config missing"); return value;
        }
        private static ItemReference Reference(string raw) { var f = Object(raw); string key = Text(f["itemKey"]); if (!ItemsJson.Key(key)) throw new FormatException("Marketplace item key invalid"); return new ItemReference { instanceId = Id(f["instanceId"]), itemKey = key }; }
        private static ItemInstance OriginalItem(string raw)
        {
            var f = Object(raw); var reference = Reference(raw);
            if (Boolean(f["equipped"])) throw new FormatException("Equipped Marketplace escrow invalid");
            return new ItemInstance { instanceId = reference.instanceId, itemKey = reference.itemKey, properties = ItemsJson.Properties(f["properties"]), equipped = false, createdAt = ItemsJson.Instant(f["createdAt"]) };
        }
        internal static long Fee(long price, int bps) => price / 10000 * bps + price % 10000 * bps / 10000;
        private static MarketplaceListing Listing(string raw)
        {
            var f = Object(raw);
            var value = new MarketplaceListing { id = Id(f["id"]), revision = Number(f["revision"], 1), sellerId = Id(f["sellerId"]), buyerId = Nullable(f["buyerId"], Id), returnOwnerId = Id(f["returnOwnerId"]), item = f["item"] == "null" ? null : OriginalItem(f["item"]), itemReference = Reference(f["itemReference"]), currencyKey = MoneyKey(f["currencyKey"]), price = Number(f["price"], 1), feeBps = Bound(f["feeBps"], 0, 10000), feeAmount = Number(f["feeAmount"]), sellerNet = Number(f["sellerNet"]), status = Choice(f["status"], "active", "return_pending", "return_delivered", "sold", "returned"), createdAt = Instant(f["createdAt"]), expiresAt = Instant(f["expiresAt"]), terminalAt = Nullable(f["terminalAt"], Instant), returnEntryId = Nullable(f["returnEntryId"], Entry), ownerUnavailable = Boolean(f["ownerUnavailable"]) };
            bool terminal = value.status == "sold" || value.status == "returned";
            if (value.feeAmount != Fee(value.price, value.feeBps) || value.sellerNet != value.price - value.feeAmount || Millis(value.expiresAt) < Millis(value.createdAt)
                || terminal != (value.item == null) || terminal != (value.terminalAt != null) || (value.status == "sold") != (value.buyerId != null)
                || value.item != null && (value.item.instanceId != value.itemReference.instanceId || value.item.itemKey != value.itemReference.itemKey)) throw new FormatException("Marketplace listing lifecycle or immutable fields invalid");
            return value;
        }
        private static MarketplaceReceipt Receipt(string raw)
        {
            var f = Object(raw); var value = new MarketplaceReceipt { operationId = Id(f["operationId"]), issuedAt = Instant(f["issuedAt"]), kind = Choice(f["kind"], "create", "buy", "cancel"), listingId = Id(f["listingId"]), listingRevision = Number(f["listingRevision"], 1), recordedAt = Instant(f["recordedAt"]), itemReference = Reference(f["itemReference"]), currencyKey = MoneyKey(f["currencyKey"]), price = Number(f["price"], 1), feeAmount = Number(f["feeAmount"]), sellerNet = Number(f["sellerNet"]) };
            if (value.feeAmount > value.price || value.sellerNet != value.price - value.feeAmount) throw new FormatException("Marketplace receipt financial fields invalid"); return value;
        }
        private static MarketplaceState State(string raw)
        {
            var f = Object(raw); var w = Object(f["operationWindow"]); var m = Object(f["moderation"]);
            var value = new MarketplaceState { userId = Id(f["userId"]), revision = Number(f["revision"]), openListings = Rows(f["openListings"], int.MaxValue, Listing, x => x.id), history = Rows(f["history"], int.MaxValue, Listing, x => x.id), operationWindow = new MarketplaceWindow { serverTime = Instant(w["serverTime"]), acceptAfter = Instant(w["acceptAfter"]) }, moderation = new MarketplaceModeration { banUntil = Nullable(m["banUntil"], Instant), reason = Text(m["reason"]) }, limits = Limits(f["limits"]) };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (value.openListings.Any(x => x.status == "sold" || x.status == "returned" || x.returnOwnerId != value.userId || !seen.Add(x.id))
                || value.history.Any(x => x.status != "sold" && x.status != "returned" || x.sellerId != value.userId && x.buyerId != value.userId && x.returnOwnerId != value.userId || !seen.Add(x.id))) throw new FormatException("Marketplace state ownership/lifecycle invalid");
            return value;
        }
        internal static T Read<T>(string raw) where T : class
        {
            var f = Object(raw); string server = Instant(f["serverTime"]); object result;
            if (typeof(T) == typeof(MarketplaceLimitsView)) { var c = Object(f["capabilities"]); if (!Boolean(c["marketplace"])) throw new FormatException("Marketplace capability missing"); result = new MarketplaceLimitsView { limits = Limits(f["limits"]), config = Config(f["config"]), capabilities = new MarketplaceCapabilities { marketplace = true, realtime = Boolean(c["realtime"]) }, serverTime = server }; }
            else if (typeof(T) == typeof(MarketplaceRulesPage))
            {
                var rules = Rows(f["rules"], 50, value => { var r = Object(value); string key = Text(r["itemKey"]); var row = new MarketplaceRule { itemKey = key, active = Boolean(r["active"]), minPrice = Number(r["minPrice"], 1), maxPrice = Number(r["maxPrice"], 1) }; if (!ItemsJson.Key(key) || row.minPrice > row.maxPrice) throw new FormatException("Marketplace rule invalid"); return row; }, x => x.itemKey);
                result = new MarketplaceRulesPage { revision = Number(f["revision"]), rules = rules, nextCursor = Nullable(f["nextCursor"], value => { string key = Text(value); if (!ItemsJson.Key(key)) throw new FormatException("Marketplace rule cursor invalid"); return key; }), serverTime = server };
            }
            else if (typeof(T) == typeof(MarketplaceStateView)) result = new MarketplaceStateView { state = State(f["state"]), serverTime = server };
            else if (typeof(T) == typeof(MarketplaceListingPage)) result = new MarketplaceListingPage { listings = Rows(f["listings"], 50, Listing, x => x.id), nextCursor = Nullable(f["nextCursor"], value => { string key = Text(value); if (!Cursor(key)) throw new FormatException("Marketplace cursor invalid"); return key; }), serverTime = server };
            else if (typeof(T) == typeof(MarketplaceListingView)) result = new MarketplaceListingView { listing = Listing(f["listing"]), serverTime = server };
            else if (typeof(T) == typeof(MarketplaceOperation)) result = new MarketplaceOperation { receipt = Receipt(f["receipt"]), serverTime = server };
            else if (typeof(T) == typeof(MarketplaceReturn)) result = new MarketplaceReturn { listing = Listing(f["listing"]), delivery = Choice(f["delivery"], "pending", "delivered", "returned"), serverTime = server };
            else if (typeof(T) == typeof(MarketplaceWritten))
            {
                var value = new MarketplaceWritten { receipt = Receipt(f["receipt"]), listing = f["listing"] == "null" ? null : Listing(f["listing"]), replayed = Boolean(f["replayed"]), serverTime = server };
                var row = value.listing; var record = value.receipt;
                if (row != null && (row.id != record.listingId || row.revision < record.listingRevision || row.itemReference.instanceId != record.itemReference.instanceId || row.itemReference.itemKey != record.itemReference.itemKey || row.currencyKey != record.currencyKey || row.price != record.price || row.feeAmount != record.feeAmount || row.sellerNet != record.sellerNet)
                    || !value.replayed && (row == null || row.revision != record.listingRevision || row.status != (record.kind == "create" ? "active" : record.kind == "buy" ? "sold" : "return_pending"))) throw new FormatException("Marketplace current listing does not match original receipt");
                result = value;
            }
            else throw new ArgumentException("Unsupported Marketplace response type");
            return (T)result;
        }
        internal static void ValidateIntent(string kind, string listing, string raw)
        {
            var f = Object(raw);
            string fields = kind == "create" ? "userId,operationId,issuedAt,instanceId,expectedInventoryRevision,expectedConfigRevision,price,durationSeconds" : kind == "buy" ? "userId,operationId,issuedAt,expectedListingRevision,expectedPrice,expectedCurrencyKey" : kind == "cancel" ? "userId,operationId,issuedAt,expectedListingRevision" : throw new FormatException("Marketplace action invalid");
            var expected = fields.Split(','); if (f.Count != expected.Length || expected.Any(x => !f.ContainsKey(x))) throw new FormatException("Marketplace original fields invalid");
            Id(f["userId"]); Id(f["operationId"]); Instant(f["issuedAt"]);
            if (kind == "create") { if (!string.IsNullOrEmpty(listing)) throw new FormatException("Create has no original listing ID"); Id(f["instanceId"]); Number(f["expectedInventoryRevision"]); Number(f["expectedConfigRevision"]); Number(f["price"], 1); long duration = Number(f["durationSeconds"], 300); if (duration > 604800) throw new FormatException("Marketplace duration invalid"); }
            else { if (!Uuid(listing)) throw new FormatException("Marketplace original listing ID missing"); Number(f["expectedListingRevision"]); if (kind == "buy") { Number(f["expectedPrice"], 1); MoneyKey(f["expectedCurrencyKey"]); } }
        }
        internal static bool Matches(MarketplaceIntent original, MarketplaceReceipt receipt)
        {
            if (original == null || receipt == null || receipt.itemReference == null || !Uuid(receipt.listingId) || receipt.operationId != original.OperationId || receipt.issuedAt != original.IssuedAt || receipt.kind != original.Kind) return false;
            var f = Object(original.Json);
            if (original.Kind == "create") return receipt.listingRevision == 1 && receipt.itemReference.instanceId == Text(f["instanceId"]) && receipt.price == Number(f["price"]);
            if (receipt.listingId != original.ListingId || receipt.listingRevision != Number(f["expectedListingRevision"]) + 1) return false;
            return original.Kind != "buy" || receipt.price == Number(f["expectedPrice"]) && receipt.currencyKey == Text(f["expectedCurrencyKey"]);
        }
        private static T[] Copy<T>(T[] values) where T : class => Array.ConvertAll(values, Clone);
        private static ItemReference RefCopy(ItemReference x) => x == null ? null : new ItemReference { instanceId = x.instanceId, itemKey = x.itemKey };
        internal static T Clone<T>(T value) where T : class
        {
            if (value == null) return null;
            var copy = ((MarketplaceDto)(object)value).ShallowCopy();
            if (copy is MarketplaceLimitsView limits) { limits.limits = Clone(limits.limits); limits.config = Clone(limits.config); limits.capabilities = Clone(limits.capabilities); }
            else if (copy is MarketplaceRulesPage rules) rules.rules = Copy(rules.rules);
            else if (copy is MarketplaceListing listing) { listing.item = ItemsJson.Clone(listing.item); listing.itemReference = RefCopy(listing.itemReference); }
            else if (copy is MarketplaceReceipt receipt) receipt.itemReference = RefCopy(receipt.itemReference);
            else if (copy is MarketplaceState state) { state.openListings = Copy(state.openListings); state.history = Copy(state.history); state.limits = Clone(state.limits); state.operationWindow = Clone(state.operationWindow); state.moderation = Clone(state.moderation); }
            else if (copy is MarketplaceStateView view) view.state = Clone(view.state);
            else if (copy is MarketplaceListingPage page) page.listings = Copy(page.listings);
            else if (copy is MarketplaceListingView point) point.listing = Clone(point.listing);
            else if (copy is MarketplaceWritten written) { written.listing = Clone(written.listing); written.receipt = Clone(written.receipt); }
            else if (copy is MarketplaceOperation operation) operation.receipt = Clone(operation.receipt);
            else if (copy is MarketplaceReturn returned) returned.listing = Clone(returned.listing);
            return (T)(object)copy;
        }
    }
}
