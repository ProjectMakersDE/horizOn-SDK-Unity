using System;
using System.Collections.Generic;
namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    [Serializable] public class InboxAttachment { public PlayerValueAmount[] values = Array.Empty<PlayerValueAmount>(); }
    [Serializable] public class InboxEntry
    {
        public string id, kind, type, reference, textKey, createdAt, expiresAt;
        public bool overdue, read, claimed;
        public InboxAttachment attachment;
        [NonSerialized] public Dictionary<string, string> parameters = new Dictionary<string, string>();
    }
    [Serializable] public class InboxCount { public int total, unread, claimable; }
    [Serializable] public class InboxLimits { public int targeted, broadcasts; }
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
