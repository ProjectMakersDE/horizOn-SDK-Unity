using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
namespace PM.horizOn.Cloud.Helper
{
    internal static class GroupsJson
    {
        internal static bool Revision(long value) => value >= 0 && value <= PlayerValuesManager.MaxSafeInteger;
        internal static string Quote(string value)
        {
            var b=new StringBuilder("\"");foreach(char c in value){
                switch(c){case '"':b.Append("\\\"");break;case '\\':b.Append("\\\\");break;
                    case '\n':b.Append("\\n");break;case '\r':b.Append("\\r");break;case '\t':b.Append("\\t");break;
                    default:if(c<32)b.Append("\\u").Append(((int)c).ToString("x4",CultureInfo.InvariantCulture));else b.Append(c);break;}
            }return b.Append('"').ToString();
        }
        internal static string Text(string raw) => JsonFragments.String(raw);
        internal static string Uuid(string raw) { var id = Text(raw); if (!FriendsJson.IsUuid(id)) throw new FormatException("Groups UUID invalid"); return Guid.ParseExact(id,"D").ToString("D"); }
        private static string Optional(Dictionary<string,string> f, string key) => !f.TryGetValue(key,out var raw) || raw == "null" ? null : Text(raw);
        private static long Number(string raw) { if (!long.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out var n) || !Revision(n)) throw new FormatException("Groups integer invalid"); return n; }
        private static int Bound(string raw,int max) { var n=Number(raw); if(n<1||n>max)throw new FormatException("Groups bound invalid");return(int)n; }
        private static string Instant(string raw) { var s=Text(raw); if(!DateTimeOffset.TryParse(s,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out _))throw new FormatException("Groups time invalid");return s; }
        private static string Enum(string raw,params string[] values) { var s=Text(raw); if(Array.IndexOf(values,s)<0)throw new FormatException("Groups enum invalid");return s; }
        internal static void ValidateData(string raw)
        {
            if(raw==null||Encoding.UTF8.GetByteCount(raw)>2048)throw new FormatException("Groups data byte limit");
            JsonFragments.Object(raw); Visit(raw,0);
        }
        private static void Visit(string raw,int depth)
        {
            if(depth>16)throw new FormatException("Groups data depth");raw=raw.Trim();
            if(raw.StartsWith("{")){foreach(var x in JsonFragments.Object(raw).Values)Visit(x,depth+1);}
            else if(raw.StartsWith("[")){foreach(var x in JsonFragments.Array(raw))Visit(x,depth+1);}
            else if(raw.StartsWith("\""))JsonFragments.String(raw);
            else if(raw!="true"&&raw!="false"&&raw!="null") { if(!double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out var d)||double.IsInfinity(d)||double.IsNaN(d))throw new FormatException("Groups finite number required"); }
        }
        internal static GroupLimits Limits(string raw)
        {
            var f=JsonFragments.Object(raw);return new GroupLimits{maxMembers=Bound(f["maxMembers"],500),maxGroupsPerPlayer=Bound(f["maxGroupsPerPlayer"],5),maxInvitations=Bound(f["maxInvitations"],20),maxJoinRequests=Bound(f["maxJoinRequests"],20),maxDataBytes=Bound(f["maxDataBytes"],2048),memberPageSize=Bound(f["memberPageSize"],50)};
        }
        internal static GroupView Group(string raw)
        {
            var f=JsonFragments.Object(raw);var result=new GroupView{id=Uuid(f["id"]),revision=Number(f["revision"]),name=Text(f["name"]),description=Text(f["description"]),joinMode=Enum(f["joinMode"],"open","request","invite"),leaderId=Uuid(f["leaderId"]),memberCount=Bound(f["memberCount"],500),createdAt=Instant(f["createdAt"]),updatedAt=Instant(f["updatedAt"])};
            if(f.TryGetValue("data",out var data)&&data!="null")result.data=GroupJsonData.FromJson(data);
            if(f.TryGetValue("myRole",out var role)&&role!="null")result.myRole=Enum(role,"leader","officer","member");
            return result;
        }
        internal static MyGroupsView Mine(string raw)
        {
            var f=JsonFragments.Object(raw);return new MyGroupsView{userId=Uuid(f["userId"]),revision=Number(f["revision"]),serverTime=Instant(f["serverTime"]),groups=Rows(f["groups"],5,Group,x=>x.id),incomingInvites=Rows(f["incomingInvites"],20,PlayerPending,x=>x.groupId),outgoingRequests=Rows(f["outgoingRequests"],20,PlayerPending,x=>x.groupId),limits=Limits(f["limits"])};
        }
        private static PlayerGroupPending PlayerPending(string raw)
        {
            var f=JsonFragments.Object(raw);var r=new PlayerGroupPending{groupId=Uuid(f["groupId"]),requestId=Uuid(f["requestId"]),issuerId=Uuid(f["issuerId"]),createdAt=Instant(f["createdAt"]),group=f["group"]=="null"?null:Group(f["group"])};
            if(r.group!=null&&(r.group.id!=r.groupId||r.group.data!=null||r.group.myRole!=null))throw new FormatException("Groups pending private projection");return r;
        }
        internal static GroupMembersView Members(string raw)
        {
            var f=JsonFragments.Object(raw);var cursor=Optional(f,"nextAfterUserId");if(cursor!=null&&!FriendsJson.IsUuid(cursor))throw new FormatException("Groups cursor invalid");
            return new GroupMembersView{groupId=Uuid(f["groupId"]),revision=Number(f["revision"]),serverTime=Instant(f["serverTime"]),nextAfterUserId=cursor,members=Rows(f["members"],50,Member,x=>x.userId)};
        }
        private static GroupMemberView Member(string raw)
        {
            var f=JsonFragments.Object(raw);var d=JsonFragments.Object(f["display"]);var p=JsonFragments.Object(d["profile"]);
            return new GroupMemberView{userId=Uuid(f["userId"]),role=Enum(f["role"],"leader","officer","member"),joinedAt=Instant(f["joinedAt"]),display=new FriendDisplay{name=Optional(d,"name"),profile=new HorizonPlayerProfile{avatarId=Optional(p,"avatarId"),frameId=Optional(p,"frameId"),badges=Rows(p["badges"],3,Text,x=>x)}}};
        }
        internal static GroupPendingSnapshot Pending(string raw)
        {
            var f=JsonFragments.Object(raw);return new GroupPendingSnapshot{groupId=Uuid(f["groupId"]),revision=Number(f["revision"]),serverTime=Instant(f["serverTime"]),invitations=Rows(f["invitations"],20,PendingEntry,x=>x.userId),joinRequests=Rows(f["joinRequests"],20,PendingEntry,x=>x.userId)};
        }
        private static GroupPending PendingEntry(string raw) {var f=JsonFragments.Object(raw);return new GroupPending{userId=Uuid(f["userId"]),requestId=Uuid(f["requestId"]),issuerId=Uuid(f["issuerId"]),createdAt=Instant(f["createdAt"])};}
        internal static GroupMutationView Mutation(string raw) {var f=JsonFragments.Object(raw);return new GroupMutationView{groups=Mine(f["groups"]),group=f["group"]=="null"?null:Group(f["group"])};}
        private static T[] Rows<T>(string raw,int max,Func<string,T> parse,Func<T,string> key)
        {var rows=JsonFragments.Array(raw);if(rows.Count>max)throw new FormatException("Groups list bound");var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var result=new T[rows.Count];for(int i=0;i<result.Length;i++){result[i]=parse(rows[i]);if(!ids.Add(key(result[i])))throw new FormatException("Groups duplicate identity");}return result;}
        internal static T Clone<T>(T value) where T:class
        {
            if(value==null)return null;
            // Typed copies preserve omitted data, optional roles, null group previews and nullable displays.
            object copy=value is MyGroupsView m?new MyGroupsView{userId=m.userId,revision=m.revision,serverTime=m.serverTime,groups=Array.ConvertAll(m.groups,Clone),incomingInvites=Array.ConvertAll(m.incomingInvites,Clone),outgoingRequests=Array.ConvertAll(m.outgoingRequests,Clone),limits=Clone(m.limits)}
              :value is GroupView g?new GroupView{id=g.id,revision=g.revision,name=g.name,description=g.description,joinMode=g.joinMode,leaderId=g.leaderId,memberCount=g.memberCount,data=g.data,myRole=g.myRole,createdAt=g.createdAt,updatedAt=g.updatedAt}
              :value is PlayerGroupPending p?new PlayerGroupPending{groupId=p.groupId,requestId=p.requestId,issuerId=p.issuerId,createdAt=p.createdAt,group=Clone(p.group)}
              :value is GroupLimits l?new GroupLimits{maxMembers=l.maxMembers,maxGroupsPerPlayer=l.maxGroupsPerPlayer,maxInvitations=l.maxInvitations,maxJoinRequests=l.maxJoinRequests,maxDataBytes=l.maxDataBytes,memberPageSize=l.memberPageSize}
              :value is GroupPending e?new GroupPending{userId=e.userId,requestId=e.requestId,issuerId=e.issuerId,createdAt=e.createdAt}
              :value is GroupMemberView v?new GroupMemberView{userId=v.userId,role=v.role,joinedAt=v.joinedAt,display=new FriendDisplay{name=v.display.name,profile=new HorizonPlayerProfile{avatarId=v.display.profile.avatarId,frameId=v.display.profile.frameId,badges=(string[])v.display.profile.badges.Clone()}}}
              :value is GroupMembersView a?new GroupMembersView{groupId=a.groupId,revision=a.revision,serverTime=a.serverTime,nextAfterUserId=a.nextAfterUserId,members=Array.ConvertAll(a.members,Clone)}
              :value is GroupPendingSnapshot s?new GroupPendingSnapshot{groupId=s.groupId,revision=s.revision,serverTime=s.serverTime,invitations=Array.ConvertAll(s.invitations,Clone),joinRequests=Array.ConvertAll(s.joinRequests,Clone)}
              :value is GroupMutationView x?new GroupMutationView{groups=Clone(x.groups),group=Clone(x.group)}:throw new ArgumentException("Unknown Groups clone type");return(T)copy;
        }
    }
}
