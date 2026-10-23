using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// Response object for redeeming a gift code.
    /// </summary>
    [Serializable]
    public class RedeemGiftCodeResponse
    {
        public bool success;
        public string message;
        public string giftData; // JSON string containing gift data

        /// <summary>
        /// Cosmetic IDs from the code's "grants" that the player owns after this redemption
        /// (newly unlocked or owned before). Empty when the code grants nothing.
        /// </summary>
        public string[] grantedUnlocks = new string[0];
        public PlayerValueAmount[] grantedValues = Array.Empty<PlayerValueAmount>();
        /// <summary>Authoritative current balances, already adopted by PlayerValuesManager. Never add grantedValues locally.</summary>
        public PlayerValuesState playerValues;
        public bool replayed, alreadyApplied;
        public string campaignId, count, configRevision;
    }

    /// <summary>
    /// Response object for validating a gift code.
    /// </summary>
    [Serializable]
    public class ValidateGiftCodeResponse
    {
        public bool valid;
        public int protocolVersion;
        public string campaignId, expectedCount, configRevision;
    }
    /// <summary>Persist before sending. Scope hashes the project key; no session token is stored.</summary>
    [Serializable] public sealed class GiftCodeIntent
    {
        public int protocolVersion = 2;
        public string projectScope, userId, code, campaignId, expectedCount, configRevision;
        public static string ProjectFingerprint(string project)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(project ?? ""))).Replace("-", "");
        }
        public static bool CanonicalCount(string value) => !string.IsNullOrEmpty(value) &&
            (value == "0" || value[0] >= '1' && value[0] <= '9') &&
            long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0 && parsed.ToString(CultureInfo.InvariantCulture) == value;
        public bool IsValidFor(string project, string user) => protocolVersion == 2 && projectScope == ProjectFingerprint(project) &&
            !string.IsNullOrEmpty(user) && userId == user && !string.IsNullOrWhiteSpace(code) && code.Length <= 50 &&
            Guid.TryParseExact(campaignId, "D", out _) && CanonicalCount(expectedCount) && CanonicalCount(configRevision);
        public GiftCodeIntent Copy() => (GiftCodeIntent)MemberwiseClone();
    }

}
