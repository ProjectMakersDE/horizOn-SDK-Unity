using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Transport;
using UnityEngine;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>One unresolved original send intent per project/player. No item, fee or completed-gift archive.</summary>
    public sealed class ItemGiftsSampleStore
    {
        [Serializable] private sealed class Envelope { public string payload, hash; }
        [Serializable] private sealed class State
        {
            public int schema;
            public string projectId, userId, originalJson, phase, confirmedGiftId;
        }
        public sealed class Prepared
        {
            public ItemGiftIntent Intent { get; }
            public bool Confirmed { get; }
            public string ConfirmedGiftId { get; }
            internal Prepared(ItemGiftIntent intent, bool confirmed, string gift) { Intent = intent; Confirmed = confirmed; ConfirmedGiftId = gift; }
        }
        private readonly string _root, _path, _project, _user;
        public ItemGiftsSampleStore(string directory, string projectId, string userId)
        {
            _project = Guid.ParseExact(projectId, "D").ToString("D"); _user = Guid.ParseExact(userId, "D").ToString("D");
            _root = directory ?? throw new ArgumentNullException(nameof(directory));
            _path = Path.Combine(directory, "item-gift-" + Hash(_project + "|" + _user) + ".json");
        }
        public bool HasPending => File.Exists(_path) || File.Exists(_path + ".next");
        public SendLease BeginNew(ItemGiftIntent original)
        {
            var gate = OpenGate();
            try
            {
                RecoverNext(); if (HasPending) throw new IOException("Reconcile the saved original item gift first");
                if (original == null || original.UserId != _user) throw new IOException("Item gift original belongs to another player");
                var state = new State { schema = 1, projectId = _project, userId = _user, originalJson = original.Json, phase = "intent", confirmedGiftId = "" };
                string raw = Encode(state); WriteNew(_path, raw);
                return new SendLease(this, gate, Decode(ReadDurable(_path)), raw);
            }
            catch { gate.Dispose(); throw; }
        }
        public SendLease AcquireSend()
        {
            var gate = OpenGate();
            try { RecoverNext(); string raw = ReadDurable(_path); return new SendLease(this, gate, Decode(raw), raw); }
            catch { gate.Dispose(); throw; }
        }
        /// <summary>Hold this OS-exclusive handle across every awaited send, read and confirmation.</summary>
        public sealed class SendLease : IDisposable
        {
            private ItemGiftsSampleStore _owner;
            private FileStream _gate;
            private string _currentRaw;
            public Prepared Original { get; private set; }
            internal SendLease(ItemGiftsSampleStore owner, FileStream gate, Prepared prepared, string raw) { _owner = owner; _gate = gate; Original = prepared; _currentRaw = raw; }
            private ItemGiftsSampleStore Owner => _owner ?? throw new ObjectDisposedException(nameof(SendLease));
            public void MarkConfirmed(ItemGiftReceipt receipt)
            {
                var owner = Owner;
                if (!Original.Intent.Matches(receipt)) throw new IOException("Item gift response does not bind the entire original identity");
                if (ReadDurable(owner._path) != _currentRaw) throw new IOException("Item gift original changed during HTTP");
                if (Original.Confirmed)
                {
                    if (Original.ConfirmedGiftId != receipt.giftId) throw new IOException("Item gift known result changed"); return;
                }
                var state = owner.ParseState(_currentRaw); state.phase = "confirmed"; state.confirmedGiftId = receipt.giftId;
                string next = owner.Encode(state);
                WriteNew(owner._path + ".next", next);
                File.Replace(owner._path + ".next", owner._path, null);
                if (ReadDurable(owner._path) != next) throw new IOException("Item gift confirmation readback failed");
                _currentRaw = next; Original = owner.Decode(next);
            }
            /// <summary>Only after explicit successful current Inventory/Values/item gift reads. Removes this one resolved intent.</summary>
            public void AcknowledgeAfterRefresh()
            {
                var owner = Owner;
                if (!Original.Confirmed || ReadDurable(owner._path) != _currentRaw || File.Exists(owner._path + ".next")) throw new IOException("Unconfirmed or changed item gift intent cannot be cleared");
                File.Delete(owner._path);
                if (File.Exists(owner._path)) throw new IOException("Item gift acknowledgement did not persist");
            }
            public void Dispose() { _owner = null; _gate?.Dispose(); _gate = null; }
        }
        private State ParseState(string raw)
        {
            Envelope envelope;
            try { envelope = JsonUtility.FromJson<Envelope>(raw); }
            catch (Exception e) { throw new IOException("Corrupt item gift journal; preserve it", e); }
            if (envelope?.payload == null || envelope.hash != Hash(envelope.payload) || JsonUtility.ToJson(envelope) != raw) throw new IOException("Unknown item gift journal envelope");
            var state = JsonUtility.FromJson<State>(envelope.payload);
            if (state == null || JsonUtility.ToJson(state) != envelope.payload || state.schema != 1 || state.projectId != _project || state.userId != _user || state.phase != "intent" && state.phase != "confirmed") throw new IOException("Item gift journal scope/schema invalid");
            var original = ItemGiftIntent.Restore(state.originalJson);
            if (original.UserId != _user || state.phase == "intent" && state.confirmedGiftId != "" || state.phase == "confirmed" && !CanonicalUuid(state.confirmedGiftId)) throw new IOException("Item gift journal original/result mismatch");
            return state;
        }
        private Prepared Decode(string raw)
        {
            var state = ParseState(raw);
            return new Prepared(ItemGiftIntent.Restore(state.originalJson), state.phase == "confirmed", state.confirmedGiftId);
        }
        private string Encode(State state)
        {
            string payload = JsonUtility.ToJson(state), raw = JsonUtility.ToJson(new Envelope { payload = payload, hash = Hash(payload) });
            ParseState(raw); return raw;
        }
        private void RecoverNext()
        {
            string path = _path + ".next";
            if (!File.Exists(path)) return;
            string proposedRaw = ReadDurable(path), currentRaw = ReadDurable(_path);
            var proposed = ParseState(proposedRaw); var current = ParseState(currentRaw);
            if (proposed.phase != "confirmed" || proposed.originalJson != current.originalJson || current.phase == "confirmed" && proposed.confirmedGiftId != current.confirmedGiftId) throw new IOException("Interrupted item gift confirmation conflicts; preserve both files");
            File.Replace(path, _path, null);
            if (ReadDurable(_path) != proposedRaw) throw new IOException("Item gift interrupted result recovery failed");
        }
        private FileStream OpenGate()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new NotSupportedException("Provide a game-owned durable browser journal with exclusive send ownership before item gift sends");
#else
            Directory.CreateDirectory(_root);
            // Keep the inode outside the pending file. Never unlink another process opener's lock.
            return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
#endif
        }
        private static bool CanonicalUuid(string text) => text != null && Guid.TryParseExact(text, "D", out var id) && id.ToString("D") == text;
        private static string Hash(string value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        private static void WriteNew(string path, string raw)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(raw);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
            if (File.ReadAllText(path, Encoding.UTF8) != raw) throw new IOException("Item gift original write/readback failed");
        }
        private static string ReadDurable(string path)
        {
            string raw = File.ReadAllText(path, Encoding.UTF8);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) file.Flush(true);
            if (File.ReadAllText(path, Encoding.UTF8) != raw) throw new IOException("Item gift journal changed during readback");
            return raw;
        }
    }
}
