using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;
using UnityEngine.TestTools;

namespace PM.horizOn.Cloud.Tests
{
    /// <summary>Socket v2 (TASK-1139) against a fake socket: frames go to a list, gateway frames are injected.
    /// HTTP runs against a local listener, so every request that is not a frame is counted.</summary>
    [Category("Transport")]
    public class RealtimeSocketV2Tests
    {
        const string Host = "http://127.0.0.1:18931", User = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", Peer = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
            Channel = "cccccccc-cccc-4ccc-8ccc-cccccccccccc", Epoch = "dddddddd-dddd-4ddd-8ddd-dddddddddddd", Operation = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee",
            Match = "cccccccc-cccc-4ccc-8ccc-ccccccccccc1", Time = "2026-10-17T00:00:00.000Z", Later = "2026-10-24T00:00:00.000Z", ApiKey = "socket-v2-fixture-key",
            Ticket = "resume-ticket-one", NextTicket = "resume-ticket-two";
        HttpListener _listener; HorizonConfig _config; string _root;
        readonly List<string> _http = new List<string>(), _frames = new List<string>(), _logs = new List<string>();
        Func<string, string, (int status, string json, string retryAfter)> _reply;
        int _connects;
        RealtimeManager Realtime => RealtimeManager.Instance;
        ChatManager Chat => ChatManager.Instance;
        MatchesManager Matches => MatchesManager.Instance;
        static string Q(string s) => ChatJson.Quote(s);
        static string Id(int i) => i.ToString("x8") + "-ffff-4fff-8fff-ffffffffffff";
        static string ChannelJson(long revision = 1) => "{\"id\":" + Q(Channel) + ",\"epoch\":" + Q(Epoch) + ",\"kind\":\"project\",\"peerIds\":[],\"groupId\":null,\"revision\":" + revision + ",\"expiresAt\":" + Q(Later) + "}";
        static string Message(int i) => ChatJson.MessageJson(new ChatMessage { id = Id(i), channelId = Channel, authorId = Peer, text = "private text " + i, createdAt = Time, expiresAt = Later });
        static string Sync(int start = 0, int count = 20, int known = 0, long revision = 1) => "{\"channel\":" + ChannelJson(revision)
            + ",\"visibleMessageIds\":[" + string.Join(",", Enumerable.Range(start, count).Select(i => Q(Id(i)))) + "],\"messages\":["
            + string.Join(",", Enumerable.Range(start + known, count - known).Select(Message)) + "],\"moderation\":{\"muteUntil\":null,\"banUntil\":null},\"serverTime\":" + Q(Time) + "}";
        static string Receipt => "{\"operationId\":" + Q(Operation) + ",\"issuedAt\":" + Q(Time) + ",\"channelId\":" + Q(Channel) + ",\"messageId\":" + Q(Id(99)) + ",\"createdAt\":" + Q(Time) + ",\"expiresAt\":" + Q(Later) + "}";
        static string Sent => "{\"receipt\":" + Receipt + ",\"replayed\":false,\"serverTime\":" + Q(Time) + "}";
        static string Ids(params string[] ids) => "[" + string.Join(",", ids.Select(Q)) + "]";
        static string MatchView(long rev, string status = "active") => "{\"id\":" + Q(Match) + ",\"name\":\"socket\",\"revision\":" + rev + ",\"creatorId\":" + Q(User) + ",\"capacity\":2,\"visibility\":\"open\",\"strength\":0,\"status\":" + Q(status)
            + ",\"participantIds\":" + Ids(User, Peer) + ",\"invitedIds\":[],\"forfeitedIds\":[],\"currentPlayerId\":" + (status == "active" ? Q(Peer) : "null") + ",\"turnDeadline\":" + (status == "active" ? "\"2026-10-18T00:00:00Z\"" : "null")
            + ",\"createdAt\":\"2026-10-16T00:00:00Z\",\"startedAt\":null,\"finishedAt\":null,\"purgeAt\":null,\"finalStateRevision\":null,\"hasDisagreement\":false,\"ranked\":false,\"timeoutDue\":false"
            + ",\"turnOrder\":" + (status == "lobby" ? "[]" : Ids(User, Peer)) + ",\"turnSeconds\":60,\"timeoutPolicy\":\"forfeit\",\"dataBase64\":\"AP8A\",\"reports\":[],\"reportDeadline\":null,\"absoluteDeadline\":null,\"rankedLeaderboardKey\":null,\"ownRankedRunId\":null,\"notificationPending\":false}";
        static string MatchRead(long rev, string status = "active") => "{\"match\":" + MatchView(rev, status) + ",\"pollAfterSeconds\":120,\"serverTime\":\"2026-10-16T00:00:0" + Math.Min(rev, 9) + "Z\"}";

        [SetUp] public void Setup()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession"); NetworkService.ResetInstance(); EventService.ResetInstance(); LogService.ResetInstance();
            _config = ScriptableObject.CreateInstance<HorizonConfig>(); _config.SetApiKey(ApiKey); _config.SetHosts(new[] { Host });
            NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost(Host);
            var user = new UserData { UserId = User, AccessToken = "session-" + User };
            typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(UserManager.Instance, user);
            NetworkService.Instance.SetSessionToken(user.AccessToken);
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false;
            Application.logMessageReceived += Capture;
            typeof(HorizonApp).GetProperty("IsInitialized").SetValue(null, true);
            _root = Path.Combine(Path.GetTempPath(), "horizon-socket-v2-" + Guid.NewGuid().ToString("N")); Chat.HistoryRootOverride = _root;
            _http.Clear(); _frames.Clear(); _logs.Clear(); _connects = 0; _reply = null;
            _listener = new HttpListener(); _listener.Prefixes.Add(Host + "/"); _listener.Start(); _ = Serve(_listener);
            Realtime.TestConnect = _ => _connects++; Realtime.TestSend = frame => _frames.Add(frame);
            _ = Matches; // subscribes the match snapshot hook
        }
        [TearDown] public void Cleanup()
        {
            Application.logMessageReceived -= Capture;
            _listener?.Close();
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (new[] { "[ChatManager]", "[RealtimeManager]", "[MatchesManager]", "[UserManager]" }.Contains(go.name)) UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(_config); PlayerPrefs.DeleteKey("horizOn_UserSession");
            typeof(HorizonApp).GetProperty("IsInitialized").SetValue(null, false);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        void Capture(string message, string stack, LogType type) { lock (_logs) _logs.Add(message); }
        async Task Serve(HttpListener listener)
        {
            while (listener.IsListening)
            {
                HttpListenerContext c;
                try { c = await listener.GetContextAsync(); } catch { return; }
                string body = await new StreamReader(c.Request.InputStream).ReadToEndAsync();
                string path = c.Request.HttpMethod + " " + c.Request.Url.AbsolutePath;
                lock (_http) _http.Add(path + " If-Match=" + c.Request.Headers["If-Match"] + " " + body);
                var answer = path == "POST /api/v1/app/realtime/session" ? (200, "{\"pollAfterSeconds\":2}", null) : _reply?.Invoke(path, body) ?? (500, "{}", null);
                c.Response.StatusCode = answer.Item1; c.Response.ContentType = "application/json";
                if (answer.Item3 != null) c.Response.Headers.Add("Retry-After", answer.Item3);
                byte[] bytes = Encoding.UTF8.GetBytes(answer.Item2); c.Response.ContentLength64 = bytes.Length;
                try { await c.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length); c.Response.Close(); } catch { }
            }
        }
        int HttpCount(string prefix) { lock (_http) return _http.Count(x => x.StartsWith(prefix, StringComparison.Ordinal)); }
        void Tick()
        {
            typeof(RealtimeManager).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Realtime, null);
            typeof(ChatManager).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Chat, null);
        }
        async Task Until(Func<bool> condition, string what, double seconds = 8)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (!condition() && DateTime.UtcNow < deadline) { Tick(); await Task.Delay(10); }
            Assert.That(condition(), Is.True, what + " / " + Realtime.LastErrorCode);
        }
        static async Task<T> Bounded<T>(Task<T> task) { Assert.That(await Task.WhenAny(task, Task.Delay(10000)), Is.SameAs(task), "bounded wait"); return await task; }
        string LastFrame(string type) => _frames.LastOrDefault(f => f.Contains("\"type\":\"" + type + "\""));
        int FrameCount(string type) => _frames.Count(f => f.Contains("\"type\":\"" + type + "\""));
        static long RequestId(string frame) => long.Parse(Regex.Match(frame, "\"requestId\":(\\d+)").Groups[1].Value);
        void Inject(string frame) => Realtime.TestReceive(frame);
        string Ready(string ticket = Ticket, bool sendOverSocket = true) => "{\"type\":\"ready\",\"version\":1,\"heartbeatSeconds\":30,\"pollAfterSeconds\":2,\"maxSubscriptions\":32"
            + (ticket == null ? "" : ",\"ticket\":" + Q(ticket)) + ",\"sendOverSocket\":" + (sendOverSocket ? "true" : "false") + "}";
        /// <summary>Connect, become ready and subscribe the topic; the subscribed frame carries the snapshot when given.</summary>
        async Task Connect(string topic, string snapshot, bool sendOverSocket = true)
        {
            int connects = _connects, subscribes = FrameCount("subscribe");
            await Until(() => _connects > connects && LastFrame("authenticate") != null, "connect");
            Inject(Ready(sendOverSocket: sendOverSocket)); Inject("{\"type\":\"resync_required\",\"version\":1,\"reason\":\"CONNECTED\",\"eventId\":\"" + new string('a', 64) + "\"}");
            await Until(() => FrameCount("subscribe") > subscribes, "subscribe");
            Inject("{\"type\":\"subscribed\",\"version\":1,\"topic\":" + Q(topic) + ",\"requestId\":" + RequestId(LastFrame("subscribe")) + (snapshot == null ? "" : ",\"snapshot\":" + snapshot) + "}");
            await Until(() => Realtime.IsSubscribed(topic), "subscribed");
        }

        [Test] public async Task ChatSend_SocketReady_GoesOverSocketAndSnapshotNeedsNoHttpReload()
        {
            Assert.That(Chat.WatchChannel(User, Channel), Is.True);
            await Connect("chat:" + Channel, Sync());
            Assert.That(LastFrame("subscribe"), Does.Contain("\"sync\":{\"epoch\":null,\"knownMessageIds\":[]}"));
            await Until(() => Chat.GetFeed(Channel).Length == 20, "snapshot feed");
            for (int i = 0; i < 20; i++) Tick();
            Assert.That(HttpCount("POST /api/v1/app/chat"), Is.Zero, "the snapshot replaces the HTTP sync");
            Assert.That(Realtime.CanSendOverSocket, Is.True);

            var send = Chat.SendMessage(User, Channel, Operation, Time, "socket text");
            await Until(() => LastFrame("chat.send") != null, "chat.send frame");
            string frame = LastFrame("chat.send");
            Assert.That(frame, Does.Contain("\"channelId\":" + Q(Channel)).And.Contain("\"operationId\":" + Q(Operation)).And.Contain("\"issuedAt\":" + Q(Time)).And.Contain("\"text\":\"socket text\""));
            Assert.That(frame, Does.Not.Contain("userId"));
            Inject("{\"type\":\"result\",\"version\":1,\"requestId\":" + RequestId(frame) + ",\"status\":200,\"result\":" + Sent + "}");
            Tick(); var result = await Bounded(send);
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode); Assert.That(result.HttpStatusCode, Is.EqualTo(200));
            Assert.That(result.Data.receipt.messageId, Is.EqualTo(Id(99)));
            Assert.That(Chat.ReadLocalHistory(Channel).Data.messages.Any(m => m.id == Id(99) && m.text == "socket text"), Is.True);
            Assert.That(HttpCount("POST /api/v1/app/chat"), Is.Zero, "no HTTP send while the socket is ready");
        }

        [Test] public async Task ResultFrame_ErrorCode_MapsToTheHttpErrorIncludingRetryAfter()
        {
            _reply = (path, body) => (429, "{\"code\":\"CHAT_RATE_LIMITED\",\"message\":\"slow\"}", "3");
            var http = await Bounded(Chat.SendMessage(User, Channel, Operation, Time, "text"));
            Assert.That(HttpCount("POST /api/v1/app/chat/channels/" + Channel + "/messages"), Is.EqualTo(1), "HTTP while no socket exists");

            Assert.That(Chat.WatchChannel(User, Channel), Is.True);
            await Connect("chat:" + Channel, Sync());
            var send = Chat.SendMessage(User, Channel, Operation, Time, "text");
            await Until(() => LastFrame("chat.send") != null, "chat.send frame");
            Inject("{\"type\":\"result\",\"version\":1,\"requestId\":" + RequestId(LastFrame("chat.send")) + ",\"status\":429,\"code\":\"CHAT_RATE_LIMITED\",\"retryAfter\":3}");
            Tick(); var socket = await Bounded(send);
            Assert.That(socket.IsSuccess, Is.False);
            Assert.That(socket.ErrorCode, Is.EqualTo(http.ErrorCode).And.EqualTo("CHAT_RATE_LIMITED"));
            Assert.That(socket.HttpStatusCode, Is.EqualTo(http.HttpStatusCode).And.EqualTo(429));
            Assert.That(socket.Error, Is.EqualTo(http.Error), "same message including the retry-after seconds");
            Assert.That(HttpCount("POST /api/v1/app/chat/channels/" + Channel + "/messages"), Is.EqualTo(1));
        }

        [Test] public async Task ChatSend_GatewayRefusesSends_FallsBackToHttp()
        {
            _reply = (path, body) => (201, Sent, null);
            Assert.That(Chat.WatchChannel(User, Channel), Is.True);
            await Connect("chat:" + Channel, Sync(), sendOverSocket: false);
            Assert.That(Realtime.IsReady, Is.True); Assert.That(Realtime.CanSendOverSocket, Is.False);
            var result = await Bounded(Chat.SendMessage(User, Channel, Operation, Time, "text"));
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(LastFrame("chat.send"), Is.Null);
            Assert.That(HttpCount("POST /api/v1/app/chat/channels/" + Channel + "/messages"), Is.EqualTo(1));
        }

        [Test] public async Task PendingSend_Timeout_IsUnknownAndResolvedOverHttpWithTheSameOperation()
        {
            _reply = (path, body) => path.EndsWith("/messages") ? (200, "{\"receipt\":" + Receipt + ",\"replayed\":true,\"serverTime\":" + Q(Time) + "}", null)
                : (409, "{\"code\":\"MATCH_REVISION_CONFLICT\"}", null);
            Assert.That(Chat.WatchChannel(User, Channel), Is.True);
            await Connect("chat:" + Channel, Sync());
            Realtime.SendTimeoutSeconds = 0.2;
            var chat = await Bounded(Chat.SendMessage(User, Channel, Operation, Time, "text"));
            Assert.That(LastFrame("chat.send"), Is.Not.Null);
            Assert.That(chat.IsSuccess, Is.True, chat.ErrorCode); Assert.That(chat.Data.replayed, Is.True);
            string retry; lock (_http) retry = _http.Single(x => x.Contains("/messages"));
            Assert.That(retry, Does.Contain("\"operationId\":" + Q(Operation)).And.Contain("\"issuedAt\":" + Q(Time)));

            var turn = await Bounded(Matches.SubmitTurn(User, Match, 7, new byte[] { 0, 255, 0 }));
            Assert.That(LastFrame("match.action"), Does.Contain("\"revision\":7"));
            string matchRetry; lock (_http) matchRetry = _http.Single(x => x.Contains("/matches/"));
            Assert.That(matchRetry, Does.Contain("If-Match=7"), "the HTTP retry keeps the original revision");
            Assert.That(turn.ErrorCode, Is.EqualTo("MATCH_REVISION_CONFLICT")); Assert.That(turn.HttpStatusCode, Is.EqualTo(409));
        }

        [Test] public async Task MatchAction_SocketReady_GoesOverSocketAndSnapshotIsApplied()
        {
            MatchRead applied = null; Matches.MatchSnapshotApplied += x => applied = x;
            Assert.That(Realtime.Subscribe("matches:" + Match), Is.True);
            await Connect("matches:" + Match, MatchRead(6));
            Assert.That(LastFrame("subscribe"), Does.Not.Contain("\"sync\""), "match topics omit sync");
            Assert.That(applied, Is.Not.Null); Assert.That(applied.match.revision, Is.EqualTo(6));
            var cached = await Bounded(Matches.GetMatch(User, Match));
            Assert.That(cached.ErrorCode, Is.EqualTo("REFRESH_TOO_SOON"), "the snapshot counts as the HTTP read");
            Assert.That(Matches.GetCached<MatchRead>(cached.Identity).match.revision, Is.EqualTo(6));

            var turn = Matches.SubmitTurn(User, Match, 6, new byte[] { 0, 255, 0 }, true);
            await Until(() => LastFrame("match.action") != null, "match.action frame");
            string frame = LastFrame("match.action");
            Assert.That(frame, Does.Contain("\"matchId\":" + Q(Match)).And.Contain("\"action\":\"turn\"").And.Contain("\"revision\":6").And.Contain("\"input\":{\"dataBase64\":\"AP8A\",\"finish\":true}"));
            Assert.That(frame, Does.Not.Contain("userId"));
            Inject("{\"type\":\"result\",\"version\":1,\"requestId\":" + RequestId(frame) + ",\"status\":200,\"result\":" + MatchRead(7) + "}");
            Tick(); var result = await Bounded(turn);
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode); Assert.That(result.Data.match.revision, Is.EqualTo(7));

            var leave = Matches.LeaveMatch(User, Match, 7);
            await Until(() => FrameCount("match.action") == 2, "second frame");
            Inject("{\"type\":\"result\",\"version\":1,\"requestId\":" + RequestId(LastFrame("match.action")) + ",\"status\":409,\"code\":\"MATCH_REVISION_CONFLICT\"}");
            Tick(); var conflict = await Bounded(leave);
            Assert.That(conflict.ErrorCode, Is.EqualTo("MATCH_REVISION_CONFLICT")); Assert.That(conflict.HttpStatusCode, Is.EqualTo(409));
            Assert.That(HttpCount("POST /api/v1/app/matches") + HttpCount("GET /api/v1/app/matches"), Is.Zero);
        }

        [Test] public async Task Reconnect_After1012_UsesTheTicketAndResubscribesWithSync()
        {
            Assert.That(Chat.WatchChannel(User, Channel), Is.True);
            await Connect("chat:" + Channel, Sync());
            Assert.That(LastFrame("authenticate"), Does.Contain("\"sessionToken\":").And.Not.Contain("\"ticket\":"));
            Inject("{\"type\":\"ticket\",\"version\":1,\"ticket\":" + Q(NextTicket) + "}"); Tick();
            int sessions = HttpCount("POST /api/v1/app/realtime/session");
            Inject("{\"type\":\"transport_closed\",\"closeCode\":1012}"); Tick();
            Assert.That(Realtime.LastErrorCode, Is.EqualTo("SERVICE_RESTART"));
            Assert.That(typeof(RealtimeManager).GetField("_attempt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Realtime), Is.EqualTo(0), "1012 is no failed attempt");
            float retryAt = (float)typeof(RealtimeManager).GetField("_retryAt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Realtime);
            Assert.That(retryAt - UnityEngine.Time.realtimeSinceStartup, Is.LessThanOrEqualTo(5.01f), "short reconnect within five seconds");

            int authenticates = FrameCount("authenticate");
            await Connect("chat:" + Channel, Sync(start: 1, count: 20, known: 19, revision: 2));
            string auth = _frames.Where(f => f.Contains("\"type\":\"authenticate\"")).Skip(authenticates).First();
            Assert.That(auth, Does.Contain("\"ticket\":" + Q(NextTicket)).And.Not.Contain("sessionToken"));
            Assert.That(HttpCount("POST /api/v1/app/realtime/session"), Is.EqualTo(sessions), "a ticket reconnect needs no backend call");
            Assert.That(LastFrame("subscribe"), Does.Contain("\"sync\":{\"epoch\":" + Q(Epoch) + ",\"knownMessageIds\":[" + string.Join(",", Enumerable.Range(0, 20).Select(i => Q(Id(i)))) + "]}"));
            await Until(() => Chat.GetFeed(Channel).Length == 20 && Chat.GetFeed(Channel).Last().id == Id(20), "catch-up snapshot");
            Assert.That(HttpCount("POST /api/v1/app/chat"), Is.Zero);
        }

        [Test] public async Task Reconnect_TicketRejected_FallsBackToTheSessionTokenAtOnce()
        {
            Assert.That(Chat.WatchChannel(User, Channel), Is.True);
            await Connect("chat:" + Channel, Sync());
            int authenticates = FrameCount("authenticate");
            Inject("{\"type\":\"transport_closed\",\"closeCode\":1012}"); Tick();
            await Until(() => FrameCount("authenticate") > authenticates, "ticket authenticate");
            Assert.That(LastFrame("authenticate"), Does.Contain("\"ticket\":" + Q(Ticket)));
            int sessions = HttpCount("POST /api/v1/app/realtime/session");
            Inject("{\"type\":\"error\",\"version\":1,\"code\":\"SESSION_REQUIRED\"}"); Inject("{\"type\":\"transport_closed\",\"closeCode\":1008}");
            await Until(() => FrameCount("authenticate") > authenticates + 1, "token authenticate", 3);
            Assert.That(LastFrame("authenticate"), Does.Contain("\"sessionToken\":").And.Not.Contain("\"ticket\":"));
            Assert.That(HttpCount("POST /api/v1/app/realtime/session"), Is.EqualTo(sessions + 1));
            Assert.That(typeof(RealtimeManager).GetField("_attempt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Realtime), Is.EqualTo(0));
        }

        [Test] public async Task ConnectionLimitReached_KeepsEverythingOnHttpForPollAfterSeconds()
        {
            _reply = (path, body) => (201, Sent, null);
            Assert.That(Chat.WatchChannel(User, Channel), Is.True);
            await Until(() => _connects == 1 && LastFrame("authenticate") != null, "connect");
            Inject("{\"type\":\"error\",\"version\":1,\"code\":\"CONNECTION_LIMIT_REACHED\"}"); Tick();
            Assert.That(Realtime.LastErrorCode, Is.EqualTo("CONNECTION_LIMIT_REACHED")); Assert.That(Realtime.CanSendOverSocket, Is.False);
            float retryAt = (float)typeof(RealtimeManager).GetField("_retryAt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Realtime);
            Assert.That(retryAt - UnityEngine.Time.realtimeSinceStartup, Is.GreaterThan(1.5f).And.LessThanOrEqualTo(2.01f), "pollAfterSeconds of the session");
            var result = await Bounded(Chat.SendMessage(User, Channel, Operation, Time, "text"));
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode); Assert.That(LastFrame("chat.send"), Is.Null);
            Assert.That(_connects, Is.EqualTo(1));
        }

        [Test] public async Task Logs_SocketLifecycle_ContainNoKeyTokenOrTicket()
        {
            LogService.Instance.EnableUnityLogging = true; LogAssert.ignoreFailingMessages = true;
            Assert.That(Chat.WatchChannel(User, Channel), Is.True);
            await Connect("chat:" + Channel, Sync());
            Inject("{\"type\":\"ticket\",\"version\":1,\"ticket\":" + Q(NextTicket) + "}");
            Inject("{\"type\":\"transport_closed\",\"closeCode\":1012}"); Tick();
            await Connect("chat:" + Channel, Sync());
            Inject("{\"type\":\"error\",\"version\":1,\"code\":\"SESSION_REQUIRED\"}"); Tick();
            lock (_logs) Assert.That(_logs.Where(x => x.Contains(ApiKey) || x.Contains("session-" + User) || x.Contains(Ticket) || x.Contains(NextTicket)), Is.Empty, string.Join("\n", _logs));
        }
    }
}
