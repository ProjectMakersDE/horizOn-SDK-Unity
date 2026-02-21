namespace PM.horizOn.Cloud.Enums
{
    /// <summary>
    /// Event keys for the horizOn SDK event system.
    /// Organized by category with numeric ranges.
    /// </summary>
    public enum EventKeys
    {
        // Connection Events (0-99)
        ConnectionStarted = 0,
        ConnectionSuccess = 1,
        ConnectionFailed = 2,
        ConnectionLost = 3,
        HostSelected = 4,
        HostPingComplete = 5,

        // Authentication Events (100-199)
        UserSignUpRequested = 100,
        UserSignUpSuccess = 101,
        UserSignUpFailed = 102,
        UserSignInRequested = 103,
        UserSignInSuccess = 104,
        UserSignInFailed = 105,
        UserAuthCheckSuccess = 106,
        UserAuthCheckFailed = 107,
        UserSignOutSuccess = 108,
        UserEmailVerified = 109,
        UserPasswordResetRequested = 110,
        UserPasswordResetSuccess = 111,

        // Data Change Events (200-299) - Trigger persistence
        UserDataChanged = 200,
        CloudSaveDataChanged = 201,
        ConfigDataChanged = 202,
        LeaderboardDataChanged = 203,

        // Data Load Events (300-399) - Distribute loaded data
        UserDataLoaded = 300,
        CloudSaveDataLoaded = 301,
        ConfigDataLoaded = 302,
        LeaderboardDataLoaded = 303,
        NewsDataLoaded = 304,
        CloudSaveBytesLoaded = 305,

        // Feature Events (400-499)
        GiftCodeRedeemed = 400,
        GiftCodeValidated = 401,
        FeedbackSubmitted = 402,
        LogCreated = 403,

        // Crash Reporting Events
        CrashReported = 410,
        CrashReportFailed = 411,
        CrashSessionRegistered = 412,
        BreadcrumbRecorded = 413,

        // Network Events (500-599)
        NetworkRequestStarted = 500,
        NetworkRequestSuccess = 501,
        NetworkRequestFailed = 502,
        NetworkRateLimited = 503,
        NetworkRetryAttempt = 504,

        // Cache Events (600-699)
        CacheHit = 600,
        CacheMiss = 601,
        CacheUpdated = 602,
        CacheCleared = 603,
        CacheExpired = 604,

        // Error Events (700-799)
        ErrorOccurred = 700,
        WarningOccurred = 701,
        ValidationFailed = 702,

        // System Events (800-899)
        SDKInitialized = 800,
        SDKShutdown = 801,
        ServiceInitialized = 802,
        ManagerInitialized = 803,
    }
}
