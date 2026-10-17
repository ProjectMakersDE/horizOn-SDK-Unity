using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Helper
{
    /// <summary>Device-only append history. Rebuild the small ID/offset index once per opened channel,
    /// then scan only newly appended records. Reads and writes never load all message text at once.</summary>
    internal sealed class ChatHistoryStore
    {
        private const int RecordBytes = 8192;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly string _directory;
        private readonly Dictionary<string, Index> _indices = new Dictionary<string, Index>();
        private sealed class Entry { internal long Offset; internal string Hash; }
        private sealed class Index
        {
            internal string Generation; internal long Position;
            internal readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
            internal readonly List<long> Offsets = new List<long>();
        }
        internal ChatHistoryStore(string root, string apiKey, string userId)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(apiKey)) throw new ArgumentException("Local history scope missing");
            ChatJson.Id(userId);
            _directory = Path.Combine(root, Hash(apiKey.Length.ToString(CultureInfo.InvariantCulture) + ":" + apiKey + ":" + userId));
        }
        private FileStream Lock()
        {
            Directory.CreateDirectory(_directory);
            return new FileStream(Path.Combine(_directory, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        private string PathFor(string channel) => Path.Combine(_directory, ChatJson.Id(channel) + ".jsonl");
        internal bool Append(string channel, IEnumerable<ChatMessage> messages)
        {
            var rows = messages.Select(m => ChatJson.Message(ChatJson.MessageJson(m))).ToArray();
            if (rows.Length > 20 || rows.Any(m => m.channelId != channel)) throw new FormatException("Invalid local history batch");
            using (Lock())
            using (var file = new FileStream(PathFor(channel), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                if (file.Length == 0)
                { Write(file, "{\"version\":1,\"generation\":\"" + Guid.NewGuid().ToString("D") + "\"}\n"); file.Flush(true); }
                var index = Load(file, channel);
                // Validate the whole batch before writing any of it.
                var pending = new Dictionary<string, string>(); var additions = new List<(ChatMessage message, string json, string hash)>();
                foreach (var row in rows)
                {
                    string json = ChatJson.MessageJson(row), hash = Hash(json);
                    if (index.Entries.TryGetValue(row.id, out var previous))
                    { if (previous.Hash != hash) throw new InvalidDataException("LOCAL_HISTORY_CONFLICT"); continue; }
                    if (pending.TryGetValue(row.id, out var duplicate))
                    { if (duplicate != hash) throw new InvalidDataException("LOCAL_HISTORY_CONFLICT"); continue; }
                    pending[row.id] = hash; additions.Add((row, json, hash));
                }
                file.Position = index.Position;
                foreach (var row in additions)
                {
                    long offset = file.Position;
                    Write(file, row.json + "\n"); file.Flush(true);
                    index.Entries.Add(row.message.id, new Entry { Offset = offset, Hash = row.hash });
                    index.Offsets.Add(offset); index.Position = file.Position;
                }
                return additions.Count != 0;
            }
        }
        internal ChatHistoryPage Read(string channel, string cursor = null, int limit = 50)
        {
            if (limit < 1 || limit > 100) throw new ArgumentOutOfRangeException(nameof(limit));
            using (Lock())
            {
                string path = PathFor(channel);
                if (!File.Exists(path))
                { if (cursor != null) throw new InvalidDataException("LOCAL_HISTORY_CURSOR_EXPIRED"); return new ChatHistoryPage(); }
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var index = Load(file, channel); long before = file.Length;
                    if (cursor != null)
                    {
                        var parts = cursor.Split(':');
                        if (parts.Length != 2 || parts[0] != index.Generation || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out before)
                            || before < 0 || before > file.Length || !index.Offsets.Contains(before)) throw new InvalidDataException("LOCAL_HISTORY_CURSOR_EXPIRED");
                    }
                    int at = index.Offsets.BinarySearch(before); if (at < 0) at = ~at; at--;
                    var result = new List<ChatMessage>(); long lastOffset = 0;
                    for (; at >= 0 && result.Count < limit; at--)
                    {
                        lastOffset = index.Offsets[at]; file.Position = lastOffset;
                        result.Add(ChatJson.Message(Line(file)));
                    }
                    return new ChatHistoryPage { messages = result.ToArray(), nextCursor = at >= 0 ? index.Generation + ":" + lastOffset.ToString(CultureInfo.InvariantCulture) : null };
                }
            }
        }
        internal void Clear(string channel)
        { using (Lock()) { File.Delete(PathFor(channel)); _indices.Remove(channel); } }
        internal void ClearAll()
        {
            using (Lock())
            {
                foreach (string path in Directory.EnumerateFiles(_directory, "*.jsonl"))
                    if (FriendsJson.IsUuid(Path.GetFileNameWithoutExtension(path))) File.Delete(path);
                _indices.Clear();
            }
        }
        private Index Load(FileStream file, string channel)
        {
            file.Position = 0; var header = ChatJson.Fields(Line(file), "version,generation");
            if (header["version"] != "1") throw new InvalidDataException("LOCAL_HISTORY_INVALID");
            string generation = ChatJson.Id(JsonFragments.String(header["generation"]));
            if (!_indices.TryGetValue(channel, out var index) || index.Generation != generation || index.Position > file.Length)
            { index = new Index { Generation = generation, Position = file.Position }; _indices[channel] = index; }
            file.Position = index.Position;
            while (file.Position < file.Length)
            {
                long offset = file.Position; string raw = Line(file); var row = ChatJson.Message(raw);
                if (row.channelId != channel) throw new InvalidDataException("LOCAL_HISTORY_INVALID");
                string hash = Hash(ChatJson.MessageJson(row));
                if (index.Entries.ContainsKey(row.id)) throw new InvalidDataException("LOCAL_HISTORY_CONFLICT");
                index.Entries.Add(row.id, new Entry { Offset = offset, Hash = hash }); index.Offsets.Add(offset); index.Position = file.Position;
            }
            return index;
        }
        private static string Line(FileStream file)
        {
            var bytes = new List<byte>(512); int value;
            while ((value = file.ReadByte()) >= 0)
            {
                if (value == 10) return Utf8.GetString(bytes.ToArray());
                if (bytes.Count == RecordBytes) throw new InvalidDataException("LOCAL_HISTORY_INVALID"); bytes.Add((byte)value);
            }
            // A partial append is visible as a storage error, never silently accepted or overwritten.
            throw new InvalidDataException("LOCAL_HISTORY_INCOMPLETE");
        }
        private static void Write(FileStream file, string text)
        { byte[] bytes = Utf8.GetBytes(text); if (bytes.Length > RecordBytes) throw new InvalidDataException("LOCAL_HISTORY_INVALID"); file.Write(bytes, 0, bytes.Length); }
        internal static string Hash(string value)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
    }
}
