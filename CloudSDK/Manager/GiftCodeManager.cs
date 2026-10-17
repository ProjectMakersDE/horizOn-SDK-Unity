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
    public class GiftCodeManager : BaseManager<GiftCodeManager>
    {
        public string LastErrorCode { get; private set; }
        private long _identityEpoch;
        protected override void RegisterEvents()
        {
            RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => _identityEpoch++);
            RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => _identityEpoch++);
        }
        [Obsolete("V1 is closed. PrepareRedemption, persist its intent, then RedeemPrepared.")]
        public Task<RedeemGiftCodeResponse> Redeem(string code) => Task.FromResult(Failure("UPGRADE_REQUIRED"));
        [Obsolete("Arbitrary IDs cannot be converted. Reconcile the V1 operation before preparing V2.")]
        public Task<RedeemGiftCodeResponse> Redeem(string code, string idempotencyId) => Task.FromResult(Failure("UPGRADE_REQUIRED"));

        /// <summary>Read-only. The caller must durably save the returned identity before RedeemPrepared.</summary>
        public async Task<GiftCodeIntent> PrepareRedemption(string code)
        {
            LastErrorCode=null;
            if (string.IsNullOrWhiteSpace(code) || code.Length>50) { Failure("INVALID_REQUEST"); return null; }
            var network=HorizonApp.Network;
            var user=UserManager.Instance.CurrentUser;
            string token=network.GetSessionToken(), project=network.GetApiKey(), userId=user?.UserId;
            if (!GiftCodeTransportContract.HasSession(user, token)) { Failure("SESSION_REQUIRED"); return null; }
            long generation=network.ContextGeneration, epoch=_identityEpoch;
            var response=await network.PostAsync<ValidateGiftCodeResponse>("/api/v1/app/gift-codes/validate",
                new ValidateGiftCodeRequest {code=code,userId=userId,includeIntent=true},useSessionToken:true);
            if (!ReferenceEquals(network,HorizonApp.Network) || generation!=network.ContextGeneration || epoch!=_identityEpoch || UserManager.Instance.CurrentUser?.UserId!=userId || !GiftCodeTransportContract.HasSession(UserManager.Instance.CurrentUser,network.GetSessionToken()))
            { Failure("STALE_RESPONSE"); return null; }
            if (!response.IsSuccess || response.Data==null) { Failure(response.ErrorCode ?? "INVALID_RESPONSE"); return null; }
            if (!response.Data.valid) { Failure("CODE_NOT_ELIGIBLE"); return null; }
            var data=response.Data;
            var intent=new GiftCodeIntent {protocolVersion=data.protocolVersion,projectScope=GiftCodeIntent.ProjectFingerprint(project),userId=userId,code=code,
                campaignId=data.campaignId,expectedCount=data.expectedCount,configRevision=data.configRevision};
            if (!intent.IsValidFor(project,userId)) { Failure("INVALID_RESPONSE"); return null; }
            return intent;
        }
        /// <summary>Retry the original persisted tuple after any uncertain outcome, including restart.</summary>
        public async Task<RedeemGiftCodeResponse> RedeemPrepared(GiftCodeIntent original)
        {
            LastErrorCode=null;
            var network=HorizonApp.Network;
            var user=UserManager.Instance.CurrentUser;
            if (!GiftCodeTransportContract.HasSession(user,network.GetSessionToken())) return Failure("SESSION_REQUIRED");
            var intent=original?.Copy();
            if (!GiftCodeTransportContract.TryCreateRedeemPlan(user,network.GetSessionToken(),network.GetApiKey(),intent,out var plan)) return Failure("INVALID_REQUEST");
            long generation=network.ContextGeneration, epoch=_identityEpoch;
            string userId=plan.Request.userId;
            var values=PlayerValuesManager.Instance;
            long valuesEpoch=values.CacheEpoch;
            var response=await network.PostAsync<RedeemGiftCodeResponse>(GiftCodeRedeemPlan.Endpoint,plan.Request,useSessionToken:true);
            if (!ReferenceEquals(network,HorizonApp.Network) || generation!=network.ContextGeneration || epoch!=_identityEpoch || UserManager.Instance.CurrentUser?.UserId!=userId || !GiftCodeTransportContract.HasSession(UserManager.Instance.CurrentUser,network.GetSessionToken()))
                return Failure("STALE_RESPONSE");
            if (!response.IsSuccess || response.Data==null || !response.Data.success) return Failure(response.ErrorCode ?? "INVALID_RESPONSE");
            var data=response.Data;
            if (data.campaignId!=intent.campaignId || !GiftCodeIntent.CanonicalCount(data.count) ||
                string.CompareOrdinal(data.count.PadLeft(19,'0'),intent.expectedCount.PadLeft(19,'0'))<=0) return Failure("INVALID_RESPONSE");
            if (!data.alreadyApplied && (data.count != checked(long.Parse(intent.expectedCount,System.Globalization.CultureInfo.InvariantCulture)+1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                || data.configRevision!=intent.configRevision)) return Failure("INVALID_RESPONSE");
            if (data.playerValues!=null)
            {
                if (valuesEpoch!=values.CacheEpoch || !values.AcceptServerState(userId,data.playerValues)) return Failure("STALE_RESPONSE");
                data.playerValues=values.CurrentState;
            }
            if (data.alreadyApplied || data.grantedUnlocks.Length>0) PlayerProfileManager.Instance.ClearCache();
            HorizonApp.Events.Publish(EventKeys.GiftCodeRedeemed,data);
            return data;
        }
        private RedeemGiftCodeResponse Failure(string code) { LastErrorCode=code; return null; }
        public async Task<bool?> Validate(string code)
        {
            LastErrorCode=null;
            if (string.IsNullOrWhiteSpace(code) || code.Length>50) { Failure("INVALID_REQUEST"); return null; }
            var response=await HorizonApp.Network.PostAsync<ValidateGiftCodeResponse>("/api/v1/app/gift-codes/validate",new ValidateGiftCodeRequest {code=code},useSessionToken:false);
            if (!response.IsSuccess || response.Data==null) { Failure(response.ErrorCode ?? "INVALID_RESPONSE"); return null; }
            HorizonApp.Events.Publish(EventKeys.GiftCodeValidated,response.Data.valid); return response.Data.valid;
        }
    }
}
