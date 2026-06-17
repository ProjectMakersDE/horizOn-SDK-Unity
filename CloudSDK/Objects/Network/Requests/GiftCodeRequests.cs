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
