using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using PM.horizOn.Cloud.Examples.Features;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;

static class MatchesJournalContractTests
{
    const string User=MatchesContractTests.User,Project="11111111-1111-4111-8111-111111111111",Key="journal-matches";
    static MatchesSampleStore Store(string root)=>new MatchesSampleStore(root,Project,User,Key);
    static string Dir(string root)=>Path.Combine(root,MatchesSampleStore.FileName(Key,User));
    static MatchesSampleStore.Intent Intent()=>MatchesSampleStore.NewCreate(Project,User,9007199254740990,new MatchCreateInput{name="前\0後😀",strength=-9007199254740991,data=new byte[]{0,255,0}});
    public static bool TryRunChild(string[] args)
    {
        if(args.Length!=3||args[0]!="--matches-journal-crash")return false;string root=args[1],phase=args[2];var store=Store(root);store.SaveNew(Intent());
        using(var lease=store.AcquireSend())
        {
            lease.MarkSent();
            if(phase=="unpublished-result")
            {
                var r=new MatchesSampleStore.Receipt{intentHash=lease.Prepared.Hash,matchId=MatchesContractTests.Id,revision=1,playerRevision=9007199254740991,serverConfirmed=true,resolution="server-confirmed"};
                typeof(MatchesSampleStore).GetMethod("WriteNew",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{Path.Combine(Dir(root),"result.0123456789abcdef0123456789abcdef.writing"),JsonUtility.ToJson(r)});
            }
            if(phase=="before-ack"||phase=="after-ack")lease.SaveSuccess(MatchesContractTests.Id,1,9007199254740991);
            if(phase=="after-ack")lease.Acknowledge(lease.Prepared.Hash);
            string ready=Path.Combine(root,"ready");File.WriteAllText(ready+".writing","ready");using(var f=new FileStream(ready+".writing",FileMode.Open,FileAccess.Write))f.Flush(true);File.Move(ready+".writing",ready);while(true)Thread.Sleep(50);
        }
    }
    public static void Run()
    {
        foreach(string phase in new[]{"held-create","unpublished-result","before-ack","after-ack"})Crash(phase);
        string root=Path.Combine(Path.GetTempPath(),"horizon-matches-journal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var s=Store(root);s.SaveNew(Intent());string oldHash;
            using(var lease=s.AcquireSend())
            {
                oldHash=lease.Prepared.Hash;Reject(()=>Store(root).AcquireSend(),"exclusive OS owner");lease.MarkSent();Reject(()=>lease.MarkSent(),"unknown create never resend");Reject(()=>lease.SaveReconciled(0,9007199254740990),"equal player CAS never fenced");lease.SaveReconciled(0,9007199254740991);lease.Acknowledge(oldHash);
            }
            Require(!s.HasIntent,"atomic full journal acknowledgement");var next=Intent();next.revision=next.playerRevision=1;s.SaveNew(next);
            using(var lease=s.AcquireSend()){lease.SaveSuccess(MatchesContractTests.Id,1,2);Reject(()=>lease.Acknowledge(oldHash),"stale acknowledgement cannot remove new original");Require(s.HasIntent,"new original retained");lease.Acknowledge(lease.Prepared.Hash);}
            s.SaveNew(Intent());File.WriteAllText(Path.Combine(Dir(root),"result.partial.writing"),"{\"intentHash\":");Reject(()=>s.AcquireSend(),"partial result blocks send");Reject(()=>s.SaveNew(Intent()),"partial result blocks replacement");
        }
        finally{Directory.Delete(root,true);}
        Console.WriteLine("Matches journal:4 real killed-process recoveries; immutable Unicode/NUL/bytes/safe64, unknown-create fence, partial/conflicting-result and exact stale-ACK ownership passed.");
    }
    static void Crash(string phase)
    {
        string root=Path.Combine(Path.GetTempPath(),"horizon-matches-crash-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Process child=null;
        try
        {
            var info=new ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false};if(Path.GetFileNameWithoutExtension(Environment.ProcessPath)=="dotnet")info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach(string a in new[]{"--matches-journal-crash",root,phase})info.ArgumentList.Add(a);child=Process.Start(info);var deadline=DateTime.UtcNow.AddSeconds(20);while(!File.Exists(Path.Combine(root,"ready"))&&!child.HasExited&&DateTime.UtcNow<deadline)Thread.Sleep(20);
            Require(File.Exists(Path.Combine(root,"ready"))&&!child.HasExited,"actual exact crash phase "+phase);Reject(()=>Store(root).AcquireSend(),"live child retains handle");child.Kill();Require(child.WaitForExit(10000),"own child stopped");child.Dispose();child=null;
            var store=Store(root);if(phase=="after-ack"){Require(!store.HasIntent&&Directory.GetDirectories(root,"*.acknowledged").Length==0&&Directory.GetDirectories(root,"*.completed").Length==0,"atomic terminal acknowledgement removes only new completed original after death");}
            else using(var lease=store.AcquireSend())
            {
                var x=lease.Prepared.Value;Require(x.revision==9007199254740990&&x.GetCreateInput().name=="前\0後😀"&&x.GetBytes()[1]==255&&x.strength==-9007199254740991,"original survives death losslessly");Require(lease.Prepared.WasSent,"sent marker survives death");if(phase=="held-create")Reject(()=>lease.MarkSent(),"never repeat create after death");else Require(lease.Prepared.HasSuccess,"interrupted complete result recovered");
            }
        }
        finally{if(child!=null){if(!child.HasExited){child.Kill();child.WaitForExit(10000);}child.Dispose();}Directory.Delete(root,true);}
    }
    static void Reject(Action a,string m){bool failed=false;try{a();}catch(IOException){failed=true;}catch(ArgumentException){failed=true;}Require(failed,m);}
    static void Require(bool b,string m){if(!b)throw new Exception("Matches journal: "+m);}
}
