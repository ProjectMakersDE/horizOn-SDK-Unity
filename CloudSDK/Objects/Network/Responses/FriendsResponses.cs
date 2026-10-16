using System;
namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    [Serializable] public class FriendDisplay { public string name; public HorizonPlayerProfile profile; }
    [Serializable] public class FriendView { public string userId, since, lastActiveAt; public FriendDisplay display; }
    [Serializable] public class FriendRequestView { public string userId, requestId, createdAt; public FriendDisplay display; }
    [Serializable] public class BlockedPlayerView { public string userId, blockedAt; }
    [Serializable] public class FriendsLimits { public int maxFriends, maxIncomingRequests, maxOutgoingRequests, maxBlocked; }
    [Serializable] public class FriendsSnapshot
    {
        public string userId, friendCode, serverTime;
        public long revision;
        public FriendView[] friends = Array.Empty<FriendView>();
        public FriendRequestView[] incoming = Array.Empty<FriendRequestView>(), outgoing = Array.Empty<FriendRequestView>();
        public BlockedPlayerView[] blocked = Array.Empty<BlockedPlayerView>();
        public FriendsLimits limits;
    }
    [Serializable] public class FriendLookup { public string userId, friendCode; public FriendDisplay display; }
    public class FriendsResult
    {
        public bool IsSuccess { get; internal set; }
        public FriendsSnapshot Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }
    public class FriendLookupResult
    {
        public bool IsSuccess { get; internal set; }
        public FriendLookup Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }
}
