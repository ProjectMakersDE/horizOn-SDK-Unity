using System;
namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    [Serializable] public class RedeemGiftCodeRequest
    {
        public string code, userId;
        public int protocolVersion;
        public string campaignId, expectedCount, configRevision;
    }
    [Serializable] public class ValidateGiftCodeRequest
    {
        public string code, userId;
        public bool includeIntent;
    }
}
