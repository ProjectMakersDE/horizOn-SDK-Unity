using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")]
    public partial class ChatTransportTests
    {
        const string Host = "http://127.0.0.1:18916", User = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", Peer = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
            Channel = "cccccccc-cccc-4ccc-8ccc-cccccccccccc", Epoch = "dddddddd-dddd-4ddd-8ddd-dddddddddddd", Operation = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee",
            Time = "2026-10-17T00:00:00.000Z", Later = "2026-10-24T00:00:00.000Z", ApiKey = "chat-fixture-key";
        HttpListener _listener; HorizonConfig _config; string _root; int _count;
        ChatManager Manager => ChatManager.Instance;
        static string Q(string s) => ChatJson.Quote(s);
        static string Id(int i) => i.ToString("x8") + "-ffff-4fff-8fff-ffffffffffff";
        static string ChannelJson(long revision = 1, string epoch = Epoch, string kind = "project") => "{\"id\":" + Q(Channel) + ",\"epoch\":" + Q(epoch) + ",\"kind\":" + Q(kind)
            + ",\"peerIds\":" + (kind == "direct" ? "[" + Q(User) + "," + Q(Peer) + "]" : "[]") + ",\"groupId\":null,\"revision\":" + revision + ",\"expiresAt\":" + Q(Later) + "}";
        static string Message(int i) => ChatJson.MessageJson(new ChatMessage { id = Id(i), channelId = Channel, authorId = Peer, text = "private 😀 " + i, createdAt = Time, expiresAt = Later });
        static string Sync(int start = 0, int count = 20, int known = 0, long revision = 1, string epoch = Epoch) => "{\"channel\":" + ChannelJson(revision, epoch)
            + ",\"visibleMessageIds\":[" + string.Join(",", Enumerable.Range(start, count).Select(i => Q(Id(i)))) + "],\"messages\":["
            + string.Join(",", Enumerable.Range(start + known, count - known).Select(Message)) + "],\"moderation\":{\"muteUntil\":null,\"banUntil\":null},\"serverTime\":" + Q(Time) + "}";
        static string Receipt => "{\"operationId\":" + Q(Operation) + ",\"issuedAt\":" + Q(Time) + ",\"channelId\":" + Q(Channel) + ",\"messageId\":" + Q(Id(99)) + ",\"createdAt\":" + Q(Time) + ",\"expiresAt\":" + Q(Later) + "}";
        static string Sent => "{\"receipt\":" + Receipt + ",\"replayed\":false,\"serverTime\":" + Q(Time) + "}";
        static string Limits => "{\"limits\":{\"retainedMessagesPerPlayer\":0,\"receiptCount\":0,\"reportReceiptCount\":0,\"channelsPerAccount\":100,\"sendsPerAccountMinute\":20,\"reportsPerProject\":50,\"retainedMessagesPerChannel\":20,\"retentionSeconds\":604800,\"privateRetainedMessagesPerChannel\":15,\"privateRetentionSeconds\":30,\"operationWindowSeconds\":30,\"operationFutureToleranceSeconds\":60,\"messageCharacters\":500,\"messageUtf8Bytes\":2000,\"playerSendIntervalMillis\":3000,\"channelSendIntervalMillis\":2000,\"wordFilterTerms\":100},\"capabilities\":{\"chat\":true,\"realtime\":true,\"incrementalSync\":true,\"localHistory\":true},\"moderation\":{\"muteUntil\":null,\"banUntil\":null},\"serverTime\":" + Q(Time) + "}";
        [SetUp] public void Setup()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession"); NetworkService.ResetInstance(); EventService.ResetInstance(); LogService.ResetInstance();
            _config = ScriptableObject.CreateInstance<HorizonConfig>(); _config.SetApiKey(ApiKey); _config.SetHosts(new[] { Host });
            NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost(Host); SetUser(User);
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false;
            _root = Path.Combine(Path.GetTempPath(), "horizon-chat-native-" + Guid.NewGuid().ToString("N")); Manager.HistoryRootOverride = _root;
            _listener = new HttpListener(); _listener.Prefixes.Add(Host + "/"); _listener.Start(); _count = 0;
        }
        [TearDown] public void Cleanup()
        {
            Debug.Log("CHAT_HTTP_COUNT " + _count); _listener?.Close();
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (new[] { "[ChatManager]", "[RealtimeManager]", "[UserManager]" }.Contains(go.name)) UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(_config); PlayerPrefs.DeleteKey("horizOn_UserSession");
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        [Test] public async Task SixAuthorizedRoutesAndConfirmedLocalSend()
        {
            var limits = Manager.GetLimits(User); var request = await Incoming(); Assert.That(request.Request.RawUrl, Does.EndWith("/chat/limits?userId=" + User)); await Respond(request, Limits);
            Assert.That((await limits).Data.limits.retainedMessagesPerPlayer, Is.Zero);
            var open = Manager.OpenChannel(User, "project"); request = await Incoming(); Assert.That(await Body(request), Is.EqualTo("{\"userId\":" + Q(User) + ",\"kind\":\"project\"}"));
            await Respond(request, "{\"channel\":" + ChannelJson() + ",\"serverTime\":" + Q(Time) + "}"); Assert.That((await open).IsSuccess, Is.True);
            Assert.That((await Reply(Manager.SyncChannel(User, Channel), Sync(count: 1))).IsSuccess, Is.True);
            var send = Manager.SendMessage(User, Channel, Operation, Time, "my 😀 message"); request = await Incoming();
            Assert.That(request.Request.RawUrl, Does.EndWith("/" + Channel + "/messages")); Assert.That(await Body(request), Does.Contain(Operation).And.Contain("my 😀 message"));
            await Respond(request, Sent, 201); Assert.That((await send).IsSuccess, Is.True); Assert.That(Manager.ReadLocalHistory(Channel).Data.messages[0].text, Is.EqualTo("my 😀 message"));
            var operation = Manager.GetOperation(User, Operation); request = await Incoming(); Assert.That(request.Request.RawUrl, Does.Contain("/operations/" + Operation));
            await Respond(request, "{\"receipt\":" + Receipt + ",\"serverTime\":" + Q(Time) + "}"); Assert.That((await operation).IsSuccess, Is.True);
            var report = Manager.ReportMessage(User, Channel, Id(0), "reason"); request = await Incoming(); Assert.That(request.Request.RawUrl, Does.EndWith("/messages/" + Id(0) + "/report"));
            await Respond(request, "{\"reportId\":" + Q(Operation) + ",\"status\":\"open\",\"replayed\":false,\"serverTime\":" + Q(Time) + "}"); Assert.That((await report).IsSuccess, Is.True);
            Assert.That(_count, Is.EqualTo(6));
        }
        [Test] public async Task SharedWindowDropsBodiesButLocalArchiveKeepsThemAcrossEpochAndLogout()
        {
            var first = await Reply(Manager.SyncChannel(User, Channel), Sync()); Assert.That(first.IsSuccess, Is.True, first.ErrorCode);
            var second = Manager.SyncChannel(User, Channel, first.Data); var request = await Incoming(); Assert.That(await Body(request), Does.Contain(Id(0)).And.Contain(Id(19)));
            await Respond(request, Sync(start: 1, known: 19, revision: 2)); var current = await second;
            Assert.That(current.Data.messages.Length, Is.EqualTo(20)); Assert.That(Manager.ReadLocalHistory(Channel).Data.messages.Length, Is.EqualTo(21));
            Assert.That((await Reply(Manager.SyncChannel(User, Channel, current.Data), Sync(count: 0, epoch: Operation))).IsSuccess, Is.True);
            Assert.That(Manager.ReadLocalHistory(Channel).Data.messages.Length, Is.EqualTo(21));
            SetUser(Peer); Assert.That(Manager.ReadLocalHistory(Channel).Data.messages, Is.Empty); SetUser(User);
            Assert.That(Manager.ReadLocalHistory(Channel).Data.messages.Length, Is.EqualTo(21));
            _config.SetApiKey("another-project"); Assert.That(Manager.ReadLocalHistory(Channel).Data.messages, Is.Empty); _config.SetApiKey(ApiKey);
            Assert.That(Manager.ReadLocalHistory(Channel).Data.messages.Length, Is.EqualTo(21));
        }
        [TestCase(429)] [TestCase(503)] public async Task SendAndReportAreOnceAndMissingOperationNeverResends(int status)
        {
            Assert.That((await Reply(Manager.SendMessage(User, Channel, Operation, Time, "once"), "{\"code\":\"UNAVAILABLE\"}", status)).IsSuccess, Is.False);
            Assert.That((await Reply(Manager.ReportMessage(User, Channel, Id(0), "reason"), "{\"code\":\"UNAVAILABLE\"}", status)).IsSuccess, Is.False);
            Assert.That((await Reply(Manager.GetOperation(User, Operation), "{\"code\":\"OPERATION_NOT_FOUND\"}", 404)).IsSuccess, Is.False);
            var next = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(next, Task.Delay(150)), Is.Not.SameAs(next)); Assert.That(_count, Is.EqualTo(3));
            Assert.That(Manager.ReadLocalHistory(Channel).Data.messages, Is.Empty);
        }
        [TestCase("host")] [TestCase("key")] [TestCase("session")] [TestCase("cache")]
        public async Task DelayedIdentityAbaCannotWriteArchive(string change)
        {
            var task = Manager.SyncChannel(User, Channel); var request = await Incoming();
            if (change == "host") { NetworkService.Instance.SetActiveHost("http://other"); NetworkService.Instance.SetActiveHost(Host); }
            if (change == "key") { _config.SetApiKey("other"); _config.SetApiKey(ApiKey); }
            if (change == "session") { NetworkService.Instance.SetSessionToken("other"); NetworkService.Instance.SetSessionToken("session-" + User); }
            if (change == "cache") Manager.ClearCache();
            await Respond(request, Sync(count: 1)); Assert.That((await task).ErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That(Manager.ReadLocalHistory(Channel).Data.messages, Is.Empty);
        }
        [Test] public async Task ClearWhileRequestIsPendingDoesNotRecreateHistory()
        {
            var task = Manager.SyncChannel(User, Channel); var request = await Incoming(); Assert.That(Manager.ClearLocalHistory(Channel), Is.True);
            await Respond(request, Sync(count: 1)); Assert.That((await task).IsSuccess, Is.True); Assert.That(Manager.ReadLocalHistory(Channel).Data.messages, Is.Empty);
        }
        [Test] public async Task ClearingAnotherChannelPreservesAnAcceptedSend()
        {
            var task = Manager.SendMessage(User, Channel, Operation, Time, "keep this"); var request = await Incoming();
            Assert.That(Manager.ClearLocalHistory(Peer), Is.True); await Respond(request, Sent, 201);
            Assert.That((await task).IsSuccess, Is.True); Assert.That(Manager.ReadLocalHistory(Channel).Data.messages.Length, Is.EqualTo(1));
        }
        [TestCase("resync")] [TestCase("disconnect")] [TestCase("cache")]
        public async Task LiveWindowInvalidationDoesNotDiscardAnAcceptedSend(string change)
        {
            var task = Manager.SendMessage(User, Channel, Operation, Time, "accepted during reconnect"); var request = await Incoming();
            if (change == "cache") Manager.ClearCache();
            else typeof(ChatManager).GetMethod(change == "resync" ? "OnResync" : "OnConnectionError", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(Manager, new object[] { change == "resync" ? "chat:" + Channel : "DISCONNECTED" });
            await Respond(request, Sent, 201); Assert.That((await task).IsSuccess, Is.True);
            Assert.That(Manager.ReadLocalHistory(Channel).Data.messages.Length, Is.EqualTo(1));
        }
        [Test] public async Task SameOwnerTransportChangesKeepWatchesAndClockUsesMonotonicElapsedTime()
        {
            double clock = 1; Manager.NowSeconds = () => clock;
            Assert.That(Manager.WatchChannel(User, Channel), Is.True);
            Assert.That((await Reply(Manager.GetLimits(User), Limits)).IsSuccess, Is.True);
            clock = 4; Assert.That(Manager.GetServerTime(), Is.EqualTo("2026-10-17T00:00:03.000Z"));
            NetworkService.Instance.SetActiveHost("http://other"); Manager.GetFeed(Channel); NetworkService.Instance.SetActiveHost(Host); Manager.GetFeed(Channel);
            var watches = (System.Collections.IDictionary)typeof(ChatManager).GetField("_watches", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Manager);
            Assert.That(watches.Count, Is.EqualTo(1));
            SetUser(User); Manager.GetFeed(Channel); Assert.That(watches.Count, Is.EqualTo(1));
            SetUser(Peer); Manager.GetFeed(Channel); Assert.That(watches.Count, Is.Zero);
        }
        [Test] public async Task RealIoFailureDoesNotTurnAcceptedSendIntoFailureOrRetry()
        {
            Directory.CreateDirectory(_root); string file = Path.Combine(_root, "not-a-directory"); File.WriteAllText(file, "fixture"); Manager.HistoryRootOverride = file;
            var errors = new List<string>(); Manager.LocalHistoryFailed += (_, code) => errors.Add(code);
            Assert.That((await Reply(Manager.SendMessage(User, Channel, Operation, Time, "accepted"), Sent, 201)).IsSuccess, Is.True);
            Assert.That(errors, Is.EqualTo(new[] { "LOCAL_HISTORY_IO_FAILED" })); Assert.That(_count, Is.EqualTo(1));
        }
        [Test] public async Task InvalidWindowAndStrictLexemesNeverReachArchive()
        {
            foreach (var json in new[] { Sync(count: 21), Sync(count: 1).Replace("\"revision\":1", "\"revision\":1e0"), Sync(count: 1).Replace("private 😀 0", "bad\\u0000"), Sync(count: 1).Replace("\"messages\":[" + Message(0) + "]", "\"messages\":[]") })
                Assert.That((await Reply(Manager.SyncChannel(User, Channel), json)).ErrorCode, Is.EqualTo("INVALID_RESPONSE"));
            Assert.That(Manager.ReadLocalHistory(Channel).Data.messages, Is.Empty);
            Assert.That((await Manager.SendMessage(User, Channel, Operation, Time, new string('x', 501))).ErrorCode, Is.EqualTo("INVALID_REQUEST"));
        }
        async Task<HttpListenerContext> Incoming()
        {
            var request = await Bounded(_listener.GetContextAsync()); Assert.That(request.Request.Headers["X-API-Key"], Is.EqualTo(ApiKey));
            Assert.That(request.Request.Headers["Authorization"], Is.EqualTo("Bearer session-" + User)); return request;
        }
        async Task<ChatResult<T>> Reply<T>(Task<ChatResult<T>> task, string json, int status = 200) where T : class { await Respond(await Incoming(), json, status); return await Bounded(task); }
        async Task Respond(HttpListenerContext request, string json, int status = 200)
        { _count++; request.Response.StatusCode = status; request.Response.ContentType = "application/json"; byte[] bytes = Encoding.UTF8.GetBytes(json); request.Response.ContentLength64 = bytes.Length; await request.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length); request.Response.Close(); }
        static Task<string> Body(HttpListenerContext request) => new StreamReader(request.Request.InputStream).ReadToEndAsync();
        static async Task<T> Bounded<T>(Task<T> task) { Assert.That(await Task.WhenAny(task, Task.Delay(10000)), Is.SameAs(task), "fixture timeout"); return await task; }
        static void SetUser(string id)
        { var user = new UserData { UserId = id, AccessToken = "session-" + id }; typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(UserManager.Instance, user); NetworkService.Instance.SetSessionToken(user.AccessToken); }
    }
}
