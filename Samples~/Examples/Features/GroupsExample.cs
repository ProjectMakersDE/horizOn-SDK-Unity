using System;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;
namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Explicit two-player screen/button actions for an existing session. No signup or poller.
    /// Refresh own state, review one group, persist one exact command, then send once. A second player
    /// refreshes their own invitations and reviews the same group before accepting its original ID.
    /// Leaders transfer to an existing member before leave; full Inbox/conflict keeps the saved intent.</summary>
    public class GroupsExample:MonoBehaviour
    {
        [SerializeField] private string projectId;
        private int _generation,_reviewSelection,_pageSelection,_reviewGeneration;private GroupView _reviewed;private GroupMembersView _page;
        private NetworkService _reviewNetwork;private long _reviewContext;private string _reviewUser,_reviewProject;
        private string User()=>isActiveAndEnabled&&UserManager.Instance.CurrentUser?.IsValid()==true&&UserManager.Instance.CurrentUser.AccessToken==HorizonApp.Network.GetSessionToken()?UserManager.Instance.CurrentUser.UserId:null;
        private bool Current(string user,NetworkService network,long context,int generation)=>User()==user&&ReferenceEquals(network,HorizonApp.Network)&&context==network.ContextGeneration&&generation==_generation;
        public async Task Refresh()
        {
            var user=User();if(user==null)return;var n=HorizonApp.Network;long c=n.ContextGeneration;int g=_generation;
            var r=await GroupsManager.Instance.GetGroups(user);if(!Current(user,n,c,g))return;
            HorizonApp.Log.Info(r.IsSuccess?"Own memberships: "+r.Data.groups.Length+", original player revision: "+r.Data.revision:"Groups: "+r.ErrorCode);
        }
        public async Task ReviewGroup(string groupId)
        {
            int selection=++_reviewSelection;_pageSelection++;_reviewed=null;_page=null;var user=User();if(user==null)return;var n=HorizonApp.Network;long c=n.ContextGeneration;int g=_generation;string project=projectId;
            var r=await GroupsManager.Instance.GetGroup(user,groupId);if(!Current(user,n,c,g)||selection!=_reviewSelection||project!=projectId)return;
            if(r.IsSuccess)SetReviewed(r.Data,user,n,c,g);else HorizonApp.Log.Warning(r.ErrorCode);
        }
        public Task ReadOneMemberPage(bool next=false)=>ReadOneMemberPageWithLimit(50,next);
        public async Task ReadOneMemberPageWithLimit(int limit,bool next=false)
        {
            var user=User();if(user==null||!ReviewedCurrent()||next&&(_page==null||_page.nextAfterUserId==null))return;
            string group=_reviewed.id,cursor=next?_page.nextAfterUserId:null;long revision=_reviewed.revision;int selection=++_pageSelection,reviewSelection=_reviewSelection;_page=null;
            var n=HorizonApp.Network;long c=n.ContextGeneration;int g=_generation;var r=await GroupsManager.Instance.GetMembers(user,group,cursor,limit);
            if(!Current(user,n,c,g)||!ReviewedCurrent()||_reviewed?.id!=group||selection!=_pageSelection||reviewSelection!=_reviewSelection)return;
            if(!r.IsSuccess||r.Data.revision!=revision){HorizonApp.Log.Warning("Review the group and explicitly restart pages; "+r.ErrorCode);return;}_page=r.Data;
        }
        public async Task SaveCreateAfterReview(string name,string description,string joinMode,GroupJsonData data)
        {
            var user=User();var state=GroupsManager.Instance.CurrentGroups;if(user==null||state==null||state.userId!=user||!MetadataValid(name,description,joinMode,data??GroupJsonData.Empty))return;
            await SaveAndApply(new GroupsSampleStore.Intent{projectId=projectId,userId=user,action="create",revision=state.revision,name=name,description=description,joinMode=joinMode,dataJson=(data??GroupJsonData.Empty).Json});
        }
        public async Task SaveActionAfterReview(string action,string targetUserId=null,string requestId=null,string role=null)
        {
            var user=User();if(user==null||!ReviewedCurrent())return;
            bool targeted=new[]{"invite","cancel-invite","accept-request","decline-request","remove","role","transfer"}.Contains(action);
            bool pending=new[]{"accept-invite","decline-invite","cancel-invite","accept-request","decline-request","cancel-request"}.Contains(action);
            if(!new[]{"join","request","invite","accept-invite","decline-invite","cancel-invite","accept-request","decline-request","cancel-request","leave","remove","role","transfer","delete"}.Contains(action)||targeted!=(targetUserId!=null)||targeted&&(!Uuid(targetUserId)||targetUserId==user)||pending!=(requestId!=null)||pending&&!Uuid(requestId)||(action=="role")!=(role!=null)||role!=null&&role!="officer"&&role!="member")return;
            string group=_reviewed.id;long revision=_reviewed.revision;
            // A NEW command selects a current original pending entry, never an old Inbox notice.
            // A previously saved command bypasses this selection and keeps its original ID on replay.
            if(pending){
                if(action=="accept-invite"||action=="decline-invite"||action=="cancel-request"){
                    var mine=GroupsManager.Instance.CurrentGroups;if(mine==null||mine.userId!=user)return;
                    var entries=action=="cancel-request"?mine.outgoingRequests:mine.incomingInvites;
                    if(!entries.Any(x=>x.groupId==group&&x.requestId==requestId&&x.group!=null&&x.group.revision==revision))return;
                }else{
                    var n=HorizonApp.Network;long c=n.ContextGeneration;int g=_generation;
                    var r=await GroupsManager.Instance.GetPending(user,group);
                    if(!Current(user,n,c,g)||!ReviewedCurrent()||_reviewed?.id!=group||_reviewed.revision!=revision||!r.IsSuccess||r.Data.revision!=revision)return;
                    var entries=action=="cancel-invite"?r.Data.invitations:r.Data.joinRequests;
                    if(!entries.Any(x=>x.userId==targetUserId&&x.requestId==requestId))return;
                }
            }
            await SaveAndApply(new GroupsSampleStore.Intent{projectId=projectId,userId=user,action=action,groupId=_reviewed.id,revision=_reviewed.revision,targetUserId=targetUserId,requestId=requestId,role=role});
        }
        public async Task SaveMetadataAfterReview(string name,string description,string joinMode,GroupJsonData data)
        {
            var user=User();if(user==null||!ReviewedCurrent()||!MetadataValid(name,description,joinMode,data))return;
            await SaveAndApply(new GroupsSampleStore.Intent{projectId=projectId,userId=user,action="update",groupId=_reviewed.id,revision=_reviewed.revision,name=name,description=description,joinMode=joinMode,dataJson=data.Json});
        }
        private async Task SaveAndApply(GroupsSampleStore.Intent intent)
        {
            // The exclusive journal lock and durable flush refuse replacement, even after restart.
            var store=new GroupsSampleStore(Application.persistentDataPath,projectId,intent.userId);store.SaveNew(intent);await ApplySavedIntent();
        }
        public async Task ApplySavedIntent()
        {
            var user=User();if(user==null)return;var store=new GroupsSampleStore(Application.persistentDataPath,projectId,user);if(!store.HasIntent)return;
            using(var lease=store.AcquireSend()){var prepared=lease.Prepared;if(prepared.HasSuccess){HorizonApp.Log.Info("Confirmed Groups result is saved; explicitly acknowledge it without resending");return;}var x=prepared.Value;if(x.projectId!=projectId||x.userId!=user)return;string originalHash=prepared.Hash,originalProject=x.projectId;
            var n=HorizonApp.Network;long c=n.ContextGeneration;int g=_generation,selection=_reviewSelection;var manager=GroupsManager.Instance;GroupsResult<GroupMutationView> r;
            switch(x.action){
                case"create":r=await manager.CreateGroup(user,x.revision,x.name,x.description,x.joinMode,GroupJsonData.FromJson(x.dataJson));break;
                case"update":r=await manager.UpdateGroup(user,x.groupId,x.revision,x.name,x.description,x.joinMode,GroupJsonData.FromJson(x.dataJson));break;
                case"join":r=await manager.JoinGroup(user,x.groupId,x.revision);break;case"request":r=await manager.RequestJoin(user,x.groupId,x.revision);break;
                case"invite":r=await manager.InvitePlayer(user,x.groupId,x.revision,x.targetUserId);break;
                case"accept-invite":r=await manager.AcceptInvite(user,x.groupId,x.revision,x.requestId);break;case"decline-invite":r=await manager.DeclineInvite(user,x.groupId,x.revision,x.requestId);break;
                case"cancel-invite":r=await manager.CancelInvite(user,x.groupId,x.revision,x.targetUserId,x.requestId);break;
                case"accept-request":r=await manager.AcceptRequest(user,x.groupId,x.revision,x.targetUserId,x.requestId);break;case"decline-request":r=await manager.DeclineRequest(user,x.groupId,x.revision,x.targetUserId,x.requestId);break;
                case"cancel-request":r=await manager.CancelRequest(user,x.groupId,x.revision,x.requestId);break;
                case"leave":r=await manager.LeaveGroup(user,x.groupId,x.revision);break;case"remove":r=await manager.RemoveMember(user,x.groupId,x.revision,x.targetUserId);break;
                case"role":r=await manager.SetMemberRole(user,x.groupId,x.revision,x.targetUserId,x.role);break;case"transfer":r=await manager.TransferLeadership(user,x.groupId,x.revision,x.targetUserId);break;case"delete":r=await manager.DeleteGroup(user,x.groupId,x.revision);break;
                default:HorizonApp.Log.Warning("Unknown saved action; keep original file");return;
            }
            if(!Current(user,n,c,g))return;
            if(!r.IsSuccess){HorizonApp.Log.Warning("Keep original command and reconcile explicitly: "+r.ErrorCode);return;}
            lease.SaveSuccess(r.Data.group?.id,r.Data.groups.revision);
            if(projectId!=originalProject){_reviewed=null;_page=null;_reviewSelection++;return;}
            if(selection==_reviewSelection)SetReviewed(r.Data.group,user,n,c,g);HorizonApp.Log.Info("Groups revision "+r.Data.groups.revision);
            // Show success and let the game/player explicitly acknowledge the durably saved receipt.
            // This example never archives a pending command automatically.
            }
        }
        private void SetReviewed(GroupView value,string user,NetworkService network,long context,int generation){_reviewed=value;_page=null;_pageSelection++;_reviewUser=user;_reviewNetwork=network;_reviewContext=context;_reviewGeneration=generation;_reviewProject=projectId;}
        private bool ReviewedCurrent()
        {
            if(_reviewed!=null&&_reviewProject==projectId&&Current(_reviewUser,_reviewNetwork,_reviewContext,_reviewGeneration))return true;
            _reviewed=null;_page=null;return false;
        }
        public void AcknowledgeSavedSuccess(){var user=User();if(user!=null)new GroupsSampleStore(Application.persistentDataPath,projectId,user).AcknowledgeSavedSuccess();}
        private static bool Uuid(string value)=>value!=null&&Regex.IsMatch(value,@"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z");
        private static bool MetadataValid(string name,string description,string mode,GroupJsonData data)=>!string.IsNullOrWhiteSpace(name)&&description!=null&&new[]{"open","request","invite"}.Contains(mode)&&data!=null;
        private void OnDisable(){_generation++;_reviewSelection++;_pageSelection++;_reviewed=null;_page=null;}
    }
}
