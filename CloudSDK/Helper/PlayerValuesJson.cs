using System;
using System.Globalization;
using System.Linq;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;
namespace PM.horizOn.Cloud.Helper
{
    /// <summary>Validate decoded member names and original integer lexemes before Unity rounds numbers.</summary>
    internal static class PlayerValuesJson
    {
        public static string Prepare(string json) => Rewrite(json, null);
        public static PlayerValuesState State(string json)
        {
            var root = JsonFragments.Object(json);
            foreach (string key in new[] { "userId", "day", "definitionVersion", "revision", "values", "operationWindow" })
                if (!root.ContainsKey(key)) throw new FormatException("Player values snapshot field missing");
            JsonFragments.String(root["userId"]); JsonFragments.String(root["day"]);
            var window = JsonFragments.Object(root["operationWindow"]);
            JsonFragments.String(window["serverTime"]); JsonFragments.String(window["oldestAcceptedAt"]);
            foreach (string balance in JsonFragments.Array(root["values"]))
            {
                var fields = JsonFragments.Object(balance);
                JsonFragments.String(fields["key"]);
                foreach (string key in new[] { "balance", "earnedToday", "dailyCap" })
                    if (!fields.ContainsKey(key)) throw new FormatException("Player values balance field missing");
            }
            var state = JsonUtility.FromJson<PlayerValuesState>(Prepare(json));
            if (root.TryGetValue("operation", out var originalOperation) && originalOperation != "null")
            {
                var operation = JsonFragments.Object(originalOperation);
                state.operation.items = operation.TryGetValue("items", out var items) ? ItemsJson.References(items) : Array.Empty<ItemReference>();
            }
            else state.operation = null;
            return state;
        }
        public static PlayerValuesOffers Offers(string json)
        {
            var result = JsonUtility.FromJson<PlayerValuesOffers>(Prepare(json));
            var root = JsonFragments.Object(json); var offers = JsonFragments.Array(root["offers"]);
            if (result == null || result.offers == null || result.offers.Length != offers.Count) throw new FormatException("Player values offers invalid");
            for (int i = 0; i < offers.Count; i++)
            {
                var fields = JsonFragments.Object(offers[i]);
                result.offers[i].name = JsonFragments.String(fields["name"]);
                result.offers[i].itemRewards = fields.TryGetValue("itemRewards", out var rewards) ? ItemsJson.Rewards(rewards) : Array.Empty<ItemReward>();
            }
            return result;
        }
        private static string Rewrite(string raw, string member)
        {
            raw = raw.Trim();
            if (member == "itemRewards") ItemsJson.Rewards(raw);
            if (member == "items") ItemsJson.References(raw);
            if (raw.StartsWith("{", StringComparison.Ordinal))
                return "{" + string.Join(",", JsonFragments.Object(raw).Select(pair => Quote(pair.Key) + ":" + Rewrite(pair.Value, pair.Key))) + "}";
            if (raw.StartsWith("[", StringComparison.Ordinal))
                return "[" + string.Join(",", JsonFragments.Array(raw).Select(value => Rewrite(value, null))) + "]";
            bool nullable = member == "dailyCap" || member == "purchasesPerDay";
            if (nullable && raw == "null") return "-1";
            if (nullable || member == "balance" || member == "earnedToday" || member == "amount" || member == "revision" || member == "definitionVersion")
            {
                if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                    || number < -PlayerValuesManager.MaxSafeInteger || number > PlayerValuesManager.MaxSafeInteger
                    || member == "purchasesPerDay" && number > int.MaxValue)
                    throw new FormatException("Player values response contains an unsafe or non-integer number");
            }
            return raw;
        }
        private static string Quote(string value) => ItemsJson.Quote(value);
        internal static T Clone<T>(T source) where T : class
        {
            if (source == null) return null;
            object copy;
            if (source is PlayerValuesState s)
                copy = new PlayerValuesState { userId = s.userId, day = s.day, definitionVersion = s.definitionVersion, revision = s.revision,
                    values = Array.ConvertAll(s.values, Balance), operationWindow = s.operationWindow == null ? null : new PlayerValuesOperationWindow { serverTime = s.operationWindow.serverTime, oldestAcceptedAt = s.operationWindow.oldestAcceptedAt },
                    operation = s.operation == null ? null : new PlayerValuesOperation { operationId = s.operation.operationId, replayed = s.operation.replayed,
                        applied = Array.ConvertAll(s.operation.applied ?? Array.Empty<PlayerValueAmount>(), Amount), items = Array.ConvertAll(s.operation.items ?? Array.Empty<ItemReference>(), x => new ItemReference { instanceId = x.instanceId, itemKey = x.itemKey }) } };
            else if (source is PlayerValuesOffers o)
                copy = new PlayerValuesOffers { definitionVersion = o.definitionVersion, serverTime = o.serverTime, definitions = Array.ConvertAll(o.definitions, Balance),
                    offers = Array.ConvertAll(o.offers, x => new PlayerValueOffer { key = x.key, name = x.name, active = x.active, purchasesPerDay = x.purchasesPerDay,
                        costs = Array.ConvertAll(x.costs, Amount), rewards = Array.ConvertAll(x.rewards, Amount), itemRewards = Array.ConvertAll(x.itemRewards ?? Array.Empty<ItemReward>(), r => new ItemReward { itemKey = r.itemKey, quantity = r.quantity }) }) };
            else throw new ArgumentException("Unknown Player Values copy");
            return (T)copy;
        }
        private static PlayerValueBalance Balance(PlayerValueBalance b) => new PlayerValueBalance { key = b.key, balance = b.balance, earnedToday = b.earnedToday, dailyCap = b.dailyCap };
        private static PlayerValueAmount Amount(PlayerValueAmount a) => new PlayerValueAmount { key = a.key, amount = a.amount };
    }
}
