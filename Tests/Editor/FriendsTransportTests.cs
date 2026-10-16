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
    public class FriendsTransportTests
    {
        private const string Host = "http://127.0.0.1:18920", User = "00000000-0000-4000-8000-000000001009";
        private const string Target = "11223344-5566-7788-99aa-bbccddeeff00", TargetCode = "H-ESIzRFVmd4iZqrvM3e7_AA";
        private const string RequestId = "00000000-0000-4000-8000-000000000123";
        private HttpListener _listener;
        private HorizonConfig _config;
        private FriendsManager Manager => FriendsManager.Instance;
        private const string Display = "{\"name\":null,\"profile\":{\"avatarId\":null,\"frameId\":null,\"badges\":[]}}";
        [SetUp] public void Setup()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession"); NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            _config=ScriptableObject.CreateInstance<HorizonConfig>();_config.SetApiKey("fixture-friends");_config.SetHosts(new[]{Host});
            NetworkService.Instance.Initialize(_config);NetworkService.Instance.SetActiveHost(Host);SetUser(User);
            LogService.Instance.EnableUnityLogging=false;LogService.Instance.EnableEventPublishing=false;
            _listener=new HttpListener();_listener.Prefixes.Add(Host+"/");_listener.Start();
        }
        [TearDown] public void Teardown()
        {
            _listener?.Close();
            foreach(var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if(new[]{"[UserManager]","[FriendsManager]","[FriendsExampleTest]"}.Contains(go.name))UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(_config);PlayerPrefs.DeleteKey("horizOn_UserSession");PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }
        private static string Code(string id)
        {string hex=Guid.Parse(id).ToString("N");byte[] bytes=Enumerable.Range(0,16).Select(i=>Convert.ToByte(hex.Substring(i*2,2),16)).ToArray();return "H-"+Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');}
        private static string Friend(string id=Target,string activity="null")=>"{\"userId\":\""+id+"\",\"since\":\"2026-10-16T00:00:00Z\",\"lastActiveAt\":"+activity+",\"display\":"+Display+"}";
        private static string Request(string id=Target)=>"{\"userId\":\""+id+"\",\"requestId\":\""+RequestId+"\",\"createdAt\":\"2026-10-16T00:00:00Z\",\"display\":"+Display+"}";
        private static string Block(string id=Target)=>"{\"userId\":\""+id+"\",\"blockedAt\":\"2026-10-16T00:00:00Z\"}";
        private static string Snapshot(long revision=0,string user=User,string friends="[]",string incoming="[]",string outgoing="[]",string blocked="[]",int second=1)=>"{\"userId\":\""+user+"\",\"revision\":"+revision+",\"friendCode\":\""+Code(user)+"\",\"friends\":"+friends+",\"incoming\":"+incoming+",\"outgoing\":"+outgoing+",\"blocked\":"+blocked+",\"limits\":{\"maxFriends\":20,\"maxIncomingRequests\":20,\"maxOutgoingRequests\":20,\"maxBlocked\":100},\"serverTime\":\"2026-10-16T00:00:"+second.ToString("D2")+"Z\"}";
        private static string Lookup(string id=Target)=>"{\"userId\":\""+id+"\",\"friendCode\":\""+Code(id)+"\",\"display\":"+Display+",\"email\":\"must-never-be-exposed\"}";
        private async Task<FriendsResult> Load(string body)
        {var incoming=_listener.GetContextAsync();var pending=Manager.GetFriends(User);await Respond(await Timeout(incoming),body);return await pending;}
        [Test] public async Task SnapshotUsesBearerKeepsNullablePublicFieldsAndSeparatesAllMutableCopies()
        {
            var incoming=_listener.GetContextAsync();var pending=Manager.GetFriends(User);var c=await Timeout(incoming);
            Assert.That(c.Request.RawUrl,Is.EqualTo("/api/v1/app/friends?userId="+User));Assert.That(c.Request.Headers["Authorization"],Is.EqualTo("Bearer session-"+User));Assert.That(c.Request.Headers["X-API-Key"],Is.EqualTo("fixture-friends"));
            await Respond(c,Snapshot(friends:"["+Friend()+"]",incoming:"["+Request("00000000-0000-4000-8000-000000000456")+"]",blocked:"["+Block("00000000-0000-4000-8000-000000000789")+"]"));var r=await pending;
            Assert.That(r.IsSuccess,Is.True);Assert.That(r.Data.friends[0].lastActiveAt,Is.Null);Assert.That(r.Data.friends[0].display.name,Is.Null);Assert.That(r.Data.friends[0].display.profile.avatarId,Is.Null);Assert.That(r.Data.friends[0].display.profile.frameId,Is.Null);
            Assert.That(typeof(BlockedPlayerView).GetFields().Select(f=>f.Name),Is.EquivalentTo(new[]{"userId","blockedAt"}));
            r.Data.friends[0].display.profile.avatarId="changed";r.Data.incoming[0].requestId="changed";r.Data.blocked[0].userId="changed";r.Data.limits.maxFriends=1;
            var cache=Manager.CurrentFriends;Assert.That(cache.friends[0].display.profile.avatarId,Is.Null);Assert.That(cache.incoming[0].requestId,Is.EqualTo(RequestId));Assert.That(cache.blocked[0].userId,Does.EndWith("789"));Assert.That(cache.limits.maxFriends,Is.EqualTo(20));
        }
        [TestCase(Target)] [TestCase(TargetCode)]
        public async Task ExactUuidAndCanonicalNetworkOrderCodeLookupExposeOnlyPublicProjection(string query)
        {
            var incoming=_listener.GetContextAsync();var pending=Manager.FindPlayer(User,query);var c=await Timeout(incoming);
            Assert.That(c.Request.QueryString["query"],Is.EqualTo(query));Assert.That(c.Request.RawUrl,Does.StartWith("/api/v1/app/friends/find?userId="));
            await Respond(c,Lookup());var r=await pending;Assert.That(r.IsSuccess,Is.True);Assert.That(r.Data.friendCode,Is.EqualTo(TargetCode));Assert.That(r.Data.display.name,Is.Null);Assert.That(r.Data.display.profile.avatarId,Is.Null);Assert.That(typeof(FriendLookup).GetFields().Select(f=>f.Name),Is.EquivalentTo(new[]{"userId","friendCode","display"}));
        }
        [TestCase("request")] [TestCase("accept")] [TestCase("decline")] [TestCase("cancel")] [TestCase("remove")] [TestCase("block")] [TestCase("unblock")]
        public async Task EachMutationSendsOriginalZeroRevisionAndOnlyRequiredRequestIdentity(string action)
        {
            var incoming=_listener.GetContextAsync();var pending=Action(action,0);var c=await Timeout(incoming);string body=await new StreamReader(c.Request.InputStream).ReadToEndAsync();
            Assert.That(c.Request.RawUrl,Is.EqualTo("/api/v1/app/friends/"+action));Assert.That(body,Does.Contain("\"expectedRevision\":0"));Assert.That(body,Does.Contain(Target));
            if(new[]{"accept","decline","cancel"}.Contains(action))Assert.That(body,Does.Contain("\"requestId\":\""+RequestId+"\""));else Assert.That(body,Does.Not.Contain("requestId"));
            await Respond(c,Snapshot(1));Assert.That((await pending).IsSuccess,Is.True);Assert.That(Manager.CurrentFriends.revision,Is.EqualTo(1));
        }
        private Task<FriendsResult> Action(string action,long revision)
        {
            switch(action){case "request":return Manager.SendRequest(User,Target,revision);case "accept":return Manager.AcceptRequest(User,Target,revision,RequestId);case "decline":return Manager.DeclineRequest(User,Target,revision,RequestId);case "cancel":return Manager.CancelRequest(User,Target,revision,RequestId);case "remove":return Manager.RemoveFriend(User,Target,revision);case "block":return Manager.BlockPlayer(User,Target,revision);default:return Manager.UnblockPlayer(User,Target,revision);}
        }
        [TestCase(429)] [TestCase(503)]
        public async Task UncertainMutationHasExactlyOneTransportAttemptAndNeverRefreshesOrSubstitutesRevision(int status)
        {
            var incoming=_listener.GetContextAsync();var pending=Manager.AcceptRequest(User,Target,7,RequestId);var c=await Timeout(incoming);string original=await new StreamReader(c.Request.InputStream).ReadToEndAsync();Assert.That(original,Does.Contain("\"expectedRevision\":7"));Assert.That(original,Does.Contain(RequestId));
            c.Response.Headers["Retry-After"]="1";await Respond(c,"{\"message\":\"uncertain\"}",status);Assert.That((await Timeout(pending)).IsSuccess,Is.False);Assert.That(Manager.CurrentFriends,Is.Null);
            var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(1200)),Is.Not.SameAs(unexpected),"no transport retry or hidden GET");
        }
        [Test] public async Task TwoPlayerRequestAcceptRemoveBlockLookupAndUnblockUseAuthoritativeSnapshots()
        {
            async Task<FriendsResult> Reply(Task<FriendsResult> task,string body){await Respond(await Timeout(_listener.GetContextAsync()),body);return await task;}
            Assert.That((await Reply(Manager.SendRequest(User,Target,0),Snapshot(1,outgoing:"["+Request()+"]"))).IsSuccess,Is.True);
            SetUser(Target);
            Assert.That((await Reply(Manager.GetFriends(Target),Snapshot(1,user:Target,incoming:"["+Request(User)+"]"))).Data.incoming[0].requestId,Is.EqualTo(RequestId));
            Assert.That((await Reply(Manager.AcceptRequest(Target,User,1,RequestId),Snapshot(2,user:Target,friends:"["+Friend(User)+"]",second:2))).Data.friends[0].userId,Is.EqualTo(User));
            SetUser(User);
            Assert.That((await Reply(Manager.GetFriends(User),Snapshot(2,friends:"["+Friend()+"]",second:2))).IsSuccess,Is.True);
            Assert.That((await Reply(Manager.RemoveFriend(User,Target,2),Snapshot(3,second:3))).Data.friends,Is.Empty);
            Assert.That((await Reply(Manager.BlockPlayer(User,Target,3),Snapshot(4,blocked:"["+Block()+"]",second:4))).Data.blocked[0].userId,Is.EqualTo(Target));
            var incoming=_listener.GetContextAsync();var find=Manager.FindPlayer(User,TargetCode);await Respond(await Timeout(incoming),"{\"code\":\"PLAYER_NOT_FOUND\",\"message\":\"Player not found\"}",404);Assert.That((await find).ErrorCode,Is.EqualTo("PLAYER_NOT_FOUND"));
            Assert.That((await Reply(Manager.UnblockPlayer(User,Target,4),Snapshot(5,second:5))).Data.blocked,Is.Empty);
        }
        [TestCase("host")] [TestCase("project")] [TestCase("session")] [TestCase("clear")] [TestCase("signout")]
        public async Task LateResponseCannotCrossAbaIdentityOrCacheInvalidation(string kind)
        {
            var incoming=_listener.GetContextAsync();var pending=Manager.GetFriends(User);var c=await Timeout(incoming);
            switch(kind){case "host":NetworkService.Instance.SetActiveHost(Host+"/other");NetworkService.Instance.SetActiveHost(Host);break;case "project":_config.SetApiKey("other");_config.SetApiKey("fixture-friends");break;case "session":NetworkService.Instance.SetSessionToken("other");NetworkService.Instance.SetSessionToken("session-"+User);break;case "signout":UserManager.Instance.SignOut();SetUser(User);break;default:Manager.ClearCache();break;}
            await Respond(c,Snapshot());Assert.That((await pending).ErrorCode,Is.EqualTo("STALE_RESPONSE"));Assert.That(Manager.CurrentFriends,Is.Null);
        }
        [Test] public async Task CacheWatermarksSurviveClearAndRejectOlderRevisionOrClock()
        {Assert.That((await Load(Snapshot(10,second:10))).IsSuccess,Is.True);Manager.ClearCache();Assert.That((await Load(Snapshot(9,second:11))).ErrorCode,Is.EqualTo("STALE_RESPONSE"));Assert.That((await Load(Snapshot(11,second:9))).ErrorCode,Is.EqualTo("STALE_RESPONSE"));_config.SetApiKey("other");_config.SetApiKey("fixture-friends");Assert.That(Manager.CurrentFriends,Is.Null);}
        [Test] public async Task QueueRetainsOriginalRevisionAndRequestEvenAfterAConcurrentRefresh()
        {
            var incoming=_listener.GetContextAsync();var read=Manager.GetFriends(User);var first=await Timeout(incoming);var mutation=Manager.AcceptRequest(User,Target,3,RequestId);var next=_listener.GetContextAsync();Assert.That(await Task.WhenAny(next,Task.Delay(80)),Is.Not.SameAs(next));
            await Respond(first,Snapshot(4));Assert.That((await read).IsSuccess,Is.True);var c=await Timeout(next);string body=await new StreamReader(c.Request.InputStream).ReadToEndAsync();Assert.That(body,Does.Contain("\"expectedRevision\":3"));Assert.That(body,Does.Contain(RequestId));await Respond(c,"{\"code\":\"REVISION_CONFLICT\",\"message\":\"refresh and reconcile\"}",409);Assert.That((await mutation).ErrorCode,Is.EqualTo("REVISION_CONFLICT"));
        }
        [Test] public async Task DowngradeRetainsAboveTierFriendsAndNanosecondActivityStaysAnOptionalTimestamp()
        {
            string[] rows=Enumerable.Range(1,21).Select(i=>Friend("00000000-0000-4000-8000-"+i.ToString("D12"),i==1?"\"2026-10-16T00:00:00.123456789Z\"":"null")).ToArray();var r=await Load(Snapshot(friends:"["+string.Join(",",rows)+"]"));
            Assert.That(r.IsSuccess,Is.True);Assert.That(r.Data.friends.Length,Is.EqualTo(21));Assert.That(r.Data.limits.maxFriends,Is.EqualTo(20));Assert.That(r.Data.friends[0].lastActiveAt,Is.EqualTo("2026-10-16T00:00:00.123456789Z"));Assert.That(r.Data.friends[1].lastActiveAt,Is.Null);
        }
        [TestCase("9007199254740992")] [TestCase("1e3")] [TestCase("1.0000000000000001")]
        public async Task UnsafeDecodedRevisionLexemeCannotRoundIntoCache(string revision)
        {Assert.That((await Load(Snapshot().Replace("\"revision\":0","\"rev\\u0069sion\":"+revision))).ErrorCode,Is.EqualTo("INVALID_RESPONSE"));Assert.That(Manager.CurrentFriends,Is.Null);}
        [Test] public async Task InvalidLookupMutationAndForeignEchoNeverCreateReplacementIdentity()
        {
            foreach(string query in new[]{"player@example.com","substring"," "+Target,TargetCode.Substring(0,23)+"B"})Assert.That((await Manager.FindPlayer(User,query)).ErrorCode,Is.EqualTo("INVALID_REQUEST"));
            Assert.That((await Manager.SendRequest(User,User,0)).ErrorCode,Is.EqualTo("INVALID_REQUEST"));Assert.That((await Manager.AcceptRequest(User,Target,0,"bad")).ErrorCode,Is.EqualTo("INVALID_REQUEST"));Assert.That((await Manager.BlockPlayer(User,Target,-1)).ErrorCode,Is.EqualTo("INVALID_REQUEST"));Assert.That((await Manager.GetFriends(Target)).ErrorCode,Is.EqualTo("SESSION_FORBIDDEN"));
            var incoming=_listener.GetContextAsync();var find=Manager.FindPlayer(User,Target);await Respond(await Timeout(incoming),Lookup(User));Assert.That((await find).ErrorCode,Is.EqualTo("INVALID_RESPONSE"));
            incoming=_listener.GetContextAsync();var read=Manager.GetFriends(User);await Respond(await Timeout(incoming),Snapshot(user:Target));Assert.That((await read).ErrorCode,Is.EqualTo("INVALID_RESPONSE"));
        }
        [Test] public async Task ActualSampleKeepsSavedOriginalIntentAndOnlyExplicitlyRefreshesAfterConflict()
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("PM.horizOn.Cloud.Examples.Features.FriendsExample")).FirstOrDefault(t=>t!=null);Assert.That(type,Is.Not.Null,"Import shipped FriendsExample.cs");var actor=new GameObject("[FriendsExampleTest]").AddComponent(type);
            Task Call(string name,params object[] args)=>(Task)type.GetMethod(name).Invoke(actor,args);
            var incoming=_listener.GetContextAsync();var action=Call("Refresh");await Respond(await Timeout(incoming),Snapshot(4));await action;
            incoming=_listener.GetContextAsync();action=Call("ApplySavedAction",User,"accept",Target,3L,RequestId);var c=await Timeout(incoming);string body=await new StreamReader(c.Request.InputStream).ReadToEndAsync();Assert.That(body,Does.Contain("\"expectedRevision\":3"));Assert.That(body,Does.Contain(RequestId));await Respond(c,"{\"code\":\"REVISION_CONFLICT\"}",409);await action;
            incoming=_listener.GetContextAsync();action=Call("Refresh");c=await Timeout(incoming);Assert.That(c.Request.HttpMethod,Is.EqualTo("GET"));await Respond(c,Snapshot(5,second:2));await action;
            await Call("ApplySavedAction",Target,"accept",User,5L,RequestId);var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(100)),Is.Not.SameAs(unexpected));
        }
        private static void SetUser(string id){NetworkService.Instance.SetSessionToken("session-"+id);typeof(UserManager).GetField("_currentUser",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(UserManager.Instance,new UserData{UserId=id,AccessToken="session-"+id});}
        private static async Task<T> Timeout<T>(Task<T> task){if(await Task.WhenAny(task,Task.Delay(5000))!=task)throw new TimeoutException();return await task;}
        private static async Task Respond(HttpListenerContext c,string body,int status=200){c.Response.StatusCode=status;c.Response.ContentType="application/json";using(var writer=new StreamWriter(c.Response.OutputStream))await writer.WriteAsync(body);c.Response.Close();}
    }
}
