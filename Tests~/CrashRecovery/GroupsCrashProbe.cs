using System;
using System.IO;
using System.Reflection;
using System.Threading;
using PM.horizOn.Cloud.Examples.Features;
using UnityEditor;
// Copied into an isolated actual Unity Editor project by the local crash-probe runner.
public static class GroupsCrashProbe
{
    const string Project="11111111-1111-4111-8111-111111111111",User="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",Request="bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
    public static void Run()
    {
        try{
            string[] args=Environment.GetCommandLineArgs();string root=Value("-groupsProbeRoot"),phase=Value("-groupsProbePhase");var store=new GroupsSampleStore(root,Project,User);
            string journal=Path.Combine(root,"groups-"+Project+"-"+User+".json.journal");
            if(phase.StartsWith("recover-",StringComparison.Ordinal)){
                string originalHash=File.ReadAllText(Path.Combine(root,"ready"));
                if(phase=="recover-after-ack"){
                    Require(!store.HasIntent,"no active half-ACK");var archives=Directory.GetDirectories(root,"*.acknowledged");Require(archives.Length==1&&File.Exists(Path.Combine(archives[0],"intent.json"))&&File.Exists(Path.Combine(archives[0],"result.json")),"complete archived pair");
                    store.SaveNew(Intent());Require(store.PrepareSend().Hash==originalHash,"explicit replacement after complete ACK");
                }else{
                    var saved=store.PrepareSend();Require(saved.Hash==originalHash&&saved.Value.revision==17&&(phase.EndsWith("null-group",StringComparison.Ordinal)?string.IsNullOrEmpty(saved.Value.requestId):saved.Value.requestId==Request),"exact original after process death");
                    bool blocked=false;try{store.SaveNew(Intent());}catch(IOException){blocked=true;}Require(blocked,"original blocks replacement");
                    if((phase=="recover-before-ack"||phase.StartsWith("recover-unpublished-",StringComparison.Ordinal))){store.VerifySavedSuccess(originalHash);store.AcknowledgeSavedSuccess();Require(!Directory.Exists(journal),"complete recovered ACK");}
                    else Require(!store.HasSuccess,"unconfirmed remains pending");
                }
                File.WriteAllText(Path.Combine(root,"verified"),phase);return;
            }
            store.SaveNew(Intent(phase));var original=store.PrepareSend();if(phase!="locked-intent"&&!phase.StartsWith("unpublished-",StringComparison.Ordinal))store.SaveSuccess(original.Hash,User,18);
            if(phase=="after-ack"){store.AcknowledgeSavedSuccess();Ready();}
            else typeof(GroupsSampleStore).GetMethod("WithLock",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(store,new object[]{(Action)(()=>{if(phase.StartsWith("unpublished-",StringComparison.Ordinal))WriteReceipt();Ready();})});
            void WriteReceipt(){var receipt=Activator.CreateInstance(typeof(GroupsSampleStore).GetNestedType("Success",BindingFlags.NonPublic));receipt.GetType().GetField("intentHash").SetValue(receipt,original.Hash);receipt.GetType().GetField("groupId").SetValue(receipt,phase.EndsWith("null-group",StringComparison.Ordinal)?null:User);receipt.GetType().GetField("playerRevision").SetValue(receipt,18L);string json=UnityEngine.JsonUtility.ToJson(receipt);typeof(GroupsSampleStore).GetMethod("WriteNew",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Path.Combine(root,"groups-"+Project+"-"+User+".json.journal","result.0123456789abcdef0123456789abcdef.writing"),json});}
            void Ready(){File.WriteAllText(Path.Combine(root,"ready"),original.Hash);using(var f=new FileStream(Path.Combine(root,"ready"),FileMode.Open,FileAccess.Write))f.Flush(true);while(true)Thread.Sleep(50);}
            string Value(string key){int i=Array.IndexOf(args,key);if(i<0||i+1>=args.Length)throw new Exception("Missing probe argument");return args[i+1];}
        }catch(Exception e){UnityEngine.Debug.LogException(e);throw;}
    }
    static GroupsSampleStore.Intent Intent(string phase="")=>new GroupsSampleStore.Intent{projectId=Project,userId=User,action=phase.EndsWith("null-group",StringComparison.Ordinal)?"leave":"accept-invite",groupId=User,requestId=phase.EndsWith("null-group",StringComparison.Ordinal)?null:Request,revision=17};
    static void Require(bool value,string name){if(!value)throw new Exception("Native crash recovery: "+name);}
}
