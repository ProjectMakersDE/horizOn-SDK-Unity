using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Transport
{
    /// <summary>
    /// GET /api/v1/app/player-profile?userId=... for the signed-in player.
    /// </summary>
    internal sealed class PlayerProfileGetPlan
    {
        internal string Endpoint { get; }

        /// <summary>
        /// Both player profile endpoints need the player's Bearer session.
        /// </summary>
        internal bool UseSessionToken => true;

        internal PlayerProfileGetPlan(string endpoint)
        {
            Endpoint = endpoint;
        }
    }

    /// <summary>
    /// PUT /api/v1/app/player-profile for the signed-in player.
    /// </summary>
    internal sealed class PlayerProfileSetPlan
    {
        internal const string Endpoint = PlayerProfileTransportContract.Endpoint;

        internal SetPlayerProfileRequest Request { get; }

        internal bool UseSessionToken => true;

        internal PlayerProfileSetPlan(SetPlayerProfileRequest request)
        {
            Request = request;
        }
    }

    internal static class PlayerProfileTransportContract
    {
        internal const string Endpoint = "/api/v1/app/player-profile";

        /// <summary>Maximum number of displayed badges (server limit, checked again there).</summary>
        internal const int MaxBadges = 3;

        private static readonly Regex CosmeticIdPattern = new Regex(
            @"^[a-z0-9][a-z0-9._-]{0,31}\z",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// True when the ID matches the cosmetic ID format of the server.
        /// </summary>
        internal static bool IsValidCosmeticId(string id)
        {
            return !string.IsNullOrEmpty(id) && CosmeticIdPattern.IsMatch(id);
        }

        /// <summary>
        /// Builds the GET plan only for a signed-in user whose access token is the session token
        /// the transport will send. Returns false otherwise, so no request without a matching
        /// session reaches the server (the caller reports SESSION_REQUIRED).
        /// </summary>
        internal static bool TryCreateGetPlan(
            UserData user,
            string transportSessionToken,
            out PlayerProfileGetPlan plan)
        {
            plan = null;
            if (!HasMatchingSession(user, transportSessionToken))
            {
                return false;
            }

            plan = new PlayerProfileGetPlan($"{Endpoint}?userId={Uri.EscapeDataString(user.UserId)}");
            return true;
        }

        /// <summary>
        /// Builds the PUT plan. PUT replaces the whole profile: a null or empty avatarId / frameId
        /// clears the slot, null or empty badges clear all badges. IDs are trimmed like on the server.
        /// Local pre-checks (no request on failure): session (SESSION_REQUIRED), more than
        /// 3 badges or a duplicate badge (INVALID_BADGES), an ID with a wrong format
        /// (INVALID_COSMETIC_ID). The server checks everything again.
        /// </summary>
        /// <param name="errorCode">The failed check, null on success</param>
        internal static bool TryCreateSetPlan(
            UserData user,
            string transportSessionToken,
            string avatarId,
            string frameId,
            IList<string> badges,
            out PlayerProfileSetPlan plan,
            out string errorCode)
        {
            plan = null;
            errorCode = null;

            if (!HasMatchingSession(user, transportSessionToken))
            {
                errorCode = PlayerProfileErrorCodes.SessionRequired;
                return false;
            }

            var badgeList = new List<string>();
            if (badges != null)
            {
                if (badges.Count > MaxBadges)
                {
                    errorCode = PlayerProfileErrorCodes.InvalidBadges;
                    return false;
                }

                // Same order as the server: badge count and duplicates first, then the ID format.
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var badge in badges)
                {
                    string trimmed = badge?.Trim() ?? string.Empty;
                    if (!seen.Add(trimmed))
                    {
                        errorCode = PlayerProfileErrorCodes.InvalidBadges;
                        return false;
                    }
                    badgeList.Add(trimmed);
                }
            }

            string normalizedAvatar = NormalizeSlot(avatarId);
            string normalizedFrame = NormalizeSlot(frameId);
            if ((normalizedAvatar != null && !IsValidCosmeticId(normalizedAvatar)) ||
                (normalizedFrame != null && !IsValidCosmeticId(normalizedFrame)))
            {
                errorCode = PlayerProfileErrorCodes.InvalidCosmeticId;
                return false;
            }
            foreach (var badge in badgeList)
            {
                if (!IsValidCosmeticId(badge))
                {
                    errorCode = PlayerProfileErrorCodes.InvalidCosmeticId;
                    return false;
                }
            }

            plan = new PlayerProfileSetPlan(
                new SetPlayerProfileRequest
                {
                    userId = user.UserId,
                    avatarId = normalizedAvatar,
                    frameId = normalizedFrame,
                    badges = badgeList.ToArray()
                });
            return true;
        }

        /// <summary>
        /// Trims like the server; an empty or whitespace-only slot becomes null (clears it).
        /// </summary>
        private static string NormalizeSlot(string id)
        {
            string trimmed = id?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        private static bool HasMatchingSession(UserData user, string transportSessionToken)
        {
            if (user == null || string.IsNullOrEmpty(user.UserId) || string.IsNullOrEmpty(user.AccessToken))
            {
                return false;
            }
            return !string.IsNullOrEmpty(transportSessionToken) &&
                   string.Equals(user.AccessToken, transportSessionToken, StringComparison.Ordinal);
        }
    }
}
