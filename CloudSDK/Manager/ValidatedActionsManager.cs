using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using PM.horizOn.Cloud.Transport;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Manager for Validated Actions: server-checked runs with single-use tickets.
    /// Start a run, seed your deterministic randomness with <see cref="ValidatedRun.seed"/>,
    /// record the input log, then submit score and log hash. The server checks the ticket and the
    /// rules of the API key before anything is written. Every call needs a signed-in player; the
    /// request carries the player's Bearer session.
    ///
    /// The class is partial: the run lifecycle (Part 1) lives in this file, the player state
    /// (Part 2) and the evidence upload (Part 3) are added in their own files and plug in through
    /// the partial hooks <c>OnStateReceived</c> and <c>OnEvidenceRequested</c>.
    /// </summary>
    public partial class ValidatedActionsManager : BaseManager<ValidatedActionsManager>
    {
        private ValidatedRun _currentRun;
        private string _currentRunUserId;

        /// <summary>
        /// The run started by the last successful <see cref="StartRun"/>. Null before the first
        /// run, after a final submit (the ticket is single use), after <see cref="DiscardRun"/>,
        /// after sign-out and when another player signed in.
        /// </summary>
        public ValidatedRun CurrentRun
        {
            get
            {
                if (_currentRun != null && !BelongsToCurrentUser(_currentRunUserId))
                {
                    _currentRun = null;
                    _currentRunUserId = null;
                }
                return _currentRun;
            }
        }

        /// <summary>True when there is a current run to submit.</summary>
        public bool HasActiveRun => CurrentRun != null;

        /// <summary>
        /// Error code of the last failed call: the server <c>code</c> (for example
        /// <c>DURATION_TOO_SHORT</c>), a local code (<c>SESSION_REQUIRED</c>, <c>NO_ACTIVE_RUN</c>,
        /// <c>INVALID_INPUT_LOG_HASH</c>, <c>INVALID_CONTENT_DIGEST</c>, and for <c>UploadEvidence</c> <c>INVALID_RUN_ID</c>,
        /// <c>EMPTY_INPUT_LOG</c>) or an HTTP fallback (see <see cref="ValidatedActionsErrorCodes"/>).
        /// Null after a success. The automatic evidence upload never sets it (see <c>LastEvidenceErrorCode</c>).
        /// </summary>
        public string LastErrorCode { get; private set; }

        /// <summary>
        /// Upload the input log right after a submit when the server asks for it (Part 3). Only applies
        /// when the submit was made with the raw log bytes (<see cref="SubmitValidated"/>). The upload
        /// runs in the background after the submit returned; its outcome arrives as
        /// <c>EventKeys.ValidatedEvidenceUploaded</c> (423) or <c>EventKeys.ValidatedEvidenceUploadFailed</c> (424).
        /// Turn it off to call <c>UploadEvidence</c> yourself. Default true.
        /// </summary>
        public bool AutoUploadEvidence { get; set; } = true;

        /// <summary>
        /// Run start context used by <see cref="StartRun"/> when the call passes none (TASK-911).
        /// Set it once with the versions of your build, for example
        /// <c>new ValidatedRunContext("1.4.2", contentVersion: "levels-7")</c>. It is not merged with
        /// a context passed to <see cref="StartRun"/>: a passed context replaces it completely.
        /// Null (default) sends no context.
        /// </summary>
        public ValidatedRunContext DefaultRunContext { get; set; }

        /// <summary>
        /// SHA-256 of the raw input log bytes as 64 lower case hex characters.
        /// Keep the same bytes: a later evidence upload must match this hash.
        /// The same helper gives the <see cref="ValidatedRunContext.contentDigest"/> of your content bytes.
        /// </summary>
        /// <param name="inputLog">Raw input log; null is hashed like an empty log</param>
        public static string ComputeInputLogHash(byte[] inputLog)
        {
            return ValidatedActionsTransportContract.ComputeInputLogHash(inputLog);
        }

        /// <summary>
        /// Start a run: the server issues a single-use ticket with a seed. The run becomes
        /// <see cref="CurrentRun"/> (a previous run is replaced; its ticket simply expires).
        /// </summary>
        /// <param name="leaderboardKey">Optional board to bind the ticket to; null or "" leaves it unbound</param>
        /// <param name="context">
        /// Optional start context (TASK-911): game, content, simulation and replay format versions,
        /// content digest and the raw initial state of the simulation. The server archives it with
        /// the run when the run turns out sus. Null uses <see cref="DefaultRunContext"/>; a context
        /// without any field is not sent.
        /// </param>
        /// <returns>
        /// The run, or null on failure (then <see cref="LastErrorCode"/> is set, for example
        /// RUN_RATE_LIMITED, INVALID_CONTENT_DIGEST, INITIAL_STATE_INVALID_ENCODING or INITIAL_STATE_TOO_LARGE)
        /// </returns>
        public async Task<ValidatedRun> StartRun(string leaderboardKey = null, ValidatedRunContext context = null)
        {
            var user = UserManager.Instance.CurrentUser;
            if (!ValidatedActionsTransportContract.TryCreateStartRunPlan(
                    user,
                    HorizonApp.Network.GetSessionToken(),
                    leaderboardKey,
                    context ?? DefaultRunContext,
                    out var plan,
                    out var localError))
            {
                string message = localError == ValidatedActionsErrorCodes.InvalidContentDigest
                    ? "Validated run context rejected before sending: contentDigest must be 64 hex characters (SHA-256)"
                    : "User must be signed in to start a validated run";
                return FailLocally<ValidatedRun>(localError, message);
            }

            var response = await HorizonApp.Network.PostAsync<ValidatedRun>(
                ValidatedStartRunPlan.Endpoint,
                plan.Request,
                useSessionToken: plan.UseSessionToken
            );

            if (!response.IsSuccess || response.Data == null || string.IsNullOrEmpty(response.Data.ticket))
            {
                LastErrorCode = ResolveErrorCode(response);
                HorizonApp.Log.Error($"Validated run start failed ({LastErrorCode}): {response.Error}");
                return null;
            }

            ValidatedRun run = response.Data;
            run.Normalize();
            _currentRun = run;
            _currentRunUserId = plan.Request.userId;
            LastErrorCode = null;

            HorizonApp.Log.Info($"Validated run started: {run.runId} (board: {(run.HasLeaderboard ? run.leaderboardKey : "none")})");
            HorizonApp.Events.Publish(EventKeys.ValidatedRunStarted, run);
            return run;
        }

        /// <summary>
        /// Submit the result of the current run with the raw input log. The SDK hashes the log
        /// (SHA-256) and sends the hash with the ticket of <see cref="CurrentRun"/>.
        /// </summary>
        /// <param name="score">Score of the run; ignored by the server for a run without a board</param>
        /// <param name="inputLog">Raw input log bytes of the run</param>
        /// <param name="stage">Optional stage key for stage rules</param>
        /// <param name="leaderboardKey">Optional target board; null uses the board of the ticket</param>
        /// <param name="earned">Optional earned (positive) or spent (negative) server-owned values; every key must be defined in the rules (see <c>GetState</c>)</param>
        /// <returns>The result, or null on failure (then <see cref="LastErrorCode"/> is set)</returns>
        public Task<ValidatedSubmitResult> SubmitValidated(
            long score,
            byte[] inputLog,
            string stage = null,
            string leaderboardKey = null,
            IList<EarnedValue> earned = null)
        {
            return Submit(score, ComputeInputLogHash(inputLog), inputLog, stage, leaderboardKey, earned);
        }

        /// <summary>
        /// Submit the result of the current run with a ready input log hash
        /// (64 hex characters, see <see cref="ComputeInputLogHash"/>).
        /// </summary>
        /// <param name="score">Score of the run; ignored by the server for a run without a board</param>
        /// <param name="inputLogHash">SHA-256 of the input log as 64 hex characters</param>
        /// <param name="stage">Optional stage key for stage rules</param>
        /// <param name="leaderboardKey">Optional target board; null uses the board of the ticket</param>
        /// <param name="earned">Optional earned (positive) or spent (negative) server-owned values; every key must be defined in the rules (see <c>GetState</c>)</param>
        /// <returns>The result, or null on failure (then <see cref="LastErrorCode"/> is set)</returns>
        public Task<ValidatedSubmitResult> SubmitValidatedWithHash(
            long score,
            string inputLogHash,
            string stage = null,
            string leaderboardKey = null,
            IList<EarnedValue> earned = null)
        {
            return Submit(score, inputLogHash, null, stage, leaderboardKey, earned);
        }

        /// <summary>
        /// Drop the current run without submitting it (for example when the player quits).
        /// The ticket expires on the server.
        /// </summary>
        public void DiscardRun()
        {
            if (_currentRun != null)
            {
                HorizonApp.Log.Info($"Validated run discarded: {_currentRun.runId}");
            }
            _currentRun = null;
            _currentRunUserId = null;
        }

        private async Task<ValidatedSubmitResult> Submit(
            long score,
            string inputLogHash,
            byte[] inputLog,
            string stage,
            string leaderboardKey,
            IList<EarnedValue> earned)
        {
            ValidatedRun run = CurrentRun;
            if (!ValidatedActionsTransportContract.TryCreateSubmitPlan(
                    UserManager.Instance.CurrentUser,
                    HorizonApp.Network.GetSessionToken(),
                    run,
                    score,
                    inputLogHash,
                    stage,
                    leaderboardKey,
                    earned,
                    out var plan,
                    out var localError))
            {
                string message;
                if (localError == ValidatedActionsErrorCodes.SessionRequired)
                    message = "User must be signed in to submit a validated run";
                else if (localError == ValidatedActionsErrorCodes.NoActiveRun)
                    message = "No active validated run. Call StartRun first";
                else
                    message = $"Validated submit rejected before sending: {localError}";
                return FailLocally<ValidatedSubmitResult>(localError, message);
            }

            var response = await HorizonApp.Network.PostAsync<ValidatedSubmitResult>(
                ValidatedSubmitPlan.Endpoint,
                plan.Request,
                useSessionToken: plan.UseSessionToken
            );

            long status = response.StatusCode;
            bool runEnded = ValidatedActionsTransportContract.EndsRun(status, response.ErrorCode);
            if (runEnded)
            {
                // The ticket is used up (accepted, rejected or failed on the server).
                ClearRunIfCurrent(run);
            }

            if (response.IsSuccess && response.Data != null)
            {
                ValidatedSubmitResult result = response.Data;
                result.Normalize();
                LastErrorCode = null;

                HorizonApp.Log.Info((result.HasLeaderboard
                    ? $"Validated run accepted: {result.runId} on {result.leaderboardKey}, score {result.score}, rank {result.rank}"
                    : $"Validated run accepted: {result.runId} (no board)") + (result.sus ? " (sus)" : string.Empty));

                if (result.HasLeaderboard)
                {
                    // Cached leaderboard pages do not contain the new score yet.
                    LeaderboardManager.Instance.ClearCache();
                }

                OnStateReceived(result);
                if (result.evidence.required)
                {
                    OnEvidenceRequested(result, inputLog);
                }

                HorizonApp.Events.Publish(EventKeys.ValidatedRunSubmitted, result);
                return result;
            }

            LastErrorCode = ResolveErrorCode(response);
            if (LastErrorCode == ValidatedActionsErrorCodes.PlayerBanned)
            {
                // Checked before the ticket is used: the run stays, but this board refuses the player.
                HorizonApp.Log.Error($"Validated submit refused: the player is banned from this leaderboard (run {run.runId} kept, call DiscardRun or submit without a board)");
            }
            else
            {
                HorizonApp.Log.Error($"Validated submit failed ({LastErrorCode}): {response.Error}");
            }

            if (ValidatedActionsTransportContract.IsRejection(status))
            {
                HorizonApp.Events.Publish(EventKeys.ValidatedRunRejected, new ValidatedRunRejection
                {
                    code = LastErrorCode,
                    runId = run.runId,
                    httpStatus = status,
                    runCleared = runEnded
                });
            }
            return null;
        }

        /// <summary>
        /// Part 2 hook (player state): called after an accepted submit, before
        /// <c>EventKeys.ValidatedRunSubmitted</c> is published. <paramref name="result"/> is normalized,
        /// so <c>result.state</c> is never null.
        /// </summary>
        partial void OnStateReceived(ValidatedSubmitResult result);

        /// <summary>
        /// Part 3 hook (evidence): called after an accepted submit whose result has
        /// <c>evidence.required == true</c>. <paramref name="inputLog"/> holds the raw log when the
        /// submit was made with <see cref="SubmitValidated"/>, null after
        /// <see cref="SubmitValidatedWithHash"/>. Must not change the submit result
        /// (respect <see cref="AutoUploadEvidence"/>, report upload errors separately).
        /// </summary>
        partial void OnEvidenceRequested(ValidatedSubmitResult result, byte[] inputLog);

        private void ClearRunIfCurrent(ValidatedRun run)
        {
            // A StartRun during the request may already have replaced the run; keep that one.
            if (ReferenceEquals(_currentRun, run))
            {
                _currentRun = null;
                _currentRunUserId = null;
            }
        }

        private static string ResolveErrorCode<T>(NetworkResponse<T> response) where T : class
        {
            if (response.StatusCode >= 200 && response.StatusCode < 300)
            {
                // 2xx whose body could not be read (or has no ticket).
                return ValidatedActionsErrorCodes.InvalidResponse;
            }
            return ValidatedActionsErrorCodes.Resolve(response.StatusCode, response.ErrorCode);
        }

        private T FailLocally<T>(string errorCode, string message) where T : class
        {
            LastErrorCode = errorCode;
            HorizonApp.Log.Error(message);
            return null;
        }

        private static bool BelongsToCurrentUser(string userId)
        {
            var user = UserManager.Instance.CurrentUser;
            return user != null &&
                   user.IsValid() &&
                   string.Equals(user.UserId, userId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
