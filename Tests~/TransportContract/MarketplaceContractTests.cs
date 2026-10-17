using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Transport;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Examples.Features;

static class MarketplaceContractTests
{
    const string User = "20000000-0000-4000-8000-000000001015", Project = "10000000-0000-4000-8000-000000001015", Item = "40000000-0000-4000-8000-000000001015", Listing = "50000000-0000-4000-8000-000000001015", Operation = "60000000-0000-4000-8000-000000001015", Time = "2026-10-17T12:00:00.000Z";
    static MarketplaceIntent Original() => MarketplaceIntent.Create(User, Operation, Time, Item, 9, 7, 101, 300);
    static MarketplaceReceipt Receipt() => new MarketplaceReceipt { operationId = Operation, issuedAt = Time, kind = "create", listingId = Listing, listingRevision = 1, itemReference = new ItemReference { instanceId = Item, itemKey = "hat" }, price = 101, currencyKey = "1coin.gold" };
    public static bool TryRunChild(string[] args)
    {
        if (args.Length != 3 || args[0] != "--marketplace-journal-crash") return false;
        string root = args[1], phase = args[2]; var store = new MarketplaceSampleStore(root, Project, User);
        using (var lease = store.BeginNew(Original()))
        {
            string path = Directory.GetFiles(root, "*.json").Single(), original = File.ReadAllText(path);
            if (phase == "confirmed" || phase == "complete-next" || phase == "after-ack") lease.MarkConfirmed(Receipt());
            if (phase == "complete-next") { File.Copy(path, path + ".next"); File.WriteAllText(path, original); }
            if (phase == "partial") File.WriteAllText(path + ".next", "{");
            if (phase == "after-ack") lease.AcknowledgeAfterRefresh();
            File.WriteAllText(Path.Combine(root, "ready"), "ready"); while (true) Thread.Sleep(20);
        }
    }
    static void Crash(string phase)
    {
        string root = Path.Combine(Path.GetTempPath(), "horizon-market-crash-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); Process child = null;
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach (var arg in new[] { "--marketplace-journal-crash", root, phase }) start.ArgumentList.Add(arg);
            child = Process.Start(start); var end = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(Path.Combine(root, "ready")) && !child.HasExited && DateTime.UtcNow < end) Thread.Sleep(20);
            Require(File.Exists(Path.Combine(root, "ready")) && !child.HasExited, "actual child reaches exact crash boundary " + phase);
            var store = new MarketplaceSampleStore(root, Project, User); RejectIO(() => store.AcquireSend(), "live process holds OS owner");
            child.Kill(); Require(child.WaitForExit(5000), "owned child terminated"); child.Dispose(); child = null;
            if (phase == "partial") { RejectIO(() => store.AcquireSend(), "partial confirmation still fail-closed after killed owner"); return; }
            if (phase == "after-ack") { Require(!store.HasPending, "ack leaves no permanent purchase archive"); using (store.BeginNew(Original())) { } return; }
            using (var lease = store.AcquireSend()) Require(lease.Original.Intent.Json == Original().Json && lease.Original.Confirmed == (phase != "original"), "exact original/minimal known phase recovered after real process death");
        }
        finally { if (child != null) { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } child.Dispose(); } Directory.Delete(root, true); }
    }
    public static void Run()
    {
        foreach (var phase in new[] { "original", "confirmed", "complete-next", "partial", "after-ack" }) Crash(phase);
        var original = MarketplaceIntent.Create(User, Operation, Time, Item, 9, 7, 101, 300);
        foreach (string number in new[] { "1.0", "1e0", "-0", "9007199254740992", "null" })
            Reject(() => MarketplaceIntent.Restore("create", "", original.Json.Replace("\"price\":101", "\"price\":" + number)), "exact integer token " + number);
        foreach (string date in new[] { "2026-02-30T12:00:00.000Z", "2026-10-17T12:00:00Z", "2026-10-17T12:00:00.000+00:00" })
            Reject(() => MarketplaceIntent.Restore("create", "", original.Json.Replace(Time, date)), "canonical date " + date);
        Reject(() => MarketplaceIntent.Restore("create", Listing, original.Json), "create cannot target existing listing");
        Reject(() => MarketplaceIntent.Restore("create", "", original.Json.Replace("\"price\":101", "\"price\":101,\"pr\\u0069ce\":101")), "escaped duplicate");
        Require(MarketplaceJson.Fee(9007199254740991, 10000) == 9007199254740991, "fee exact without overflow");
        var record = new MarketplaceReceipt { operationId = Operation, issuedAt = Time, kind = "create", listingId = Listing, listingRevision = 1, itemReference = new ItemReference { instanceId = Item, itemKey = "hat" }, price = 101, currencyKey = "1coin.gold" };
        Require(original.Matches(record), "entire original response identity"); record.listingRevision = 2; Require(!original.Matches(record), "wrong original committed revision"); record.listingRevision = 1;
        string root = Path.Combine(Path.GetTempPath(), "horizon-marketplace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MarketplaceSampleStore(root, Project, User);
            using (var lease = store.BeginNew(original))
            {
                RejectIO(() => store.AcquireSend(), "exclusive owner across actor instances");
                RejectIO(() => store.BeginNew(original), "replacement blocked during HTTP");
                RejectIO(() => lease.AcknowledgeAfterRefresh(), "unknown operation never acknowledged");
                lease.MarkConfirmed(record);
                string raw = File.ReadAllText(Directory.GetFiles(root, "*.json").Single());
                Require(!raw.Contains("itemReference") && !raw.Contains("balance") && !raw.Contains("recordedAt"), "minimal known identity without result archive");
            }
            using (var lease = new MarketplaceSampleStore(root, Project, User).AcquireSend())
            {
                Require(lease.Original.Confirmed && lease.Original.Intent.Json == original.Json && lease.Original.ConfirmedListingId == Listing, "known original survives restart");
                lease.AcknowledgeAfterRefresh(); Require(!store.HasPending, "ack removes resolved original");
                RejectIO(() => store.BeginNew(original), "ack does not unlink the live gate inode");
            }
            using (var lease = store.BeginNew(original)) { }
            string path = Directory.GetFiles(root, "*.json").Single(); File.WriteAllText(path + ".next", "{");
            RejectIO(() => store.AcquireSend(), "partial confirmation never permits another HTTP send");
            File.Delete(path + ".next");
            using (var lease = store.AcquireSend()) Require(!lease.Original.Confirmed && lease.Original.Intent.Json == original.Json, "unknown original unchanged");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Console.WriteLine("Marketplace exact token/original identity/minimal journal/5 real killed-process recovery/ownership/partial confirmation contracts passed.");
    }
    static void Require(bool b, string message) { if (!b) throw new Exception("Marketplace: " + message); }
    static void Reject(Action f, string message) { bool rejected = false; try { f(); } catch (FormatException) { rejected = true; } Require(rejected, message); }
    static void RejectIO(Action f, string message) { bool rejected = false; try { f(); } catch (IOException) { rejected = true; } Require(rejected, message); }
}
