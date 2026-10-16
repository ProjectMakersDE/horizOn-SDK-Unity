using System;
using PM.horizOn.Cloud.Helper;
namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>Immutable JSON object. Keeps legal arbitrary keys, nulls and original number tokens without JsonUtility dictionary conversion.</summary>
    public sealed class GroupJsonData
    {
        public string Json { get; }
        private GroupJsonData(string json) { Json = json; }
        public static GroupJsonData FromJson(string json) { GroupsJson.ValidateData(json); return new GroupJsonData(json); }
        public static GroupJsonData Empty => new GroupJsonData("{}");
        public override string ToString() => Json;
    }
    [Serializable] public class GroupLimits { public int maxMembers, maxGroupsPerPlayer, maxInvitations, maxJoinRequests, maxDataBytes, memberPageSize; }
    [Serializable] public class GroupView
    {
        public string id, name, description, joinMode, leaderId, myRole, createdAt, updatedAt;
        public long revision; public int memberCount;
        public GroupJsonData data; // null means private data was omitted, not an empty object.
    }
    [Serializable] public class GroupPending { public string userId, requestId, issuerId, createdAt; }
    [Serializable] public class PlayerGroupPending { public string groupId, requestId, issuerId, createdAt; public GroupView group; }
    [Serializable] public class MyGroupsView
    {
        public string userId, serverTime; public long revision;
        public GroupView[] groups = Array.Empty<GroupView>();
        public PlayerGroupPending[] incomingInvites = Array.Empty<PlayerGroupPending>(), outgoingRequests = Array.Empty<PlayerGroupPending>();
        public GroupLimits limits;
    }
    [Serializable] public class GroupMemberView { public string userId, role, joinedAt; public FriendDisplay display; }
    [Serializable] public class GroupMembersView { public string groupId, nextAfterUserId, serverTime; public long revision; public GroupMemberView[] members = Array.Empty<GroupMemberView>(); }
    [Serializable] public class GroupPendingSnapshot { public string groupId, serverTime; public long revision; public GroupPending[] invitations = Array.Empty<GroupPending>(), joinRequests = Array.Empty<GroupPending>(); }
    [Serializable] public class GroupMutationView { public MyGroupsView groups; public GroupView group; }
    public class GroupsResult<T> where T:class
    {
        public bool IsSuccess { get; internal set; } public T Data { get; internal set; }
        public string ErrorCode { get; internal set; } public string Error { get; internal set; }
    }
    internal sealed class GroupsJsonRequest { internal readonly string Json; internal GroupsJsonRequest(string json) { Json = json; } }
}
