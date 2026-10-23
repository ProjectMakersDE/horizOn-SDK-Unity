using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Transport;
using UnityEngine;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>One unresolved original tuple. No financial/item result archive or completed-purchase history.</summary>
    public sealed class MarketplaceSampleStore
    {
        [Serializable] private sealed class Envelope { public string payload, hash; }
        [Serializable] private sealed class State
        {
            public int schema;
            public string projectId, userId, kind, listingId, originalJson, phase, confirmedListingId;
        }
        public sealed class Prepared
        {
            public MarketplaceIntent Intent { get; }
            public bool Confirmed { get; }
            public string ConfirmedListingId { get; }
            internal Prepared(MarketplaceIntent intent, bool confirmed, string listing) { Intent = intent; Confirmed = confirmed; ConfirmedListingId = listing; }
        }
        private readonly string _root, _path, _project, _user;
        public MarketplaceSampleStore(string directory, string projectId, string userId)
        {
            _project = Guid.ParseExact(projectId, "D").ToString("D"); _user = Guid.ParseExact(userId, "D").ToString("D");
            _root = directory ?? throw new ArgumentNullException(nameof(directory));
            _path = Path.Combine(directory, "marketplace-" + Hash(_project + "|" + _user) + ".json");
        }
        public bool HasPending => File.Exists(_path) || File.Exists(_path + ".next");
        public SendLease BeginNew(MarketplaceIntent original)
        {
            var gate = OpenGate();
            try
            {
                RecoverNext(); if (HasPending) throw new IOException("Reconcile the saved original Marketplace intent first");
                if (original == null || original.UserId != _user) throw new IOException("Marketplace original belongs to another player");
                var state = new State { schema = 1, projectId = _project, userId = _user, kind = original.Kind, listingId = original.ListingId, originalJson = original.Json, phase = "intent", confirmedListingId = "" };
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
            private MarketplaceSampleStore _owner;
            private FileStream _gate;
            private string _currentRaw;
            public Prepared Original { get; private set; }
            internal SendLease(MarketplaceSampleStore owner, FileStream gate, Prepared prepared, string raw) { _owner = owner; _gate = gate; Original = prepared; _currentRaw = raw; }
            private MarketplaceSampleStore Owner => _owner ?? throw new ObjectDisposedException(nameof(SendLease));
            public void MarkConfirmed(MarketplaceReceipt receipt)
            {
                var owner = Owner;
                if (!Original.Intent.Matches(receipt)) throw new IOException("Marketplace response does not bind the entire original identity");
                if (ReadDurable(owner._path) != _currentRaw) throw new IOException("Marketplace original changed during HTTP");
                if (Original.Confirmed)
                {
                    if (Original.ConfirmedListingId != receipt.listingId) throw new IOException("Marketplace known result changed"); return;
                }
                var state = owner.ParseState(_currentRaw); state.phase = "confirmed"; state.confirmedListingId = receipt.listingId;
                string next = owner.Encode(state);
                WriteNew(owner._path + ".next", next);
                File.Replace(owner._path + ".next", owner._path, null);
                if (ReadDurable(owner._path) != next) throw new IOException("Marketplace confirmation readback failed");
                _currentRaw = next; Original = owner.Decode(next);
            }
            /// <summary>Only after explicit successful current Inventory/Values/Marketplace reads. Removes this one resolved intent.</summary>
            public void AcknowledgeAfterRefresh()
            {
                var owner = Owner;
                if (!Original.Confirmed || ReadDurable(owner._path) != _currentRaw || File.Exists(owner._path + ".next")) throw new IOException("Unconfirmed or changed Marketplace intent cannot be cleared");
                File.Delete(owner._path);
                if (File.Exists(owner._path)) throw new IOException("Marketplace acknowledgement did not persist");
            }
            public void Dispose() { _owner = null; _gate?.Dispose(); _gate = null; }
        }
        private State ParseState(string raw)
        {
            Envelope envelope;
            try { envelope = JsonUtility.FromJson<Envelope>(raw); }
            catch (Exception e) { throw new IOException("Corrupt Marketplace journal; preserve it", e); }
            if (envelope?.payload == null || envelope.hash != Hash(envelope.payload) || JsonUtility.ToJson(envelope) != raw) throw new IOException("Unknown Marketplace journal envelope");
            var state = JsonUtility.FromJson<State>(envelope.payload);
            if (state == null || JsonUtility.ToJson(state) != envelope.payload || state.schema != 1 || state.projectId != _project || state.userId != _user || state.phase != "intent" && state.phase != "confirmed") throw new IOException("Marketplace journal scope/schema invalid");
            var original = MarketplaceIntent.Restore(state.kind, state.listingId, state.originalJson);
            if (original.UserId != _user || state.phase == "intent" && state.confirmedListingId != "" || state.phase == "confirmed" && (!CanonicalUuid(state.confirmedListingId) || original.Kind != "create" && state.confirmedListingId != original.ListingId)) throw new IOException("Marketplace journal original/result mismatch");
            return state;
        }
        private Prepared Decode(string raw)
        {
            var state = ParseState(raw);
            return new Prepared(MarketplaceIntent.Restore(state.kind, state.listingId, state.originalJson), state.phase == "confirmed", state.confirmedListingId);
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
            if (proposed.phase != "confirmed" || proposed.kind != current.kind || proposed.listingId != current.listingId || proposed.originalJson != current.originalJson || current.phase == "confirmed" && proposed.confirmedListingId != current.confirmedListingId) throw new IOException("Interrupted Marketplace confirmation conflicts; preserve both files");
            File.Replace(path, _path, null);
            if (ReadDurable(_path) != proposedRaw) throw new IOException("Marketplace interrupted result recovery failed");
        }
        private FileStream OpenGate()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new NotSupportedException("Provide a game-owned durable browser journal with exclusive send ownership before Marketplace writes");
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
            if (File.ReadAllText(path, Encoding.UTF8) != raw) throw new IOException("Marketplace original write/readback failed");
        }
        private static string ReadDurable(string path)
        {
            string raw = File.ReadAllText(path, Encoding.UTF8);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) file.Flush(true);
            if (File.ReadAllText(path, Encoding.UTF8) != raw) throw new IOException("Marketplace journal changed during readback");
            return raw;
        }
    }
}
