using System;

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
    }

    /// <summary>
    /// Response object for validating a gift code.
    /// </summary>
    [Serializable]
    public class ValidateGiftCodeResponse
    {
        public bool valid;
    }
}
