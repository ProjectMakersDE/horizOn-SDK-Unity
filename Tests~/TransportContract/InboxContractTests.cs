using System;
using System.Reflection;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;
internal static class InboxContractTests
{
    private const string User = "00000000-0000-4000-8000-000000001006";
    private const string Entry = "message:00000000-0000-4000-8000-000000001006";
    private static string Snapshot(long revision = 1, string cursor = "one", bool unchanged = false, int second = 1) => "{\"userId\":\"" + User + "\",\"cursor\":\"" + cursor + "\",\"revision\":" + revision + ",\"broadcastRevision\":1,\"serverTime\":\"2026-10-16T00:00:" + second.ToString("D2") + "Z\",\"pollAfterSeconds\":120,\"unchanged\":" + (unchanged ? "true" : "false") + ",\"entries\":" + (unchanged ? "[]" : "[{\"id\":\"" + Entry + "\",\"kind\":\"message\",\"type\":\"gift\",\"textKey\":\"gift.ready\",\"reference\":\"source\",\"createdAt\":\"2026-10-16T00:00:00Z\",\"expiresAt\":\"2026-11-16T00:00:00Z\",\"overdue\":false,\"read\":false,\"claimed\":false,\"parameters\":{\"name\":\"Gold \\\"coins\\\" \\\\ path\\n⚡\",\"text\":\"\\\"amount\\\":9007199254740992\"},\"attachment\":{\"values\":[{\"key\":\"gold\",\"amount\":9007199254740991}]}}]") + ",\"counts\":{\"gift\":{\"total\":1,\"unread\":1,\"claimable\":1}},\"limits\":{\"targeted\":10,\"broadcasts\":10}}";
    public static async Task Run()
    {
        CompactContracts();
        var parsed = InboxJson.Snapshot(Snapshot());
        Check(parsed.counts["gift"].claimable == 1 && parsed.entries[0].parameters["name"] == "Gold \"coins\" \\ path\n⚡", "dictionary escapes preserved");
        Check(parsed.entries[0].attachment.values[0].amount == PlayerValuesManager.MaxSafeInteger, "safe reward exact");
        foreach (string lexeme in new[] { "9007199254740992", "9007199254740991.1", "1e3", "\"10\"" })
        {
            bool refused = false; try { InboxJson.Snapshot(Snapshot().Replace("\"amount\":9007199254740991", "\"amount\":" + lexeme)); } catch { refused = true; }
            Check(refused, "unsafe amount refused before rounding");
        }
        foreach (string invalid in new[] { "{\"x\":1,\"x\":2}", "{\"x\":01}", "{\"x\":true} trailing", "{\"x\":\"bad\\q\"}" })
        { bool refused = false; try { JsonFragments.Object(invalid); } catch { refused = true; } Check(refused, "invalid JSON refused"); }
        foreach (string name in new[] { "revision", "balance", "dailyCap" })
        {
            string escaped = "\\u" + ((int)name[0]).ToString("x4") + name.Substring(1);
            bool refused = false; try { PlayerValuesManager.PrepareJson("{\"" + escaped + "\":1e3}"); } catch { refused = true; }
            Check(refused, "escaped numeric field validates original lexeme");
        }
        var manager = new InboxManager(); manager.Init();
        var network = new NetworkService(); HorizonApp.Network = network; network.SetActiveHost("http://fixture.invalid"); network.SetSessionToken("inbox-session");
        PlayerPrefs.DeleteKey("horizOn_UserSession");
        var auth = new UserManager(); auth.Init(); network.SetSessionToken("inbox-session");
        typeof(UserManager).GetField("_currentUser", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(auth, new UserData { UserId = User, AccessToken = "inbox-session" });
        var first = new TaskCompletionSource<object>(); var second = new TaskCompletionSource<object>();
        int requests = 0; network.RespondAsync = (url, body) => { requests++; return requests == 1 ? first.Task : second.Task; };
        var a = manager.GetInbox(User); var b = manager.GetInbox(User); var c = manager.GetInbox(User);
        Check(requests == 1, "one active read");
        first.SetResult(NetworkResponse<InboxSnapshot>.Success(parsed)); await a;
        Check(requests == 2, "one coalesced followup");
        second.SetResult(NetworkResponse<InboxSnapshot>.Success(InboxJson.Snapshot(Snapshot(1, "one", true, 2))));
        var rb = await b; var rc = await c;
        Check(rb.Data.entries.Length == 1 && rc.Data.entries.Length == 1, "unchanged materializes cache");
        rb.Data.entries[0].parameters["name"] = "mutated";
        Check(rc.Data.entries[0].parameters["name"] != "mutated" && manager.CurrentInbox.entries[0].parameters["name"] != "mutated", "callers cannot mutate cache/another caller");
        var delayed = new TaskCompletionSource<object>(); network.RespondAsync = (_, _) => delayed.Task;
        var late = manager.GetInbox(User); manager.ClearCache(); delayed.SetResult(NetworkResponse<InboxSnapshot>.Success(InboxJson.Snapshot(Snapshot(2, "two", false, 3))));
        Check(!(await late).IsSuccess && manager.CurrentInbox == null, "clear rejects late result");
        network.RespondAsync = null; network.Respond = (_, _) => NetworkResponse<InboxSnapshot>.Success(InboxJson.Snapshot(Snapshot(0, "old", false, 4)));
        Check(!(await manager.GetInbox(User)).IsSuccess, "revision watermark survives clear");
        PlayerPrefs.SetString("horizOn_AnonymousToken", new string('x', 36));
        Check(!await auth.RestoreAnonymousSession() && auth.HasCachedAnonymousToken() && PlayerPrefs.GetString("horizOn_AnonymousToken").Length == 36, "invalid legacy identity remains recoverable");
        PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        var purchase = new PM.horizOn.Cloud.Examples.Features.PendingPlayerValuesPurchase.Intent { userId = User, offerKey = "pack", operationId = "1760000000000:11111111-1111-4111-8111-111111111111" };
        var stableKey = PM.horizOn.Cloud.Examples.Features.PendingPlayerValuesPurchase.Key("project", User);
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            string oldKey = "horizon_values_pending_" + BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("http://old|project|" + User))).Replace("-", "");
            PlayerPrefs.SetString(oldKey, JsonUtility.ToJson(purchase));
            Check(PM.horizOn.Cloud.Examples.Features.PendingPlayerValuesPurchase.TryLoad("project", User, new[] { "http://old", "http://new" }, out var restored) && restored.operationId == purchase.operationId, "legacy host migration keeps immutable ID");
            Check(!PlayerPrefs.HasKey(oldKey) && PlayerPrefs.HasKey(stableKey), "legacy key removed only after stable save");
            Check(!PM.horizOn.Cloud.Examples.Features.PendingPlayerValuesPurchase.Save("project", purchase), "pending intent cannot be replaced");
            PlayerPrefs.SetString(oldKey, JsonUtility.ToJson(new PM.horizOn.Cloud.Examples.Features.PendingPlayerValuesPurchase.Intent { userId = User, offerKey = "other", operationId = purchase.operationId }));
            Check(!PM.horizOn.Cloud.Examples.Features.PendingPlayerValuesPurchase.TryLoad("project", User, new[] { "http://old" }, out _) && PlayerPrefs.HasKey(oldKey) && PlayerPrefs.HasKey(stableKey), "conflicting host intents stay blocked and preserved");
            PlayerPrefs.DeleteKey(oldKey); PlayerPrefs.DeleteKey(stableKey);
        }
        var batch = new PM.horizOn.Cloud.Objects.Network.Requests.UpdateStatisticsRequest { userId = User, operationId = purchase.operationId,
            updates = new PM.horizOn.Cloud.Objects.Network.Requests.StatisticUpdate[201] };
        for (int i = 0; i < batch.updates.Length; i++) batch.updates[i] = new PM.horizOn.Cloud.Objects.Network.Requests.StatisticUpdate("stat" + i, i);
        string legacyBatchKey = "horizOn_statistics_pending_" + User;
        PlayerPrefs.SetString(legacyBatchKey, JsonUtility.ToJson(batch));
        Check(!PM.horizOn.Cloud.Examples.Features.StatisticsSampleStore.TryLoad("other-project", User, out _) && PlayerPrefs.HasKey(legacyBatchKey), "projectless statistics batch blocks and remains intact");
        PlayerPrefs.DeleteKey(legacyBatchKey);
        Check(PM.horizOn.Cloud.Examples.Features.StatisticsSampleStore.Save("project", batch), "statistics batch saved by project/player");
        Check(PM.horizOn.Cloud.Examples.Features.StatisticsSampleStore.TryLoad("other-project", User, out var otherBatch) && otherBatch == null, "project switch cannot reuse another project batch");
        Check(!PM.horizOn.Cloud.Examples.Features.StatisticsSampleStore.Save("project", batch), "existing statistics batch cannot be replaced");
        Check(PM.horizOn.Cloud.Examples.Features.StatisticsSampleStore.Confirm("project", batch), "only original batch can be confirmed");
        PlayerPrefs.SetString("horizOn_AnonymousToken", new string('x', 36));
        Check(PM.horizOn.Cloud.Examples.Features.StatisticsSampleStore.PrepareIdentity(out var restore) && restore, "legacy identity recovery persisted before restore");
        PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        Check(PM.horizOn.Cloud.Examples.Features.StatisticsSampleStore.PrepareIdentity(out restore) && restore && PlayerPrefs.GetString("horizOn_AnonymousToken").Length == 36, "cache loss cannot trigger signup after recovery");
        PlayerPrefs.DeleteKey("horizOn_AnonymousToken"); PlayerPrefs.DeleteKey("horizOn_statistics_identity_recovery"); PlayerPrefs.DeleteKey("horizOn_statistics_identity_started");
        Check(PM.horizOn.Cloud.Examples.Features.StatisticsSampleStore.PrepareIdentity(out restore) && !restore, "first signup is marked before transport");
        Check(!PM.horizOn.Cloud.Examples.Features.StatisticsSampleStore.PrepareIdentity(out restore), "unknown signup cannot create another identity");
        PlayerPrefs.DeleteKey("horizOn_statistics_identity_started");
        Console.WriteLine("Inbox JSON, bounded refresh queue, cache watermarks and anonymous identity contracts passed.");
    }
    private static string WithEntries(string rows, string limits = null)
    {
        string original = Snapshot();
        var fields = JsonFragments.Object(original);
        return original.Replace("\"entries\":" + fields["entries"], "\"entries\":" + rows)
            .Replace("\"limits\":" + fields["limits"], "\"limits\":" + (limits ?? "{\"targeted\":1,\"broadcasts\":1,\"freeTexts\":1,\"freeTextBytes\":512,\"freeTextRetentionDays\":30}"));
    }
    private static string SourceEntry(string type, string key, string reference, string parameters, string attachment = "null") =>
        "{\"id\":\"" + Entry + "\",\"kind\":\"message\",\"type\":\"" + type + "\",\"textKey\":\"" + key + "\",\"reference\":\"" + reference + "\",\"createdAt\":\"2030-01-01T00:00:00Z\",\"expiresAt\":\"2026-11-16T00:00:00Z\",\"overdue\":false,\"read\":false,\"claimed\":false,\"parameters\":" + parameters + ",\"attachment\":" + attachment + "}";
    private static void CompactContracts()
    {
        string text = new string('ä', 256);
        var plain = InboxJson.Snapshot(WithEntries("[" + SourceEntry("free_text", "horizon.inbox.free_text", "", "{\"text\":\"" + text + "\"}") + "]"));
        Check(InboxJson.Snapshot(WithEntries("[]", "{\"targeted\":1000,\"broadcasts\":2000,\"freeTexts\":20,\"freeTextBytes\":512,\"freeTextRetentionDays\":30}")).limits.broadcasts == 2000, "raised admin limits parse");
        Check(plain.limits.freeTextBytes == 512 && plain.limits.freeTextRetentionDays == 30 && plain.entries[0].TryGetFreeText(out var decoded) && decoded == text, "configured UTF8 free text and reverse clock dates");
        string row = JsonFragments.Array(JsonFragments.Object(Snapshot())["entries"])[0];
        var retained = new string[551];
        for (int i = 0; i < retained.Length; i++) retained[i] = row.Replace(Entry, "message:" + Guid.NewGuid().ToString("D"));
        Check(InboxJson.Snapshot(WithEntries("[" + string.Join(",", retained) + "]")).entries.Length == 551, "retained entries survive lower limits and old fixed bound");
        Check(InboxJson.Snapshot(Snapshot(PlayerValuesManager.MaxSafeInteger)).revision == PlayerValuesManager.MaxSafeInteger, "safe maximum revision remains exact");
        foreach (string bad in new[] { "1e3", "9007199254740992", "\"1\"" }) Refuses(Snapshot().Replace("\"revision\":1", "\"revision\":" + bad), "unsafe revision");
        Refuses(WithEntries("[" + row + "," + row + "]"), "duplicate entry identity");
        Refuses(WithEntries("[" + row.Replace(Entry, "message: " + User) + "]"), "entry UUID cannot be trimmed");
        string item = "{\"values\":[],\"itemReturn\":{\"listingId\":\"" + User + "\",\"instanceId\":\"" + User + "\",\"itemKey\":\"sword\"}}";
        var cases = new[] {
            new[] { "friend_request", "horizon.friends.request", "{\"userId\":\"" + User + "\"}" },
            new[] { "friend_accept", "horizon.friends.accept", "{\"userId\":\"" + User + "\"}" },
            new[] { "group_invite", "horizon.groups.invite", "{\"groupId\":\"" + User + "\"}" },
            new[] { "group_request", "horizon.groups.request", "{\"groupId\":\"" + User + "\"}" },
            new[] { "group_accept-invite", "horizon.groups.accept-invite", "{\"groupId\":\"" + User + "\"}" },
            new[] { "group_accept-request", "horizon.groups.accept-request", "{\"groupId\":\"" + User + "\"}" },
            new[] { "match_turn", "horizon.matches.your_turn", "{\"matchId\":\"" + User + "\",\"revision\":\"9007199254740991\"}" },
            new[] { "marketplace", "marketplace.item_return", "{}" }
        };
        foreach (var c in cases)
        {
            string source = SourceEntry(c[0], c[1], User, c[2], c[0] == "marketplace" ? item : "null");
            var parsed = InboxJson.Snapshot(WithEntries("[" + source + "]"));
            Check(parsed.entries[0].TryGetSource(out var reference) && reference.SourceId == User, "typed source " + c[0]);
            Refuses(WithEntries("[" + source.Replace("\"reference\":\"" + User + "\"", "\"reference\":\"bad\"") + "]"), "invalid source identity");
        }
        string market = SourceEntry("marketplace", "marketplace.item_return", User, "{}", item);
        var oldAcceptance = InboxJson.Snapshot(WithEntries("[" + SourceEntry("group_accept-invite", "horizon.groups.accept-invite", "00000000-0000-4000-8000-000000001007", "{\"groupId\":\"" + User + "\"}") + "]"));
        Check(oldAcceptance.entries[0].TryGetSource(out var oldGroup) && oldGroup.GroupId == User && oldGroup.SourceId != User, "legacy acceptance retains original request reference and group navigation");
        var returned = InboxJson.Snapshot(WithEntries("[" + market + "]"));
        Check(returned.entries[0].CanClaim && returned.entries[0].attachment.values.Length == 0, "item-only attachment is claimable");
        Check(InboxJson.Clone(plain).entries[0].attachment == null && !InboxJson.Clone(InboxJson.Snapshot(WithEntries("[" + row.Replace("9007199254740991", "1").Replace("[{\"key\":\"gold\",\"amount\":1}]", "[]") + "]"))).entries[0].CanClaim, "optional reward presence survives clone");
        var copy = InboxJson.Clone(returned); copy.entries[0].attachment.itemReturn.itemKey = "changed";
        Check(returned.entries[0].attachment.itemReturn.itemKey == "sword", "item descriptor deep clone");
        foreach (string bad in new[] { market.Replace("\"itemKey\":\"sword\"", "\"itemKey\":\"sword\",\"unknown\":1"), market.Replace("\"values\":[]", "\"values\":[],\"unknown\":1"), market.Replace("\"instanceId\":\"" + User + "\"", "\"instanceId\":\"bad\"") }) Refuses(WithEntries("[" + bad + "]"), "unsupported valuable content");
        string state = "{\"userId\":\"" + User + "\",\"day\":\"2026-10-16\",\"revision\":1,\"definitionVersion\":1,\"values\":[],\"operationWindow\":{\"serverTime\":\"2026-10-16T00:00:00Z\",\"oldestAcceptedAt\":\"2026-09-16T00:00:00Z\"}}";
        string claim = "{\"entryId\":\"" + Entry + "\",\"replayed\":true,\"inbox\":" + WithEntries("[]") + ",\"playerValues\":" + state + ",\"items\":[{\"instanceId\":\"" + User + "\",\"itemKey\":\"sword\"}]}";
        Check(InboxJson.Claim(claim).items.Length == 1, "claim keeps returned references");
    }
    private static void Refuses(string json, string message) { bool refused = false; try { InboxJson.Snapshot(json); } catch { refused = true; } Check(refused, message); }
    private static void Check(bool value, string message) { if (!value) throw new Exception("Inbox contract failed: " + message); }
}
