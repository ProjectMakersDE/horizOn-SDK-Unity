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
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")] public partial class MatchesTransportTests
    {
        internal const string Host="http://127.0.0.1:18913", User="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", Peer="bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", Id="cccccccc-cccc-4ccc-8ccc-cccccccccccc", RunId="dddddddd-dddd-4ddd-8ddd-dddddddddddd", Time="2026-10-16T00:00:00Z", Project="11111111-1111-4111-8111-111111111111", ApiKey="fixture-matches-1013";
        private HttpListener _listener; private HorizonConfig _config; private int _count;
        private MatchesManager Manager=>MatchesManager.Instance;
        private const string Limits="{\"activeMatches\":2,\"stateBytes\":1024,\"retainedMatches\":20,\"pendingInvites\":20}";
        private static string Ids(params string[] ids)=>"["+string.Join(",",ids.Select(x=>"\""+x+"\""))+"]";
        private static string Summary(long rev=1,string status="lobby",string participants=null,string invites="[]",string forfeits="[]",bool ranked=false)
            =>"{\"id\":\""+Id+"\",\"name\":\"前\\u0000後😀\",\"revision\":"+rev+",\"creatorId\":\""+User+"\",\"capacity\":3,\"visibility\":\"open\",\"strength\":-9007199254740991,\"status\":\""+status+"\",\"participantIds\":"+(participants??Ids(User,Peer))+",\"invitedIds\":"+invites+",\"forfeitedIds\":"+forfeits+",\"currentPlayerId\":"+(status=="active"?"\""+Peer+"\"":"null")+",\"turnDeadline\":"+(status=="active"?"\"2026-10-17T00:00:00Z\"":"null")+",\"createdAt\":\""+Time+"\",\"startedAt\":null,\"finishedAt\":null,\"purgeAt\":null,\"finalStateRevision\":"+(new[]{"reporting","confirmed","disputed","unconfirmed"}.Contains(status)?"5":"null")+",\"hasDisagreement\":"+(status=="disputed"?"true":"false")+",\"ranked\":"+(ranked?"true":"false")+",\"timeoutDue\":false}";
        private static string View(long rev=1,string status="lobby",string bytes="\"AP8A\"",string participants=null,string invites="[]",string reports="[]",bool ranked=false)
        {var s=Summary(rev,status,participants,invites,ranked:ranked);return s.Substring(0,s.Length-1)+",\"turnOrder\":"+(status=="lobby"?"[]":Ids(User,Peer))+",\"turnSeconds\":60,\"timeoutPolicy\":\"forfeit\",\"dataBase64\":"+bytes+",\"reports\":"+reports+",\"reportDeadline\":null,\"absoluteDeadline\":null,\"rankedLeaderboardKey\":"+(ranked?"\"1-ranked-board\"":"null")+",\"ownRankedRunId\":null,\"notificationPending\":true}";}
        private static string Envelope(string view,string fields="",string time=Time)=>"{\"match\":"+view+fields+",\"pollAfterSeconds\":120,\"serverTime\":\""+time+"\"}";
        private static string Ledger(long rev=0,string active="[]",string invites="[]",string recent="[]",string user=User,string time=Time)=>"{\"userId\":\""+user+"\",\"revision\":"+rev+",\"active\":"+active+",\"invites\":"+invites+",\"recent\":"+recent+",\"limits\":"+Limits+",\"pollAfterSeconds\":120,\"serverTime\":\""+time+"\"}";
        private const string Config="{\"limits\":"+Limits+",\"config\":{\"revision\":0,\"rankedLeaderboardKey\":\"1-ranked-board\"},\"capabilities\":{\"matches\":true,\"matchesRanked\":true,\"realtime\":true},\"pollAfterSeconds\":120,\"serverTime\":\""+Time+"\"}";
        private static string Page(string rows="[]",string cursor="null")=>"{\"matches\":"+rows+",\"nextCursor\":"+cursor+",\"pollAfterSeconds\":120,\"serverTime\":\""+Time+"\"}";
        private static string Run=>"{\"runId\":\""+RunId+"\",\"ticket\":\"private-original-ranked-ticket\",\"seed\":2147483646,\"leaderboardKey\":\"1-ranked-board\",\"issuedAt\":\""+Time+"\",\"expiresAt\":\"2026-10-16T02:00:00Z\",\"expiresInSeconds\":7200}";
        private static string Report(string user=User,string bytes="AQ==")=>"{\"userId\":\""+user+"\",\"finalStateRevision\":5,\"resultBase64\":\""+bytes+"\",\"winnerIds\":"+Ids(User)+",\"reportedAt\":\""+Time+"\"}";
        [SetUp] public void Setup()
        {
            _count=0;PlayerPrefs.DeleteKey("horizOn_UserSession");NetworkService.ResetInstance();LogService.ResetInstance();EventService.ResetInstance();
            _config=ScriptableObject.CreateInstance<HorizonConfig>();_config.SetApiKey(ApiKey);_config.SetHosts(new[]{Host});NetworkService.Instance.Initialize(_config);NetworkService.Instance.SetActiveHost(Host);SetUser(User);
            LogService.Instance.EnableUnityLogging=false;LogService.Instance.EnableEventPublishing=false;_listener=new HttpListener();_listener.Prefixes.Add(Host+"/");_listener.Start();CleanupJournals();
        }
        [TearDown] public void Stop()
        {
            Debug.Log("MATCHES_HTTP_COUNT "+_count);_listener?.Close();CleanupJournals();
            foreach(var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))if(new[]{"[MatchesManager]","[UserManager]","[ValidatedActionsManager]","[LeaderboardManager]","[MatchesExampleTest]"}.Contains(go.name))UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(_config);PlayerPrefs.DeleteKey("horizOn_UserSession");PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }
        [Test] public async Task AllFourteenRoutesCarryMatchingSessionAndOriginalPreconditions()
        {
            Assert.That((await Reply(Manager.GetMatches(User),Ledger())).IsSuccess,Is.True);
            Assert.That((await Reply(Manager.GetLimits(User),Config)).IsSuccess,Is.True);
            Assert.That((await Reply(Manager.FindOpenMatches(User,-9007199254740991,9007199254740991,1),Page("["+Summary()+"]"))).IsSuccess,Is.True);
            var read=await Reply(Manager.GetMatch(User,Id),Envelope(View()));Assert.That(read.IsSuccess,Is.True);Assert.That(read.Data.match.name,Is.EqualTo("前\0後😀"));Assert.That(read.Data.match.data,Is.EqualTo(new byte[]{0,255,0}));
            var input=new MatchCreateInput{name="前\0後😀",capacity=3,strength=-9007199254740991,turnSeconds=60,timeoutPolicy="forfeit",data=new byte[]{0,255,0},inviteeIds=new[]{Peer}};
            var create=Manager.CreateMatch(User,0,input);input.data[1]=0;var c=await Incoming();Assert.That(c.Request.Headers["If-Match"],Is.EqualTo("0"));Assert.That(await Body(c),Does.Contain("AP8A").And.Contain("-9007199254740991").And.Contain("\"ranked\":false"));await Respond(c,Envelope(View(participants:Ids(User),invites:Ids(Peer)),",\"playerRevision\":1"));Assert.That((await create).IsSuccess,Is.True);
            foreach(var action in new[]{"join","decline","start","turn","timeout","leave","cancel"})
            {
                var call=Action(action,4294967296L);c=await Incoming();Assert.That(c.Request.Url.AbsolutePath,Is.EqualTo("/api/v1/app/matches/"+Id+"/"+action));Assert.That(c.Request.Headers["If-Match"],Is.EqualTo("4294967296"));string body=await Body(c);if(action=="turn")Assert.That(body,Does.Contain("\"finish\":false").And.Contain("AP8A"));
                await Respond(c,Envelope(View(4294967297L,action=="start"?"active":"lobby")));Assert.That((await call).IsSuccess,Is.True,action);
            }
            // Report final revision belongs to the game state, not the latest administrative/gameplay revision.
            var report=Manager.ReportResult(User,Id,5,new byte[]{1},new[]{User});c=await Incoming();Assert.That(c.Request.Headers["If-Match"],Is.Null);Assert.That(await Body(c),Does.Contain("\"finalStateRevision\":5").And.Contain("AQ=="));await Respond(c,Envelope(View(4294967298L,"confirmed",reports:"["+Report()+","+Report(Peer)+"]"),",\"replayed\":true"));Assert.That((await report).Data.replayed,Is.True);
            var run=Manager.StartRankedRun(User,Id,5);c=await Incoming();Assert.That(c.Request.Url.AbsolutePath,Does.EndWith("/ranked-run"));Assert.That(c.Request.Headers["If-Match"],Is.Null);await Respond(c,Run);var started=await run;Assert.That(started.IsSuccess,Is.True);Assert.That(started.Data.seed,Is.EqualTo(2147483646));started.Data.ticket="mutated";Assert.That(ValidatedActionsManager.Instance.CurrentRun.ticket,Is.EqualTo("private-original-ranked-ticket"));
            Assert.That(Manager.GetCurrentRankedRun(User,Id,5).runId,Is.EqualTo(RunId));Assert.That((await Manager.StartRankedRun(User,Id,5)).ErrorCode,Is.EqualTo("PENDING_RUN"));
        }
        private Task<MatchesResult<MatchRead>> Action(string action,long rev)=>action=="join"?Manager.JoinMatch(User,Id,rev):action=="decline"?Manager.DeclineInvite(User,Id,rev):action=="start"?Manager.StartMatch(User,Id,rev,new[]{User,Peer}):action=="turn"?Manager.SubmitTurn(User,Id,rev,new byte[]{0,255,0}):action=="timeout"?Manager.AdvanceTimeout(User,Id,rev):action=="leave"?Manager.LeaveMatch(User,Id,rev):Manager.CancelMatch(User,Id,rev);
        [TestCase(429)][TestCase(503)] public async Task EveryWriteIsOnceEvenForGlobal429AndUncertain503(int status)
        {
            foreach(var action in new[]{"join","decline","start","turn","timeout","leave","cancel"}){var call=Action(action,1);await Respond(await Incoming(),"{\"message\":\"unknown\"}",status);Assert.That((await Bounded(call)).IsSuccess,Is.False);}
            await Reply(Manager.CreateMatch(User,0,new MatchCreateInput{name="x"}),"{\"message\":\"unknown\"}",status);
            await Reply(Manager.ReportResult(User,Id,5,new byte[]{1},new[]{User}),"{\"message\":\"unknown\"}",status);
            await Reply(Manager.StartRankedRun(User,Id,5),"{\"message\":\"unknown\"}",status);
            var next=_listener.GetContextAsync();Assert.That(await Task.WhenAny(next,Task.Delay(100)),Is.Not.SameAs(next));Assert.That(_count,Is.EqualTo(10));
        }
        [Test] public async Task CadenceAndGlobalMatchFencesSurviveCacheClearAndSummaryReads()
        {
            var read=await Reply(Manager.GetMatch(User,Id),Envelope(View(7)));read.Data.match.data[0]=1;Assert.That(Manager.GetCached<MatchRead>(read.Identity).match.data[0],Is.EqualTo(0));
            Assert.That((await Manager.GetMatch(User,Id)).ErrorCode,Is.EqualTo("REFRESH_TOO_SOON"));Manager.ClearCache();Assert.That((await Manager.GetMatch(User,Id)).ErrorCode,Is.EqualTo("REFRESH_TOO_SOON"));
            Assert.That((await Reply(Manager.GetMatch(User,Id,true),Envelope(View(6)))).ErrorCode,Is.EqualTo("STALE_RESPONSE"));
            Assert.That((await Reply(Manager.GetMatches(User,true),Ledger(1,"["+Summary(8)+"]"))).IsSuccess,Is.True);
            Assert.That((await Reply(Manager.GetMatch(User,Id,true),Envelope(View(7)))).ErrorCode,Is.EqualTo("STALE_RESPONSE"));
            Assert.That((await Reply(Manager.GetMatch(User,Id,true),Envelope(View(8),time:"2026-10-15T23:59:59Z"))).ErrorCode,Is.EqualTo("STALE_RESPONSE"));
            Assert.That((await Reply(Manager.FindOpenMatches(User,0,1,cursor:"next"),Page(cursor:"\"further\""))).IsSuccess,Is.True,"an empty candidate page may have another cursor");
        }
        [TestCase("host")][TestCase("key")][TestCase("session")][TestCase("clear")]
        public async Task DelayedIdentityAbaAndCacheEpochNeverRestorePrivateState(string change)
        {
            var task=Manager.GetMatch(User,Id);var c=await Incoming();
            if(change=="host"){NetworkService.Instance.SetActiveHost("http://other");NetworkService.Instance.SetActiveHost(Host);}if(change=="key"){_config.SetApiKey("other");_config.SetApiKey(ApiKey);}if(change=="session"){NetworkService.Instance.SetSessionToken("other");NetworkService.Instance.SetSessionToken("session-"+User);}if(change=="clear")Manager.ClearCache();
            await Respond(c,Envelope(View()));Assert.That((await task).ErrorCode,Is.EqualTo("STALE_RESPONSE"));
        }
        [Test] public async Task ReadsSerializeAndQueuedTurnFreezesBinaryBeforeAwait()
        {
            var read=Manager.GetMatches(User);var c=await Incoming();byte[]data={0,255,0};var turn=Manager.SubmitTurn(User,Id,1,data);data[1]=0;var second=_listener.GetContextAsync();Assert.That(await Task.WhenAny(second,Task.Delay(100)),Is.Not.SameAs(second));
            await Respond(c,Ledger());Assert.That((await read).IsSuccess,Is.True);c=await Bounded(second);Assert.That(await Body(c),Does.Contain("AP8A"));await Respond(c,Envelope(View(2)));Assert.That((await turn).IsSuccess,Is.True);
        }
        [Test] public async Task MetadataRemovalAndOngoingForfeitedRecentRemainValid()
        {
            SetUser(Peer);var invite=await Reply(Manager.GetMatch(Peer,Id),Envelope(View(bytes:"null",participants:Ids(User),invites:Ids(Peer))));Assert.That(invite.IsSuccess,Is.True);Assert.That(invite.Data.match.data,Is.Null);
            var decline=await Reply(Manager.DeclineInvite(Peer,Id,1),Envelope(View(2,bytes:"null",participants:Ids(User))));Assert.That(decline.IsSuccess,Is.True);
            Assert.That((await Reply(Manager.GetMatch(Peer,Id,true),"{\"code\":\"MATCH_NOT_FOUND\"}",404)).ErrorCode,Is.EqualTo("MATCH_NOT_FOUND"));Assert.That(Manager.GetCached<MatchRead>(invite.Identity),Is.Null);
            SetUser(User);var mine=await Reply(Manager.GetMatches(User),Ledger(3,recent:"["+Summary(4,"active",forfeits:Ids(User))+"]"));Assert.That(mine.IsSuccess,Is.True);Assert.That(mine.Data.recent[0].status,Is.EqualTo("active"));
        }
        [Test] public async Task CommonRunLeaseBlocksGenericAndMatchStartsIncludingReentrantCallbackAndDiscard()
        {
            var pending=ValidatedActionsManager.Instance.StartRun();var c=await Incoming();Assert.That((await Manager.StartRankedRun(User,Id,5)).ErrorCode,Is.EqualTo("RUN_OPERATION_PENDING"));await Respond(c,Run);Assert.That(await pending,Is.Not.Null);ValidatedActionsManager.Instance.DiscardRun();
            var match=Manager.StartRankedRun(User,Id,5);c=await Incoming();Assert.That(await ValidatedActionsManager.Instance.StartRun(),Is.Null);Assert.That(ValidatedActionsManager.Instance.LastErrorCode,Is.EqualTo("RUN_OPERATION_PENDING"));ValidatedActionsManager.Instance.DiscardRun();Assert.That(await ValidatedActionsManager.Instance.StartRun(),Is.Null);await Respond(c,Run);Assert.That((await match).ErrorCode,Is.EqualTo("STALE_RESPONSE"));Assert.That(ValidatedActionsManager.Instance.CurrentRun,Is.Null);
            Task<MatchesResult<ValidatedRun>> reentrant=null;HorizonApp.Events.ClearEvent(EventKeys.ValidatedRunStarted);HorizonApp.Events.Subscribe<ValidatedRun>(EventKeys.ValidatedRunStarted,r=>{r.ticket="callback-mutated";reentrant=Manager.StartRankedRun(User,Id,5);});
            Assert.That((await Reply(Manager.StartRankedRun(User,Id,5),Run)).IsSuccess,Is.True);Assert.That((await reentrant).ErrorCode,Is.EqualTo("RUN_OPERATION_PENDING"));Assert.That(ValidatedActionsManager.Instance.CurrentRun.ticket,Is.EqualTo("private-original-ranked-ticket"));
        }
        [TestCase("1e0")][TestCase("1.0000000000000001")][TestCase("9007199254740992")]
        public async Task InvalidLexemesAndCanonicalBytesFailAsPrivateInvalidResponse(string number)
        {Assert.That((await Reply(Manager.GetMatch(User,Id),Envelope(View()).Replace("\"revision\":1","\"rev\\u0069sion\":"+number))).ErrorCode,Is.EqualTo("INVALID_RESPONSE"));}
        [Test] public async Task NoPrivatePayloadTicketOrSessionLeaksToSdkLogs()
        {
            var logs=new List<string>();LogService.Instance.EnableEventPublishing=true;HorizonApp.Events.Subscribe<LogEventData>(EventKeys.ErrorOccurred,x=>logs.Add(x.Message));
            Assert.That((await Reply(Manager.GetMatch(User,Id),Envelope(View()))).IsSuccess,Is.True);Assert.That((await Reply(Manager.StartRankedRun(User,Id,5),Run)).IsSuccess,Is.True);
            Assert.That(logs.Any(x=>x.Contains("AP8A")||x.Contains("前")||x.Contains(ApiKey)||x.Contains("private-original-ranked-ticket")||x.Contains("session-"+User)),Is.False,string.Join("\n",logs));
        }
        private async Task<HttpListenerContext> Incoming(){var c=await Bounded(_listener.GetContextAsync());Assert.That(c.Request.Headers["X-API-Key"],Is.EqualTo(ApiKey));Assert.That(c.Request.Headers["Authorization"],Is.EqualTo("Bearer session-"+UserManager.Instance.CurrentUser.UserId));return c;}
        private async Task<MatchesResult<T>> Reply<T>(Task<MatchesResult<T>> call,string json,int status=200)where T:class{await Respond(await Incoming(),json,status);return await Bounded(call);}
        private async Task Respond(HttpListenerContext c,string json,int status=200){_count++;c.Response.StatusCode=status;c.Response.ContentType="application/json";byte[]bytes=Encoding.UTF8.GetBytes(json);c.Response.ContentLength64=bytes.Length;await c.Response.OutputStream.WriteAsync(bytes,0,bytes.Length);c.Response.Close();}
        private static Task<string> Body(HttpListenerContext c)=>new StreamReader(c.Request.InputStream).ReadToEndAsync();
        private static async Task<T> Bounded<T>(Task<T> task){Assert.That(await Task.WhenAny(task,Task.Delay(10000)),Is.SameAs(task),"bounded fixture wait");return await task;}
        private static void SetUser(string id){var user=new UserData{UserId=id,AccessToken="session-"+id};typeof(UserManager).GetField("_currentUser",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(UserManager.Instance,user);NetworkService.Instance.SetSessionToken(user.AccessToken);}
    }
}
