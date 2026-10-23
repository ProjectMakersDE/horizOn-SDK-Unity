using System;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;
namespace PM.horizOn.Cloud.Transport
{
    internal sealed class GiftCodeRedeemPlan
    {
        internal const string Endpoint = "/api/v1/app/gift-codes/redeem";
        internal RedeemGiftCodeRequest Request { get; }
        internal bool UseSessionToken => true;
        internal GiftCodeRedeemPlan(RedeemGiftCodeRequest request) { Request = request; }
    }
    internal static class GiftCodeTransportContract
    {
        internal static bool HasSession(UserData user, string token) => user != null && !string.IsNullOrEmpty(user.UserId) &&
            !string.IsNullOrEmpty(token) && string.Equals(user.AccessToken, token, StringComparison.Ordinal);
        internal static bool TryCreateRedeemPlan(UserData user, string token, string project, GiftCodeIntent intent, out GiftCodeRedeemPlan plan)
        {
            plan = null;
            if (!HasSession(user, token) || intent == null || !intent.IsValidFor(project, user.UserId)) return false;
            plan = new GiftCodeRedeemPlan(new RedeemGiftCodeRequest { code=intent.code, userId=intent.userId, protocolVersion=2,
                campaignId=intent.campaignId, expectedCount=intent.expectedCount, configRevision=intent.configRevision });
            return true;
        }
        // V1 identities are never silently translated to a new sequence slot.
        internal static bool TryCreateRedeemPlan(UserData user, string token, string code, out GiftCodeRedeemPlan plan) { plan=null; return false; }
        internal static bool TryCreateRedeemPlan(UserData user, string token, string code, string id, out GiftCodeRedeemPlan plan) { plan=null; return false; }
    }
}
