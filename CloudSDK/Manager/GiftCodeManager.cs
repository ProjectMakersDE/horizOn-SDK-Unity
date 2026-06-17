using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Manager for gift code redemption and validation.
    /// </summary>
    public class GiftCodeManager : BaseManager<GiftCodeManager>
    {
        /// <summary>
        /// Redeem a gift code for rewards.
        /// </summary>
        /// <param name="code">The gift code to redeem</param>
        /// <returns>Redeem response with giftData JSON string, or null if failed</returns>
        public async Task<RedeemGiftCodeResponse> Redeem(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                HorizonApp.Log.Error("Gift code is required");
                return null;
            }

            if (!PM.horizOn.Cloud.Manager.UserManager.Instance.IsSignedIn)
            {
                HorizonApp.Log.Error("User must be signed in to redeem gift code");
                return null;
            }

            var request = new RedeemGiftCodeRequest
            {
                code = code,
                userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId
            };

            var response = await HorizonApp.Network.PostAsync<RedeemGiftCodeResponse>(
                "/api/v1/app/gift-codes/redeem",
                request,
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null && response.Data.success)
            {
                HorizonApp.Log.Info($"Gift code redeemed: {code}");
                HorizonApp.Events.Publish(EventKeys.GiftCodeRedeemed, response.Data);
                return response.Data;
            }
            else
            {
                HorizonApp.Log.Error($"Gift code redemption failed: {response.Error ?? response.Data?.message}");
                return null;
            }
        }

        /// <summary>
        /// Validate a gift code without redeeming it.
        /// </summary>
        /// <param name="code">The gift code to validate</param>
        /// <returns>True if valid, false otherwise, or null if request failed</returns>
        public async Task<bool?> Validate(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                HorizonApp.Log.Error("Gift code is required");
                return null;
            }

            if (!PM.horizOn.Cloud.Manager.UserManager.Instance.IsSignedIn)
            {
                HorizonApp.Log.Error("User must be signed in to validate gift code");
                return null;
            }

            var request = new RedeemGiftCodeRequest
            {
                code = code,
                userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId
            };

            var response = await HorizonApp.Network.PostAsync<ValidateGiftCodeResponse>(
                "/api/v1/app/gift-codes/validate",
                request,
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null)
            {
                HorizonApp.Log.Info($"Gift code validated: {code} (Valid: {response.Data.valid})");
                HorizonApp.Events.Publish(EventKeys.GiftCodeValidated, response.Data.valid);
                return response.Data.valid;
            }
            else
            {
                HorizonApp.Log.Error($"Gift code validation failed: {response.Error}");
                return null;
            }
        }
    }
}
