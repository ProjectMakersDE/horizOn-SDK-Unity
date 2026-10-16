using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
namespace PM.horizOn.Cloud.Manager
{
    /// <summary>Explicit, serialized Groups calls. No poller, automatic pages or revision replacement. Every write is one attempt.</summary>
    public class GroupsManager : BaseManager<GroupsManager>
    {
        private const string Endpoint="/api/v1/app/groups";
        private readonly SemaphoreSlim _requests=new SemaphoreSlim(1,1);
        private NetworkService _network; private string _identity; private long _epoch;
        private MyGroupsView _current;
        private readonly Dictionary<string,long> _revisions=new Dictionary<string,long>(),_times=new Dictionary<string,long>();
        public string LastErrorCode {get;private set;}
        public MyGroupsView CurrentGroups {get{SyncIdentity();return GroupsJson.Clone(_current);}}
        protected override void RegisterEvents(){RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess,_=>ResetIdentity());RegisterEvent<UserData>(EventKeys.UserSignInSuccess,_=>SyncIdentity());}
        protected override void OnDisable(){ResetIdentity();base.OnDisable();}
        /// <summary>Realtime metadata only invalidates. Retain independent revision/time fences until identity changes.</summary>
        public void ClearCache(){SyncIdentity();_epoch++;_current=null;}
        public Task<GroupsResult<MyGroupsView>> GetGroups(string userId)=>Read<MyGroupsView>(userId,null,"",null,50);
        public Task<GroupsResult<GroupView>> GetGroup(string userId,string groupId)=>Read<GroupView>(userId,groupId,"",null,50);
        public Task<GroupsResult<GroupMembersView>> GetMembers(string userId,string groupId,string afterUserId=null,int limit=50)=>Read<GroupMembersView>(userId,groupId,"/members",afterUserId,limit);
        public Task<GroupsResult<GroupPendingSnapshot>> GetPending(string userId,string groupId)=>Read<GroupPendingSnapshot>(userId,groupId,"/pending",null,50);
        private async Task<GroupsResult<T>> Read<T>(string userId,string groupId,string suffix,string after,int limit)where T:class
        {
            if(!Begin(userId,out var identity,out var epoch))return Failure<T>(LastErrorCode);
            if((typeof(T)!=typeof(MyGroupsView)&&!FriendsJson.IsUuid(groupId))||(after!=null&&!FriendsJson.IsUuid(after))||limit<1||limit>50)return Failure<T>("INVALID_PAGE");
            string path=Endpoint+(groupId==null?"":"/"+Uri.EscapeDataString(groupId))+suffix+"?userId="+Uri.EscapeDataString(userId);
            if(typeof(T)==typeof(GroupMembersView))path+="&limit="+limit.ToString(CultureInfo.InvariantCulture)+(after==null?"":"&afterUserId="+Uri.EscapeDataString(after));
            await _requests.WaitAsync();try{
                if(!IsCurrent(identity,epoch))return Failure<T>("STALE_RESPONSE");
                var r=await HorizonApp.Network.GetAsync<T>(path,true);
                if(!IsCurrent(identity,epoch))return Failure<T>("STALE_RESPONSE");
                if(!r.IsSuccess)return Failure<T>(ErrorCode(r),r.Error);
                var revisions=new Dictionary<string,long>(_revisions);var times=new Dictionary<string,long>(_times);
                string error=Validate(r.Data,userId,groupId,after);
                if(error!=null){RestoreWatermarks(revisions,times);return Failure<T>(error);}
                if(r.Data is MyGroupsView mine)_current=GroupsJson.Clone(mine);
                LastErrorCode=null;return new GroupsResult<T>{IsSuccess=true,Data=GroupsJson.Clone(r.Data)};
            }finally{_requests.Release();}
        }
        public Task<GroupsResult<GroupMutationView>> CreateGroup(string userId,long expectedPlayerRevision,string name,string description="",string joinMode="open",GroupJsonData data=null)
            =>Metadata(userId,null,expectedPlayerRevision,name,description,joinMode,data??GroupJsonData.Empty);
        public Task<GroupsResult<GroupMutationView>> UpdateGroup(string userId,string groupId,long expectedRevision,string name,string description,string joinMode,GroupJsonData data)
        {
            if(!FriendsJson.IsUuid(groupId))return Task.FromResult(Failure<GroupMutationView>("INVALID_GROUP"));
            return Metadata(userId,groupId,expectedRevision,name,description,joinMode,data);
        }
        private Task<GroupsResult<GroupMutationView>> Metadata(string user,string group,long revision,string name,string description,string mode,GroupJsonData data)
        {
            if(!Begin(user,out _,out _))return Task.FromResult(Failure<GroupMutationView>(LastErrorCode));
            if(!GroupsJson.Revision(revision))return Task.FromResult(Failure<GroupMutationView>("INVALID_REVISION"));
            if(group!=null&&!FriendsJson.IsUuid(group)||string.IsNullOrWhiteSpace(name)||Encoding.UTF8.GetByteCount(name)>120||description==null||Encoding.UTF8.GetByteCount(description)>1024||Array.IndexOf(new[]{"open","request","invite"},mode)<0||data==null)return Task.FromResult(Failure<GroupMutationView>("INVALID_GROUP"));
            string json="{\"userId\":"+GroupsJson.Quote(user)+",\""+(group==null?"expectedPlayerRevision":"expectedRevision")+"\":"+revision.ToString(CultureInfo.InvariantCulture)+",\"name\":"+GroupsJson.Quote(name)+",\"description\":"+GroupsJson.Quote(description)+",\"joinMode\":"+GroupsJson.Quote(mode)+",\"data\":"+data.Json+"}";
            return Write(user,group,"",new GroupsJsonRequest(json),group!=null);
        }
        public Task<GroupsResult<GroupMutationView>> JoinGroup(string userId,string groupId,long expectedRevision)=>Action("join",userId,groupId,expectedRevision);
        public Task<GroupsResult<GroupMutationView>> RequestJoin(string userId,string groupId,long expectedRevision)=>Action("request",userId,groupId,expectedRevision);
        public Task<GroupsResult<GroupMutationView>> InvitePlayer(string userId,string groupId,long expectedRevision,string targetUserId)=>Action("invite",userId,groupId,expectedRevision,targetUserId);
        public Task<GroupsResult<GroupMutationView>> AcceptInvite(string userId,string groupId,long expectedRevision,string requestId)=>Action("accept-invite",userId,groupId,expectedRevision,null,requestId);
        public Task<GroupsResult<GroupMutationView>> DeclineInvite(string userId,string groupId,long expectedRevision,string requestId)=>Action("decline-invite",userId,groupId,expectedRevision,null,requestId);
        public Task<GroupsResult<GroupMutationView>> CancelInvite(string userId,string groupId,long expectedRevision,string targetUserId,string requestId)=>Action("cancel-invite",userId,groupId,expectedRevision,targetUserId,requestId);
        public Task<GroupsResult<GroupMutationView>> AcceptRequest(string userId,string groupId,long expectedRevision,string targetUserId,string requestId)=>Action("accept-request",userId,groupId,expectedRevision,targetUserId,requestId);
        public Task<GroupsResult<GroupMutationView>> DeclineRequest(string userId,string groupId,long expectedRevision,string targetUserId,string requestId)=>Action("decline-request",userId,groupId,expectedRevision,targetUserId,requestId);
        public Task<GroupsResult<GroupMutationView>> CancelRequest(string userId,string groupId,long expectedRevision,string requestId)=>Action("cancel-request",userId,groupId,expectedRevision,null,requestId);
        public Task<GroupsResult<GroupMutationView>> LeaveGroup(string userId,string groupId,long expectedRevision)=>Action("leave",userId,groupId,expectedRevision);
        public Task<GroupsResult<GroupMutationView>> RemoveMember(string userId,string groupId,long expectedRevision,string targetUserId)=>Action("remove",userId,groupId,expectedRevision,targetUserId);
        public Task<GroupsResult<GroupMutationView>> SetMemberRole(string userId,string groupId,long expectedRevision,string targetUserId,string role)=>Action("role",userId,groupId,expectedRevision,targetUserId,null,role);
        public Task<GroupsResult<GroupMutationView>> TransferLeadership(string userId,string groupId,long expectedRevision,string targetUserId)=>Action("transfer",userId,groupId,expectedRevision,targetUserId);
        public Task<GroupsResult<GroupMutationView>> DeleteGroup(string userId,string groupId,long expectedRevision)=>Action("delete",userId,groupId,expectedRevision);
        private Task<GroupsResult<GroupMutationView>> Action(string action,string user,string group,long revision,string target=null,string requestId=null,string role=null)
        {
            if(!Begin(user,out _,out _))return Task.FromResult(Failure<GroupMutationView>(LastErrorCode));
            bool targeted=Array.IndexOf(new[]{"invite","cancel-invite","accept-request","decline-request","remove","role","transfer"},action)>=0;
            bool pending=Array.IndexOf(new[]{"accept-invite","decline-invite","cancel-invite","accept-request","decline-request","cancel-request"},action)>=0;
            if(!FriendsJson.IsUuid(group)||!GroupsJson.Revision(revision)||targeted!= (target!=null)||pending!=(requestId!=null)||targeted&&(!FriendsJson.IsUuid(target)||string.Equals(user,target,StringComparison.OrdinalIgnoreCase))||pending&&!FriendsJson.IsUuid(requestId)||(action=="role")!=(role!=null)||role!=null&&role!="officer"&&role!="member")return Task.FromResult(Failure<GroupMutationView>("INVALID_GROUP_ACTION"));
            string json="{\"userId\":"+GroupsJson.Quote(user)+",\"expectedRevision\":"+revision.ToString(CultureInfo.InvariantCulture)+(target==null?"":",\"targetUserId\":"+GroupsJson.Quote(target))+(requestId==null?"":",\"requestId\":"+GroupsJson.Quote(requestId))+(role==null?"":",\"role\":"+GroupsJson.Quote(role))+"}";
            return Write(user,group,"/"+action,new GroupsJsonRequest(json),false);
        }
        private async Task<GroupsResult<GroupMutationView>> Write(string user,string group,string suffix,GroupsJsonRequest body,bool put)
        {
            if(!Begin(user,out var identity,out var epoch))return Failure<GroupMutationView>(LastErrorCode);
            string path=Endpoint+(group==null?"":"/"+Uri.EscapeDataString(group))+suffix;
            await _requests.WaitAsync();try{
                if(!IsCurrent(identity,epoch))return Failure<GroupMutationView>("STALE_RESPONSE");_current=null;
                var r=put?await HorizonApp.Network.PutOnceAsync<GroupMutationView>(path,body,true):await HorizonApp.Network.PostOnceAsync<GroupMutationView>(path,body,true);
                if(!IsCurrent(identity,epoch))return Failure<GroupMutationView>("STALE_RESPONSE");
                if(!r.IsSuccess)return Failure<GroupMutationView>(ErrorCode(r),r.Error);
                if(r.Data==null)return Failure<GroupMutationView>("INVALID_RESPONSE");
                var revisions=new Dictionary<string,long>(_revisions);var times=new Dictionary<string,long>(_times);
                var error=Validate(r.Data.groups,user,null,null);
                if(error==null&&r.Data.group!=null)error=Validate(r.Data.group,user,group??r.Data.group.id,null);
                if(error!=null){RestoreWatermarks(revisions,times);return Failure<GroupMutationView>(error);}
                _current=GroupsJson.Clone(r.Data.groups);LastErrorCode=null;return new GroupsResult<GroupMutationView>{IsSuccess=true,Data=GroupsJson.Clone(r.Data)};
            }finally{_requests.Release();}
        }
        private string Validate<T>(T data,string user,string group,string after)where T:class
        {
            if(data==null)return"INVALID_RESPONSE";string key,time;long revision;
            if(data is MyGroupsView m){if(!string.Equals(m.userId,user,StringComparison.OrdinalIgnoreCase))return"INVALID_RESPONSE";key="player";revision=m.revision;time=m.serverTime;}
            else if(data is GroupView g){if(!string.Equals(g.id,group,StringComparison.OrdinalIgnoreCase))return"INVALID_RESPONSE";key=g.id;revision=g.revision;time=g.updatedAt;}
            else if(data is GroupMembersView a){if(!string.Equals(a.groupId,group,StringComparison.OrdinalIgnoreCase)||a.nextAfterUserId!=null&&(string.Equals(a.nextAfterUserId,after,StringComparison.OrdinalIgnoreCase)||a.members.Length==0||string.CompareOrdinal(a.nextAfterUserId,after)<0))return"INVALID_RESPONSE";key=a.groupId;revision=a.revision;time=a.serverTime;}
            else if(data is GroupPendingSnapshot p){if(!string.Equals(p.groupId,group,StringComparison.OrdinalIgnoreCase))return"INVALID_RESPONSE";key=p.groupId;revision=p.revision;time=p.serverTime;}
            else return"INVALID_RESPONSE";
            if(!GroupsJson.Revision(revision)||!DateTimeOffset.TryParse(time,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var instant))return"INVALID_RESPONSE";
            long millis=instant.ToUnixTimeMilliseconds();
            // Detail updatedAt is a state timestamp, whereas page serverTime advances on every read.
            string clockKey=key+(data is GroupView?":updated":":server");
            if(_revisions.TryGetValue(key,out var old)&&revision<old||_times.TryGetValue(clockKey,out var previous)&&millis<previous)return"STALE_RESPONSE";
            _revisions[key]=revision;_times[clockKey]=millis;
            if(data is MyGroupsView mine)foreach(var nested in mine.groups){var error=Validate(nested,user,nested.id,null);if(error!=null)return error;}
            // Keep bounded historical fences; no data/page cache or background enumeration is created.
            if(_revisions.Count>64){string evict=null;foreach(var id in _revisions.Keys)if(id!="player"&&id!=key){evict=id;break;}if(evict!=null){_revisions.Remove(evict);_times.Remove(evict+":updated");_times.Remove(evict+":server");}}
            return null;
        }
        private void RestoreWatermarks(Dictionary<string,long> revisions,Dictionary<string,long> times){_revisions.Clear();_times.Clear();foreach(var x in revisions)_revisions.Add(x.Key,x.Value);foreach(var x in times)_times.Add(x.Key,x.Value);}
        private bool Begin(string user,out string identity,out long epoch)
        {SyncIdentity();identity=_identity;epoch=_epoch;var p=UserManager.Instance.CurrentUser;LastErrorCode=p==null||!p.IsValid()||string.IsNullOrEmpty(HorizonApp.Network.GetSessionToken())||p.AccessToken!=HorizonApp.Network.GetSessionToken()?"SESSION_REQUIRED":!string.Equals(p.UserId,user,StringComparison.OrdinalIgnoreCase)?"SESSION_FORBIDDEN":!FriendsJson.IsUuid(user)?"INVALID_GROUP_ACTION":null;return LastErrorCode==null;}
        private void SyncIdentity(){var n=HorizonApp.Network;string identity=UserManager.Instance.CurrentUser?.UserId+"|"+n.GetSessionToken()+"|"+n.GetApiKey()+"|"+n.GetActiveHost()+"|"+n.ContextGeneration;if(ReferenceEquals(_network,n)&&identity==_identity)return;ResetIdentity();_network=n;_identity=identity;}
        private void ResetIdentity(){_epoch++;_identity=null;_network=null;_current=null;_revisions.Clear();_times.Clear();LastErrorCode=null;}
        private bool IsCurrent(string identity,long epoch){SyncIdentity();return _identity==identity&&_epoch==epoch;}
        private static string ErrorCode<T>(NetworkResponse<T>r)where T:class=>r.ErrorCode??(r.StatusCode>=200&&r.StatusCode<300?"INVALID_RESPONSE":"HTTP_"+r.StatusCode);
        private GroupsResult<T> Failure<T>(string code,string error=null)where T:class{LastErrorCode=code;return new GroupsResult<T>{ErrorCode=code,Error=error??code};}
    }
}
