using System;
using System.IO;
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
        public sealed class PreparedIntent { public Intent Value { get; } public string Hash { get; } internal PreparedIntent(Intent value, string hash) { Value = value; Hash = hash; } }
        private readonly string _directory, _path, _root, _project, _user;
        public ItemsSampleStore(string directory, string projectId, string userId, string apiKey)
        {
            if (!Uuid(projectId) || !Uuid(userId) || string.IsNullOrEmpty(apiKey)) throw new ArgumentException("Stable project/player UUIDs and current project key required");
            _project = Guid.ParseExact(projectId, "D").ToString("D"); _user = Guid.ParseExact(userId, "D").ToString("D");
            _root = directory; _directory = Path.Combine(directory, FileName(apiKey, _user)); _path = Path.Combine(_directory, "intent.json");
        }
        public static string FileName(string apiKey, string userId) => "items-" + Hash(apiKey) + "-" + userId.ToLowerInvariant();
        public bool HasIntent => File.Exists(_path);
        public bool HasSuccess => File.Exists(Path.Combine(_directory, "result.json"));
        public static string Hash(string text) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        private Envelope ReadEnvelope()
        {
            string raw = File.ReadAllText(_path, Encoding.UTF8);
            if (Encoding.UTF8.GetByteCount(raw) > 32768) throw new IOException("Items intent exceeds limit");
            var envelope = JsonUtility.FromJson<Envelope>(raw);
            if (envelope?.payload == null || envelope.hash != Hash(envelope.payload)) throw new IOException("Unknown/corrupt Items command; preserve it for reconciliation");
            return envelope;
        }
        public Intent Load()
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
        public PreparedIntent PrepareSend()
        {
            PreparedIntent result = null;
            WithLock(() =>
            {
                var envelope = ReadEnvelope(); var value = Load(); DurableRead(_path);
                if (ReadEnvelope().hash != envelope.hash) throw new IOException("Original Items command changed during verification");
                result = new PreparedIntent(value, envelope.hash);
            });
            return result;
        }
        public Receipt ReadSuccess(string originalHash)
        {
            var receipt = JsonUtility.FromJson<Receipt>(DurableRead(Path.Combine(_directory, "result.json")));
            var intent = Load();
            if (ReadEnvelope().hash != originalHash || receipt == null || receipt.intentHash != originalHash || receipt.action != intent.action
                || !receipt.serverConfirmed && (intent.action == "purchase" || receipt.resolution != "player-reviewed-current-inventory" || receipt.observedRevision < 0 || receipt.observedRevision > 9007199254740991L)
                || intent.action == "purchase" && receipt.operationId != intent.operationId) throw new IOException("Items success does not match the original command");
            return receipt;
        }
        public void SaveSuccess(string originalHash, string operationId, ItemReference[] items)
        {
            WithLock(() =>
            {
                if (ReadEnvelope().hash != originalHash) throw new IOException("Items command changed during HTTP");
                var intent = Load();
                if (intent.action == "purchase" && (operationId != intent.operationId || items == null)) throw new IOException("Items purchase receipt identity missing");
                WriteReceipt(new Receipt { intentHash = originalHash, action = intent.action, operationId = operationId, serverConfirmed = true, items = items ?? Array.Empty<ItemReference>() });
            });
        }
        /// <summary>Explicit player/game reconciliation after a fresh inventory review, not a claim of server success. Never closes an uncertain purchase.</summary>
        public void SaveReconciledOriginal(string originalHash, long observedRevision)
        {
            WithLock(() =>
            {
                var intent = Load();
                if (ReadEnvelope().hash != originalHash || intent.action == "purchase" || observedRevision < 0 || observedRevision > 9007199254740991L || HasSuccess) throw new IOException("Cannot reconcile this original Items action");
                WriteReceipt(new Receipt { intentHash = originalHash, action = intent.action, resolution = "player-reviewed-current-inventory", observedRevision = observedRevision, items = Array.Empty<ItemReference>() });
            });
        }
        private void WriteReceipt(Receipt receipt)
        {
            string path = Path.Combine(_directory, "result.json"), json = JsonUtility.ToJson(receipt);
            if (HasSuccess) { if (DurableRead(path) != json) throw new IOException("A different Items result is already saved"); return; }
            // A crash/full disk leaves an unpublished writing file, never a partial result marker.
            string temporary = Path.Combine(_directory, "result." + Guid.NewGuid().ToString("N") + ".writing");
            WriteNew(temporary, json); File.Move(temporary, path); DurableRead(path);
        }
        /// <summary>Explicit game acknowledgement preserves both files in a local archive. Production games own archive retention.</summary>
        public void AcknowledgeSavedSuccess()
        {
            WithLock(() =>
            {
                var original = ReadEnvelope(); DurableRead(_path); ReadSuccess(original.hash);
                // One directory rename archives original and result together: interruption leaves either the complete active journal or the complete archive.
                string archive = _directory + "." + original.hash + "." + Guid.NewGuid().ToString("N") + ".acknowledged";
                Directory.Move(_directory, archive);
            });
        }
        private void WithLock(Action action)
        {
            Directory.CreateDirectory(_root);
            // Persistent lock inode, exclusive OS handle: a terminated process releases it automatically. Never unlink another opener's lock.
            using (var gate = new FileStream(_directory + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) action();
        }
        private static void WriteNew(string path, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json); if (bytes.Length > 32768) throw new IOException("Items journal exceeds limit");
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
            if (File.ReadAllText(path, Encoding.UTF8) != json) throw new IOException("Items journal readback failed");
        }
        private static string DurableRead(string path)
        {
            string original = File.ReadAllText(path, Encoding.UTF8);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) file.Flush(true);
            if (File.ReadAllText(path, Encoding.UTF8) != original) throw new IOException("Items journal changed during durable readback");
            return original;
        }
        private static bool Uuid(string s) => s != null && Regex.IsMatch(s, @"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z");
    }
}
