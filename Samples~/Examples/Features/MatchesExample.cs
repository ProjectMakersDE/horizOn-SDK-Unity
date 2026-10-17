using System;
using System.Linq;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Explicit UI/button actions for two existing signed-in players. No guest creation, polling,
    /// automatic deadline advancement, pages, report, ranked start or client-authoritative award.
    /// Restore an existing anonymous identity first; failed restore must never create a replacement player.</summary>
    public class MatchesExample:MonoBehaviour
    {
        [SerializeField] private string projectId;
        private MyMatches _ledger; private MatchView _selected; private string _ledgerScope,_selectedScope,_reviewedHash,_reviewedScope;
        private string _ledgerIdentity,_selectedIdentity;
        private int _generation; private bool _busy;
        private string User=>isActiveAndEnabled&&UserManager.Instance.CurrentUser?.IsValid()==true&&UserManager.Instance.CurrentUser.AccessToken==HorizonApp.Network.GetSessionToken()?UserManager.Instance.CurrentUser.UserId:null;
        private string Scope=>projectId+"|"+User+"|"+HorizonApp.Network.GetApiKey()+"|"+HorizonApp.Network.GetActiveHost()+"|"+HorizonApp.Network.GetSessionToken()+"|"+HorizonApp.Network.ContextGeneration+"|"+_generation;
        private MatchesSampleStore Store(string user)=>new MatchesSampleStore(Application.persistentDataPath,projectId,user,HorizonApp.Network.GetApiKey());
        public MyMatches ReviewedLedger=>_ledgerScope==Scope?MatchesManager.Instance.GetCached<MyMatches>(_ledgerIdentity):null;
        public MatchView ReviewedMatch=>_selectedScope==Scope?MatchesManager.Instance.GetCached<MatchRead>(_selectedIdentity)?.match:null;
        public async Task<bool> ReadLedger(bool recovery=false)
        {
            string user=User;if(user==null||_busy)return false;string scope=Scope;var network=HorizonApp.Network;var r=await MatchesManager.Instance.GetMatches(user,recovery);
            if(!Current(scope,network))return false;_ledgerIdentity=r.Identity;_ledger=r.IsSuccess?r.Data:null;_ledgerScope=r.IsSuccess?scope:null;return r.IsSuccess;
        }
        public async Task<bool> ReadMatch(string id,bool recovery=false)
        {
            string user=User;if(user==null||_busy)return false;string scope=Scope;var network=HorizonApp.Network;var r=await MatchesManager.Instance.GetMatch(user,id,recovery);
            if(!Current(scope,network))return false;_selectedIdentity=r.Identity;_selected=r.IsSuccess?r.Data.match:null;_selectedScope=r.IsSuccess?scope:null;return r.IsSuccess;
        }
        public Task<MatchesResult<MatchPage>> FindOneOpenPage(long min,long max,string cursor=null)=>MatchesManager.Instance.FindOpenMatches(User,min,max,20,cursor);
        public Task<MatchesResult<MatchLimitsView>> ReadLimits()=>MatchesManager.Instance.GetLimits(User);
        /// <summary>Explicit recovery from the original minimal ledger source after full detail expired.</summary>
        public Task<bool> SaveRankedAfterPendingResultReview(string matchId)
        {
            string user=User; var ledger=ReviewedLedger;
            if(user==null||_busy||ledger==null||ValidatedActionsManager.Instance.HasActiveRun||ValidatedActionsManager.Instance.HasPendingOperation)return Task.FromResult(false);
            var source=ledger.pendingResults.SingleOrDefault(x=>string.Equals(x.matchId,matchId,StringComparison.OrdinalIgnoreCase));
            if(source==null)return Task.FromResult(false);
            Store(user).SaveNew(MatchesSampleStore.NewAction(projectId,user,"ranked",source.matchId,source.finalStateRevision,ledger.revision));
            return ApplySavedIntent();
        }
        public Task<bool> SaveCreateAfterLedgerReview(MatchCreateInput input)
        {
            string user=User;if(user==null||_busy||_ledger==null||_ledgerScope!=Scope)return Task.FromResult(false);
            Store(user).SaveNew(MatchesSampleStore.NewCreate(projectId,user,_ledger.revision,input));return ApplySavedIntent();
        }
        /// <summary>Use the game's separate confirmation for leave/cancel. Reports freeze final revision/result/winners;
        /// start freezes the complete turn order. Null/metadata-only invitations do not expose game state.</summary>
        public Task<bool> SaveActionAfterMatchReview(string action,byte[] data=null,string[] ids=null,bool finish=false)
        {
            string user=User;if(user==null||_busy||_selected==null||_selectedScope!=Scope||_ledger==null||_ledgerScope!=Scope)return Task.FromResult(false);
            if(!new[]{"join","decline","start","turn","timeout","leave","cancel","report","ranked"}.Contains(action))return Task.FromResult(false);
            if(action=="ranked"&&(ValidatedActionsManager.Instance.HasActiveRun||ValidatedActionsManager.Instance.HasPendingOperation))return Task.FromResult(false);
            long revision=action=="report"||action=="ranked"?_selected.finalStateRevision??0:_selected.revision;if(revision==0)return Task.FromResult(false);
            Store(user).SaveNew(MatchesSampleStore.NewAction(projectId,user,action,_selected.id,revision,_ledger.revision,data,ids,finish));return ApplySavedIntent();
        }
        public async Task<bool> ApplySavedIntent()
        {
            string user=User;if(user==null||_busy)return false;var store=Store(user);if(!store.HasIntent)return false;_busy=true;
            try
            {
                using(var lease=store.AcquireSend())
                {
                    var p=lease.Prepared;var x=p.Value;string scope=Scope;var network=HorizonApp.Network;
                    if(p.HasSuccess){Reviewed(p.Hash);return true;}
                    if(x.action=="create"&&p.WasSent)return false; // Never send an unknown create again, even after recreation/region change.
                    if(x.action=="ranked")return await StartOriginalRanked(lease,scope,network);
                    lease.MarkSent();
                    if(x.action=="create")
                    {
                        var r=await MatchesManager.Instance.CreateMatch(user,x.revision,x.GetCreateInput());if(!r.IsSuccess)
                        {
                            if(Current(scope,network)&&MatchesSampleStore.DefinitiveCreateCode(r.ErrorCode)&&new long[]{400,401,403,404,422,501}.Contains(r.HttpStatusCode)){lease.SaveDefinitiveCreateRejection(r.ErrorCode);Reviewed(p.Hash);}return false;
                        }
                        lease.SaveSuccess(r.Data.match.id,r.Data.match.revision,r.Data.playerRevision);
                    }
                    else if(x.action=="report")
                    {
                        var r=await MatchesManager.Instance.ReportResult(user,x.matchId,x.revision,x.GetBytes(),x.ids);if(!r.IsSuccess)return false;lease.SaveSuccess(r.Data.match.id,r.Data.match.revision);
                    }
                    else
                    {
                        var r=await SendAction(x);if(!r.IsSuccess)return false;lease.SaveSuccess(r.Data.match.id,r.Data.match.revision);
                    }
                    if(!Current(scope,network))return false;_ledger=null;_selected=null;_ledgerScope=_selectedScope=null;Reviewed(p.Hash);return true;
                }
            }
            finally{_busy=false;}
        }
        private static Task<MatchesResult<MatchRead>> SendAction(MatchesSampleStore.Intent x)
        {
            var m=MatchesManager.Instance;switch(x.action)
            {
                case "join":return m.JoinMatch(x.userId,x.matchId,x.revision);case "decline":return m.DeclineInvite(x.userId,x.matchId,x.revision);
                case "start":return m.StartMatch(x.userId,x.matchId,x.revision,x.ids);case "turn":return m.SubmitTurn(x.userId,x.matchId,x.revision,x.GetBytes(),x.finish);
                case "timeout":return m.AdvanceTimeout(x.userId,x.matchId,x.revision);case "leave":return m.LeaveMatch(x.userId,x.matchId,x.revision);case "cancel":return m.CancelMatch(x.userId,x.matchId,x.revision);
                default:throw new InvalidOperationException("Unknown original Matches action");
            }
        }
        /// <summary>A complete explicitly reviewed ledger must be stable and strictly newer than the original create CAS.
        /// This fences the old create, without guessing which writer created any candidate Match.</summary>
        public async Task<bool> ReconcileUnknownCreateAfterLedgerReview()
        {
            string user=User;if(user==null||_busy||_ledger==null||_ledgerScope!=Scope)return false;var reviewed=_ledger;_busy=true;
            try
            {
                using(var lease=Store(user).AcquireSend())
                {
                    var x=lease.Prepared.Value;if(x.action!="create"||!lease.Prepared.WasSent||lease.Prepared.HasSuccess)return false;string scope=Scope;var network=HorizonApp.Network;
                    var r=await MatchesManager.Instance.GetMatches(user,true);if(!Current(scope,network)||!r.IsSuccess||r.Data.revision<=x.revision||r.Data.revision!=reviewed.revision||!SameLedger(reviewed,r.Data))return false;
                    lease.SaveReconciled(0,r.Data.revision);Reviewed(lease.Prepared.Hash);return true;
                }
            }
            finally{_busy=false;}
        }
        /// <summary>Explicit point/ledger reconciliation. 404 alone never implies success or releases a delayed action.</summary>
        public async Task<bool> ReconcileOriginalActionAfterReview()
        {
            string user=User;if(user==null||_busy)return false;_busy=true;
            try
            {
                using(var lease=Store(user).AcquireSend())
                {
                    var x=lease.Prepared.Value;if(lease.Prepared.HasSuccess||new[]{"create","report","ranked"}.Contains(x.action))return false;string scope=Scope;var network=HorizonApp.Network;
                    var r=await MatchesManager.Instance.GetMatch(user,x.matchId,true);if(!Current(scope,network))return false;
                    if(r.IsSuccess){if(r.Data.match.revision<=x.revision)return false;lease.SaveReconciled(r.Data.match.revision,0);}
                    else
                    {
                        if(r.ErrorCode!="MATCH_NOT_FOUND"||(x.action!="leave"&&x.action!="decline"))return false;
                        var ledger=await MatchesManager.Instance.GetMatches(user,true);if(!Current(scope,network)||!ledger.IsSuccess||ledger.Data.revision<=x.playerRevision||All(ledger.Data).Any(m=>m.id==x.matchId))return false;
                        lease.SaveReconciled(0,ledger.Data.revision,true);
                    }
                    Reviewed(lease.Prepared.Hash);return true;
                }
            }
            finally{_busy=false;}
        }
        private async Task<bool> StartOriginalRanked(MatchesSampleStore.SendLease lease,string scope,NetworkService network)
        {
            var x=lease.Prepared.Value;var m=MatchesManager.Instance;var run=m.GetCurrentRankedRun(x.userId,x.matchId,x.revision);
            if(run==null)
            {
                lease.MarkSent();var r=await m.StartRankedRun(x.userId,x.matchId,x.revision);
                if(!r.IsSuccess)
                {
                    if(Current(scope,network)&&(r.ErrorCode=="RUN_EXPIRED"||r.ErrorCode=="RUN_ALREADY_USED")){lease.SaveRankedTerminal(r.ErrorCode);Reviewed(lease.Prepared.Hash);}
                    return false;
                }
                run=r.Data;
            }
            string saved=lease.GetRunId();if(saved!=null&&saved!=run.runId)throw new System.IO.IOException("Original ranked run changed");lease.SaveRunId(run.runId);return Current(scope,network);
        }
        /// <summary>Persist the full original score/log before VA submit. Later retries must use the same saved tuple.
        /// Success is the returned VA result/state; there is no local reward addition.</summary>
        public async Task<bool> SubmitOriginalRanked(long score,byte[] inputLog)
        {
            string user=User;if(user==null||_busy)return false;_busy=true;
            try
            {
                using(var lease=Store(user).AcquireSend())
                {
                    var x=lease.Prepared.Value;if(x.action!="ranked"||lease.Prepared.HasSuccess||ValidatedActionsManager.Instance.CurrentRun?.runId!=lease.GetRunId())return false;
                    lease.SaveSubmission(score,inputLog);var original=lease.GetSubmission();string scope=Scope;var network=HorizonApp.Network;
                    var r=await ValidatedActionsManager.Instance.SubmitValidated(original.score,Convert.FromBase64String(original.inputLogBase64));
                    if(r==null||r.runId!=original.runId)return false;lease.SaveSuccess(x.matchId,x.revision);if(!Current(scope,network))return false;Reviewed(lease.Prepared.Hash);return true;
                }
            }
            finally{_busy=false;}
        }
        public void AcknowledgeSavedResult()
        {
            string user=User;if(user==null||_busy||_reviewedHash==null||_reviewedScope!=Scope)return;
            using(var lease=Store(user).AcquireSend()){if(!lease.Prepared.HasSuccess)return;lease.Acknowledge(_reviewedHash);}_reviewedHash=_reviewedScope=null;
        }
        private void Reviewed(string hash){_reviewedHash=hash;_reviewedScope=Scope;}
        private static System.Collections.Generic.IEnumerable<MatchSummary> All(MyMatches m)=>m.active.Concat(m.invites).Concat(m.recent);
        private static bool SameLedger(MyMatches a,MyMatches b)=>a.userId==b.userId&&All(a).Select(x=>x.id+":"+x.revision).OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(All(b).Select(x=>x.id+":"+x.revision).OrderBy(x=>x,StringComparer.Ordinal));
        private bool Current(string scope,NetworkService network)=>User!=null&&scope==Scope&&ReferenceEquals(network,HorizonApp.Network);
        private void OnDisable(){_generation++;_ledger=null;_selected=null;_ledgerScope=_selectedScope=_reviewedHash=_reviewedScope=null;}
        private void OnValidate(){_generation++;}
    }
}
