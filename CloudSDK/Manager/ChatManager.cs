using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>Hosted Chat, on-demand live snapshots and separate persistent device history.
    /// Writes are sent once. A missing operation never authorizes automatic resending.</summary>
    public sealed class ChatManager : BaseManager<ChatManager>
    {
        private const string Endpoint = "/api/v1/app/chat";
        private readonly SemaphoreSlim _requests = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, Watch> _watches = new Dictionary<string, Watch>();
        private sealed class Watch { internal string User, Channel; internal ChatSynced Snapshot; internal bool Busy, Dirty; internal double ReceivedAt; }
        private NetworkService _network; private RealtimeManager _realtime;
        private ChatHistoryStore _history; private string _scope, _owner;
        private readonly Dictionary<string, long> _channelArchiveGenerations = new Dictionary<string, long>();
        private long _epoch, _syncEpoch, _archiveGeneration;
        private long? _serverMillis; private double _clockReceived;
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void HorizonChatFlushStorage(string objectName);
#endif
        private void FlushBrowserStorage()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            HorizonChatFlushStorage(gameObject.name);
#endif
        }
        public void OnHorizonChatStorageError(string code) => LocalHistoryFailed?.Invoke(null, "LOCAL_HISTORY_IO_FAILED");
        public string GetServerTime()
        {
            SyncIdentity();
            return _serverMillis.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(_serverMillis.Value + (long)(Math.Max(0, NowSeconds() - _clockReceived) * 1000))
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture) : null;
        }
        private void RecordClock<T>(T value)
        {
            string stamp = value is ChatLimitsView limits ? limits.serverTime : value is ChatOpened opened ? opened.serverTime
                : value is ChatSynced synced ? synced.serverTime : value is ChatSent sent ? sent.serverTime
                : value is ChatOperation operation ? operation.serverTime : value is ChatReported report ? report.serverTime : null;
            if (stamp == null) return;
            long now = ChatJson.Millis(stamp);
            if (_serverMillis.HasValue) now = Math.Max(now, _serverMillis.Value + (long)(Math.Max(0, NowSeconds() - _clockReceived) * 1000));
            _serverMillis = now; _clockReceived = NowSeconds();
        }
        internal string HistoryRootOverride;
        internal Func<double> NowSeconds = () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        public string LastErrorCode { get; private set; }
        public event Action<string, ChatMessage[]> FeedChanged;
        public event Action<string> LocalHistoryChanged;
        public event Action<string, string> LocalHistoryFailed;
        public event Action<string, string> ChannelError;
        protected override void RegisterEvents()
        {
            RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => ResetIdentity());
            RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => SyncIdentity());
        }
        protected override void OnDisable()
        {
            ResetIdentity();
            if (_realtime != null) { _realtime.EventReceived -= OnEvent; _realtime.ResyncRequired -= OnResync; _realtime.ConnectionError -= OnConnectionError; _realtime = null; }
            base.OnDisable();
        }
        public Task<ChatResult<ChatLimitsView>> GetLimits(string userId)
            => Call(userId, null, Endpoint + "/limits?userId=" + Escape(userId), null, ChatJson.Limits, x => true);
        public Task<ChatResult<ChatOpened>> OpenChannel(string userId, string kind, string peerId = null, string groupId = null)
        {
            try
            {
                if (!new[] { "direct", "group", "project" }.Contains(kind) || (kind == "direct") != (peerId != null)
                    || (kind == "group") != (groupId != null) || peerId == userId) return Invalid<ChatOpened>();
                if (peerId != null) ChatJson.Id(peerId); if (groupId != null) ChatJson.Id(groupId);
                string body = User(userId) + ",\"kind\":" + ChatJson.Quote(kind) + (peerId == null ? "" : ",\"peerId\":" + ChatJson.Quote(peerId)) + (groupId == null ? "" : ",\"groupId\":" + ChatJson.Quote(groupId)) + "}";
                return Call(userId, null, Endpoint + "/channels/open", body, ChatJson.Opened, x => x.channel.kind == kind && x.channel.groupId == groupId
                    && (kind != "direct" || x.channel.peerIds.Contains(userId) && x.channel.peerIds.Contains(peerId)));
            }
            catch (FormatException) { return Invalid<ChatOpened>(); }
        }
        /// <summary>One explicit authorized snapshot, also usable without a live socket. previous must be
        /// the complete prior server window. Returned messages are composed into the complete new window.</summary>
        public Task<ChatResult<ChatSynced>> SyncChannel(string userId, string channelId, ChatSynced previous = null)
        {
            try
            {
                ChatJson.Id(channelId); var old = previous == null ? null : ChatJson.Copy(previous);
                if (old != null && (old.channel.id != channelId || old.visibleMessageIds.Length > 20 || old.visibleMessageIds.Distinct().Count() != old.visibleMessageIds.Length
                    || !old.messages.Select(m => m.id).SequenceEqual(old.visibleMessageIds) || old.messages.Any(m => m.channelId != channelId))) return Invalid<ChatSynced>();
                if (old != null) foreach (var m in old.messages) ChatJson.Message(ChatJson.MessageJson(m));
                string body = User(userId) + ",\"epoch\":" + (old == null ? "null" : ChatJson.Quote(ChatJson.Id(old.channel.epoch)))
                    + ",\"knownMessageIds\":[" + string.Join(",", (old?.visibleMessageIds ?? Array.Empty<string>()).Select(x => ChatJson.Quote(ChatJson.Id(x)))) + "]}";
                return Call(userId, channelId, Path(channelId) + "/sync", body, ChatJson.Synced, result =>
                {
                    if (result.channel.id != channelId || result.channel.kind == "direct" && !result.channel.peerIds.Contains(userId)) return false;
                    if (old != null && (ChatJson.Millis(result.serverTime) < ChatJson.Millis(old.serverTime)
                        || old.channel.epoch == result.channel.epoch && result.channel.revision < old.channel.revision)) return false;
                    var cache = old != null && old.channel.epoch == result.channel.epoch ? old.messages.ToDictionary(m => m.id, ChatJson.Copy) : new Dictionary<string, ChatMessage>();
                    var newIds = result.messages.Select(m => m.id).ToHashSet();
                    if (newIds.Any(cache.ContainsKey) || !newIds.SetEquals(result.visibleMessageIds.Where(id => !cache.ContainsKey(id)))) return false;
                    foreach (var message in result.messages) cache[message.id] = message;
                    result.messages = result.visibleMessageIds.Select(id => cache[id]).ToArray();
                    long time = ChatJson.Millis(result.serverTime);
                    return result.messages.All(m => ChatJson.Millis(m.expiresAt) > time && ChatJson.Millis(m.createdAt) <= time)
                        && result.messages.Zip(result.messages.Skip(1), (a, b) => ChatJson.Millis(a.createdAt) <= ChatJson.Millis(b.createdAt)).All(x => x);
                }, result => Archive(channelId, result.messages), snapshot: true);
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is NullReferenceException) { return Invalid<ChatSynced>(); }
        }
        public Task<ChatResult<ChatSent>> SendMessage(string userId, string channelId, string operationId, string issuedAt, string text)
        {
            try
            {
                ChatJson.Id(channelId); ChatJson.Id(operationId); ChatJson.Time(ChatJson.Quote(issuedAt)); ChatJson.Text(text);
                string body = User(userId) + ",\"operationId\":" + ChatJson.Quote(operationId) + ",\"issuedAt\":" + ChatJson.Quote(issuedAt) + ",\"text\":" + ChatJson.Quote(text) + "}";
                return Call(userId, channelId, Path(channelId) + "/messages", body, ChatJson.Sent,
                    x => x.receipt.channelId == channelId && x.receipt.operationId == operationId && x.receipt.issuedAt == issuedAt,
                    x => Archive(channelId, new[] { new ChatMessage { id = x.receipt.messageId, channelId = channelId, authorId = userId, text = text, createdAt = x.receipt.createdAt, expiresAt = x.receipt.expiresAt } }));
            }
            catch (FormatException) { return Invalid<ChatSent>(); }
        }
        public Task<ChatResult<ChatOperation>> GetOperation(string userId, string operationId)
        {
            try { ChatJson.Id(operationId); return Call(userId, null, Endpoint + "/operations/" + operationId + "?userId=" + Escape(userId), null, ChatJson.Operation, x => x.receipt.operationId == operationId); }
            catch (FormatException) { return Invalid<ChatOperation>(); }
        }
        public Task<ChatResult<ChatReported>> ReportMessage(string userId, string channelId, string messageId, string reason)
        {
            try
            {
                ChatJson.Id(channelId); ChatJson.Id(messageId); ChatJson.Text(reason, 240, 960);
                return Call(userId, channelId, Path(channelId) + "/messages/" + messageId + "/report", User(userId) + ",\"reason\":" + ChatJson.Quote(reason) + "}", ChatJson.Reported, x => true);
            }
            catch (FormatException) { return Invalid<ChatReported>(); }
        }
        public bool WatchChannel(string userId, string channelId)
        {
            try { ChatJson.Id(channelId); } catch (FormatException) { LastErrorCode = "INVALID_REQUEST"; return false; }
            if (!Begin(userId, out _, out _)) return false;
            if (_watches.ContainsKey(channelId)) return true;
            if (_watches.Count >= 32) { LastErrorCode = "SUBSCRIPTION_LIMIT_REACHED"; return false; }
            EnsureRealtime();
            if (!_realtime.Subscribe("chat:" + channelId)) { LastErrorCode = "SUBSCRIPTION_LIMIT_REACHED"; return false; }
            _watches[channelId] = new Watch { User = userId, Channel = channelId };
            if (_realtime.IsSubscribed("chat:" + channelId)) Schedule(_watches[channelId]);
            return true;
        }
        public void UnwatchChannel(string channelId)
        {
            if (!_watches.TryGetValue(channelId, out var watch)) return;
            _watches.Remove(channelId); _realtime?.Unsubscribe("chat:" + channelId); Hide(watch);
        }
        public ChatMessage[] GetFeed(string channelId)
        {
            SyncIdentity();
            if (!_watches.TryGetValue(channelId, out var watch) || watch.Snapshot == null || _realtime == null || !_realtime.IsSubscribed("chat:" + channelId)) return Array.Empty<ChatMessage>();
            return CurrentFeed(watch).Select(ChatJson.Copy).ToArray();
        }
        public void ClearCache() { SyncIdentity(); _syncEpoch++; foreach (var w in _watches.Values.ToArray()) Hide(w); }
        public ChatResult<ChatHistoryPage> ReadLocalHistory(string channelId, string before = null, int limit = 50)
        {
            if (!LocalIdentity()) return Failure<ChatHistoryPage>("SESSION_REQUIRED");
            try { var page = History().Read(channelId, before, limit); LastErrorCode = null; return new ChatResult<ChatHistoryPage> { IsSuccess = true, Data = page }; }
            catch (Exception e) when (LocalError(e)) { string code = StorageCode(e); LocalHistoryFailed?.Invoke(channelId, code); return Failure<ChatHistoryPage>(code); }
        }
        public bool ClearLocalHistory(string channelId)
        {
            if (!LocalIdentity()) { LastErrorCode = "SESSION_REQUIRED"; return false; }
            try { ChatJson.Id(channelId); _channelArchiveGenerations[channelId] = ChannelGeneration(channelId) + 1; History().Clear(channelId); FlushBrowserStorage(); LastErrorCode = null; LocalHistoryChanged?.Invoke(channelId); return true; }
            catch (Exception e) when (LocalError(e)) { LastErrorCode = StorageCode(e); LocalHistoryFailed?.Invoke(channelId, LastErrorCode); return false; }
        }
        public bool ClearAllLocalHistory()
        {
            if (!LocalIdentity()) { LastErrorCode = "SESSION_REQUIRED"; return false; }
            _archiveGeneration++;
            try { History().ClearAll(); FlushBrowserStorage(); LastErrorCode = null; LocalHistoryChanged?.Invoke(null); return true; }
            catch (Exception e) when (LocalError(e)) { LastErrorCode = StorageCode(e); LocalHistoryFailed?.Invoke(null, LastErrorCode); return false; }
        }
        private async Task<ChatResult<T>> Call<T>(string user, string channel, string path, string body, Func<string, T> parser, Func<T, bool> valid, Action<T> accepted = null, bool snapshot = false) where T : class
        {
            if (!Begin(user, out string scope, out long epoch)) return Failure<T>(LastErrorCode);
            long archiveGeneration = _archiveGeneration, channelGeneration = ChannelGeneration(channel), syncEpoch = _syncEpoch; var network = _network;
            await _requests.WaitAsync();
            try
            {
                if (!Current(scope, epoch) || snapshot && syncEpoch != _syncEpoch) return Failure<T>("STALE_RESPONSE");
                // NetworkService uses the same strict parser for native and WebGL transports.
                var response = body == null ? await network.GetAsync<T>(path, true) : await network.PostOnceAsync<T>(path, new ChatJsonRequest(body), true);
                if (!Current(scope, epoch) || snapshot && syncEpoch != _syncEpoch) return Failure<T>("STALE_RESPONSE");
                if (!response.IsSuccess)
                {
                    if (channel != null && response.ErrorCode != "CHAT_MUTED" && response.ErrorCode != "MESSAGE_NOT_FOUND"
                        && (response.StatusCode == 401 || response.StatusCode == 403 || response.StatusCode == 404) && _watches.TryGetValue(channel, out var watch)) Hide(watch);
                    return Failure<T>(response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode), response.Error, response.StatusCode);
                }
                try { if (response.Data == null || !valid(response.Data)) return Failure<T>("INVALID_RESPONSE", status: response.StatusCode); }
                catch (Exception e) when (e is FormatException || e is ArgumentException || e is KeyNotFoundException) { return Failure<T>("INVALID_RESPONSE", status: response.StatusCode); }
                LastErrorCode = null; RecordClock(response.Data);
                if (archiveGeneration == _archiveGeneration && channelGeneration == ChannelGeneration(channel)) accepted?.Invoke(response.Data);
                if (!Current(scope, epoch) || snapshot && syncEpoch != _syncEpoch) return Failure<T>("STALE_RESPONSE");
                return new ChatResult<T> { IsSuccess = true, Data = response.Data, HttpStatusCode = response.StatusCode };
            }
            finally { _requests.Release(); }
        }
        private long ChannelGeneration(string channel) => channel != null && _channelArchiveGenerations.TryGetValue(channel, out long generation) ? generation : 0;
        private void Archive(string channel, ChatMessage[] messages)
        {
            try { if (History().Append(channel, messages)) { FlushBrowserStorage(); LocalHistoryChanged?.Invoke(channel); } }
            catch (Exception e) when (LocalError(e)) { LocalHistoryFailed?.Invoke(channel, StorageCode(e)); }
        }
        private ChatHistoryStore History() => _history ?? (_history = new ChatHistoryStore(HistoryRootOverride ?? System.IO.Path.Combine(Application.persistentDataPath, "horizon", "chat"), _network.GetApiKey(), UserManager.Instance.CurrentUser.UserId));
        private bool LocalIdentity()
        { SyncIdentity(); var u = UserManager.Instance.CurrentUser; return u != null && u.IsValid() && FriendsJson.IsUuid(u.UserId) && !string.IsNullOrEmpty(_network.GetApiKey()); }
        private bool Begin(string user, out string scope, out long epoch)
        {
            SyncIdentity(); scope = _scope; epoch = _epoch; var current = UserManager.Instance.CurrentUser;
            LastErrorCode = current == null || !current.IsValid() || string.IsNullOrEmpty(_network.GetSessionToken()) || current.AccessToken != _network.GetSessionToken() ? "SESSION_REQUIRED"
                : current.UserId != user ? "SESSION_FORBIDDEN" : !FriendsJson.IsUuid(user) ? "INVALID_REQUEST" : null;
            return LastErrorCode == null;
        }
        private void SyncIdentity()
        {
            var network = HorizonApp.Network; var user = UserManager.Instance.CurrentUser;
            string scope = user?.UserId + "|" + user?.AccessToken + "|" + network.GetSessionToken() + "|" + network.GetApiKey() + "|" + network.GetActiveHost() + "|" + network.ContextGeneration;
            if (ReferenceEquals(network, _network) && scope == _scope) return;
            string owner = user?.UserId + "|" + network.GetApiKey();
            if (_scope != null && owner == _owner)
            {
                // Transport changes invalidate requests and visible data, while same-owner watches
                // wait for a newly authorized subscription on the replacement connection.
                _epoch++; _syncEpoch++; _serverMillis = null;
                foreach (var watch in _watches.Values.ToArray()) { Hide(watch); watch.Dirty = true; }
            }
            else ResetIdentity();
            _network = network; _scope = scope; _owner = owner;
        }
        private void ResetIdentity()
        {
            _epoch++; _syncEpoch++; _archiveGeneration++; _channelArchiveGenerations.Clear(); _scope = null; _owner = null; _history = null; _network = null; _serverMillis = null;
            var old = _watches.Values.ToArray(); _watches.Clear();
            foreach (var w in old) { _realtime?.Unsubscribe("chat:" + w.Channel); Hide(w); }
            LastErrorCode = null;
        }
        private bool Current(string scope, long epoch) { SyncIdentity(); return scope == _scope && epoch == _epoch; }
        private void EnsureRealtime()
        {
            if (_realtime != null) return; _realtime = RealtimeManager.Instance;
            _realtime.EventReceived += OnEvent; _realtime.ResyncRequired += OnResync; _realtime.ConnectionError += OnConnectionError;
        }
        private void OnEvent(RealtimeFrame frame)
        { SyncIdentity(); if (frame.topic != null && frame.topic.StartsWith("chat:", StringComparison.Ordinal) && _watches.TryGetValue(frame.topic.Substring(5), out var w)) Schedule(w); }
        private void OnResync(string topic)
        {
            SyncIdentity(); _syncEpoch++;
            // All in-flight snapshots share this request epoch. Requeue every watched channel
            // so one channel's resync cannot leave another channel permanently stale.
            foreach (var w in _watches.Values.ToArray()) { Hide(w); Schedule(w); }
        }
        private void OnConnectionError(string code)
        { _syncEpoch++; foreach (var w in _watches.Values.ToArray()) { Hide(w); ChannelError?.Invoke(w.Channel, code); } }
        private void Schedule(Watch watch) { watch.Dirty = true; if (!watch.Busy) _ = Refresh(watch); }
        private async Task Refresh(Watch watch)
        {
            watch.Busy = true;
            try
            {
                while (watch.Dirty && _watches.TryGetValue(watch.Channel, out var current) && ReferenceEquals(current, watch) && _realtime != null && _realtime.IsSubscribed("chat:" + watch.Channel))
                {
                    watch.Dirty = false;
                    var result = await SyncChannel(watch.User, watch.Channel, watch.Snapshot);
                    if (!_watches.TryGetValue(watch.Channel, out current) || !ReferenceEquals(current, watch)) return;
                    if (!result.IsSuccess) { Hide(watch); if (result.ErrorCode != "STALE_RESPONSE") ChannelError?.Invoke(watch.Channel, result.ErrorCode); continue; }
                    if (!_realtime.IsSubscribed("chat:" + watch.Channel)) { Hide(watch); return; }
                    watch.Snapshot = ChatJson.Copy(result.Data); watch.ReceivedAt = NowSeconds();
                    FeedChanged?.Invoke(watch.Channel, GetFeed(watch.Channel));
                }
            }
            catch { Hide(watch); ChannelError?.Invoke(watch.Channel, "CHAT_SYNC_FAILED"); }
            finally { watch.Busy = false; }
        }
        private void Update()
        {
            if (!HorizonApp.IsInitialized) return; SyncIdentity();
            foreach (var w in _watches.Values.ToArray())
            {
                if (_realtime == null || !_realtime.IsSubscribed("chat:" + w.Channel)) { Hide(w); continue; }
                if (w.Dirty && !w.Busy) Schedule(w);
                if (w.Snapshot == null) continue;
                var live = CurrentFeed(w);
                if (live.Length != w.Snapshot.messages.Length)
                { w.Snapshot.messages = live; w.Snapshot.visibleMessageIds = live.Select(m => m.id).ToArray(); FeedChanged?.Invoke(w.Channel, live.Select(ChatJson.Copy).ToArray()); }
            }
        }
        private ChatMessage[] CurrentFeed(Watch w)
        {
            long now = ChatJson.Millis(w.Snapshot.serverTime) + (long)(Math.Max(0, NowSeconds() - w.ReceivedAt) * 1000);
            if (ChatJson.Millis(w.Snapshot.channel.expiresAt) <= now) return Array.Empty<ChatMessage>();
            return w.Snapshot.messages.Where(m => ChatJson.Millis(m.expiresAt) > now).ToArray();
        }
        private void Hide(Watch w) { bool had = w.Snapshot != null; w.Snapshot = null; if (had) FeedChanged?.Invoke(w.Channel, Array.Empty<ChatMessage>()); }
        private static bool LocalError(Exception e) => e is InvalidDataException || e is IOException || e is UnauthorizedAccessException || e is FormatException || e is ArgumentException || e is System.Security.SecurityException || e is NotSupportedException;
        private static string StorageCode(Exception e) => e is InvalidDataException && e.Message.StartsWith("LOCAL_HISTORY_", StringComparison.Ordinal) ? e.Message : "LOCAL_HISTORY_IO_FAILED";
        private static string User(string user) => "{\"userId\":" + ChatJson.Quote(ChatJson.Id(user));
        private static string Escape(string s) => Uri.EscapeDataString(s ?? "");
        private static string Path(string id) => Endpoint + "/channels/" + id;
        private Task<ChatResult<T>> Invalid<T>() where T : class => Task.FromResult(Failure<T>("INVALID_REQUEST"));
        private ChatResult<T> Failure<T>(string code, string error = null, long status = 0) where T : class
        { LastErrorCode = code; return new ChatResult<T> { ErrorCode = code, Error = error ?? code, HttpStatusCode = status }; }
    }
}
