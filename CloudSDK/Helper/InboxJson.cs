using System;
using System.Collections.Generic;
using System.Globalization;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;
namespace PM.horizOn.Cloud.Helper
{
    internal static class InboxJson
    {
        public static InboxSnapshot Snapshot(string json)
        {
            var root = JsonFragments.Object(json);
            foreach (var field in new[] { "userId", "cursor", "serverTime", "revision", "broadcastRevision", "pollAfterSeconds", "unchanged", "entries", "counts", "limits" }) if (!root.ContainsKey(field)) throw new FormatException("Inbox field missing");
            foreach (var field in new[] { "userId", "cursor", "serverTime" }) JsonFragments.String(root[field]);
            Integer(root["revision"]); Integer(root["broadcastRevision"]); Integer(root["pollAfterSeconds"], int.MaxValue);
            if (root["unchanged"] != "true" && root["unchanged"] != "false") throw new FormatException("Inbox flag invalid");
            var result = JsonUtility.FromJson<InboxSnapshot>(json);
            if (!FriendsJson.IsUuid(result.userId) || string.IsNullOrEmpty(result.cursor) || result.cursor.Length > 128
                || result.pollAfterSeconds <= 0) throw new FormatException("Inbox identity invalid");
            Instant(result.serverTime);
            var entries = JsonFragments.Array(root["entries"]);
            // Quotas control new writes. Retained entries must remain readable after an admin lowers them.
            if (result.entries == null || entries.Count != result.entries.Length) throw new FormatException("Inbox entries invalid");
            var entryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Count; i++)
            {
                var fields = JsonFragments.Object(entries[i]);
                foreach (var field in new[] { "id", "kind", "type", "reference", "textKey", "createdAt", "expiresAt" }) JsonFragments.String(fields[field]);
                foreach (var field in new[] { "overdue", "read", "claimed" }) if (fields[field] != "true" && fields[field] != "false") throw new FormatException("Inbox entry flag invalid");
                var entry = result.entries[i];
                if (entry == null || !fields.TryGetValue("parameters", out var parameters)) throw new FormatException("Inbox entry invalid");
                if ((entry.kind != "message" && entry.kind != "broadcast") || !entry.id.StartsWith(entry.kind + ":", StringComparison.Ordinal) || !Guid.TryParseExact(entry.id.Substring(entry.kind.Length + 1), "D", out _) || !entryIds.Add(entry.id)) throw new FormatException("Inbox entry identity invalid");
                Instant(entry.createdAt); Instant(entry.expiresAt);
                entry.parameters = new Dictionary<string, string>();
                foreach (var pair in JsonFragments.Object(parameters)) entry.parameters.Add(pair.Key, JsonFragments.String(pair.Value));
                entry.attachment = null;
                if (fields.TryGetValue("attachment", out var attachment) && attachment != "null")
                {
                    var raw = JsonFragments.Object(attachment);
                    foreach (var key in raw.Keys) if (key != "values" && key != "itemReturn") throw new FormatException("Unsupported Inbox attachment");
                    var amounts = raw.TryGetValue("values", out var values) ? JsonFragments.Array(values) : new List<string>();
                    if (amounts.Count > 8) throw new FormatException("Inbox reward bound");
                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    var parsedValues = new List<PlayerValueAmount>();
                    foreach (var amount in amounts)
                    {
                        var value = JsonFragments.Object(amount);
                        string key = JsonFragments.String(value["key"]);
                        if (value.Count != 2 || string.IsNullOrEmpty(key) || key.Length > 24 || !keys.Add(key) || Integer(value["amount"]) <= 0) throw new FormatException("Inbox reward invalid");
                        parsedValues.Add(new PlayerValueAmount { key = key, amount = Integer(value["amount"]) });
                    }
                    entry.attachment = new InboxAttachment { values = parsedValues.ToArray() };
                    if (raw.TryGetValue("itemReturn", out var itemReturn) && itemReturn != "null")
                    {
                        var item = JsonFragments.Object(itemReturn);
                        if (item.Count != 3 || !FriendsJson.IsUuid(JsonFragments.String(item["listingId"]))
                            || !FriendsJson.IsUuid(JsonFragments.String(item["instanceId"])) || !ItemsJson.Key(JsonFragments.String(item["itemKey"]))) throw new FormatException("Inbox return invalid");
                        entry.attachment.itemReturn = new InboxItemReturn { listingId = JsonFragments.String(item["listingId"]), instanceId = JsonFragments.String(item["instanceId"]), itemKey = JsonFragments.String(item["itemKey"]) };
                    }
                }
                if (entry.SourceKind != InboxSourceKind.Unknown && !entry.TryGetSource(out _)) throw new FormatException("Inbox source invalid");
                if (entry.kind == "message" && entry.type == "free_text" && entry.textKey == "horizon.inbox.free_text" && !entry.TryGetFreeText(out _)) throw new FormatException("Inbox free text missing");
            }
            result.counts = new Dictionary<string, InboxCount>();
            foreach (var pair in JsonFragments.Object(root["counts"]))
            {
                var counts = JsonFragments.Object(pair.Value);
                foreach (var key in new[] { "total", "unread", "claimable" }) Integer(counts[key], int.MaxValue);
                result.counts.Add(pair.Key, JsonUtility.FromJson<InboxCount>(pair.Value));
            }
            var limits = JsonFragments.Object(root["limits"]);
            foreach (var name in new[] { "targeted", "broadcasts", "freeTexts", "freeTextBytes", "freeTextRetentionDays" })
                if (limits.TryGetValue(name, out var limit)) { if (Integer(limit, int.MaxValue) == 0) throw new FormatException("Inbox limit invalid"); }
                else if (name == "targeted" || name == "broadcasts") throw new FormatException("Inbox limit missing");
            result.limits.freeTexts = limits.TryGetValue("freeTexts", out var freeTexts) ? (int)Integer(freeTexts, int.MaxValue) : 2;
            result.limits.freeTextBytes = limits.TryGetValue("freeTextBytes", out var freeTextBytes) ? (int)Integer(freeTextBytes, int.MaxValue) : 256;
            result.limits.freeTextRetentionDays = limits.TryGetValue("freeTextRetentionDays", out var retention) ? (int)Integer(retention, int.MaxValue) : 7;
            return result;
        }
        public static InboxClaimResponse Claim(string json)
        {
            var root = JsonFragments.Object(json);
            if (root["replayed"] != "true" && root["replayed"] != "false") throw new FormatException("Inbox replay flag invalid");
            string entryId = JsonFragments.String(root["entryId"]);
            var parts = entryId.Split(':');
            if (parts.Length != 2 || (parts[0] != "message" && parts[0] != "broadcast") || !FriendsJson.IsUuid(parts[1])) throw new FormatException("Inbox claim identity invalid");
            var result = JsonUtility.FromJson<InboxClaimResponse>(json);
            result.inbox = Snapshot(root["inbox"]);
            if (!root.TryGetValue("playerValues", out var raw) || raw == "null") throw new FormatException("Inbox claim value snapshot missing");
            result.playerValues = PlayerValuesJson.State(raw);
            result.items = root.TryGetValue("items", out var items) ? ItemsJson.References(items) : Array.Empty<ItemReference>();
            return result;
        }
        public static InboxSnapshot Clone(InboxSnapshot source)
        {
            if (source == null) return null;
            var result = JsonUtility.FromJson<InboxSnapshot>(JsonUtility.ToJson(source));
            result.counts = new Dictionary<string, InboxCount>();
            foreach (var pair in source.counts) result.counts.Add(pair.Key, new InboxCount { total = pair.Value.total, unread = pair.Value.unread, claimable = pair.Value.claimable });
            for (int i = 0; i < source.entries.Length; i++)
            {
                result.entries[i].parameters = new Dictionary<string, string>(source.entries[i].parameters);
                // Preserve optional reward presence explicitly; Unity serialization may materialize an absent nested class.
                var attachment = source.entries[i].attachment;
                var item = attachment?.itemReturn;
                result.entries[i].attachment = attachment == null ? null : new InboxAttachment {
                    values = Array.ConvertAll(attachment.values ?? Array.Empty<PlayerValueAmount>(), value => new PlayerValueAmount { key = value.key, amount = value.amount }),
                    itemReturn = item == null ? null : new InboxItemReturn { listingId = item.listingId, instanceId = item.instanceId, itemKey = item.itemKey }
                };
            }
            return result;
        }
        private static long Integer(string value, long max = PlayerValuesManager.MaxSafeInteger)
        {
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 0 || number > max) throw new FormatException("Inbox integer invalid");
            return number;
        }
        private static void Instant(string value)
        {
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)) throw new FormatException("Inbox time invalid");
        }
    }
}
