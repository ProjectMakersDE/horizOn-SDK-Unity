using System;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Request object for redeeming a gift code.
    /// </summary>
    [Serializable]
    public class RedeemGiftCodeRequest
    {
        public string code;
        public string userId;
        /// <summary>Optional stable ID for this redemption. Preserve it across uncertain outcomes.</summary>
        public string idempotencyId;
    }

    /// <summary>
    /// Request object for validating a gift code.
    /// </summary>
    [Serializable]
    public class ValidateGiftCodeRequest
    {
        public string code;
        public string userId;
    }
}
