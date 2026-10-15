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
            var entries = JsonFragments.Array(root["entries"]);
            if (entries.Count > 550 || result.entries == null || entries.Count != result.entries.Length) throw new FormatException("Inbox entry bound");
            for (int i = 0; i < entries.Count; i++)
            {
                var fields = JsonFragments.Object(entries[i]);
                foreach (var field in new[] { "id", "kind", "type", "reference", "textKey", "createdAt", "expiresAt" }) JsonFragments.String(fields[field]);
                foreach (var field in new[] { "overdue", "read", "claimed" }) if (fields[field] != "true" && fields[field] != "false") throw new FormatException("Inbox entry flag invalid");
                var entry = result.entries[i];
                if (entry == null || !fields.TryGetValue("parameters", out var parameters)) throw new FormatException("Inbox entry invalid");
                if ((entry.kind != "message" && entry.kind != "broadcast") || !entry.id.StartsWith(entry.kind + ":", StringComparison.Ordinal) || !Guid.TryParseExact(entry.id.Substring(entry.kind.Length + 1), "D", out _)) throw new FormatException("Inbox entry identity invalid");
                entry.parameters = new Dictionary<string, string>();
                foreach (var pair in JsonFragments.Object(parameters)) entry.parameters.Add(pair.Key, JsonFragments.String(pair.Value));
                if (fields.TryGetValue("attachment", out var attachment) && attachment != "null")
                {
                    var raw = JsonFragments.Object(attachment);
                    if (raw.Count != 1 || !raw.ContainsKey("values")) throw new FormatException("Unsupported Inbox attachment");
                    var amounts = JsonFragments.Array(raw["values"]);
                    if (amounts.Count > 8) throw new FormatException("Inbox reward bound");
                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var amount in amounts)
                    {
                        var value = JsonFragments.Object(amount);
                        string key = JsonFragments.String(value["key"]);
                        if (string.IsNullOrEmpty(key) || key.Length > 24 || !keys.Add(key) || Integer(value["amount"]) <= 0) throw new FormatException("Inbox reward invalid");
                    }
                }
            }
            result.counts = new Dictionary<string, InboxCount>();
            foreach (var pair in JsonFragments.Object(root["counts"]))
            {
                var counts = JsonFragments.Object(pair.Value);
                foreach (var key in new[] { "total", "unread", "claimable" }) Integer(counts[key], int.MaxValue);
                result.counts.Add(pair.Key, JsonUtility.FromJson<InboxCount>(pair.Value));
            }
            var limits = JsonFragments.Object(root["limits"]);
            Integer(limits["targeted"], int.MaxValue); Integer(limits["broadcasts"], int.MaxValue);
            return result;
        }
        public static InboxClaimResponse Claim(string json)
        {
            var root = JsonFragments.Object(json);
            if (root["replayed"] != "true" && root["replayed"] != "false") throw new FormatException("Inbox replay flag invalid");
            JsonFragments.String(root["entryId"]);
            var result = JsonUtility.FromJson<InboxClaimResponse>(json);
            result.inbox = Snapshot(root["inbox"]);
            if (!root.TryGetValue("playerValues", out var raw) || raw == "null") throw new FormatException("Inbox claim value snapshot missing");
            result.playerValues = PlayerValuesJson.State(raw);
            return result;
        }
        public static InboxSnapshot Clone(InboxSnapshot source)
        {
            if (source == null) return null;
            var result = JsonUtility.FromJson<InboxSnapshot>(JsonUtility.ToJson(source));
            result.counts = new Dictionary<string, InboxCount>();
            foreach (var pair in source.counts) result.counts.Add(pair.Key, new InboxCount { total = pair.Value.total, unread = pair.Value.unread, claimable = pair.Value.claimable });
            for (int i = 0; i < source.entries.Length; i++) result.entries[i].parameters = new Dictionary<string, string>(source.entries[i].parameters);
            return result;
        }
        private static long Integer(string value, long max = PlayerValuesManager.MaxSafeInteger)
        {
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 0 || number > max) throw new FormatException("Inbox integer invalid");
            return number;
        }
    }
}
