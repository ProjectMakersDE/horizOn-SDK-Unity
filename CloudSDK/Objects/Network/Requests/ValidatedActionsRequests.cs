using System;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Body of POST /api/v1/app/validated-actions/runs.
    /// A null or empty leaderboardKey is left out: the ticket is then not bound to a board.
    /// </summary>
    [Serializable]
    public class StartRunRequest
    {
        public string userId;
        public string leaderboardKey;
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
