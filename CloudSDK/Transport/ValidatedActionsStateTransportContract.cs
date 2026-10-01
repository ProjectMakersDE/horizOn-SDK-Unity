using System;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Transport
{
    /// <summary>
    /// GET /api/v1/app/validated-actions/state?userId=... for the signed-in player (Part 2).
    /// </summary>
    internal sealed class ValidatedStatePlan
    {
        internal string Endpoint { get; }

        /// <summary>The signed-in player the state belongs to.</summary>
        internal string UserId { get; }

        /// <summary>Every validated actions endpoint needs the player's Bearer session.</summary>
        internal bool UseSessionToken => true;

        internal ValidatedStatePlan(string endpoint, string userId)
        {
            Endpoint = endpoint;
            UserId = userId;
        }
    }

    /// <summary>
    /// Player state part (TASK-887) of the Validated Actions transport contract.
    /// </summary>
    internal static partial class ValidatedActionsTransportContract
    {
        internal const string StateEndpoint = BasePath + "/state";

        /// <summary>
        /// Builds the state plan only for a signed-in user whose access token is the session token
        /// the transport will send (otherwise SESSION_REQUIRED, no request).
        /// </summary>
        internal static bool TryCreateGetStatePlan(
            UserData user,
            string transportSessionToken,
            out ValidatedStatePlan plan,
            out string errorCode)
        {
            plan = null;
            errorCode = null;

            if (!HasMatchingSession(user, transportSessionToken))
            {
                errorCode = ValidatedActionsErrorCodes.SessionRequired;
                return false;
            }

            plan = new ValidatedStatePlan(
                $"{StateEndpoint}?userId={Uri.EscapeDataString(user.UserId)}",
                user.UserId);
            return true;
        }

        /// <summary>
        /// The state to keep as the manager's current state after an accepted submit, or null to
        /// keep the previous one. A submit <c>state</c> of null (no values in the rules, a Part 1
        /// server, or a failed state write on the server) must not wipe a loaded state. The kept
        /// copy has no per-run fields (<c>requested</c> and <c>credited</c> are 0).
        /// </summary>
        internal static PlayerState StateToCache(PlayerState submitState)
        {
            if (submitState == null || !submitState.HasData)
            {
                return null;
            }
            return submitState.WithoutRunDetails();
        }
    }
}
