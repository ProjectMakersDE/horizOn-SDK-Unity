using System;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Transport
{
    /// <summary>
    /// PUT /api/v1/app/validated-actions/runs/{runId}/evidence for the signed-in player (Part 3).
    /// </summary>
    internal sealed class ValidatedEvidencePlan
    {
        internal string Endpoint { get; }

        /// <summary>The run whose log is uploaded (trimmed).</summary>
        internal string RunId { get; }

        /// <summary>Decoded size of the log in bytes.</summary>
        internal int LogBytes { get; }

        internal UploadEvidenceRequest Request { get; }

        /// <summary>Every validated actions endpoint needs the player's Bearer session.</summary>
        internal bool UseSessionToken => true;

        internal ValidatedEvidencePlan(string endpoint, string runId, int logBytes, UploadEvidenceRequest request)
        {
            Endpoint = endpoint;
            RunId = runId;
            LogBytes = logBytes;
            Request = request;
        }
    }

    /// <summary>
    /// Evidence part (TASK-888) of the Validated Actions transport contract.
    /// </summary>
    internal static partial class ValidatedActionsTransportContract
    {
        /// <summary>
        /// Builds the upload plan. Local checks in this order (no request on failure): session
        /// (SESSION_REQUIRED), run ID (INVALID_RUN_ID), a non-empty log (EMPTY_INPUT_LOG, the server
        /// needs a log), and, when <paramref name="maxBytes"/> is known (above 0), the size
        /// (EVIDENCE_TOO_LARGE). The log is encoded here, so the caller may reuse the byte array
        /// right after this call. The run ID is escaped into the path.
        /// </summary>
        /// <param name="maxBytes">Limit from <c>EvidenceRequest.maxBytes</c>; 0 or less leaves the size check to the server</param>
        /// <param name="errorCode">The failed check, null on success</param>
        internal static bool TryCreateEvidencePlan(
            UserData user,
            string transportSessionToken,
            string runId,
            byte[] inputLog,
            int maxBytes,
            out ValidatedEvidencePlan plan,
            out string errorCode)
        {
            plan = null;
            errorCode = null;

            if (!HasMatchingSession(user, transportSessionToken))
            {
                errorCode = ValidatedActionsErrorCodes.SessionRequired;
                return false;
            }

            string trimmedRunId = runId?.Trim();
            if (string.IsNullOrEmpty(trimmedRunId))
            {
                errorCode = ValidatedActionsErrorCodes.InvalidRunId;
                return false;
            }

            if (inputLog == null || inputLog.Length == 0)
            {
                errorCode = ValidatedActionsErrorCodes.EmptyInputLog;
                return false;
            }

            if (maxBytes > 0 && inputLog.Length > maxBytes)
            {
                errorCode = ValidatedActionsErrorCodes.EvidenceTooLarge;
                return false;
            }

            plan = new ValidatedEvidencePlan(
                EvidenceEndpoint(trimmedRunId),
                trimmedRunId,
                inputLog.Length,
                new UploadEvidenceRequest
                {
                    userId = user.UserId,
                    // Standard base64 with padding, as the server decodes it.
                    log = Convert.ToBase64String(inputLog)
                });
            return true;
        }

        /// <summary>
        /// Path of the evidence upload of one run.
        /// </summary>
        internal static string EvidenceEndpoint(string runId)
        {
            return $"{RunsEndpoint}/{Uri.EscapeDataString(runId ?? string.Empty)}/evidence";
        }

        /// <summary>
        /// True when the submit result asks for the log and the SDK uploads it right away: evidence
        /// required, the raw log is known (submit with bytes, not with a hash) and auto upload is on.
        /// </summary>
        internal static bool ShouldAutoUploadEvidence(bool autoUploadEvidence, EvidenceRequest evidence, byte[] inputLog)
        {
            return autoUploadEvidence && evidence != null && evidence.required && inputLog != null;
        }

        /// <summary>
        /// Run ID of an evidence request: <c>evidence.runId</c>, or the run ID of the result when the
        /// request has none.
        /// </summary>
        internal static string EvidenceRunId(ValidatedSubmitResult result)
        {
            if (result == null)
            {
                return null;
            }
            string runId = result.evidence?.runId;
            return string.IsNullOrEmpty(runId) ? result.runId : runId;
        }

        /// <summary>
        /// True when another upload of the same run can succeed: after EVIDENCE_HASH_MISMATCH (the
        /// request stays open; send the exact bytes that were hashed) and after a network error.
        /// Every other code is final (400, 404, 409, 410, 413) or needs a new session first.
        /// </summary>
        /// <param name="errorCode">The resolved error code of the failed upload</param>
        internal static bool IsEvidenceRetryable(string errorCode)
        {
            return string.Equals(errorCode, ValidatedActionsErrorCodes.EvidenceHashMismatch, StringComparison.Ordinal) ||
                   string.Equals(errorCode, ValidatedActionsErrorCodes.NetworkError, StringComparison.Ordinal);
        }
    }
}
