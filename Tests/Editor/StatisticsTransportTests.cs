using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")]
    public class StatisticsTransportTests
    {
        private const string Host = "http://127.0.0.1:18805";
        private HttpListener _listener;

        [SetUp]
        public void Setup()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
            NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            var config = ScriptableObject.CreateInstance<HorizonConfig>();
            config.SetApiKey("project-key-statistics"); config.SetHosts(new[] { Host });
            NetworkService.Instance.Initialize(config); NetworkService.Instance.SetActiveHost(Host);
            NetworkService.Instance.SetSessionToken("statistics-session");
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false;
            SetUser("statistics-session");
            _listener = new HttpListener(); _listener.Prefixes.Add(Host + "/"); _listener.Start();
        }

        [TearDown]
        public void Cleanup()
        {
            _listener?.Close();
            foreach (var obj in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (obj.name == "[UserManager]" || obj.name == "[StatisticsManager]") UnityEngine.Object.DestroyImmediate(obj);
            PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }

        [Test]
        public async Task Snapshot_UsesBearer_ExactLongs_AndConditionalCatalog()
        {
            var incoming = _listener.GetContextAsync();
            var loading = StatisticsManager.Instance.GetStatistics();
            var context = await Receive(incoming);
            Assert.That(context.Request.HttpMethod, Is.EqualTo("GET"));
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/statistics?userId=statistics-user"));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer statistics-session"));
            Assert.That(context.Request.Headers["X-API-Key"], Is.EqualTo("project-key-statistics"));
            await Respond(context, 200, Snapshot(1, -9007199254740991L));
            var first = await loading;
            Assert.That(first.values[0].value, Is.EqualTo(-9007199254740991L));
            Assert.That(first.definitions[0].HasMaxPerUpdate, Is.False);
            incoming = _listener.GetContextAsync(); loading = StatisticsManager.Instance.GetStatistics();
            context = await Receive(incoming);
            Assert.That(context.Request.RawUrl, Does.Contain("definitionVersion=1"));
            await Respond(context, 200, Snapshot(2, 5, omitDefinitions: true));
            Assert.That((await loading).definitions.Length, Is.EqualTo(1));
            Assert.That(StatisticsManager.Instance.CurrentStatistics.revision, Is.EqualTo(2));
            SetUser("another-session"); NetworkService.Instance.SetSessionToken("another-session");
            Assert.That(StatisticsManager.Instance.CurrentStatistics, Is.Null);
            Assert.That(StatisticsManager.Instance.CurrentDefinitions, Is.Null);
        }

        [Test]
        public async Task Update_PreservesExplicitIdAndSafeInteger_AndExposesStableErrors()
        {
            string operation = StatisticsManager.Instance.CreateOperationId();
            var incoming = _listener.GetContextAsync();
            var updating = StatisticsManager.Instance.UpdateStatistics(new[] { new StatisticUpdate("kills", 9007199254740991L) }, operation);
            var context = await Receive(incoming);
            string body;
            using (var reader = new StreamReader(context.Request.InputStream)) body = await reader.ReadToEndAsync();
            Assert.That(context.Request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer statistics-session"));
            Assert.That(body, Does.Contain("\"operationId\":\"" + operation + "\""));
            Assert.That(body, Does.Contain("\"value\":9007199254740991"));
            await Respond(context, 200, Snapshot(1, 9007199254740991L, operation));
            Assert.That((await updating).operationId, Is.EqualTo(operation));
            incoming = _listener.GetContextAsync();
            updating = StatisticsManager.Instance.UpdateStatistics(new[] { new StatisticUpdate("kills", 1) }, operation);
            await Respond(await Receive(incoming), 409, "{\"code\":\"OPERATION_CONFLICT\",\"message\":\"conflict\"}");
            Assert.That(await updating, Is.Null);
            Assert.That(StatisticsManager.Instance.LastErrorCode, Is.EqualTo(StatisticsErrorCodes.OperationConflict));
            Assert.That(StatisticsManager.Instance.LastSubmittedOperationId, Is.EqualTo(operation));
        }

        [Test]
        public async Task LocalValidationAndInitialization_NeverSendRequests()
        {
            var unexpected = _listener.GetContextAsync();
            var manager = StatisticsManager.Instance;
            Assert.That(manager.CurrentStatistics, Is.Null);
            Assert.That(await manager.UpdateStatistics(new[] { new StatisticUpdate("kills", 1) }, "no-timestamp"), Is.Null);
            Assert.That(manager.LastErrorCode, Is.EqualTo(StatisticsErrorCodes.InvalidRequest));
            Assert.That(await manager.UpdateStatistics(new[] { new StatisticUpdate("kills", 9007199254740992L) }, manager.CreateOperationId()), Is.Null);
            SetUser(null); NetworkService.Instance.ClearSessionToken();
            Assert.That(await manager.GetStatistics(), Is.Null);
            Assert.That(manager.LastErrorCode, Is.EqualTo(StatisticsErrorCodes.SessionRequired));
            Assert.That(await Task.WhenAny(unexpected, Task.Delay(250)), Is.Not.SameAs(unexpected));
        }

        [Test]
        public async Task LateOldSessionResponse_CannotEraseFreshSessionSnapshot()
        {
            var incoming = _listener.GetContextAsync();
            var oldLoad = StatisticsManager.Instance.GetStatistics();
            var oldRequest = await Receive(incoming);
            SetUser("fresh-session"); NetworkService.Instance.SetSessionToken("fresh-session");
            incoming = _listener.GetContextAsync();
            var freshLoad = StatisticsManager.Instance.GetStatistics();
            await Respond(await Receive(incoming), 200, Snapshot(1, 9));
            Assert.That(await freshLoad, Is.Not.Null);
            await Respond(oldRequest, 401, "{\"code\":\"SESSION_REQUIRED\",\"message\":\"expired\"}");
            Assert.That(await oldLoad, Is.Null);
            Assert.That(StatisticsManager.Instance.CurrentStatistics.values[0].value, Is.EqualTo(9));
        }

        private static void SetUser(string token) => typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(UserManager.Instance, token == null ? new UserData() : new UserData { UserId = "statistics-user", AccessToken = token });
        private static async Task<HttpListenerContext> Receive(Task<HttpListenerContext> incoming)
        {
            if (await Task.WhenAny(incoming, Task.Delay(5000)) != incoming) throw new TimeoutException("Statistics loopback request missing");
            return await incoming;
        }
        private static async Task Respond(HttpListenerContext context, int status, string json)
        {
            context.Response.StatusCode = status; context.Response.ContentType = "application/json";
            using (var writer = new StreamWriter(context.Response.OutputStream)) await writer.WriteAsync(json);
            context.Response.Close();
        }
        [Test]
        public async Task RevisionAndServerTimeWatermarks_SurviveCatalogProjectionInvalidation()
        {
            long monday = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(8 - (int)DateTime.UtcNow.DayOfWeek).AddSeconds(1)).ToUnixTimeMilliseconds();
            var incoming = _listener.GetContextAsync();
            var load = StatisticsManager.Instance.GetStatistics();
            await Respond(await Receive(incoming), 200, Snapshot(5, 7, serverTime: monday));
            Assert.That(await load, Is.Not.Null);
            incoming = _listener.GetContextAsync();
            var definitions = StatisticsManager.Instance.GetDefinitions();
            await Respond(await Receive(incoming), 200, "{\"definitionVersion\":2,\"definitions\":[{\"key\":\"kills\",\"name\":\"Kills\",\"mode\":\"sum\",\"reset\":\"none\"}]}");
            Assert.That(await definitions, Is.Not.Null);
            Assert.That(StatisticsManager.Instance.CurrentStatistics, Is.Null);
            incoming = _listener.GetContextAsync(); load = StatisticsManager.Instance.GetStatistics();
            await Respond(await Receive(incoming), 200, Snapshot(4, 99, version: 2, serverTime: monday));
            Assert.That(await load, Is.Null, "Catalog changes must preserve the observed revision watermark");
            incoming = _listener.GetContextAsync(); load = StatisticsManager.Instance.GetStatistics();
            await Respond(await Receive(incoming), 200, Snapshot(5, 99, version: 2, serverTime: monday - 2000));
            Assert.That(await load, Is.Null, "A lazy-reset projection cannot move backwards at equal revision");
            incoming = _listener.GetContextAsync(); load = StatisticsManager.Instance.GetStatistics();
            await Respond(await Receive(incoming), 200, Snapshot(5, 8, version: 2, serverTime: monday + 1));
            Assert.That((await load).values[0].value, Is.EqualTo(8));
        }

        [Test]
        public async Task ClearCacheDuringConditionalRead_RejectsOldResponseAndReloadsFullCatalog()
        {
            var incoming = _listener.GetContextAsync();
            var load = StatisticsManager.Instance.GetStatistics();
            await Respond(await Receive(incoming), 200, Snapshot(1, 5));
            Assert.That(await load, Is.Not.Null);
            incoming = _listener.GetContextAsync(); load = StatisticsManager.Instance.GetStatistics();
            var conditional = await Receive(incoming);
            Assert.That(conditional.Request.RawUrl, Does.Contain("definitionVersion=1"));
            StatisticsManager.Instance.ClearCache();
            await Respond(conditional, 200, Snapshot(1, 5, omitDefinitions: true));
            Assert.That(await load, Is.Null);
            Assert.That(StatisticsManager.Instance.CurrentDefinitions, Is.Null);
            incoming = _listener.GetContextAsync(); load = StatisticsManager.Instance.GetStatistics();
            var full = await Receive(incoming);
            Assert.That(full.Request.RawUrl, Does.Not.Contain("definitionVersion"));
            await Respond(full, 200, Snapshot(1, 5));
            Assert.That((await load).definitions.Length, Is.EqualTo(1));
        }

        private static string Snapshot(long revision, long value, string operation = null, bool omitDefinitions = false, long version = 1, long serverTime = 0) =>
            "{\"userId\":\"statistics-user\",\"definitionVersion\":" + version + ",\"revision\":" + revision +
            ",\"values\":[{\"key\":\"kills\",\"value\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"hasValue\":true}]," +
            "\"definitions\":" + (omitDefinitions ? "null" : "[{\"key\":\"kills\",\"name\":\"Kills\",\"mode\":\"sum\",\"reset\":\"none\"}]") +
            ",\"operationWindow\":{\"serverTimeUnixMillis\":" + (serverTime == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : serverTime) + ",\"maxAgeSeconds\":86400,\"maxFutureSkewSeconds\":30}" +
            (operation == null ? "" : ",\"operationId\":\"" + operation + "\"") + ",\"newlyUnlocked\":[]}";
    }
}
