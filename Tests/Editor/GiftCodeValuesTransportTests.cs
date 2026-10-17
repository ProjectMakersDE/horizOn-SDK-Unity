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
        private static GiftCodeIntent NewIntent(string code="VALUES") => new GiftCodeIntent {userId=User,code=code,projectScope=GiftCodeIntent.ProjectFingerprint("fixture-gifts"),campaignId=Intent,expectedCount="9007199254740993",configRevision="9223372036854775807"};
        private static string Response(bool replay=false,long revision=1) => "{\"success\":true,\"message\":\"ok\",\"campaignId\":\""+Intent+"\",\"count\":\"9007199254740994\",\"configRevision\":\"9223372036854775807\",\"giftData\":"+(replay?"null":"\"{\\\"opaque\\\":true}\"")+",\"grantedUnlocks\":"+(replay?"[]":"[\"badge.supporter\"]")+",\"grantedValues\":"+(replay?"[]":"[{\"key\":\"gold\",\"amount\":50}]")+",\"playerValues\":"+Values(revision)+",\"replayed\":"+(replay?"true":"false")+",\"alreadyApplied\":"+(replay?"true":"false")+"}";
        [Test] public async Task PrepareAndTwoDeviceReplayKeepExactStringsAndCurrentBalances()
        {
            GiftCodeIntent[] intents=new GiftCodeIntent[2];
            for(int i=0;i<2;i++)
            {
                var incoming=_listener.GetContextAsync(); var pending=Manager.PrepareRedemption("VALUES"); var c=await Timeout(incoming);
                string body=await new StreamReader(c.Request.InputStream).ReadToEndAsync();
                Assert.That(c.Request.RawUrl,Is.EqualTo("/api/v1/app/gift-codes/validate")); Assert.That(c.Request.Headers["Authorization"],Is.EqualTo("Bearer fixture-session"));
                Assert.That(JsonUtility.FromJson<ValidateGiftCodeRequest>(body).includeIntent,Is.True);
                await Respond(c,200,"{\"valid\":true,\"protocolVersion\":2,\"campaignId\":\""+Intent+"\",\"expectedCount\":\"9007199254740993\",\"configRevision\":\"9223372036854775807\"}");
                intents[i]=await pending; Assert.That(intents[i].expectedCount,Is.EqualTo("9007199254740993"));
            }
            for(int i=0;i<2;i++)
            {
                var incoming=_listener.GetContextAsync(); var pending=Manager.RedeemPrepared(intents[i]); var c=await Timeout(incoming);
                var request=JsonUtility.FromJson<RedeemGiftCodeRequest>(await new StreamReader(c.Request.InputStream).ReadToEndAsync());
                Assert.That(request.expectedCount,Is.EqualTo("9007199254740993")); Assert.That(request.configRevision,Is.EqualTo("9223372036854775807")); Assert.That(request.campaignId,Is.EqualTo(Intent)); Assert.That(request.protocolVersion,Is.EqualTo(2));
                await Respond(c,200,Response(i==1,i+1)); var result=await pending; Assert.That(result,Is.Not.Null);
                Assert.That(result.alreadyApplied,Is.EqualTo(i==1)); Assert.That(result.playerValues.values[0].balance,Is.EqualTo(9007199254740991L));
                if(i==1) { Assert.That(result.giftData,Is.Null); Assert.That(result.grantedValues,Is.Empty); Assert.That(result.grantedUnlocks,Is.Empty); }
            }
        }
        [Test] public async Task LostResponseRetriesExactPersistedTupleEvenAfterManagerRestart()
        {
            typeof(HorizonConfig).GetField("_retryDelaySeconds",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(_config,0f);
            var saved=JsonUtility.ToJson(NewIntent());
            var incoming=_listener.GetContextAsync(); var pending=Manager.RedeemPrepared(JsonUtility.FromJson<GiftCodeIntent>(saved)); var first=await Timeout(incoming);
            string original=await new StreamReader(first.Request.InputStream).ReadToEndAsync();
            incoming=_listener.GetContextAsync(); await Respond(first,503,"{\"code\":\"TEMPORARY\"}"); var retry=await Timeout(incoming);
            Assert.That(await new StreamReader(retry.Request.InputStream).ReadToEndAsync(),Is.EqualTo(original)); await Respond(retry,200,Response(true)); Assert.That((await pending).alreadyApplied,Is.True);
            incoming=_listener.GetContextAsync(); pending=Manager.RedeemPrepared(JsonUtility.FromJson<GiftCodeIntent>(saved)); retry=await Timeout(incoming);
            Assert.That(await new StreamReader(retry.Request.InputStream).ReadToEndAsync(),Is.EqualTo(original)); await Respond(retry,200,Response(true,2)); Assert.That((await pending).giftData,Is.Null);
        }
        [TestCase("COUNT_AHEAD")][TestCase("CAMPAIGN_CHANGED")][TestCase("UPGRADE_REQUIRED")][TestCase("MIGRATION_REQUIRED")][TestCase("COUNTER_EXHAUSTED")]
        public async Task DomainErrorNeverPreparesOrAdvances(string code)
        {
            var incoming=_listener.GetContextAsync(); var pending=Manager.RedeemPrepared(NewIntent()); var c=await Timeout(incoming);
            await Respond(c,409,"{\"code\":\""+code+"\",\"message\":\"protected\"}"); Assert.That(await pending,Is.Null); Assert.That(Manager.LastErrorCode,Is.EqualTo(code));
            var extra=_listener.GetContextAsync(); Assert.That(await Task.WhenAny(extra,Task.Delay(80)),Is.Not.SameAs(extra));
        }
        [TestCase("session")][TestCase("project")][TestCase("signout")][TestCase("cache")][TestCase("host")]
        public async Task InFlightIdentityAndValueCacheChangesAreFenced(string kind)
        {
            var incoming=_listener.GetContextAsync(); var pending=Manager.RedeemPrepared(NewIntent()); var c=await Timeout(incoming);
            if(kind=="session") { NetworkService.Instance.SetSessionToken("other"); NetworkService.Instance.SetSessionToken("fixture-session"); }
            if(kind=="project") { _config.SetApiKey("other"); _config.SetApiKey("fixture-gifts"); }
            if(kind=="host") { NetworkService.Instance.SetActiveHost("http://other.invalid"); NetworkService.Instance.SetActiveHost(Host); }
            if(kind=="signout") UserManager.Instance.SignOut();
            if(kind=="cache") PlayerValuesManager.Instance.ClearCache();
            await Respond(c,200,Response()); Assert.That(await pending,Is.Null); Assert.That(Manager.LastErrorCode,Is.EqualTo("STALE_RESPONSE"));
        }
        [TestCase("9223372036854775808")][TestCase("01")][TestCase("1e2")][TestCase("-1")]
        public async Task InvalidPersistedCountsNeverReachTransport(string value)
        {
            var i=NewIntent(); i.expectedCount=value; var extra=_listener.GetContextAsync();
            Assert.That(await Manager.RedeemPrepared(i),Is.Null); Assert.That(Manager.LastErrorCode,Is.EqualTo("INVALID_REQUEST"));
            Assert.That(await Task.WhenAny(extra,Task.Delay(50)),Is.Not.SameAs(extra));
        }
        [Test] public async Task OriginalCampaignSurvivesVisibleCodeReuseAndForeignScopeIsRejected()
        {
            var i=NewIntent("REUSED"); var incoming=_listener.GetContextAsync(); var pending=Manager.RedeemPrepared(i); var c=await Timeout(incoming);
            Assert.That((await new StreamReader(c.Request.InputStream).ReadToEndAsync()).Contains(Intent),Is.True);
            await Respond(c,200,Response(true)); Assert.That(await pending,Is.Not.Null);
            i.userId="other"; Assert.That(await Manager.RedeemPrepared(i),Is.Null); i.userId=User; i.projectScope="other"; Assert.That(await Manager.RedeemPrepared(i),Is.Null);
        }
        [TestCase("\"count\":9007199254740994")][TestCase("\"count\":\"9223372036854775808\"")][TestCase("\"count\":\"01\"")]
        public async Task MalformedResponseCannotOverwriteValues(string replacement)
        {
            var incoming=_listener.GetContextAsync(); var pending=Manager.RedeemPrepared(NewIntent()); var c=await Timeout(incoming);
            await Respond(c,200,Response().Replace("\"count\":\"9007199254740994\"",replacement)); Assert.That(await pending,Is.Null); Assert.That(PlayerValuesManager.Instance.CurrentState,Is.Null);
        }
        [Test] public async Task ReplayCannotSmuggleHistoricalRewards()
        {
            var incoming=_listener.GetContextAsync(); var pending=Manager.RedeemPrepared(NewIntent()); var c=await Timeout(incoming);
            await Respond(c,200,Response().Replace("\"replayed\":false","\"replayed\":true").Replace("\"alreadyApplied\":false","\"alreadyApplied\":true")); Assert.That(await pending,Is.Null);
        }
        private static void SetUser(string id, string token) => typeof(UserManager).GetField("_currentUser", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(UserManager.Instance, new UserData { UserId = id, AccessToken = token });
        private static async Task<T> Timeout<T>(Task<T> task)
        { if (await Task.WhenAny(task, Task.Delay(5000)) != task) throw new TimeoutException("Gift code loopback request timed out"); return await task; }
        private static async Task Respond(HttpListenerContext context, int status, string body)
        { context.Response.StatusCode = status; context.Response.ContentType = "application/json"; using (var writer = new StreamWriter(context.Response.OutputStream)) await writer.WriteAsync(body); context.Response.Close(); }
    }
}
