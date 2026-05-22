using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// Simple leaderboard entry.
    /// </summary>
    [Serializable]
    public class SimpleLeaderboardEntry
    {
        public long position;
        public string username;
        public long score;
    }

    /// <summary>
    /// Response object for submitting a score.
    /// </summary>
    [Serializable]
    public class SubmitScoreResponse
    {
        // API returns empty body on success (200 OK)
    }

    /// <summary>
    /// Response containing top leaderboard entries.
    /// </summary>
    [Serializable]
    public class AppLeaderboardTopResponse
    {
        public SimpleLeaderboardEntry[] entries;
    }

    /// <summary>
    /// Response containing user rank information.
    /// </summary>
    [Serializable]
    public class AppUserRankResponse
    {
        public long position;
        public string username;
        public long score;
    }

    /// <summary>
    /// Response containing leaderboard entries around user.
    /// </summary>
    [Serializable]
    public class AppLeaderboardAroundResponse
    {
        public SimpleLeaderboardEntry[] entries;
    }

    /// <summary>
    /// Leaderboard board metadata for multi-board leaderboards.
    /// </summary>
    [Serializable]
    public class LeaderboardBoardResponse
    {
        public string id;
        public string apiKeyId;
        public string key;
        public string name;
        public string sortOrder;
        public bool isActive;
        public long scoreCount;
        public string createdAt;
        public string updatedAt;
    }

    /// <summary>
    /// Response containing available leaderboard boards.
    /// </summary>
    [Serializable]
    public class LeaderboardListResponseV2
    {
        public LeaderboardBoardResponse[] boards;
        public long totalElements;
    }
}
