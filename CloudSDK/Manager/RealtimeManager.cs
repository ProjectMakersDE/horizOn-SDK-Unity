using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
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
    [Serializable] internal sealed class RealtimeAuthentication
    {
        public string type = "authenticate";
        public int version = 1;
        public string apiKey, sessionToken, clientId;
    }
    [Serializable] internal sealed class RealtimeCommand { public string type, topic, eventId; public long requestId; }

    /// <summary>On-demand invalidations. Events and snapshots are loaded through each feature's HTTP API.</summary>
    public class RealtimeManager : BaseManager<RealtimeManager>
    {
        public event Action<RealtimeFrame> EventReceived;
        public event Action<string> ResyncRequired;
        public event Action<string, int> PollingRequired;
        public event Action<string> ConnectionError;
        public bool IsReady { get; private set; }
        public int PollAfterSeconds { get; private set; } = 120;
        /// <summary>Optional full ws/wss URL, for Dev stacks exposing the gateway on a separate port.</summary>
        public string GatewayUrl { get; set; }
        public string LastErrorCode { get; private set; }
        private readonly HashSet<string> _topics = new HashSet<string>();
        private readonly HashSet<string> _sent = new HashSet<string>();
        private readonly ConcurrentQueue<(int generation, string raw)> _incoming = new ConcurrentQueue<(int, string)>();
        private readonly string _clientId = Guid.NewGuid().ToString();
        private string _pending, _token, _userId;
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
                        Send(new RealtimeCommand { type = "subscribe", topic = topic, requestId = _pendingRequestId }); break; }
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
                var response = await HorizonApp.Network.PostAsync<RealtimeSessionInfo>("/api/v1/app/realtime/session", new object(), true);
                if (generation != _generation || this == null) return;
                if (!response.IsSuccess)
                {
                    _unsupported = response.ErrorCode == "NOT_SUPPORTED" || response.StatusCode == 501;
                    Failed(_unsupported ? "NOT_SUPPORTED" : (response.ErrorCode ?? "GATEWAY_UNAVAILABLE"));
                    return;
                }
                PollAfterSeconds = Math.Max(1, response.Data?.pollAfterSeconds ?? 120);
                string host = HorizonApp.Network.GetActiveHost().TrimEnd('/');
                var uri = new UriBuilder(host) { Scheme = host.StartsWith("https:", StringComparison.Ordinal) ? "wss" : "ws", Path = "/api/v1/app/realtime/socket", Query = "" };
                Uri target = string.IsNullOrEmpty(GatewayUrl) ? uri.Uri : new Uri(GatewayUrl);
                if ((target.Scheme != "ws" && target.Scheme != "wss") || !string.IsNullOrEmpty(target.UserInfo) || !string.IsNullOrEmpty(target.Query)) throw new InvalidOperationException("INVALID_GATEWAY_URL");
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
        private void Authenticate()
        {
            Send(new RealtimeAuthentication { apiKey = HorizonApp.Network.GetApiKey(), sessionToken = _token, clientId = _clientId });
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
            if (int.TryParse(transportId, out int id) && id == _webSocket) Failed("CONNECTION_CLOSED");
#endif
        }
        private void Enqueue(string raw, int? generation = null)
        {
            if (_incoming.Count >= 64) { _overflow = true; return; }
            _incoming.Enqueue((generation ?? _generation, raw));
        }
#if !(UNITY_WEBGL && !UNITY_EDITOR)
        private async Task Receive(ClientWebSocket socket, CancellationToken cancellation, int generation)
        {
            var buffer = new byte[8192];
            try
            {
                while (!cancellation.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    int size = 0; WebSocketReceiveResult result;
                    do
                    {
                        if (size == buffer.Length) throw new InvalidOperationException("FRAME_TOO_LARGE");
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, size, buffer.Length - size), cancellation).ConfigureAwait(false);
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
        private async void Send(object frame)
        {
            string json = JsonUtility.ToJson(frame);
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
            if (frame == null || frame.type == "transport_closed") { Failed("CONNECTION_CLOSED"); return; }
            _lastFrameAt = Time.realtimeSinceStartup;
            if (frame.version != 1) { Failed("INVALID_FRAME"); return; }
            switch (frame.type)
            {
                case "ready": IsReady = true; _connecting = false; _attempt = 0; PollAfterSeconds = Math.Max(1, frame.pollAfterSeconds); _pingAt = Time.realtimeSinceStartup + 30; break;
                case "unsubscribed": if (frame.requestId == _pendingRequestId && frame.topic == _pending) { _sent.Remove(frame.topic); _pending = null; } break;
                case "subscribed": if (frame.requestId == _pendingRequestId && frame.topic == _pending)
                    { if (!_pendingCanceled && _topics.Contains(frame.topic)) _sent.Add(frame.topic); _pending = null; } break;
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
                case "error": if (frame.code == "SUBSCRIPTION_FORBIDDEN" && frame.topic != null && frame.requestId == _pendingRequestId)
                    { _topics.Remove(frame.topic); _sent.Remove(frame.topic); _pending = null; } LastErrorCode = frame.code; ConnectionError?.Invoke(frame.code); break;
            }
        }
        private void Failed(string code)
        {
            DisconnectTransport(); LastErrorCode = code;
            _retryAt = Time.realtimeSinceStartup + (float)ReconnectDelay(_attempt++, UnityEngine.Random.value);
            ConnectionError?.Invoke(code);
        }
        private bool SyncIdentity()
        {
            if (!HorizonApp.IsInitialized) return false;
            bool changed = false;
            string user = UserManager.Instance.CurrentUser?.UserId;
            if (user != _userId) { if (!string.IsNullOrEmpty(_userId)) Close(); _userId = user; changed = true; }
            string token = HorizonApp.Network.GetSessionToken();
            if (token != _token) { DisconnectTransport(); _token = token; _unsupported = false; _attempt = 0; changed = true; }
            return changed;
        }
        private void DisconnectTransport()
        {
            _generation++; IsReady = false; _connecting = false; _sent.Clear(); _pending = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_webSocket >= 0) HorizonRealtimeClose(_webSocket);
            _webSocket = -1;
#else
            _cancel?.Cancel(); _socket?.Dispose(); _socket = null; _cancel = null;
#endif
            while (_incoming.TryDequeue(out _)) { }
        }
        protected override void OnDisable() { Close(); base.OnDisable(); }
        private void OnDestroy() { Close(); }
        private void OnApplicationPause(bool pause) { if (pause) DisconnectTransport(); }
    }
}
