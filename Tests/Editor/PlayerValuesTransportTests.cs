using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;
namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")]
    public class PlayerValuesTransportTests
    {
        private const string Host = "http://127.0.0.1:18904", User = "00000000-0000-4000-8000-000000001004";
        private HttpListener _listener;
        private HorizonConfig _config;
        private PlayerValuesManager Manager => PlayerValuesManager.Instance;
        [SetUp] public void Setup()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession"); NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            _config = ScriptableObject.CreateInstance<HorizonConfig>(); _config.SetApiKey("fixture-1004"); _config.SetHosts(new[] { Host });
            NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost(Host); NetworkService.Instance.SetSessionToken("fixture-session");
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false; SetUser(User, "fixture-session");
            _listener = new HttpListener(); _listener.Prefixes.Add(Host + "/"); _listener.Start();
        }
        [TearDown] public void Teardown()
        {
            _listener?.Close();
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.name == "[UserManager]" || go.name == "[PlayerValuesManager]") UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(_config); PlayerPrefs.DeleteKey("horizOn_UserSession");
        }
        private static string State(long revision, long definition = 1, int second = 1) => "{\"userId\":\"" + User + "\",\"definitionVersion\":" + definition + ",\"revision\":" + revision + ",\"day\":\"2026-10-15\",\"values\":[{\"key\":\"gold\",\"balance\":9007199254740991,\"earnedToday\":0,\"dailyCap\":null},{\"key\":\"energy\",\"balance\":0,\"earnedToday\":0,\"dailyCap\":0}],\"operationWindow\":{\"serverTime\":\"2026-10-15T12:00:" + second.ToString("D2") + "Z\",\"oldestAcceptedAt\":\"2026-09-15T12:00:00Z\"}}";
        private async Task<PlayerValuesResult> Load(long revision, long definition = 1, int second = 1)
        { var incoming = _listener.GetContextAsync(); var pending = Manager.GetState(User); await Respond(await Timeout(incoming), 200, State(revision, definition, second)); return await pending; }
        [Test] public async Task ReadsSafeInt64NullableCapsAndSynchronizesOperationClock()
        {
            var incoming = _listener.GetContextAsync(); var pending = Manager.GetState(User); var context = await Timeout(incoming);
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/player-values?userId=" + User));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer fixture-session")); Assert.That(context.Request.Headers["X-API-Key"], Is.EqualTo("fixture-1004"));
            await Respond(context, 200, State(5)); var result = await pending;
            Assert.That(result.IsSuccess, Is.True); Assert.That(result.State.values[0].balance, Is.EqualTo(9007199254740991L));
            Assert.That(result.State.values[0].HasDailyCap, Is.False); Assert.That(result.State.values[1].HasDailyCap, Is.True);
            var id = Manager.CreateOperationId().Split(':'); Assert.That(Guid.TryParse(id[1], out _), Is.True);
            Assert.That(Math.Abs(long.Parse(id[0]) - DateTimeOffset.Parse("2026-10-15T12:00:01Z").ToUnixTimeMilliseconds()), Is.LessThan(5000));
            result.State.values[0].balance = 1; Assert.That(Manager.CurrentState.values[0].balance, Is.EqualTo(9007199254740991L));
        }
        [Test] public async Task BusinessErrorCarriesCurrentStateAndDoesNotReplaceExpiredOperation()
        {
            var id = Manager.CreateOperationId(); var incoming = _listener.GetContextAsync(); var pending = Manager.Purchase(User, "energy-pack", id); var context = await Timeout(incoming);
            var body = await new StreamReader(context.Request.InputStream).ReadToEndAsync(); Assert.That(body, Does.Contain(id));
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/player-values/purchase"));
            await Respond(context, 409, "{\"code\":\"OPERATION_EXPIRED\",\"message\":\"Expired\",\"state\":" + State(6) + "}");
            var result = await pending; Assert.That(result.IsSuccess, Is.False); Assert.That(result.ErrorCode, Is.EqualTo("OPERATION_EXPIRED"));
            Assert.That(result.State.revision, Is.EqualTo(6)); Assert.That(Manager.CurrentState.revision, Is.EqualTo(6));
            var unexpected = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(unexpected, Task.Delay(100)), Is.Not.SameAs(unexpected));
        }
        [Test] public async Task SafeIntegerAndSessionChecksRejectLocally()
        {
            var id = Manager.CreateOperationId(); Assert.That((await Manager.Adjust(User, "gold", 9007199254740992L, "test", id)).ErrorCode, Is.EqualTo("INVALID_REQUEST"));
            Assert.That((await Manager.GetState("foreign")).ErrorCode, Is.EqualTo("SESSION_FORBIDDEN")); NetworkService.Instance.SetSessionToken("other");
            Assert.That((await Manager.GetState(User)).ErrorCode, Is.EqualTo("SESSION_REQUIRED"));
        }
        [Test] public async Task AdjustmentSendsExactSignedIntegerAndStableOperationId()
        {
            var id = Manager.CreateOperationId(); var incoming = _listener.GetContextAsync();
            var pending = Manager.Adjust(User, "gold", -9007199254740991L, "debug", id); var context = await Timeout(incoming);
            var body = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/player-values/adjust"));
            Assert.That(body, Does.Contain("\"amount\":-9007199254740991")); Assert.That(body, Does.Contain(id));
            await Respond(context, 200, State(12)); Assert.That((await pending).IsSuccess, Is.True);
        }
        [TestCase("9007199254740991.1")]
        [TestCase("9007199254740992")]
        [TestCase("1e3")]
        public async Task InvalidNumericResponseCannotRoundIntoSafeInteger(string lexeme)
        {
            var incoming = _listener.GetContextAsync(); var pending = Manager.GetState(User);
            await Respond(await Timeout(incoming), 200, State(1).Replace("9007199254740991", lexeme));
            var result = await pending; Assert.That(result.IsSuccess, Is.False); Assert.That(result.ErrorCode, Is.EqualTo("INVALID_RESPONSE"));
            Assert.That(Manager.CurrentState, Is.Null);
        }
        [Test] public async Task AuthEventSubscriptionsUseTheSharedPayloadTypes()
        {
            Action<UserData> other = _ => { };
            EventService.Instance.Subscribe(PM.horizOn.Cloud.Enums.EventKeys.UserSignInSuccess, other);
            Assert.That((await Load(3)).IsSuccess, Is.True);
            EventService.Instance.Publish(PM.horizOn.Cloud.Enums.EventKeys.UserSignInSuccess, UserManager.Instance.CurrentUser);
            EventService.Instance.Publish(PM.horizOn.Cloud.Enums.EventKeys.UserSignOutSuccess, DateTime.UtcNow);
            EventService.Instance.Unsubscribe(PM.horizOn.Cloud.Enums.EventKeys.UserSignInSuccess, other);
        }
        [Test] public void SignedAmountsSerializeWithJsonCultureRegardlessOfDeviceLocale()
        {
            var old = System.Globalization.CultureInfo.CurrentCulture;
            var custom = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
            custom.NumberFormat.NegativeSign = "minus";
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = custom;
                var json = PM.horizOn.Cloud.Helper.JsonHelper.ToJsonExcludeEmpty(new AmountRequest { amount = -9007199254740991L });
                Assert.That(json, Is.EqualTo("{\"amount\":-9007199254740991}"));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = old; }
        }
        private class AmountRequest { public long amount; }
        [Test] public async Task ClearCacheAndSignoutRejectLateReplies()
        {
            var incoming = _listener.GetContextAsync(); var pending = Manager.GetState(User); var context = await Timeout(incoming);
            Manager.ClearCache(); await Respond(context, 200, State(8)); Assert.That((await pending).ErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That(Manager.CurrentState, Is.Null);
            incoming = _listener.GetContextAsync(); pending = Manager.GetState(User); context = await Timeout(incoming); SetUser("other", "other-token"); NetworkService.Instance.SetSessionToken("other-token");
            await Respond(context, 200, State(9)); Assert.That((await pending).State, Is.Null); Assert.That(Manager.CurrentState, Is.Null);
        }
        [Test] public async Task RevisionAndTimeWatermarksSurviveSnapshotAndCatalogueInvalidation()
        {
            Assert.That((await Load(10, 1, 10)).IsSuccess, Is.True); Manager.ClearCache(); Assert.That((await Load(9, 1, 11)).ErrorCode, Is.EqualTo("STALE_RESPONSE"));
            var incoming = _listener.GetContextAsync(); var pending = Manager.GetOffers(User); var context = await Timeout(incoming);
            await Respond(context, 200, "{\"definitionVersion\":2,\"serverTime\":\"2026-10-15T12:00:12Z\",\"definitions\":[],\"offers\":[]}");
            Assert.That((await pending).IsSuccess, Is.True); Assert.That(Manager.CurrentState, Is.Null);
            Assert.That((await Load(9, 2, 13)).ErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That((await Load(11, 2, 11)).ErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That((await Load(11, 2, 14)).IsSuccess, Is.True);
        }
        [Test] public async Task RetryCannotMoveOldMutationIntoNewSession()
        {
            typeof(HorizonConfig).GetField("_retryDelaySeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, .15f);
            var incoming = _listener.GetContextAsync(); var pending = Manager.Purchase(User, "energy", Manager.CreateOperationId()); var context = await Timeout(incoming); await Respond(context, 503, "{}"); await Task.Delay(40);
            SetUser(User, "new-token"); NetworkService.Instance.SetSessionToken("new-token"); Assert.That((await pending).ErrorCode, Is.EqualTo("STALE_RESPONSE"));
            var unexpected = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(unexpected, Task.Delay(200)), Is.Not.SameAs(unexpected));
        }
        private static string ConfiguredBalances(long gold = 100, bool lowered = false) => string.Join(",", Enumerable.Range(0, 80).Select(i =>
            "{\"key\":\"" + (i == 0 ? "gold" : i == 1 ? "energy" : "value_" + i) + "\",\"balance\":" + (i == 0 ? gold : i) + ",\"earnedToday\":" + (i == 0 && lowered ? 50 : 0) + ",\"dailyCap\":" + (i == 0 && lowered ? "0" : "null") + "}"));
        private static string ConfiguredState(long revision, long definition, int second, long gold = 100, bool lowered = false, string operation = null)
        {
            string raw = State(revision, definition, second), field = "\"values\":[";
            int start = raw.IndexOf(field, StringComparison.Ordinal) + field.Length;
            int end = raw.IndexOf("],\"operationWindow\"", start, StringComparison.Ordinal);
            raw = raw.Substring(0, start) + ConfiguredBalances(gold, lowered) + raw.Substring(end);
            return operation == null ? raw : raw.Substring(0, raw.Length - 1) + ",\"operation\":" + operation + "}";
        }
        [Test] public async Task ConfiguredCatalogsAboveTariffDefaultsAndAbsentOfferDayQuotaRemainReadable()
        {
            string rows = string.Join(",", Enumerable.Range(0, 1201).Select(i => "{\"key\":\"offer_" + i + "\",\"name\":\"Configured\",\"active\":true,\"costs\":[{\"key\":\"gold\",\"amount\":1}],\"rewards\":[{\"key\":\"energy\",\"amount\":1}]}"));
            var incoming = _listener.GetContextAsync(); var pending = Manager.GetOffers(User);
            await Respond(await Timeout(incoming), 200, "{\"definitionVersion\":3,\"serverTime\":\"2026-10-15T12:00:30Z\",\"definitions\":[" + ConfiguredBalances() + "],\"offers\":[" + rows + "]}");
            var offered = await pending; Assert.That(offered.IsSuccess, Is.True); Assert.That(offered.Data.definitions.Length, Is.EqualTo(80)); Assert.That(offered.Data.offers.Length, Is.EqualTo(1201));
            Assert.That(offered.Data.offers.All(o => o.purchasesPerDay == -1), Is.True, "Omitted retired quota cannot deserialize as a zero daily limit");
            incoming = _listener.GetContextAsync(); var state = Manager.GetState(User);
            await Respond(await Timeout(incoming), 200, ConfiguredState(2, 3, 31)); Assert.That((await state).State.values.Length, Is.EqualTo(80)); Assert.That(Manager.CurrentState.values.Length, Is.EqualTo(80));
        }
        [Test] public async Task CompactReplayPreservesOriginalIdentityAndReplacesStateWithoutHistoricalDeltas()
        {
            var incoming = _listener.GetContextAsync(); var loaded = Manager.GetState(User);
            await Respond(await Timeout(incoming), 200, ConfiguredState(1, 3, 28)); Assert.That((await loaded).IsSuccess, Is.True);
            string id = Manager.CreateOperationId();
            string fresh = "{\"operationId\":\"" + id + "\",\"replayed\":false,\"applied\":[{\"key\":\"gold\",\"amount\":-1}],\"items\":[{\"instanceId\":\"00000000-0000-4000-8000-000000001011\",\"itemKey\":\"sword\"}]}";
            incoming = _listener.GetContextAsync(); var purchased = Manager.Purchase(User, "offer_0", id); var context = await Timeout(incoming);
            string originalBody = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
            await Respond(context, 200, ConfiguredState(2, 3, 29, 99, false, fresh)); var first = await purchased;
            Assert.That(first.IsSuccess, Is.True); Assert.That(first.State.operation.items.Length, Is.EqualTo(1)); Assert.That(first.State.operation.replayed, Is.False);
            string replay = "{\"operationId\":\"" + id + "\",\"replayed\":true,\"applied\":[],\"items\":[]}";
            incoming = _listener.GetContextAsync(); var retried = Manager.Purchase(User, "offer_0", id); context = await Timeout(incoming);
            Assert.That(await new StreamReader(context.Request.InputStream).ReadToEndAsync(), Is.EqualTo(originalBody));
            await Respond(context, 200, ConfiguredState(3, 4, 30, 33, true, replay)); var result = await retried;
            Assert.That(result.IsSuccess, Is.True); Assert.That(result.State.operation.operationId, Is.EqualTo(id)); Assert.That(result.State.operation.replayed, Is.True);
            Assert.That(result.State.operation.applied, Is.Empty); Assert.That(result.State.operation.items, Is.Empty);
            Assert.That(Manager.CurrentState.values.Length, Is.EqualTo(80)); Assert.That(Manager.CurrentState.values[0].balance, Is.EqualTo(33));
            Assert.That(Manager.CurrentState.values[0].earnedToday, Is.EqualTo(50)); Assert.That(Manager.CurrentState.values[0].dailyCap, Is.Zero);
        }
        private static void SetUser(string id, string token) => typeof(UserManager).GetField("_currentUser", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(UserManager.Instance, new UserData { UserId = id, AccessToken = token });
        private static async Task<T> Timeout<T>(Task<T> task) { if (await Task.WhenAny(task, Task.Delay(5000)) != task) throw new TimeoutException(); return await task; }
        private static async Task Respond(HttpListenerContext context, int status, string body)
        { context.Response.StatusCode = status; context.Response.ContentType = "application/json"; using (var writer = new StreamWriter(context.Response.OutputStream)) await writer.WriteAsync(body); context.Response.Close(); }
    }
}
