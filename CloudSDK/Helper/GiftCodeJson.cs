using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Helper
{
    /// <summary>Validate original integer lexemes and decoded keys before any Unity numeric conversion.</summary>
    internal static class GiftCodeJson
    {
        public static ValidateGiftCodeResponse Validate(string json)
        {
            var root=JsonFragments.Object(json);
            if (!root.TryGetValue("valid",out var valid) || (valid!="true" && valid!="false")) throw new FormatException("Invalid validation");
            var result=new ValidateGiftCodeResponse { valid=valid=="true" };
            if (root.ContainsKey("protocolVersion"))
            {
                if (root["protocolVersion"]!="2") throw new FormatException("Unsupported protocol");
                result.protocolVersion=2; result.campaignId=JsonFragments.String(root["campaignId"]);
                result.expectedCount=JsonFragments.String(root["expectedCount"]); result.configRevision=JsonFragments.String(root["configRevision"]);
                if (!Guid.TryParseExact(result.campaignId,"D",out _) || !GiftCodeIntent.CanonicalCount(result.expectedCount) || !GiftCodeIntent.CanonicalCount(result.configRevision)) throw new FormatException("Invalid original intent");
            }
            return result;
        }
        public static RedeemGiftCodeResponse Redeem(string json)
        {
            var root = JsonFragments.Object(json);
            if (!root.TryGetValue("success", out var success) || (success != "true" && success != "false"))
                throw new FormatException("Gift code success flag invalid");
            var result = new RedeemGiftCodeResponse { success = success == "true" };
            if (root.TryGetValue("message", out var message)) result.message = JsonFragments.String(message);
            if (root.TryGetValue("giftData", out var gift) && gift != "null") result.giftData = JsonFragments.String(gift);
            if (root.TryGetValue("replayed", out var replayed))
            {
                if (replayed != "true" && replayed != "false") throw new FormatException("Gift code replay flag invalid");
                result.replayed = replayed == "true";
            }
            if (root.TryGetValue("grantedUnlocks", out var unlocks) && unlocks != "null")
                result.grantedUnlocks = JsonFragments.Array(unlocks).ConvertAll(JsonFragments.String).ToArray();
            if (root.TryGetValue("grantedValues", out var grants))
            {
                var raw = JsonFragments.Array(grants);
                if (raw.Count > 8) throw new FormatException("Gift code reward bound");
                var keys = new HashSet<string>(StringComparer.Ordinal);
                result.grantedValues = new PlayerValueAmount[raw.Count];
                for (int i = 0; i < raw.Count; i++)
                {
                    var fields = JsonFragments.Object(raw[i]);
                    string key = JsonFragments.String(fields["key"]);
                    if (!Regex.IsMatch(key, @"\A[a-z0-9][a-z0-9._-]{0,23}\z") || !keys.Add(key)
                        || !long.TryParse(fields["amount"], NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
                        || amount <= 0 || amount > PlayerValuesManager.MaxSafeInteger) throw new FormatException("Gift code reward invalid");
                    result.grantedValues[i] = new PlayerValueAmount { key = key, amount = amount };
                }
            }
            if (root.TryGetValue("playerValues", out var state) && state != "null") result.playerValues = PlayerValuesJson.State(state);
            if (result.grantedValues.Length > 0 && result.playerValues == null) throw new FormatException("Gift code balance snapshot missing");
            result.campaignId=JsonFragments.String(root["campaignId"]);
            result.count=JsonFragments.String(root["count"]); result.configRevision=JsonFragments.String(root["configRevision"]);
            if (!Guid.TryParseExact(result.campaignId,"D",out _) || !GiftCodeIntent.CanonicalCount(result.count) || !GiftCodeIntent.CanonicalCount(result.configRevision)
                || !root.TryGetValue("alreadyApplied",out var applied) || (applied!="true" && applied!="false")) throw new FormatException("Invalid sequence result");
            result.alreadyApplied=applied=="true";
            if (!root.ContainsKey("replayed") || result.replayed!=result.alreadyApplied || result.alreadyApplied &&
                (result.giftData!=null || result.grantedValues.Length>0 || result.grantedUnlocks.Length>0)) throw new FormatException("Historical replay rewards forbidden");
            return result;
        }
    }
}
