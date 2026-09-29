using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Transport
{
    /// <summary>
    /// POST /api/v1/app/validated-actions/runs for the signed-in player.
    /// </summary>
    internal sealed class ValidatedStartRunPlan
    {
        internal const string Endpoint = ValidatedActionsTransportContract.RunsEndpoint;

        internal StartRunRequest Request { get; }

        /// <summary>Every validated actions endpoint needs the player's Bearer session.</summary>
        internal bool UseSessionToken => true;

        internal ValidatedStartRunPlan(StartRunRequest request)
        {
            Request = request;
        }
    }

    /// <summary>
    /// POST /api/v1/app/validated-actions/submit with the ticket of the current run.
    /// </summary>
    internal sealed class ValidatedSubmitPlan
    {
        internal const string Endpoint = ValidatedActionsTransportContract.SubmitEndpoint;

        internal SubmitValidatedRequest Request { get; }

        internal bool UseSessionToken => true;

        internal ValidatedSubmitPlan(SubmitValidatedRequest request)
        {
            Request = request;
        }
    }

    /// <summary>
    /// Request plans and run lifecycle rules of Validated Actions, free of UnityEngine so the
    /// .NET transport contract can compile them.
    /// </summary>
    internal static class ValidatedActionsTransportContract
    {
        internal const string BasePath = "/api/v1/app/validated-actions";
        internal const string RunsEndpoint = BasePath + "/runs";
        internal const string SubmitEndpoint = BasePath + "/submit";

        private static readonly Regex InputLogHashPattern = new Regex(
            @"^[0-9a-fA-F]{64}\z",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// SHA-256 of the raw input log bytes as 64 lower case hex characters.
        /// A null log is hashed like an empty one.
        /// </summary>
        internal static string ComputeInputLogHash(byte[] inputLog)
        {
            byte[] digest;
            using (SHA256 sha = SHA256.Create())
            {
                digest = sha.ComputeHash(inputLog ?? new byte[0]);
            }

            const string hexDigits = "0123456789abcdef";
            var chars = new char[digest.Length * 2];
            for (int i = 0; i < digest.Length; i++)
            {
                chars[i * 2] = hexDigits[digest[i] >> 4];
                chars[i * 2 + 1] = hexDigits[digest[i] & 0x0F];
            }
            return new string(chars);
        }

        /// <summary>
        /// True when the hash is 64 hex characters (upper case is accepted, like on the server).
        /// </summary>
        internal static bool IsValidInputLogHash(string inputLogHash)
        {
            return !string.IsNullOrEmpty(inputLogHash) && InputLogHashPattern.IsMatch(inputLogHash);
        }

        /// <summary>
        /// Builds the start plan only for a signed-in user whose access token is the session token
        /// the transport will send (otherwise SESSION_REQUIRED, no request).
        /// A blank leaderboardKey is left out: the ticket is then not bound to a board.
        /// </summary>
        internal static bool TryCreateStartRunPlan(
            UserData user,
            string transportSessionToken,
            string leaderboardKey,
            out ValidatedStartRunPlan plan,
            out string errorCode)
        {
            plan = null;
            errorCode = null;

            if (!HasMatchingSession(user, transportSessionToken))
            {
                errorCode = ValidatedActionsErrorCodes.SessionRequired;
                return false;
            }

            plan = new ValidatedStartRunPlan(new StartRunRequest
            {
                userId = user.UserId,
                leaderboardKey = NormalizeOptional(leaderboardKey)
            });
            return true;
        }

        /// <summary>
        /// Builds the submit plan for the current run. Local checks in this order (no request on
        /// failure): session (SESSION_REQUIRED), a current run with a ticket (NO_ACTIVE_RUN), the
        /// hash format (INVALID_INPUT_LOG_HASH). An expired run is still sent; the server decides.
        /// Blank stage and leaderboardKey are left out (the ticket's board is used), the hash is
        /// sent in lower case, null entries of earned are skipped and an empty earned list is left out.
        /// </summary>
        /// <param name="errorCode">The failed check, null on success</param>
        internal static bool TryCreateSubmitPlan(
            UserData user,
            string transportSessionToken,
            ValidatedRun currentRun,
            long score,
            string inputLogHash,
            string stage,
            string leaderboardKey,
            IList<EarnedValue> earned,
            out ValidatedSubmitPlan plan,
            out string errorCode)
        {
            plan = null;
            errorCode = null;

            if (!HasMatchingSession(user, transportSessionToken))
            {
                errorCode = ValidatedActionsErrorCodes.SessionRequired;
                return false;
            }

            if (currentRun == null || string.IsNullOrEmpty(currentRun.ticket))
            {
                errorCode = ValidatedActionsErrorCodes.NoActiveRun;
                return false;
            }

            string hash = inputLogHash?.Trim();
            if (!IsValidInputLogHash(hash))
            {
                errorCode = ValidatedActionsErrorCodes.InvalidInputLogHash;
                return false;
            }

            EarnedValue[] earnedValues = null;
            if (earned != null && earned.Count > 0)
            {
                var list = new List<EarnedValue>(earned.Count);
                foreach (var value in earned)
                {
                    if (value != null)
                    {
                        list.Add(new EarnedValue(value.key, value.amount));
                    }
                }
                if (list.Count > 0)
                {
                    earnedValues = list.ToArray();
                }
            }

            plan = new ValidatedSubmitPlan(new SubmitValidatedRequest
            {
                userId = user.UserId,
                ticket = currentRun.ticket,
                inputLogHash = hash.ToLowerInvariant(),
                score = score,
                stage = NormalizeOptional(stage),
                leaderboardKey = NormalizeOptional(leaderboardKey),
                earned = earnedValues
            });
            return true;
        }

        /// <summary>
        /// True when a submit outcome uses up the ticket, so the SDK drops the current run:
        /// a success, any 422 except LEADERBOARD_MISMATCH (ticket checks and rule rejections), and
        /// 403 SCORE_LIMIT_REACHED. On network errors, 400, 401, other 403, 404, 429 and 5xx the run
        /// stays and the game may retry with the same ticket. LEADERBOARD_MISMATCH is checked before
        /// the ticket is consumed, so the game may resubmit with the ticket's board.
        /// </summary>
        /// <param name="httpStatus">HTTP status, 0 when there was none</param>
        /// <param name="errorCode">Server code of the error body, or null</param>
        internal static bool EndsRun(long httpStatus, string errorCode)
        {
            if (httpStatus >= 200 && httpStatus < 300)
            {
                return true;
            }
            if (httpStatus == 422)
            {
                return !string.Equals(errorCode, ValidatedActionsErrorCodes.LeaderboardMismatch, StringComparison.Ordinal);
            }
            if (httpStatus == 403)
            {
                return string.Equals(errorCode, ValidatedActionsErrorCodes.ScoreLimitReached, StringComparison.Ordinal);
            }
            return false;
        }

        /// <summary>
        /// True for a submit the server refused on purpose (422 or 403); these publish
        /// <c>EventKeys.ValidatedRunRejected</c>.
        /// </summary>
        internal static bool IsRejection(long httpStatus)
        {
            return httpStatus == 422 || httpStatus == 403;
        }

        /// <summary>
        /// Trims; blank becomes null so the field is left out of the body.
        /// </summary>
        private static string NormalizeOptional(string value)
        {
            string trimmed = value?.Trim();
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
