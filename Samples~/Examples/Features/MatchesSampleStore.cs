using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Game-owned sample storage. Stable project key/player scope survives regional changes.
    /// One OS handle owns original preparation, HTTP, result and exact reviewed acknowledgement.</summary>
    public sealed class MatchesSampleStore
    {
        [Serializable] public class Intent
        {
            public string projectId,userId,action,matchId,nameBase64,visibility="open",timeoutPolicy="skip",dataBase64="";
            public long revision,playerRevision,strength; public int capacity=2,turnSeconds=3600; public bool ranked,finish;
            public string[] ids=Array.Empty<string>();
            public MatchCreateInput GetCreateInput()=>new MatchCreateInput{name=Utf8.GetString(Convert.FromBase64String(nameBase64)),capacity=capacity,visibility=visibility,strength=strength,turnSeconds=turnSeconds,timeoutPolicy=timeoutPolicy,ranked=ranked,data=GetBytes(),inviteeIds=(string[])ids.Clone()};
            public byte[] GetBytes()=>Convert.FromBase64String(dataBase64);
        }
        [Serializable] private class Envelope{public string payload,hash;}
        [Serializable] private class Sent{public string intentHash;}
        [Serializable] public class Submission{public string intentHash,runId,inputLogBase64;public long score;}
        [Serializable] private class RunBinding{public string intentHash,runId;}
        [Serializable] public class Receipt{public string intentHash,matchId,resolution;public long revision,playerRevision;public bool serverConfirmed,missing;}
        public sealed class PreparedIntent
        {
            public Intent Value{get;} public string Hash{get;} public bool HasSuccess{get;} public bool WasSent{get;}
            internal PreparedIntent(Intent value,string hash,bool success,bool sent){Value=value;Hash=hash;HasSuccess=success;WasSent=sent;}
        }
        private static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        private readonly string _root,_directory,_path,_result,_sent,_project,_user;
        public MatchesSampleStore(string root,string projectId,string userId,string apiKey)
        {
            if(!Uuid(projectId)||!Uuid(userId)||string.IsNullOrEmpty(apiKey))throw new ArgumentException("Stable project/player/key required");
            _root=root;_project=projectId.ToLowerInvariant();_user=userId.ToLowerInvariant();_directory=Path.Combine(root,FileName(apiKey,_user));_path=Path.Combine(_directory,"intent.json");_result=Path.Combine(_directory,"result.json");_sent=Path.Combine(_directory,"sent.json");
        }
        public static string FileName(string apiKey,string user)=>"matches-"+Hash(apiKey)+"-"+user.ToLowerInvariant()+".journal";
        public static string Hash(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        public bool HasIntent=>Directory.Exists(_directory)&&Directory.GetFileSystemEntries(_directory).Length!=0;
        public static Intent NewCreate(string project,string user,long playerRevision,MatchCreateInput input)
        {
            if(input==null||input.name==null||input.data==null||input.data.Length>MatchLimits.MaxStateBytes||input.inviteeIds==null||input.inviteeIds.Length>7)throw new IOException("Bounded complete create input required");
            return new Intent{projectId=project.ToLowerInvariant(),userId=user.ToLowerInvariant(),action="create",revision=playerRevision,playerRevision=playerRevision,nameBase64=Convert.ToBase64String(Utf8.GetBytes(input.name)),capacity=input.capacity,visibility=input.visibility,strength=input.strength,turnSeconds=input.turnSeconds,timeoutPolicy=input.timeoutPolicy,ranked=input.ranked,dataBase64=Convert.ToBase64String(input.data),ids=Array.ConvertAll(input.inviteeIds,x=>x?.ToLowerInvariant())};
        }
        public static Intent NewAction(string project,string user,string action,string match,long revision,long playerRevision,byte[] data=null,string[] ids=null,bool finish=false)
            =>new Intent{projectId=project.ToLowerInvariant(),userId=user.ToLowerInvariant(),action=action,matchId=match?.ToLowerInvariant(),revision=revision,playerRevision=playerRevision,dataBase64=Convert.ToBase64String(data??Array.Empty<byte>()),ids=ids==null?Array.Empty<string>():Array.ConvertAll(ids,x=>x?.ToLowerInvariant()),finish=finish};
        public void SaveNew(Intent value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new NotSupportedException("Supply game-owned durable browser storage before Matches mutations");
#else
            Validate(value);WithLock(()=>{if(HasIntent)throw new IOException("Reconcile original Matches intent first");Directory.CreateDirectory(_directory);string payload=JsonUtility.ToJson(value);WriteNew(_path,JsonUtility.ToJson(new Envelope{payload=payload,hash=Hash(payload)}));});
#endif
        }
        public SendLease AcquireSend(){var gate=OpenGate();try{RecoverWritingResult();return new SendLease(this,gate,ReadIntent());}catch{gate.Dispose();throw;}}
        public sealed class SendLease:IDisposable
        {
            private MatchesSampleStore _owner;private FileStream _gate;public PreparedIntent Prepared{get;}
            internal SendLease(MatchesSampleStore owner,FileStream gate,PreparedIntent prepared){_owner=owner;_gate=gate;Prepared=prepared;}
            private MatchesSampleStore Owner=>_owner??throw new ObjectDisposedException(nameof(SendLease));
            public void MarkSent()
            {
                var owner=Owner;if(Prepared.HasSuccess||Prepared.Value.action=="create"&&(Prepared.WasSent||File.Exists(owner._sent)))throw new IOException("Unknown create or saved result cannot be resent");
                if(!File.Exists(owner._sent))WriteNew(owner._sent,JsonUtility.ToJson(new Sent{intentHash=Prepared.Hash}));else owner.VerifySent(Prepared.Hash);
            }
            public void SaveSuccess(string match,long revision,long playerRevision=0)=>Owner.WriteReceipt(new Receipt{intentHash=Prepared.Hash,matchId=match,revision=revision,playerRevision=playerRevision,serverConfirmed=true,resolution="server-confirmed"});
            public void SaveDefinitiveCreateRejection(string code)
            {
                if(Prepared.Value.action!="create"||Prepared.WasSent||!DefinitiveCreateCode(code))throw new IOException("Only the first explicitly rejected create may be resolved");
                Owner.WriteReceipt(new Receipt{intentHash=Prepared.Hash,revision=Prepared.Value.revision,playerRevision=Prepared.Value.playerRevision,resolution="rejected:"+code});
            }
            public void SaveRankedTerminal(string code)
            {
                if(Prepared.Value.action!="ranked"||(code!="RUN_ALREADY_USED"&&code!="RUN_EXPIRED"))throw new IOException("Original terminal result required");
                Owner.WriteReceipt(new Receipt{intentHash=Prepared.Hash,matchId=Prepared.Value.matchId,revision=Prepared.Value.revision,resolution=code});
            }
            public Submission GetSubmission()
            {
                string path=Path.Combine(Owner._directory,"submission.json");if(!File.Exists(path))return null;string raw=DurableRead(path);var x=Parse<Submission>(raw);
                if(x?.intentHash!=Prepared.Hash||x.runId!=GetRunId()||!Safe(x.score)||x.inputLogBase64==null||JsonUtility.ToJson(x)!=raw)throw new IOException("Unknown original ranked submission");
                byte[] b=Convert.FromBase64String(x.inputLogBase64);if(b.Length>32768||Convert.ToBase64String(b)!=x.inputLogBase64)throw new IOException("Original ranked input-log bound");return x;
            }
            public void SaveSubmission(long score,byte[] log)
            {
                string run=GetRunId();if(run==null||Prepared.Value.action!="ranked"||log==null||log.Length>32768||!Safe(score))throw new IOException("Bounded original ranked log/run required");
                string raw=JsonUtility.ToJson(new Submission{intentHash=Prepared.Hash,runId=run,score=score,inputLogBase64=Convert.ToBase64String(log)});var old=GetSubmission();
                if(old!=null){if(JsonUtility.ToJson(old)!=raw)throw new IOException("Original ranked submit cannot change");return;}
                WriteNew(Path.Combine(Owner._directory,"submission.json"),raw);
            }
            public void SaveReconciled(long revision,long playerRevision,bool missing=false)=>Owner.WriteReceipt(new Receipt{intentHash=Prepared.Hash,matchId=Prepared.Value.matchId,revision=revision,playerRevision=playerRevision,missing=missing,resolution="reviewed-original-fenced"});
            public string GetRunId()
            {
                string p=Path.Combine(Owner._directory,"run.json");if(!File.Exists(p))return null;string raw=DurableRead(p);var r=Parse<RunBinding>(raw);
                if(r?.intentHash!=Prepared.Hash||!Uuid(r.runId)||JsonUtility.ToJson(r)!=raw)throw new IOException("Unknown ranked-run binding");return r.runId;
            }
            public void SaveRunId(string id)
            {
                if(Prepared.Value.action!="ranked"||!Uuid(id))throw new IOException("Invalid original ranked run");string old=GetRunId();if(old!=null){if(old!=id)throw new IOException("Original ranked run changed");return;}
                WriteNew(Path.Combine(Owner._directory,"run.json"),JsonUtility.ToJson(new RunBinding{intentHash=Prepared.Hash,runId=id}));
            }
            public void Acknowledge(string reviewedHash)
            {
                var owner=Owner;if(reviewedHash!=Prepared.Hash)throw new IOException("Review belongs to another Matches original");owner.ReadReceipt(DurableRead(owner._result),Prepared);
                // Retire the proven original atomically before deleting only this new terminal
                // directory. Existing *.acknowledged archives remain untouched for recovery.
                string terminal=owner._directory+"."+Prepared.Hash+"."+Guid.NewGuid().ToString("N")+".completed";
                Directory.Move(owner._directory,terminal); Directory.Delete(terminal,true);
            }
            public void Dispose(){_owner=null;_gate?.Dispose();_gate=null;}
        }
        public Receipt ReadSuccess(){Receipt value=null;WithLock(()=>{var p=ReadIntent();value=ReadReceipt(DurableRead(_result),p);});return value;}
        private PreparedIntent ReadIntent()
        {
            string raw=DurableRead(_path);var e=Parse<Envelope>(raw);
            if(e?.payload==null||e.hash!=Hash(e.payload)||JsonUtility.ToJson(e)!=raw)throw new IOException("Corrupt original Matches envelope");
            var x=Parse<Intent>(e.payload);Validate(x);if(JsonUtility.ToJson(x)!=e.payload)throw new IOException("Noncanonical original Matches payload");
            if(File.Exists(_sent))VerifySent(e.hash);var p=new PreparedIntent(x,e.hash,File.Exists(_result),File.Exists(_sent));if(p.HasSuccess)ReadReceipt(DurableRead(_result),p);return p;
        }
        private void Validate(Intent x)
        {
            if(x==null||x.projectId!=_project||x.userId!=_user||!Safe(x.revision)||!Safe(x.playerRevision)||x.revision<0||x.playerRevision<0||!Safe(x.strength)||x.ids==null||x.ids.Length>8||x.ids.Any(i=>!Uuid(i)||i!=i.ToLowerInvariant())||x.ids.Distinct().Count()!=x.ids.Length
                ||!new[]{"create","join","decline","start","turn","timeout","leave","cancel","report","ranked"}.Contains(x.action)||x.action!="create"&&(!Uuid(x.matchId)||x.revision==0))throw new IOException("Original Matches scope/parameters invalid");
            try
            {
                byte[] b=x.GetBytes();if(Convert.ToBase64String(b)!=x.dataBase64||b.Length>(x.action=="report"?256:MatchLimits.MaxStateBytes))throw new IOException("Original Matches bytes invalid");
                if(x.action=="create")
                {
                    var i=x.GetCreateInput();string name=i.name;
                    if(Convert.ToBase64String(Utf8.GetBytes(name))!=x.nameBase64||Utf8.GetByteCount(name)>120||Blank(name)||i.capacity<2||i.capacity>8||i.turnSeconds<60||i.turnSeconds>604800||i.visibility!="open"&&i.visibility!="private"||i.timeoutPolicy!="skip"&&i.timeoutPolicy!="forfeit"||i.inviteeIds.Length>=i.capacity||i.inviteeIds.Contains(_user)||x.revision!=x.playerRevision)throw new IOException("Original create invalid");
                }
                if(x.action=="start"&&x.ids.Length<2||x.action!="ranked"&&x.action!="report"&&x.revision==9007199254740991)throw new IOException("Original Matches revision/order invalid");
            }
            catch(Exception e)when(e is FormatException||e is DecoderFallbackException||e is ArgumentNullException){throw new IOException("Original Matches encoding invalid",e);}
        }
        private Receipt ReadReceipt(string raw,PreparedIntent p)
        {
            var r=Parse<Receipt>(raw);var x=p.Value;
            if(r==null||r.intentHash!=p.Hash||JsonUtility.ToJson(r)!=raw||!Safe(r.revision)||r.revision<0||!Safe(r.playerRevision)||r.playerRevision<0)throw new IOException("Corrupt/partial Matches result");
            if(r.serverConfirmed)
            {
                if(r.resolution!="server-confirmed"||!Uuid(r.matchId)||x.action!="create"&&r.matchId!=x.matchId||x.action=="create"&&(r.revision!=1||r.playerRevision!=x.revision+1)||x.action!="create"&&x.action!="report"&&x.action!="ranked"&&r.revision!=x.revision+1||x.action=="report"&&r.revision<x.revision||x.action=="ranked"&&r.revision!=x.revision)throw new IOException("Matches result contradicts original");
            }
            else if(x.action=="create"&&r.resolution!=null&&r.resolution.StartsWith("rejected:",StringComparison.Ordinal))
            {
                if(!DefinitiveCreateCode(r.resolution.Substring(9))||r.revision!=x.revision||r.playerRevision!=x.playerRevision||!string.IsNullOrEmpty(r.matchId)||r.missing)throw new IOException("Invalid rejected-create receipt");
            }
            else if(x.action=="ranked")
            {
                if((r.resolution!="RUN_ALREADY_USED"&&r.resolution!="RUN_EXPIRED")||r.revision!=x.revision||r.matchId!=x.matchId)throw new IOException("Original ranked terminal proof required");
            }
            else if(r.resolution!="reviewed-original-fenced"||x.action=="ranked"||x.action=="report"||x.action=="create"&&r.playerRevision<=x.revision||x.action!="create"&&!r.missing&&r.revision<=x.revision||r.missing&&((x.action!="leave"&&x.action!="decline")||r.playerRevision<=x.playerRevision))throw new IOException("Original Matches command is not permanently fenced");
            return r;
        }
        private void VerifySent(string hash){string raw=DurableRead(_sent);var s=Parse<Sent>(raw);if(s?.intentHash!=hash||JsonUtility.ToJson(s)!=raw)throw new IOException("Unknown partial send marker");}
        private void WriteReceipt(Receipt r)
        {
            var p=ReadIntent();string raw=JsonUtility.ToJson(r);ReadReceipt(raw,p);if(File.Exists(_result)){if(DurableRead(_result)!=raw)throw new IOException("Conflicting Matches result");return;}
            string temp=Path.Combine(_directory,"result."+Guid.NewGuid().ToString("N")+".writing");WriteNew(temp,raw);File.Move(temp,_result);DurableRead(_result);
        }
        private void RecoverWritingResult()
        {
            if(!Directory.Exists(_directory))return;string[] files=Directory.GetFiles(_directory,"result.*.writing");if(files.Length==0)return;var p=ReadIntent();string canonical=null;
            foreach(string file in files){string raw=DurableRead(file);ReadReceipt(raw,p);if(canonical!=null&&canonical!=raw)throw new IOException("Conflicting interrupted Matches results");canonical=raw;}
            if(File.Exists(_result)){if(DurableRead(_result)!=canonical)throw new IOException("Interrupted Matches result differs");}else{File.Move(files[0],_result);DurableRead(_result);}
        }
        private FileStream OpenGate(){Directory.CreateDirectory(_root);return new FileStream(_directory+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        private void WithLock(Action action){using(var gate=OpenGate()){RecoverWritingResult();action();}}
        private static void WriteNew(string path,string raw){byte[] b=Utf8.GetBytes(raw);if(b.Length>32*1024*1024)throw new IOException("Matches journal bound");using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(b,0,b.Length);f.Flush(true);}if(DurableRead(path)!=raw)throw new IOException("Matches durable readback changed");}
        private static string DurableRead(string path){if(new FileInfo(path).Length>32*1024*1024)throw new IOException("Matches journal bound");string raw=File.ReadAllText(path,Utf8);using(var f=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))f.Flush(true);if(File.ReadAllText(path,Utf8)!=raw)throw new IOException("Matches journal changed");return raw;}
        private static T Parse<T>(string raw)
        {
            try{return JsonUtility.FromJson<T>(raw);}catch(Exception e){throw new IOException("Corrupt/partial Matches JSON",e);}
        }
        private static bool Uuid(string s)=>s!=null&&Regex.IsMatch(s,@"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z");
        public static bool DefinitiveCreateCode(string code)=>new[]{"INVALID_REQUEST","STATE_SIZE_LIMIT_REACHED","ACTIVE_MATCH_LIMIT_REACHED","HISTORY_LIMIT_REACHED","MATCH_RELATION_BLOCKED","INVITE_LIMIT_REACHED","RANKED_NOT_CONFIGURED","PLAYER_NOT_FOUND","FEATURE_NOT_SUPPORTED","SESSION_REQUIRED","SESSION_FORBIDDEN"}.Contains(code);
        private static bool Safe(long n)=>n>=-9007199254740991L&&n<=9007199254740991L;
        private static bool Blank(string s)=>s==null||s.All(c=>c>='\t'&&c<='\r'||c>=0x1c&&c<=0x1f||char.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.SpaceSeparator||char.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.LineSeparator||char.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.ParagraphSeparator);
    }
}
