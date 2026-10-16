using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using PM.horizOn.Cloud.Examples.Features;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;

static class SharedObjectsJournalContractTests
{
    internal const string Project = "11111111-1111-4111-8111-111111111111", User = SharedObjectsContractTests.User, Key = "shared-contract-key";
    static SharedObjectsSampleStore Store(string root) => new SharedObjectsSampleStore(root, Project, User, Key);
    static string DirectoryPath(string root) => Path.Combine(root, SharedObjectsSampleStore.FileName(Key, User));
    static SharedObjectsSampleStore.Intent Intent() => SharedObjectsSampleStore.NewIntent(Project, User, "create", "levels", null, null, 9007199254740990L, new SharedObjectInput { data = new byte[] { 0, 255, 0 }, search = new[] { new SharedSearchValue { key = "label", value = new SharedScalar { type = SharedScalarType.String, stringValue = "前\0後😀" } } } });
    public static bool TryRunChild(string[] args)
    {
        if (args.Length != 3 || args[0] != "--shared-journal-crash") return false;
        string root = args[1], phase = args[2]; var store = Store(root); store.SaveNew(Intent());
        if (phase == "after-ack")
        {
            using (var lease = store.AcquireSend()) { lease.MarkSent(); lease.SaveServerSuccess(SharedObjectsContractTests.Id, 1, 9007199254740991); }
            store.AcknowledgeSavedSuccess(); Ready(root); return true;
        }
        using (var lease = store.AcquireSend())
        {
            lease.MarkSent();
            if (phase == "unpublished-result")
            {
                var receipt = new SharedObjectsSampleStore.Receipt { intentHash = lease.Prepared.Hash, objectId = SharedObjectsContractTests.Id, objectRevision = 1, usageRevision = 9007199254740991, serverConfirmed = true, resolution = "server-confirmed" };
                string path = Path.Combine(DirectoryPath(root), "result.0123456789abcdef0123456789abcdef.writing");
                typeof(SharedObjectsSampleStore).GetMethod("WriteNew", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { path, JsonUtility.ToJson(receipt) });
            }
            if (phase == "before-ack") lease.SaveServerSuccess(SharedObjectsContractTests.Id, 1, 9007199254740991);
            Ready(root);
        }
        return true;
    }
    static void Ready(string root)
    {
        string path = Path.Combine(root, "ready"); File.WriteAllText(path + ".writing", "ready");
        using (var f = new FileStream(path + ".writing", FileMode.Open, FileAccess.Write)) f.Flush(true);
        File.Move(path + ".writing", path); while (true) Thread.Sleep(50);
    }
    public static void Run()
    {
        foreach (string phase in new[] { "held-sent-create", "unpublished-result", "before-ack", "after-ack" }) Crash(phase);
        string root = Path.Combine(Path.GetTempPath(), "horizon-shared-journal-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var store = Store(root); store.SaveNew(Intent());
            using (var lease = store.AcquireSend()) { Reject(() => Store(root).AcquireSend(), "live actor/process gate"); lease.MarkSent(); }
            using (var lease = Store(root).AcquireSend()) { Require(lease.Prepared.WasSent, "durable sent marker"); Reject(() => lease.MarkSent(), "never repeat CREATE"); Reject(() => lease.SaveReconciled(0, 9007199254740990, false), "unfenced old create"); lease.SaveReconciled(0, 9007199254740991, false); }
            var r = store.ReadSuccess(); Require(!r.serverConfirmed && r.resolution == "reviewed-monotone-snapshot", "reconciliation does not invent success"); store.AcknowledgeSavedSuccess();
            store.SaveNew(Intent()); string path = Path.Combine(DirectoryPath(root), "result.partial.writing"); File.WriteAllText(path, "{\"intentHash\":");
            Reject(() => Store(root).AcquireSend(), "partial interrupted result blocks HTTP");
            Reject(() => Store(root).SaveNew(Intent()), "unknown journal blocks replacement");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("Shared Objects journal:4 actual killed-process lock/CREATE/result/ACK recoveries plus explicit monotone reconciliation passed.");
    }
    static void Crash(string phase)
    {
        string root = Path.Combine(Path.GetTempPath(), "horizon-shared-crash-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); Process child = null;
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach (string value in new[] { "--shared-journal-crash", root, phase }) info.ArgumentList.Add(value);
            child = Process.Start(info); var deadline = DateTime.UtcNow.AddSeconds(20); string ready = Path.Combine(root, "ready");
            while (!File.Exists(ready) && !child.HasExited && DateTime.UtcNow < deadline) Thread.Sleep(20);
            Require(File.Exists(ready) && !child.HasExited, "child exact crash phase " + phase);
            if (phase != "after-ack") Reject(() => Store(root).AcquireSend(), "live child owns OS handle");
            child.Kill(); Require(child.WaitForExit(10000), "owned crash process exited"); child.Dispose(); child = null;
            var store = Store(root);
            if (phase == "after-ack")
            {
                Require(!store.HasIntent, "atomic ACK leaves no active intent"); var directories = Directory.GetDirectories(root, "*.acknowledged");
                Require(directories.Length == 1 && File.Exists(Path.Combine(directories[0], "intent.json")) && File.Exists(Path.Combine(directories[0], "result.json")), "ACK includes complete original/result");
            }
            else
            {
                using (var lease = store.AcquireSend())
                {
                    Require(lease.Prepared.Value.revision == 9007199254740990 && lease.Prepared.Value.GetInput().data[1] == 255 && lease.Prepared.Value.GetInput().search[0].value.stringValue == "前\0後😀", "restart preserves safe64/bytes/NUL original");
                    Require(lease.Prepared.WasSent, "restart preserves sent original");
                    if (phase == "held-sent-create") Reject(() => lease.MarkSent(), "uncertain CREATE is never resent after death");
                    else Require(lease.Prepared.HasSuccess, "interrupted result recovered before any HTTP");
                }
            }
        }
        finally { if (child != null) { if (!child.HasExited) { child.Kill(); child.WaitForExit(10000); } child.Dispose(); } Directory.Delete(root, true); }
    }
    static void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (IOException) { rejected = true; } Require(rejected, message); }
    static void Require(bool value, string message) { if (!value) throw new Exception("Shared Objects journal: " + message); }
}
