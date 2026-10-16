using System;
using System.IO;
using System.Net;
using System.Reflection;
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
    [Category("Transport")]
    public class GiftCodeValuesTransportTests
    {
        private const string Host = "http://127.0.0.1:18907", User = "00000000-0000-4000-8000-000000001007";
        private const string Intent = "00000000-0000-4000-8000-000000101007";
        private HttpListener _listener;
        private HorizonConfig _config;
        private GiftCodeManager Manager => GiftCodeManager.Instance;
        [SetUp] public void Setup()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
            NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            _config = ScriptableObject.CreateInstance<HorizonConfig>(); _config.SetApiKey("fixture-gifts"); _config.SetHosts(new[] { Host });
            NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost(Host); NetworkService.Instance.SetSessionToken("fixture-session");
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false;
            SetUser(User, "fixture-session");
            _listener = new HttpListener(); _listener.Prefixes.Add(Host + "/"); _listener.Start();
        }
        [TearDown] public void Teardown()
        {
            _listener?.Close();
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.name == "[UserManager]" || go.name == "[GiftCodeManager]" || go.name == "[PlayerValuesManager]" || go.name == "[PlayerProfileManager]") UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(_config); PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }
        private static string Values(long revision = 1) => "{\"userId\":\"" + User + "\",\"definitionVersion\":1,\"revision\":" + revision + ",\"day\":\"2026-10-16\",\"values\":[{\"key\":\"gold\",\"balance\":9007199254740991,\"earnedToday\":50,\"dailyCap\":null}],\"operationWindow\":{\"serverTime\":\"2026-10-16T00:00:10Z\",\"oldestAcceptedAt\":\"2026-09-16T00:00:00Z\"}}";
        private static string Response(bool replay = false, long revision = 1) => "{\"success\":true,\"message\":\"ok\",\"giftData\":\"{\\\"amount\\\":9007199254740992}\",\"grantedUnlocks\":[\"badge.supporter\"],\"grantedValues\":[{\"key\":\"gold\",\"amount\":50}],\"playerValues\":" + Values(revision) + ",\"replayed\":" + (replay ? "true" : "false") + "}";
        [Test] public async Task StableIntentAndReplayReplaceBalancesWithoutAddingGrantAgain()
        {
            int events = 0; HorizonApp.Events.Subscribe(EventKeys.GiftCodeRedeemed, new Action<RedeemGiftCodeResponse>(_ => events++));
            for (int i = 0; i < 2; i++)
            {
                var incoming = _listener.GetContextAsync(); var pending = Manager.Redeem("VALUES", Intent); var context = await Timeout(incoming);
                var request = JsonUtility.FromJson<RedeemGiftCodeRequest>(await new StreamReader(context.Request.InputStream).ReadToEndAsync());
                Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/gift-codes/redeem"));
                Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer fixture-session")); Assert.That(context.Request.Headers["X-API-Key"], Is.EqualTo("fixture-gifts"));
                Assert.That(request.code, Is.EqualTo("VALUES")); Assert.That(request.userId, Is.EqualTo(User)); Assert.That(request.idempotencyId, Is.EqualTo(Intent));
                await Respond(context, 200, Response(i == 1)); var result = await pending;
                Assert.That(result, Is.Not.Null); Assert.That(result.replayed, Is.EqualTo(i == 1)); Assert.That(result.grantedValues[0].amount, Is.EqualTo(50));
                Assert.That(result.giftData, Is.EqualTo("{\"amount\":9007199254740992}"));
                Assert.That(result.playerValues.values[0].balance, Is.EqualTo(9007199254740991L));
                result.playerValues.values[0].balance = 0;
                Assert.That(PlayerValuesManager.Instance.CurrentState.values[0].balance, Is.EqualTo(9007199254740991L));
            }
            Assert.That(events, Is.EqualTo(2));
        }
        [Test] public async Task LegacyAndNullSnapshotKeepExistingContractAndOmitId()
        {
            foreach (string body in new[] { "{\"success\":true,\"giftData\":\"{}\"}", "{\"success\":true,\"grantedValues\":[],\"playerValues\":null,\"replayed\":false}" })
            {
                var incoming = _listener.GetContextAsync(); var pending = Manager.Redeem("LEGACY"); var context = await Timeout(incoming);
                string request = await new StreamReader(context.Request.InputStream).ReadToEndAsync(); Assert.That(request, Does.Not.Contain("idempotencyId"));
                await Respond(context, 200, body); var result = await pending;
                Assert.That(result, Is.Not.Null); Assert.That(result.grantedValues, Is.Empty); Assert.That(result.grantedUnlocks, Is.Empty); Assert.That(result.playerValues, Is.Null); Assert.That(result.replayed, Is.False);
            }
        }
        [TestCase("1.25")][TestCase("1e2")][TestCase("9007199254740992")][TestCase("-1")][TestCase("0")][TestCase("\"50\"")]
        public async Task InvalidGrantIntegerNeverReachesPlayerValuesCache(string amount)
        {
            var incoming = _listener.GetContextAsync(); var pending = Manager.Redeem("VALUES", Intent);
            await Respond(await Timeout(incoming), 200, Response().Replace("\"amount\":50", "\"amou\\u006et\":" + amount));
            Assert.That(await pending, Is.Null); Assert.That(Manager.LastErrorCode, Is.EqualTo("INVALID_RESPONSE")); Assert.That(PlayerValuesManager.Instance.CurrentState, Is.Null);
        }
        [TestCase("host")][TestCase("session")][TestCase("project")][TestCase("signout")][TestCase("cache")]
        public async Task LateReplyIsRejectedAfterContextChangesIncludingSwitchAwayAndBack(string change)
        {
            int events = 0; HorizonApp.Events.Subscribe(EventKeys.GiftCodeRedeemed, new Action<RedeemGiftCodeResponse>(_ => events++));
            var incoming = _listener.GetContextAsync(); var pending = Manager.Redeem("VALUES", Intent); var context = await Timeout(incoming);
            switch (change)
            {
                case "host": NetworkService.Instance.SetActiveHost("http://other.invalid"); NetworkService.Instance.SetActiveHost(Host); break;
                case "session": NetworkService.Instance.SetSessionToken("other"); NetworkService.Instance.SetSessionToken("fixture-session"); break;
                case "project": _config.SetApiKey("other"); _config.SetApiKey("fixture-gifts"); break;
                case "signout": UserManager.Instance.SignOut(); SetUser(User, "fixture-session"); NetworkService.Instance.SetSessionToken("fixture-session"); break;
                case "cache": PlayerValuesManager.Instance.ClearCache(); break;
            }
            await Respond(context, 200, Response()); Assert.That(await pending, Is.Null); Assert.That(Manager.LastErrorCode, Is.EqualTo("STALE_RESPONSE"));
            Assert.That(PlayerValuesManager.Instance.CurrentState, Is.Null); Assert.That(events, Is.Zero);
        }
        [Test] public async Task OlderSnapshotCannotOverwriteNewerRedemptionOrClearedWatermark()
        {
            var incoming = _listener.GetContextAsync(); var old = Manager.Redeem("OLD", Intent); var first = await Timeout(incoming);
            incoming = _listener.GetContextAsync(); var fresh = Manager.Redeem("NEW", Guid.NewGuid().ToString("D"));
            await Respond(await Timeout(incoming), 200, Response(revision: 2)); Assert.That(await fresh, Is.Not.Null);
            await Respond(first, 200, Response()); Assert.That(await old, Is.Null); Assert.That(PlayerValuesManager.Instance.CurrentState.revision, Is.EqualTo(2));
            PlayerValuesManager.Instance.ClearCache(); incoming = _listener.GetContextAsync(); old = Manager.Redeem("OLD", Intent);
            await Respond(await Timeout(incoming), 200, Response()); Assert.That(await old, Is.Null); Assert.That(PlayerValuesManager.Instance.CurrentState, Is.Null);
        }
        [Test] public async Task SameContextRefreshStaysValidButForeignPlayerSnapshotIsRejected()
        {
            var incoming = _listener.GetContextAsync(); var pending = Manager.Redeem("VALUES", Intent); var context = await Timeout(incoming);
            _config.SetApiKey("fixture-gifts"); NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost(Host); NetworkService.Instance.SetSessionToken("fixture-session");
            await Respond(context, 200, Response()); Assert.That(await pending, Is.Not.Null);
            incoming = _listener.GetContextAsync(); pending = Manager.Redeem("VALUES", Intent);
            await Respond(await Timeout(incoming), 200, Response(revision: 2).Replace(User, "00000000-0000-4000-8000-000000009999"));
            Assert.That(await pending, Is.Null); Assert.That(PlayerValuesManager.Instance.CurrentState.userId, Is.EqualTo(User));
            Assert.That(PlayerValuesManager.Instance.CurrentState.revision, Is.EqualTo(1));
        }
        [Test] public async Task ServerRetryKeepsOriginalBodyAndPermanentFailureIsNotRetried()
        {
            typeof(HorizonConfig).GetField("_retryDelaySeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 0f);
            var incoming = _listener.GetContextAsync(); var pending = Manager.Redeem("VALUES", Intent); var first = await Timeout(incoming);
            string body = await new StreamReader(first.Request.InputStream).ReadToEndAsync(); incoming = _listener.GetContextAsync();
            await Respond(first, 503, "{\"code\":\"TEMPORARY\"}"); var retry = await Timeout(incoming);
            Assert.That(await new StreamReader(retry.Request.InputStream).ReadToEndAsync(), Is.EqualTo(body)); Assert.That(body, Does.Contain(Intent));
            await Respond(retry, 200, Response(true)); Assert.That((await pending).replayed, Is.True);
            incoming = _listener.GetContextAsync(); pending = Manager.Redeem("VALUES", Intent); first = await Timeout(incoming);
            await Respond(first, 409, "{\"code\":\"IDEMPOTENCY_CONFLICT\",\"message\":\"Conflict\"}");
            Assert.That(await pending, Is.Null); Assert.That(Manager.LastErrorCode, Is.EqualTo("IDEMPOTENCY_CONFLICT"));
            var unexpected = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(unexpected, Task.Delay(100)), Is.Not.SameAs(unexpected));
        }
        [Test] public async Task LegacyRedemptionAndSignupNeverRepeatAfterUncertainServerResponse()
        {
            typeof(HorizonConfig).GetField("_retryDelaySeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 0f);
            var incoming = _listener.GetContextAsync(); var pending = Manager.Redeem("LEGACY");
            await Respond(await Timeout(incoming), 503, "{\"code\":\"TEMPORARY\"}"); Assert.That(await pending, Is.Null);
            var next = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(next, Task.Delay(100)), Is.Not.SameAs(next));
            SetUser(null, null); NetworkService.Instance.ClearSessionToken();
            var signup = UserManager.Instance.SignUpAnonymous("fixture-new"); var context = await Timeout(next);
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/user-management/signup"));
            await Respond(context, 503, "{\"code\":\"TEMPORARY\"}"); Assert.That(await signup, Is.False);
            var unexpected = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(unexpected, Task.Delay(100)), Is.Not.SameAs(unexpected));
            Assert.That(UserManager.Instance.HasCachedAnonymousToken(), Is.False);
        }
        [TestCase(null)][TestCase("")][TestCase(" ")]
        public async Task MissingExplicitIdNeverFallsBackToLegacyRedemption(string id)
        {
            var unexpected = _listener.GetContextAsync(); Assert.That(await Manager.Redeem("VALUES", id), Is.Null);
            Assert.That(Manager.LastErrorCode, Is.EqualTo("INVALID_REQUEST"));
            Assert.That(await Task.WhenAny(unexpected, Task.Delay(80)), Is.Not.SameAs(unexpected));
        }
        [Test] public async Task PublicReadRetrySurvivesConcurrentSigninButStillFencesRoutingChanges()
        {
            typeof(HorizonConfig).GetField("_retryDelaySeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 0f);
            var incoming = _listener.GetContextAsync(); var pending = NetworkService.Instance.GetAsync<MessageResponse>("/api/v1/app/news");
            var first = await Timeout(incoming); NetworkService.Instance.SetSessionToken("signed-in-during-public-request");
            incoming = _listener.GetContextAsync(); await Respond(first, 503, "{}"); var second = await Timeout(incoming);
            Assert.That(second.Request.Headers["Authorization"], Is.Null);
            await Respond(second, 200, "{\"success\":true,\"message\":\"public\"}"); Assert.That((await pending).IsSuccess, Is.True);
            incoming = _listener.GetContextAsync(); pending = NetworkService.Instance.GetAsync<MessageResponse>("/api/v1/app/news"); first = await Timeout(incoming);
            NetworkService.Instance.SetActiveHost("http://other.invalid"); NetworkService.Instance.SetActiveHost(Host);
            await Respond(first, 503, "{}"); Assert.That((await pending).ErrorCode, Is.EqualTo("SESSION_CHANGED"));
        }
        [TestCase("")][TestCase(" ")][TestCase(null)]
        public async Task MissingOrInvalidIdentityIsRejectedBeforeTransport(string token)
        {
            NetworkService.Instance.SetSessionToken(token);
            var unexpected = _listener.GetContextAsync(); Assert.That(await Manager.Redeem("VALUES", Intent), Is.Null);
            Assert.That(await Task.WhenAny(unexpected, Task.Delay(80)), Is.Not.SameAs(unexpected));
        }
        private static void SetUser(string id, string token) => typeof(UserManager).GetField("_currentUser", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(UserManager.Instance, new UserData { UserId = id, AccessToken = token });
        private static async Task<T> Timeout<T>(Task<T> task)
        { if (await Task.WhenAny(task, Task.Delay(5000)) != task) throw new TimeoutException("Gift code loopback request timed out"); return await task; }
        private static async Task Respond(HttpListenerContext context, int status, string body)
        { context.Response.StatusCode = status; context.Response.ContentType = "application/json"; using (var writer = new StreamWriter(context.Response.OutputStream)) await writer.WriteAsync(body); context.Response.Close(); }
    }
}
