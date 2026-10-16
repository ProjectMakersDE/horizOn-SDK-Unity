using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;
namespace PM.horizOn.Cloud.Helper
{
    /// <summary>Decode names and validate original safe-integer tokens before Unity conversion.</summary>
    internal static class AchievementsJson
    {
        internal static bool ValidKey(string key) => key != null && Regex.IsMatch(key, @"\A[a-z0-9][a-z0-9._-]{0,23}\z");
        internal static bool ValidVersion(string version) => version != null && Regex.IsMatch(version, @"\A[0-9a-f]{64}\z");
        public static AchievementsSnapshot Snapshot(string json)
        {
            var f = JsonFragments.Object(json);
            var result = new AchievementsSnapshot { userId = Text(f["userId"]), revision = Number(f["revision"]), definitionVersion = Text(f["definitionVersion"]), serverTime = Instant(f["serverTime"]) };
            if (!Guid.TryParseExact(result.userId, "D", out _) || !ValidVersion(result.definitionVersion)) throw new FormatException("Achievement identity/version invalid");
            result.definitions = f["definitions"] == "null" ? null : Rows(f["definitions"], 500, Definition, d => d.key);
            result.progress = Rows(f["progress"], 500, Progress, p => p.key);
            var limits = JsonFragments.Object(f["limits"]);
            result.limits = new AchievementLimits { maxDefinitions = (int)Number(limits["maxDefinitions"], 500), maxLifetimeKeys = (int)Number(limits["maxLifetimeKeys"], 500) };
            if (result.limits.maxDefinitions <= 0 || result.limits.maxLifetimeKeys <= 0) throw new FormatException("Achievement limits invalid");
            return result;
        }
        public static AchievementUnlockResponse Unlock(string json)
        {
            var f = JsonFragments.Object(json);
            var result = new AchievementUnlockResponse { achievements = Snapshot(f["achievements"]), replayed = Flag(f["replayed"]),
                newlyUnlocked = Rows(f["newlyUnlocked"], 1, Key, k => k), deliveryResults = Rows(f["deliveryResults"], 1, Outcome, r => r.key) };
            if (result.replayed && result.newlyUnlocked.Length != 0) throw new FormatException("Replayed achievement cannot be new");
            return result;
        }
        public static AchievementDeliveryResponse Delivery(string json)
        {
            var f = JsonFragments.Object(json);
            return new AchievementDeliveryResponse { achievements = Snapshot(f["achievements"]), results = Rows(f["results"], 8, Outcome, r => r.key) };
        }
        private static AchievementDefinition Definition(string json)
        {
            var f = JsonFragments.Object(json);
            var result = new AchievementDefinition { key = Key(f["key"]), name = Text(f["name"]), description = Text(f["description"]), imageUrl = OptionalText(f, "imageUrl"),
                hidden = Flag(f["hidden"]), active = Flag(f["active"]), mode = Text(f["mode"]), rewards = new InboxAttachment { values = Amounts(JsonFragments.Object(f["rewards"])["values"]) } };
            if (string.IsNullOrWhiteSpace(result.name) || Encoding.UTF8.GetByteCount(result.name) > 120 || Encoding.UTF8.GetByteCount(result.description) > 512) throw new FormatException("Achievement text invalid");
            if (result.imageUrl != null && (result.imageUrl.Length > 2048 || !Uri.TryCreate(result.imageUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != "http" && uri.Scheme != "https") || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))) throw new FormatException("Achievement image URL invalid");
            if (f.TryGetValue("condition", out var condition) && condition != "null")
            {
                var c = JsonFragments.Object(condition);
                result.condition = new AchievementCondition { statisticKey = Key(c["statisticKey"]), threshold = Number(c["threshold"]) };
            }
            if ((result.mode == "manual" && result.condition != null) || (result.mode == "statistic" && result.condition == null)
                || (result.mode != "manual" && result.mode != "statistic")) throw new FormatException("Achievement mode invalid");
            return result;
        }
        private static AchievementProgress Progress(string json)
        {
            var f = JsonFragments.Object(json);
            var result = new AchievementProgress { key = Key(f["key"]), unlockedAt = Instant(f["unlockedAt"]), rewardState = State(f["rewardState"]), inboxEntryId = OptionalText(f, "inboxEntryId") };
            if (f.TryGetValue("pendingReward", out var pending) && pending != "null")
            {
                var p = JsonFragments.Object(pending);
                result.pendingReward = new PendingAchievementReward { issuedAt = Instant(p["issuedAt"]), values = Amounts(p["values"]) };
                if (result.pendingReward.values.Length == 0) throw new FormatException("Pending achievement has no reward");
            }
            if ((result.rewardState == "pending") != (result.pendingReward != null)
                || (result.rewardState == "delivered" ? !ValidEntry(result.inboxEntryId) : result.inboxEntryId != null)) throw new FormatException("Achievement progress invalid");
            return result;
        }
        private static AchievementDeliveryOutcome Outcome(string json)
        {
            var f = JsonFragments.Object(json);
            var result = new AchievementDeliveryOutcome { key = Key(f["key"]), state = State(f["state"]), inboxEntryId = OptionalText(f, "inboxEntryId"), code = OptionalText(f, "code") };
            if (result.state == "delivered" ? !ValidEntry(result.inboxEntryId) : result.inboxEntryId != null) throw new FormatException("Achievement delivery invalid");
            return result;
        }
        private static PlayerValueAmount[] Amounts(string json) => Rows(json, 8, raw =>
        {
            var f = JsonFragments.Object(raw); long amount = Number(f["amount"]);
            if (amount <= 0) throw new FormatException("Achievement reward invalid");
            return new PlayerValueAmount { key = Key(f["key"]), amount = amount };
        }, a => a.key);
        private static T[] Rows<T>(string json, int bound, Func<string, T> parse, Func<T, string> identity)
        {
            var raw = JsonFragments.Array(json); if (raw.Count > bound) throw new FormatException("Achievement array bound");
            var keys = new HashSet<string>(StringComparer.Ordinal); var result = new T[raw.Count];
            for (int i = 0; i < raw.Count; i++) { result[i] = parse(raw[i]); if (!keys.Add(identity(result[i]))) throw new FormatException("Achievement duplicate key"); }
            return result;
        }
        private static string Key(string json) { string value = Text(json); if (!ValidKey(value)) throw new FormatException("Achievement key invalid"); return value; }
        private static string State(string json) { string value = Text(json); if (value != "none" && value != "pending" && value != "delivered") throw new FormatException("Achievement reward state invalid"); return value; }
        private static string Text(string json) => JsonFragments.String(json);
        private static string OptionalText(Dictionary<string, string> fields, string key) => fields.TryGetValue(key, out var raw) && raw != "null" ? Text(raw) : null;
        private static string Instant(string json) { string value = Text(json); if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)) throw new FormatException("Achievement time invalid"); return value; }
        private static long Number(string raw, long max = PlayerValuesManager.MaxSafeInteger)
        { if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 0 || n > max) throw new FormatException("Achievement integer invalid"); return n; }
        private static bool Flag(string raw) { if (raw != "true" && raw != "false") throw new FormatException("Achievement boolean invalid"); return raw == "true"; }
        private static bool ValidEntry(string id) => id != null && id.StartsWith("message:", StringComparison.Ordinal) && Guid.TryParseExact(id.Substring(8), "D", out _);
        internal static T Clone<T>(T source) where T : class => source == null ? null : JsonUtility.FromJson<T>(JsonUtility.ToJson(source));
    }
}
