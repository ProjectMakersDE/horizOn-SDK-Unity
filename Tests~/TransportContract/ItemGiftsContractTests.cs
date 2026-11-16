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

static class ItemGiftsContractTests
{
    const string User = "20000000-0000-4000-8000-000000001140", Recipient = "30000000-0000-4000-8000-000000001140", Project = "10000000-0000-4000-8000-000000001140", Item = "40000000-0000-4000-8000-000000001140", Gift = "50000000-0000-4000-8000-000000001140", Operation = "60000000-0000-4000-8000-000000001140", Time = "2026-11-16T12:00:00.000Z";
    static ItemGiftIntent Original() => ItemGiftIntent.Send(User, Operation, Time, Recipient, Item, 9, 7);
    static ItemGiftReceipt Receipt() => new ItemGiftReceipt { operationId = Operation, issuedAt = Time, giftId = Gift, giftRevision = 2, recipientId = Recipient, itemReference = new ItemReference { instanceId = Item, itemKey = "hat" }, configRevision = 7 };
    static string GiftJson(string status = "offered", string currency = "\"1coin.gold\"", long fee = 5, string terminal = "null", string declined = "null")
        => "{\"id\":\"" + Gift + "\",\"revision\":2,\"senderId\":\"" + User + "\",\"recipientId\":\"" + Recipient + "\",\"returnOwnerId\":\"" + User + "\",\"itemReference\":{\"instanceId\":\"" + Item + "\",\"itemKey\":\"hat\"},\"currencyKey\":" + currency + ",\"fee\":" + fee + ",\"configRevision\":7,\"status\":\"" + status + "\",\"createdAt\":\"" + Time + "\",\"expiresAt\":\"2026-11-23T12:00:00.000Z\",\"terminalAt\":" + terminal + ",\"declinedAt\":" + declined + ",\"offerEntryId\":\"message:70000000-0000-4000-8000-000000001140\",\"returnEntryId\":null,\"ownerUnavailable\":false}";
    public static bool TryRunChild(string[] args)
    {
        if (args.Length != 3 || args[0] != "--item-gifts-journal-crash") return false;
        string root = args[1], phase = args[2]; var store = new ItemGiftsSampleStore(root, Project, User);
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
        string root = Path.Combine(Path.GetTempPath(), "horizon-gift-crash-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); Process child = null;
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach (var arg in new[] { "--item-gifts-journal-crash", root, phase }) start.ArgumentList.Add(arg);
            child = Process.Start(start); var end = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(Path.Combine(root, "ready")) && !child.HasExited && DateTime.UtcNow < end) Thread.Sleep(20);
            Require(File.Exists(Path.Combine(root, "ready")) && !child.HasExited, "actual child reaches exact crash boundary " + phase);
            var store = new ItemGiftsSampleStore(root, Project, User); RejectIO(() => store.AcquireSend(), "live process holds OS owner");
            child.Kill(); Require(child.WaitForExit(5000), "owned child terminated"); child.Dispose(); child = null;
            if (phase == "partial") { RejectIO(() => store.AcquireSend(), "partial confirmation still fail-closed after killed owner"); return; }
            if (phase == "after-ack") { Require(!store.HasPending, "ack leaves no gift archive"); using (store.BeginNew(Original())) { } return; }
            using (var lease = store.AcquireSend()) Require(lease.Original.Intent.Json == Original().Json && lease.Original.Confirmed == (phase != "original"), "exact original/minimal known phase recovered after real process death");
        }
        finally { if (child != null) { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } child.Dispose(); } Directory.Delete(root, true); }
    }
    public static void Run()
    {
        foreach (var phase in new[] { "original", "confirmed", "complete-next", "partial", "after-ack" }) Crash(phase);
        var original = Original();
        Require(original.Json == "{\"userId\":\"" + User + "\",\"operationId\":\"" + Operation + "\",\"issuedAt\":\"" + Time + "\",\"recipientId\":\"" + Recipient + "\",\"instanceId\":\"" + Item + "\",\"expectedInventoryRevision\":9,\"expectedConfigRevision\":7}", "seven-field original body");
        Require(ItemGiftIntent.Restore(original.Json).Json == original.Json, "restore keeps original bytes");
        foreach (string number in new[] { "1.0", "1e0", "-0", "-1", "9007199254740992", "null" })
            Reject(() => ItemGiftIntent.Restore(original.Json.Replace("\"expectedInventoryRevision\":9", "\"expectedInventoryRevision\":" + number)), "exact integer token " + number);
        foreach (string date in new[] { "2026-02-30T12:00:00.000Z", "2026-11-16T12:00:00Z", "2026-11-16T12:00:00.000+00:00" })
            Reject(() => ItemGiftIntent.Restore(original.Json.Replace(Time, date)), "canonical date " + date);
        Reject(() => ItemGiftIntent.Send(User, Operation, Time, User, Item, 9, 7), "self gift rejected locally");
        Reject(() => ItemGiftIntent.Send(User, "6000000A-0000-4000-8000-000000001140", Time, Recipient, Item, 9, 7), "canonical lowercase UUID");
        Reject(() => ItemGiftIntent.Restore(original.Json.Replace("}", ",\"extra\":1}")), "unknown field");
        Reject(() => ItemGiftIntent.Restore(original.Json.Replace("\"expectedConfigRevision\":7", "\"expectedConfigRevision\":7,\"expectedConfigRevisio\\u006e\":7")), "escaped duplicate");
        var record = Receipt();
        Require(original.Matches(record), "entire original response identity");
        record.recipientId = User; Require(!original.Matches(record), "other recipient"); record.recipientId = Recipient;
        record.configRevision = 8; Require(!original.Matches(record), "other config revision"); record.configRevision = 7;
        record.itemReference.instanceId = Gift; Require(!original.Matches(record), "other instance"); record.itemReference.instanceId = Item;

        string server = ",\"serverTime\":\"" + Time + "\"}";
        var point = ItemGiftsJson.Read<ItemGiftView>("{\"gift\":" + GiftJson() + server);
        Require(point.gift.fee == 5 && point.gift.currencyKey == "1coin.gold" && point.gift.status == "offered", "gift view");
        var free = ItemGiftsJson.Read<ItemGiftView>("{\"gift\":" + GiftJson(currency: "null", fee: 0) + server);
        Require(free.gift.currencyKey == null && free.gift.fee == 0, "free gift");
        Reject(() => ItemGiftsJson.Read<ItemGiftView>("{\"gift\":" + GiftJson(currency: "null", fee: 3) + server), "fee without currency");
        Reject(() => ItemGiftsJson.Read<ItemGiftView>("{\"gift\":" + GiftJson(status: "claimed") + server), "terminal without terminalAt");
        Reject(() => ItemGiftsJson.Read<ItemGiftView>("{\"gift\":" + GiftJson(status: "sent") + server), "unknown status");
        Reject(() => ItemGiftsJson.Read<ItemGiftView>("{\"gift\":" + GiftJson(declined: "\"" + Time + "\"") + server), "offered cannot be declined");
        var copy = ItemGiftsJson.Clone(point); copy.gift.itemReference.itemKey = "edited"; Require(point.gift.itemReference.itemKey == "hat", "deep clone");
        string receiptJson = "{\"operationId\":\"" + Operation + "\",\"issuedAt\":\"" + Time + "\",\"giftId\":\"" + Gift + "\",\"giftRevision\":2,\"recordedAt\":\"" + Time + "\",\"recipientId\":\"" + Recipient + "\",\"itemReference\":{\"instanceId\":\"" + Item + "\",\"itemKey\":\"hat\"},\"currencyKey\":\"1coin.gold\",\"fee\":5,\"configRevision\":7}";
        var written = ItemGiftsJson.Read<ItemGiftWritten>("{\"receipt\":" + receiptJson + ",\"gift\":" + GiftJson() + ",\"replayed\":false" + server);
        Require(!written.replayed && original.Matches(written.receipt), "fresh send");
        var gone = ItemGiftsJson.Read<ItemGiftWritten>("{\"receipt\":" + receiptJson + ",\"gift\":null,\"replayed\":true" + server);
        Require(gone.gift == null && gone.replayed, "replay after terminal retention");
        Reject(() => ItemGiftsJson.Read<ItemGiftWritten>("{\"receipt\":" + receiptJson + ",\"gift\":null,\"replayed\":false" + server), "fresh send needs gift");
        Reject(() => ItemGiftsJson.Read<ItemGiftWritten>("{\"receipt\":" + receiptJson.Replace("\"fee\":5", "\"fee\":6") + ",\"gift\":" + GiftJson() + ",\"replayed\":true" + server), "receipt and gift fee must match");
        var declinedGift = GiftJson(status: "return_pending", declined: "\"" + Time + "\"");
        var declinedView = ItemGiftsJson.Read<ItemGiftDeclined>("{\"gift\":" + declinedGift + ",\"replayed\":false,\"returnDelivery\":\"pending\"" + server);
        Require(declinedView.returnDelivery == "pending" && declinedView.gift.declinedAt == Time, "decline view");
        string limits = "{\"openGiftsPerPlayer\":3,\"retainedGiftsPerProject\":50,\"projectWritesPerMinute\":10,\"historyPerPlayer\":20,\"receivedPageSize\":50,\"playerWriteIntervalMillis\":1000,\"expirySeconds\":604800,\"terminalRetentionSeconds\":604800,\"operationRetentionSeconds\":2592000}";
        var config = ItemGiftsJson.Read<ItemGiftConfigView>("{\"config\":{\"revision\":7,\"enabled\":true,\"currencyKey\":null,\"fee\":0},\"limits\":" + limits + server);
        Require(config.config.enabled && config.limits.expirySeconds == 604800, "config view");
        string state = "{\"state\":{\"userId\":\"" + User + "\",\"revision\":4,\"sent\":[" + GiftJson() + "],\"history\":[],\"received\":[],\"operationWindow\":{\"serverTime\":\"" + Time + "\",\"acceptAfter\":\"" + Time + "\"},\"limits\":" + limits.Replace("\"openGiftsPerPlayer\":3", "\"openGiftsPerPlayer\":1") + "}" + server;
        Require(ItemGiftsJson.Read<ItemGiftStateView>(state).state.sent.Length == 1, "retained sent list survives a lowered quota");
        Reject(() => ItemGiftsJson.Read<ItemGiftStateView>(state.Replace("\"received\":[]", "\"received\":[" + GiftJson() + "]")), "received offers belong to the reader");

        string root = Path.Combine(Path.GetTempPath(), "horizon-item-gifts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ItemGiftsSampleStore(root, Project, User);
            RejectIO(() => store.BeginNew(ItemGiftIntent.Send(Recipient, Operation, Time, User, Item, 9, 7)), "original of another player");
            using (var lease = store.BeginNew(original))
            {
                RejectIO(() => store.AcquireSend(), "exclusive owner across actor instances");
                RejectIO(() => store.BeginNew(original), "replacement blocked during HTTP");
                RejectIO(() => lease.AcknowledgeAfterRefresh(), "unknown operation never acknowledged");
                lease.MarkConfirmed(record);
                string raw = File.ReadAllText(Directory.GetFiles(root, "*.json").Single());
                Require(!raw.Contains("itemReference") && !raw.Contains("balance") && !raw.Contains("recordedAt") && !raw.Contains("fee"), "minimal known identity without result archive");
            }
            Require(!new ItemGiftsSampleStore(root, Project, Recipient).HasPending && !new ItemGiftsSampleStore(root, Recipient, User).HasPending, "another player or project never sees this original");
            using (var lease = new ItemGiftsSampleStore(root, Project, User).AcquireSend())
            {
                Require(lease.Original.Confirmed && lease.Original.Intent.Json == original.Json && lease.Original.ConfirmedGiftId == Gift, "known original survives restart");
                lease.AcknowledgeAfterRefresh(); Require(!store.HasPending, "ack removes resolved original");
            }
            using (var lease = store.BeginNew(original)) { }
            string path = Directory.GetFiles(root, "*.json").Single(); File.WriteAllText(path + ".next", "{");
            RejectIO(() => store.AcquireSend(), "partial confirmation never permits another HTTP send");
            File.Delete(path + ".next");
            using (var lease = store.AcquireSend()) Require(!lease.Original.Confirmed && lease.Original.Intent.Json == original.Json, "unknown original unchanged");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Console.WriteLine("Item gifts exact token/original identity/strict views/minimal journal/5 real killed-process recovery/ownership contracts passed.");
    }
    static void Require(bool b, string message) { if (!b) throw new Exception("Item gifts: " + message); }
    static void Reject(Action f, string message) { bool rejected = false; try { f(); } catch (FormatException) { rejected = true; } Require(rejected, message); }
    static void RejectIO(Action f, string message) { bool rejected = false; try { f(); } catch (IOException) { rejected = true; } Require(rejected, message); }
}
