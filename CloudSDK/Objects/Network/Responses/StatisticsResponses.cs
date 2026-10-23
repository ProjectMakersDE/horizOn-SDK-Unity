using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    [Serializable]
    public class StatisticDefinition
    {
        public string key;
        public string name;
        public string mode;
        public string reset;
        // The server omits an absent bound; only positive bounds exist on the wire.
        public long maxPerUpdate;
        public bool HasMaxPerUpdate => maxPerUpdate > 0;
    }

    [Serializable]
    public class StatisticValue
    {
        public string key;
        public long value;
        public bool hasValue;
        public string period;
    }

    [Serializable]
    public class StatisticsOperationWindow
    {
        public long serverTimeUnixMillis;
        public long maxAgeSeconds;
        public long maxFutureSkewSeconds;
    }

    [Serializable]
    public class StatisticsDefinitionsResponse
    {
        public long definitionVersion;
        public StatisticDefinition[] definitions;
    }

    [Serializable]
    public class StatisticsSnapshot
    {
        public string userId;
        public long definitionVersion;
        public StatisticDefinition[] definitions;
        public long revision;
        public StatisticValue[] values;
        public StatisticsOperationWindow operationWindow;
        public string operationId;
        public bool replayed;
        public string[] newlyUnlocked;
    }

    public static class StatisticsErrorCodes
    {
        public const string SessionRequired = "SESSION_REQUIRED";
        public const string SessionForbidden = "SESSION_FORBIDDEN";
        public const string SessionChanged = "SESSION_CHANGED";
        public const string InvalidRequest = "INVALID_REQUEST";
        public const string InvalidResponse = "INVALID_RESPONSE";
        public const string OperationConflict = "OPERATION_CONFLICT";
        public const string OperationExpired = "OPERATION_EXPIRED";
        public const string OperationInFuture = "OPERATION_IN_FUTURE";
        public const string StateConflict = "STATE_CONFLICT";
        public const string UnknownStatistic = "UNKNOWN_STATISTIC";
        public const string UpdateAboveLimit = "UPDATE_ABOVE_LIMIT";
        public const string ValueOverflow = "VALUE_OVERFLOW";
        public const string RateLimited = "RATE_LIMITED";

        public static string FromHttpStatus(long status) => status == 401 ? SessionRequired
            : status == 403 ? SessionForbidden : status == 409 ? StateConflict
            : status == 410 ? OperationExpired : status == 429 ? RateLimited
            : status == 0 ? "NETWORK_ERROR" : "HTTP_" + status;
    }
}
