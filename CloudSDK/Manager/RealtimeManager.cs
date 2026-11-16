using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Service;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using System.Net.WebSockets;
#endif

namespace PM.horizOn.Cloud.Manager
{
    [Serializable]
    public sealed class RealtimeFrame
    {
        public string type, topic, eventId, resourceId, eventType, reason, code;
        public int version, pollAfterSeconds;
        public long revision, requestId;
    }
    [Serializable] internal sealed class RealtimeSessionInfo { public int pollAfterSeconds = 120; }
    [Serializable] internal sealed class RealtimeCommand { public string type, topic, eventId; public long requestId; }
    /// <summary>Socket v2 fields kept out of the public frame, so a ticket never reaches game callbacks.</summary>
    [Serializable] internal sealed class RealtimeFrameExtras { public string ticket; public bool sendOverSocket; public long status; public double retryAfter; public int closeCode; }
    /// <summary>Outcome of one socket send. Delivered false means unknown: the socket closed or the result timed out.</summary>
    internal sealed class RealtimeSendResult { internal bool Delivered; internal long Status; internal string Code; internal double RetryAfter; internal string Result; }

    /// <summary>On-demand invalidations. Events are loaded through each feature's HTTP API. Socket v2 (TASK-1139):
    /// chat sends and match actions travel as frames while <see cref="CanSendOverSocket"/>, a subscription may carry a
    /// snapshot instead of an HTTP reload, and reconnects authenticate with a resume ticket held in memory only.</summary>
    public class RealtimeManager : BaseManager<RealtimeManager>
    {
        private const float TicketSeconds = 540;
        public event Action<RealtimeFrame> EventReceived;
        /// <summary>Topic and the snapshot JSON (the feature's HTTP answer) of a subscription. No resync follows for it.</summary>
        public event Action<string, string> SnapshotReceived;
        internal static event Action<string, string> Snapshots;
        private static RealtimeManager _active;
        /// <summary>The live manager, if any. Looked up because edit mode calls no OnEnable.</summary>
        internal static RealtimeManager Active => _active != null ? _active : (_active = FindFirstObjectByType<RealtimeManager>());
        public event Action<string> ResyncRequired;
        public event Action<string, int> PollingRequired;
        public event Action<string> ConnectionError;
        public bool IsReady { get; private set; }
        public int PollAfterSeconds { get; private set; } = 120;
        /// <summary>Optional full ws/wss URL, for Dev stacks exposing the gateway on a separate port.</summary>
        public string GatewayUrl { get; set; }
        public string LastErrorCode { get; private set; }
        /// <summary>True while the socket is ready and the gateway accepts chat sends and match actions as frames.</summary>
        public bool CanSendOverSocket { get { SyncIdentity(); return IsReady && _sendOverSocket; } }
        internal double SendTimeoutSeconds = 10;
        /// <summary>Catch-up state per chat topic: {"epoch":...,"knownMessageIds":[...]} or null.</summary>
        internal Func<string, string> SyncFor;
        // Fake socket seam for EditMode tests: replaces connect and send of the real transport.
        internal Action<Uri> TestConnect; internal Action<string> TestSend;
        private readonly Dictionary<long, TaskCompletionSource<RealtimeSendResult>> _sends = new Dictionary<long, TaskCompletionSource<RealtimeSendResult>>();
        private string _ticket; private float _ticketAt; private bool _sendOverSocket, _ticketAuth;
        private readonly HashSet<string> _topics = new HashSet<string>();
        private readonly HashSet<string> _sent = new HashSet<string>();
        private readonly ConcurrentQueue<(int generation, string raw)> _incoming = new ConcurrentQueue<(int, string)>();
        private readonly string _clientId = Guid.NewGuid().ToString();
        private string _pending, _token, _userId, _apiKey, _host;
        private long _networkGeneration;
        private int _generation, _attempt, _pendingSends;
        private long _requestId, _pendingRequestId;
        private bool _pendingCanceled;
        private float _retryAt, _pollAt, _pingAt, _connectDeadline, _lastFrameAt;
        private bool _connecting, _unsupported;
        private volatile bool _overflow;
#if UNITY_WEBGL && !UNITY_EDITOR
        private int _webSocket = -1;
        [DllImport("__Internal")] private static extern int HorizonRealtimeConnect(string url, string objectName);
        [DllImport("__Internal")] private static extern void HorizonRealtimeSend(int id, string frame);
        [DllImport("__Internal")] private static extern void HorizonRealtimeClose(int id);
#else
        private ClientWebSocket _socket;
        private CancellationTokenSource _cancel;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
#endif
        public bool Subscribe(string topic)
        {
            SyncIdentity();
            if (topic == null || !Regex.IsMatch(topic, "^(inbox|matches|chat):[A-Za-z0-9._-]{1,96}$") ||
                (_topics.Count >= 32 && !_topics.Contains(topic))) return false;
            _topics.Add(topic);
            return true;
        }
        public bool IsSubscribed(string topic)
        { SyncIdentity(); return IsReady && _topics.Contains(topic) && _sent.Contains(topic); }
        public void Unsubscribe(string topic)
        {
            if (!_topics.Remove(topic)) return;
            _sent.Remove(topic);
            if (_pending == topic) _pendingCanceled = true;
            if (IsReady) Send(new RealtimeCommand { type = "unsubscribe", topic = topic, requestId = ++_requestId });
            if (_topics.Count == 0) DisconnectTransport();
        }
        public void Close() { _topics.Clear(); DisconnectTransport(); }
        public void SetPollAfterSeconds(int seconds) { PollAfterSeconds = Math.Max(1, seconds); _pollAt = Time.realtimeSinceStartup + PollAfterSeconds; }
        public static bool CanPoll(string topic) => topic != null && !topic.StartsWith("chat:", StringComparison.Ordinal);
        public static double ReconnectDelay(int attempt, double fraction) =>
            Math.Max(0, Math.Min(1, fraction)) * Math.Min(30, Math.Pow(2, Math.Min(attempt, 5)));

        private void Update()
        {
            if (!HorizonApp.IsInitialized) return;
            SyncIdentity();
            if (_overflow) { _overflow = false; Failed("SLOW_CONSUMER"); }
            while (_incoming.TryDequeue(out var incoming))
            {
                if (SyncIdentity()) break;
                if (incoming.generation == _generation) Handle(incoming.raw);
            }
            if (_topics.Count == 0 || string.IsNullOrEmpty(_token)) return;
            float now = Time.realtimeSinceStartup;
            if (IsReady && now - _lastFrameAt > 90) { Failed("HEARTBEAT_TIMEOUT"); return; }
            if (IsReady)
            {
                if (string.IsNullOrEmpty(_pending))
                {
                    foreach (string topic in _topics) if (!_sent.Contains(topic))
                    { _pending = topic; _pendingCanceled = false; _pendingRequestId = ++_requestId;
                        string sync = topic.StartsWith("chat:", StringComparison.Ordinal) ? SyncFor?.Invoke(topic) : null;
                        if (sync == null) Send(new RealtimeCommand { type = "subscribe", topic = topic, requestId = _pendingRequestId });
                        else SendText("{\"type\":\"subscribe\",\"topic\":" + ItemsJson.Quote(topic) + ",\"requestId\":" + _pendingRequestId + ",\"sync\":" + sync + "}");
                        break; }
                }
                if (now >= _pingAt && string.IsNullOrEmpty(_pending))
                { Send(new RealtimeCommand { type = "ping" }); _pingAt = now + 30; }
            }
            else
            {
                if (_connecting && now > _connectDeadline) Failed("CONNECTION_TIMEOUT");
                if (now >= _pollAt)
                {
                    _pollAt = now + PollAfterSeconds;
                    foreach (string topic in new List<string>(_topics))
                    { if (SyncIdentity()) break; if (_topics.Contains(topic) && CanPoll(topic)) PollingRequired?.Invoke(topic, PollAfterSeconds); }
                }
                if (!_connecting && !_unsupported && now >= _retryAt) Open();
            }
        }
        private async void Open()
        {
            _connecting = true;
            _connectDeadline = Time.realtimeSinceStartup + 15;
            int generation = _generation;
            try
            {
                // A resume ticket authenticates at the gateway without a backend call.
                if (!HasTicket())
                {
                    var response = await HorizonApp.Network.PostAsync<RealtimeSessionInfo>("/api/v1/app/realtime/session", new object(), true);
                    if (this == null || SyncIdentity() || generation != _generation) return;
                    if (!response.IsSuccess)
                    {
                        _unsupported = response.ErrorCode == "NOT_SUPPORTED" || response.StatusCode == 501;
                        Failed(_unsupported ? "NOT_SUPPORTED" : (response.ErrorCode ?? "GATEWAY_UNAVAILABLE"));
                        return;
                    }
                    PollAfterSeconds = Math.Max(1, response.Data?.pollAfterSeconds ?? 120);
                }
                string host = HorizonApp.Network.GetActiveHost().TrimEnd('/');
                var uri = new UriBuilder(host) { Scheme = host.StartsWith("https:", StringComparison.Ordinal) ? "wss" : "ws", Path = "/api/v1/app/realtime/socket", Query = "" };
                Uri target = string.IsNullOrEmpty(GatewayUrl) ? uri.Uri : new Uri(GatewayUrl);
                if ((target.Scheme != "ws" && target.Scheme != "wss") || !string.IsNullOrEmpty(target.UserInfo) || !string.IsNullOrEmpty(target.Query)) throw new InvalidOperationException("INVALID_GATEWAY_URL");
                if (TestConnect != null) { TestConnect(target); Authenticate(); return; }
#if UNITY_WEBGL && !UNITY_EDITOR
                _webSocket = HorizonRealtimeConnect(target.ToString(), gameObject.name);
#else
                _cancel = new CancellationTokenSource();
                _socket = new ClientWebSocket();
                ClientWebSocket socket = _socket;
                CancellationToken cancellation = _cancel.Token;
                await socket.ConnectAsync(target, cancellation);
                if (generation != _generation) { socket.Dispose(); return; }
                Authenticate();
                _ = Receive(socket, cancellation, generation);
#endif
            }
            catch { if (generation == _generation && this != null) Failed("CONNECTION_FAILED"); }
        }
        private bool HasTicket()
        {
            if (!string.IsNullOrEmpty(_ticket) && Time.realtimeSinceStartup - _ticketAt < TicketSeconds) return true;
            _ticket = null; return false;
        }
        /// <summary>Exactly one credential: the held ticket while it is younger than nine minutes, else the session token.</summary>
        private void Authenticate()
        {
            _ticketAuth = HasTicket();
            SendText("{\"type\":\"authenticate\",\"version\":1,\"apiKey\":" + ItemsJson.Quote(HorizonApp.Network.GetApiKey() ?? "") + ",\"clientId\":" + ItemsJson.Quote(_clientId)
                + (_ticketAuth ? ",\"ticket\":" + ItemsJson.Quote(_ticket) : ",\"sessionToken\":" + ItemsJson.Quote(_token ?? "")) + "}");
        }
        // Browser bridge callbacks run on Unity's main thread. They contain no credentials in URLs.
        public void OnHorizonRealtimeOpen(string transportId)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (int.TryParse(transportId, out int id) && id == _webSocket) Authenticate();
#endif
        }
        public void OnHorizonRealtimeMessage(string raw)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int separator = raw.IndexOf('|');
            if (separator < 1 || !int.TryParse(raw.Substring(0, separator), out int id) || id != _webSocket) return;
            Enqueue(raw.Substring(separator + 1));
#endif
        }
        public void OnHorizonRealtimeClosed(string transportId)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // "id|closeCode" since socket v2; a bare id from an older bridge counts as a plain close.
            string[] parts = (transportId ?? "").Split('|');
            if (int.TryParse(parts[0], out int id) && id == _webSocket)
                Enqueue("{\"type\":\"transport_closed\",\"closeCode\":" + (parts.Length > 1 && int.TryParse(parts[1], out int code) ? code : 0) + "}");
#endif
        }
        internal void TestReceive(string raw) => Enqueue(raw);
        private void Enqueue(string raw, int? generation = null)
        {
            if (_incoming.Count >= 64) { _overflow = true; return; }
            _incoming.Enqueue((generation ?? _generation, raw));
        }
#if !(UNITY_WEBGL && !UNITY_EDITOR)
        private async Task Receive(ClientWebSocket socket, CancellationToken cancellation, int generation)
        {
            // Snapshots carry a whole chat window or match state, so frames are larger than v1 invalidations.
            var buffer = new byte[65536];
            try
            {
                while (!cancellation.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    int size = 0; WebSocketReceiveResult result;
                    do
                    {
                        if (size == buffer.Length) throw new InvalidOperationException("FRAME_TOO_LARGE");
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, size, buffer.Length - size), cancellation).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        { if (generation == _generation) Enqueue("{\"type\":\"transport_closed\",\"closeCode\":" + (int)(result.CloseStatus ?? 0) + "}", generation); return; }
                        if (result.MessageType != WebSocketMessageType.Text) throw new InvalidOperationException("CONNECTION_CLOSED");
                        size += result.Count;
                    } while (!result.EndOfMessage);
                    if (generation != _generation) return;
                    Enqueue(Encoding.UTF8.GetString(buffer, 0, size), generation);
                }
            }
            catch { if (generation == _generation) Enqueue("{\"type\":\"transport_closed\"}", generation); }
        }
#endif
        private void Send(object frame) => SendText(JsonUtility.ToJson(frame));
        private async void SendText(string json)
        {
            if (TestSend != null) { TestSend(json); return; }
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_webSocket >= 0) HorizonRealtimeSend(_webSocket, json);
            await Task.CompletedTask;
#else
            ClientWebSocket socket = _socket;
            CancellationTokenSource cancellation = _cancel;
            if (socket == null || cancellation == null) return;
            if (Interlocked.Increment(ref _pendingSends) > 64)
            { Interlocked.Decrement(ref _pendingSends); Failed("SLOW_CONSUMER"); return; }
            try
            {
                await _sendLock.WaitAsync(cancellation.Token);
                try { await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, cancellation.Token); }
                finally { _sendLock.Release(); }
            }
            catch { if (socket == _socket) Failed("CONNECTION_CLOSED"); }
            finally { Interlocked.Decrement(ref _pendingSends); }
#endif
        }
        private void Handle(string raw)
        {
            RealtimeFrame frame;
            try { frame = JsonUtility.FromJson<RealtimeFrame>(raw); }
            catch { Failed("INVALID_FRAME"); return; }
            if (frame == null) { Failed("CONNECTION_CLOSED"); return; }
            var extras = JsonUtility.FromJson<RealtimeFrameExtras>(raw) ?? new RealtimeFrameExtras();
            if (frame.type == "transport_closed")
            {
                // 1012: the gateway restarts for an update. Come back within five seconds with the ticket, no backoff step.
                if (extras.closeCode == 1012) Retry("SERVICE_RESTART", UnityEngine.Random.value * 5f, false);
                else Failed("CONNECTION_CLOSED");
                return;
            }
            _lastFrameAt = Time.realtimeSinceStartup;
            if (frame.version != 1) { Failed("INVALID_FRAME"); return; }
            switch (frame.type)
            {
                case "ready": IsReady = true; _connecting = false; _attempt = 0; PollAfterSeconds = Math.Max(1, frame.pollAfterSeconds); _pingAt = Time.realtimeSinceStartup + 30;
                    _sendOverSocket = extras.sendOverSocket; StoreTicket(extras.ticket); break;
                case "ticket": StoreTicket(extras.ticket); break;
                case "result":
                    if (_sends.TryGetValue(frame.requestId, out var pending))
                    {
                        _sends.Remove(frame.requestId);
                        string body = null;
                        if (extras.status >= 200 && extras.status < 300) { try { JsonFragments.Object(raw).TryGetValue("result", out body); } catch (FormatException) { body = null; } }
                        pending.TrySetResult(new RealtimeSendResult { Delivered = true, Status = extras.status, Code = string.IsNullOrEmpty(frame.code) ? null : frame.code, RetryAfter = extras.retryAfter, Result = body });
                    }
                    break;
                case "unsubscribed": if (frame.requestId == _pendingRequestId && frame.topic == _pending) { _sent.Remove(frame.topic); _pending = null; } break;
                case "subscribed": if (frame.requestId == _pendingRequestId && frame.topic == _pending)
                    {
                        bool accepted = !_pendingCanceled && _topics.Contains(frame.topic);
                        if (accepted) _sent.Add(frame.topic); _pending = null;
                        string snapshot = null;
                        if (accepted) { try { JsonFragments.Object(raw).TryGetValue("snapshot", out snapshot); } catch (FormatException) { snapshot = null; } }
                        if (snapshot != null && snapshot.StartsWith("{", StringComparison.Ordinal))
                        { string topic = frame.topic; Snapshots?.Invoke(topic, snapshot); if (!SyncIdentity() && _topics.Contains(topic)) SnapshotReceived?.Invoke(topic, snapshot); }
                    } break;
                case "event":
                    if (_topics.Contains(frame.topic)) EventReceived?.Invoke(frame);
                    Send(new RealtimeCommand { type = "ack", eventId = frame.eventId });
                    break;
                case "resync_required":
                    if (frame.reason == "PERMISSIONS_CHANGED") _sent.Clear();
                    if (!string.IsNullOrEmpty(frame.topic)) { if (_topics.Contains(frame.topic)) ResyncRequired?.Invoke(frame.topic); }
                    else foreach (string topic in new List<string>(_topics))
                    { if (SyncIdentity()) break; if (_topics.Contains(topic)) ResyncRequired?.Invoke(topic); }
                    Send(new RealtimeCommand { type = "ack", eventId = frame.eventId });
                    break;
                case "error":
                    // A rejected ticket falls back to the session token at once, exactly once (the ticket is gone).
                    if (frame.code == "SESSION_REQUIRED" && _ticketAuth && !IsReady) { _ticket = null; Retry(frame.code, 0, false); break; }
                    // No socket for this session for a while: everything goes over HTTP (polling as v1), then try again.
                    if (frame.code == "CONNECTION_LIMIT_REACHED" && !IsReady) { Retry(frame.code, PollAfterSeconds, false); break; }
                    if (frame.code == "SUBSCRIPTION_FORBIDDEN" && frame.topic != null && frame.requestId == _pendingRequestId)
                    { _topics.Remove(frame.topic); _sent.Remove(frame.topic); _pending = null; } LastErrorCode = frame.code; ConnectionError?.Invoke(frame.code); break;
            }
        }
        private void StoreTicket(string ticket) { if (!string.IsNullOrEmpty(ticket)) { _ticket = ticket; _ticketAt = Time.realtimeSinceStartup; } }
        /// <summary>One chat send or match action as a frame. Null when the socket cannot send (the caller uses HTTP).</summary>
        internal async Task<RealtimeSendResult> SendRequest(string type, string fields)
        {
            if (!CanSendOverSocket) return null;
            long id = ++_requestId;
            var completion = new TaskCompletionSource<RealtimeSendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _sends[id] = completion;
            SendText("{\"type\":" + ItemsJson.Quote(type) + ",\"requestId\":" + id + fields + "}");
            var done = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(Math.Max(0.01, SendTimeoutSeconds))));
            _sends.Remove(id);
            return done == completion.Task ? completion.Task.Result : new RealtimeSendResult();
        }
        /// <summary>The response the HTTP endpoint gives for the same status, code and body.</summary>
        internal static NetworkResponse<T> ToResponse<T>(RealtimeSendResult result, Func<string, T> parser) where T : class
        {
            if (result.Status >= 200 && result.Status < 300)
            {
                try { return NetworkResponse<T>.Success(result.Result == null ? null : parser(result.Result), result.Status); }
                catch (Exception e) when (e is FormatException || e is ArgumentException || e is KeyNotFoundException || e is InvalidOperationException || e is OverflowException)
                { return NetworkResponse<T>.Failure("Deserialization failed", result.Status); }
            }
            if (result.Status == 429)
            {
                double retryAfter = Math.Max(0, result.RetryAfter);
                EventService.Instance?.Publish(EventKeys.NetworkRateLimited, new RateLimitData { RetryAfter = (float)Math.Min(retryAfter, float.MaxValue) });
                var headers = retryAfter > 0 ? ResponseHeaders.From(new Dictionary<string, string> { ["Retry-After"] = Math.Ceiling(retryAfter).ToString(System.Globalization.CultureInfo.InvariantCulture) }) : null;
                return NetworkResponse<T>.Failure(NetworkService.BuildRateLimitMessage(retryAfter), 429, result.Code, headers);
            }
            return NetworkResponse<T>.Failure("HTTP " + result.Status, result.Status, result.Code);
        }
        private void Failed(string code) => Retry(code, (float)ReconnectDelay(_attempt, UnityEngine.Random.value), true);
        private void Retry(string code, float delaySeconds, bool countAttempt)
        {
            DisconnectTransport(); LastErrorCode = code;
            _retryAt = Time.realtimeSinceStartup + delaySeconds;
            if (countAttempt) _attempt++;
            ConnectionError?.Invoke(code);
        }
        private bool SyncIdentity()
        {
            if (!HorizonApp.IsInitialized) return false;
            string user = UserManager.Instance.CurrentUser?.UserId;
            var network = HorizonApp.Network;
            string token = network.GetSessionToken(), key = network.GetApiKey(), host = network.GetActiveHost();
            long context = network.ContextGeneration;
            bool ownerChanged = user != _userId || key != _apiKey;
            bool changed = ownerChanged || token != _token || host != _host || context != _networkGeneration;
            if (!changed) return false;
            if (ownerChanged && (!string.IsNullOrEmpty(_userId) || !string.IsNullOrEmpty(_apiKey))) _topics.Clear();
            DisconnectTransport(); _ticket = null;
            _userId = user; _token = token; _apiKey = key; _host = host; _networkGeneration = context;
            _unsupported = false; _attempt = 0;
            return true;
        }
        private void DisconnectTransport()
        {
            _generation++; IsReady = false; _connecting = false; _sent.Clear(); _pending = null; _sendOverSocket = false; _ticketAuth = false;
            // Pending sends end with an unknown outcome; the feature resolves them over HTTP.
            var pending = new List<TaskCompletionSource<RealtimeSendResult>>(_sends.Values); _sends.Clear();
            foreach (var send in pending) send.TrySetResult(new RealtimeSendResult());
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_webSocket >= 0) HorizonRealtimeClose(_webSocket);
            _webSocket = -1;
#else
            _cancel?.Cancel(); _socket?.Dispose(); _socket = null; _cancel = null;
#endif
            while (_incoming.TryDequeue(out _)) { }
        }
        protected override void OnEnable() { _active = this; base.OnEnable(); }
        protected override void OnDisable() { Close(); if (_active == this) _active = null; base.OnDisable(); }
        private void OnDestroy() { Close(); }
        private void OnApplicationPause(bool pause) { if (pause) DisconnectTransport(); }
    }
}
