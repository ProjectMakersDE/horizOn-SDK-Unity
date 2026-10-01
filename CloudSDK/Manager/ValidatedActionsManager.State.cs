using System.Threading.Tasks;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Transport;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Validated Actions Part 2 (TASK-887): server-owned player state.
    /// Currency and loot counters are written only by the server, through <c>earned</c> of an
    /// accepted validated run. The SDK reads them with <see cref="GetState"/> and keeps the last
    /// known state in <see cref="CurrentState"/>. There is no method that writes the state.
    ///
    /// Cloud save as a mirror: copy the state into your save for display and offline start, load
    /// it with <see cref="GetState"/> on start and overwrite the copy with it (never the other
    /// way round), and never send a cloud save value back as a balance. Values earned offline go
    /// into <c>earned</c> of the next validated run.
    /// </summary>
    public partial class ValidatedActionsManager
    {
        private PlayerState _currentState;
        private string _currentStateUserId;

        /// <summary>
        /// Last known server-owned state of the signed-in player: from the last successful
        /// <see cref="GetState"/> or the last accepted submit that carried a state, whichever came
        /// last. <c>requested</c> and <c>credited</c> are always 0 here (read them from the submit
        /// result). Null before the first load, after sign-out and when another player signed in.
        /// </summary>
        public PlayerState CurrentState
        {
            get
            {
                if (_currentState != null && !BelongsToCurrentUser(_currentStateUserId))
                {
                    _currentState = null;
                    _currentStateUserId = null;
                }
                return _currentState;
            }
        }

        /// <summary>
        /// Load the server-owned values of the signed-in player
        /// (GET /api/v1/app/validated-actions/state). Every value defined in the rules of the API
        /// key is listed, sorted by key (balance 0 when never earned); <c>values</c> is empty when
        /// the rules define no values. On success the state becomes <see cref="CurrentState"/> and
        /// <see cref="EventKeys.ValidatedStateLoaded"/> (308) is published. No cache: every call
        /// asks the server.
        /// </summary>
        /// <returns>The state, or null on failure (then <see cref="ValidatedActionsManager.LastErrorCode"/> is set,
        /// for example SESSION_REQUIRED or NOT_SUPPORTED)</returns>
        public async Task<PlayerState> GetState()
        {
            if (!ValidatedActionsTransportContract.TryCreateGetStatePlan(
                    UserManager.Instance.CurrentUser,
                    HorizonApp.Network.GetSessionToken(),
                    out var plan,
                    out var localError))
            {
                return FailLocally<PlayerState>(localError, "User must be signed in to load the validated actions state");
            }

            var response = await HorizonApp.Network.GetAsync<PlayerState>(
                plan.Endpoint,
                useSessionToken: plan.UseSessionToken
            );

            if (!response.IsSuccess || response.Data == null)
            {
                LastErrorCode = ResolveErrorCode(response);
                HorizonApp.Log.Error($"Validated actions state load failed ({LastErrorCode}): {response.Error}");
                return null;
            }

            PlayerState state = response.Data;
            state.Normalize();
            LastErrorCode = null;

            if (BelongsToCurrentUser(plan.UserId))
            {
                // A sign-out or another sign-in during the request must not take over this state.
                SetCurrentState(state.WithoutRunDetails(), plan.UserId);
            }

            HorizonApp.Log.Info($"Validated actions state loaded: {state.values.Length} values ({state.day})");
            HorizonApp.Events.Publish(EventKeys.ValidatedStateLoaded, state);
            return state;
        }

        partial void OnStateReceived(ValidatedSubmitResult result)
        {
            PlayerState cached = ValidatedActionsTransportContract.StateToCache(result.state);
            if (cached == null)
            {
                // No state in the result: keep the previous CurrentState.
                return;
            }

            var user = UserManager.Instance.CurrentUser;
            if (user == null || !user.IsValid())
            {
                return;
            }

            SetCurrentState(cached, user.UserId);
            HorizonApp.Events.Publish(EventKeys.ValidatedStateLoaded, cached);
        }

        private void SetCurrentState(PlayerState state, string userId)
        {
            _currentState = state;
            _currentStateUserId = userId;
        }
    }
}
