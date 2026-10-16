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
    [Category("Transport")] public class GroupsTransportTests
    {
        private const string Host="http://127.0.0.1:18910",User="00000000-0000-4000-8000-000000001010",Peer="11111111-1111-4111-8111-111111111111",Group="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",RequestId="bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
        private const string Time="2026-10-16T00:00:00Z",Data="{\"x.y\":9007199254740993,\"$key\":{\"\":null},\"fraction\":1.0000000000000001}";
        private HttpListener _listener;private HorizonConfig _config;
        private GroupsManager Manager=>GroupsManager.Instance;
        [SetUp] public void Setup(){PlayerPrefs.DeleteKey("horizOn_UserSession");NetworkService.ResetInstance();LogService.ResetInstance();EventService.ResetInstance();_config=ScriptableObject.CreateInstance<HorizonConfig>();_config.SetApiKey("fixture-groups");_config.SetHosts(new[]{Host});NetworkService.Instance.Initialize(_config);NetworkService.Instance.SetActiveHost(Host);SetUser(User);LogService.Instance.EnableUnityLogging=false;LogService.Instance.EnableEventPublishing=false;_listener=new HttpListener();_listener.Prefixes.Add(Host+"/");_listener.Start();}
        [TearDown]public void Stop(){_listener?.Close();foreach(var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))if(new[]{"[GroupsManager]","[UserManager]","[GroupsExampleTest]"}.Contains(go.name))UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(_config);PlayerPrefs.DeleteKey("horizOn_UserSession");PlayerPrefs.DeleteKey("horizOn_AnonymousToken");}
        private static string Limits=>"{\"maxMembers\":20,\"maxGroupsPerPlayer\":1,\"maxInvitations\":20,\"maxJoinRequests\":20,\"maxDataBytes\":2048,\"memberPageSize\":50}";
        private static string Detail(long revision=1,string data=Data,string id=Group)=>"{\"id\":\""+id+"\",\"revision\":"+revision+",\"name\":\"Seagull\",\"description\":\"\",\"joinMode\":\"invite\",\"leaderId\":\""+User+"\",\"memberCount\":21,"+(data==null?"":"\"data\":"+data+",\"myRole\":\"leader\",")+"\"createdAt\":\""+Time+"\",\"updatedAt\":\""+Time+"\"}";
        private static string Mine(long revision=0,string user=User,string groups="[]",string time=Time)=>"{\"userId\":\""+user+"\",\"revision\":"+revision+",\"groups\":"+groups+",\"incomingInvites\":[],\"outgoingRequests\":[],\"limits\":"+Limits+",\"serverTime\":\""+time+"\"}";
        private static string Mutation(long revision=1,string group=null)=>"{\"groups\":"+Mine(revision,groups:group=="null"?"[]":"["+(group??Detail())+"]")+",\"group\":"+(group??Detail())+"}";
        private static string Pending=>"{\"groupId\":\""+Group+"\",\"revision\":1,\"invitations\":[{\"userId\":\""+Peer+"\",\"requestId\":\""+RequestId+"\",\"issuerId\":\""+User+"\",\"createdAt\":\""+Time+"\"}],\"joinRequests\":[],\"serverTime\":\""+Time+"\"}";
        private static string Members(long revision=1,string cursor=null)=>"{\"groupId\":\""+Group+"\",\"revision\":"+revision+",\"members\":[{\"userId\":\""+User+"\",\"role\":\"leader\",\"joinedAt\":\""+Time+"\",\"display\":{\"name\":null,\"profile\":{\"avatarId\":null,\"frameId\":null,\"badges\":[]}}}],\"nextAfterUserId\":"+(cursor==null?"null":"\""+cursor+"\"")+",\"serverTime\":\""+Time+"\"}";
        private async Task<GroupsResult<T>> Reply<T>(Task<GroupsResult<T>>task,string json,int status=200)where T:class{await Respond(await Timeout(_listener.GetContextAsync()),json,status);return await Timeout(task);}
        [Test]public async Task FourReadsUseMatchingBearerAndProjectAndBoundedExplicitPages()
        {
            var own=await Reply(Manager.GetGroups(User),Mine(groups:"["+Detail()+"]"));Assert.That(own.IsSuccess,Is.True);Assert.That(own.Data.groups[0].data.Json,Is.EqualTo(Data));Assert.That(own.Data.groups[0].memberCount,Is.EqualTo(21));own.Data.groups[0].name="changed";Assert.That(Manager.CurrentGroups.groups[0].name,Is.EqualTo("Seagull"));
            Assert.That((await Reply(Manager.GetGroup(User,Group),Detail(data:null))).Data.data,Is.Null);
            var incoming=_listener.GetContextAsync();var page=Manager.GetMembers(User,Group,Peer,7);var c=await Timeout(incoming);Assert.That(c.Request.RawUrl,Is.EqualTo("/api/v1/app/groups/"+Group+"/members?userId="+User+"&limit=7&afterUserId="+Peer));Assert.That(c.Request.Headers["Authorization"],Is.EqualTo("Bearer session-"+User));Assert.That(c.Request.Headers["X-API-Key"],Is.EqualTo("fixture-groups"));await Respond(c,Members());var r=await page;Assert.That(r.Data.members[0].display.name,Is.Null);Assert.That(r.Data.nextAfterUserId,Is.Null);
            var p=await Reply(Manager.GetPending(User,Group),Pending);Assert.That(p.Data.invitations[0].requestId,Is.EqualTo(RequestId));Assert.That(p.Data.invitations[0].issuerId,Is.EqualTo(User));
        }
        [TestCase("create")][TestCase("update")][TestCase("join")][TestCase("request")][TestCase("invite")][TestCase("accept-invite")][TestCase("decline-invite")][TestCase("cancel-invite")][TestCase("accept-request")][TestCase("decline-request")][TestCase("cancel-request")][TestCase("leave")][TestCase("remove")][TestCase("role")][TestCase("transfer")][TestCase("delete")]
        public async Task EveryWritePreservesOriginalZeroRevisionRequiredIdsAndOneWireBody(string action)
        {
            var incoming=_listener.GetContextAsync();var task=Command(action);var c=await Timeout(incoming);var body=await new StreamReader(c.Request.InputStream).ReadToEndAsync();
            Assert.That(c.Request.HttpMethod,Is.EqualTo(action=="update"?"PUT":"POST"));Assert.That(c.Request.RawUrl,Is.EqualTo("/api/v1/app/groups"+(action=="create"?"":"/"+Group+(action=="update"?"":"/"+action))));Assert.That(body,Does.Contain(action=="create"?"\"expectedPlayerRevision\":0":"\"expectedRevision\":0"));Assert.That(body,Does.Not.Contain("operationId"));Assert.That(body,Does.Not.Contain("\"groupId\""));
            if(action=="create"||action=="update"){Assert.That(body,Does.Contain("\"description\":\"\""));Assert.That(body,Does.Contain("\"data\":"+Data));}
            bool targeted=new[]{"invite","cancel-invite","accept-request","decline-request","remove","role","transfer"}.Contains(action),pending=new[]{"accept-invite","decline-invite","cancel-invite","accept-request","decline-request","cancel-request"}.Contains(action);
            Assert.That(body.Contains("targetUserId"),Is.EqualTo(targeted));Assert.That(body.Contains("requestId"),Is.EqualTo(pending));if(pending)Assert.That(body,Does.Contain(RequestId));if(targeted)Assert.That(body,Does.Contain(Peer));
            await Respond(c,Mutation());Assert.That((await task).IsSuccess,Is.True);
        }
        private Task<GroupsResult<GroupMutationView>> Command(string action,long revision=0)
        {switch(action){case"create":return Manager.CreateGroup(User,revision,"Guild","","invite",GroupJsonData.FromJson(Data));case"update":return Manager.UpdateGroup(User,Group,revision,"Guild","","open",GroupJsonData.FromJson(Data));case"join":return Manager.JoinGroup(User,Group,revision);case"request":return Manager.RequestJoin(User,Group,revision);case"invite":return Manager.InvitePlayer(User,Group,revision,Peer);case"accept-invite":return Manager.AcceptInvite(User,Group,revision,RequestId);case"decline-invite":return Manager.DeclineInvite(User,Group,revision,RequestId);case"cancel-invite":return Manager.CancelInvite(User,Group,revision,Peer,RequestId);case"accept-request":return Manager.AcceptRequest(User,Group,revision,Peer,RequestId);case"decline-request":return Manager.DeclineRequest(User,Group,revision,Peer,RequestId);case"cancel-request":return Manager.CancelRequest(User,Group,revision,RequestId);case"leave":return Manager.LeaveGroup(User,Group,revision);case"remove":return Manager.RemoveMember(User,Group,revision,Peer);case"role":return Manager.SetMemberRole(User,Group,revision,Peer,"officer");case"transfer":return Manager.TransferLeadership(User,Group,revision,Peer);default:return Manager.DeleteGroup(User,Group,revision);}}
        [TestCase("create",429)][TestCase("update",503)][TestCase("accept-invite",503)]
        public async Task UncertainWriteNeverRetriesOrSubstitutesARevision(string action,int status)
        {var incoming=_listener.GetContextAsync();var task=Command(action,7);var c=await Timeout(incoming);Assert.That(await new StreamReader(c.Request.InputStream).ReadToEndAsync(),Does.Contain(":7"));c.Response.Headers["Retry-After"]="1";await Respond(c,"{\"message\":\"uncertain\"}",status);Assert.That((await Timeout(task)).IsSuccess,Is.False);var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(1200)),Is.Not.SameAs(unexpected));}
        [TestCase("host")][TestCase("project")][TestCase("session")][TestCase("clear")][TestCase("signout")]
        public async Task LateResponsesCannotCrossAbaIdentityOrExplicitInvalidation(string kind)
        {var incoming=_listener.GetContextAsync();var task=Manager.GetGroups(User);var c=await Timeout(incoming);switch(kind){case"host":NetworkService.Instance.SetActiveHost(Host+"/other");NetworkService.Instance.SetActiveHost(Host);break;case"project":_config.SetApiKey("other");_config.SetApiKey("fixture-groups");break;case"session":NetworkService.Instance.SetSessionToken("other");NetworkService.Instance.SetSessionToken("session-"+User);break;case"signout":UserManager.Instance.SignOut();SetUser(User);break;default:Manager.ClearCache();break;}await Respond(c,Mine());Assert.That((await task).ErrorCode,Is.EqualTo("STALE_RESPONSE"));Assert.That(Manager.CurrentGroups,Is.Null);}
        [Test]public async Task WrongPlayerGroupUnsafeRevisionAndNonadvancingCursorCannotUpdateState()
        {Assert.That((await Reply(Manager.GetGroups(User),Mine(user:Peer))).IsSuccess,Is.False);Assert.That((await Reply(Manager.GetGroup(User,Group),Detail(id:Peer))).IsSuccess,Is.False);Assert.That((await Reply(Manager.GetGroups(User),Mine().Replace("\"revision\":0","\"revisi\\u006fn\":1.0000000000000001"))).IsSuccess,Is.False);Assert.That((await Reply(Manager.GetMembers(User,Group,Peer),Members(cursor:Peer))).IsSuccess,Is.False);}
        [Test]public async Task IndependentWatermarksSurviveClearAndFailedMutationDoesNotAdvanceThemPartially()
        {Assert.That((await Reply(Manager.GetGroups(User),Mine(10,groups:"["+Detail(5)+"]"))).IsSuccess,Is.True);Manager.ClearCache();Assert.That((await Reply(Manager.GetGroups(User),Mine(9))).ErrorCode,Is.EqualTo("STALE_RESPONSE"));Assert.That((await Reply(Manager.GetGroup(User,Group),Detail(4))).ErrorCode,Is.EqualTo("STALE_RESPONSE"));Assert.That((await Reply(Command("join",5),Mutation(12,Detail(8,id:Peer)))).IsSuccess,Is.False);Assert.That((await Reply(Manager.GetGroups(User),Mine(11))).IsSuccess,Is.True);}
        [Test]public async Task InvalidInputsOrUnsignedUserFailBeforeHttp()
        {Assert.That((await Manager.GetMembers(User,Group,null,51)).IsSuccess,Is.False);Assert.That((await Manager.CreateGroup(User,0," ")).IsSuccess,Is.False);Assert.That((await Manager.UpdateGroup(User,Group,0,"A","","open",null)).IsSuccess,Is.False);Assert.That((await Manager.UpdateGroup(User,null,0,"A","","open",GroupJsonData.Empty)).IsSuccess,Is.False);Assert.That((await Manager.AcceptInvite(User,Group,0," "+RequestId)).IsSuccess,Is.False);Assert.That((await Manager.InvitePlayer(User,Group,0,User)).IsSuccess,Is.False);NetworkService.Instance.SetSessionToken("");Assert.That((await Manager.GetGroups(User)).ErrorCode,Is.EqualTo("SESSION_REQUIRED"));var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(100)),Is.Not.SameAs(unexpected));}
        [Test]public async Task ActualSamplePersistsBeforeHttpKeepsOriginalAfterLostResultAndBlocksReplacementAcrossActors()
        {
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.GroupsExample, Assembly-CSharp",true);
            var first=new GameObject("[GroupsExampleTest]").AddComponent(type);var second=new GameObject("[GroupsExampleTest]").AddComponent(type);
            foreach(var actor in new[]{first,second})type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,Peer);
            var path=Path.Combine(Application.persistentDataPath,"groups-"+Peer+"-"+User+".json");Assert.That(File.Exists(path),Is.False);
            try{
                await Reply(Manager.GetGroups(User),Mine());
                var incoming=_listener.GetContextAsync();var create=(Task)type.GetMethod("SaveCreateAfterReview").Invoke(first,new object[]{"Guild","","open",GroupJsonData.FromJson(Data)});var c=await Timeout(incoming);
                Assert.That(File.Exists(path),Is.True,"durable original exists before HTTP");var original=File.ReadAllText(path);var body=await new StreamReader(c.Request.InputStream).ReadToEndAsync();Assert.That(body,Does.Contain("\"expectedPlayerRevision\":0"));await Respond(c,"{\"code\":\"INBOX_FULL\"}",409);await create;
                await Reply(Manager.GetGroups(User),Mine(1));
                Assert.ThrowsAsync<IOException>(async()=>await (Task)type.GetMethod("SaveCreateAfterReview").Invoke(second,new object[]{"Replacement","","open",GroupJsonData.Empty}));
                Assert.That(File.ReadAllText(path),Is.EqualTo(original));
                NetworkService.Instance.SetActiveHost(Host+"/other");NetworkService.Instance.SetActiveHost(Host);
                incoming=_listener.GetContextAsync();var replay=(Task)type.GetMethod("ApplySavedIntent").Invoke(second,null);c=await Timeout(incoming);body=await new StreamReader(c.Request.InputStream).ReadToEndAsync();Assert.That(body,Does.Contain("\"expectedPlayerRevision\":0"));Assert.That(body,Does.Contain(Data));await Respond(c,Mutation());await replay;
                Assert.That(File.Exists(path+".success"),Is.True);Assert.That(File.ReadAllText(path),Is.EqualTo(original));
                type.GetMethod("AcknowledgeSavedSuccess").Invoke(second,null);Assert.That(File.Exists(path),Is.False);Assert.That(File.Exists(path+".success"),Is.False);Assert.That(Directory.GetFiles(Application.persistentDataPath,Path.GetFileName(path)+".*.acknowledged").Length,Is.EqualTo(2));
            }finally{foreach(var file in Directory.GetFiles(Application.persistentDataPath,Path.GetFileName(path)+"*"))File.Delete(file);}
        }
        [Test]public async Task ActualSampleRejectsUnselectedPendingIntentAndCorruptStorageBeforeAnyMutation()
        {
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.GroupsExample, Assembly-CSharp",true);var actor=new GameObject("[GroupsExampleTest]").AddComponent(type);type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,Peer);
            var path=Path.Combine(Application.persistentDataPath,"groups-"+Peer+"-"+User+".json");Assert.That(File.Exists(path),Is.False);
            try{
                await Reply(Manager.GetGroups(User),Mine());var incoming=_listener.GetContextAsync();var review=(Task)type.GetMethod("ReviewGroup").Invoke(actor,new object[]{Group});await Respond(await Timeout(incoming),Detail(data:null));await review;
                await (Task)type.GetMethod("SaveActionAfterReview").Invoke(actor,new object[]{"accept-invite",null,RequestId,null});Assert.That(File.Exists(path),Is.False,"no current matching invitation means no new intent");
                Directory.CreateDirectory(Application.persistentDataPath);File.WriteAllText(path,"{\"revision\":1.0000000000000001}");
                Assert.ThrowsAsync<IOException>(async()=>await (Task)type.GetMethod("ApplySavedIntent").Invoke(actor,null));Assert.That(File.ReadAllText(path),Does.Contain("1.0000000000000001"));
            }finally{foreach(var file in Directory.GetFiles(Application.persistentDataPath,Path.GetFileName(path)+"*"))File.Delete(file);}
        }
        [TestCase("host")][TestCase("project")][TestCase("session")]
        public async Task SampleCannotUseReviewFromEarlierContextAfterAba(string kind)
        {
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.GroupsExample, Assembly-CSharp",true);var actor=new GameObject("[GroupsExampleTest]").AddComponent(type);type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,Peer);
            var incoming=_listener.GetContextAsync();var review=(Task)type.GetMethod("ReviewGroup").Invoke(actor,new object[]{Group});await Respond(await Timeout(incoming),Detail());await review;
            if(kind=="host"){NetworkService.Instance.SetActiveHost(Host+"/other");NetworkService.Instance.SetActiveHost(Host);}else if(kind=="project"){_config.SetApiKey("other");_config.SetApiKey("fixture-groups");}else{NetworkService.Instance.SetSessionToken("other");NetworkService.Instance.SetSessionToken("session-"+User);}
            await(Task)type.GetMethod("SaveMetadataAfterReview").Invoke(actor,new object[]{"Changed","","open",GroupJsonData.Empty});
            Assert.That(File.Exists(Path.Combine(Application.persistentDataPath,"groups-"+Peer+"-"+User+".json")),Is.False);var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(100)),Is.Not.SameAs(unexpected));
        }
        [Test]public async Task EarlierReviewCannotRestoreSelectionWhenTheNewerGroupFails()
        {
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.GroupsExample, Assembly-CSharp",true);var actor=new GameObject("[GroupsExampleTest]").AddComponent(type);type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,Peer);
            var incoming=_listener.GetContextAsync();var first=(Task)type.GetMethod("ReviewGroup").Invoke(actor,new object[]{Group});var c=await Timeout(incoming);var second=(Task)type.GetMethod("ReviewGroup").Invoke(actor,new object[]{Peer});await Respond(c,Detail());await first;await Respond(await Timeout(_listener.GetContextAsync()),"{\"code\":\"GROUP_NOT_FOUND\"}",404);await second;
            await(Task)type.GetMethod("SaveActionAfterReview").Invoke(actor,new object[]{"delete",null,null,null});Assert.That(File.Exists(Path.Combine(Application.persistentDataPath,"groups-"+Peer+"-"+User+".json")),Is.False);var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(100)),Is.Not.SameAs(unexpected));
        }
        [Test]public async Task NewPendingCommandRequiresTheSameGroupVersionAsItsSelectedEntry()
        {
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.GroupsExample, Assembly-CSharp",true);var actor=new GameObject("[GroupsExampleTest]").AddComponent(type);type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,Peer);
            string entry="{\"groupId\":\""+Group+"\",\"requestId\":\""+RequestId+"\",\"issuerId\":\""+Peer+"\",\"createdAt\":\""+Time+"\",\"group\":"+Detail(1,data:null)+"}";
            await Reply(Manager.GetGroups(User),Mine().Replace("\"incomingInvites\":[]","\"incomingInvites\":["+entry+"]"));var incoming=_listener.GetContextAsync();var review=(Task)type.GetMethod("ReviewGroup").Invoke(actor,new object[]{Group});await Respond(await Timeout(incoming),Detail(2,data:null));await review;
            await(Task)type.GetMethod("SaveActionAfterReview").Invoke(actor,new object[]{"accept-invite",null,RequestId,null});Assert.That(File.Exists(Path.Combine(Application.persistentDataPath,"groups-"+Peer+"-"+User+".json")),Is.False);var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(100)),Is.Not.SameAs(unexpected));
        }
        [Test]public async Task ReadableOriginalCannotReplayWithoutDurableWriteAccess()
        {
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.GroupsExample, Assembly-CSharp",true);var actor=new GameObject("[GroupsExampleTest]").AddComponent(type);type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,Peer);var path=Path.Combine(Application.persistentDataPath,"groups-"+Peer+"-"+User+".json");Assert.That(File.Exists(path),Is.False);
            try{await Reply(Manager.GetGroups(User),Mine());var incoming=_listener.GetContextAsync();var create=(Task)type.GetMethod("SaveCreateAfterReview").Invoke(actor,new object[]{"Guild","","open",GroupJsonData.Empty});await Respond(await Timeout(incoming),"{\"code\":\"INBOX_FULL\"}",409);await create;var original=File.ReadAllText(path);
                using(var blocker=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){Assert.That(File.ReadAllText(path),Is.EqualTo(original));Assert.ThrowsAsync<IOException>(async()=>await(Task)type.GetMethod("ApplySavedIntent").Invoke(actor,null));}
                Assert.That(File.ReadAllText(path),Is.EqualTo(original));var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(100)),Is.Not.SameAs(unexpected));
            }finally{foreach(var file in Directory.GetFiles(Application.persistentDataPath,Path.GetFileName(path)+"*"))File.Delete(file);}
        }
        [Test]public async Task ActualTwoPlayerSampleCreatesInvitesAcceptsPromotesTransfersAndLeavesWithDurableAcknowledgements()
        {
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.GroupsExample, Assembly-CSharp",true);var a=new GameObject("[GroupsExampleTest]").AddComponent(type);var b=new GameObject("[GroupsExampleTest]").AddComponent(type);foreach(var actor in new[]{a,b})type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,Peer);
            string Prefix(string user)=>"groups-"+Peer+"-"+user+".json";
            string View(long revision,string role="leader",string leader=User,int count=2)=>Detail(revision).Replace("\"myRole\":\"leader\"","\"myRole\":\""+role+"\"").Replace("\"leaderId\":\""+User+"\"","\"leaderId\":\""+leader+"\"").Replace("\"memberCount\":21","\"memberCount\":"+count);
            string Result(long ownRevision,string user,string view)=>"{\"groups\":"+Mine(ownRevision,user,view=="null"?"[]":"["+view+"]")+",\"group\":"+view+"}";
            async Task Run(Component actor,string method,object[] args,string reply){var incoming=_listener.GetContextAsync();var task=(Task)type.GetMethod(method).Invoke(actor,args);var c=await Timeout(incoming);await Respond(c,reply);await task;}
            void Ack(Component actor)=>type.GetMethod("AcknowledgeSavedSuccess").Invoke(actor,null);
            try{
                await Run(a,"Refresh",null,Mine());await Run(a,"SaveCreateAfterReview",new object[]{"Guild","","invite",GroupJsonData.FromJson(Data)},Result(1,User,View(1,count:1)));Ack(a);
                await Run(a,"SaveActionAfterReview",new object[]{"invite",Peer,null,null},Result(1,User,View(2,count:1)));Ack(a);
                SetUser(Peer);string preview=Detail(2,data:null).Replace("\"memberCount\":21","\"memberCount\":1");string pending="{\"groupId\":\""+Group+"\",\"requestId\":\""+RequestId+"\",\"issuerId\":\""+User+"\",\"createdAt\":\""+Time+"\",\"group\":"+preview+"}";
                await Run(b,"Refresh",null,Mine(user:Peer).Replace("\"incomingInvites\":[]","\"incomingInvites\":["+pending+"]"));await Run(b,"ReviewGroup",new object[]{Group},preview);
                await Run(b,"SaveActionAfterReview",new object[]{"accept-invite",null,RequestId,null},Result(1,Peer,View(3,"member")));Ack(b);
                SetUser(User);await Run(a,"ReviewGroup",new object[]{Group},View(3));
                await Run(a,"SaveActionAfterReview",new object[]{"role",Peer,null,"officer"},Result(1,User,View(4)));Ack(a);
                await Run(a,"SaveActionAfterReview",new object[]{"transfer",Peer,null,null},Result(1,User,View(5,"member",Peer)));Ack(a);
                await Run(a,"SaveActionAfterReview",new object[]{"leave",null,null,null},Result(2,User,"null"));Ack(a);
                Assert.That(Manager.CurrentGroups.groups,Is.Empty);Assert.That(Directory.GetFiles(Application.persistentDataPath,Prefix(User)+".*.acknowledged").Length,Is.EqualTo(10));Assert.That(Directory.GetFiles(Application.persistentDataPath,Prefix(Peer)+".*.acknowledged").Length,Is.EqualTo(2));
            }finally{foreach(var user in new[]{User,Peer})foreach(var file in Directory.GetFiles(Application.persistentDataPath,Prefix(user)+"*"))File.Delete(file);}
        }
        [Test]public async Task RepeatedIdenticalNoopCanBeAcknowledgedWithoutArchiveCollision()
        {
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.GroupsExample, Assembly-CSharp",true);var actor=new GameObject("[GroupsExampleTest]").AddComponent(type);type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,Peer);var path=Path.Combine(Application.persistentDataPath,"groups-"+Peer+"-"+User+".json");Assert.That(File.Exists(path),Is.False);
            try{var incoming=_listener.GetContextAsync();var review=(Task)type.GetMethod("ReviewGroup").Invoke(actor,new object[]{Group});await Respond(await Timeout(incoming),Detail());await review;
                for(int i=0;i<2;i++){incoming=_listener.GetContextAsync();var update=(Task)type.GetMethod("SaveMetadataAfterReview").Invoke(actor,new object[]{"Seagull","","invite",GroupJsonData.FromJson(Data)});await Respond(await Timeout(incoming),Mutation());await update;type.GetMethod("AcknowledgeSavedSuccess").Invoke(actor,null);Assert.That(File.Exists(path),Is.False);}
                Assert.That(Directory.GetFiles(Application.persistentDataPath,Path.GetFileName(path)+".*.acknowledged").Length,Is.EqualTo(4));
            }finally{foreach(var file in Directory.GetFiles(Application.persistentDataPath,Path.GetFileName(path)+"*"))File.Delete(file);}
        }
        [Test]public async Task ReadableSuccessReceiptRequiresDurableWriteAccessBeforeAcknowledgement()
        {
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.GroupsExample, Assembly-CSharp",true);var actor=new GameObject("[GroupsExampleTest]").AddComponent(type);type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,Peer);var path=Path.Combine(Application.persistentDataPath,"groups-"+Peer+"-"+User+".json");Assert.That(File.Exists(path),Is.False);
            try{await Reply(Manager.GetGroups(User),Mine());var incoming=_listener.GetContextAsync();var create=(Task)type.GetMethod("SaveCreateAfterReview").Invoke(actor,new object[]{"Guild","","open",GroupJsonData.Empty});await Respond(await Timeout(incoming),Mutation());await create;
                using(var blocker=new FileStream(path+".success",FileMode.Open,FileAccess.Read,FileShare.Read)){Assert.That(File.ReadAllText(path+".success"),Is.Not.Empty);var error=Assert.Throws<TargetInvocationException>(()=>type.GetMethod("AcknowledgeSavedSuccess").Invoke(actor,null));Assert.That(error.InnerException,Is.InstanceOf<IOException>());}
                Assert.That(File.Exists(path),Is.True);Assert.That(File.Exists(path+".success"),Is.True);type.GetMethod("AcknowledgeSavedSuccess").Invoke(actor,null);Assert.That(File.Exists(path),Is.False);
            }finally{foreach(var file in Directory.GetFiles(Application.persistentDataPath,Path.GetFileName(path)+"*"))File.Delete(file);}
        }
        [Test]public async Task MutationReceiptStaysInOriginalProjectAndCannotAttachReviewToChangedSampleProject()
        {
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.GroupsExample, Assembly-CSharp",true);var actor=new GameObject("[GroupsExampleTest]").AddComponent(type);var projectField=type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic);projectField.SetValue(actor,Peer);var path=Path.Combine(Application.persistentDataPath,"groups-"+Peer+"-"+User+".json");var other=Path.Combine(Application.persistentDataPath,"groups-"+Group+"-"+User+".json");Assert.That(File.Exists(path),Is.False);
            try{await Reply(Manager.GetGroups(User),Mine());var incoming=_listener.GetContextAsync();var create=(Task)type.GetMethod("SaveCreateAfterReview").Invoke(actor,new object[]{"Guild","","open",GroupJsonData.Empty});var c=await Timeout(incoming);projectField.SetValue(actor,Group);await Respond(c,Mutation());await create;
                Assert.That(File.Exists(path+".success"),Is.True);Assert.That(File.Exists(other),Is.False);await(Task)type.GetMethod("SaveMetadataAfterReview").Invoke(actor,new object[]{"Changed","","open",GroupJsonData.Empty});Assert.That(File.Exists(other),Is.False);var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(100)),Is.Not.SameAs(unexpected));
            }finally{foreach(var prefix in new[]{path,other})foreach(var file in Directory.GetFiles(Application.persistentDataPath,Path.GetFileName(prefix)+"*"))File.Delete(file);}
        }
        private static void SetUser(string id){typeof(UserManager).GetField("_currentUser",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(UserManager.Instance,new UserData{UserId=id,AccessToken="session-"+id});NetworkService.Instance.SetSessionToken("session-"+id);}
        private static async Task Respond(HttpListenerContext c,string json,int status=200){c.Response.StatusCode=status;c.Response.ContentType="application/json";byte[] b=System.Text.Encoding.UTF8.GetBytes(json);c.Response.ContentLength64=b.Length;await c.Response.OutputStream.WriteAsync(b,0,b.Length);c.Response.Close();}
        private static async Task<T>Timeout<T>(Task<T>t){if(await Task.WhenAny(t,Task.Delay(8000))!=t)throw new TimeoutException("Groups fixture");return await t;}
    }
}
