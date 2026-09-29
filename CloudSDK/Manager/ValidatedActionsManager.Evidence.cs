using System;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Transport;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Validated Actions Part 3 (TASK-888): evidence upload.
    /// When an accepted submit answers with <c>evidence.required == true</c>, the server wants the
    /// raw input log of that run (for example a new top entry or a flagged run). After
    /// <see cref="ValidatedActionsManager.SubmitValidated"/> the SDK uploads the log on its own
    /// (unless <see cref="ValidatedActionsManager.AutoUploadEvidence"/> is off). After
    /// <see cref="ValidatedActionsManager.SubmitValidatedWithHash"/> the SDK does not know the bytes:
    /// call <see cref="UploadEvidence"/> with <c>result.evidence.runId</c> and the exact bytes that
    /// were hashed, before <c>result.evidence.uploadBefore</c> (24 h).
    ///
    /// An upload never changes the submit result: the run stays accepted either way. Outcomes are
    /// reported through <see cref="EventKeys.ValidatedEvidenceUploaded"/> (423) and
    /// <see cref="EventKeys.ValidatedEvidenceUploadFailed"/> (424).
    /// </summary>
    public partial class ValidatedActionsManager
    {
        /// <summary>
        /// Error code of the last failed evidence upload (automatic or manual), for example
        /// <c>EVIDENCE_HASH_MISMATCH</c>; null after a successful upload. The automatic upload sets
        /// only this property, never <see cref="LastErrorCode"/>, because the submit already
        /// succeeded.
        /// </summary>
        public string LastEvidenceErrorCode { get; private set; }

        /// <summary>
        /// True when another <see cref="UploadEvidence"/> of the same run can succeed after a failure
        /// with this code: <c>EVIDENCE_HASH_MISMATCH</c> (send the exact bytes that were hashed) and
        /// <c>NETWORK_ERROR</c>. Every other code is final (for example <c>EVIDENCE_EXPIRED</c>).
        /// </summary>
        /// <param name="errorCode">Code of the failed upload</param>
        public static bool IsEvidenceRetryable(string errorCode)
        {
            return ValidatedActionsTransportContract.IsEvidenceRetryable(errorCode);
        }

        /// <summary>
        /// Upload the raw input log of an accepted run whose submit result asked for it
        /// (PUT /api/v1/app/validated-actions/runs/{runId}/evidence). Use it after
        /// <see cref="SubmitValidatedWithHash"/>, with <see cref="AutoUploadEvidence"/> off, or to
        /// retry after <c>EVIDENCE_HASH_MISMATCH</c> or <c>NETWORK_ERROR</c>. The SDK sends the bytes
        /// as standard base64; the server compares their SHA-256 with the hash sent with the run.
        /// Server errors and timeouts are retried by the network layer; the SDK does not retry on
        /// its own beyond that.
        /// </summary>
        /// <param name="runId">Run ID from <c>result.evidence.runId</c></param>
        /// <param name="inputLog">The exact raw log bytes whose hash was submitted</param>
        /// <returns>True when the log was stored; false otherwise (then <see cref="LastErrorCode"/>
        /// and <see cref="LastEvidenceErrorCode"/> are set, for example EVIDENCE_EXPIRED)</returns>
        public Task<bool> UploadEvidence(string runId, byte[] inputLog)
        {
            return Upload(runId, inputLog, 0, false);
        }

        partial void OnEvidenceRequested(ValidatedSubmitResult result, byte[] inputLog)
        {
            string runId = ValidatedActionsTransportContract.EvidenceRunId(result);
            if (!ValidatedActionsTransportContract.ShouldAutoUploadEvidence(AutoUploadEvidence, result.evidence, inputLog))
            {
                HorizonApp.Log.Info(
                    $"Evidence requested for run {runId} until {result.evidence.uploadBefore}: " +
                    "call UploadEvidence with the raw input log");
                return;
            }

            // Runs in the background: the submit result is returned right away. The plan (and the
            // base64 of the log) is built before the first await, so the game may reuse the array.
            _ = UploadInBackground(runId, inputLog, result.evidence.maxBytes);
        }

        private async Task UploadInBackground(string runId, byte[] inputLog, int maxBytes)
        {
            try
            {
                await Upload(runId, inputLog, maxBytes, true);
            }
            catch (Exception e)
            {
                HorizonApp.Log.Error($"Evidence upload of run {runId} failed unexpectedly: {e.Message}");
            }
        }

        private async Task<bool> Upload(string runId, byte[] inputLog, int maxBytes, bool automatic)
        {
            if (!ValidatedActionsTransportContract.TryCreateEvidencePlan(
                    UserManager.Instance.CurrentUser,
                    HorizonApp.Network.GetSessionToken(),
                    runId,
                    inputLog,
                    maxBytes,
                    out var plan,
                    out var localError))
            {
                string message;
                if (localError == ValidatedActionsErrorCodes.SessionRequired)
                    message = "User must be signed in to upload evidence";
                else if (localError == ValidatedActionsErrorCodes.EvidenceTooLarge)
                    message = $"Evidence of run {runId} not sent: the log has {inputLog.Length} bytes, the limit is {maxBytes}";
                else
                    message = $"Evidence upload rejected before sending: {localError}";
                FailEvidence(runId?.Trim(), localError, 0, automatic, message);
                return false;
            }

            var response = await HorizonApp.Network.PutAsync<EvidenceUploadResult>(
                plan.Endpoint,
                plan.Request,
                useSessionToken: plan.UseSessionToken
            );

            long status = response.StatusCode;
            if (response.IsSuccess || (status >= 200 && status < 300))
            {
                // A 2xx means the server stored the log, even when the body could not be read.
                EvidenceUploadResult uploaded = response.Data ?? new EvidenceUploadResult();
                if (string.IsNullOrEmpty(uploaded.runId)) uploaded.runId = plan.RunId;
                if (string.IsNullOrEmpty(uploaded.status)) uploaded.status = "UPLOADED";
                if (uploaded.bytes <= 0) uploaded.bytes = plan.LogBytes;

                LastEvidenceErrorCode = null;
                if (!automatic)
                {
                    LastErrorCode = null;
                }

                HorizonApp.Log.Info($"Evidence uploaded: run {uploaded.runId}, {uploaded.bytes} bytes");
                HorizonApp.Events.Publish(EventKeys.ValidatedEvidenceUploaded, uploaded);
                return true;
            }

            string code = ValidatedActionsErrorCodes.Resolve(status, response.ErrorCode);
            FailEvidence(plan.RunId, code, status, automatic, $"Evidence upload of run {plan.RunId} failed ({code}): {response.Error}");
            return false;
        }

        private void FailEvidence(string runId, string code, long httpStatus, bool automatic, string message)
        {
            LastEvidenceErrorCode = code;
            if (!automatic)
            {
                LastErrorCode = code;
            }

            HorizonApp.Log.Error(message);
            HorizonApp.Events.Publish(EventKeys.ValidatedEvidenceUploadFailed, new ValidatedEvidenceFailure
            {
                runId = runId ?? string.Empty,
                code = code,
                httpStatus = httpStatus,
                retryable = ValidatedActionsTransportContract.IsEvidenceRetryable(code),
                automatic = automatic
            });
        }
    }
}
