using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    [Serializable] public class MatchLimits { public const int MaxStateBytes = 14648 * 1024; public int activeMatches, stateBytes, retainedMatches, pendingInvites, settlementMatches, historyRetentionSeconds, rankedSettlementSeconds; }
    [Serializable] public class MatchCapabilities { public bool matches, matchesRanked, realtime; }
    [Serializable] public class MatchConfig { public long revision; public string rankedLeaderboardKey; }
    [Serializable] public class MatchLimitsView
    {
        public MatchLimits limits; public MatchConfig config; public MatchCapabilities capabilities;
        public int pollAfterSeconds; public string serverTime;
    }
    [Serializable] public class MatchSummary
    {
        public string id, name, creatorId, visibility, status, currentPlayerId, turnDeadline, createdAt, startedAt, finishedAt, purgeAt;
        public long revision, strength; public long? finalStateRevision; public int capacity;
        public string[] participantIds = Array.Empty<string>(), invitedIds = Array.Empty<string>(), forfeitedIds = Array.Empty<string>();
        public bool hasDisagreement, ranked, timeoutDue;
    }
    [Serializable] public class MatchReport
    {
        public string userId, resultBase64, reportedAt; public long finalStateRevision;
        public string[] winnerIds = Array.Empty<string>(); public byte[] result = Array.Empty<byte>();
    }
    [Serializable] public class MatchView : MatchSummary
    {
        public string[] turnOrder = Array.Empty<string>(); public int turnSeconds;
        public string timeoutPolicy, dataBase64, reportDeadline, absoluteDeadline, rankedLeaderboardKey, ownRankedRunId;
        /// <summary>Null is an invite/removal metadata-only view; an accepted player's empty state is byte[0].</summary>
        public byte[] data;
        public MatchReport[] reports = Array.Empty<MatchReport>(); public bool notificationPending;
    }
    [Serializable] public class MatchPendingResult { public string matchId, ownRankedRunId, expiresAt; public long finalStateRevision; }
    [Serializable] public class MyMatches
    {
        public string userId, serverTime; public long revision; public MatchLimits limits; public int pollAfterSeconds;
        public MatchSummary[] active = Array.Empty<MatchSummary>(), invites = Array.Empty<MatchSummary>(), recent = Array.Empty<MatchSummary>();
        public MatchPendingResult[] pendingResults = Array.Empty<MatchPendingResult>();
    }
    [Serializable] public class MatchPage
    {
        public MatchSummary[] matches = Array.Empty<MatchSummary>(); public string nextCursor, serverTime; public int pollAfterSeconds;
    }
    [Serializable] public class MatchRead { public MatchView match; public int pollAfterSeconds; public string serverTime; }
    [Serializable] public class MatchCreated : MatchRead { public long playerRevision; }
    [Serializable] public class MatchReported : MatchRead { public bool replayed; }
    [Serializable] public class MatchCreateInput
    {
        public string name, visibility = "open", timeoutPolicy = "skip";
        public int capacity = 2, turnSeconds = 3600; public long strength; public bool ranked;
        public byte[] data = Array.Empty<byte>(); public string[] inviteeIds = Array.Empty<string>();
    }
    public class MatchesResult<T> where T : class
    {
        public bool IsSuccess { get; internal set; }
        public T Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
        public string Identity { get; internal set; }
        public double RetryAfterSeconds { get; internal set; }
        /// <summary>Actual HTTP status; zero for local validation/context/cadence failures.</summary>
        public long HttpStatusCode { get; internal set; }
    }
    internal sealed class MatchRankedRunResponse : ValidatedRun { }
    internal sealed class MatchesJsonRequest
    {
        internal readonly string Json;
        internal MatchesJsonRequest(string json) { Json = json; }
    }
}
