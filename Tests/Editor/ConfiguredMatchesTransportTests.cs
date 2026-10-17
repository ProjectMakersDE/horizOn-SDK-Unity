using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;
namespace PM.horizOn.Cloud.Tests
{
    public partial class MatchesTransportTests
    {
        static string ConfigMatchId(int i)=>"30000000-0000-4000-8000-"+i.ToString("D12");
        static string ExtendedLimits=>"{\"activeMatches\":11,\"stateBytes\":"+MatchLimits.MaxStateBytes+",\"retainedMatches\":0,\"pendingInvites\":21,\"settlementMatches\":201,\"historyRetentionSeconds\":0,\"rankedSettlementSeconds\":604800}";
        static string PendingSources=>"["+string.Join(",",Enumerable.Range(0,201).Select(i=>"{\"matchId\":\""+ConfigMatchId(9000+i)+"\",\"finalStateRevision\":5,\"ownRankedRunId\":"+(i==0?"\""+RunId+"\"":"null")+",\"expiresAt\":\"2026-10-23T00:00:00Z\"}"))+"]";
        static string ConfiguredLedger()
        {
            string active="["+string.Join(",",Enumerable.Range(0,11).Select(i=>Summary().Replace(Id,ConfigMatchId(i))))+"]";
            string invites="["+string.Join(",",Enumerable.Range(0,21).Select(i=>Summary(participants:Ids(Peer),invites:Ids(User)).Replace("\"creatorId\":\""+User+"\"","\"creatorId\":\""+Peer+"\"").Replace(Id,ConfigMatchId(1000+i))))+"]";
            string recent="["+string.Join(",",Enumerable.Range(0,101).Select(i=>Summary(status:"cancelled").Replace("\"finishedAt\":null","\"finishedAt\":\""+Time+"\"").Replace("\"purgeAt\":null","\"purgeAt\":\"2026-10-23T00:00:00Z\"").Replace(Id,ConfigMatchId(2000+i))))+"]";
            return Ledger(1,active,invites,recent).Replace(Limits,ExtendedLimits).Replace("\"pendingResults\":[]","\"pendingResults\":"+PendingSources);
        }
        [Test] public async Task ConfiguredMatchListsAndOriginalPendingSourceRecoverWithoutHistoricalDetail()
        {
            var caps=await Reply(Manager.GetLimits(User),Config.Replace(Limits,ExtendedLimits));Assert.That(caps.IsSuccess,Is.True);Assert.That(caps.Data.limits.stateBytes,Is.EqualTo(MatchLimits.MaxStateBytes));Assert.That(caps.Data.limits.historyRetentionSeconds,Is.Zero);Assert.That(caps.Data.limits.settlementMatches,Is.EqualTo(201));
            var ledger=await Reply(Manager.GetMatches(User),ConfiguredLedger());Assert.That(ledger.IsSuccess,Is.True);Assert.That(ledger.Data.active.Length,Is.EqualTo(11));Assert.That(ledger.Data.invites.Length,Is.EqualTo(21));Assert.That(ledger.Data.recent.Length,Is.EqualTo(101));Assert.That(ledger.Data.pendingResults.Length,Is.EqualTo(201));Assert.That(ledger.Data.pendingResults[1].ownRankedRunId,Is.Null);
            var actor=Actor();Assert.That(await ActReply(actor,"ReadLedger",ConfiguredLedger(),true),Is.True);
            var sent=Act(actor,"SaveRankedAfterPendingResultReview",ConfigMatchId(9000));var c=await Incoming();string original=await Body(c);Assert.That(original,Does.Contain("\"finalStateRevision\":5"));Assert.That(c.Request.RawUrl,Is.EqualTo("/api/v1/app/matches/"+ConfigMatchId(9000)+"/ranked-run"));string path=Path.Combine(Journal(),"intent.json");Assert.That(File.Exists(path),Is.True);string saved=File.ReadAllText(path);await Respond(c,"{\"code\":\"UNAVAILABLE\"}",503);Assert.That(await sent,Is.False);
            UnityEngine.Object.DestroyImmediate(actor.gameObject);actor=Actor();sent=Act(actor,"ApplySavedIntent");c=await Incoming();Assert.That(await Body(c),Is.EqualTo(original));await Respond(c,Run.Replace(RunId,ConfigMatchId(9999)));Assert.That(await sent,Is.False);Assert.That(Manager.LastErrorCode,Is.EqualTo("INVALID_RESPONSE"));Assert.That(ValidatedActionsManager.Instance.CurrentRun,Is.Null);Assert.That(File.ReadAllText(path),Is.EqualTo(saved));
            sent=Act(actor,"ApplySavedIntent");c=await Incoming();Assert.That(await Body(c),Is.EqualTo(original));await Respond(c,Run);Assert.That(await sent,Is.True);Assert.That(ValidatedActionsManager.Instance.CurrentRun.runId,Is.EqualTo(RunId));Assert.That(File.Exists(Path.Combine(Journal(),"result.json")),Is.False,"A run start is not a finished ranked submission");Assert.That(File.ReadAllText(path),Is.EqualTo(saved));
        }
        [Test] public async Task MaximumStateReadAndDurableUncertainCreateRemainExactAcrossRestartAndSafeAck()
        {
            byte[] state=Enumerable.Range(0,MatchLimits.MaxStateBytes).Select(i=>(byte)(i%2==0?0:255)).ToArray();string encoded=Convert.ToBase64String(state);
            var point=await Reply(Manager.GetMatch(User,Id),Envelope(View(bytes:"\""+encoded+"\"")));Assert.That(point.IsSuccess,Is.True);Assert.That(point.Data.match.data,Is.EqualTo(state));
            string oldArchive=Journal()+".old.acknowledged";Directory.CreateDirectory(oldArchive);File.WriteAllText(Path.Combine(oldArchive,"original.txt"),"retained legacy proof");
            var actor=Actor();Assert.That(await ActReply(actor,"ReadLedger",Ledger().Replace(Limits,ExtendedLimits),true),Is.True);
            var sent=Act(actor,"SaveCreateAfterLedgerReview",new MatchCreateInput{name="Maximum original",data=state});var c=await Incoming();string path=Path.Combine(Journal(),"intent.json");Assert.That(File.Exists(path),Is.True,"full original is durable before HTTP");string saved=File.ReadAllText(path);string body=await Body(c);Assert.That(JsonFragments.String(JsonFragments.Object(body)["dataBase64"]),Is.EqualTo(encoded));Assert.That(c.Request.Headers["If-Match"],Is.EqualTo("0"));await Respond(c,"{\"code\":\"UNAVAILABLE\"}",503);Assert.That(await sent,Is.False);
            UnityEngine.Object.DestroyImmediate(actor.gameObject);actor=Actor();Assert.That(await Act(actor,"ApplySavedIntent"),Is.False,"unknown create is never resent");Assert.That(File.ReadAllText(path),Is.EqualTo(saved));
            string newer=Ledger(1).Replace(Limits,ExtendedLimits);Assert.That(await ActReply(actor,"ReadLedger",newer,true),Is.True);Assert.That(await ActReply(actor,"ReconcileUnknownCreateAfterLedgerReview",newer),Is.True);Ack(actor);
            Assert.That(Directory.Exists(Journal()),Is.False);Assert.That(Directory.GetDirectories(Application.persistentDataPath,Path.GetFileName(Journal())+"*.completed"),Is.Empty);Assert.That(File.ReadAllText(Path.Combine(oldArchive,"original.txt")),Is.EqualTo("retained legacy proof"));Assert.That(Directory.GetDirectories(Application.persistentDataPath,Path.GetFileName(Journal())+"*.acknowledged").Length,Is.EqualTo(1),"no new completed payload archive");
        }
    }
}
