using System;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Body of POST /api/v1/app/validated-actions/runs.
    /// A null or empty leaderboardKey is left out: the ticket is then not bound to a board.
    /// A null context (nothing declared) is left out as well, so the request equals the one of
    /// older SDKs.
    /// </summary>
    [Serializable]
    public class StartRunRequest
    {
        public string userId;
        public string leaderboardKey;
        public RunStartContextRequest context;
    }

    /// <summary>
    /// Wire form of <see cref="ValidatedRunContext"/> (TASK-911): the <c>context</c> object of a run
    /// start. Null or empty fields are left out. <c>initialState</c> is the raw initial state as
    /// standard base64 with padding.
    /// </summary>
    [Serializable]
    public class RunStartContextRequest
    {
        public string gameVersion;
        public string contentVersion;
        public string simulationVersion;
        public string replayFormatVersion;
        public string contentDigest;
        public string initialState;
    }

    /// <summary>
    /// What a validated run starts from, declared by the game (optional, TASK-911). The server binds
    /// it into the run's start context together with the values it fixes itself (rule version,
    /// cloud save, server-owned values, seed, start time). When the run turns out sus, the context
    /// is archived with the run so it can be replayed later. Every field is optional; null or empty
    /// fields are not sent, and a context without any field is not sent at all.
    /// </summary>
    [Serializable]
    public class ValidatedRunContext
    {
        /// <summary>Version of the game build, at most 64 printable ASCII characters (for example "1.4.2").</summary>
        public string gameVersion;

        /// <summary>Version of the game content (levels, balancing data), at most 64 printable ASCII characters.</summary>
        public string contentVersion;

        /// <summary>Version of the deterministic simulation, at most 64 printable ASCII characters.</summary>
        public string simulationVersion;

        /// <summary>Version of the input log format, at most 64 printable ASCII characters.</summary>
        public string replayFormatVersion;

        /// <summary>
        /// SHA-256 of the game content the run uses as 64 hex characters (checked locally, otherwise
        /// INVALID_CONTENT_DIGEST without a request). Compute it with
        /// <c>ValidatedActionsManager.ComputeInputLogHash(contentBytes)</c>, the same SHA-256 helper.
        /// </summary>
        public string contentDigest;

        /// <summary>
        /// Raw bytes the simulation starts from (for example a serialized level state). Sent as base64,
        /// decoded at most <c>evidenceMaxBytes</c> on the server (otherwise INITIAL_STATE_TOO_LARGE).
        /// </summary>
        public byte[] initialState;

        public ValidatedRunContext()
        {
        }

        public ValidatedRunContext(
            string gameVersion,
            string contentVersion = null,
            string simulationVersion = null,
            string replayFormatVersion = null,
            string contentDigest = null,
            byte[] initialState = null)
        {
            this.gameVersion = gameVersion;
            this.contentVersion = contentVersion;
            this.simulationVersion = simulationVersion;
            this.replayFormatVersion = replayFormatVersion;
            this.contentDigest = contentDigest;
            this.initialState = initialState;
        }

        /// <summary>True when no field is set; such a context is not sent.</summary>
        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(gameVersion) &&
            string.IsNullOrWhiteSpace(contentVersion) &&
            string.IsNullOrWhiteSpace(simulationVersion) &&
            string.IsNullOrWhiteSpace(replayFormatVersion) &&
            string.IsNullOrWhiteSpace(contentDigest) &&
            (initialState == null || initialState.Length == 0);
    }

    /// <summary>
    /// Body of POST /api/v1/app/validated-actions/submit.
    /// Null or empty strings are left out (the server treats blank stage and leaderboardKey as
    /// absent). A null earned array is left out as well. score is always sent; the server ignores
    /// it for a run without a board.
    /// </summary>
    [Serializable]
    public class SubmitValidatedRequest
    {
        public string userId;
        public string ticket;
        public string inputLogHash;
        public long score;
        public string stage;
        public string leaderboardKey;
        public EarnedValue[] earned;
    }

    /// <summary>
    /// Body of PUT /api/v1/app/validated-actions/runs/{runId}/evidence (Part 3, TASK-888).
    /// <c>log</c> is the raw input log as standard base64 with padding; decoded, its SHA-256 must
    /// equal the <c>inputLogHash</c> sent with the run.
    /// </summary>
    [Serializable]
    public class UploadEvidenceRequest
    {
        public string userId;
        public string log;
    }

    /// <summary>
    /// A server-owned value the run earned (positive amount) or spent (negative amount).
    /// The key must be defined under <c>values</c> in the rules of the API key, otherwise the run
    /// is rejected with UNKNOWN_VALUE_KEY. At most 64 entries, each key once.
    /// Key format: <c>^[a-z0-9][a-z0-9._-]{0,23}$</c>.
    /// </summary>
    [Serializable]
    public class EarnedValue
    {
        /// <summary>Value key as defined in the rules of the API key, for example "gold".</summary>
        public string key;

        /// <summary>Earned (positive) or spent (negative) amount.</summary>
        public long amount;

        public EarnedValue()
        {
        }

        public EarnedValue(string key, long amount)
        {
            this.key = key;
            this.amount = amount;
        }
    }
}
