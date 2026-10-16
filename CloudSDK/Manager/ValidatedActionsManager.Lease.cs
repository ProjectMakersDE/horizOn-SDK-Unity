using System;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;

namespace PM.horizOn.Cloud.Manager
{
    public partial class ValidatedActionsManager
    {
        private RunLease _runLease;
        /// <summary>True while a start/submit and its synchronous callbacks still own the common run slot.</summary>
        public bool HasPendingOperation => _runLease != null;
        private long _runEpoch;
        private string _currentRunScope;
        private NetworkService _currentRunNetwork;
        protected override void RegisterEvents()
        {
            RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => DiscardRun());
            RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => DiscardRun());
        }
        protected override void OnDisable() { DiscardRun(); base.OnDisable(); }
        private static string RunScope()
        {
            var n = HorizonApp.Network; var user = UserManager.Instance.CurrentUser;
            return user?.UserId + "|" + user?.AccessToken + "|" + n.GetSessionToken() + "|" + n.GetApiKey() + "|" + n.GetActiveHost() + "|" + n.ContextGeneration;
        }
        /// <summary>One shared start slot for generic, Shared defense and ranked Match starts in this SDK assembly.
        /// The owner holds it through publication/callbacks; discard invalidates adoption, never releases in-flight HTTP ownership.</summary>
        internal RunLease AcquireRunStart(string userId, out string error) => AcquireRunLease(userId, false, out error);
        private RunLease AcquireRunLease(string userId, bool submit, out string error)
        {
            error = null;
            if (_runLease != null) { error = "RUN_OPERATION_PENDING"; return null; }
            if (!BelongsToCurrentUser(userId) || UserManager.Instance.CurrentUser.AccessToken != HorizonApp.Network.GetSessionToken()) { error = "SESSION_REQUIRED"; return null; }
            if (!submit && CurrentRun != null) { error = "PENDING_RUN"; return null; }
            return _runLease = new RunLease(this, userId, _runEpoch, RunScope(), HorizonApp.Network);
        }
        internal sealed class RunLease : IDisposable
        {
            private ValidatedActionsManager _owner;
            private readonly string _user, _scope;
            private readonly long _epoch;
            internal NetworkService Network { get; }
            internal RunLease(ValidatedActionsManager owner, string user, long epoch, string scope, NetworkService network)
            { _owner = owner; _user = user; _epoch = epoch; _scope = scope; Network = network; }
            internal bool IsCurrent => _owner != null && ReferenceEquals(_owner._runLease, this) && _owner._runEpoch == _epoch
                && ReferenceEquals(Network, HorizonApp.Network) && _scope == RunScope() && BelongsToCurrentUser(_user);
            /// <summary>Adopt only the exact authorized response while retaining exclusive ownership through caller callbacks.</summary>
            internal bool TryAdoptRun(ValidatedRun run)
            {
                if (!IsCurrent || run == null || string.IsNullOrEmpty(run.ticket)) return false;
                run.Normalize(); _owner._currentRun = run; _owner._currentRunUserId = _user;
                _owner._currentRunScope = _scope; _owner._currentRunNetwork = Network;
                return true;
            }
            public void Dispose()
            {
                if (_owner != null && ReferenceEquals(_owner._runLease, this)) _owner._runLease = null;
                _owner = null;
            }
        }
    }
}
