using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;
namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Durable original intent scoped by stable project/player UUID, never host/session.
    /// Unknown/corrupt state blocks replacements. Production games own transactional storage/retention.</summary>
    public sealed class GroupsSampleStore
    {
        [Serializable] public class Intent { public string projectId,userId,action,groupId,targetUserId,requestId,role,name,description,joinMode,dataJson; public long revision; }
        [Serializable] private class Envelope { public string payload,sha256; }
        [Serializable] private class Success { public string intentHash,groupId; public long playerRevision; }
        private readonly string _root,_directory,_path,_result,_legacy,_project,_user;
        private const string UuidPattern=@"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z";
        public GroupsSampleStore(string directory,string projectId,string userId)
        {
            if(projectId==null||userId==null||!Regex.IsMatch(projectId,UuidPattern)||!Regex.IsMatch(userId,UuidPattern))throw new ArgumentException("Stable project/player UUIDs required");
            _project=Guid.ParseExact(projectId,"D").ToString("D");_user=Guid.ParseExact(userId,"D").ToString("D");_root=directory;
            _legacy=Path.Combine(directory,"groups-"+_project+"-"+_user+".json");_directory=_legacy+".journal";
            _path=Path.Combine(_directory,"intent.json");_result=Path.Combine(_directory,"result.json");
        }
        public bool HasIntent=>File.Exists(_legacy)||File.Exists(_legacy+".success")||Directory.Exists(_directory)&&Directory.GetFileSystemEntries(_directory).Length!=0;
        public bool HasSuccess=>File.Exists(_result)||File.Exists(_legacy+".success");
        private Envelope ReadEnvelope(string path)
        {
            if(new FileInfo(path).Length>64*1024*1024)throw new IOException("Saved Groups intent exceeds transport frame");
            string raw=File.ReadAllText(path,Encoding.UTF8);if(Encoding.UTF8.GetByteCount(raw)>64*1024*1024)throw new IOException("Saved Groups intent exceeds transport frame");
            var e=JsonUtility.FromJson<Envelope>(raw);if(e?.payload==null||e.sha256!=Hash(e.payload))throw new IOException("Unknown/corrupt Groups intent; reconcile without replacing it");
            ParseIntent(e);return e;
        }
        private Intent ParseIntent(Envelope e)
        {
            var x=JsonUtility.FromJson<Intent>(e.payload);
            if(x==null||string.IsNullOrEmpty(x.action)||!string.Equals(x.projectId,_project,StringComparison.OrdinalIgnoreCase)||!string.Equals(x.userId,_user,StringComparison.OrdinalIgnoreCase))throw new IOException("Saved Groups intent scope is invalid");return x;
        }
        public Intent Load(){Intent result=null;WithLock(()=>result=ParseIntent(ReadEnvelope(_path)));return result;}
        public void SaveNew(Intent intent)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new NotSupportedException("Use a game-owned durable browser intent store before sending Groups mutations");
#else
            if(intent==null||!string.Equals(intent.projectId,_project,StringComparison.OrdinalIgnoreCase)||!string.Equals(intent.userId,_user,StringComparison.OrdinalIgnoreCase))throw new IOException("Groups intent scope mismatch");
            WithLock(()=>{
                if(Directory.Exists(_directory)&&Directory.GetFileSystemEntries(_directory).Length!=0)throw new IOException("Reconcile the original Groups intent first");
                Directory.CreateDirectory(_directory);var payload=JsonUtility.ToJson(intent);WriteNew(_path,JsonUtility.ToJson(new Envelope{payload=payload,sha256=Hash(payload)}));
            });
#endif
        }
        public sealed class PreparedIntent
        {
            public Intent Value {get;} public string Hash {get;} public bool HasSuccess {get;}
            internal PreparedIntent(Intent value,string hash,bool hasSuccess){Value=value;Hash=hash;HasSuccess=hasSuccess;}
        }
        /// <summary>Even a readable file from a failed earlier Flush is not permission to send.</summary>
        public PreparedIntent PrepareSend(){PreparedIntent prepared=null;WithLock(()=>prepared=PrepareCore());return prepared;}
        private PreparedIntent PrepareCore()
        {
            var original=ReadEnvelope(_path);DurableRead(_path);
            if(ReadEnvelope(_path).sha256!=original.sha256)throw new IOException("Groups original changed during durable verification");
            bool confirmed=File.Exists(_result);if(confirmed)ReadSuccess(original.sha256);
            return new PreparedIntent(ParseIntent(original),original.sha256,confirmed);
        }
        /// <summary>Exclusive ownership spans preparation, HTTP and result persistence, including awaits.</summary>
        public SendLease AcquireSend()
        {
            FileStream gate=OpenGate();try{MigrateLegacy();RecoverWritingResult();return new SendLease(this,gate,PrepareCore());}catch{gate.Dispose();throw;}
        }
        public sealed class SendLease:IDisposable
        {
            private GroupsSampleStore _owner;private FileStream _gate;
            public PreparedIntent Prepared {get;}
            internal SendLease(GroupsSampleStore owner,FileStream gate,PreparedIntent prepared){_owner=owner;_gate=gate;Prepared=prepared;}
            public void SaveSuccess(string groupId,long playerRevision){if(_owner==null)throw new ObjectDisposedException(nameof(SendLease));_owner.SaveSuccessCore(Prepared.Hash,groupId,playerRevision);}
            public void Dispose(){_owner=null;_gate?.Dispose();_gate=null;}
        }
        public void VerifySavedSuccess(string originalHash){WithLock(()=>ReadSuccess(originalHash));}
        private void ReadSuccess(string originalHash)
        {
            var receipt=JsonUtility.FromJson<Success>(DurableRead(_result));
            if(ReadEnvelope(_path).sha256!=originalHash||receipt==null||receipt.intentHash!=originalHash)throw new IOException("No matching saved Groups success");
        }
        public void SaveSuccess(string originalHash,string groupId,long playerRevision){WithLock(()=>SaveSuccessCore(originalHash,groupId,playerRevision));}
        private void SaveSuccessCore(string originalHash,string groupId,long playerRevision)
        {
            if(ReadEnvelope(_path).sha256!=originalHash)throw new IOException("Groups intent changed while awaiting response");
            string json=JsonUtility.ToJson(new Success{intentHash=originalHash,groupId=groupId,playerRevision=playerRevision});
            if(File.Exists(_result)){if(DurableRead(_result)!=json)throw new IOException("A different Groups result is already saved");return;}
            // Publish only a fully flushed result. Recovery verifies any interrupted temporary first.
            string temporary=Path.Combine(_directory,"result."+Guid.NewGuid().ToString("N")+".writing");
            WriteNew(temporary,json);File.Move(temporary,_result);DurableRead(_result);
        }
        /// <summary>Explicit acknowledgement archives the complete original/result pair with one rename; no HTTP.</summary>
        public void AcknowledgeSavedSuccess()
        {
            WithLock(()=>{
                var original=ReadEnvelope(_path);DurableRead(_path);ReadSuccess(original.sha256);
                Directory.Move(_directory,_directory+"."+original.sha256+"."+Guid.NewGuid().ToString("N")+".acknowledged");
            });
        }
        private FileStream OpenGate()
        {
            Directory.CreateDirectory(_root);
            // The inode persists. OS ownership ends on process death; never unlink another opener's lock.
            return new FileStream(_legacy+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        }
        private void WithLock(Action action){using(var gate=OpenGate()){MigrateLegacy();RecoverWritingResult();action();}}
        private void RecoverWritingResult()
        {
            if(!Directory.Exists(_directory))return;string[] temporary=Directory.GetFiles(_directory,"result.*.writing");if(temporary.Length==0)return;
            string hash=ReadEnvelope(_path).sha256,canonical=null;
            foreach(string path in temporary){
                string raw=DurableRead(path);Success value;
                try{value=JsonUtility.FromJson<Success>(raw);}catch(ArgumentException e){throw new IOException("Unresolved Groups result write; preserve before resending",e);}
                if(value==null||value.intentHash!=hash||value.playerRevision<0||value.playerRevision>9007199254740991L||!string.IsNullOrEmpty(value.groupId)&&!Regex.IsMatch(value.groupId,UuidPattern)||JsonUtility.ToJson(value)!=raw||canonical!=null&&canonical!=raw)
                    throw new IOException("Unresolved/conflicting Groups result write; preserve before resending");
                canonical=raw;
            }
            if(File.Exists(_result)){if(DurableRead(_result)!=canonical)throw new IOException("Groups saved result differs from interrupted write");}
            else{File.Move(temporary[0],_result);DurableRead(_result);}
        }
        private void MigrateLegacy()
        {
            bool intent=File.Exists(_legacy),success=File.Exists(_legacy+".success");if(!intent&&!success)return;
            string source=_legacy;
            if(!intent){
                var receipt=JsonUtility.FromJson<Success>(DurableRead(_legacy+".success"));
                if(receipt?.intentHash==null||!Regex.IsMatch(receipt.intentHash,@"\A[0-9a-f]{64}\z"))throw new IOException("Unknown legacy Groups result; preserve for reconciliation");
                source=File.Exists(_path)&&ReadEnvelope(_path).sha256==receipt.intentHash?_path:null;
                // Recover an old interrupted two-file ACK only from its exact owned original hash/scope.
                foreach(string candidate in Directory.GetFiles(_root,Path.GetFileName(_legacy)+"."+receipt.intentHash+".*.acknowledged")){
                    if(candidate.EndsWith(".success.acknowledged",StringComparison.Ordinal))continue;
                    if(ReadEnvelope(candidate).sha256!=receipt.intentHash)throw new IOException("Legacy Groups archive hash mismatch");
                    if(source!=null&&DurableRead(source)!=DurableRead(candidate))throw new IOException("Conflicting legacy Groups originals");source=candidate;
                }
                if(source==null)throw new IOException("Legacy Groups result has no known original; preserve without replacement");
            }
            var envelope=ReadEnvelope(source);string original=DurableRead(source),result=success?DurableRead(_legacy+".success"):null;
            if(result!=null&&JsonUtility.FromJson<Success>(result)?.intentHash!=envelope.sha256)throw new IOException("Legacy Groups original/result mismatch");
            if(!Directory.Exists(_directory)){
                string temporary=_directory+".migration-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(temporary);
                WriteNew(Path.Combine(temporary,"intent.json"),original);if(result!=null)WriteNew(Path.Combine(temporary,"result.json"),result);
                Directory.Move(temporary,_directory);
            }
            // Re-entry after any migration crash accepts exact duplicates only, never overwrites a journal.
            if(DurableRead(_path)!=original||result!=null&&(!File.Exists(_result)||DurableRead(_result)!=result))throw new IOException("Legacy and current Groups journals differ; preserve both");
            if(intent)MoveLegacy(_legacy,Path.Combine(_directory,"legacy-intent.json"));
            if(success)MoveLegacy(_legacy+".success",Path.Combine(_directory,"legacy-result.json"));
        }
        private static void MoveLegacy(string source,string destination)
        {
            if(File.Exists(destination))throw new IOException("Legacy Groups backup already exists; preserve both for reconciliation");
            File.Move(source,destination);DurableRead(destination);
        }
        private static string DurableRead(string path)
        {
            string original=File.ReadAllText(path,Encoding.UTF8);
            using(var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))file.Flush(true);
            if(File.ReadAllText(path,Encoding.UTF8)!=original)throw new IOException("Groups receipt changed during durable verification");return original;
        }
        private static void WriteNew(string path,string json)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(json);if(bytes.Length>64*1024*1024)throw new IOException("Groups intent exceeds limit");
            using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}
            if(File.ReadAllText(path,Encoding.UTF8)!=json)throw new IOException("Groups storage readback failed");
        }
        private static string Hash(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
    }
}
