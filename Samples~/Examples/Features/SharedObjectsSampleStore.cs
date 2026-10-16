using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Game-owned sample journal: stable project key/player scope, never regional host.
    /// A persistent exclusive OS handle owns prepare/HTTP/result; a sent CREATE is never sent again.</summary>
    public sealed class SharedObjectsSampleStore
    {
        [Serializable] public class Search { public string key, stringBase64; public bool isString; public long integer; }
        [Serializable] public class Intent
        {
            public string projectId, userId, action, collection, objectId, groupId, dataBase64;
            public long revision, x, y;
            public bool hasPosition;
            public Search[] search = Array.Empty<Search>();
            public SharedObjectInput GetInput() => new SharedObjectInput
            {
                data = Convert.FromBase64String(dataBase64), position = hasPosition ? new SharedPosition { x = x, y = y } : null,
                search = Array.ConvertAll(search, s => new SharedSearchValue { key = s.key, value = s.isString ? new SharedScalar { type = SharedScalarType.String, stringValue = StrictUtf8.GetString(Convert.FromBase64String(s.stringBase64)) } : new SharedScalar { type = SharedScalarType.Integer, integerValue = s.integer } })
            };
        }
        [Serializable] private class Envelope { public string payload, hash; }
        [Serializable] private class Sent { public string intentHash; }
        [Serializable] private class RunBinding { public string intentHash, runId; }
        [Serializable] public class Receipt { public string intentHash, objectId, resolution; public long objectRevision, usageRevision; public bool serverConfirmed, objectMissing; }
        public sealed class PreparedIntent
        {
            public Intent Value { get; } public string Hash { get; } public bool HasSuccess { get; } public bool WasSent { get; }
            internal PreparedIntent(Intent value, string hash, bool success, bool sent) { Value = value; Hash = hash; HasSuccess = success; WasSent = sent; }
        }
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private const string Uuid = @"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z";
        private readonly string _root, _directory, _path, _result, _sent, _project, _user;
        public SharedObjectsSampleStore(string root, string projectId, string userId, string apiKey)
        {
            if (!IsUuid(projectId) || !IsUuid(userId) || string.IsNullOrEmpty(apiKey)) throw new ArgumentException("Stable project UUID, player UUID and project key required");
            _root = root; _project = projectId.ToLowerInvariant(); _user = userId.ToLowerInvariant();
            _directory = Path.Combine(root, FileName(apiKey, _user)); _path = Path.Combine(_directory, "intent.json"); _result = Path.Combine(_directory, "result.json"); _sent = Path.Combine(_directory, "sent.json");
        }
        public static string FileName(string apiKey, string userId) => "shared-objects-" + Hash(apiKey) + "-" + userId.ToLowerInvariant() + ".journal";
        public bool HasIntent => Directory.Exists(_directory) && Directory.GetFileSystemEntries(_directory).Length != 0;
        public bool HasSuccess => File.Exists(_result);
        public static string Hash(string text) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(StrictUtf8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        public static Intent NewIntent(string project, string user, string action, string collection, string objectId, string groupId, long revision, SharedObjectInput input)
        {
            if (action != "delete" && (input == null || input.data == null || input.data.Length > 65536 || input.search == null || input.search.Length > 3 || input.search.Any(s => s == null || s.value == null || s.value.type != SharedScalarType.Integer && s.value.type != SharedScalarType.String))) throw new ArgumentException("Bounded complete Shared Objects input required before journal encoding");
            return new Intent
            {
                projectId = project.ToLowerInvariant(), userId = user.ToLowerInvariant(), action = action, collection = collection, objectId = objectId, groupId = groupId, revision = revision,
                dataBase64 = Convert.ToBase64String(input?.data ?? Array.Empty<byte>()), hasPosition = input?.position != null, x = input?.position?.x ?? 0, y = input?.position?.y ?? 0,
                search = Array.ConvertAll(input?.search ?? Array.Empty<SharedSearchValue>(), s => new Search { key = s.key, isString = s.value.type == SharedScalarType.String, stringBase64 = s.value.type == SharedScalarType.String ? Convert.ToBase64String(StrictUtf8.GetBytes(s.value.stringValue)) : null, integer = s.value.integerValue })
            };
        }
        public void SaveNew(Intent value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new NotSupportedException("Supply a game-owned durable browser journal before Shared Objects mutations");
#else
            Validate(value);
            WithLock(() =>
            {
                if (HasIntent) throw new IOException("Reconcile the original Shared Objects command first");
                Directory.CreateDirectory(_directory); string payload = JsonUtility.ToJson(value);
                WriteNew(_path, JsonUtility.ToJson(new Envelope { payload = payload, hash = Hash(payload) }));
            });
#endif
        }
        public Intent Load() { Intent value = null; WithLock(() => value = ReadIntent().Value); return value; }
        public PreparedIntent PrepareSend() { PreparedIntent value = null; WithLock(() => value = ReadIntent()); return value; }
        public SendLease AcquireSend()
        {
            var gate = OpenGate(); try { RecoverWritingResult(); return new SendLease(this, gate, ReadIntent()); } catch { gate.Dispose(); throw; }
        }
        public sealed class SendLease : IDisposable
        {
            private SharedObjectsSampleStore _owner; private FileStream _gate;
            public PreparedIntent Prepared { get; }
            internal SendLease(SharedObjectsSampleStore owner, FileStream gate, PreparedIntent prepared) { _owner = owner; _gate = gate; Prepared = prepared; }
            public void MarkSent()
            {
                if (_owner == null) throw new ObjectDisposedException(nameof(SendLease));
                if (Prepared.HasSuccess || (Prepared.Value.action == "create" || Prepared.Value.action == "defense") && Prepared.WasSent) throw new IOException("Unknown CREATE or saved result must be reconciled; never send another CREATE");
                if (!File.Exists(_owner._sent)) WriteNew(_owner._sent, JsonUtility.ToJson(new Sent { intentHash = Prepared.Hash }));
                else _owner.VerifySent(Prepared.Hash);
            }
            public void SaveServerSuccess(string objectId, long objectRevision, long usageRevision)
            {
                if (_owner == null) throw new ObjectDisposedException(nameof(SendLease));
                _owner.WriteReceipt(new Receipt { intentHash = Prepared.Hash, objectId = objectId, objectRevision = objectRevision, usageRevision = usageRevision, serverConfirmed = true, resolution = Prepared.Value.action == "defense" ? "validated-submit-confirmed" : "server-confirmed" });
            }
            public string GetRunId()
            {
                if (_owner == null) throw new ObjectDisposedException(nameof(SendLease));
                string path = Path.Combine(_owner._directory, "run.json"); if (!File.Exists(path)) return null;
                string raw = DurableRead(path); var value = JsonUtility.FromJson<RunBinding>(raw);
                if (value?.intentHash != Prepared.Hash || !IsUuid(value.runId) || JsonUtility.ToJson(value) != raw) throw new IOException("Corrupt Shared defense run binding");
                return value.runId;
            }
            public void SaveRunId(string runId)
            {
                if (_owner == null) throw new ObjectDisposedException(nameof(SendLease));
                if (Prepared.Value.action != "defense" || !IsUuid(runId)) throw new IOException("Invalid original Shared defense run");
                string existing = GetRunId(); if (existing != null) { if (existing != runId) throw new IOException("Another defense run is already bound"); return; }
                WriteNew(Path.Combine(_owner._directory, "run.json"), JsonUtility.ToJson(new RunBinding { intentHash = Prepared.Hash, runId = runId }));
            }
            /// <summary>A reviewed monotone read closes the original CAS; this does not claim that the uncertain request succeeded.</summary>
            public void SaveReconciled(long objectRevision, long usageRevision, bool objectMissing)
            {
                if (_owner == null) throw new ObjectDisposedException(nameof(SendLease));
                _owner.WriteReceipt(new Receipt { intentHash = Prepared.Hash, objectId = Prepared.Value.objectId, objectRevision = objectRevision, usageRevision = usageRevision, objectMissing = objectMissing, resolution = "reviewed-monotone-snapshot" });
            }
            public void Dispose() { _owner = null; _gate?.Dispose(); _gate = null; }
        }
        public Receipt ReadSuccess() { Receipt value = null; WithLock(() => { var intent = ReadIntent(); value = ReadReceipt(DurableRead(_result), intent); }); return value; }
        public void AcknowledgeSavedSuccess()
        {
            WithLock(() =>
            {
                var intent = ReadIntent(); ReadReceipt(DurableRead(_result), intent);
                Directory.Move(_directory, _directory + "." + intent.Hash + "." + Guid.NewGuid().ToString("N") + ".acknowledged");
            });
        }
        private PreparedIntent ReadIntent()
        {
            string raw = DurableRead(_path); Envelope e;
            try { e = JsonUtility.FromJson<Envelope>(raw); } catch (Exception error) { throw new IOException("Corrupt Shared Objects journal", error); }
            if (e?.payload == null || e.hash != Hash(e.payload)) throw new IOException("Original Shared Objects hash missing or changed");
            var value = JsonUtility.FromJson<Intent>(e.payload); Validate(value);
            if (File.Exists(_sent)) VerifySent(e.hash);
            var prepared = new PreparedIntent(value, e.hash, File.Exists(_result), File.Exists(_sent));
            if (prepared.HasSuccess) ReadReceipt(DurableRead(_result), prepared);
            return prepared;
        }
        private void VerifySent(string hash)
        {
            string raw = DurableRead(_sent); var sent = JsonUtility.FromJson<Sent>(raw);
            if (sent?.intentHash != hash || JsonUtility.ToJson(sent) != raw) throw new IOException("Unknown/partial Shared Objects send marker; do not send");
        }
        private void Validate(Intent x)
        {
            if (x == null || x.projectId != _project || x.userId != _user || !Regex.IsMatch(x.collection ?? "", @"\A[a-z][a-z0-9_]{0,63}\z") || !Safe(x.revision) || x.revision < 0
                || !new[] { "create", "update", "delete", "defense" }.Contains(x.action) || x.action != "create" && !IsUuid(x.objectId) || x.groupId != null && x.groupId != "" && !IsUuid(x.groupId)
                || !Safe(x.x) || !Safe(x.y) || x.search == null || x.search.Length > 3 || x.search.Any(s => s == null || !Regex.IsMatch(s.key ?? "", @"\A[a-z][a-z0-9_]{0,63}\z") || !Safe(s.integer)) || x.search.Select(s => s.key).Distinct().Count() != x.search.Length) throw new IOException("Shared Objects original scope/input invalid");
            try
            {
                var input = x.GetInput();
                if (input.data.Length > 65536 || Convert.ToBase64String(input.data) != x.dataBase64 || input.search.Any(s => s.value.type == SharedScalarType.String && StrictUtf8.GetByteCount(s.value.stringValue) > 96)) throw new IOException("Shared Objects original bytes invalid");
            }
            catch (Exception e) when (e is FormatException || e is DecoderFallbackException || e is ArgumentNullException) { throw new IOException("Shared Objects original encoding invalid", e); }
        }
        private Receipt ReadReceipt(string raw, PreparedIntent intent)
        {
            Receipt r; try { r = JsonUtility.FromJson<Receipt>(raw); } catch (Exception e) { throw new IOException("Unresolved Shared Objects result", e); }
            if (r == null || r.intentHash != intent.Hash || JsonUtility.ToJson(r) != raw || !Safe(r.objectRevision) || r.objectRevision < 0 || !Safe(r.usageRevision) || r.usageRevision < 0) throw new IOException("Conflicting/partial Shared Objects result");
            var x = intent.Value;
            if (x.action == "defense")
            {
                if (!r.serverConfirmed || r.resolution != "validated-submit-confirmed" || r.objectId != x.objectId || r.objectRevision != x.revision) throw new IOException("Defense was not submitted and confirmed");
                return r;
            }
            if (r.serverConfirmed)
            {
                if (r.resolution != "server-confirmed" || !IsUuid(r.objectId) || x.action == "create" && (r.objectRevision != 1 || r.usageRevision != x.revision + 1)
                    || x.action != "create" && r.objectId != x.objectId || x.action == "update" && r.objectRevision != x.revision + 1) throw new IOException("Shared Objects receipt mismatches original");
            }
            else if (r.resolution != "reviewed-monotone-snapshot" || x.action == "create" && r.usageRevision <= x.revision || x.action != "create" && !r.objectMissing && r.objectRevision <= x.revision) throw new IOException("Original Shared Objects revision is not fenced by the reviewed snapshot");
            return r;
        }
        private void WriteReceipt(Receipt receipt)
        {
            var intent = ReadIntent(); string raw = JsonUtility.ToJson(receipt); ReadReceipt(raw, intent);
            if (File.Exists(_result)) { if (DurableRead(_result) != raw) throw new IOException("Different Shared Objects result already saved"); return; }
            string temporary = Path.Combine(_directory, "result." + Guid.NewGuid().ToString("N") + ".writing");
            WriteNew(temporary, raw); File.Move(temporary, _result); DurableRead(_result);
        }
        private void RecoverWritingResult()
        {
            if (!Directory.Exists(_directory)) return;
            string[] files = Directory.GetFiles(_directory, "result.*.writing"); if (files.Length == 0) return;
            var intent = ReadIntent(); string canonical = null;
            foreach (string path in files)
            {
                string raw = DurableRead(path); ReadReceipt(raw, intent);
                if (canonical != null && canonical != raw) throw new IOException("Conflicting interrupted Shared Objects results"); canonical = raw;
            }
            if (File.Exists(_result)) { if (DurableRead(_result) != canonical) throw new IOException("Interrupted Shared Objects result differs"); }
            else { File.Move(files[0], _result); DurableRead(_result); }
        }
        private FileStream OpenGate()
        {
            Directory.CreateDirectory(_root);
            return new FileStream(_directory + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        private void WithLock(Action action) { using (var gate = OpenGate()) { RecoverWritingResult(); action(); } }
        private static void WriteNew(string path, string raw)
        {
            byte[] bytes = StrictUtf8.GetBytes(raw); if (bytes.Length > 262144) throw new IOException("Shared Objects journal exceeds bound");
            using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
            if (DurableRead(path) != raw) throw new IOException("Shared Objects durable readback changed");
        }
        private static string DurableRead(string path)
        {
            if (new FileInfo(path).Length > 262144) throw new IOException("Shared Objects journal exceeds bound");
            string raw = File.ReadAllText(path, StrictUtf8);
            using (var f = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) f.Flush(true);
            if (File.ReadAllText(path, StrictUtf8) != raw) throw new IOException("Shared Objects journal changed"); return raw;
        }
        private static bool Safe(long n) => n >= -9007199254740991L && n <= 9007199254740991L;
        private static bool IsUuid(string value) => value != null && Regex.IsMatch(value, Uuid);
    }
}
