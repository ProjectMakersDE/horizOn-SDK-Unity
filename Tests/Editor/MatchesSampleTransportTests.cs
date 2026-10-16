using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    public partial class MatchesTransportTests
    {
        private static Type SampleType=>Type.GetType("PM.horizOn.Cloud.Examples.Features.MatchesExample, Assembly-CSharp",true);
        private static Component Actor(){var a=new GameObject("[MatchesExampleTest]").AddComponent(SampleType);SampleType.GetField("projectId",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(a,Project);return a;}
        private static Task<bool> Act(Component a,string method,params object[] args)=>(Task<bool>)SampleType.GetMethod(method).Invoke(a,args);
        private static void Ack(Component a)=>SampleType.GetMethod("AcknowledgeSavedResult").Invoke(a,null);
        private async Task<bool> ActReply(Component a,string method,string json,params object[] args){var t=Act(a,method,args);await Respond(await Incoming(),json);return await Bounded(t);}
        private static string Journal(string user=User)=>Path.Combine(Application.persistentDataPath,"matches-"+Hash(ApiKey)+"-"+user+".journal");
        private static string Hash(string text){using(var s=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(s.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        private static void CleanupJournals()
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            foreach(string user in new[]{User,Peer})
            {
                string prefix=Path.GetFileName(Journal(user));foreach(string path in Directory.GetDirectories(Application.persistentDataPath,prefix+"*"))Directory.Delete(path,true);
                string gate=Journal(user)+".lock";if(File.Exists(gate))File.Delete(gate);
            }
        }
        [Test] public async Task ActualTwoPlayerSampleCreateJoinTurnsImmutableReportAndRankedSubmit()
        {
            var a=Actor();Assert.That(await ActReply(a,"ReadLedger",Ledger(),false),Is.True);
            var input=new MatchCreateInput{name="前\0後😀",capacity=3,strength=-9007199254740991,turnSeconds=60,timeoutPolicy="forfeit",data=new byte[]{0,255,0},inviteeIds=new[]{Peer},ranked=true};
            Assert.That(await ActReply(a,"SaveCreateAfterLedgerReview",Envelope(View(participants:Ids(User),invites:Ids(Peer),ranked:true),",\"playerRevision\":1"),input),Is.True);Ack(a);
            SetUser(Peer);var b=Actor();Assert.That(await ActReply(b,"ReadLedger",Ledger(1,invites:"["+Summary(participants:Ids(User),invites:Ids(Peer),ranked:true)+"]",user:Peer),false),Is.True);
            Assert.That(await ActReply(b,"ReadMatch",Envelope(View(bytes:"null",participants:Ids(User),invites:Ids(Peer),ranked:true)),Id,false),Is.True);
            Assert.That(await ActReply(b,"SaveActionAfterMatchReview",Envelope(View(2,ranked:true)),"join",null,null,false),Is.True);Ack(b);
            SetUser(User);Assert.That(await ActReply(a,"ReadLedger",Ledger(2,"["+Summary(2,ranked:true)+"]"),true),Is.True);Assert.That(await ActReply(a,"ReadMatch",Envelope(View(2,ranked:true)),Id,true),Is.True);
            Assert.That(await ActReply(a,"SaveActionAfterMatchReview",Envelope(View(3,"active",ranked:true)),"start",null,new[]{User,Peer},false),Is.True);Ack(a);
            SetUser(Peer);Assert.That(await ActReply(b,"ReadLedger",Ledger(2,"["+Summary(3,"active",ranked:true)+"]",user:Peer),true),Is.True);Assert.That(await ActReply(b,"ReadMatch",Envelope(View(3,"active",ranked:true)),Id,true),Is.True);
            Assert.That(await ActReply(b,"SaveActionAfterMatchReview",Envelope(View(4,"active",ranked:true)).Replace("\"currentPlayerId\":\""+Peer+"\"","\"currentPlayerId\":\""+User+"\""),"turn",new byte[]{0,255,0},null,false),Is.True);Ack(b);
            SetUser(User);Assert.That(await ActReply(a,"ReadLedger",Ledger(2,"["+Summary(4,"active",ranked:true)+"]"),true),Is.True);Assert.That(await ActReply(a,"ReadMatch",Envelope(View(4,"active",ranked:true)),Id,true),Is.True);
            Assert.That(await ActReply(a,"SaveActionAfterMatchReview",Envelope(View(5,"reporting",ranked:true)),"turn",new byte[]{0,255,0},null,true),Is.True);Ack(a);
            Assert.That(await ActReply(a,"ReadLedger",Ledger(3,recent:"["+Summary(5,"reporting",ranked:true)+"]"),true),Is.True);Assert.That(await ActReply(a,"ReadMatch",Envelope(View(5,"reporting",ranked:true)),Id,true),Is.True);
            var first=Act(a,"SaveActionAfterMatchReview","report",new byte[]{1},new[]{User},false);var c=await Incoming();string original=await Body(c);await Respond(c,"{\"code\":\"UNCERTAIN\"}",503);Assert.That(await first,Is.False);
            UnityEngine.Object.DestroyImmediate(a.gameObject);a=Actor();var replay=Act(a,"ApplySavedIntent");c=await Incoming();Assert.That(await Body(c),Is.EqualTo(original));await Respond(c,Envelope(View(6,"reporting",reports:"["+Report()+"]",ranked:true),",\"replayed\":true"));Assert.That(await replay,Is.True);Ack(a);
            SetUser(Peer);Assert.That(await ActReply(b,"ReadLedger",Ledger(3,recent:"["+Summary(6,"reporting",ranked:true)+"]",user:Peer),true),Is.True);Assert.That(await ActReply(b,"ReadMatch",Envelope(View(6,"reporting",reports:"["+Report()+"]",ranked:true)),Id,true),Is.True);
            Assert.That(await ActReply(b,"SaveActionAfterMatchReview",Envelope(View(7,"confirmed",reports:"["+Report()+","+Report(Peer)+"]",ranked:true),",\"replayed\":false"),"report",new byte[]{1},new[]{User},false),Is.True);Ack(b);
            SetUser(User);Assert.That(await ActReply(a,"ReadLedger",Ledger(4,recent:"["+Summary(7,"confirmed",ranked:true)+"]"),true),Is.True);Assert.That(await ActReply(a,"ReadMatch",Envelope(View(7,"confirmed",reports:"["+Report()+","+Report(Peer)+"]",ranked:true)),Id,true),Is.True);
            Assert.That(await ActReply(a,"SaveActionAfterMatchReview",Run,"ranked",null,null,false),Is.True);Assert.That(File.Exists(Path.Combine(Journal(),"run.json")),Is.True);
            var submit=Act(a,"SubmitOriginalRanked",3L,new byte[]{0,255,0});c=await Incoming();Assert.That(await Body(c),Does.Contain("private-original-ranked-ticket").And.Contain(ValidatedActionsManager.ComputeInputLogHash(new byte[]{0,255,0})));Assert.That(File.Exists(Path.Combine(Journal(),"submission.json")),Is.True);await Respond(c,"{\"accepted\":true,\"runId\":\""+RunId+"\",\"state\":null,\"evidence\":null}");Assert.That(await submit,Is.True);Ack(a);Assert.That(Directory.Exists(Journal()),Is.False);Assert.That(ValidatedActionsManager.Instance.CurrentRun,Is.Null);
        }
        [Test] public async Task ActualUnknownCreateRequiresExplicitStableNewerLedgerAndNeverGuessesSuccess()
        {
            var a=Actor();Assert.That(await ActReply(a,"ReadLedger",Ledger(),false),Is.True);var t=Act(a,"SaveCreateAfterLedgerReview",new MatchCreateInput{name="unknown"});await Respond(await Incoming(),"{\"code\":\"UNCERTAIN\"}",503);Assert.That(await t,Is.False);string original=File.ReadAllText(Path.Combine(Journal(),"intent.json"));
            UnityEngine.Object.DestroyImmediate(a.gameObject);a=Actor();Assert.That(await Act(a,"ApplySavedIntent"),Is.False);Assert.That(File.ReadAllText(Path.Combine(Journal(),"intent.json")),Is.EqualTo(original));
            Assert.That(await ActReply(a,"ReadLedger",Ledger(),true),Is.True);Assert.That(await ActReply(a,"ReconcileUnknownCreateAfterLedgerReview",Ledger()),Is.False,"unchanged CAS is still live");
            Assert.That(await ActReply(a,"ReadLedger",Ledger(1,"["+Summary()+"]"),true),Is.True);Assert.That(await ActReply(a,"ReconcileUnknownCreateAfterLedgerReview",Ledger(1,"["+Summary()+"]")),Is.True);Assert.That(File.ReadAllText(Path.Combine(Journal(),"result.json")),Does.Contain("\"serverConfirmed\":false").And.Contain("reviewed-original-fenced"));Ack(a);
        }
        [Test] public async Task ActualTwoActorsRetainOsOwnershipThroughHttpAndStaleAckCannotRemoveNewOriginal()
        {
            var a=Actor();var b=Actor();Assert.That(await ActReply(a,"ReadLedger",Ledger(),false),Is.True);var t=Act(a,"SaveCreateAfterLedgerReview",new MatchCreateInput{name="前\0後😀",capacity=3,strength=-9007199254740991,turnSeconds=60,timeoutPolicy="forfeit",data=new byte[]{0,255,0}});var c=await Incoming();bool blocked=false;try{await Act(b,"ApplySavedIntent");}catch(IOException){blocked=true;}Assert.That(blocked,Is.True);await Respond(c,Envelope(View(participants:Ids(User)),",\"playerRevision\":1"));Assert.That(await t,Is.True);
            Assert.That(await Act(b,"ApplySavedIntent"),Is.True);Ack(b);Assert.That(await ActReply(b,"ReadLedger",Ledger(1,"["+Summary()+"]"),true),Is.True);
            t=Act(b,"SaveCreateAfterLedgerReview",new MatchCreateInput{name="前\0後😀",capacity=3,strength=-9007199254740991,turnSeconds=60,timeoutPolicy="forfeit",data=new byte[]{0,255,0}});c=await Incoming();await Respond(c,Envelope(View(participants:Ids(User)),",\"playerRevision\":2"));Assert.That(await t,Is.True);
            Assert.Throws<TargetInvocationException>(()=>Ack(a));Assert.That(Directory.Exists(Journal()),Is.True);Ack(b);
        }
        [Test] public async Task LostRemovalAckNeedsSignedMissingAndStrictlyAdvancedOwnLedger()
        {
            SetUser(Peer);var a=Actor();Assert.That(await ActReply(a,"ReadLedger",Ledger(1,invites:"["+Summary(participants:Ids(User),invites:Ids(Peer))+"]",user:Peer),false),Is.True);Assert.That(await ActReply(a,"ReadMatch",Envelope(View(bytes:"null",participants:Ids(User),invites:Ids(Peer))),Id,false),Is.True);
            var t=Act(a,"SaveActionAfterMatchReview","decline",null,null,false);await Respond(await Incoming(),"{\"code\":\"UNCERTAIN\"}",503);Assert.That(await t,Is.False);
            t=Act(a,"ReconcileOriginalActionAfterReview");await Respond(await Incoming(),"{\"code\":\"MATCH_NOT_FOUND\"}",404);await Respond(await Incoming(),Ledger(1,user:Peer));Assert.That(await t,Is.False);
            t=Act(a,"ReconcileOriginalActionAfterReview");await Respond(await Incoming(),"{\"code\":\"MATCH_NOT_FOUND\"}",404);await Respond(await Incoming(),Ledger(2,user:Peer));Assert.That(await t,Is.True);Ack(a);
        }
        [Test] public async Task OriginalRankedSubmitRejectsChangedTupleAndRecoversSameRunAfterNetworkRecreation()
        {
            var a=Actor();Assert.That(await ActReply(a,"ReadLedger",Ledger(4,recent:"["+Summary(7,"confirmed",ranked:true)+"]"),false),Is.True);Assert.That(await ActReply(a,"ReadMatch",Envelope(View(7,"confirmed",ranked:true)),Id,false),Is.True);
            Assert.That(await ActReply(a,"SaveActionAfterMatchReview",Run,"ranked",null,null,false),Is.True);
            var t=Act(a,"SubmitOriginalRanked",3L,new byte[]{0,255,0});string original=null;
            for(int attempt=0;attempt<=_config.MaxRetryAttempts;attempt++){var incoming=await Incoming();string sent=await Body(incoming);if(original==null)original=sent;else Assert.That(sent,Is.EqualTo(original));await Respond(incoming,"{\"code\":\"UNCERTAIN\"}",503);}Assert.That(await t,Is.False);

            Assert.ThrowsAsync<IOException>(async()=>await Act(a,"SubmitOriginalRanked",4L,new byte[]{0,255,0}));
            // A recreated transport drops the local run but the stable key/player journal remains.
            PM.horizOn.Cloud.Service.NetworkService.ResetInstance();PM.horizOn.Cloud.Service.NetworkService.Instance.Initialize(_config);PM.horizOn.Cloud.Service.NetworkService.Instance.SetActiveHost(Host);SetUser(User);
            UnityEngine.Object.DestroyImmediate(a.gameObject);a=Actor();Assert.That(await ActReply(a,"ApplySavedIntent",Run),Is.True,"manager="+Manager.LastErrorCode+" va="+ValidatedActionsManager.Instance.LastErrorCode);Assert.That(ValidatedActionsManager.Instance.CurrentRun.runId,Is.EqualTo(RunId));
            t=Act(a,"SubmitOriginalRanked",3L,new byte[]{0,255,0});var c=await Incoming();Assert.That(await Body(c),Is.EqualTo(original));await Respond(c,"{\"accepted\":true,\"runId\":\""+RunId+"\",\"state\":null,\"evidence\":null}");Assert.That(await t,Is.True);Ack(a);
        }
        [TestCase(422,true)][TestCase(503,false)]
        public async Task FreshDefinitiveCreateRejectionCanBeReviewedButUncertainCodeNeverReleasesOriginal(int status,bool resolved)
        {
            var a=Actor();Assert.That(await ActReply(a,"ReadLedger",Ledger(),false),Is.True);var t=Act(a,"SaveCreateAfterLedgerReview",new MatchCreateInput{name="bounded"});await Respond(await Incoming(),"{\"code\":\"ACTIVE_MATCH_LIMIT_REACHED\"}",status);Assert.That(await t,Is.False);
            Assert.That(File.Exists(Path.Combine(Journal(),"result.json")),Is.EqualTo(resolved));Ack(a);Assert.That(Directory.Exists(Journal()),Is.EqualTo(!resolved));
        }
        [Test] public async Task ActualSharedDefenseAndMatchRankedStartShareOneHttpOwner()
        {
            var type=Type.GetType("PM.horizOn.Cloud.Examples.Features.SharedObjectsExample, Assembly-CSharp",true);var actor=new GameObject("[MatchesSharedDefenseTest]").AddComponent(type);type.GetField("projectId",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(actor,Project);
            var selected=new SharedObject{id=Id,collection="levels",revision=7,data=new byte[]{0,255,0}};
            try
            {
                var start=Manager.StartRankedRun(User,Id,5);var c=await Incoming();var blocked=(Task<bool>)type.GetMethod("StartDefense").Invoke(actor,new object[]{selected});Assert.That(await blocked,Is.False);await Respond(c,Run);Assert.That((await start).IsSuccess,Is.True);ValidatedActionsManager.Instance.DiscardRun();
                var defense=(Task<bool>)type.GetMethod("StartDefense").Invoke(actor,new object[]{selected});c=await Incoming();Assert.That((await Manager.StartRankedRun(User,Id,5)).ErrorCode,Is.EqualTo("RUN_OPERATION_PENDING"));Assert.That(await ValidatedActionsManager.Instance.StartRun(),Is.Null);await Respond(c,Run);Assert.That(await defense,Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actor.gameObject);string prefix="shared-objects-"+Hash(ApiKey)+"-"+User+".journal";
                foreach(string path in Directory.GetDirectories(Application.persistentDataPath,prefix+"*"))Directory.Delete(path,true);string gate=Path.Combine(Application.persistentDataPath,prefix+".lock");if(File.Exists(gate))File.Delete(gate);
            }
        }
        [Test] public async Task DisputedAndMissingReportsExposeAuthoritativeStatusAndFullInboxMetadataOnly()
        {
            var r=await Reply(Manager.GetMatch(User,Id),Envelope(View(7,"disputed",reports:"["+Report()+","+Report(Peer,"Ag==")+"]")));Assert.That(r.IsSuccess,Is.True);Assert.That(r.Data.match.hasDisagreement,Is.True);Assert.That(r.Data.match.notificationPending,Is.True);
            var missing=await Reply(Manager.GetMatch(User,Id,true),Envelope(View(8,"unconfirmed",reports:"["+Report()+"]")));Assert.That(missing.IsSuccess,Is.True);Assert.That((await Reply(Manager.StartRankedRun(User,Id,5),"{\"code\":\"RANKED_NOT_CONFIRMED\"}",422)).ErrorCode,Is.EqualTo("RANKED_NOT_CONFIRMED"));Assert.That(ValidatedActionsManager.Instance.CurrentRun,Is.Null);
        }
    }
}
