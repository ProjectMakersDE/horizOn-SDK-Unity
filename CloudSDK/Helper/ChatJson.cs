using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Helper
{
    internal static class ChatJson
    {
        internal static Dictionary<string, string> Fields(string raw, string names)
        {
            var f = JsonFragments.Object(raw); var required = names.Split(',');
            if (f.Count != required.Length || required.Any(n => !f.ContainsKey(n))) throw new FormatException("Invalid Chat fields");
            return f;
        }
        internal static string Id(string value)
        { if (!FriendsJson.IsUuid(value) || value != value.ToLowerInvariant()) throw new FormatException("Invalid Chat UUID"); return value; }
        private static string Uuid(string raw) => Id(JsonFragments.String(raw));
        internal static string Time(string raw)
        {
            string s = JsonFragments.String(raw);
            if (!DateTimeOffset.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _)) throw new FormatException("Invalid Chat timestamp");
            return s;
        }
        internal static long Millis(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUnixTimeMilliseconds();
        internal static string Quote(string s) => ItemsJson.Quote(s);
        private static string Optional(string raw, Func<string, string> parse) => raw == "null" ? null : parse(raw);
        private static bool Bool(string raw) => raw == "true" ? true : raw == "false" ? false : throw new FormatException("Invalid Chat boolean");
        private static int Number(string raw, int min = 0, int max = int.MaxValue)
        { long n = ItemsJson.Integer(raw); if (n < min || n > max) throw new FormatException("Invalid Chat limit"); return (int)n; }
        private static int Exact(string raw, int value)
        { if (Number(raw) != value) throw new FormatException("Unsupported Chat contract"); return value; }
        internal static string Text(string value, int characters = 500, int bytes = 2000)
        {
            if (value == null || Encoding.UTF8.GetByteCount(value) > bytes) throw new FormatException("Invalid Chat text");
            Quote(value); int scalars = 0;
            for (int i = 0; i < value.Length; i++, scalars++)
            {
                char c = value[i]; if (char.IsHighSurrogate(c)) i++;
                if (char.IsControl(c) && c != '\n' && c != '\t') throw new FormatException("Invalid Chat control character");
            }
            if (scalars > characters || MatchesJson.Blank(value)) throw new FormatException("Invalid Chat text bound");
            return value;
        }
        private static string[] Ids(string raw, int max)
        {
            var ids = JsonFragments.Array(raw).Select(Uuid).ToArray();
            if (ids.Length > max || ids.Distinct().Count() != ids.Length) throw new FormatException("Invalid Chat IDs"); return ids;
        }
        private static ChatModeration Moderation(string raw)
        { var f = Fields(raw, "muteUntil,banUntil"); return new ChatModeration { muteUntil = Optional(f["muteUntil"], Time), banUntil = Optional(f["banUntil"], Time) }; }
        internal static ChatLimitsView Limits(string raw)
        {
            var f = Fields(raw, "limits,capabilities,moderation,serverTime");
            var l = Fields(f["limits"], "retainedMessagesPerPlayer,channelsPerAccount,sendsPerAccountMinute,reportsPerProject,retainedMessagesPerChannel,retentionSeconds,privateRetainedMessagesPerChannel,privateRetentionSeconds,operationWindowSeconds,operationFutureToleranceSeconds,messageCharacters,messageUtf8Bytes,playerSendIntervalMillis,channelSendIntervalMillis,receiptCount,reportReceiptCount,wordFilterTerms");
            var c = Fields(f["capabilities"], "chat,realtime,incrementalSync,localHistory");
            return new ChatLimitsView { limits = new ChatLimits {
                retainedMessagesPerPlayer = Exact(l["retainedMessagesPerPlayer"], 0), receiptCount = Exact(l["receiptCount"], 0), reportReceiptCount = Exact(l["reportReceiptCount"], 0),
                retainedMessagesPerChannel = Exact(l["retainedMessagesPerChannel"], 20), retentionSeconds = Exact(l["retentionSeconds"], 604800),
                privateRetainedMessagesPerChannel = Exact(l["privateRetainedMessagesPerChannel"], 15), privateRetentionSeconds = Exact(l["privateRetentionSeconds"], 30),
                operationWindowSeconds = Exact(l["operationWindowSeconds"], 30), operationFutureToleranceSeconds = Exact(l["operationFutureToleranceSeconds"], 60),
                messageCharacters = Exact(l["messageCharacters"], 500), messageUtf8Bytes = Exact(l["messageUtf8Bytes"], 2000),
                playerSendIntervalMillis = Exact(l["playerSendIntervalMillis"], 3000), channelSendIntervalMillis = Exact(l["channelSendIntervalMillis"], 2000),
                wordFilterTerms = Exact(l["wordFilterTerms"], 100), channelsPerAccount = Number(l["channelsPerAccount"], 1, 2000),
                sendsPerAccountMinute = Number(l["sendsPerAccountMinute"], 1, 120), reportsPerProject = Number(l["reportsPerProject"], 1, 200)
            }, capabilities = new ChatCapabilities { chat = Bool(c["chat"]), realtime = Bool(c["realtime"]), incrementalSync = Bool(c["incrementalSync"]), localHistory = Bool(c["localHistory"]) },
                moderation = Moderation(f["moderation"]), serverTime = Time(f["serverTime"]) };
        }
        internal static ChatChannel Channel(string raw)
        {
            var f = Fields(raw, "id,epoch,kind,peerIds,groupId,revision,expiresAt");
            var c = new ChatChannel { id = Uuid(f["id"]), epoch = Uuid(f["epoch"]), kind = JsonFragments.String(f["kind"]), peerIds = Ids(f["peerIds"], 2), groupId = Optional(f["groupId"], Uuid), revision = ItemsJson.Integer(f["revision"]), expiresAt = Time(f["expiresAt"]) };
            if (c.revision < 1 || !new[] { "direct", "group", "project" }.Contains(c.kind)
                || (c.kind == "direct" ? c.peerIds.Length != 2 || c.groupId != null || !c.peerIds.SequenceEqual(c.peerIds.OrderBy(x => x, StringComparer.Ordinal)) : c.peerIds.Length != 0 || (c.kind == "group") != (c.groupId != null))) throw new FormatException("Invalid Chat channel");
            return c;
        }
        internal static ChatMessage Message(string raw)
        {
            var f = Fields(raw, "id,channelId,authorId,text,createdAt,expiresAt");
            var m = new ChatMessage { id = Uuid(f["id"]), channelId = Uuid(f["channelId"]), authorId = Uuid(f["authorId"]), text = Text(JsonFragments.String(f["text"])), createdAt = Time(f["createdAt"]), expiresAt = Time(f["expiresAt"]) };
            if (Millis(m.expiresAt) <= Millis(m.createdAt) || Millis(m.expiresAt) - Millis(m.createdAt) > 604800000) throw new FormatException("Invalid Chat expiry"); return m;
        }
        internal static ChatReceipt Receipt(string raw)
        {
            var f = Fields(raw, "operationId,issuedAt,channelId,messageId,createdAt,expiresAt");
            var r = new ChatReceipt { operationId = Uuid(f["operationId"]), issuedAt = Time(f["issuedAt"]), channelId = Uuid(f["channelId"]), messageId = Uuid(f["messageId"]), createdAt = Time(f["createdAt"]), expiresAt = Time(f["expiresAt"]) };
            if (Millis(r.expiresAt) <= Millis(r.createdAt) || Millis(r.expiresAt) - Millis(r.createdAt) > 604800000) throw new FormatException("Invalid Chat receipt"); return r;
        }
        internal static ChatOpened Opened(string raw)
        { var f = Fields(raw, "channel,serverTime"); return new ChatOpened { channel = Channel(f["channel"]), serverTime = Time(f["serverTime"]) }; }
        internal static ChatSynced Synced(string raw)
        {
            var f = Fields(raw, "channel,visibleMessageIds,messages,moderation,serverTime");
            var c = Channel(f["channel"]); int max = c.kind == "project" ? 20 : 15;
            var ids = Ids(f["visibleMessageIds"], max); var rows = JsonFragments.Array(f["messages"]);
            if (rows.Count > max) throw new FormatException("Invalid Chat message count");
            var messages = rows.Select(Message).ToArray();
            if (messages.Select(m => m.id).Distinct().Count() != messages.Length || messages.Any(m => m.channelId != c.id || !ids.Contains(m.id) || c.kind != "project" && Millis(m.expiresAt) - Millis(m.createdAt) > 30000)) throw new FormatException("Invalid Chat snapshot");
            return new ChatSynced { channel = c, visibleMessageIds = ids, messages = messages, moderation = Moderation(f["moderation"]), serverTime = Time(f["serverTime"]) };
        }
        internal static ChatSent Sent(string raw)
        { var f = Fields(raw, "receipt,replayed,serverTime"); return new ChatSent { receipt = Receipt(f["receipt"]), replayed = Bool(f["replayed"]), serverTime = Time(f["serverTime"]) }; }
        internal static ChatOperation Operation(string raw)
        { var f = Fields(raw, "receipt,serverTime"); return new ChatOperation { receipt = Receipt(f["receipt"]), serverTime = Time(f["serverTime"]) }; }
        internal static ChatReported Reported(string raw)
        {
            var f = Fields(raw, "reportId,status,replayed,serverTime"); string s = JsonFragments.String(f["status"]);
            if (!new[] { "open", "dismissed", "actioned" }.Contains(s)) throw new FormatException("Invalid Chat report status");
            return new ChatReported { reportId = Uuid(f["reportId"]), status = s, replayed = Bool(f["replayed"]), serverTime = Time(f["serverTime"]) };
        }
        internal static string MessageJson(ChatMessage m) => "{\"id\":" + Quote(Id(m.id)) + ",\"channelId\":" + Quote(Id(m.channelId)) + ",\"authorId\":" + Quote(Id(m.authorId)) + ",\"text\":" + Quote(Text(m.text)) + ",\"createdAt\":" + Quote(Time(Quote(m.createdAt))) + ",\"expiresAt\":" + Quote(Time(Quote(m.expiresAt))) + "}";
        internal static ChatMessage Copy(ChatMessage m) => new ChatMessage { id = m.id, channelId = m.channelId, authorId = m.authorId, text = m.text, createdAt = m.createdAt, expiresAt = m.expiresAt };
        internal static ChatChannel Copy(ChatChannel c) => new ChatChannel { id = c.id, epoch = c.epoch, kind = c.kind, peerIds = (string[])c.peerIds.Clone(), groupId = c.groupId, revision = c.revision, expiresAt = c.expiresAt };
        internal static ChatSynced Copy(ChatSynced s) => new ChatSynced { channel = Copy(s.channel), visibleMessageIds = (string[])s.visibleMessageIds.Clone(), messages = s.messages.Select(Copy).ToArray(), moderation = new ChatModeration { muteUntil = s.moderation.muteUntil, banUntil = s.moderation.banUntil }, serverTime = s.serverTime };
    }
}
