using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;
namespace PM.horizOn.Cloud.Tests
{
    public partial class FriendsTransportTests
    {
        static string ConfigId(int i)=>"10000000-0000-4000-8000-"+i.ToString("D12");
        [Test] public async Task ConfiguredRelationshipsAndUnknownDatesSurviveLoweringWithoutRebasingOriginalRequest()
        {
            string Stamp(string row,string key,int n)=>n%3==0?row:n%3==1?row.Replace("\""+key+"\":\"2026-10-16T00:00:00Z\"","\""+key+"\":null"):row.Replace(",\""+key+"\":\"2026-10-16T00:00:00Z\"","");
            string rows="["+string.Join(",",Enumerable.Range(0,201).Select(i=>Stamp(Friend(ConfigId(i)),"since",i)))+"]";
            string requests="["+string.Join(",",Enumerable.Range(0,21).Select(i=>Stamp(Request(ConfigId(1000+i)).Replace(RequestId,ConfigId(2000+i)),"createdAt",i)))+"]";
            string outgoing="["+string.Join(",",Enumerable.Range(0,21).Select(i=>Stamp(Request(ConfigId(5000+i)).Replace(RequestId,ConfigId(6000+i)),"createdAt",i)))+"]";
            string blocks="["+string.Join(",",Enumerable.Range(0,101).Select(i=>Block(ConfigId(3000+i))))+"]";
            const string old="\"maxFriends\":20,\"maxIncomingRequests\":20,\"maxOutgoingRequests\":20,\"maxBlocked\":100";
            string high=Snapshot(1,friends:rows,incoming:requests,outgoing:outgoing,blocked:blocks).Replace(old,"\"maxFriends\":300,\"maxIncomingRequests\":22,\"maxOutgoingRequests\":22,\"maxBlocked\":150");
            var first=await Load(high);Assert.That(first.IsSuccess,Is.True);Assert.That(first.Data.friends.Length,Is.EqualTo(201));Assert.That(first.Data.friends[0].since,Is.Not.Null);Assert.That(first.Data.friends[1].since,Is.Null);Assert.That(first.Data.friends[2].since,Is.Null);Assert.That(first.Data.incoming[2].createdAt,Is.Null);
            var original=first.Data.outgoing[0];string low=Snapshot(2,friends:rows,incoming:requests,outgoing:outgoing,blocked:blocks).Replace(old,"\"maxFriends\":1,\"maxIncomingRequests\":1,\"maxOutgoingRequests\":1,\"maxBlocked\":1");
            var lowered=await Load(low);Assert.That(lowered.IsSuccess,Is.True);Assert.That(lowered.Data.blocked.Length,Is.EqualTo(101));Assert.That(lowered.Data.outgoing.Length,Is.EqualTo(21));
            var incoming=_listener.GetContextAsync();var rejected=Manager.CancelRequest(User,original.userId,1,original.requestId);var c=await Timeout(incoming);string body=await new StreamReader(c.Request.InputStream).ReadToEndAsync();Assert.That(body,Does.Contain("\"expectedRevision\":1").And.Contain(original.requestId));await Respond(c,"{\"code\":\"REVISION_CONFLICT\"}",409);Assert.That((await rejected).ErrorCode,Is.EqualTo("REVISION_CONFLICT"));
        }
    }
    public partial class GroupsTransportTests
    {
        [Test]public async Task ConfiguredDefaultMemberPageRestartsAfterSizeChangeAndRequiresCurrentAuthLimits()
        {
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.GroupsExample, Assembly-CSharp",true);var actor=new GameObject("[GroupsExampleTest]").AddComponent(type);type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,Peer);
            var incoming=_listener.GetContextAsync();var task=(Task)type.GetMethod("ReviewGroup").Invoke(actor,new object[]{Group});await Respond(await Timeout(incoming),Detail());await Timeout(task);
            // The selected detail contains no authoritative operator limits.
            await Timeout((Task)type.GetMethod("ReadOneMemberPage").Invoke(actor,new object[]{false}));
            await Reply(Manager.GetGroups(User),Mine(1).Replace("\"memberPageSize\":50","\"memberPageSize\":51"));
            incoming=_listener.GetContextAsync();task=(Task)type.GetMethod("ReadOneMemberPage").Invoke(actor,new object[]{false});var c=await Timeout(incoming);Assert.That(c.Request.QueryString["limit"],Is.EqualTo("51"));Assert.That(c.Request.QueryString["afterUserId"],Is.Null);await Respond(c,Members(cursor:User));await Timeout(task);
            await Reply(Manager.GetGroups(User),Mine(1).Replace("\"memberPageSize\":50","\"memberPageSize\":1"));
            incoming=_listener.GetContextAsync();task=(Task)type.GetMethod("ReadOneMemberPage").Invoke(actor,new object[]{true});c=await Timeout(incoming);Assert.That(c.Request.QueryString["limit"],Is.EqualTo("1"));Assert.That(c.Request.QueryString["afterUserId"],Is.Null,"Old51 cursor cannot continue a new1-sized traversal");await Respond(c,Members(cursor:User));await Timeout(task);
            incoming=_listener.GetContextAsync();task=(Task)type.GetMethod("ReadOneMemberPage").Invoke(actor,new object[]{true});c=await Timeout(incoming);Assert.That(c.Request.QueryString["limit"],Is.EqualTo("1"));Assert.That(c.Request.QueryString["afterUserId"],Is.EqualTo(User));await Respond(c,Members().Replace("\"userId\":\""+User+"\"","\"userId\":\""+Peer+"\""));await Timeout(task);
            incoming=_listener.GetContextAsync();task=(Task)type.GetMethod("ReadOneMemberPageWithLimit").Invoke(actor,new object[]{7,true});c=await Timeout(incoming);Assert.That(c.Request.QueryString["limit"],Is.EqualTo("7"),"Explicit API parameter is never clamped to current1");Assert.That(c.Request.QueryString["afterUserId"],Is.Null);await Respond(c,"{\"code\":\"INVALID_REQUEST\"}",400);await Timeout(task);
            NetworkService.Instance.SetSessionToken("other");NetworkService.Instance.SetSessionToken("session-"+User);
            await Timeout((Task)type.GetMethod("ReadOneMemberPage").Invoke(actor,new object[]{false}));
            var unexpected=_listener.GetContextAsync();Assert.That(await Task.WhenAny(unexpected,Task.Delay(100)),Is.Not.SameAs(unexpected),"Old auth-context limits never cause another request");
        }

        static string ConfigGroupId(int i)=>"20000000-0000-4000-8000-"+i.ToString("D12");
        [Test] public async Task ElevenConfiguredLimitsRetainedGroupsPageAndDurableMetadataOriginalRemainExact()
        {
            string nested=new string('[',32)+"0"+new string(']',32);
            string data="{\"big\":\""+new string('x',70000)+"\",\"seed\":9007199254740993,\"decimal\":1.0000000000000000000001,\"nested\":"+nested+"}";
            string details="["+string.Join(",",Enumerable.Range(0,6).Select(i=>Detail(data:data,id:i==0?Group:ConfigGroupId(i)).Replace("\"memberCount\":21","\"memberCount\":501")))+"]";
            string PlayerPending(int start)=>"["+string.Join(",",Enumerable.Range(0,21).Select(i=>"{\"groupId\":\""+ConfigGroupId(start+i)+"\",\"requestId\":\""+ConfigGroupId(start+100+i)+"\",\"issuerId\":\""+User+"\",\"group\":null"+(i%2==0?",\"createdAt\":null":"")+"}"))+"]";
            string WithPending(string raw)=>raw.Replace("\"incomingInvites\":[]","\"incomingInvites\":"+PlayerPending(4000)).Replace("\"outgoingRequests\":[]","\"outgoingRequests\":"+PlayerPending(5000));
            string highLimits="{\"maxMembers\":501,\"maxGroupsPerPlayer\":6,\"maxInvitations\":21,\"maxJoinRequests\":21,\"maxDataBytes\":100000,\"memberPageSize\":51,\"maxPlayerInvitations\":21,\"maxPlayerRequests\":21,\"maxNameBytes\":512,\"maxDescriptionBytes\":4096,\"maxDataDepth\":64}";
            var high=await Reply(Manager.GetGroups(User),WithPending(Mine(1,groups:details)).Replace(Limits,highLimits));Assert.That(high.IsSuccess,Is.True);Assert.That(high.Data.groups.Length,Is.EqualTo(6));Assert.That(high.Data.incomingInvites.Length,Is.EqualTo(21));Assert.That(high.Data.outgoingRequests[1].createdAt,Is.Null);Assert.That(high.Data.limits.maxDataDepth,Is.EqualTo(64));Assert.That(high.Data.groups[0].data.Json,Is.EqualTo(data));Assert.That(high.Data.groups[0].memberCount,Is.EqualTo(501));
            string member=JsonFragments.Array(JsonFragments.Object(Members())["members"])[0];string page=Members(cursor:ConfigGroupId(1050)).Replace("["+member+"]","["+string.Join(",",Enumerable.Range(0,51).Select(i=>i==0?member:member.Replace(User,ConfigGroupId(1000+i)).Replace("\"role\":\"leader\"","\"role\":\"member\"")))+"]");
            var incoming=_listener.GetContextAsync();var members=Manager.GetMembers(User,Group,null,51);var c=await Timeout(incoming);Assert.That(c.Request.RawUrl,Does.Contain("limit=51"));await Respond(c,page);Assert.That((await members).Data.members.Length,Is.EqualTo(51));
            string one="{\"userId\":\""+Peer+"\",\"requestId\":\""+RequestId+"\",\"issuerId\":\""+User+"\",\"createdAt\":null}";
            string pending=Pending.Replace("[{\"userId\":\""+Peer+"\",\"requestId\":\""+RequestId+"\",\"issuerId\":\""+User+"\",\"createdAt\":\""+Time+"\"}]","["+string.Join(",",Enumerable.Range(0,21).Select(i=>(i%2==0?one:one.Replace(",\"createdAt\":null","")).Replace(Peer,ConfigGroupId(2000+i)).Replace(RequestId,ConfigGroupId(3000+i))))+"]");
            var requested=await Reply(Manager.GetPending(User,Group),pending);Assert.That(requested.IsSuccess,Is.True);Assert.That(requested.Data.invitations.Length,Is.EqualTo(21));Assert.That(requested.Data.invitations[1].createdAt,Is.Null);
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.GroupsExample, Assembly-CSharp",true);Component actor=new GameObject("[GroupsExampleTest]").AddComponent(type);type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,Peer);
            string path=Path.Combine(Application.persistentDataPath,"groups-"+Peer+"-"+User+".json.journal/intent.json");
            try{
                incoming=_listener.GetContextAsync();var sent=(Task)type.GetMethod("SaveCreateAfterReview").Invoke(actor,new object[]{new string('n',400),new string('d',2048),"open",GroupJsonData.FromJson(data)});c=await Timeout(incoming);Assert.That(File.Exists(path),Is.True);string saved=File.ReadAllText(path);string body=await new StreamReader(c.Request.InputStream).ReadToEndAsync();Assert.That(body,Does.Contain(data).And.Contain("\"expectedPlayerRevision\":1"));await Respond(c,"{\"code\":\"UNAVAILABLE\"}",503);await sent;
                string lowLimits="{"+string.Join(",",JsonFragments.Object(highLimits).Keys.Select(k=>GroupsJson.Quote(k)+":1"))+"}";var low=await Reply(Manager.GetGroups(User),WithPending(Mine(2,groups:details)).Replace(Limits,lowLimits));Assert.That(low.IsSuccess,Is.True);Assert.That(low.Data.groups.Length,Is.EqualTo(6));Assert.That(low.Data.groups[0].data.Json,Is.EqualTo(data));
                UnityEngine.Object.DestroyImmediate(actor.gameObject);actor=new GameObject("[GroupsExampleTest]").AddComponent(type);type.GetField("projectId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,Peer);incoming=_listener.GetContextAsync();sent=(Task)type.GetMethod("ApplySavedIntent").Invoke(actor,null);c=await Timeout(incoming);Assert.That(await new StreamReader(c.Request.InputStream).ReadToEndAsync(),Is.EqualTo(body));await Respond(c,"{\"code\":\"INVALID_GROUP\"}",400);await sent;Assert.That(File.ReadAllText(path),Is.EqualTo(saved));
                string large="{\"big\":\""+new string('x',9*1024*1024)+"\",\"seed\":9007199254740993}";var retained=await Reply(Manager.GetGroup(User,Group),Detail(3,data:large));Assert.That(retained.IsSuccess,Is.True);Assert.That(retained.Data.data.Json,Is.EqualTo(large));
            }finally{CleanupJournal(path);}
        }
    }
    public partial class SharedObjectsTransportTests
    {
        [Test] public async Task ConfiguredSharedCatalogOwnerOverhangAndMaximumOpaqueBytesRemainReadable()
        {
            const string limits="{\"collections\":101,\"storageBytes\":4294967296,\"objectBytes\":1048576}";
            var catalog=await Reply(Manager.GetCollections(User),Collections().Replace(Limits,limits).Replace("\"maxObjectBytes\":65536","\"maxObjectBytes\":1048576").Replace("\"maxObjectsPerOwner\":100","\"maxObjectsPerOwner\":101"));Assert.That(catalog.IsSuccess,Is.True);Assert.That(catalog.Data.limits.storageBytes,Is.EqualTo(4294967296L));Assert.That(catalog.Data.definitions[0].maxObjectsPerOwner,Is.EqualTo(101));
            var usage=await Reply(Manager.GetUsage(User,"levels"),Usage(count:101).Replace(Limits,limits.Replace("1048576","1024")));Assert.That(usage.IsSuccess,Is.True);Assert.That(usage.Data.count,Is.EqualTo(101));
            byte[] bytes=Enumerable.Range(0,1048576).Select(i=>(byte)(i%2==0?0:255)).ToArray();string raw="{\"object\":"+Object(data:Convert.ToBase64String(bytes))+",\"pollAfterSeconds\":120,\"serverTime\":\""+Time+"\"}";
            var point=await Reply(Manager.GetObject(User,"levels",Id),raw);Assert.That(point.IsSuccess,Is.True);Assert.That(point.Data.@object.data,Is.EqualTo(bytes));
        }
    }
    public partial class ChatTransportTests
    {
        [TestCase(0,0,0)] [TestCase(2001,121,201)] [TestCase(int.MaxValue,int.MaxValue,int.MaxValue)]
        public async Task OperatorChatQuotasUseNonnegativeInt32WithoutChangingProtocol(int channels,int sends,int reports)
        {
            string json=Limits.Replace("\"channelsPerAccount\":100","\"channelsPerAccount\":"+channels).Replace("\"sendsPerAccountMinute\":20","\"sendsPerAccountMinute\":"+sends).Replace("\"reportsPerProject\":50","\"reportsPerProject\":"+reports);
            var result=await Reply(Manager.GetLimits(User),json);Assert.That(result.IsSuccess,Is.True);Assert.That(result.Data.limits.channelsPerAccount,Is.EqualTo(channels));Assert.That(result.Data.limits.sendsPerAccountMinute,Is.EqualTo(sends));Assert.That(result.Data.limits.reportsPerProject,Is.EqualTo(reports));Assert.That(result.Data.limits.retainedMessagesPerChannel,Is.EqualTo(20));
        }
    }
}
