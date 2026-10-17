using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    [Serializable] public sealed class ChatLimits
    {
        public int retainedMessagesPerPlayer, channelsPerAccount, sendsPerAccountMinute, reportsPerProject;
        public int retainedMessagesPerChannel, retentionSeconds, privateRetainedMessagesPerChannel, privateRetentionSeconds;
        public int operationWindowSeconds, operationFutureToleranceSeconds, messageCharacters, messageUtf8Bytes;
        public int playerSendIntervalMillis, channelSendIntervalMillis, receiptCount, reportReceiptCount, wordFilterTerms;
    }
    [Serializable] public sealed class ChatCapabilities { public bool chat, realtime, incrementalSync, localHistory; }
    [Serializable] public sealed class ChatModeration { public string muteUntil, banUntil; }
    [Serializable] public sealed class ChatLimitsView
    { public ChatLimits limits; public ChatCapabilities capabilities; public ChatModeration moderation; public string serverTime; }
    [Serializable] public sealed class ChatChannel
    { public string id, epoch, kind, groupId, expiresAt; public string[] peerIds = Array.Empty<string>(); public long revision; }
    [Serializable] public sealed class ChatMessage
    { public string id, channelId, authorId, text, createdAt, expiresAt; }
    [Serializable] public sealed class ChatReceipt
    { public string operationId, issuedAt, channelId, messageId, createdAt, expiresAt; }
    [Serializable] public sealed class ChatOpened { public ChatChannel channel; public string serverTime; }
    [Serializable] public sealed class ChatSynced
    {
        public ChatChannel channel; public string[] visibleMessageIds = Array.Empty<string>();
        public ChatMessage[] messages = Array.Empty<ChatMessage>(); public ChatModeration moderation; public string serverTime;
    }
    [Serializable] public sealed class ChatSent { public ChatReceipt receipt; public bool replayed; public string serverTime; }
    [Serializable] public sealed class ChatOperation { public ChatReceipt receipt; public string serverTime; }
    [Serializable] public sealed class ChatReported { public string reportId, status, serverTime; public bool replayed; }
    public sealed class ChatHistoryPage
    {
        /// <summary>Newest delivered records first. Server timestamps remain unchanged.</summary>
        public ChatMessage[] messages = Array.Empty<ChatMessage>(); public string nextCursor;
    }
    public sealed class ChatResult<T> where T : class
    {
        public bool IsSuccess { get; internal set; }
        public T Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
        public long HttpStatusCode { get; internal set; }
    }
    internal sealed class ChatJsonRequest { internal readonly string Json; internal ChatJsonRequest(string json) { Json = json; } }
}
