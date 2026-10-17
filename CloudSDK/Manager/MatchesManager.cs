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
    /// <summary>Explicit asynchronous Matches. Every write sends the original revision/body exactly once.
    /// There is no automatic polling, timeout advancement, report, retry or replacement ranked run.</summary>
    public class MatchesManager : BaseManager<MatchesManager>
    {
        private const string Endpoint = "/api/v1/app/matches";
        private readonly SemaphoreSlim _requests = new SemaphoreSlim(1, 1);
        private NetworkService _network;
        private string _scope;
        private long _epoch;
        private readonly Dictionary<string, object> _cache = new Dictionary<string, object>();
        private readonly Dictionary<string, string> _sourceRuns = new Dictionary<string, string>();
        private readonly Queue<string> _sourceRunOrder = new Queue<string>();
        private readonly Dictionary<string, Refresh> _refresh = new Dictionary<string, Refresh>();
        private readonly Dictionary<string, long> _revisions = new Dictionary<string, long>(), _times = new Dictionary<string, long>();
        private readonly Queue<string> _cacheOrder = new Queue<string>(), _refreshOrder = new Queue<string>(), _revisionOrder = new Queue<string>(), _timeOrder = new Queue<string>();
        private sealed class Refresh { internal double At; internal int Seconds; }
        private string _rankedMatch, _rankedRun; private long _rankedFinal;
        internal Func<double> NowSeconds = () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        public string LastErrorCode { get; private set; }
        protected override void RegisterEvents()
        {
            RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => ResetIdentity());
            RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => ResetIdentity());
        }
        protected override void OnDisable() { ResetIdentity(); base.OnDisable(); }
        /// <summary>Invalidate private content and queued responses, retaining bounded revision/time/cadence fences and an actual current ranked-run binding.</summary>
        public void ClearCache() { SyncIdentity(); _epoch++; InvalidateContent(); }
        public T GetCached<T>(string identity) where T : class
        { SyncIdentity(); return identity != null && _cache.TryGetValue(identity, out var v) && v is T typed ? MatchesJson.Clone(typed) : null; }
        public double GetRefreshDelaySeconds(string identity)
        { SyncIdentity(); return identity != null && _refresh.TryGetValue(identity, out var r) ? Math.Max(0, r.Seconds - (NowSeconds() - r.At)) : 0; }
        public Task<MatchesResult<MyMatches>> GetMatches(string userId, bool recovery = false)
            => Call<MyMatches>(userId, "ledger", Endpoint + "?userId=" + Escape(userId), null, null, x => Same(x.userId, userId), recovery);
        public Task<MatchesResult<MatchLimitsView>> GetLimits(string userId)
            => Call<MatchLimitsView>(userId, "limits", Endpoint + "/limits?userId=" + Escape(userId), null, null, x => true);
        public Task<MatchesResult<MatchPage>> FindOpenMatches(string userId, long minStrength, long maxStrength, int limit = 20, string cursor = null)
        {
            try
            {
                if (!ItemsJson.Safe(minStrength) || !ItemsJson.Safe(maxStrength) || minStrength > maxStrength || limit < 1 || limit > 50
                    || cursor != null && (cursor.Length == 0 || Encoding.UTF8.GetByteCount(cursor) > 16384)) return Invalid<MatchPage>();
                if (cursor != null) ItemsJson.Quote(cursor);
                string path = Endpoint + "/open?userId=" + Escape(userId) + "&minStrength=" + N(minStrength) + "&maxStrength=" + N(maxStrength) + "&limit=" + N(limit) + (cursor == null ? "" : "&cursor=" + Escape(cursor));
                return Call<MatchPage>(userId, "open", path, null, null, x => x.matches.Length <= limit && x.matches.All(m => m.visibility == "open" && m.status == "lobby" && m.strength >= minStrength && m.strength <= maxStrength), cursor != null);
            }
            catch (FormatException) { return Invalid<MatchPage>(); }
        }
        public Task<MatchesResult<MatchRead>> GetMatch(string userId, string matchId, bool recovery = false)
        {
            if (!FriendsJson.IsUuid(matchId)) return Invalid<MatchRead>();
            return Call<MatchRead>(userId, MatchKey(matchId), Path(matchId) + "?userId=" + Escape(userId), null, null,
                x => Same(x.match.id, matchId) && AuthorizedView(x.match, userId), recovery);
        }
        public Task<MatchesResult<MatchCreated>> CreateMatch(string userId, long expectedPlayerRevision, MatchCreateInput input)
        {
            try
            {
                if (!WritableRevision(expectedPlayerRevision)) return Invalid<MatchCreated>();
                string body = MatchesJson.CreateJson(userId, input); var original = JsonFragments.Object(body, maxBytes: MatchesJson.TransportBytes);
                return Call<MatchCreated>(userId, "create", Endpoint, body, expectedPlayerRevision,
                    x => x.playerRevision == expectedPlayerRevision + 1 && x.match.revision == 1 && Same(x.match.creatorId, userId) && x.match.status == "lobby"
                        && AuthorizedView(x.match, userId) && x.match.name == JsonFragments.String(original["name"]) && x.match.dataBase64 == JsonFragments.String(original["dataBase64"])
                        && x.match.capacity == ItemsJson.Integer(original["capacity"]) && x.match.visibility == JsonFragments.String(original["visibility"])
                        && x.match.strength == ItemsJson.Integer(original["strength"], true) && x.match.turnSeconds == ItemsJson.Integer(original["turnSeconds"])
                        && x.match.timeoutPolicy == JsonFragments.String(original["timeoutPolicy"]) && x.match.ranked == (original["ranked"] == "true")
                        && x.match.participantIds.Length == 1 && Same(x.match.participantIds[0], userId)
                        && x.match.invitedIds.OrderBy(i => i, StringComparer.Ordinal).SequenceEqual(JsonFragments.Array(original["inviteeIds"]).Select(JsonFragments.String).OrderBy(i => i, StringComparer.Ordinal)));
            }
            catch (FormatException) { return Invalid<MatchCreated>(); }
        }
        public Task<MatchesResult<MatchRead>> JoinMatch(string userId, string matchId, long expectedRevision) => Action(userId, matchId, expectedRevision, "join");
        public Task<MatchesResult<MatchRead>> DeclineInvite(string userId, string matchId, long expectedRevision) => Action(userId, matchId, expectedRevision, "decline");
        public Task<MatchesResult<MatchRead>> AdvanceTimeout(string userId, string matchId, long expectedRevision) => Action(userId, matchId, expectedRevision, "timeout");
        public Task<MatchesResult<MatchRead>> LeaveMatch(string userId, string matchId, long expectedRevision) => Action(userId, matchId, expectedRevision, "leave");
        public Task<MatchesResult<MatchRead>> CancelMatch(string userId, string matchId, long expectedRevision) => Action(userId, matchId, expectedRevision, "cancel");
        public Task<MatchesResult<MatchRead>> StartMatch(string userId, string matchId, long expectedRevision, IList<string> turnOrder)
        {
            try
            {
                var order = MatchesJson.CanonicalIds(turnOrder); if (order.Length < 2) return Invalid<MatchRead>();
                return Action(userId, matchId, expectedRevision, "start", ",\"turnOrder\":" + MatchesJson.IdsJson(order), x => x.turnOrder.SequenceEqual(order));
            }
            catch (FormatException) { return Invalid<MatchRead>(); }
        }
        public Task<MatchesResult<MatchRead>> SubmitTurn(string userId, string matchId, long expectedRevision, byte[] data, bool finish = false)
        {
            try
            {
                string encoded = MatchesJson.Encode(data, MatchLimits.MaxStateBytes);
                return Action(userId, matchId, expectedRevision, "turn", ",\"dataBase64\":" + encoded + ",\"finish\":" + (finish ? "true" : "false"), x => ItemsJson.Quote(x.dataBase64) == encoded);
            }
            catch (FormatException) { return Invalid<MatchRead>(); }
        }
        private Task<MatchesResult<MatchRead>> Action(string user, string match, long revision, string action, string fields = "", Func<MatchView, bool> extra = null)
        {
            try
            {
                if (!FriendsJson.IsUuid(match) || !WritableRevision(revision) || revision == 0) return Invalid<MatchRead>();
                string body = MatchesJson.UserJson(user) + fields + "}";
                return Call<MatchRead>(user, MatchKey(match), Path(match) + "/" + action, body, revision,
                    x => Same(x.match.id, match) && x.match.revision == revision + 1 && (AuthorizedView(x.match, user) || (action == "leave" || action == "decline") && x.match.data == null) && (extra == null || extra(x.match)));
            }
            catch (FormatException) { return Invalid<MatchRead>(); }
        }
        public Task<MatchesResult<MatchReported>> ReportResult(string userId, string matchId, long finalStateRevision, byte[] result, IList<string> winnerIds)
        {
            try
            {
                if (!FriendsJson.IsUuid(matchId) || !ItemsJson.Revision(finalStateRevision) || finalStateRevision == 0) return Invalid<MatchReported>();
                string encoded = MatchesJson.Encode(result, 256); string[] winners = MatchesJson.CanonicalIds(winnerIds).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                string body = MatchesJson.UserJson(userId) + ",\"finalStateRevision\":" + N(finalStateRevision) + ",\"resultBase64\":" + encoded + ",\"winnerIds\":" + MatchesJson.IdsJson(winners) + "}";
                return Call<MatchReported>(userId, MatchKey(matchId), Path(matchId) + "/result", body, null,
                    x => Same(x.match.id, matchId) && AuthorizedView(x.match, userId) && x.match.finalStateRevision == finalStateRevision
                        && x.match.reports.Any(r => Same(r.userId, userId) && r.finalStateRevision == finalStateRevision && ItemsJson.Quote(r.resultBase64) == encoded && r.winnerIds.SequenceEqual(winners)));
            }
            catch (FormatException) { return Invalid<MatchReported>(); }
        }
        /// <summary>Read an actual previously adopted matching current run locally. Never creates, replaces or resumes a ticket.</summary>
        public ValidatedRun GetCurrentRankedRun(string userId, string matchId, long finalStateRevision)
        {
            if (!Begin(userId, out _, out _) || !Same(_rankedMatch, matchId) || _rankedFinal != finalStateRevision) return null;
            var run = ValidatedActionsManager.Instance.CurrentRun;
            return run != null && Same(run.runId, _rankedRun) ? MatchesJson.Clone(run) : null;
        }
        public async Task<MatchesResult<ValidatedRun>> StartRankedRun(string userId, string matchId, long finalStateRevision)
        {
            if (!FriendsJson.IsUuid(userId) || !FriendsJson.IsUuid(matchId) || !ItemsJson.Revision(finalStateRevision) || finalStateRevision == 0) return Failure<ValidatedRun>("INVALID_REQUEST");
            if (!Begin(userId, out string scope, out long epoch)) return Failure<ValidatedRun>(LastErrorCode);
            // Acquire before queueing: generic start, Shared defense and Match-ranked start own the same slot.
            using (var lease = ValidatedActionsManager.Instance.AcquireRunStart(userId, out string error))
            {
                if (lease == null) return Failure<ValidatedRun>(error);
                await _requests.WaitAsync();
                try
                {
                    if (!IsCurrent(scope, epoch) || !lease.IsCurrent) return Failure<ValidatedRun>("STALE_RESPONSE");
                    string body = MatchesJson.UserJson(userId) + ",\"finalStateRevision\":" + N(finalStateRevision) + "}";
                    var r = await lease.Network.PostOnceAsync<MatchRankedRunResponse>(Path(matchId) + "/ranked-run", new MatchesJsonRequest(body), true);
                    if (!IsCurrent(scope, epoch) || !lease.IsCurrent) return Failure<ValidatedRun>("STALE_RESPONSE");
                    if (!r.IsSuccess || r.Data == null) return Failure<ValidatedRun>(ResponseCode(r), r.Error, status: r.StatusCode);
                    var adopted = MatchesJson.Clone<ValidatedRun>(r.Data);
                    string binding=SourceBinding(matchId,finalStateRevision);
                    if(_sourceRuns.TryGetValue(binding,out var originalRun)&&!Same(originalRun,adopted.runId))return Failure<ValidatedRun>("INVALID_RESPONSE");
                    if (!lease.TryAdoptRun(adopted)) return Failure<ValidatedRun>("STALE_RESPONSE");
                    Put(_sourceRuns,_sourceRunOrder,binding,adopted.runId,128);
                    _rankedMatch = matchId; _rankedFinal = finalStateRevision; _rankedRun = adopted.runId; InvalidateContent();
                    LastErrorCode = null;
                    HorizonApp.Log.Info("Ranked Match run started");
                    if (!IsCurrent(scope, epoch) || !lease.IsCurrent) return Failure<ValidatedRun>("STALE_RESPONSE");
                    HorizonApp.Events.Publish(EventKeys.ValidatedRunStarted, MatchesJson.Clone(adopted));
                    if (!IsCurrent(scope, epoch) || !lease.IsCurrent) return Failure<ValidatedRun>("STALE_RESPONSE");
                    return new MatchesResult<ValidatedRun> { IsSuccess = true, Data = MatchesJson.Clone(adopted), HttpStatusCode = r.StatusCode };
                }
                finally { _requests.Release(); }
            }
        }
        private async Task<MatchesResult<T>> Call<T>(string user, string resource, string path, string body, long? revision, Func<T, bool> valid, bool bypassCadence = false) where T : class
        {
            if (!Begin(user, out string scope, out long epoch)) return Failure<T>(LastErrorCode);
            string identity = Hash(scope + "\n" + path + "\n" + body); var network = _network;
            await _requests.WaitAsync();
            try
            {
                if (!IsCurrent(scope, epoch)) return Failure<T>("STALE_RESPONSE");
                double delay = GetRefreshDelaySeconds(identity);
                if (body == null && !bypassCadence && delay > 0) return Failure<T>("REFRESH_TOO_SOON", identity: identity, delay: delay);
                if (body != null) InvalidateContent();
                var headers = revision.HasValue ? new Dictionary<string, string> { ["If-Match"] = N(revision.Value) } : null;
                var response = body == null ? await network.GetAsync<T>(path, true) : await network.PostOnceAsync<T>(path, new MatchesJsonRequest(body), true, headers);
                if (!IsCurrent(scope, epoch)) return Failure<T>("STALE_RESPONSE");
                if (!response.IsSuccess)
                {
                    if (response.StatusCode == 401 || response.StatusCode == 403 || response.StatusCode == 404) InvalidateContent();
                    return Failure<T>(ResponseCode(response), response.Error, identity, status: response.StatusCode);
                }
                if (response.Data == null || !valid(response.Data)) return Failure<T>("INVALID_RESPONSE", identity: identity);
                var data = response.Data; long time = MatchesJson.Millis(ServerTime(data)); var versions = Versions(data);
                var sources=KnownSources(data).ToArray();
                if(sources.Any(x=>_sourceRuns.TryGetValue(x.Key,out var original)&&!Same(original,x.Value)))return Failure<T>("STALE_RESPONSE",identity:identity);
                if (_times.TryGetValue(resource, out var previousTime) && time < previousTime
                    || versions.Any(v => _revisions.TryGetValue(v.Key, out var old) && v.Value < old || _times.TryGetValue(v.Key, out var oldTime) && time < oldTime)) return Failure<T>("STALE_RESPONSE", identity: identity);
                Put(_times, _timeOrder, resource, time, 512);
                foreach (var v in versions) { Put(_revisions, _revisionOrder, v.Key, v.Value, 512); Put(_times, _timeOrder, v.Key, time, 512); }
                foreach(var source in sources)Put(_sourceRuns,_sourceRunOrder,source.Key,source.Value,128);
                Put(_cache, _cacheOrder, identity, MatchesJson.Clone(data), 8);
                if (body == null) Put(_refresh, _refreshOrder, identity, new Refresh { At = NowSeconds(), Seconds = Poll(data) }, 512);
                LastErrorCode = null; return new MatchesResult<T> { IsSuccess = true, Data = MatchesJson.Clone(data), Identity = identity, HttpStatusCode = response.StatusCode };
            }
            finally { _requests.Release(); }
        }
        private static bool AuthorizedView(MatchView match, string user) => match.participantIds.Any(x => Same(x, user)) ? match.data != null : match.invitedIds.Any(x => Same(x, user)) && match.data == null;
        private static string SourceBinding(string match,long revision)=>match.ToLowerInvariant()+":"+N(revision);
        private static IEnumerable<KeyValuePair<string,string>> KnownSources(object data)
        {
            if(data is MyMatches ledger)
                foreach(var source in ledger.pendingResults)
                    if(source.ownRankedRunId!=null)yield return new KeyValuePair<string,string>(SourceBinding(source.matchId,source.finalStateRevision),source.ownRankedRunId);
            if(data is MatchRead read&&read.match.ownRankedRunId!=null&&read.match.finalStateRevision.HasValue)
                yield return new KeyValuePair<string,string>(SourceBinding(read.match.id,read.match.finalStateRevision.Value),read.match.ownRankedRunId);
        }
        private static IEnumerable<MatchSummary> Summaries(object data) => data is MyMatches m ? m.active.Concat(m.invites).Concat(m.recent) : data is MatchPage p ? p.matches : data is MatchRead r ? new[] { r.match } : Array.Empty<MatchSummary>();
        private static Dictionary<string, long> Versions(object data)
        {
            var values = new Dictionary<string, long>();
            if (data is MyMatches m) values["ledger"] = m.revision;
            if (data is MatchCreated c) values["ledger"] = c.playerRevision;
            if (data is MatchLimitsView l) values["config"] = l.config.revision;
            foreach (var s in Summaries(data)) values[MatchKey(s.id)] = s.revision;
            return values;
        }
        private static string ServerTime(object data) => data is MyMatches m ? m.serverTime : data is MatchLimitsView l ? l.serverTime : data is MatchPage p ? p.serverTime : ((MatchRead)data).serverTime;
        private static int Poll(object data) => data is MyMatches m ? m.pollAfterSeconds : data is MatchLimitsView l ? l.pollAfterSeconds : data is MatchPage p ? p.pollAfterSeconds : ((MatchRead)data).pollAfterSeconds;
        private static string ResponseCode<T>(NetworkResponse<T> response) where T : class => response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode);
        private static void Put<T>(Dictionary<string, T> values, Queue<string> order, string key, T value, int limit)
        { if (!values.ContainsKey(key)) order.Enqueue(key); values[key] = value; while (values.Count > limit) values.Remove(order.Dequeue()); }
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
            _epoch++; _network = null; _scope = null; InvalidateContent(); _refresh.Clear(); _revisions.Clear(); _times.Clear();
            _refreshOrder.Clear(); _revisionOrder.Clear(); _timeOrder.Clear(); _rankedMatch = _rankedRun = null; LastErrorCode = null;
            _sourceRuns.Clear();_sourceRunOrder.Clear();
        }
        private void InvalidateContent() { _cache.Clear(); _cacheOrder.Clear(); }
        private bool IsCurrent(string scope, long epoch) { SyncIdentity(); return _scope == scope && _epoch == epoch; }
        private static bool WritableRevision(long value) => ItemsJson.Revision(value) && value < PlayerValuesManager.MaxSafeInteger;
        private static string MatchKey(string id) => "match:" + id.ToLowerInvariant();
        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Escape(string value) => Uri.EscapeDataString(value ?? "");
        private static string Path(string id) => Endpoint + "/" + Escape(id);
        private static string Hash(string value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        private Task<MatchesResult<T>> Invalid<T>() where T : class => Task.FromResult(Failure<T>("INVALID_REQUEST"));
        private MatchesResult<T> Failure<T>(string code, string error = null, string identity = null, double delay = 0, long status = 0) where T : class
        { LastErrorCode = code; return new MatchesResult<T> { ErrorCode = code, Error = error ?? code, Identity = identity, RetryAfterSeconds = delay, HttpStatusCode = status }; }
    }
}
