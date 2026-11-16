using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    [Serializable] public abstract class ItemGiftsDto
    {
        internal ItemGiftsDto ShallowCopy() => (ItemGiftsDto)MemberwiseClone();
    }
    /// <summary>Effective operator quotas and fixed product timings. Lowering a quota never removes open gifts.</summary>
    [Serializable] public sealed class ItemGiftLimits : ItemGiftsDto
    {
        public int openGiftsPerPlayer, retainedGiftsPerProject, projectWritesPerMinute, historyPerPlayer, receivedPageSize;
        public long playerWriteIntervalMillis, expirySeconds, terminalRetentionSeconds, operationRetentionSeconds;
    }
    /// <summary>A free gift has <c>fee == 0</c> and <c>currencyKey == null</c>. A fee is charged once on send and never refunded.</summary>
    [Serializable] public sealed class ItemGiftConfig : ItemGiftsDto { public long revision; public bool enabled; public string currencyKey; public long fee; }
    [Serializable] public sealed class ItemGiftConfigView : ItemGiftsDto { public ItemGiftConfig config; public ItemGiftLimits limits; public string serverTime; }
    /// <summary>Status: offered, claimed, return_pending, return_delivered, returned. Fee and config revision are the values the gift was sent with.</summary>
    [Serializable] public sealed class ItemGift : ItemGiftsDto
    {
        public string id, senderId, recipientId, returnOwnerId, currencyKey, status, createdAt, expiresAt, terminalAt, declinedAt, offerEntryId, returnEntryId;
        public long revision, fee, configRevision;
        public ItemReference itemReference;
        public bool ownerUnavailable;
    }
    [Serializable] public sealed class ItemGiftWindow : ItemGiftsDto { public string serverTime, acceptAfter; }
    /// <summary>Sent lists open gifts and pending returns, history the retained terminal gifts, received the open offers to this player.</summary>
    [Serializable] public sealed class ItemGiftState : ItemGiftsDto
    {
        public string userId;
        public long revision;
        public ItemGift[] sent = Array.Empty<ItemGift>(), history = Array.Empty<ItemGift>(), received = Array.Empty<ItemGift>();
        public ItemGiftWindow operationWindow;
        public ItemGiftLimits limits;
    }
    [Serializable] public sealed class ItemGiftStateView : ItemGiftsDto { public ItemGiftState state; public string serverTime; }
    [Serializable] public sealed class ItemGiftView : ItemGiftsDto { public ItemGift gift; public string serverTime; }
    /// <summary>The fee proof of one original send. No inventory or balance snapshot.</summary>
    [Serializable] public sealed class ItemGiftReceipt : ItemGiftsDto
    {
        public string operationId, issuedAt, giftId, recordedAt, recipientId, currencyKey;
        public long giftRevision, fee, configRevision;
        public ItemReference itemReference;
    }
    /// <summary><c>gift</c> is null on a replay after the terminal gift left its retention window.</summary>
    [Serializable] public sealed class ItemGiftWritten : ItemGiftsDto { public ItemGiftReceipt receipt; public ItemGift gift; public bool replayed; public string serverTime; }
    [Serializable] public sealed class ItemGiftOperation : ItemGiftsDto { public ItemGiftReceipt receipt; public string serverTime; }
    /// <summary><c>returnDelivery</c>: pending (sender inbox full, the server retries), delivered, returned or claimed.</summary>
    [Serializable] public sealed class ItemGiftDeclined : ItemGiftsDto { public ItemGift gift; public bool replayed; public string returnDelivery, serverTime; }
    public sealed class ItemGiftsResult<T> where T : class
    {
        public bool IsSuccess { get; internal set; }
        public T Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }
    internal sealed class ItemGiftsJsonRequest
    {
        internal string Json { get; }
        internal ItemGiftsJsonRequest(string json) { Json = json; }
    }
}
