using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// A started validated run (response of POST /api/v1/app/validated-actions/runs).
    /// Seed your deterministic randomness with <see cref="seed"/>, record the input log, and send
    /// the result with <c>ValidatedActionsManager.SubmitValidated</c>. The ticket is single use.
    /// </summary>
    [Serializable]
    public class ValidatedRun
    {
        /// <summary>ID of the run (ticket ID).</summary>
        public string runId;

        /// <summary>Opaque ticket token (about 190 characters). Send it unchanged.</summary>
        public string ticket;

        /// <summary>Server seed for the run, 0 to 2,147,483,646.</summary>
        public int seed;

        /// <summary>Board the ticket is bound to, empty when the run is not bound to a board.</summary>
        public string leaderboardKey = string.Empty;

        /// <summary>Issue time, ISO 8601 UTC.</summary>
        public string issuedAt;

        /// <summary>Expiry time, ISO 8601 UTC. The server decides; the SDK still sends expired runs.</summary>
        public string expiresAt;

        /// <summary>Lifetime of the ticket at issue, in seconds.</summary>
        public int expiresInSeconds;

        /// <summary>True when the ticket is bound to a board.</summary>
        public bool HasLeaderboard => !string.IsNullOrEmpty(leaderboardKey);

        /// <summary>
        /// Expiry as UTC time, or null when <see cref="expiresAt"/> cannot be read.
        /// </summary>
        public DateTime? ExpiresAtUtc
        {
            get
            {
                if (DateTime.TryParse(
                        expiresAt,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                        out DateTime value))
                {
                    return value;
                }
                return null;
            }
        }

        /// <summary>
        /// True when <see cref="expiresAt"/> has passed by the device clock. Informational only:
        /// the device clock may be wrong, so the SDK still submits and the server decides.
        /// </summary>
        public bool IsExpired
        {
            get
            {
                DateTime? expires = ExpiresAtUtc;
                return expires.HasValue && expires.Value <= DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Replaces JSON null values with empty defaults (an unbound ticket has leaderboardKey null).
        /// </summary>
        internal void Normalize()
        {
            if (leaderboardKey == null) leaderboardKey = string.Empty;
        }
    }

    /// <summary>
    /// Result of an accepted validated run (response of POST /api/v1/app/validated-actions/submit).
    /// For a run without a board, <see cref="leaderboardKey"/> is empty and score, best score and
    /// rank are 0.
    /// </summary>
    [Serializable]
    public class ValidatedSubmitResult
    {
        /// <summary>Always true on success.</summary>
        public bool accepted;

        /// <summary>ID of the run.</summary>
        public string runId;

        /// <summary>Board the score was written to, empty for a run without a board.</summary>
        public string leaderboardKey = string.Empty;

        /// <summary>Submitted score (0 for a run without a board).</summary>
        public long score;

        /// <summary>The player's best score on the board after the write (0 without a board).</summary>
        public long bestScore;

        /// <summary>True when the run set a new best score for the player.</summary>
        public bool isNewHighScore;

        /// <summary>1-based rank of the player's row after the write (0 without a board).</summary>
        public long rank;

        /// <summary>Server-measured duration of the run in seconds, rounded down.</summary>
        public long durationSeconds;

        /// <summary>
        /// Server-owned player state after the run (Part 2). Never null; empty
        /// (<see cref="PlayerState.IsEmpty"/>) while the server does not send one.
        /// </summary>
        public PlayerState state = new PlayerState();

        /// <summary>
        /// Evidence request (Part 3). Never null; <see cref="EvidenceRequest.required"/> is false
        /// while the server does not ask for the input log.
        /// </summary>
        public EvidenceRequest evidence = new EvidenceRequest();

        /// <summary>True when the score was written to a board.</summary>
        public bool HasLeaderboard => !string.IsNullOrEmpty(leaderboardKey);

        /// <summary>
        /// Replaces JSON null values with empty defaults, so callers never see null.
        /// </summary>
        internal void Normalize()
        {
            if (leaderboardKey == null) leaderboardKey = string.Empty;
            if (state == null) state = new PlayerState();
            if (state.values == null) state.values = new PlayerStateValue[0];
            if (state.day == null) state.day = string.Empty;
            if (evidence == null) evidence = new EvidenceRequest();
        }
    }

    /// <summary>
    /// Server-owned values of the player (Part 2, TASK-887). Empty in Part 1.
    /// </summary>
    [Serializable]
    public class PlayerState
    {
        /// <summary>UTC day the daily counters belong to, for example "2026-09-29". Empty when unset.</summary>
        public string day = string.Empty;

        /// <summary>One entry per value key, sorted by key. Never null.</summary>
        public PlayerStateValue[] values = new PlayerStateValue[0];

        /// <summary>True when the state carries no values.</summary>
        public bool IsEmpty => values == null || values.Length == 0;

        /// <summary>
        /// Balance of one value key.
        /// </summary>
        /// <param name="key">Value key, for example "gold"</param>
        /// <returns>The balance, 0 when the key is not listed</returns>
        public long GetBalance(string key)
        {
            if (string.IsNullOrEmpty(key) || values == null)
            {
                return 0;
            }

            foreach (var value in values)
            {
                if (value != null && string.Equals(value.key, key, StringComparison.Ordinal))
                {
                    return value.balance;
                }
            }
            return 0;
        }
    }

    /// <summary>
    /// One server-owned value of the player (Part 2, TASK-887).
    /// </summary>
    [Serializable]
    public class PlayerStateValue
    {
        /// <summary>Value key, for example "gold".</summary>
        public string key;

        /// <summary>Current balance.</summary>
        public long balance;

        /// <summary>Amount earned on <see cref="PlayerState.day"/>.</summary>
        public long earnedToday;

        /// <summary>Daily cap, 0 when there is none.</summary>
        public long dailyCap;

        /// <summary>Amount the run asked for (only in submit results, 0 otherwise).</summary>
        public long requested;

        /// <summary>Amount actually credited after the caps (only in submit results, 0 otherwise).</summary>
        public long credited;
    }

    /// <summary>
    /// Request to upload the input log of an accepted run (Part 3, TASK-888).
    /// <see cref="required"/> is false in Part 1.
    /// </summary>
    [Serializable]
    public class EvidenceRequest
    {
        /// <summary>True when the server wants the input log of this run.</summary>
        public bool required;

        /// <summary>Run whose log is requested.</summary>
        public string runId;

        /// <summary>Deadline of the upload, ISO 8601 UTC.</summary>
        public string uploadBefore;

        /// <summary>Maximum size of the log in bytes.</summary>
        public int maxBytes;
    }

    /// <summary>
    /// Data of <c>EventKeys.ValidatedRunRejected</c>: why a validated submit was refused.
    /// </summary>
    [Serializable]
    public class ValidatedRunRejection
    {
        /// <summary>Server code, for example "DURATION_TOO_SHORT" (see <see cref="ValidatedActionsErrorCodes"/>).</summary>
        public string code;

        /// <summary>The run that was submitted.</summary>
        public string runId;

        /// <summary>HTTP status (422 or 403).</summary>
        public long httpStatus;

        /// <summary>True when the ticket is used up and the SDK dropped the current run.</summary>
        public bool runCleared;
    }

    /// <summary>
    /// Stable error codes of Validated Actions. Switch on these, never on the message.
    /// Read the code of the last failure from <c>ValidatedActionsManager.Instance.LastErrorCode</c>.
    /// </summary>
    public static class ValidatedActionsErrorCodes
    {
        // Local codes (no request was sent)

        /// <summary>No signed-in player (checked locally, no request) or the server rejected the session (401).</summary>
        public const string SessionRequired = "SESSION_REQUIRED";

        /// <summary>Submit without a current run (local, no request). Call StartRun first.</summary>
        public const string NoActiveRun = "NO_ACTIVE_RUN";

        /// <summary>The input log hash is not 64 hex characters (local, no request).</summary>
        public const string InvalidInputLogHash = "INVALID_INPUT_LOG_HASH";

        // Server codes

        /// <summary>The session belongs to another player, account or API key (403).</summary>
        public const string SessionForbidden = "SESSION_FORBIDDEN";

        /// <summary>Player missing, deleted, inactive or of another API key (404).</summary>
        public const string PlayerNotFound = "PLAYER_NOT_FOUND";

        /// <summary>Board key unknown for the API key (404). Only "default" is created on first use.</summary>
        public const string LeaderboardNotFound = "LEADERBOARD_NOT_FOUND";

        /// <summary>A leaderboard run without score (400).</summary>
        public const string ScoreRequired = "SCORE_REQUIRED";

        /// <summary>A leaderboard run by a player without display name (400).</summary>
        public const string PlayerNameRequired = "PLAYER_NAME_REQUIRED";

        /// <summary>Malformed or unknown ticket (422). The run is dropped.</summary>
        public const string TicketInvalid = "TICKET_INVALID";

        /// <summary>The ticket expired (422). The run is dropped.</summary>
        public const string TicketExpired = "TICKET_EXPIRED";

        /// <summary>The ticket was issued for another account, API key or player (422). The run is dropped.</summary>
        public const string TicketForeign = "TICKET_FOREIGN";

        /// <summary>The ticket was already used (422). The run is dropped.</summary>
        public const string TicketConsumed = "TICKET_CONSUMED";

        /// <summary>The ticket is bound to another board than the submitted leaderboardKey (422). The run is kept.</summary>
        public const string LeaderboardMismatch = "LEADERBOARD_MISMATCH";

        /// <summary>The rules require a stage and none was sent (422, rule rejection).</summary>
        public const string StageRequired = "STAGE_REQUIRED";

        /// <summary>The rules require a stage and the stage has no rule (422, rule rejection).</summary>
        public const string StageUnknown = "STAGE_UNKNOWN";

        /// <summary>Score above the maximum (422, rule rejection).</summary>
        public const string ScoreAboveMax = "SCORE_ABOVE_MAX";

        /// <summary>Score below the minimum (422, rule rejection).</summary>
        public const string ScoreBelowMin = "SCORE_BELOW_MIN";

        /// <summary>Score above the stage maximum (422, rule rejection).</summary>
        public const string StageScoreAboveMax = "STAGE_SCORE_ABOVE_MAX";

        /// <summary>Score below the stage minimum (422, rule rejection).</summary>
        public const string StageScoreBelowMin = "STAGE_SCORE_BELOW_MIN";

        /// <summary>The server-measured duration is below the minimum (422, rule rejection).</summary>
        public const string DurationTooShort = "DURATION_TOO_SHORT";

        /// <summary>Score per measured second above the maximum (422, rule rejection).</summary>
        public const string ScoreRateTooHigh = "SCORE_RATE_TOO_HIGH";

        /// <summary>The score rows of the API key are full (403). The ticket is used up.</summary>
        public const string ScoreLimitReached = "SCORE_LIMIT_REACHED";

        /// <summary>Plain SubmitScore to a "validated only" board (403). Use SubmitValidated instead.</summary>
        public const string ValidatedSubmitRequired = "VALIDATED_SUBMIT_REQUIRED";

        /// <summary>The player's hourly run limit is reached (429). Not retried automatically.</summary>
        public const string RunRateLimited = "RUN_RATE_LIMITED";

        /// <summary>The account's hourly run capacity is reached (429). Not retried automatically.</summary>
        public const string RunCapacityReached = "RUN_CAPACITY_REACHED";

        /// <summary>The server has no ticket key configured (503).</summary>
        public const string ValidatedActionsUnavailable = "VALIDATED_ACTIONS_UNAVAILABLE";

        // Fallbacks for failures without a server code

        /// <summary>The endpoint answered 404 without a code, for example a self-hosted simpleServer (cloud only feature).</summary>
        public const string NotSupported = "NOT_SUPPORTED";

        /// <summary>Fallback for HTTP 400 without a server code (for example bean validation of a bad stage format).</summary>
        public const string BadRequest = "BAD_REQUEST";

        /// <summary>Fallback for HTTP 401 without a server code (for example an invalid API key).</summary>
        public const string Unauthorized = "UNAUTHORIZED";

        /// <summary>Fallback for HTTP 403 without a server code.</summary>
        public const string Forbidden = "FORBIDDEN";

        /// <summary>Fallback for HTTP 404 without a server code outside the validated actions endpoints.</summary>
        public const string NotFound = "NOT_FOUND";

        /// <summary>Fallback for HTTP 409 without a server code.</summary>
        public const string Conflict = "CONFLICT";

        /// <summary>Fallback for HTTP 422 without a server code.</summary>
        public const string UnprocessableEntity = "UNPROCESSABLE_ENTITY";

        /// <summary>Still rate limited (HTTP 429 without a code) after the SDK's retries.</summary>
        public const string RateLimited = "RATE_LIMITED";

        /// <summary>HTTP 5xx without a code after the SDK's retries.</summary>
        public const string ServerError = "SERVER_ERROR";

        /// <summary>No HTTP status (connection error, timeout, SDK not connected).</summary>
        public const string NetworkError = "NETWORK_ERROR";

        /// <summary>HTTP 2xx whose body could not be read.</summary>
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
            if (httpStatus == 422) return UnprocessableEntity;
            if (httpStatus == 429) return RateLimited;
            if (httpStatus >= 500) return ServerError;
            return $"HTTP_{httpStatus}";
        }

        /// <summary>
        /// Error code of a failed validated actions call: the server code when present, otherwise
        /// <see cref="NotSupported"/> for a 404 (the endpoint does not exist, for example on a
        /// simpleServer) and <see cref="FromHttpStatus"/> for everything else.
        /// </summary>
        /// <param name="httpStatus">HTTP status, 0 when there was none</param>
        /// <param name="serverCode">The <c>code</c> of the JSON error body, or null</param>
        public static string Resolve(long httpStatus, string serverCode)
        {
            if (!string.IsNullOrEmpty(serverCode)) return serverCode;
            if (httpStatus == 404) return NotSupported;
            return FromHttpStatus(httpStatus);
        }
    }
}
