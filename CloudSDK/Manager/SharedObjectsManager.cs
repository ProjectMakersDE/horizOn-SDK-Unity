using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>Explicit bounded Shared Objects reads and single-attempt original If-Match writes. No polling or automatic pages.</summary>
    public class SharedObjectsManager : BaseManager<SharedObjectsManager>
    {
        private const string Endpoint = "/api/v1/app/shared-objects";
        private readonly SemaphoreSlim _requests = new SemaphoreSlim(1, 1);
        private NetworkService _network;
        private string _scope;
        private long _epoch;
        private readonly Dictionary<string, object> _cache = new Dictionary<string, object>();
        private readonly Dictionary<string, Refresh> _refresh = new Dictionary<string, Refresh>();
        private readonly Dictionary<string, long> _revisions = new Dictionary<string, long>(), _times = new Dictionary<string, long>();
        private readonly Dictionary<string, bool> _deleted = new Dictionary<string, bool>();
        private readonly Queue<string> _cacheOrder = new Queue<string>(), _refreshOrder = new Queue<string>(), _revisionOrder = new Queue<string>(), _timeOrder = new Queue<string>(), _deletedOrder = new Queue<string>();
        private sealed class Refresh { internal double At; internal int Seconds; }
        internal Func<double> NowSeconds = () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        public string LastErrorCode { get; private set; }
        protected override void RegisterEvents()
        {
            RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => ResetIdentity());
            RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => ResetIdentity());
        }
        protected override void OnDisable() { ResetIdentity(); base.OnDisable(); }
        /// <summary>Invalidate private content and queued responses, retaining bounded revision/time and cadence fences for this identity.</summary>
        public void ClearCache() { SyncIdentity(); _epoch++; _cache.Clear(); _cacheOrder.Clear(); }
        public T GetCached<T>(string identity) where T : class
        {
            SyncIdentity(); return identity != null && _cache.TryGetValue(identity, out var v) && v is T typed ? SharedObjectsJson.Clone(typed) : null;
        }
        public double GetRefreshDelaySeconds(string identity)
        {
            SyncIdentity(); return identity != null && _refresh.TryGetValue(identity, out var r) ? Math.Max(0, r.Seconds - (NowSeconds() - r.At)) : 0;
        }
        public Task<SharedObjectsResult<SharedCollectionPage>> GetCollections(string userId, string afterKey = null, int limit = 50, long? catalogRevision = null)
        {
            if (afterKey != null && !ItemsJson.Key(afterKey) || limit < 1 || limit > 50 || catalogRevision.HasValue && !ItemsJson.Revision(catalogRevision.Value)) return Invalid<SharedCollectionPage>();
            string path = Endpoint + "/collections?userId=" + Escape(userId) + "&limit=" + limit.ToString(CultureInfo.InvariantCulture)
                + (afterKey == null ? "" : "&afterKey=" + Escape(afterKey)) + (catalogRevision.HasValue ? "&catalogRevision=" + catalogRevision.Value.ToString(CultureInfo.InvariantCulture) : "");
            return Call<SharedCollectionPage>(userId, "collections", "", path, null, null, false, p =>
            {
                if (p.definitions.Length > limit || catalogRevision.HasValue && p.revision != catalogRevision.Value) return false;
                string previous = afterKey;
                foreach (var d in p.definitions) { if (d.revision > p.revision || previous != null && string.CompareOrdinal(d.key, previous) <= 0) return false; previous = d.key; }
                return p.nextAfterKey == null || p.definitions.Length != 0 && p.nextAfterKey == previous;
            }, afterKey != null);
        }
        public Task<SharedObjectsResult<SharedUsage>> GetUsage(string userId, string collection) => Usage(userId, collection, false);
        /// <summary>Explicit uncertain-create reconciliation only; bypasses cadence, never sends a write.</summary>
        public Task<SharedObjectsResult<SharedUsage>> GetRecoveryUsage(string userId, string collection) => Usage(userId, collection, true);
        private Task<SharedObjectsResult<SharedUsage>> Usage(string user, string collection, bool recovery)
        {
            if (!ItemsJson.Key(collection)) return Invalid<SharedUsage>();
            return Call<SharedUsage>(user, "usage", collection, Endpoint + "/" + collection + "/usage?userId=" + Escape(user), null, null, false,
                x => Same(x.userId, user) && x.collection == collection, recovery);
        }
        public Task<SharedObjectsResult<SharedObjectRead>> GetObject(string userId, string collection, string objectId)
        {
            if (!ItemsJson.Key(collection) || !FriendsJson.IsUuid(objectId)) return Invalid<SharedObjectRead>();
            return Call<SharedObjectRead>(userId, "get", collection, ObjectPath(collection, objectId) + "?userId=" + Escape(userId), null, null, false,
                x => Same(x.@object.id, objectId) && x.@object.collection == collection);
        }
        public Task<SharedObjectsResult<SharedQueryResult>> QueryObjects(string userId, string collection, SharedQuery query) => Query(userId, collection, query, false);
        /// <summary>Explicit recovery query restricted to the current player's original owner partition. No automatic follow-up pages.</summary>
        public Task<SharedObjectsResult<SharedQueryResult>> QueryRecoveryObjects(string userId, string collection, SharedQuery query)
        {
            if (query == null || query.mode != SharedQueryMode.Owner || !Same(query.ownerId, userId)) return Invalid<SharedQueryResult>();
            return Query(userId, collection, query, true);
        }
        private Task<SharedObjectsResult<SharedQueryResult>> Query(string user, string collection, SharedQuery query, bool recovery)
        {
            try
            {
                if (!ItemsJson.Key(collection)) return Invalid<SharedQueryResult>();
                // Freeze the query before any await. Later caller edits cannot alter the request or its validation.
                string body = SharedObjectsJson.QueryJson(user, query); var f = JsonFragments.Object(body);
                int limit = (int)ItemsJson.Integer(f["limit"]); string owner = f.ContainsKey("ownerId") ? JsonFragments.String(f["ownerId"]) : null;
                string group = f.ContainsKey("groupId") ? JsonFragments.String(f["groupId"]) : null; bool random = f["mode"] == "\"random\"";
                return Call<SharedQueryResult>(user, "query", collection, Endpoint + "/" + collection + "/query", body, null, false,
                    x => x.objects.Length <= limit && (!random || x.nextCursor == null) && x.objects.All(o => o.collection == collection && (owner == null || Same(o.ownerId, owner)) && (group == null || Same(o.groupId, group)) && SharedObjectsJson.MatchesQuery(o, f)), recovery || f.ContainsKey("cursor"));
            }
            catch (FormatException) { return Invalid<SharedQueryResult>(); }
        }
        public Task<SharedObjectsResult<SharedWriteResult>> CreateObject(string userId, string collection, long expectedUsageRevision, SharedObjectInput input, string groupId = null)
            => Write(userId, collection, null, expectedUsageRevision, input, groupId);
        public Task<SharedObjectsResult<SharedWriteResult>> UpdateObject(string userId, string collection, string objectId, long expectedRevision, SharedObjectInput input)
            => Write(userId, collection, objectId, expectedRevision, input, null);
        private Task<SharedObjectsResult<SharedWriteResult>> Write(string user, string collection, string id, long revision, SharedObjectInput input, string group)
        {
            try
            {
                if (!ItemsJson.Key(collection) || id != null && !FriendsJson.IsUuid(id) || !ItemsJson.Revision(revision) || revision == PlayerValuesManager.MaxSafeInteger) return Invalid<SharedWriteResult>();
                string body = SharedObjectsJson.InputJson(user, input, group, id == null); var original = JsonFragments.Object(body);
                return Call<SharedWriteResult>(user, id == null ? "create" : "update", collection, id == null ? Endpoint + "/" + collection + "/objects" : ObjectPath(collection, id), body, revision, true,
                    x => x.@object.collection == collection && x.usage.collection == collection && Same(x.usage.userId, x.@object.ownerId)
                        && (id == null ? Same(x.@object.ownerId, user) && Same(x.@object.groupId, group) && x.@object.revision == 1 && x.usage.revision == revision + 1 : Same(x.@object.id, id) && x.@object.revision == revision + 1)
                        && ItemsJson.Quote(x.@object.dataBase64) == original["dataBase64"] && SharedObjectsJson.SearchJson(x.@object.search) == original["search"] && SharedObjectsJson.PositionJson(x.@object.position) == original["position"]);
            }
            catch (FormatException) { return Invalid<SharedWriteResult>(); }
        }
        public Task<SharedObjectsResult<SharedDeleteResult>> DeleteObject(string userId, string collection, string objectId, long expectedRevision)
        {
            if (!ItemsJson.Key(collection) || !FriendsJson.IsUuid(objectId) || !ItemsJson.Revision(expectedRevision)) return Invalid<SharedDeleteResult>();
            return Call<SharedDeleteResult>(userId, "delete", collection, ObjectPath(collection, objectId) + "?userId=" + Escape(userId), null, expectedRevision, true,
                x => Same(x.objectId, objectId) && x.deleted && x.usage.collection == collection);
        }
        private async Task<SharedObjectsResult<T>> Call<T>(string user, string kind, string collection, string path, string body, long? revision, bool mutation, Func<T, bool> valid, bool bypassCadence = false) where T : class
        {
            if (!Begin(user, out string scope, out long epoch)) return Failure<T>(LastErrorCode);
            string identity = Hash(scope + "\n" + kind + "\n" + path + "\n" + body);
            var network = _network;
            await _requests.WaitAsync();
            try
            {
                if (!IsCurrent(scope, epoch)) return Failure<T>("STALE_RESPONSE");
                double delay = GetRefreshDelaySeconds(identity);
                if (!mutation && !bypassCadence && delay > 0) return Failure<T>("REFRESH_TOO_SOON", identity: identity, delay: delay);
                if (mutation) { _cache.Clear(); _cacheOrder.Clear(); }
                var headers = revision.HasValue ? new Dictionary<string, string> { ["If-Match"] = revision.Value.ToString(CultureInfo.InvariantCulture) } : null;
                NetworkResponse<T> response = kind == "delete" ? await network.DeleteOnceAsync<T>(path, true, headers)
                    : kind == "update" ? await network.PutOnceAsync<T>(path, new SharedObjectsJsonRequest(body), true, headers)
                    : kind == "create" ? await network.PostOnceAsync<T>(path, new SharedObjectsJsonRequest(body), true, headers)
                    : body == null ? await network.GetAsync<T>(path, true) : await network.PostAsync<T>(path, new SharedObjectsJsonRequest(body), true);
                if (!IsCurrent(scope, epoch)) return Failure<T>("STALE_RESPONSE");
                if (!response.IsSuccess)
                {
                    if (response.StatusCode == 401) ClearCache();
                    return Failure<T>(response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode), response.Error, identity);
                }
                if (response.Data == null || !valid(response.Data)) return Failure<T>("INVALID_RESPONSE", identity: identity);
                var data = response.Data; string serverTime = ServerTime(data); long time = SharedObjectsJson.Millis(serverTime);
                var versions = Versions(data); string resource = kind + ":" + collection;
                if (_times.TryGetValue(resource, out var previousTime) && time < previousTime || versions.Any(v => _revisions.TryGetValue(v.Key, out var previous) && v.Value < previous || _deleted.ContainsKey(v.Key))) return Failure<T>("STALE_RESPONSE", identity: identity);
                Put(_times, _timeOrder, resource, time, 512);
                foreach (var v in versions) Put(_revisions, _revisionOrder, v.Key, v.Value, 512);
                if (data is SharedDeleteResult deleted) Put(_deleted, _deletedOrder, ObjectKey(collection, deleted.objectId), true, 512);
                Put(_cache, _cacheOrder, identity, SharedObjectsJson.Clone(data), 8);
                if (!mutation) Put(_refresh, _refreshOrder, identity, new Refresh { At = NowSeconds(), Seconds = Poll(data) }, 512);
                LastErrorCode = null;
                return new SharedObjectsResult<T> { IsSuccess = true, Data = SharedObjectsJson.Clone(data), Identity = identity };
            }
            finally { _requests.Release(); }
        }
        private static Dictionary<string, long> Versions(object data)
        {
            var v = new Dictionary<string, long>();
            if (data is SharedCollectionPage c) v["catalog"] = c.revision;
            SharedUsage usage = data as SharedUsage ?? (data as SharedWriteResult)?.usage ?? (data as SharedDeleteResult)?.usage;
            if (usage != null) v["usage:" + usage.collection + ":" + usage.userId] = usage.revision;
            var objects = data is SharedQueryResult q ? q.objects : data is SharedObjectRead r ? new[] { r.@object } : data is SharedWriteResult w ? new[] { w.@object } : Array.Empty<SharedObject>();
            foreach (var o in objects) v[ObjectKey(o.collection, o.id)] = o.revision;
            return v;
        }
        private static string ObjectKey(string collection, string id) => "object:" + collection + ":" + id.ToLowerInvariant();
        private static string ServerTime(object data) => data is SharedCollectionPage c ? c.serverTime : data is SharedUsage u ? u.serverTime : data is SharedObjectRead r ? r.serverTime : data is SharedQueryResult q ? q.serverTime : data is SharedWriteResult w ? w.serverTime : ((SharedDeleteResult)data).serverTime;
        private static int Poll(object data) => data is SharedCollectionPage ? 120 : data is SharedUsage u ? u.pollAfterSeconds : data is SharedObjectRead r ? r.pollAfterSeconds : data is SharedQueryResult q ? q.pollAfterSeconds : data is SharedWriteResult w ? w.pollAfterSeconds : ((SharedDeleteResult)data).pollAfterSeconds;
        private static void Put<T>(Dictionary<string, T> values, Queue<string> order, string key, T value, int limit)
        {
            if (!values.ContainsKey(key)) order.Enqueue(key); values[key] = value;
            while (values.Count > limit) values.Remove(order.Dequeue());
        }
        private bool Begin(string user, out string scope, out long epoch)
        {
            SyncIdentity(); scope = _scope; epoch = _epoch; var current = UserManager.Instance.CurrentUser;
            LastErrorCode = current == null || !current.IsValid() || string.IsNullOrEmpty(_network.GetSessionToken()) || current.AccessToken != _network.GetSessionToken() ? "SESSION_REQUIRED"
                : !Same(current.UserId, user) ? "SESSION_FORBIDDEN" : !FriendsJson.IsUuid(user) ? "INVALID_REQUEST" : null;
            return LastErrorCode == null;
        }
        private void SyncIdentity()
        {
            var network = HorizonApp.Network; var user = UserManager.Instance.CurrentUser;
            string scope = user?.UserId + "|" + user?.AccessToken + "|" + network.GetSessionToken() + "|" + network.GetApiKey() + "|" + network.GetActiveHost() + "|" + network.ContextGeneration;
            if (ReferenceEquals(network, _network) && scope == _scope) return;
            ResetIdentity(); _network = network; _scope = scope;
        }
        private void ResetIdentity()
        {
            _epoch++; _network = null; _scope = null; _cache.Clear(); _refresh.Clear(); _revisions.Clear(); _times.Clear(); _deleted.Clear();
            _cacheOrder.Clear(); _refreshOrder.Clear(); _revisionOrder.Clear(); _timeOrder.Clear(); _deletedOrder.Clear(); LastErrorCode = null;
        }
        private bool IsCurrent(string scope, long epoch) { SyncIdentity(); return _scope == scope && _epoch == epoch; }
        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private static string Escape(string s) => Uri.EscapeDataString(s ?? "");
        private static string ObjectPath(string collection, string id) => Endpoint + "/" + collection + "/objects/" + Escape(id);
        private static string Hash(string value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        private Task<SharedObjectsResult<T>> Invalid<T>() where T : class => Task.FromResult(Failure<T>("INVALID_REQUEST"));
        private SharedObjectsResult<T> Failure<T>(string code, string error = null, string identity = null, double delay = 0) where T : class
        { LastErrorCode = code; return new SharedObjectsResult<T> { ErrorCode = code, Error = error ?? code, Identity = identity, RetryAfterSeconds = delay }; }
    }
}
