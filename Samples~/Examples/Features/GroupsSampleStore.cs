using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;
namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Original intent scoped by stable project/player UUID, never by host/session.
    /// Unknown/corrupt state blocks replacements. Production games can use their own transactional store.</summary>
    public sealed class GroupsSampleStore
    {
        [Serializable] public class Intent { public string projectId,userId,action,groupId,targetUserId,requestId,role,name,description,joinMode,dataJson; public long revision; }
        [Serializable] private class Envelope { public string payload,sha256; }
        [Serializable] private class Success { public string intentHash,groupId; public long playerRevision; }
        private readonly string _path,_project,_user;
        private const string UuidPattern=@"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z";
        public GroupsSampleStore(string directory,string projectId,string userId)
        {
            if(projectId==null||userId==null||!Regex.IsMatch(projectId,UuidPattern)||!Regex.IsMatch(userId,UuidPattern))throw new ArgumentException("Stable project/player UUIDs required");
            _project=Guid.ParseExact(projectId,"D").ToString("D");_user=Guid.ParseExact(userId,"D").ToString("D");_path=Path.Combine(directory,"groups-"+_project+"-"+_user+".json");
        }
        public bool HasIntent=>File.Exists(_path);
        private Envelope ReadEnvelope()
        {
            var raw=File.ReadAllText(_path,Encoding.UTF8);if(raw.Length>32768)throw new IOException("Saved Groups intent exceeds limit");
            var e=JsonUtility.FromJson<Envelope>(raw);if(e==null||e.payload==null||e.sha256!=Hash(e.payload))throw new IOException("Unknown/corrupt Groups intent; reconcile without replacing it");return e;
        }
        public Intent Load()
        {
            var value=JsonUtility.FromJson<Intent>(ReadEnvelope().payload);
            if(value==null||string.IsNullOrEmpty(value.action)||!string.Equals(value.projectId,_project,StringComparison.OrdinalIgnoreCase)||!string.Equals(value.userId,_user,StringComparison.OrdinalIgnoreCase))throw new IOException("Saved Groups intent scope is invalid");return value;
        }
        public void SaveNew(Intent intent)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new NotSupportedException("Use a game-owned durable browser intent store before sending Groups mutations");
#else
            if(!string.Equals(intent.projectId,_project,StringComparison.OrdinalIgnoreCase)||!string.Equals(intent.userId,_user,StringComparison.OrdinalIgnoreCase))throw new IOException("Groups intent scope mismatch");
            WithLock(()=>{
                if(File.Exists(_path)||File.Exists(_path+".success"))throw new IOException("Reconcile the original Groups intent first");
                var payload=JsonUtility.ToJson(intent);WriteNew(_path,JsonUtility.ToJson(new Envelope{payload=payload,sha256=Hash(payload)}));
            });
#endif
        }
        public sealed class PreparedIntent
        {
            public Intent Value {get;} public string Hash {get;}
            internal PreparedIntent(Intent value,string hash){Value=value;Hash=hash;}
        }
        /// <summary>Even a readable file from a failed earlier Flush is not permission to send.</summary>
        public PreparedIntent PrepareSend()
        {
            PreparedIntent prepared=null;WithLock(()=>{
                var original=ReadEnvelope();var value=Load();
                using(var file=new FileStream(_path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))file.Flush(true);
                if(ReadEnvelope().sha256!=original.sha256)throw new IOException("Groups original changed during durable verification");
                prepared=new PreparedIntent(value,original.sha256);
            });return prepared;
        }
        public void SaveSuccess(string originalHash,string groupId,long playerRevision)
        {
            WithLock(()=>{
                if(ReadEnvelope().sha256!=originalHash)throw new IOException("Groups intent changed while awaiting response");
                var receipt=new Success{intentHash=originalHash,groupId=groupId,playerRevision=playerRevision};var json=JsonUtility.ToJson(receipt);
                if(File.Exists(_path+".success")){if(DurableRead(_path+".success")!=json)throw new IOException("A different Groups result is already saved");return;}
                WriteNew(_path+".success",json);
            });
        }
        /// <summary>Explicit acknowledgement after the game has applied the saved success. No HTTP or replacement action.
        /// The example preserves acknowledged artifacts for inspection; production stores own their retention policy.</summary>
        public void AcknowledgeSavedSuccess()
        {
            WithLock(()=>{
                var original=ReadEnvelope();DurableRead(_path);var receipt=JsonUtility.FromJson<Success>(DurableRead(_path+".success"));
                if(receipt==null||receipt.intentHash!=original.sha256)throw new IOException("No matching saved Groups success");
                // An interruption between renames leaves a success marker, which blocks SaveNew.
                // Local archive identity only; this never changes any gameplay request/UUID/revision.
                string archive=_path+"."+original.sha256+"."+Guid.NewGuid().ToString("N");
                File.Move(_path,archive+".acknowledged");
                File.Move(_path+".success",archive+".success.acknowledged");
            });
        }
        private void WithLock(Action action)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));bool owned=false;
            try{using(var gate=new FileStream(_path+".lock",FileMode.CreateNew,FileAccess.Write,FileShare.None)){owned=true;action();}}
            finally{if(owned)File.Delete(_path+".lock");}
        }
        private static string DurableRead(string path)
        {
            string original=File.ReadAllText(path,Encoding.UTF8);
            using(var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))file.Flush(true);
            if(File.ReadAllText(path,Encoding.UTF8)!=original)throw new IOException("Groups receipt changed during durable verification");return original;
        }
        private static void WriteNew(string path,string json)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(json);if(bytes.Length>32768)throw new IOException("Groups intent exceeds limit");
            using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}
            if(File.ReadAllText(path,Encoding.UTF8)!=json)throw new IOException("Groups storage readback failed");
        }
        private static string Hash(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
    }
}
