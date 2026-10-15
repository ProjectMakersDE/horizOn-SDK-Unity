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
            return JsonUtility.FromJson<PlayerValuesState>(Prepare(json));
        }
        private static string Rewrite(string raw, string member)
        {
            raw = raw.Trim();
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
        private static string Quote(string value)
        {
            string wrapper = JsonUtility.ToJson(new StringValue { value = value });
            return wrapper.Substring(wrapper.IndexOf(':') + 1, wrapper.Length - wrapper.IndexOf(':') - 2);
        }
        [Serializable] private sealed class StringValue { public string value; }
    }
}
