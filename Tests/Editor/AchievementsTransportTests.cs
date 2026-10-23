using System;
using System.Collections.Generic;
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
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Service;
using UnityEngine;
namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")]
    public class AchievementsTransportTests
    {
        private const string Host = "http://127.0.0.1:18918", User = "00000000-0000-4000-8000-000000001008";
        private const string Entry = "message:00000000-0000-4000-8000-000000001008";
        private HttpListener _listener;
        private HorizonConfig _config;
        private AchievementsManager Manager => AchievementsManager.Instance;
        private static string Version(char c) => new string(c,64);
        [SetUp] public void Setup()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession"); NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            _config = ScriptableObject.CreateInstance<HorizonConfig>(); _config.SetApiKey("fixture-achievements"); _config.SetHosts(new[] {Host});
            NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost(Host); NetworkService.Instance.SetSessionToken("fixture-session");
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false; SetUser();
            _listener = new HttpListener(); _listener.Prefixes.Add(Host+"/"); _listener.Start();
        }
        [TearDown] public void Teardown()
        {
            _listener?.Close();
            foreach(var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if(new[]{"[UserManager]","[AchievementsManager]","[StatisticsManager]","[InboxManager]","[PlayerValuesManager]","[AchievementsExampleTest]"}.Contains(go.name))UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(_config); PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken"); PlayerPrefs.DeleteKey("task1008_owned_pending_claim");
        }
        private static string Definition(string key="starter",string amount="9007199254740991",bool hidden=false) => "{\"key\":\""+key+"\",\"name\":\"Achievement\",\"description\":\"Visible metadata\",\"imageUrl\":null,\"hidden\":"+(hidden?"true":"false")+",\"active\":true,\"mode\":\"manual\",\"condition\":null,\"rewards\":{\"values\":[{\"key\":\"gold\",\"amount\":"+amount+"}]}}";
        private static string Progress(string key="starter",string state="pending",string amount="3") => "{\"key\":\""+key+"\",\"unlockedAt\":\"2026-10-16T00:00:00Z\",\"rewardState\":\""+state+"\",\"inboxEntryId\":"+(state=="delivered"?"\""+Entry+"\"":"null")+",\"pendingReward\":"+(state=="pending"?"{\"issuedAt\":\"2026-10-16T00:00:00Z\",\"values\":[{\"key\":\"gold\",\"amount\":"+amount+"}]}":"null")+"}";
        private static string Snapshot(long revision=1,char version='a',string definitions=null,string progress="[]",int second=1) => "{\"userId\":\""+User+"\",\"revision\":"+revision+",\"definitionVersion\":\""+Version(version)+"\",\"definitions\":"+(definitions??"["+Definition()+"]")+",\"progress\":"+progress+",\"limits\":{\"maxDefinitions\":20,\"maxLifetimeKeys\":500},\"serverTime\":\"2026-10-16T00:00:"+second.ToString("D2")+"Z\"}";
        private static string Outcome(string key="starter",string state="pending",string code="INBOX_FULL") => "{\"key\":\""+key+"\",\"state\":\""+state+"\",\"inboxEntryId\":"+(state=="delivered"?"\""+Entry+"\"":"null")+",\"code\":"+(code==null?"null":"\""+code+"\"")+"}";
        private static string Complete(string key="starter") => "{\"key\":\""+key+"\",\"unlockedAt\":null,\"rewardState\":\"complete\",\"inboxEntryId\":null,\"pendingReward\":null}";
        private static string UnlockBody(string snapshot,bool replay=false,string newly="[\"starter\"]",string outcomes=null) => "{\"achievements\":"+snapshot+",\"newlyUnlocked\":"+newly+",\"replayed\":"+(replay?"true":"false")+",\"deliveryResults\":["+(outcomes??Outcome())+"]}";
        private async Task<AchievementsResult> Load(string body,string known=null)
        {var incoming=_listener.GetContextAsync();var pending=Manager.GetAchievements(User,known);await Respond(await Timeout(incoming),body);return await pending;}
        private async Task SeedValueCache()
        {
            var incoming=_listener.GetContextAsync();var state=PlayerValuesManager.Instance.GetState(User);
            var values="{\"userId\":\""+User+"\",\"definitionVersion\":1,\"revision\":1,\"day\":\"2026-10-16\",\"values\":[{\"key\":\"gold\",\"balance\":10,\"earnedToday\":0,\"dailyCap\":null}],\"operationWindow\":{\"serverTime\":\"2026-10-16T00:00:00Z\",\"oldestAcceptedAt\":\"2026-09-16T00:00:00Z\"}}";
            await Respond(await Timeout(incoming),values);Assert.That((await state).IsSuccess,Is.True);
            Assert.That(PlayerValuesManager.Instance.CurrentState,Is.Not.Null);
        }
        [Test] public async Task CompactUnlockAndReplayNeedNoInboxClaimAndInvalidatePossiblyCommittedBalances()
        {
            await SeedValueCache();
            for(int i=0;i<2;i++)
            {
                var incoming=_listener.GetContextAsync();var unlock=Manager.Unlock(User,"starter");var request=await Timeout(incoming);
                Assert.That(PlayerValuesManager.Instance.CurrentState,Is.Null,"a lost reply must not leave old balances visible");
                Assert.That(request.Request.RawUrl,Is.EqualTo("/api/v1/app/achievements/unlock"));
                await Respond(request,UnlockBody(Snapshot(2,'b',progress:"["+Complete()+"]",second:2+i),i==1,i==1?"[]":"[\"starter\"]",outcomes:""));
                var result=await unlock;Assert.That(result.IsSuccess,Is.True,result.ErrorCode);
                Assert.That(result.Data.deliveryResults,Is.Empty);Assert.That(result.Data.achievements.progress[0].unlockedAt,Is.Null);
                Assert.That(result.Data.achievements.progress[0].pendingReward,Is.Null);Assert.That(result.Data.replayed,Is.EqualTo(i==1));
            }
            Assert.That(Manager.CurrentAchievements.progress[0].rewardState,Is.EqualTo("complete"));
            var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(80)),Is.Not.SameAs(unexpected));
        }
        [Test] public async Task StatisticReportInvalidatesValueCacheEvenWhenItsCommittedOutcomeCannotBeDecoded()
        {
            await SeedValueCache();
            Assert.That((await Load(Snapshot())).IsSuccess,Is.True);
            var incoming=_listener.GetContextAsync();
            var update=StatisticsManager.Instance.UpdateStatistics(new[]{new StatisticUpdate("kills",1)},"1792180800000:"+Guid.NewGuid());
            var request=await Timeout(incoming);Assert.That(PlayerValuesManager.Instance.CurrentState,Is.Null);
            Assert.That(Manager.CurrentAchievements,Is.Null);
            await Respond(request,"{}");Assert.That(await update,Is.Null);
            Assert.That(PlayerValuesManager.Instance.CurrentState,Is.Null);
        }
        [Test] public async Task ActualExampleShowsAtomicUnlockWithoutDeliveryOrClaimRequest()
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("PM.horizOn.Cloud.Examples.Features.AchievementsExample")).FirstOrDefault(t=>t!=null);
            Assert.That(type,Is.Not.Null);
            var actor=new GameObject("[AchievementsExampleTest]").AddComponent(type);
            var incoming=_listener.GetContextAsync();var action=(Task)type.GetMethod("UnlockManual").Invoke(actor,new object[]{"starter"});
            await Respond(await Timeout(incoming),UnlockBody(Snapshot(progress:"["+Complete()+"]"),outcomes:""));await action;
            Assert.That(Manager.CurrentAchievements.progress[0].rewardState,Is.EqualTo("complete"));
            var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(80)),Is.Not.SameAs(unexpected));
        }
        [Test] public async Task AdminLimitsAbove500AndExistingUnlocksAfterReductionRemainReadable()
        {
            string definitions="["+string.Join(",",Enumerable.Range(0,701).Select(i=>Definition("k"+i)))+"]";
            string progress="["+string.Join(",",Enumerable.Range(0,701).Select(i=>Complete("k"+i)))+"]";
            var raised=Snapshot(definitions:definitions,progress:progress).Replace("\"maxDefinitions\":20,\"maxLifetimeKeys\":500","\"maxDefinitions\":800,\"maxLifetimeKeys\":1000");
            var first=await Load(raised);Assert.That(first.IsSuccess,Is.True,first.ErrorCode);Assert.That(first.Data.progress.Length,Is.EqualTo(701));
            var reduced=await Load(Snapshot(2,'b',definitions,progress,2));Assert.That(reduced.IsSuccess,Is.True,reduced.ErrorCode);
            Assert.That(reduced.Data.progress.Length,Is.EqualTo(701));Assert.That(reduced.Data.limits.maxDefinitions,Is.EqualTo(20));
        }
        [Test] public async Task SnapshotUsesBearerAndPreservesMaximumSafeRewardWithoutLeakingMutableCache()
        {
            var incoming=_listener.GetContextAsync();var pending=Manager.GetAchievements(User);var c=await Timeout(incoming);
            Assert.That(c.Request.RawUrl,Is.EqualTo("/api/v1/app/achievements?userId="+User));Assert.That(c.Request.Headers["Authorization"],Is.EqualTo("Bearer fixture-session"));Assert.That(c.Request.Headers["X-API-Key"],Is.EqualTo("fixture-achievements"));
            await Respond(c,Snapshot());var r=await pending;Assert.That(r.IsSuccess,Is.True);Assert.That(r.Data.definitions[0].rewards.values[0].amount,Is.EqualTo(9007199254740991L));
            r.Data.definitions[0].name="mutated";Assert.That(Manager.CurrentAchievements.definitions[0].name,Is.EqualTo("Achievement"));
        }
        [Test] public async Task ConditionalNullKeepsOnlyMatchingMetadataAndEmptyListReallyClearsIt()
        {
            Assert.That((await Load(Snapshot())).IsSuccess,Is.True);
            var incoming=_listener.GetContextAsync();var pending=Manager.GetAchievements(User);var c=await Timeout(incoming);Assert.That(c.Request.RawUrl,Does.Contain("definitionVersion="+Version('a')));
            await Respond(c,Snapshot(definitions:"null",second:2));var r=await pending;Assert.That(r.Data.definitions.Length,Is.EqualTo(1));
            Assert.That((await Load(Snapshot(version:'b',definitions:"[]",second:3))).Data.definitions,Is.Empty);
            Assert.That((await Load(Snapshot(version:'b',definitions:"null",second:4))).Data.definitions,Is.Empty);
        }
        [TestCase("none")] [TestCase("delivered")]
        public async Task SnapshotAndConditionalCacheKeepNullMetadataAndNonPendingRewards(string state)
        {
            var first = await Load(Snapshot(progress:"["+Progress(state:state)+"]"));
            Assert.That(first.IsSuccess, Is.True);
            AssertOptionalNulls(first.Data, state);
            first.Data.definitions[0].rewards.values[0].amount = 1;
            first.Data.limits.maxDefinitions = 1;
            first.Data.progress[0].rewardState = "changed";
            var cached = Manager.CurrentAchievements;
            AssertOptionalNulls(cached, state);
            Assert.That(cached.definitions[0].rewards.values[0].amount, Is.EqualTo(9007199254740991L));
            Assert.That(cached.limits.maxDefinitions, Is.EqualTo(20));
            Assert.That(cached.progress[0].rewardState, Is.EqualTo(state));
            var conditional = await Load(Snapshot(definitions:"null", progress:"["+Progress(state:state)+"]", second:2));
            Assert.That(conditional.IsSuccess, Is.True);
            AssertOptionalNulls(conditional.Data, state);
            AssertOptionalNulls(Manager.CurrentAchievements, state);
        }
        private static void AssertOptionalNulls(AchievementsSnapshot snapshot, string state)
        {
            Assert.That(snapshot.definitions[0].condition, Is.Null, "manual conditions remain absent");
            Assert.That(snapshot.definitions[0].imageUrl, Is.Null);
            Assert.That(snapshot.progress[0].pendingReward, Is.Null, "only pending rewards have a frozen reward");
            if (state == "delivered") Assert.That(snapshot.progress[0].inboxEntryId, Is.EqualTo(Entry));
            else Assert.That(snapshot.progress[0].inboxEntryId, Is.Null);
        }
        [Test] public async Task RewardlessUnlockKeepsNullOutcomeAndProgressInReturnedWrapper()
        {
            var incoming = _listener.GetContextAsync();
            var pending = Manager.Unlock(User, "starter");
            var definition = Definition().Replace("[{\"key\":\"gold\",\"amount\":9007199254740991}]", "[]");
            await Respond(await Timeout(incoming), UnlockBody(Snapshot(definitions:"["+definition+"]", progress:"["+Progress(state:"none")+"]"), outcomes:Outcome(state:"none", code:null)));
            var result = await pending;
            Assert.That(result.IsSuccess, Is.True);
            AssertOptionalNulls(result.Data.achievements, "none");
            Assert.That(result.Data.deliveryResults[0].code, Is.Null);
            Assert.That(result.Data.deliveryResults[0].inboxEntryId, Is.Null);
            Assert.That(result.Data.achievements.definitions[0].rewards.values, Is.Empty);
        }
        [Test] public async Task UnknownMetadataGetsExactlyOneFullReloadAndNeverTrustsCallerVersionAlone()
        {
            var incoming=_listener.GetContextAsync();var pending=Manager.GetAchievements(User,Version('a'));var c=await Timeout(incoming);Assert.That(c.Request.RawUrl,Does.Not.Contain("definitionVersion"));
            await Respond(c,Snapshot(definitions:"null"));c=await Timeout(_listener.GetContextAsync());Assert.That(c.Request.RawUrl,Does.Not.Contain("definitionVersion"));await Respond(c,Snapshot(second:2));Assert.That((await pending).Data.definitions.Length,Is.EqualTo(1));
        }
        [Test] public async Task RepeatedNullWithoutCacheIsBoundedAndRejected()
        {
            var incoming=_listener.GetContextAsync();var pending=Manager.GetAchievements(User);
            await Respond(await Timeout(incoming),Snapshot(definitions:"null"));await Respond(await Timeout(_listener.GetContextAsync()),Snapshot(definitions:"null"));
            Assert.That((await pending).ErrorCode,Is.EqualTo("INVALID_RESPONSE"));Assert.That(Manager.CurrentAchievements,Is.Null);
            var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(80)),Is.Not.SameAs(unexpected));
        }
        [Test] public async Task OpaqueVersionsAreNotOrderedLexicallyAndUnlockRevealsHiddenMetadata()
        {
            Assert.That((await Load(Snapshot(version:'f'))).IsSuccess,Is.True);
            Assert.That((await Load(Snapshot(version:'a',second:2))).IsSuccess,Is.True,"a later opaque version may sort lower");
            var incoming=_listener.GetContextAsync();var pending=Manager.Unlock(User,"hidden");var c=await Timeout(incoming);string body=await new StreamReader(c.Request.InputStream).ReadToEndAsync();Assert.That(body,Does.Contain("\"key\":\"hidden\""));
            await Respond(c,UnlockBody(Snapshot(2,'b',"["+Definition()+","+Definition("hidden",hidden:true)+"]","["+Progress("hidden")+"]",3),newly:"[\"hidden\"]",outcomes:Outcome("hidden")));
            var r=await pending;Assert.That(r.IsSuccess,Is.True);Assert.That(r.Data.achievements.definitions.Length,Is.EqualTo(2));Assert.That(r.Data.achievements.progress[0].pendingReward.values[0].amount,Is.EqualTo(3));
        }
        [Test] public async Task DuplicateManualUnlockUsesSameKeyAndReturnsNoDuplicateNotification()
        {
            for(int i=0;i<2;i++)
            {
                var incoming=_listener.GetContextAsync();var pending=Manager.Unlock(User,"starter");var c=await Timeout(incoming);Assert.That(c.Request.RawUrl,Is.EqualTo("/api/v1/app/achievements/unlock"));Assert.That(await new StreamReader(c.Request.InputStream).ReadToEndAsync(),Does.Contain("\"key\":\"starter\""));
                await Respond(c,UnlockBody(Snapshot(2,'b',progress:"["+Progress()+"]",second:2+i),i==1,i==1?"[]":"[\"starter\"]"));var r=await pending;Assert.That(r.IsSuccess,Is.True);Assert.That(r.Data.replayed,Is.EqualTo(i==1));Assert.That(r.Data.newlyUnlocked.Length,Is.EqualTo(i==1?0:1));
            }
            Assert.That(PlayerValuesManager.Instance.CurrentState,Is.Null,"unlock creates no local currency");
        }
        [Test] public async Task PartialDeliveryKeepsOriginalPendingAmountsAndInvalidatesInboxWithoutCreditingBalances()
        {
            var inboxIncoming=_listener.GetContextAsync();var inbox=InboxManager.Instance.GetInbox(User);
            await Respond(await Timeout(inboxIncoming),"{\"userId\":\""+User+"\",\"cursor\":\"old\",\"revision\":1,\"broadcastRevision\":1,\"serverTime\":\"2026-10-16T00:00:00Z\",\"pollAfterSeconds\":120,\"unchanged\":false,\"entries\":[],\"counts\":{},\"limits\":{\"targeted\":10,\"broadcasts\":10}}");Assert.That((await inbox).IsSuccess,Is.True);
            var keys=new List<string>{"starter","second"};var incoming=_listener.GetContextAsync();var pending=Manager.DeliverRewards(User,keys);keys[0]="changed";var c=await Timeout(incoming);Assert.That(c.Request.RawUrl,Is.EqualTo("/api/v1/app/achievements/rewards/deliver"));Assert.That(await new StreamReader(c.Request.InputStream).ReadToEndAsync(),Does.Contain("[\"starter\",\"second\"]"));
            var state=Snapshot(3,'b',"["+Definition(amount:"99")+","+Definition("second","99")+"]","["+Progress(state:"delivered")+","+Progress("second")+"]",3);
            await Respond(c,"{\"achievements\":"+state+",\"results\":["+Outcome(state:"delivered",code:null)+","+Outcome("second")+"]}");var r=await pending;
            Assert.That(r.IsSuccess,Is.True);Assert.That(r.Data.results[0].code,Is.Null);Assert.That(r.Data.results[1].inboxEntryId,Is.Null);Assert.That(r.Data.achievements.progress[0].pendingReward,Is.Null);Assert.That(r.Data.achievements.definitions[0].condition,Is.Null);Assert.That(r.Data.results[1].code,Is.EqualTo("INBOX_FULL"));Assert.That(r.Data.achievements.progress[1].pendingReward.values[0].amount,Is.EqualTo(3));Assert.That(r.Data.achievements.definitions[1].rewards.values[0].amount,Is.EqualTo(99));Assert.That(InboxManager.Instance.CurrentInbox,Is.Null);Assert.That(PlayerValuesManager.Instance.CurrentState,Is.Null);
        }
        [TestCase("host")] [TestCase("project")] [TestCase("session")] [TestCase("clear")] [TestCase("signout")]
        public async Task LateRepliesAndCachedMetadataCannotCrossIdentityOrInvalidation(string kind)
        {
            var incoming=_listener.GetContextAsync();var pending=Manager.GetAchievements(User);var c=await Timeout(incoming);
            switch(kind){case "host":NetworkService.Instance.SetActiveHost("http://127.0.0.1:18919");NetworkService.Instance.SetActiveHost(Host);break;case "project":_config.SetApiKey("other");_config.SetApiKey("fixture-achievements");break;case "session":NetworkService.Instance.SetSessionToken("other");NetworkService.Instance.SetSessionToken("fixture-session");break;case "signout":UserManager.Instance.SignOut();SetUser();NetworkService.Instance.SetSessionToken("fixture-session");break;default:Manager.ClearCache();break;}
            await Respond(c,Snapshot());Assert.That((await pending).ErrorCode,Is.EqualTo("STALE_RESPONSE"));Assert.That(Manager.CurrentAchievements,Is.Null);
        }
        [Test] public async Task CacheGetterAlsoDetectsProjectAbaWithoutAnHttpRequest()
        {await Load(Snapshot());_config.SetApiKey("other");_config.SetApiKey("fixture-achievements");Assert.That(Manager.CurrentAchievements,Is.Null);}
        [Test] public async Task WatermarksSurviveCacheClearAndRejectOldProgressOrClock()
        {Assert.That((await Load(Snapshot(10,second:10))).IsSuccess,Is.True);Manager.ClearCache();Assert.That((await Load(Snapshot(9,second:11))).ErrorCode,Is.EqualTo("STALE_RESPONSE"));Assert.That((await Load(Snapshot(11,second:9))).ErrorCode,Is.EqualTo("STALE_RESPONSE"));}
        [Test] public async Task ReadsAndMutationsAreSerializedAndQueuedCommandsKeepTheirOriginalKeys()
        {
            var incoming=_listener.GetContextAsync();var read=Manager.GetAchievements(User);var first=await Timeout(incoming);var keys=new List<string>{"starter"};var delivery=Manager.DeliverRewards(User,keys);keys[0]="changed";var next=_listener.GetContextAsync();Assert.That(await Task.WhenAny(next,Task.Delay(80)),Is.Not.SameAs(next));
            await Respond(first,Snapshot());Assert.That((await read).IsSuccess,Is.True);var c=await Timeout(next);Assert.That(await new StreamReader(c.Request.InputStream).ReadToEndAsync(),Does.Contain("[\"starter\"]"));await Respond(c,"{\"achievements\":"+Snapshot(2,'b',progress:"["+Progress()+"]",second:2)+",\"results\":["+Outcome()+"]}");Assert.That((await delivery).IsSuccess,Is.True);
        }
        [TestCase("9007199254740992")] [TestCase("1e3")] [TestCase("1.0000000000000001")]
        public async Task UnsafeOrFractionalDecodedRewardTokenNeverRoundsIntoTheCache(string number)
        {var body=Snapshot().Replace("\"amount\":9007199254740991","\"am\\u006funt\":"+number);Assert.That((await Load(body)).ErrorCode,Is.EqualTo("INVALID_RESPONSE"));Assert.That(Manager.CurrentAchievements,Is.Null);}
        [Test] public async Task FeatureErrorAndForeignMutationEchoAreVisibleWithoutCacheAdoption()
        {
            var incoming=_listener.GetContextAsync();var pending=Manager.Unlock(User,"starter");await Respond(await Timeout(incoming),"{\"code\":\"ACHIEVEMENT_NOT_MANUAL\",\"message\":\"Statistic only\"}",409);Assert.That((await pending).ErrorCode,Is.EqualTo("ACHIEVEMENT_NOT_MANUAL"));
            incoming=_listener.GetContextAsync();pending=Manager.Unlock(User,"starter");await Respond(await Timeout(incoming),UnlockBody(Snapshot(),outcomes:Outcome("foreign")));Assert.That((await pending).ErrorCode,Is.EqualTo("INVALID_RESPONSE"));Assert.That(Manager.CurrentAchievements,Is.Null);
        }
        [Test] public async Task InvalidInputsNeverCreateRequestsOrReplacementRewardIdentities()
        {
            Assert.That((await Manager.GetAchievements("foreign")).ErrorCode,Is.EqualTo("SESSION_FORBIDDEN"));Assert.That((await Manager.GetAchievements(User,"bad-version")).ErrorCode,Is.EqualTo("INVALID_REQUEST"));Assert.That((await Manager.Unlock(User,"INVALID")).ErrorCode,Is.EqualTo("INVALID_REQUEST"));Assert.That((await Manager.DeliverRewards(User,new[]{"starter","starter"})).ErrorCode,Is.EqualTo("INVALID_REQUEST"));Assert.That((await Manager.DeliverRewards(User,Array.Empty<string>())).ErrorCode,Is.EqualTo("INVALID_REQUEST"));
            var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(80)),Is.Not.SameAs(unexpected));
        }
        [Test] public async Task ActualExampleShowsExplicitUnlockPendingHandoffAndSavedInboxClaimReplay()
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("PM.horizOn.Cloud.Examples.Features.AchievementsExample")).FirstOrDefault(t=>t!=null);
            Assert.That(type,Is.Not.Null,"Import the shipped AchievementsExample.cs into the native QA project");
            var actor=new GameObject("[AchievementsExampleTest]").AddComponent(type);
            Task Call(string method,params object[] args)=>(Task)type.GetMethod(method).Invoke(actor,args);
            var incoming=_listener.GetContextAsync();var action=Call("Refresh");await Respond(await Timeout(incoming),Snapshot());await action;
            incoming=_listener.GetContextAsync();action=Call("UnlockManual","starter");await Respond(await Timeout(incoming),UnlockBody(Snapshot(2,'b',progress:"["+Progress()+"]",second:2)));await action;
            Assert.That(Manager.CurrentAchievements.progress[0].rewardState,Is.EqualTo("pending"));
            incoming=_listener.GetContextAsync();action=Call("RetryPendingRewards",(object)new[]{"starter"});await Respond(await Timeout(incoming),"{\"achievements\":"+Snapshot(3,'c',progress:"["+Progress(state:"delivered")+"]",second:3)+",\"results\":["+Outcome(state:"delivered",code:null)+"]}");await action;
            Assert.That(Manager.CurrentAchievements.progress[0].inboxEntryId,Is.EqualTo(Entry));
            // The game's Inbox selection verifies the authorized reward BEFORE its durable store.
            string entry="{\"id\":\""+Entry+"\",\"kind\":\"message\",\"type\":\"achievement\",\"reference\":\"starter\",\"textKey\":\"achievement.ready\",\"createdAt\":\"2026-10-16T00:00:00Z\",\"expiresAt\":\"2026-11-15T00:00:00Z\",\"overdue\":false,\"read\":false,\"claimed\":false,\"parameters\":{},\"attachment\":{\"values\":[{\"key\":\"gold\",\"amount\":3}]}}";
            string inbox="{\"userId\":\""+User+"\",\"cursor\":\"reward\",\"revision\":1,\"broadcastRevision\":1,\"serverTime\":\"2026-10-16T00:00:03Z\",\"pollAfterSeconds\":120,\"unchanged\":false,\"entries\":["+entry+"],\"counts\":{},\"limits\":{\"targeted\":10,\"broadcasts\":10}}";
            incoming=_listener.GetContextAsync();var read=InboxManager.Instance.GetInbox(User);await Respond(await Timeout(incoming),inbox);Assert.That((await read).IsSuccess,Is.True);
            var selected=InboxManager.Instance.CurrentInbox.entries.Single(e=>e.id==Entry);Assert.That(!selected.claimed&&selected.attachment.values.Length>0,Is.True);
            PlayerPrefs.SetString("task1008_owned_pending_claim",selected.id);PlayerPrefs.Save();
            string values="{\"userId\":\""+User+"\",\"definitionVersion\":1,\"revision\":1,\"day\":\"2026-10-16\",\"values\":[{\"key\":\"gold\",\"balance\":13,\"earnedToday\":3,\"dailyCap\":null}],\"operationWindow\":{\"serverTime\":\"2026-10-16T00:00:04Z\",\"oldestAcceptedAt\":\"2026-09-16T00:00:00Z\"}}";
            for(int i=0;i<2;i++)
            {
                incoming=_listener.GetContextAsync();action=Call("ClaimSavedInboxReward",PlayerPrefs.GetString("task1008_owned_pending_claim"));var c=await Timeout(incoming);Assert.That(await new StreamReader(c.Request.InputStream).ReadToEndAsync(),Does.Contain(Entry));
                await Respond(c,"{\"entryId\":\""+Entry+"\",\"replayed\":"+(i==1?"true":"false")+",\"inbox\":"+inbox.Replace("\"claimed\":false","\"claimed\":true")+",\"playerValues\":"+values+"}");await action;
                Assert.That(InboxManager.Instance.LastErrorCode,Is.Null);Assert.That(PlayerValuesManager.Instance.CurrentState.values[0].balance,Is.EqualTo(13));
            }
            await Call("RefreshAfterStatistics",new StatisticsSnapshot{replayed=true,newlyUnlocked=new[]{"starter"}});
            var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(80)),Is.Not.SameAs(unexpected));
        }
        private static void SetUser()=>typeof(UserManager).GetField("_currentUser",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(UserManager.Instance,new UserData{UserId=User,AccessToken="fixture-session"});
        private static async Task<T> Timeout<T>(Task<T> task){if(await Task.WhenAny(task,Task.Delay(5000))!=task)throw new TimeoutException();return await task;}
        private static async Task Respond(HttpListenerContext c,string body,int status=200){c.Response.StatusCode=status;c.Response.ContentType="application/json";using(var writer=new StreamWriter(c.Response.OutputStream))await writer.WriteAsync(body);c.Response.Close();}
    }
}
