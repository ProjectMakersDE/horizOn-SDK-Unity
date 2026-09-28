using System;
using System.Collections.Generic;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;

namespace PM.horizOn.Cloud.Transport
{
    internal sealed class LeaderboardSubmitPlan
    {
        internal string Endpoint { get; }
        internal SubmitScoreRequest Request { get; }
        internal bool UseSessionToken => true;

        internal LeaderboardSubmitPlan(string endpoint, SubmitScoreRequest request)
        {
            Endpoint = endpoint;
            Request = request;
        }
    }

    internal static class LeaderboardTransportContract
    {
        internal static bool TryCreateSubmitPlan(
            UserData user,
            string transportSessionToken,
            long score,
            string boardKey,
            out LeaderboardSubmitPlan plan)
        {
            plan = null;
            if (user == null || string.IsNullOrEmpty(user.UserId) || string.IsNullOrEmpty(user.AccessToken))
            {
                return false;
            }
            if (string.IsNullOrEmpty(transportSessionToken) ||
                !string.Equals(user.AccessToken, transportSessionToken, StringComparison.Ordinal))
            {
                return false;
            }

            string normalizedBoardKey = string.IsNullOrWhiteSpace(boardKey) ? null : boardKey.Trim();
            string endpoint = normalizedBoardKey == null
                ? "/api/v1/app/leaderboard/submit"
                : $"/api/v1/app/leaderboards/{Uri.EscapeDataString(normalizedBoardKey)}/submit";
            plan = new LeaderboardSubmitPlan(
                endpoint,
                new SubmitScoreRequest
                {
                    userId = user.UserId,
                    score = score,
                    leaderboardKey = normalizedBoardKey
                });
            return true;
        }
    }

    internal static class HorizonRequestHeaders
    {
        internal static IReadOnlyDictionary<string, string> Create(
            string projectKey,
            string sessionToken,
            bool useSessionToken)
        {
            var headers = new Dictionary<string, string>
            {
                ["X-API-Key"] = projectKey ?? string.Empty
            };
            if (useSessionToken && !string.IsNullOrEmpty(sessionToken))
            {
                headers["Authorization"] = $"Bearer {sessionToken}";
            }
            return headers;
        }
    }
}
