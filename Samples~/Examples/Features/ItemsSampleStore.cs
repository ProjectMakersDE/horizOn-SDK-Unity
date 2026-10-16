using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Sample durable original command and confirmed-result journal, scoped by stable project/player, never regional host.</summary>
    public sealed class ItemsSampleStore
    {
        [Serializable] public class Intent
        {
            public string projectId, userId, action, offerKey, operationId, offerHash, instanceId;
            public long revision;
            public bool equipped;
            public ItemPropertyDelta[] changes;
        }
        [Serializable] private class Envelope { public string payload, hash; }
        [Serializable] public class Receipt { public string intentHash, action, operationId, resolution; public bool serverConfirmed; public long observedRevision; public ItemReference[] items; }
        public sealed class PreparedIntent { public Intent Value { get; } public string Hash { get; } public bool HasSuccess { get; } internal PreparedIntent(Intent value, string hash, bool success) { Value = value; Hash = hash; HasSuccess = success; } }
        private readonly string _directory, _path, _root, _project, _user;
        public ItemsSampleStore(string directory, string projectId, string userId, string apiKey)
        {
            if (!Uuid(projectId) || !Uuid(userId) || string.IsNullOrEmpty(apiKey)) throw new ArgumentException("Stable project/player UUIDs and current project key required");
            _project = Guid.ParseExact(projectId, "D").ToString("D"); _user = Guid.ParseExact(userId, "D").ToString("D");
            _root = directory; _directory = Path.Combine(directory, FileName(apiKey, _user)); _path = Path.Combine(_directory, "intent.json");
        }
        public static string FileName(string apiKey, string userId) => "items-" + Hash(apiKey) + "-" + userId.ToLowerInvariant();
        public bool HasIntent => Directory.Exists(_directory) && Directory.GetFileSystemEntries(_directory).Length != 0;
        public bool HasSuccess => File.Exists(Path.Combine(_directory, "result.json"));
        public static string Hash(string text) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        private Envelope ReadEnvelope()
        {
            string raw = DurableRead(_path);
            if (Encoding.UTF8.GetByteCount(raw) > 32768) throw new IOException("Items intent exceeds limit");
            var envelope = JsonUtility.FromJson<Envelope>(raw);
            if (envelope?.payload == null || envelope.hash != Hash(envelope.payload) || JsonUtility.ToJson(envelope) != raw) throw new IOException("Unknown/corrupt Items command; preserve it for reconciliation");
            return envelope;
        }
        private Intent LoadCore()
        {
            var x = JsonUtility.FromJson<Intent>(ReadEnvelope().payload);
            if (x == null || x.projectId != _project || x.userId != _user || Array.IndexOf(new[] { "purchase", "equip", "adjust", "discard" }, x.action) < 0)
                throw new IOException("Items command scope/action invalid");
            return x;
        }
        public void SaveNew(Intent value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new NotSupportedException("Use a game-owned durable browser journal before Items mutations or purchases");
#else
            if (value == null || !string.Equals(value.projectId, _project, StringComparison.OrdinalIgnoreCase) || !string.Equals(value.userId, _user, StringComparison.OrdinalIgnoreCase)) throw new IOException("Items command scope mismatch");
            WithLock(() =>
            {
                if (Directory.Exists(_directory) && Directory.GetFileSystemEntries(_directory).Length != 0) throw new IOException("Reconcile the saved original Items command first");
                Directory.CreateDirectory(_directory);
                value.projectId = _project; value.userId = _user;
                string payload = JsonUtility.ToJson(value);
                WriteNew(_path, JsonUtility.ToJson(new Envelope { payload = payload, hash = Hash(payload) }));
            });
#endif
        }
        public Intent Load() { Intent value = null; WithLock(() => value = LoadCore()); return value; }
        public PreparedIntent PrepareSend() { PreparedIntent value = null; WithLock(() => value = ReadIntent()); return value; }
        /// <summary>The exclusive handle must remain alive across every awaited request and result/ACK persistence.</summary>
        public SendLease AcquireSend()
        {
            var gate = OpenGate();
            try { RecoverWritingResult(); return new SendLease(this, gate, ReadIntent()); }
            catch { gate.Dispose(); throw; }
        }
        public sealed class SendLease : IDisposable
        {
            private ItemsSampleStore _owner; private FileStream _gate;
            public PreparedIntent Prepared { get; }
            internal SendLease(ItemsSampleStore owner, FileStream gate, PreparedIntent prepared) { _owner = owner; _gate = gate; Prepared = prepared; }
            private ItemsSampleStore Owner => _owner ?? throw new ObjectDisposedException(nameof(SendLease));
            public Receipt ReadSuccess() => Owner.ReadSuccessCore(Prepared.Hash);
            public void SaveSuccess(string operationId, ItemReference[] items) => Owner.SaveSuccessCore(Prepared.Hash, operationId, items);
            public void SaveReconciledOriginal(long observedRevision) => Owner.SaveReconciledCore(Prepared.Hash, observedRevision);
            public void AcknowledgeSavedSuccess() => Owner.AcknowledgeCore(Prepared.Hash);
            public void Dispose() { _owner = null; _gate?.Dispose(); _gate = null; }
        }
        private PreparedIntent ReadIntent()
        {
            var envelope = ReadEnvelope(); var value = LoadCore();
            if (ReadEnvelope().hash != envelope.hash) throw new IOException("Original Items command changed during verification");
            var prepared = new PreparedIntent(value, envelope.hash, HasSuccess);
            if (prepared.HasSuccess) ReadReceipt(DurableRead(Path.Combine(_directory, "result.json")), prepared);
            return prepared;
        }
        public Receipt ReadSuccess(string originalHash) { Receipt value = null; WithLock(() => value = ReadSuccessCore(originalHash)); return value; }
        private Receipt ReadSuccessCore(string originalHash)
        {
            var prepared = ReadIntent();
            if (prepared.Hash != originalHash) throw new IOException("Items command changed before receipt review");
            return ReadReceipt(DurableRead(Path.Combine(_directory, "result.json")), prepared);
        }
        private Receipt ReadReceipt(string raw, PreparedIntent original)
        {
            Receipt receipt;
            try { receipt = JsonUtility.FromJson<Receipt>(raw); }
            catch (Exception e) { throw new IOException("Unresolved Items receipt", e); }
            var intent = original.Value;
            if (receipt == null || JsonUtility.ToJson(receipt) != raw || receipt.intentHash != original.Hash || receipt.action != intent.action
                || receipt.items == null || receipt.items.Any(x => x == null || !Uuid(x.instanceId) || string.IsNullOrEmpty(x.itemKey))
                || !receipt.serverConfirmed && (intent.action == "purchase" || receipt.resolution != "player-reviewed-current-inventory" || receipt.observedRevision <= intent.revision || receipt.observedRevision > 9007199254740991L)
                || intent.action == "purchase" && receipt.operationId != intent.operationId) throw new IOException("Items success does not match or permanently fence the original command");
            return receipt;
        }
        public void SaveSuccess(string originalHash, string operationId, ItemReference[] items) => WithLock(() => SaveSuccessCore(originalHash, operationId, items));
        private void SaveSuccessCore(string originalHash, string operationId, ItemReference[] items)
        {
            var original = ReadIntent();
            if (original.Hash != originalHash) throw new IOException("Items command changed during HTTP");
            var intent = original.Value;
            if (intent.action == "purchase" && (operationId != intent.operationId || items == null)) throw new IOException("Items purchase receipt identity missing");
            WriteReceipt(new Receipt { intentHash = originalHash, action = intent.action, operationId = operationId, serverConfirmed = true, items = items ?? Array.Empty<ItemReference>() });
        }
        /// <summary>Explicit reviewed snapshot must permanently fence the old inventory CAS. Never closes an uncertain purchase.</summary>
        public void SaveReconciledOriginal(string originalHash, long observedRevision) => WithLock(() => SaveReconciledCore(originalHash, observedRevision));
        private void SaveReconciledCore(string originalHash, long observedRevision)
        {
            var original = ReadIntent(); var intent = original.Value;
            if (original.Hash != originalHash || intent.action == "purchase" || observedRevision <= intent.revision || observedRevision > 9007199254740991L || HasSuccess) throw new IOException("Cannot reconcile an unfenced original Items action");
            WriteReceipt(new Receipt { intentHash = originalHash, action = intent.action, resolution = "player-reviewed-current-inventory", observedRevision = observedRevision, items = Array.Empty<ItemReference>() });
        }
        private void WriteReceipt(Receipt receipt)
        {
            string path = Path.Combine(_directory, "result.json"), json = JsonUtility.ToJson(receipt); ReadReceipt(json, ReadIntent());
            if (HasSuccess) { if (DurableRead(path) != json) throw new IOException("A different Items result is already saved"); return; }
            string temporary = Path.Combine(_directory, "result." + Guid.NewGuid().ToString("N") + ".writing");
            WriteNew(temporary, json); File.Move(temporary, path); DurableRead(path);
        }
        private void RecoverWritingResult()
        {
            if (!Directory.Exists(_directory)) return;
            string[] files = Directory.GetFiles(_directory, "result.*.writing"); if (files.Length == 0) return;
            var original = ReadIntent(); string canonical = null;
            foreach (string file in files)
            {
                string raw = DurableRead(file); ReadReceipt(raw, original);
                if (canonical != null && canonical != raw) throw new IOException("Conflicting interrupted Items results"); canonical = raw;
            }
            string result = Path.Combine(_directory, "result.json");
            if (File.Exists(result)) { if (DurableRead(result) != canonical) throw new IOException("Interrupted Items result differs"); }
            else { File.Move(files[0], result); DurableRead(result); }
        }
        /// <summary>Archives the complete original/result directory atomically. Callers reviewing a receipt should use its SendLease.</summary>
        public void AcknowledgeSavedSuccess() => WithLock(() => AcknowledgeCore(ReadIntent().Hash));
        private void AcknowledgeCore(string originalHash)
        {
            ReadSuccessCore(originalHash);
            Directory.Move(_directory, _directory + "." + originalHash + "." + Guid.NewGuid().ToString("N") + ".acknowledged");
        }
        private FileStream OpenGate()
        {
            Directory.CreateDirectory(_root);
            // Persistent inode outside the atomically archived directory. Never unlink a competing opener's lock.
            return new FileStream(_directory + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        private void WithLock(Action action) { using (var gate = OpenGate()) { RecoverWritingResult(); action(); } }
        private static void WriteNew(string path, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json); if (bytes.Length > 32768) throw new IOException("Items journal exceeds limit");
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
            if (File.ReadAllText(path, Encoding.UTF8) != json) throw new IOException("Items journal readback failed");
        }
        private static string DurableRead(string path)
        {
            if (new FileInfo(path).Length > 32768) throw new IOException("Items journal exceeds limit");
            string original = File.ReadAllText(path, Encoding.UTF8);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) file.Flush(true);
            if (File.ReadAllText(path, Encoding.UTF8) != original) throw new IOException("Items journal changed during durable readback");
            return original;
        }
        private static bool Uuid(string s) => s != null && Regex.IsMatch(s, @"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z");
    }
}
