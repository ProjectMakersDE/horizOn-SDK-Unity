using System;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Transport;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Manager for gift code redemption and validation.
    /// </summary>
    public class GiftCodeManager : BaseManager<GiftCodeManager>
    {
        /// <summary>
        /// Redeem a gift code for rewards.
        /// The request carries the signed-in player's session, the server only redeems
        /// codes for the player who owns that session.
        /// When the code granted cosmetics (<c>grantedUnlocks</c> not empty), the cached player
        /// profile is dropped so the next <see cref="PlayerProfileManager.GetProfile"/> shows the unlock.
        /// </summary>
        /// <param name="code">The gift code to redeem</param>
        /// <returns>Redeem response with giftData JSON string and grantedUnlocks, or null if failed</returns>
        public Task<RedeemGiftCodeResponse> Redeem(string code) => RedeemInternal(code, null);

        public string LastErrorCode { get; private set; }
        private long _identityEpoch;
        protected override void RegisterEvents()
        {
            RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => _identityEpoch++);
            RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => _identityEpoch++);
        }

        /// <summary>Redeem using the original persisted ID. Never replace it after a timeout.</summary>
        public Task<RedeemGiftCodeResponse> Redeem(string code, string idempotencyId)
        {
            if (string.IsNullOrWhiteSpace(idempotencyId) || idempotencyId.Length > 128)
                return Task.FromResult(Failure("INVALID_REQUEST"));
            return RedeemInternal(code, idempotencyId);
        }
        private async Task<RedeemGiftCodeResponse> RedeemInternal(string code, string idempotencyId)
        {
            LastErrorCode = null;
            if (string.IsNullOrWhiteSpace(code) || code.Length > 50 ||
                (idempotencyId != null && (string.IsNullOrWhiteSpace(idempotencyId) || idempotencyId.Length > 128)))
                return Failure("INVALID_REQUEST");
            var network = HorizonApp.Network;
            GiftCodeRedeemPlan plan;
            bool valid = idempotencyId == null
                ? GiftCodeTransportContract.TryCreateRedeemPlan(UserManager.Instance.CurrentUser, network.GetSessionToken(), code, out plan)
                : GiftCodeTransportContract.TryCreateRedeemPlan(UserManager.Instance.CurrentUser, network.GetSessionToken(), code, idempotencyId, out plan);
            if (!valid) return Failure("SESSION_REQUIRED");

            long generation = network.ContextGeneration, epoch = _identityEpoch;
            string userId = plan.Request.userId, token = network.GetSessionToken();
            var values = PlayerValuesManager.Instance;
            long valuesEpoch = values.CacheEpoch;
            var response = await network.PostAsync<RedeemGiftCodeResponse>(GiftCodeRedeemPlan.Endpoint,
                plan.Request, useSessionToken: plan.UseSessionToken);
            var user = UserManager.Instance.CurrentUser;
            if (!ReferenceEquals(network, HorizonApp.Network) || generation != network.ContextGeneration || epoch != _identityEpoch
                || user?.UserId != userId || user.AccessToken != token)
                return Failure("STALE_RESPONSE");
            if (!response.IsSuccess || response.Data == null || !response.Data.success)
                return Failure(response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode));
            var data = response.Data;
            if (data.playerValues != null)
            {
                if (valuesEpoch != values.CacheEpoch || !values.AcceptServerState(userId, data.playerValues)) return Failure("STALE_RESPONSE");
                data.playerValues = values.CurrentState;
            }
            if (data.grantedUnlocks != null && data.grantedUnlocks.Length > 0) PlayerProfileManager.Instance.ClearCache();
            LastErrorCode = null;
            HorizonApp.Events.Publish(EventKeys.GiftCodeRedeemed, data);
            return data;
        }
        private RedeemGiftCodeResponse Failure(string code)
        {
            LastErrorCode = code;
            HorizonApp.Log.Warning($"Gift code redemption failed [{code}]; retain the original intent for reconciliation.");
            return null;
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

            var request = new ValidateGiftCodeRequest
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
