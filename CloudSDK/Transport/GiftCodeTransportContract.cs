using System;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;

namespace PM.horizOn.Cloud.Transport
{
    internal sealed class GiftCodeRedeemPlan
    {
        internal const string Endpoint = "/api/v1/app/gift-codes/redeem";

        internal RedeemGiftCodeRequest Request { get; }

        /// <summary>
        /// The server binds redemption to the player's Bearer session (TASK-886),
        /// so redeem always sends the session token.
        /// </summary>
        internal bool UseSessionToken => true;

        internal GiftCodeRedeemPlan(RedeemGiftCodeRequest request)
        {
            Request = request;
        }
    }

    internal static class GiftCodeTransportContract
    {
        /// <summary>
        /// Builds the redeem request only for a signed-in user whose access token is the
        /// session token the transport will send. Returns false otherwise, so no request
        /// without a matching session reaches the server.
        /// </summary>
        internal static bool TryCreateRedeemPlan(
            UserData user,
            string transportSessionToken,
            string code,
            out GiftCodeRedeemPlan plan)
        {
            plan = null;
            if (string.IsNullOrEmpty(code))
            {
                return false;
            }
            if (user == null || string.IsNullOrEmpty(user.UserId) || string.IsNullOrEmpty(user.AccessToken))
            {
                return false;
            }
            if (string.IsNullOrEmpty(transportSessionToken) ||
                !string.Equals(user.AccessToken, transportSessionToken, StringComparison.Ordinal))
            {
                return false;
            }

            plan = new GiftCodeRedeemPlan(
                new RedeemGiftCodeRequest
                {
                    code = code,
                    userId = user.UserId
                });
            return true;
        }
    }
}
