using System;
using System.Collections.Generic;
namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    [Serializable] public class InboxItemReturn { public string listingId, instanceId, itemKey; }
    [Serializable] public class InboxAttachment
    {
        public PlayerValueAmount[] values = Array.Empty<PlayerValueAmount>();
        public InboxItemReturn itemReturn;
        public bool HasReward => values != null && values.Length > 0 || itemReturn != null;
    }
    public enum InboxSourceKind { Unknown, FriendRequest, FriendAccepted, GroupInvitation, GroupRequest, GroupInvitationAccepted, GroupRequestAccepted, MatchTurn, MarketReturn }
    /// <summary>A display/navigation reference. Actions still use the original Inbox entry ID and the authoritative source API.</summary>
    public sealed class InboxSource
    {
        public InboxSourceKind Kind { get; internal set; }
        public string SourceId { get; internal set; }
        public string UserId { get; internal set; }
        public string GroupId { get; internal set; }
        public long? Revision { get; internal set; }
    }
    [Serializable] public class InboxEntry
    {
        public string id, kind, type, reference, textKey, createdAt, expiresAt;
        public bool overdue, read, claimed;
        public InboxAttachment attachment;
        [NonSerialized] public Dictionary<string, string> parameters = new Dictionary<string, string>();
        public bool CanClaim => !claimed && attachment != null && attachment.HasReward;
        public bool TryGetFreeText(out string text)
        {
            text = null;
            return kind == "message" && type == "free_text" && textKey == "horizon.inbox.free_text"
                && parameters != null && parameters.TryGetValue("text", out text);
        }
        public InboxSourceKind SourceKind
        {
            get
            {
                if (kind != "message") return InboxSourceKind.Unknown;
                if (type == "friend_request" && textKey == "horizon.friends.request") return InboxSourceKind.FriendRequest;
                if (type == "friend_accept" && textKey == "horizon.friends.accept") return InboxSourceKind.FriendAccepted;
                if (type == "group_invite" && textKey == "horizon.groups.invite") return InboxSourceKind.GroupInvitation;
                if (type == "group_request" && textKey == "horizon.groups.request") return InboxSourceKind.GroupRequest;
                if (type == "group_accept-invite" && textKey == "horizon.groups.accept-invite") return InboxSourceKind.GroupInvitationAccepted;
                if (type == "group_accept-request" && textKey == "horizon.groups.accept-request") return InboxSourceKind.GroupRequestAccepted;
                if (type == "match_turn" && textKey == "horizon.matches.your_turn") return InboxSourceKind.MatchTurn;
                if (type == "marketplace" && textKey == "marketplace.item_return") return InboxSourceKind.MarketReturn;
                return InboxSourceKind.Unknown;
            }
        }
        public bool TryGetSource(out InboxSource source)
        {
            source = null;
            var sourceKind = SourceKind;
            if (sourceKind == InboxSourceKind.Unknown || reference == null || reference.Length != 36 || !Guid.TryParseExact(reference, "D", out var id)) return false;
            var result = new InboxSource { Kind = sourceKind, SourceId = id.ToString("D") };
            if (sourceKind == InboxSourceKind.FriendRequest || sourceKind == InboxSourceKind.FriendAccepted)
            {
                if (!ParameterId("userId", out var userId)) return false;
                result.UserId = userId;
            }
            else if (sourceKind == InboxSourceKind.GroupInvitation || sourceKind == InboxSourceKind.GroupRequest
                || sourceKind == InboxSourceKind.GroupInvitationAccepted || sourceKind == InboxSourceKind.GroupRequestAccepted)
            {
                if (!ParameterId("groupId", out var groupId)) return false;
                if ((sourceKind == InboxSourceKind.GroupInvitationAccepted || sourceKind == InboxSourceKind.GroupRequestAccepted) && groupId != result.SourceId) return false;
                result.GroupId = groupId;
            }
            else if (sourceKind == InboxSourceKind.MatchTurn)
            {
                if (!ParameterId("matchId", out var matchId) || matchId != result.SourceId || parameters == null
                    || !parameters.TryGetValue("revision", out var revision) || !long.TryParse(revision, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var n) || n < 0 || n > 9007199254740991L) return false;
                result.Revision = n;
            }
            else if (attachment?.itemReturn == null || !Guid.TryParseExact(attachment.itemReturn.listingId, "D", out var listingId) || listingId != id) return false;
            source = result; return true;
        }
        private bool ParameterId(string name, out string id)
        {
            id = null;
            if (parameters == null || !parameters.TryGetValue(name, out var raw) || raw == null || raw.Length != 36 || !Guid.TryParseExact(raw, "D", out var value)) return false;
            id = value.ToString("D"); return true;
        }
    }
    [Serializable] public class InboxCount { public int total, unread, claimable; }
    [Serializable] public class InboxLimits { public int targeted, broadcasts, freeTexts = 2, freeTextBytes = 256, freeTextRetentionDays = 7; }
    [Serializable] public class InboxSnapshot
    {
        public string userId, cursor, serverTime;
        public long revision, broadcastRevision;
        public int pollAfterSeconds;
        public bool unchanged;
        public InboxEntry[] entries = Array.Empty<InboxEntry>();
        public InboxLimits limits;
        [NonSerialized] public Dictionary<string, InboxCount> counts = new Dictionary<string, InboxCount>();
    }
    [Serializable] public class InboxClaimResponse
    {
        public string entryId;
        public bool replayed;
        public InboxSnapshot inbox;
        public PlayerValuesState playerValues;
        public ItemReference[] items = Array.Empty<ItemReference>();
    }
    public class InboxResult
    {
        public bool IsSuccess { get; internal set; }
        public InboxSnapshot Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }
    public class InboxClaimResult
    {
        public bool IsSuccess { get; internal set; }
        public InboxClaimResponse Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }
}
