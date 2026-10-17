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
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")] public partial class SharedObjectsTransportTests
    {
        internal const string Host = "http://127.0.0.1:18912", User = "00000000-0000-4000-8000-000000001012", Id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", Other = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", Project = "11111111-1111-4111-8111-111111111111", ApiKey = "fixture-shared-1012", Time = "2026-10-16T00:00:00Z";
        private const string Limits = "{\"collections\":100,\"storageBytes\":2147483648,\"objectBytes\":65536}";
        private const string Search = "{\"label\":\"前\\u0000後😀\",\"strength\":9007199254740991}";
        private HttpListener _listener; private HorizonConfig _config;
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private SharedObjectsManager Manager => SharedObjectsManager.Instance;
        private static string Object(long revision = 1, string id = Id, string data = "AP8A", string search = Search, string group = "null") => "{\"id\":\"" + id + "\",\"collection\":\"levels\",\"ownerId\":\"" + User + "\",\"groupId\":" + group + ",\"revision\":" + revision + ",\"dataBase64\":\"" + data + "\",\"search\":" + search + ",\"position\":{\"x\":-1,\"y\":0},\"createdAt\":\"" + Time + "\",\"updatedAt\":\"" + Time + "\",\"expiresAt\":null}";
        private static string Usage(long revision = 0, int count = 0, string time = Time) => "{\"userId\":\"" + User + "\",\"collection\":\"levels\",\"revision\":" + revision + ",\"count\":" + count + ",\"limits\":" + Limits + ",\"pollAfterSeconds\":120,\"serverTime\":\"" + time + "\"}";
        private static string Read(long revision = 1, string id = Id) => "{\"object\":" + Object(revision, id) + ",\"pollAfterSeconds\":120,\"serverTime\":\"" + Time + "\"}";
        private static string Query(string objects = null, string cursor = "null") => "{\"objects\":" + (objects ?? "[" + Object() + "]") + ",\"nextCursor\":" + cursor + ",\"pollAfterSeconds\":120,\"serverTime\":\"" + Time + "\"}";
        private static string Write(long objectRevision = 1, long usageRevision = 1, string id = Id, string data = "AP8A", string search = Search) => "{\"object\":" + Object(objectRevision, id, data, search) + ",\"usage\":" + Usage(usageRevision, 1) + ",\"pollAfterSeconds\":120,\"serverTime\":\"" + Time + "\"}";
        private static string Delete(long usageRevision = 2) => "{\"objectId\":\"" + Id + "\",\"deleted\":true,\"usage\":" + Usage(usageRevision) + ",\"pollAfterSeconds\":120,\"serverTime\":\"" + Time + "\"}";
        private static string Definition(string key = "levels") => "{\"key\":\"" + key + "\",\"name\":\"前\\u0000後😀\",\"active\":false,\"readPolicy\":\"all\",\"writePolicy\":\"owner\",\"maxObjectBytes\":65536,\"maxObjectsPerOwner\":100,\"ttlSeconds\":null,\"searchFields\":[{\"key\":\"strength\",\"type\":\"integer\"}],\"positionEnabled\":true,\"used\":true,\"revision\":1}";
        private static string Collections(long revision = 1, string rows = null, string next = "null") => "{\"revision\":" + revision + ",\"definitions\":" + (rows ?? "[" + Definition() + "]") + ",\"nextAfterKey\":" + next + ",\"limits\":" + Limits + ",\"serverTime\":\"" + Time + "\"}";
        private static SharedObjectInput Input() => new SharedObjectInput { data = new byte[] { 0, 255, 0 }, position = new SharedPosition { x = -1, y = 0 }, search = new[] { new SharedSearchValue { key = "strength", value = new SharedScalar { integerValue = 9007199254740991 } }, new SharedSearchValue { key = "label", value = new SharedScalar { type = SharedScalarType.String, stringValue = "前\0後😀" } } } };
        [SetUp] public void Setup()
        {
            Counts.Clear(); PlayerPrefs.DeleteKey("horizOn_UserSession"); NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            _config = ScriptableObject.CreateInstance<HorizonConfig>(); _config.SetApiKey(ApiKey); _config.SetHosts(new[] { Host });
            NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost(Host); SetUser(User);
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false;
            _listener = new HttpListener(); _listener.Prefixes.Add(Host + "/"); _listener.Start(); CleanupJournal();
        }
        [TearDown] public void Stop()
        {
            Debug.Log("SHARED_OBJECTS_HTTP_COUNTS " + string.Join(",", Counts.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)));
            _listener?.Close();
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (new[] { "[SharedObjectsManager]", "[UserManager]", "[ValidatedActionsManager]", "[LeaderboardManager]", "[SharedObjectsExampleTest]" }.Contains(go.name)) UnityEngine.Object.DestroyImmediate(go);
            CleanupJournal(); UnityEngine.Object.DestroyImmediate(_config); PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }
        [Test] public async Task AllSevenRoutesUseMatchingBearerExactBytesAndOriginalHeaders()
        {
            var page = await Reply(Manager.GetCollections(User, limit: 7, catalogRevision: 1), Collections());
            Assert.That(page.IsSuccess, Is.True); Assert.That(page.Data.definitions[0].name, Is.EqualTo("前\0後😀")); Assert.That(page.Data.definitions[0].active, Is.False); Assert.That(page.Data.limits.storageBytes, Is.EqualTo(2147483648L));
            Assert.That((await Reply(Manager.GetUsage(User, "levels"), Usage())).IsSuccess, Is.True);
            var read = await Reply(Manager.GetObject(User, "levels", Other), Read(id: Other)); Assert.That(read.IsSuccess, Is.True);
            Assert.That(read.Data.@object.data, Is.EqualTo(new byte[] { 0, 255, 0 })); Assert.That(read.Data.@object.search.Single(x => x.key == "label").value.stringValue, Is.EqualTo("前\0後😀"));
            var query = await Reply(Manager.QueryObjects(User, "levels", new SharedQuery { mode = SharedQueryMode.Rectangle, minX = -5, maxX = 5, minY = -5, maxY = 5 }), Query()); Assert.That(query.IsSuccess, Is.True);
            var input = Input(); var incoming = _listener.GetContextAsync(); var call = Manager.CreateObject(User, "levels", 0, input); input.data[1] = 1; input.search[0].value.integerValue = 2;
            var c = await Timeout(incoming); Headers(c); Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/shared-objects/levels/objects")); Assert.That(c.Request.HttpMethod, Is.EqualTo("POST")); Assert.That(c.Request.Headers["If-Match"], Is.EqualTo("0"));
            string body = await Body(c); Assert.That(body, Does.Contain("\"dataBase64\":\"AP8A\"").And.Contain("\"strength\":9007199254740991").And.Contain("前\\u0000後😀").And.Contain("\"y\":0"));
            await Respond(c, Write()); Assert.That((await call).IsSuccess, Is.True);
            incoming = _listener.GetContextAsync(); var update = Manager.UpdateObject(User, "levels", Id, 1, Input()); c = await Timeout(incoming); Headers(c); Assert.That(c.Request.HttpMethod, Is.EqualTo("PUT")); Assert.That(c.Request.Headers["If-Match"], Is.EqualTo("1")); Assert.That(await Body(c), Does.Not.Contain("groupId")); await Respond(c, Write(2)); Assert.That((await update).IsSuccess, Is.True);
            incoming = _listener.GetContextAsync(); var delete = Manager.DeleteObject(User, "levels", Id, 2); c = await Timeout(incoming); Headers(c); Assert.That(c.Request.HttpMethod, Is.EqualTo("DELETE")); Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/shared-objects/levels/objects/" + Id + "?userId=" + User)); Assert.That(c.Request.Headers["If-Match"], Is.EqualTo("2")); await Respond(c, Delete()); Assert.That((await delete).IsSuccess, Is.True);
            Assert.That((await Reply(Manager.QueryObjects(User, "levels", new SharedQuery { cursor = "next-opaque" }), Query())).ErrorCode, Is.EqualTo("STALE_RESPONSE"), "deleted ID never reappears in a later query");
        }
        [TestCase("create", 429)][TestCase("create", 503)][TestCase("update", 429)][TestCase("update", 503)][TestCase("delete", 429)][TestCase("delete", 503)]
        public async Task EveryMutationHasExactlyOneTransportAttempt(string action, int status)
        {
            Task call = action == "create" ? Manager.CreateObject(User, "levels", 0, Input()) : action == "update" ? Manager.UpdateObject(User, "levels", Id, 4294967296L, Input()) : (Task)Manager.DeleteObject(User, "levels", Id, 4294967296L);
            var c = await Timeout(_listener.GetContextAsync()); Headers(c); Assert.That(c.Request.Headers["If-Match"], Is.EqualTo(action == "create" ? "0" : "4294967296")); await Respond(c, "{\"message\":\"unknown\"}", status); await Timeout(call);
            var extra = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(extra, Task.Delay(100)), Is.Not.SameAs(extra));
        }
        [Test] public async Task ReadCadenceHasOpaqueIdentityCloneAndExplicitRecoveryBypass()
        {
            var r = await Reply(Manager.GetUsage(User, "levels"), Usage(5)); Assert.That(r.IsSuccess, Is.True); Assert.That(r.Identity.Length, Is.EqualTo(64));
            r.Data.count = 99; Assert.That(Manager.GetCached<SharedUsage>(r.Identity).count, Is.EqualTo(0));
            var blocked = await Manager.GetUsage(User, "levels"); Assert.That(blocked.ErrorCode, Is.EqualTo("REFRESH_TOO_SOON")); Assert.That(blocked.RetryAfterSeconds, Is.GreaterThan(0));
            Manager.ClearCache(); Assert.That(Manager.GetCached<SharedUsage>(r.Identity), Is.Null);
            Assert.That((await Manager.GetUsage(User, "levels")).ErrorCode, Is.EqualTo("REFRESH_TOO_SOON"));
            Assert.That((await Reply(Manager.GetRecoveryUsage(User, "levels"), Usage(4))).ErrorCode, Is.EqualTo("STALE_RESPONSE"));
            Assert.That((await Reply(Manager.GetRecoveryUsage(User, "levels"), Usage(6))).IsSuccess, Is.True);
            Assert.That((await Manager.QueryRecoveryObjects(User, "levels", new SharedQuery { mode = SharedQueryMode.Owner, ownerId = Other })).ErrorCode, Is.EqualTo("INVALID_REQUEST"));
        }
        [Test] public async Task CatalogAndOwnerUsageRevisionsAreIndependentAndPagesStayConsistent()
        {
            Assert.That((await Reply(Manager.GetCollections(User), Collections(99, next: "\"levels\""))).IsSuccess, Is.True);
            Assert.That((await Reply(Manager.GetCollections(User, "levels", 1, 99), Collections(99, "[" + Definition("worlds") + "]"))).IsSuccess, Is.True);
            Assert.That((await Reply(Manager.GetUsage(User, "levels"), Usage(1))).IsSuccess, Is.True);
            Assert.That((await Reply(Manager.GetCollections(User, "worlds", 1, 99), Collections(100, "[]"))).ErrorCode, Is.EqualTo("INVALID_RESPONSE"));
            Assert.That((await Reply(Manager.GetCollections(User, "worlds", 1, 100), Collections(100, next: "\"wrong\""))).IsSuccess, Is.False);
        }
        [TestCase("host")][TestCase("project")][TestCase("session")][TestCase("clear")]
        public async Task LateResponsesCannotRestoreOldIdentityAcrossAba(string kind)
        {
            var incoming = _listener.GetContextAsync(); var call = Manager.GetObject(User, "levels", Id); var c = await Timeout(incoming);
            if (kind == "host") { NetworkService.Instance.SetActiveHost("http://other"); NetworkService.Instance.SetActiveHost(Host); }
            if (kind == "project") { _config.SetApiKey("other"); _config.SetApiKey(ApiKey); }
            if (kind == "session") { NetworkService.Instance.SetSessionToken("other"); NetworkService.Instance.SetSessionToken("session-" + User); }
            if (kind == "clear") Manager.ClearCache();
            await Respond(c, Read()); var r = await call; Assert.That(r.ErrorCode, Is.EqualTo("STALE_RESPONSE"));
        }
        [Test] public async Task QueuedQueriesFreezeInputAndNeverOverlapHttp()
        {
            var incoming = _listener.GetContextAsync(); var first = Manager.GetUsage(User, "levels"); var c = await Timeout(incoming);
            var q = new SharedQuery { mode = SharedQueryMode.Field, field = "strength", equal = new SharedScalar { integerValue = 9007199254740991 } };
            var next = Manager.QueryObjects(User, "levels", q); q.equal.integerValue = 1; var secondIncoming = _listener.GetContextAsync();
            Assert.That(await Task.WhenAny(secondIncoming, Task.Delay(100)), Is.Not.SameAs(secondIncoming)); await Respond(c, Usage()); Assert.That((await first).IsSuccess, Is.True);
            c = await Timeout(secondIncoming); Assert.That(await Body(c), Does.Contain("\"equal\":9007199254740991")); await Respond(c, Query()); Assert.That((await next).IsSuccess, Is.True);
        }
        [TestCase("1.0000000000000001")][TestCase("1e0")][TestCase("-0")][TestCase("9007199254740992")][TestCase("false")]
        public async Task OriginalScalarTokensAreStrictEvenWithEscapedKeys(string number)
        {
            Assert.That((await Reply(Manager.GetObject(User, "levels", Id), Read().Replace("\"strength\":9007199254740991", "\"str\\u0065ngth\":" + number))).ErrorCode, Is.EqualTo("INVALID_RESPONSE"));
        }
        [Test] public async Task InvalidForeignAndContradictoryRequestsNeverReachHttp()
        {
            Assert.That((await Manager.GetUsage(Other, "levels")).ErrorCode, Is.EqualTo("SESSION_FORBIDDEN"));
            Assert.That((await Manager.GetUsage(User, "a\0b")).ErrorCode, Is.EqualTo("INVALID_REQUEST"));
            Assert.That((await Manager.QueryObjects(User, "levels", new SharedQuery { mode = SharedQueryMode.Random, field = "strength", cursor = "old", min = new SharedScalar(), max = new SharedScalar() })).IsSuccess, Is.False);
            Assert.That((await Manager.CreateObject(User, "levels", -1, Input())).IsSuccess, Is.False);
            var input = Input(); input.data = new byte[1048577]; Assert.That((await Manager.CreateObject(User, "levels", 0, input)).IsSuccess, Is.False);
            input = Input(); input.search[0].value.integerValue = 9007199254740992; Assert.That((await Manager.UpdateObject(User, "levels", Id, 0, input)).IsSuccess, Is.False);
            NetworkService.Instance.ClearSessionToken(); Assert.That((await Manager.GetUsage(User, "levels")).ErrorCode, Is.EqualTo("SESSION_REQUIRED"));
            var incoming = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(incoming, Task.Delay(100)), Is.Not.SameAs(incoming));
        }
        [Test] public async Task QueryCannotReturnAnotherPartitionOrOutOfRangeObject()
        {
            var q = new SharedQuery { mode = SharedQueryMode.Rectangle, minX = 5, maxX = 10, minY = 5, maxY = 10 };
            Assert.That((await Reply(Manager.QueryObjects(User, "levels", q), Query())).ErrorCode, Is.EqualTo("INVALID_RESPONSE"));
            q = new SharedQuery { mode = SharedQueryMode.Owner, ownerId = Other };
            Assert.That((await Reply(Manager.QueryObjects(User, "levels", q), Query())).ErrorCode, Is.EqualTo("INVALID_RESPONSE"));
            q = new SharedQuery { mode = SharedQueryMode.Field, field = "strength", equal = new SharedScalar { integerValue = 3 } };
            Assert.That((await Reply(Manager.QueryObjects(User, "levels", q), Query())).ErrorCode, Is.EqualTo("INVALID_RESPONSE"));
        }
        [Test] public async Task PrivateOpaquePayloadAndProjectKeyAreAbsentFromSdkLogEvents()
        {
            var logs = new List<string>(); LogService.Instance.EnableEventPublishing = true;
            HorizonApp.Events.Subscribe<LogEventData>(EventKeys.ErrorOccurred, entry => logs.Add(entry.Message));
            var input = Input(); input.data = Encoding.UTF8.GetBytes("private-opaque-payload-marker"); string encoded = Convert.ToBase64String(input.data);
            var result = await Reply(Manager.CreateObject(User, "levels", 0, input), Write(data: encoded)); Assert.That(result.IsSuccess, Is.True);
            Assert.That(logs.Any(s => s.Contains("Private player request:")), Is.True);
            Assert.That(logs.Any(s => s.Contains(encoded) || s.Contains("前") || s.Contains(ApiKey) || s.Contains("fixture-sh")), Is.False, string.Join("\n", logs));
        }
        private static void Headers(HttpListenerContext c)
        { Assert.That(c.Request.Headers["X-API-Key"], Is.EqualTo(ApiKey)); Assert.That(c.Request.Headers["Authorization"], Is.EqualTo("Bearer session-" + User)); }
        private async Task<SharedObjectsResult<T>> Reply<T>(Task<SharedObjectsResult<T>> call, string json, int status = 200) where T : class
        { var c = await Timeout(_listener.GetContextAsync()); Headers(c); await Respond(c, json, status); return await Timeout(call); }
        private static async Task Respond(HttpListenerContext c, string json, int status = 200)
        {
            string key = c.Request.HttpMethod + " " + c.Request.Url.AbsolutePath; Counts[key] = Counts.TryGetValue(key, out int n) ? n + 1 : 1;
            c.Response.StatusCode = status; c.Response.ContentType = "application/json"; byte[] bytes = Encoding.UTF8.GetBytes(json); c.Response.ContentLength64 = bytes.Length; await c.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length); c.Response.Close();
        }
        private static Task<string> Body(HttpListenerContext c) => new StreamReader(c.Request.InputStream).ReadToEndAsync();
        private static async Task<T> Timeout<T>(Task<T> task)
        { Assert.That(await Task.WhenAny(task, Task.Delay(10000)), Is.SameAs(task), "bounded fixture wait"); return await task; }
        private static async Task Timeout(Task task)
        { Assert.That(await Task.WhenAny(task, Task.Delay(10000)), Is.SameAs(task), "bounded fixture wait"); await task; }
        private static void SetUser(string user)
        {
            var value = new UserData { UserId = user, AccessToken = "session-" + user };
            typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(UserManager.Instance, value);
            NetworkService.Instance.SetSessionToken(value.AccessToken);
        }
        private static string JournalPrefix => "shared-objects-" + Hash(ApiKey) + "-" + User + ".journal";
        private static string Hash(string s) { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").ToLowerInvariant(); }
        private static void CleanupJournal()
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            foreach (string path in Directory.GetDirectories(Application.persistentDataPath, JournalPrefix + "*")) Directory.Delete(path, true);
            string gate = Path.Combine(Application.persistentDataPath, JournalPrefix + ".lock"); if (File.Exists(gate)) File.Delete(gate);
        }
    }
}
