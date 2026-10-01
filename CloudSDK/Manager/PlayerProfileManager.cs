using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using PM.horizOn.Cloud.Transport;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Manager for the player profile: avatar, frame and up to 3 badges, the player's unlocks
    /// and the cosmetics catalog of the API key.
    /// Both calls need a signed-in player; the request carries the player's Bearer session.
    /// </summary>
    public class PlayerProfileManager : BaseManager<PlayerProfileManager>
    {
        private PlayerProfileResponse _currentProfile;

        /// <summary>
        /// Last successful result of <see cref="GetProfile"/> or <see cref="SetProfile"/>.
        /// Null before the first call, after <see cref="ClearCache"/>, after sign-out and when
        /// another player signed in.
        /// </summary>
        public PlayerProfileResponse CurrentProfile
        {
            get
            {
                if (_currentProfile != null && !BelongsToCurrentUser(_currentProfile))
                {
                    _currentProfile = null;
                }
                return _currentProfile;
            }
        }

        /// <summary>
        /// Error code of the last failed call: the server <c>code</c> (for example
        /// <c>COSMETIC_LOCKED</c>), <c>SESSION_REQUIRED</c> when no player is signed in, or an
        /// HTTP fallback code (see <see cref="PlayerProfileErrorCodes"/>). Null after a success.
        /// </summary>
        public string LastErrorCode { get; private set; }

        /// <summary>
        /// Load profile, unlocks and cosmetics catalog of the signed-in player.
        /// No time-based cache: every call asks the server, so new unlocks (for example from a
        /// gift code) show up right away.
        /// </summary>
        /// <returns>The response, or null on failure (then <see cref="LastErrorCode"/> is set)</returns>
        public async Task<PlayerProfileResponse> GetProfile()
        {
            if (!PlayerProfileTransportContract.TryCreateGetPlan(
                    UserManager.Instance.CurrentUser,
                    HorizonApp.Network.GetSessionToken(),
                    out var plan))
            {
                return FailLocally(PlayerProfileErrorCodes.SessionRequired, "User must be signed in to load the player profile");
            }

            var response = await HorizonApp.Network.GetAsync<PlayerProfileResponse>(
                plan.Endpoint,
                useSessionToken: plan.UseSessionToken
            );

            if (!TryAccept(response, "load", out var profile))
            {
                return null;
            }

            HorizonApp.Log.Info($"Player profile loaded ({profile.cosmetics?.Length ?? 0} cosmetics, {profile.unlocks?.Length ?? 0} unlocks)");
            HorizonApp.Events.Publish(EventKeys.PlayerProfileLoaded, profile);
            return profile;
        }

        /// <summary>
        /// Replace the whole visible profile of the signed-in player.
        /// Pass the current values for slots you do not want to change.
        /// On success the leaderboard cache is cleared, so the next GetTop / GetAround shows the change.
        /// </summary>
        /// <param name="avatarId">Avatar ID; null or "" clears the slot</param>
        /// <param name="frameId">Frame ID; null or "" clears the slot</param>
        /// <param name="badges">Up to 3 distinct badge IDs, order kept; null or empty clears all badges</param>
        /// <returns>The updated response, or null on failure (then <see cref="LastErrorCode"/> is set)</returns>
        public async Task<PlayerProfileResponse> SetProfile(string avatarId, string frameId, IList<string> badges)
        {
            if (!PlayerProfileTransportContract.TryCreateSetPlan(
                    UserManager.Instance.CurrentUser,
                    HorizonApp.Network.GetSessionToken(),
                    avatarId,
                    frameId,
                    badges,
                    out var plan,
                    out var localError))
            {
                string message = localError == PlayerProfileErrorCodes.SessionRequired
                    ? "User must be signed in to set the player profile"
                    : $"Player profile rejected before sending: {localError}";
                return FailLocally(localError, message);
            }

            var response = await HorizonApp.Network.PutAsync<PlayerProfileResponse>(
                PlayerProfileSetPlan.Endpoint,
                plan.Request,
                useSessionToken: plan.UseSessionToken
            );

            if (!TryAccept(response, "update", out var profile))
            {
                return null;
            }

            HorizonApp.Log.Info("Player profile updated");

            // Cached leaderboard pages still carry the old profile of this player.
            LeaderboardManager.Instance.ClearCache();

            HorizonApp.Events.Publish(EventKeys.PlayerProfileChanged, profile);
            return profile;
        }

        /// <summary>
        /// Drop the cached <see cref="CurrentProfile"/>. The SDK calls this after a gift code
        /// redemption that granted unlocks, so the next <see cref="GetProfile"/> shows them.
        /// </summary>
        public void ClearCache()
        {
            _currentProfile = null;
            HorizonApp.Log.Info("Player profile cache cleared");
            HorizonApp.Events.Publish(EventKeys.CacheCleared, "PlayerProfile");
        }

        private bool TryAccept(NetworkResponse<PlayerProfileResponse> response, string action, out PlayerProfileResponse profile)
        {
            profile = null;

            if (response.IsSuccess && response.Data != null)
            {
                profile = response.Data;
                _currentProfile = profile;
                LastErrorCode = null;
                return true;
            }

            LastErrorCode = !response.IsSuccess
                ? (!string.IsNullOrEmpty(response.ErrorCode)
                    ? response.ErrorCode
                    : PlayerProfileErrorCodes.FromHttpStatus(response.StatusCode))
                : PlayerProfileErrorCodes.InvalidResponse;

            HorizonApp.Log.Error($"Player profile {action} failed ({LastErrorCode}): {response.Error}");
            return false;
        }

        private PlayerProfileResponse FailLocally(string errorCode, string message)
        {
            LastErrorCode = errorCode;
            HorizonApp.Log.Error(message);
            return null;
        }

        private static bool BelongsToCurrentUser(PlayerProfileResponse profile)
        {
            var user = UserManager.Instance.CurrentUser;
            return user != null &&
                   user.IsValid() &&
                   string.Equals(user.UserId, profile.userId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
