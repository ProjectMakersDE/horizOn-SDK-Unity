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
