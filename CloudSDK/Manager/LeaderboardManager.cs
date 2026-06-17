using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Manager for leaderboard functionality.
    /// Supports score submission, ranking, and leaderboard queries.
    /// </summary>
    public class LeaderboardManager : BaseManager<LeaderboardManager>
    {
        private Dictionary<string, List<SimpleLeaderboardEntry>> _leaderboardCache = new Dictionary<string, List<SimpleLeaderboardEntry>>();

        private string BuildEndpoint(string boardKey, string action)
        {
            if (string.IsNullOrEmpty(boardKey))
            {
                return $"/api/v1/app/leaderboard/{action}";
            }

            return $"/api/v1/app/leaderboards/{Uri.EscapeDataString(boardKey)}/{action}";
        }

        private string BuildCacheKey(string boardKey, string action, int value)
        {
            var normalizedBoardKey = string.IsNullOrEmpty(boardKey) ? "default" : boardKey;
            return $"{normalizedBoardKey}:{action}:{value}";
        }

        /// <summary>
        /// Submit a score to a leaderboard.
        /// Score is only updated if it's higher than the previous best.
        /// </summary>
        /// <param name="score">Score value</param>
        /// <param name="metadata">Optional metadata JSON string</param>
        /// <param name="boardKey">Optional board key for multi-board leaderboards</param>
        /// <returns>True if submission succeeded, false otherwise</returns>
        public async Task<bool> SubmitScore(long score, string metadata = null, string boardKey = null)
        {
            if (!PM.horizOn.Cloud.Manager.UserManager.Instance.IsSignedIn)
            {
                HorizonApp.Log.Error("User must be signed in to submit score");
                return false;
            }

            var user = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser;

            var request = new SubmitScoreRequest
            {
                userId = user.UserId,
                score = score,
                leaderboardKey = string.IsNullOrEmpty(boardKey) ? null : boardKey,
            };

            var response = await HorizonApp.Network.PostAsync<SubmitScoreResponse>(
                BuildEndpoint(boardKey, "submit"),
                request,
                useSessionToken: false
            );

            if (response.IsSuccess)
            {
                HorizonApp.Log.Info($"Score submitted: {score}");
                HorizonApp.Events.Publish(EventKeys.LeaderboardDataChanged, score);

                // Invalidate cache
                _leaderboardCache.Clear();

                return true;
            }
            else
            {
                HorizonApp.Log.Error($"Score submission failed: {response.Error}");
                return false;
            }
        }

        /// <summary>
        /// Get top entries from the leaderboard.
        /// </summary>
        /// <param name="limit">Number of entries to retrieve (max 100)</param>
        /// <param name="useCache">Whether to use cached data if available</param>
        /// <param name="boardKey">Optional board key for multi-board leaderboards</param>
        /// <returns>List of leaderboard entries, or null if failed</returns>
        public async Task<List<SimpleLeaderboardEntry>> GetTop(int limit = 10, bool useCache = true, string boardKey = null)
        {
            if (!PM.horizOn.Cloud.Manager.UserManager.Instance.IsSignedIn)
            {
                HorizonApp.Log.Error("User must be signed in to get leaderboard");
                return null;
            }

            if (limit > 100)
            {
                HorizonApp.Log.Warning("Limit capped at 100 entries");
                limit = 100;
            }

            // Check cache
            string cacheKey = BuildCacheKey(boardKey, "top", limit);
            if (useCache && _leaderboardCache.ContainsKey(cacheKey))
            {
                HorizonApp.Events.Publish(EventKeys.CacheHit, cacheKey);
                return new List<SimpleLeaderboardEntry>(_leaderboardCache[cacheKey]);
            }

            string userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId;

            var response = await HorizonApp.Network.GetAsync<AppLeaderboardTopResponse>(
                $"{BuildEndpoint(boardKey, "top")}?userId={userId}&limit={limit}",
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null && response.Data.entries != null)
            {
                var entries = new List<SimpleLeaderboardEntry>(response.Data.entries);

                // Cache the results
                _leaderboardCache[cacheKey] = entries;

                HorizonApp.Log.Info($"Loaded top {entries.Count} entries");
                HorizonApp.Events.Publish(EventKeys.LeaderboardDataLoaded, entries);

                return entries;
            }
            else
            {
                HorizonApp.Log.Error($"Failed to get top leaderboard entries: {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// Get the current user's rank in the leaderboard.
        /// </summary>
        /// <param name="boardKey">Optional board key for multi-board leaderboards</param>
        /// <returns>Rank response, or null if failed</returns>
        public async Task<AppUserRankResponse> GetRank(string boardKey = null)
        {
            if (!PM.horizOn.Cloud.Manager.UserManager.Instance.IsSignedIn)
            {
                HorizonApp.Log.Error("User must be signed in to get rank");
                return null;
            }

            string userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId;

            var response = await HorizonApp.Network.GetAsync<AppUserRankResponse>(
                $"{BuildEndpoint(boardKey, "rank")}?userId={userId}",
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null)
            {
                HorizonApp.Log.Info($"User rank: {response.Data.position} (Score: {response.Data.score})");
                return response.Data;
            }
            else
            {
                HorizonApp.Log.Error($"Failed to get rank: {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// Get leaderboard entries around the current user's position.
        /// </summary>
        /// <param name="range">Number of entries before and after the user (default 10)</param>
        /// <param name="useCache">Whether to use cached data if available</param>
        /// <param name="boardKey">Optional board key for multi-board leaderboards</param>
        /// <returns>List of leaderboard entries, or null if failed</returns>
        public async Task<List<SimpleLeaderboardEntry>> GetAround(int range = 10, bool useCache = true, string boardKey = null)
        {
            if (!PM.horizOn.Cloud.Manager.UserManager.Instance.IsSignedIn)
            {
                HorizonApp.Log.Error("User must be signed in to get leaderboard");
                return null;
            }

            // Check cache
            string cacheKey = BuildCacheKey(boardKey, "around", range);
            if (useCache && _leaderboardCache.ContainsKey(cacheKey))
            {
                HorizonApp.Events.Publish(EventKeys.CacheHit, cacheKey);
                return new List<SimpleLeaderboardEntry>(_leaderboardCache[cacheKey]);
            }

            string userId = PM.horizOn.Cloud.Manager.UserManager.Instance.CurrentUser.UserId;

            var response = await HorizonApp.Network.GetAsync<AppLeaderboardAroundResponse>(
                $"{BuildEndpoint(boardKey, "around")}?userId={userId}&range={range}",
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null && response.Data.entries != null)
            {
                var entries = new List<SimpleLeaderboardEntry>(response.Data.entries);

                // Cache the results
                _leaderboardCache[cacheKey] = entries;

                HorizonApp.Log.Info($"Loaded {entries.Count} entries around user");
                HorizonApp.Events.Publish(EventKeys.LeaderboardDataLoaded, entries);

                return entries;
            }
            else
            {
                HorizonApp.Log.Error($"Failed to get leaderboard entries around user: {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// List all active leaderboard boards for this app.
        /// </summary>
        /// <returns>List of leaderboard boards, or null if failed</returns>
        public async Task<List<LeaderboardBoardResponse>> ListBoards()
        {
            var response = await HorizonApp.Network.GetAsync<LeaderboardListResponseV2>(
                "/api/v1/app/leaderboards",
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null && response.Data.boards != null)
            {
                return new List<LeaderboardBoardResponse>(response.Data.boards);
            }

            HorizonApp.Log.Error($"Failed to list leaderboard boards: {response.Error}");
            return null;
        }

        /// <summary>
        /// Clear the leaderboard cache.
        /// </summary>
        public void ClearCache()
        {
            _leaderboardCache.Clear();
            HorizonApp.Log.Info("Leaderboard cache cleared");
            HorizonApp.Events.Publish(EventKeys.CacheCleared, "Leaderboard");
        }
    }
}
