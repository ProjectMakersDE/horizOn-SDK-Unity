using System;
using System.Collections.Generic;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// The visible part of a player profile: what leaderboards show next to name and score.
    /// The server stores IDs only, the game maps them to its own assets.
    /// An empty <see cref="avatarId"/> or <see cref="frameId"/> means "not set".
    /// Treat IDs your game does not know as "not set" as well.
    /// </summary>
    [Serializable]
    public class HorizonPlayerProfile
    {
        /// <summary>Selected avatar ID, empty when not set.</summary>
        public string avatarId = string.Empty;

        /// <summary>Selected frame ID, empty when not set.</summary>
        public string frameId = string.Empty;

        /// <summary>Displayed badge IDs (0 to 3), order as chosen by the player.</summary>
        public string[] badges = new string[0];

        /// <summary>True when an avatar is selected.</summary>
        public bool HasAvatar => !string.IsNullOrEmpty(avatarId);

        /// <summary>True when a frame is selected.</summary>
        public bool HasFrame => !string.IsNullOrEmpty(frameId);
    }

    /// <summary>
    /// One entry of the cosmetics catalog of the API key.
    /// </summary>
    [Serializable]
    public class PlayerCosmetic
    {
        /// <summary>Cosmetic ID, for example "avatar.zombie_07".</summary>
        public string id;

        /// <summary>"avatar", "frame" or "badge".</summary>
        public string type;

        /// <summary>True when the cosmetic needs an unlock.</summary>
        public bool locked;

        /// <summary>True when the player may select it now (free or unlocked).</summary>
        public bool available;
    }

    /// <summary>
    /// Limits of the player profile feature.
    /// </summary>
    [Serializable]
    public class PlayerProfileLimits
    {
        /// <summary>Maximum number of displayed badges (3).</summary>
        public int maxBadges = 3;

        /// <summary>Maximum number of unlocks per player (25).</summary>
        public int maxUnlocks = 25;
    }

    /// <summary>
    /// Response of GET and PUT /api/v1/app/player-profile: the profile, the player's unlocks
    /// and the full catalog of the API key with an "available" flag per entry.
    /// </summary>
    [Serializable]
    public class PlayerProfileResponse
    {
        /// <summary>The player.</summary>
        public string userId;

        /// <summary>Current selection.</summary>
        public HorizonPlayerProfile profile = new HorizonPlayerProfile();

        /// <summary>Owned locked cosmetics. May contain IDs that were deleted from the catalog.</summary>
        public string[] unlocks = new string[0];

        /// <summary>Catalog of the API key, sorted by id.</summary>
        public PlayerCosmetic[] cosmetics = new PlayerCosmetic[0];

        /// <summary>Feature limits.</summary>
        public PlayerProfileLimits limits = new PlayerProfileLimits();

        /// <summary>
        /// All catalog entries of one type.
        /// </summary>
        /// <param name="type">"avatar", "frame" or "badge"</param>
        /// <returns>Matching entries in catalog order, never null</returns>
        public List<PlayerCosmetic> GetCosmetics(string type)
        {
            var result = new List<PlayerCosmetic>();
            if (cosmetics == null)
            {
                return result;
            }

            foreach (var cosmetic in cosmetics)
            {
                if (cosmetic != null && string.Equals(cosmetic.type, type, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(cosmetic);
                }
            }
            return result;
        }

        /// <summary>
        /// True when the cosmetic is in the catalog and the player may select it now.
        /// </summary>
        /// <param name="id">Cosmetic ID</param>
        public bool IsAvailable(string id)
        {
            if (string.IsNullOrEmpty(id) || cosmetics == null)
            {
                return false;
            }

            foreach (var cosmetic in cosmetics)
            {
                if (cosmetic != null && string.Equals(cosmetic.id, id, StringComparison.Ordinal))
                {
                    return cosmetic.available;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Stable error codes of the player profile feature. Switch on these, never on the message.
    /// Read the code of the last failure from <c>PlayerProfileManager.Instance.LastErrorCode</c>.
    /// </summary>
    public static class PlayerProfileErrorCodes
    {
        /// <summary>No signed-in player (checked locally, no request) or the server rejected the session (401).</summary>
        public const string SessionRequired = "SESSION_REQUIRED";

        /// <summary>The session belongs to another player, account or API key (403).</summary>
        public const string SessionForbidden = "SESSION_FORBIDDEN";

        /// <summary>More than 3 badges, or a badge listed twice (400).</summary>
        public const string InvalidBadges = "INVALID_BADGES";

        /// <summary>An ID does not match ^[a-z0-9][a-z0-9._-]{0,31}$ (400).</summary>
        public const string InvalidCosmeticId = "INVALID_COSMETIC_ID";

        /// <summary>An ID is not in the catalog of the API key (400).</summary>
        public const string CosmeticNotFound = "COSMETIC_NOT_FOUND";

        /// <summary>An ID exists with another type than its slot (400).</summary>
        public const string CosmeticTypeMismatch = "COSMETIC_TYPE_MISMATCH";

        /// <summary>A locked cosmetic the player has not unlocked (403).</summary>
        public const string CosmeticLocked = "COSMETIC_LOCKED";

        /// <summary>Player missing, deleted, inactive or of another API key (404).</summary>
        public const string PlayerNotFound = "PLAYER_NOT_FOUND";

        /// <summary>A gift code would give the player more than 25 unlocks (409).</summary>
        public const string UnlockLimitReached = "UNLOCK_LIMIT_REACHED";

        /// <summary>Fallback for HTTP 400 without a server code (for example bean validation).</summary>
        public const string BadRequest = "BAD_REQUEST";

        /// <summary>Fallback for HTTP 401 without a server code (for example an invalid API key).</summary>
        public const string Unauthorized = "UNAUTHORIZED";

        /// <summary>Fallback for HTTP 403 without a server code.</summary>
        public const string Forbidden = "FORBIDDEN";

        /// <summary>Fallback for HTTP 404 without a server code.</summary>
        public const string NotFound = "NOT_FOUND";

        /// <summary>Fallback for HTTP 409 without a server code.</summary>
        public const string Conflict = "CONFLICT";

        /// <summary>Still rate limited (HTTP 429) after the SDK's retries.</summary>
        public const string RateLimited = "RATE_LIMITED";

        /// <summary>HTTP 5xx after the SDK's retries.</summary>
        public const string ServerError = "SERVER_ERROR";

        /// <summary>No HTTP status (connection error, timeout, SDK not connected).</summary>
        public const string NetworkError = "NETWORK_ERROR";

        /// <summary>HTTP 2xx whose body could not be read as a profile response.</summary>
        public const string InvalidResponse = "INVALID_RESPONSE";

        /// <summary>
        /// Fallback code for a failure without a server <c>code</c>, derived from the HTTP status.
        /// </summary>
        /// <param name="httpStatus">HTTP status, 0 when there was none</param>
        public static string FromHttpStatus(long httpStatus)
        {
            if (httpStatus <= 0) return NetworkError;
            if (httpStatus == 400) return BadRequest;
            if (httpStatus == 401) return Unauthorized;
            if (httpStatus == 403) return Forbidden;
            if (httpStatus == 404) return NotFound;
            if (httpStatus == 409) return Conflict;
            if (httpStatus == 429) return RateLimited;
            if (httpStatus >= 500) return ServerError;
            return $"HTTP_{httpStatus}";
        }
    }
}
