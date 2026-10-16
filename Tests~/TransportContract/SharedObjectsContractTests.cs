using System;
using System.Linq;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Network.Responses;

static class SharedObjectsContractTests
{
    internal const string User = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", Id = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", Time = "2026-10-16T00:00:00Z";
    internal const string Limits = "{\"collections\":100,\"storageBytes\":2147483648,\"objectBytes\":65536}";
    internal static string Object(string data = "AP8A", string search = "{\"strength\":9007199254740991,\"label\":\"前\\u0000後😀\"}") => "{\"id\":\"" + Id + "\",\"collection\":\"levels\",\"ownerId\":\"" + User + "\",\"groupId\":null,\"revision\":9007199254740991,\"dataBase64\":\"" + data + "\",\"search\":" + search + ",\"position\":{\"x\":-9007199254740991,\"y\":9007199254740991},\"createdAt\":\"" + Time + "\",\"updatedAt\":\"" + Time + "\",\"expiresAt\":null}";
    public static void Run()
    {
        var o = SharedObjectsJson.Object(Object());
        Require(o.data.SequenceEqual(new byte[] { 0, 255, 0 }) && o.dataBase64 == "AP8A" && o.groupId == null && o.expiresAt == null, "opaque bytes/null flags");
        Require(o.search.Single(x => x.key == "label").value.stringValue == "前\0後😀" && o.search.Single(x => x.key == "strength").value.integerValue == 9007199254740991, "typed NUL/safe64 search");
        Require(o.position.x == -9007199254740991 && o.position.y == 9007199254740991 && o.revision == 9007199254740991, "exact grid/revision");
        var clone = SharedObjectsJson.Clone(o); clone.data[1] = 0; clone.search[0].value.stringValue = "changed";
        Require(o.data[1] == 255 && o.search[0].value.stringValue != "changed", "clone boundary");
        foreach (var n in new[] { "1.0000000000000001", "1e0", "-0", "9007199254740992", "-9007199254740992", "null", "false" }) Reject(() => SharedObjectsJson.Object(Object(search: "{\"str\\u0065ngth\":" + n + "}")), "original search lexeme " + n);
        foreach (var data in new[] { "AP8", "AP8A\n", "AB==", "A===", "-_8=" }) Reject(() => SharedObjectsJson.Object(Object(data)), "canonical Base64");
        Require(SharedObjectsJson.Object(Object("", "{}")).data.Length == 0, "empty bytes/search valid");
        Reject(() => SharedObjectsJson.Object(Object(search: "{\"a\":1,\"\\u0061\":2}")), "decoded duplicate search");
        Reject(() => SharedObjectsJson.Object(Object(search: "{\"a\\u0000b\":1}")), "illegal search key no truncation");
        Reject(() => SharedObjectsJson.Object(Object(search: "{\"label\":\"\\ud800\"}")), "invalid Unicode");
        Require(SharedObjectsJson.Object(Object(search: "{\"label\":\"" + new string('é', 48) + "\"}")).search[0].value.stringValue.Length == 48, "UTF8 exact96 boundary");
        Reject(() => SharedObjectsJson.Object(Object(search: "{\"label\":\"" + new string('é', 49) + "\"}")), "UTF8 over96");
        string raw = "{\"objects\":[],\"nextCursor\":\"opaque+/=\",\"pollAfterSeconds\":120,\"serverTime\":\"" + Time + "\"}";
        var page = SharedObjectsJson.Query(raw); Require(page.objects.Length == 0 && page.nextCursor == "opaque+/=", "empty bounded page still has cursor");
        Reject(() => SharedObjectsJson.Query(raw.Replace("120", "120.0")), "poll integer");
        Reject(() => SharedObjectsJson.Query(raw.Replace("\"objects\":[]", "\"objects\":[],\"unexpected\":true")), "strict response fields");
        var value = new SharedObjectInput { data = new byte[] { 0, 255, 0 }, search = new[] { new SharedSearchValue { key = "label", value = new SharedScalar { type = SharedScalarType.String, stringValue = "前\0後😀" } } }, position = new SharedPosition { x = -9007199254740991, y = 0 } };
        string body = SharedObjectsJson.InputJson(User, value, null, false);
        Require(body.Contains("AP8A") && body.Contains("前\\u0000後😀") && body.Contains("\"y\":0") && !body.Contains("groupId"), "complete frozen payload preserves empty/null/zero and excludes update group");
        var q = new SharedQuery { mode = SharedQueryMode.Field, field = "strength", min = new SharedScalar { integerValue = -9007199254740991 }, max = new SharedScalar { integerValue = 9007199254740991 } };
        Require(SharedObjectsJson.QueryJson(User, q).Contains("\"min\":-9007199254740991"), "typed range");
        q.mode = SharedQueryMode.Random; q.cursor = "opaque"; Reject(() => SharedObjectsJson.QueryJson(User, q), "random no cursor");
        q.cursor = null; q.equal = new SharedScalar(); Reject(() => SharedObjectsJson.QueryJson(User, q), "equal exclusive range");
        q = new SharedQuery { mode = SharedQueryMode.Owner }; Reject(() => SharedObjectsJson.QueryJson(User, q), "owner required");
        Console.WriteLine("Shared Objects strict7-route DTO, opaque bytes, safe integers, Unicode/NUL, bounded pages and immutable-input contracts passed.");
    }
    private static void Reject(Action action, string name)
    {
        bool rejected = false; try { action(); } catch (FormatException) { rejected = true; }
        Require(rejected, name);
    }
    private static void Require(bool value, string name) { if (!value) throw new Exception("Shared Objects: " + name); }
}
