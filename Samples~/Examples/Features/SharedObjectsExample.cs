using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Explicit screen/button actions for an existing session. No poller, auto-pagination or client-authoritative reward.
    /// Games must restore any stored anonymous identity first and fail closed on restore failure, never create a replacement guest.</summary>
    public class SharedObjectsExample : MonoBehaviour
    {
        [SerializeField] private string projectId;
        [SerializeField] private string collection = "levels";
        private SharedUsage _usage;
        private SharedObject _object;
        private string _usageScope, _objectScope;
        private int _generation;
        private bool _busy;
        private string _recoveryHash, _recoveryScope, _nextRecoveryCursor;
        private long _recoveryUsageRevision;
        private readonly HashSet<string> _recoveryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Call ReviewNextCreateOwnerPage explicitly with this opaque cursor; no automatic paging.</summary>
        public string NextRecoveryCursor => _recoveryScope == Scope ? _nextRecoveryCursor : null;
        private string User => isActiveAndEnabled && UserManager.Instance.CurrentUser?.IsValid() == true && UserManager.Instance.CurrentUser.AccessToken == HorizonApp.Network.GetSessionToken() ? UserManager.Instance.CurrentUser.UserId : null;
        private string Scope => projectId + "|" + collection + "|" + User + "|" + HorizonApp.Network.GetApiKey() + "|" + HorizonApp.Network.GetActiveHost() + "|" + HorizonApp.Network.GetSessionToken() + "|" + HorizonApp.Network.ContextGeneration + "|" + _generation;
        private SharedObjectsSampleStore Store(string user) => new SharedObjectsSampleStore(Application.persistentDataPath, projectId, user, HorizonApp.Network.GetApiKey());
        public async Task<bool> ReadUsage()
        {
            string user = User; if (user == null || _busy) return false; string scope = Scope; var network = HorizonApp.Network;
            var r = await SharedObjectsManager.Instance.GetUsage(user, collection);
            if (!Current(scope, network)) return false;
            _usage = r.IsSuccess ? r.Data : null; _usageScope = r.IsSuccess ? scope : null; return r.IsSuccess;
        }
        public async Task<bool> ReadObject(string objectId)
        {
            string user = User; if (user == null || _busy) return false; string scope = Scope; var network = HorizonApp.Network;
            var r = await SharedObjectsManager.Instance.GetObject(user, collection, objectId);
            if (!Current(scope, network)) return false;
            _object = r.IsSuccess ? r.Data.@object : null; _objectScope = r.IsSuccess ? scope : null; return r.IsSuccess;
        }
        /// <summary>One explicit page; keep returned nextAfterKey and revision together for the next player action.</summary>
        public Task<SharedObjectsResult<SharedCollectionPage>> ReadOneCollectionPage(string afterKey = null, long? catalogRevision = null)
            => SharedObjectsManager.Instance.GetCollections(User, afterKey, 50, catalogRevision);
        public Task<SharedObjectsResult<SharedQueryResult>> ReadRectangle(long minX, long maxX, long minY, long maxY, string cursor = null)
            => SharedObjectsManager.Instance.QueryObjects(User, collection, new SharedQuery { mode = SharedQueryMode.Rectangle, minX = minX, maxX = maxX, minY = minY, maxY = maxY, cursor = cursor, limit = 20 });
        /// <summary>Bounded, non-uniform integer sampling; choose/render a returned opponent explicitly.</summary>
        public Task<SharedObjectsResult<SharedQueryResult>> FindOpponent(long minStrength, long maxStrength)
            => SharedObjectsManager.Instance.QueryObjects(User, collection, new SharedQuery { mode = SharedQueryMode.Random, field = "strength", min = new SharedScalar { integerValue = minStrength }, max = new SharedScalar { integerValue = maxStrength }, limit = 10 });
        public Task<bool> SaveCreateAfterUsageReview(SharedObjectInput input, string groupId = null)
        {
            string user = User; if (user == null || _busy || _usage == null || _usageScope != Scope) return Task.FromResult(false);
            Store(user).SaveNew(SharedObjectsSampleStore.NewIntent(projectId, user, "create", collection, null, groupId, _usage.revision, input));
            return ApplySavedIntent();
        }
        public Task<bool> SaveUpdateAfterObjectReview(SharedObjectInput input)
        {
            string user = User; if (user == null || _busy || _object == null || _objectScope != Scope) return Task.FromResult(false);
            Store(user).SaveNew(SharedObjectsSampleStore.NewIntent(projectId, user, "update", collection, _object.id, _object.groupId, _object.revision, input));
            return ApplySavedIntent();
        }
        /// <summary>The game's separate destructive confirmation calls this method; no update is reinterpreted as delete.</summary>
        public Task<bool> SaveDeleteAfterSeparateConfirmation()
        {
            string user = User; if (user == null || _busy || _object == null || _objectScope != Scope) return Task.FromResult(false);
            Store(user).SaveNew(SharedObjectsSampleStore.NewIntent(projectId, user, "delete", collection, _object.id, _object.groupId, _object.revision, null));
            return ApplySavedIntent();
        }
        public async Task<bool> ApplySavedIntent()
        {
            string user = User; if (user == null || _busy) return false; var store = Store(user); if (!store.HasIntent) return false;
            _busy = true;
            try
            {
                using (var lease = store.AcquireSend())
                {
                    var original = lease.Prepared; var x = original.Value;
                    if (original.HasSuccess) return true;
                    if (x.action == "defense") return false;
                    if (x.action == "create" && original.WasSent) { HorizonApp.Log.Warning("Unknown CREATE: use owner recovery and explicit reconciliation, never resend"); return false; }
                    string scope = Scope; var network = HorizonApp.Network;
                    lease.MarkSent(); // Flush before the first fallible HTTP; another actor/process cannot own this journal until disposal.
                    if (x.action == "delete")
                    {
                        var r = await SharedObjectsManager.Instance.DeleteObject(user, x.collection, x.objectId, x.revision);
                        if (!r.IsSuccess) return false;
                        lease.SaveServerSuccess(r.Data.objectId, x.revision, r.Data.usage.revision);
                    }
                    else
                    {
                        var r = x.action == "create" ? await SharedObjectsManager.Instance.CreateObject(user, x.collection, x.revision, x.GetInput(), Empty(x.groupId))
                            : await SharedObjectsManager.Instance.UpdateObject(user, x.collection, x.objectId, x.revision, x.GetInput());
                        if (!r.IsSuccess) return false;
                        lease.SaveServerSuccess(r.Data.@object.id, r.Data.@object.revision, r.Data.usage.revision);
                    }
                    if (!Current(scope, network)) return false;
                    _usage = null; _object = null; _usageScope = _objectScope = null; return true;
                }
            }
            finally { _busy = false; }
        }
        /// <summary>Read-only reconciliation of one explicit owner page. A changed/continued page needs another explicit inspection.
        /// The old usage CAS must be permanently fenced before an uncertain CREATE can be acknowledged.</summary>
        public Task<bool> ReconcileCreateAfterOwnerReview() => ReconcileCreatePage(null);
        public Task<bool> ReviewNextCreateOwnerPage(string cursor) => ReconcileCreatePage(cursor);
        private async Task<bool> ReconcileCreatePage(string cursor)
        {
            string user = User; if (user == null || _busy) return false; var store = Store(user); if (!store.HasIntent) return false;
            _busy = true;
            try
            {
                using (var lease = store.AcquireSend())
                {
                    var x = lease.Prepared.Value;
                    if (lease.Prepared.HasSuccess || x.action != "create" || !lease.Prepared.WasSent) return false;
                    string scope = Scope; var network = HorizonApp.Network;
                    if (cursor == null) { _recoveryIds.Clear(); _recoveryHash = lease.Prepared.Hash; _recoveryScope = scope; _nextRecoveryCursor = null; }
                    else if (cursor != _nextRecoveryCursor || _recoveryHash != lease.Prepared.Hash || _recoveryScope != scope) return false;
                    var before = await SharedObjectsManager.Instance.GetRecoveryUsage(user, x.collection);
                    if (!before.IsSuccess || !Current(scope, network) || cursor != null && before.Data.revision != _recoveryUsageRevision) return false;
                    var page = await SharedObjectsManager.Instance.QueryRecoveryObjects(user, x.collection, new SharedQuery { mode = SharedQueryMode.Owner, ownerId = user, groupId = Empty(x.groupId), limit = 50, cursor = cursor });
                    if (!page.IsSuccess || !Current(scope, network)) return false;
                    var after = await SharedObjectsManager.Instance.GetRecoveryUsage(user, x.collection);
                    if (!after.IsSuccess || !Current(scope, network) || before.Data.revision != after.Data.revision || before.Data.count != after.Data.count || after.Data.revision <= x.revision) { _nextRecoveryCursor = null; return false; }
                    foreach (var o in page.Data.objects) if (!_recoveryIds.Add(o.id)) { _nextRecoveryCursor = null; return false; }
                    _recoveryUsageRevision = after.Data.revision; _nextRecoveryCursor = page.Data.nextCursor;
                    if (_recoveryIds.Count > after.Data.count || page.Data.nextCursor == cursor && cursor != null) { _nextRecoveryCursor = null; return false; }
                    if (_nextRecoveryCursor != null) return false; // The next page is a separate, explicit game/player action.
                    // Group recovery reviews only the original group partition; usage may also count the owner's other groups.
                    if (string.IsNullOrEmpty(x.groupId) && _recoveryIds.Count != after.Data.count) return false;
                    // The completed consistent partition review permanently fences the old usage CAS; it does not infer which writer advanced it.
                    lease.SaveReconciled(0, after.Data.revision, false); return true;
                }
            }
            finally { _busy = false; }
        }
        public async Task<bool> ReconcileOriginalObjectAfterReview()
        {
            string user = User; if (user == null || _busy) return false; var store = Store(user); if (!store.HasIntent) return false;
            _busy = true;
            try
            {
                using (var lease = store.AcquireSend())
                {
                    var x = lease.Prepared.Value; if (lease.Prepared.HasSuccess || x.action == "create" || x.action == "defense") return false;
                    string scope = Scope; var network = HorizonApp.Network;
                    var r = await SharedObjectsManager.Instance.GetObject(user, x.collection, x.objectId);
                    if (!Current(scope, network) || !r.IsSuccess && r.ErrorCode != "OBJECT_NOT_FOUND") return false;
                    bool missing = !r.IsSuccess; long revision = missing ? 0 : r.Data.@object.revision;
                    if (!missing && revision <= x.revision) return false;
                    lease.SaveReconciled(revision, 0, missing); return true;
                }
            }
            finally { _busy = false; }
        }
        public void AcknowledgeSavedResult()
        {
            string user = User; if (user != null && !_busy) Store(user).AcknowledgeSavedSuccess();
        }
        /// <summary>Freeze the selected opponent's original bytes, ID, revision and digest before the guarded VA start.
        /// Unknown starts remain durable and block replacement; recovery never invents another run or trusts a client winner.</summary>
        public async Task<bool> StartDefense(SharedObject selected)
        {
            string user = User; if (user == null || _busy || selected == null || selected.collection != collection || selected.data == null) return false;
            var store = Store(user); if (store.HasIntent || ValidatedActionsManager.Instance.HasActiveRun || ValidatedActionsManager.Instance.HasPendingOperation) return false;
            store.SaveNew(SharedObjectsSampleStore.NewIntent(projectId, user, "defense", collection, selected.id, selected.groupId, selected.revision, new SharedObjectInput { data = selected.data }));
            _busy = true;
            try
            {
                using (var lease = store.AcquireSend())
                {
                    var x = lease.Prepared.Value; byte[] bytes = x.GetInput().data; string scope = Scope; var network = HorizonApp.Network;
                    lease.MarkSent();
                    var run = await ValidatedActionsManager.Instance.StartRun(context: new ValidatedRunContext { gameVersion = Application.version, contentVersion = x.objectId + ":" + x.revision, simulationVersion = "defense-v1", contentDigest = ValidatedActionsManager.ComputeInputLogHash(bytes), initialState = bytes });
                    if (run == null || !Current(scope, network)) return false;
                    lease.SaveRunId(run.runId); return true;
                }
            }
            finally { _busy = false; }
        }
        public async Task<bool> SubmitOriginalDefense(byte[] originalInputLog)
        {
            string user = User; if (user == null || _busy || originalInputLog == null) return false; byte[] frozenLog = (byte[])originalInputLog.Clone(); var store = Store(user); if (!store.HasIntent) return false;
            _busy = true;
            try
            {
                using (var lease = store.AcquireSend())
                {
                    var x = lease.Prepared.Value; if (x.action != "defense" || lease.Prepared.HasSuccess) return false;
                    string runId = lease.GetRunId(); if (runId == null || ValidatedActionsManager.Instance.CurrentRun?.runId != runId) return false;
                    string scope = Scope; var network = HorizonApp.Network;
                    var result = await ValidatedActionsManager.Instance.SubmitValidated(0, frozenLog);
                    if (result == null || result.runId != runId || !Current(scope, network)) return false;
                    lease.SaveServerSuccess(x.objectId, x.revision, 0);
                    // Only the returned VA state is authoritative. Ordinary Inbox delivery belongs to a trusted backend with its own durable original UUID.
                    return true;
                }
            }
            finally { _busy = false; }
        }
        private static string Empty(string s) => string.IsNullOrEmpty(s) ? null : s;
        private bool Current(string scope, NetworkService network) => User != null && scope == Scope && ReferenceEquals(network, HorizonApp.Network);
        private void OnDisable() { _generation++; _usage = null; _object = null; _usageScope = _objectScope = null; _nextRecoveryCursor = _recoveryHash = _recoveryScope = null; _recoveryIds.Clear(); }
    }
}
