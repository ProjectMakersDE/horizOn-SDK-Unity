using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using PM.horizOn.Cloud.Examples.Features;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;

static class ItemsJournalContractTests
{
    internal const string Project = "11111111-1111-4111-8111-111111111111", User = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", Key = "items-crash-contract-key";
    static ItemsSampleStore Store(string root) => new ItemsSampleStore(root, Project, User, Key);
    static string DirectoryPath(string root) => Path.Combine(root, ItemsSampleStore.FileName(Key, User));
    static ItemsSampleStore.Intent Intent() => new ItemsSampleStore.Intent { projectId = Project, userId = User, action = "purchase", offerKey = "sword", operationId = "1792108800000:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", offerHash = ItemsSampleStore.Hash("前\0後😀/9007199254740991"), revision = 9007199254740990L };
    static ItemReference[] Items() => new[] { new ItemReference { instanceId = Project, itemKey = "sword" } };
    public static bool TryRunChild(string[] args)
    {
        if (args.Length != 3 || args[0] != "--items-journal-crash") return false;
        string root = args[1], phase = args[2]; var store = Store(root); store.SaveNew(Intent());
        using (var lease = store.AcquireSend())
        {
            if (phase == "http-overlap")
            {
                using var client = new HttpClient();
                // Parent deliberately holds this actual HTTP response while it tries another process opener.
                client.PostAsync(File.ReadAllText(Path.Combine(root, "url")), new StringContent(lease.Prepared.Hash)).GetAwaiter().GetResult();
                throw new Exception("Overlap fixture must kill the waiting child");
            }
            if (phase == "unpublished-result")
            {
                var r = new ItemsSampleStore.Receipt { intentHash = lease.Prepared.Hash, action = "purchase", operationId = lease.Prepared.Value.operationId, serverConfirmed = true, items = Items() };
                string path = Path.Combine(DirectoryPath(root), "result.0123456789abcdef0123456789abcdef.writing");
                typeof(ItemsSampleStore).GetMethod("WriteNew", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { path, JsonUtility.ToJson(r) });
            }
            if (phase == "confirmed-before-inventory" || phase == "before-ack" || phase == "after-ack") lease.SaveSuccess(lease.Prepared.Value.operationId, Items());
            if (phase == "after-ack") lease.AcknowledgeSavedSuccess();
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
        foreach (string phase in new[] { "held-purchase", "unpublished-result", "confirmed-before-inventory", "before-ack", "after-ack", "http-overlap" }) Crash(phase);
        string root = Path.Combine(Path.GetTempPath(), "horizon-items-journal-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var store = Store(root); store.SaveNew(Intent()); string hash;
            using (var lease = store.AcquireSend())
            {
                hash = lease.Prepared.Hash; Reject(() => Store(root).AcquireSend(), "another actor cannot enter live send");
                Reject(() => Store(root).SaveNew(Intent()), "new intent cannot overtake live send");
                Reject(() => lease.SaveReconciledOriginal(9007199254740991), "purchase never reconciled as inventory CAS");
                lease.SaveSuccess(lease.Prepared.Value.operationId, Array.Empty<ItemReference>()); lease.AcknowledgeSavedSuccess();
                Reject(() => Store(root).SaveNew(Intent()), "ACK rename does not release the still-live lock inode");
            }
            var mutation = Intent(); mutation.action = "equip"; mutation.instanceId = Project; mutation.revision = 9007199254740990;
            store.SaveNew(mutation);
            using (var lease = store.AcquireSend())
            {
                Reject(() => lease.SaveReconciledOriginal(9007199254740990), "equal revision does not fence old CAS");
                Reject(() => lease.SaveReconciledOriginal(9007199254740989), "older revision does not fence old CAS");
                lease.SaveReconciledOriginal(9007199254740991);
                Require(!lease.ReadSuccess().serverConfirmed, "explicit reconciliation does not invent success"); lease.AcknowledgeSavedSuccess();
            }
            store.SaveNew(Intent()); string dir = DirectoryPath(root);
            File.WriteAllText(Path.Combine(dir, "result.partial.writing"), "{\"intentHash\":");
            Reject(() => Store(root).AcquireSend(), "partial result blocks HTTP"); Reject(() => Store(root).SaveNew(Intent()), "partial result blocks replacement");
            File.Delete(Path.Combine(dir, "result.partial.writing"));
            using (var lease = store.AcquireSend()) lease.SaveSuccess(lease.Prepared.Value.operationId, Items());
            string result = Path.Combine(dir, "result.json"), writing = Path.Combine(dir, "result.conflict.writing");
            File.WriteAllText(writing, File.ReadAllText(result).Replace(Project, User));
            Reject(() => Store(root).AcquireSend(), "conflicting complete unpublished receipt blocks HTTP");
            File.Delete(writing); store.AcknowledgeSavedSuccess();
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("Items journal:6 actual killed-child recovery cases, including a live two-process HTTP overlap; equal-CAS/partial/conflicting receipt and atomic ACK guards passed.");
    }
    static void Crash(string phase)
    {
        string root = Path.Combine(Path.GetTempPath(), "horizon-items-crash-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); Process child = null; HttpListener server = null; HttpListenerContext context = null;
        try
        {
            if (phase == "http-overlap")
            {
                var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start(); int port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
                string url = "http://127.0.0.1:" + port + "/"; server = new HttpListener(); server.Prefixes.Add(url); server.Start(); File.WriteAllText(Path.Combine(root, "url"), url);
            }
            var request = server?.GetContextAsync(); var info = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach (string value in new[] { "--items-journal-crash", root, phase }) info.ArgumentList.Add(value);
            child = Process.Start(info); var deadline = DateTime.UtcNow.AddSeconds(20); string ready = Path.Combine(root, "ready");
            if (request != null)
            {
                Require(request.Wait(20000), "child reaches actual HTTP while holding OS lock"); context = request.Result;
                Require(context.Request.HttpMethod == "POST", "real mutation-shaped overlap request");
                Require(new StreamReader(context.Request.InputStream).ReadToEnd().Length == 64, "frozen original hash in HTTP");
            }
            else
            {
                while (!File.Exists(ready) && !child.HasExited && DateTime.UtcNow < deadline) Thread.Sleep(20);
                Require(File.Exists(ready) && !child.HasExited, "child reaches exact crash phase " + phase);
            }
            if (phase != "after-ack") Reject(() => Store(root).AcquireSend(), "live separate process owns send/result handle");
            child.Kill(); Require(child.WaitForExit(10000), "owned process killed"); child.Dispose(); child = null;
            var store = Store(root);
            if (phase == "after-ack")
            {
                Require(!store.HasIntent, "ACK removes entire active journal atomically"); var dirs = Directory.GetDirectories(root, "*.acknowledged");
                Require(dirs.Length == 1 && File.Exists(Path.Combine(dirs[0], "intent.json")) && File.Exists(Path.Combine(dirs[0], "result.json")), "complete original and result remain archived");
            }
            else using (var lease = store.AcquireSend())
            {
                var x = lease.Prepared.Value;
                Require(x.operationId == Intent().operationId && x.offerHash == Intent().offerHash && x.revision == 9007199254740990, "same ID/Unicode-NUL hash/safe64 original survives process death");
                if (phase == "held-purchase" || phase == "http-overlap") Require(!lease.Prepared.HasSuccess, "unknown purchase remains original for explicit same-ID retry");
                else Require(lease.Prepared.HasSuccess && lease.ReadSuccess().items[0].instanceId == Project, "complete interrupted receipt recovered before another HTTP request");
            }
        }
        finally { if (child != null) { if (!child.HasExited) { child.Kill(); child.WaitForExit(10000); } child.Dispose(); } context?.Response.Close(); server?.Close(); Directory.Delete(root, true); }
    }
    static void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (IOException) { rejected = true; } Require(rejected, message); }
    static void Require(bool value, string message) { if (!value) throw new Exception("Items journal: " + message); }
}
