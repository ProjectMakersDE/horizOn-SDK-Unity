using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using PM.horizOn.Cloud.Examples.Features;

static class GroupsJournalContractTests
{
    const string Project="11111111-1111-4111-8111-111111111111",User="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",Request="bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
    static string Legacy(string root)=>Path.Combine(root,"groups-"+Project+"-"+User+".json");
    static GroupsSampleStore Store(string root)=>new GroupsSampleStore(root,Project,User);
    static GroupsSampleStore.Intent Intent(string phase="")=>new GroupsSampleStore.Intent{projectId=Project,userId=User,action=phase.EndsWith("null-group",StringComparison.Ordinal)?"leave":"accept-invite",groupId=User,revision=17,requestId=phase.EndsWith("null-group",StringComparison.Ordinal)?null:Request};
    public static bool TryRunChild(string[] args)
    {
        if(args.Length!=3||args[0]!="--groups-journal-crash")return false;
        string root=args[1],phase=args[2];var store=Store(root);store.SaveNew(Intent(phase));var original=store.PrepareSend();
        if(phase!="locked-intent"&&!phase.StartsWith("unpublished-",StringComparison.Ordinal))store.SaveSuccess(original.Hash,User,18);
        if(phase=="after-ack"){store.AcknowledgeSavedSuccess();Ready();}
        else typeof(GroupsSampleStore).GetMethod("WithLock",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(store,new object[]{(Action)(()=>{if(phase.StartsWith("unpublished-",StringComparison.Ordinal))WriteReceipt();Ready();})});
        return true;
        void WriteReceipt(){var receipt=Activator.CreateInstance(typeof(GroupsSampleStore).GetNestedType("Success",BindingFlags.NonPublic));receipt.GetType().GetField("intentHash").SetValue(receipt,original.Hash);receipt.GetType().GetField("groupId").SetValue(receipt,phase.EndsWith("null-group",StringComparison.Ordinal)?null:User);receipt.GetType().GetField("playerRevision").SetValue(receipt,18L);string json=UnityEngine.JsonUtility.ToJson(receipt);typeof(GroupsSampleStore).GetMethod("WriteNew",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Path.Combine(root,"groups-"+Project+"-"+User+".json.journal","result.0123456789abcdef0123456789abcdef.writing"),json});}
            void Ready(){File.WriteAllText(Path.Combine(root,"ready.writing"),original.Hash);using(var f=new FileStream(Path.Combine(root,"ready.writing"),FileMode.Open,FileAccess.Write))f.Flush(true);File.Move(Path.Combine(root,"ready.writing"),Path.Combine(root,"ready"));while(true)Thread.Sleep(50);}
    }
    public static void Run()
    {
        foreach(string phase in new[]{"locked-intent","unpublished-result","unpublished-null-group","before-ack","after-ack"})Crash(phase);
        Console.WriteLine("Groups journal: five actual killed-process lock/ACK recovery contracts passed.");
    }
    static void Crash(string phase)
    {
        string root=Path.Combine(Path.GetTempPath(),"horizon-groups-crash-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Process child=null;
        try{
            string executable=Environment.ProcessPath;var info=new ProcessStartInfo(executable){UseShellExecute=false};
            if(Path.GetFileNameWithoutExtension(executable)=="dotnet")info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach(string value in new[]{"--groups-journal-crash",root,phase})info.ArgumentList.Add(value);
            child=Process.Start(info);var deadline=DateTime.UtcNow.AddSeconds(20);string ready=Path.Combine(root,"ready");
            while(!File.Exists(ready)&&!child.HasExited&&DateTime.UtcNow<deadline)Thread.Sleep(20);
            Require(File.Exists(ready)&&!child.HasExited,"child reached exact crash phase "+phase);string hash=File.ReadAllText(ready);
            if(phase!="after-ack")Reject(()=>Store(root).PrepareSend(),"another live process owns the exclusive lock");
            child.Kill();Require(child.WaitForExit(10000),"owned child exited after kill");child.Dispose();child=null;
            var restarted=Store(root);string journal=Legacy(root)+".journal";
            if(phase=="after-ack"){
                Require(!restarted.HasIntent,"ACK crash leaves no active original");var archives=Directory.GetDirectories(root,"*.acknowledged");
                Require(archives.Length==1&&File.Exists(Path.Combine(archives[0],"intent.json"))&&File.Exists(Path.Combine(archives[0],"result.json")),"original and result archived together");
                restarted.SaveNew(Intent());Require(restarted.PrepareSend().Hash==hash,"new explicit original after complete ACK");
            }else{
                var original=restarted.PrepareSend();Require(original.Hash==hash&&original.Value.revision==17&&(phase.EndsWith("null-group",StringComparison.Ordinal)?string.IsNullOrEmpty(original.Value.requestId):original.Value.requestId==Request),"process death preserves exact original revision and request ID");
                Reject(()=>restarted.SaveNew(Intent()),"pending original still blocks replacement");
                if((phase=="before-ack"||phase.StartsWith("unpublished-",StringComparison.Ordinal))){Require(restarted.HasSuccess,"confirmed result survives death");restarted.VerifySavedSuccess(hash);restarted.AcknowledgeSavedSuccess();Require(!Directory.Exists(journal),"whole journal acknowledged after recovery");}
                else Require(!restarted.HasSuccess,"unconfirmed original never becomes successful");
            }
            Require(File.Exists(Legacy(root)+".lock"),"persistent lock inode survives ownership release");
        }finally{if(child!=null){if(!child.HasExited){child.Kill();child.WaitForExit(10000);}child.Dispose();}Directory.Delete(root,true);}
    }
    static void Reject(Action action,string message){bool rejected=false;try{action();}catch(IOException){rejected=true;}Require(rejected,message);}
    static void Require(bool value,string message){if(!value)throw new Exception("Groups crash recovery: "+message);}
}
