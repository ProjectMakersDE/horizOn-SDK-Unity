using System;
using System.IO;
using PM.horizOn.Cloud.Examples.Features;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Network.Responses;

static class ItemsContractTests
{
    public static void Run()
    {
        var p = ItemsJson.Properties("{\"z\":true,\"n\":9007199254740991,\"s\":\"前\\u0000後😀/\\b\\t\\n\\f\\r\\u0001\"}");
        Require(p[0].integerValue == 9007199254740991 && p[1].stringValue == "前\0後😀/\b\t\n\f\r\u0001", "exact integers and NUL strings");
        Require(ItemsJson.PropertiesJson(p) == "{\"n\":9007199254740991,\"s\":\"前\\u0000後😀/\\b\\t\\n\\f\\r\\u0001\",\"z\":true}", "canonical UTF8 escaping/key order");
        var escapedKeys = JsonFragments.Object("{\"a\\u0000b\":7,\"a\\u0000c\":8,\"a\":9}");
        Require(escapedKeys["a\0b"] == "7" && escapedKeys["a\0c"] == "8" && escapedKeys["a"] == "9", "decoded keys never alias or truncate");
        var group = GroupJsonData.FromJson("{\"a\\u0000b\":9007199254740993,\"a\\u0000c\":1.00000000000000000001,\"a\":null}");
        Require(group.Json.Contains("9007199254740993"), "Group raw JSON regression");
        foreach (var raw in new[] { "1.0000000000000001", "1e0", "-0", "9007199254740992", "-9007199254740992", "null", "[]", "{}" })
            Reject(() => ItemsJson.Properties("{\"n\":" + raw + "}"), "unsafe property " + raw);
        foreach (var raw in new[] { "{\"n\":1,\"\\u006e\":2}", "{\"n\\u0000suffix\":1}", "{\"s\":\"\\ud800\"}", "{\"s\":\"\\udc00\"}", "{\"s\":\"" + new string('é', 126) + "\"}" })
            Reject(() => ItemsJson.Properties(raw), "strict keys/Unicode/bytes");
        Require(ItemsJson.Properties("{\"s\":\"" + new string('é', 124) + "\"}")[0].stringValue.Length == 124, "exact canonical byte boundary");
        Require(ItemsJson.Rewards("[]").Length == 0 && ItemsJson.References("[]").Length == 0, "legacy empty arrays");
        foreach (var raw in new[] { "1.0", "1e0", "0", "5", "-1", "9007199254740992" })
            Reject(() => ItemsJson.Rewards("[{\"itemKey\":\"sword\",\"quantity\":" + raw + "}]"), "exact reward quantity");
        Reject(() => ItemsJson.Rewards("[{\"itemKey\":\"a\",\"quantity\":3},{\"itemKey\":\"b\",\"quantity\":2}]"), "total quantity");
        Reject(() => JsonFragments.Object("{\"a\":1,\"\\u0061\":2}"), "decoded duplicate");
        Stacks();
        Journal();
        Console.WriteLine("Items canonical, safe integer, typed/NUL string, original JSON key and bounded reward contracts passed.");
    }
    private static void Stacks()
    {
        const string user = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", head = "{\"userId\":\"" + user + "\",\"revision\":2,\"serverTime\":\"2026-11-23T00:00:00Z\",\"instances\":[]";
        var old = ItemsJson.Inventory(head + ",\"limits\":{\"catalog\":1,\"inventory\":1,\"receipts\":1}}");
        Require(old.stacks.Length == 0 && old.limits.stacks == 0, "old server inventory has no stacks");
        var inv = ItemsJson.Inventory(head + ",\"stacks\":[{\"itemKey\":\"potion\",\"count\":999999,\"equipped\":true}],\"limits\":{\"catalog\":1,\"inventory\":1,\"receipts\":1,\"stacks\":25}}");
        Require(inv.stacks.Length == 1 && inv.stacks[0].itemKey == "potion" && inv.stacks[0].count == 999999 && inv.stacks[0].equipped && inv.limits.stacks == 25, "stack inventory");
        var copy = ItemsJson.Clone(inv); copy.stacks[0].count = 1; Require(inv.stacks[0].count == 999999 && copy.limits.stacks == 25, "stack copy is defensive");
        Reject(() => ItemsJson.Inventory(head + ",\"stacks\":[{\"itemKey\":\"potion\",\"count\":1,\"equipped\":true},{\"itemKey\":\"potion\",\"count\":2,\"equipped\":false}],\"limits\":{\"catalog\":1,\"inventory\":1,\"receipts\":1}}"), "duplicate stack key");
        Reject(() => ItemsJson.Inventory(head + ",\"stacks\":[{\"itemKey\":\"potion\",\"count\":-1,\"equipped\":true}],\"limits\":{\"catalog\":1,\"inventory\":1,\"receipts\":1}}"), "negative stack count");
        string def = "{\"key\":\"potion\",\"name\":\"Potion\",\"type\":\"consumable\",\"rarity\":\"common\",\"active\":true,\"baseProperties\":{},\"propertyRules\":[]";
        string page = "{\"revision\":1,\"nextAfterKey\":null,\"total\":1,\"limits\":{\"catalog\":1,\"inventory\":1,\"receipts\":1,\"stacks\":25},\"serverTime\":\"2026-11-23T00:00:00Z\",\"definitions\":[";
        var legacy = ItemsJson.Catalog(page + def + "}]}").definitions[0];
        Require(legacy.mode == "unique" && legacy.maxStack == null, "old server definition is unique");
        var unique = ItemsJson.Catalog(page + def + ",\"mode\":\"unique\",\"maxStack\":null}]}").definitions[0];
        Require(unique.mode == "unique" && unique.maxStack == null, "unique definition with null maxStack");
        var stack = ItemsJson.Catalog(page + def + ",\"mode\":\"stack\",\"maxStack\":99}]}");
        Require(stack.definitions[0].mode == "stack" && stack.definitions[0].maxStack == 99 && ItemsJson.Clone(stack).definitions[0].maxStack == 99 && stack.limits.stacks == 25, "stack definition");
        Reject(() => ItemsJson.Catalog(page + def + ",\"mode\":\"bundle\"}]}"), "unknown mode");
        Reject(() => ItemsJson.Catalog(page + def + ",\"mode\":\"stack\",\"maxStack\":1000000}]}"), "maxStack bound");
        var grants = ItemsJson.StackGrants("[{\"itemKey\":\"potion\",\"quantity\":3,\"count\":42}]");
        Require(grants.Length == 1 && grants[0].quantity == 3 && grants[0].count == 42 && ItemsJson.StackGrants("[]").Length == 0, "stack grants");
        Reject(() => ItemsJson.StackGrants("[{\"itemKey\":\"potion\",\"quantity\":5,\"count\":5}]"), "stack grant quantity");
        string state = "{\"userId\":\"" + user + "\",\"day\":\"2026-11-23\",\"definitionVersion\":1,\"revision\":1,\"values\":[],\"operationWindow\":{\"serverTime\":\"2026-11-23T00:00:00Z\",\"oldestAcceptedAt\":\"2026-10-23T00:00:00Z\"},\"operation\":{\"operationId\":\"op\",\"replayed\":false,\"applied\":[],\"items\":[]";
        var bought = PlayerValuesJson.State(state + ",\"stacks\":[{\"itemKey\":\"potion\",\"quantity\":2,\"count\":9}]}}");
        Require(bought.operation.stacks.Length == 1 && bought.operation.stacks[0].count == 9 && PlayerValuesJson.Clone(bought).operation.stacks[0].quantity == 2, "purchase stack grants");
        Require(PlayerValuesJson.State(state + "}}").operation.stacks.Length == 0, "old server purchase has no stacks");
    }
    private static void Journal()
    {
        string root = Path.Combine(Path.GetTempPath(), "horizon-items-contract-" + Guid.NewGuid().ToString("N"));
        const string project = "11111111-1111-4111-8111-111111111111", user = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", key = "items-contract-key";
        Directory.CreateDirectory(root);
        try
        {
            var store = new ItemsSampleStore(root, project, user, key);
            string directory = Path.Combine(root, ItemsSampleStore.FileName(key, user));
            // Simulate a terminated former opener: the persistent inode remains, its exclusive handle is gone.
            File.WriteAllText(directory + ".lock", "old owner");
            var intent = new ItemsSampleStore.Intent { projectId = project, userId = user, action = "purchase", offerKey = "sword", operationId = "1792108800000:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb" };
            store.SaveNew(intent); var original = store.PrepareSend(); Require(original.Value.operationId == intent.operationId, "original purchase persisted");
            // A changed offer can legitimately become value-only before its first accepted execution.
            store.SaveSuccess(original.Hash, intent.operationId, Array.Empty<ItemReference>());
            var restarted = new ItemsSampleStore(root, project, user, key); Require(restarted.HasSuccess && restarted.ReadSuccess(original.Hash).serverConfirmed, "zero-item confirmed purchase survives restart");
            using (var competing = new FileStream(directory + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool blocked = false; try { restarted.PrepareSend(); } catch (IOException) { blocked = true; } Require(blocked, "active exclusive owner blocks another opener");
            }
            restarted.AcknowledgeSavedSuccess(); Require(!Directory.Exists(directory), "atomic complete journal archive");
            var archives = Directory.GetDirectories(root, "*.acknowledged"); Require(archives.Length == 1 && File.Exists(Path.Combine(archives[0], "intent.json")) && File.Exists(Path.Combine(archives[0], "result.json")), "original and receipt archived together");
            intent.action = "equip"; intent.instanceId = user; intent.revision = 0; store.SaveNew(intent); original = store.PrepareSend();
            store.SaveReconciledOriginal(original.Hash, 1); var receipt = store.ReadSuccess(original.Hash); Require(!receipt.serverConfirmed && receipt.observedRevision == 1, "explicit resolution does not claim server success");
            store.AcknowledgeSavedSuccess(); Require(!store.HasIntent, "reconciled original no longer blocks later intent");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (FormatException) { rejected = true; } Require(rejected, name); }
    private static void Require(bool yes, string name) { if (!yes) throw new Exception("Items: " + name); }
}
