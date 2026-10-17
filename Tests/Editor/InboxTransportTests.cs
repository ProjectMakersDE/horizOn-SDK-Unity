using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;
namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")]
    public class InboxTransportTests
    {
        private const string Host = "http://127.0.0.1:18906", User = "00000000-0000-4000-8000-000000001006", Entry = "message:00000000-0000-4000-8000-000000001006";
        private HttpListener _listener;
        private HorizonConfig _config;
        private InboxManager Manager => InboxManager.Instance;
        [SetUp] public void Setup()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession"); NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            _config = ScriptableObject.CreateInstance<HorizonConfig>(); _config.SetApiKey("fixture-inbox"); _config.SetHosts(new[] { Host });
            NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost(Host); NetworkService.Instance.SetSessionToken("fixture-session");
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false; SetUser(User, "fixture-session");
            _listener = new HttpListener(); _listener.Prefixes.Add(Host + "/"); _listener.Start();
        }
        [TearDown] public void Teardown()
        {
            _listener?.Close();
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.name == "[UserManager]" || go.name == "[InboxManager]" || go.name == "[PlayerValuesManager]") UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(_config); PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }
        private static string Snapshot(long revision = 1, string cursor = "one", bool unchanged = false, bool entry = true, int second = 1)
        {
            string entries = "[{\"id\":\"" + Entry + "\",\"kind\":\"message\",\"type\":\"gift\",\"reference\":\"source\",\"textKey\":\"gift.ready\",\"createdAt\":\"2026-10-16T00:00:00Z\",\"expiresAt\":\"2026-11-16T00:00:00Z\",\"overdue\":false,\"read\":false,\"claimed\":false,\"parameters\":{\"display\":\"Gold \\\"coin\\\" \\\\path\\n⚡\",\"text\":\"\\\"amount\\\":9007199254740992\"},\"attachment\":{\"values\":[{\"key\":\"gold\",\"amount\":9007199254740991}]}}]";
            return "{\"userId\":\"" + User + "\",\"cursor\":\"" + cursor + "\",\"revision\":" + revision + ",\"broadcastRevision\":1,\"serverTime\":\"2026-10-16T00:00:" + second.ToString("D2") + "Z\",\"pollAfterSeconds\":120,\"unchanged\":" + (unchanged ? "true" : "false") + ",\"entries\":" + (unchanged || !entry ? "[]" : entries) + ",\"counts\":{\"gift\":{\"total\":1,\"unread\":1,\"claimable\":1}},\"limits\":{\"targeted\":10,\"broadcasts\":10}}";
        }
        private static string Values(long revision = 1) => "{\"userId\":\"" + User + "\",\"definitionVersion\":1,\"revision\":" + revision + ",\"day\":\"2026-10-16\",\"values\":[{\"key\":\"gold\",\"balance\":10,\"earnedToday\":10,\"dailyCap\":null}],\"operationWindow\":{\"serverTime\":\"2026-10-16T00:00:10Z\",\"oldestAcceptedAt\":\"2026-09-16T00:00:00Z\"}}";
        private async Task<InboxResult> Load(string body)
        { var incoming = _listener.GetContextAsync(); var pending = Manager.GetInbox(User); await Respond(await Timeout(incoming), 200, body); return await pending; }
        private static string WithEntries(string rows, long revision = 1)
        {
            string json = Snapshot(revision);
            int start = json.IndexOf("\"entries\":", StringComparison.Ordinal) + "\"entries\":".Length;
            int end = json.IndexOf(",\"counts\":", start, StringComparison.Ordinal);
            return json.Substring(0, start) + rows + json.Substring(end).Replace("\"limits\":{\"targeted\":10,\"broadcasts\":10}",
                "\"limits\":{\"targeted\":1,\"broadcasts\":1,\"freeTexts\":4,\"freeTextBytes\":512,\"freeTextRetentionDays\":30}");
        }
        private static string SourceEntry(string type, string key, string reference, string parameters, string attachment = "null") =>
            "{\"id\":\"" + Entry + "\",\"kind\":\"message\",\"type\":\"" + type + "\",\"textKey\":\"" + key + "\",\"reference\":\"" + reference + "\",\"createdAt\":\"2030-01-01T00:00:00Z\",\"expiresAt\":\"2026-11-16T00:00:00Z\",\"overdue\":false,\"read\":false,\"claimed\":false,\"parameters\":" + parameters + ",\"attachment\":" + attachment + "}";
        [Test] public async Task ConfiguredFreeTextAndRetainedEntriesRemainReadableAfterLimitsAreLowered()
        {
            string text = new string('ä', 256);
            string row = SourceEntry("free_text", "horizon.inbox.free_text", "", "{\"text\":\"" + text + "\"}");
            string rows = "[" + string.Join(",", Enumerable.Range(0, 551).Select(_ => row.Replace(Entry, "message:" + Guid.NewGuid().ToString("D")))) + "]";
            var result = await Load(WithEntries(rows, 9007199254740991L));
            Assert.That(result.IsSuccess, Is.True); Assert.That(result.Data.entries.Length, Is.EqualTo(551));
            Assert.That(result.Data.limits.freeTextBytes, Is.EqualTo(512)); Assert.That(result.Data.limits.freeTexts, Is.EqualTo(4));
            Assert.That(result.Data.entries[0].TryGetFreeText(out var decoded), Is.True); Assert.That(decoded, Is.EqualTo(text));
            Assert.That(Manager.CurrentInbox.revision, Is.EqualTo(9007199254740991L));
        }
        [TestCase("friend_request", "horizon.friends.request", "userId", InboxSourceKind.FriendRequest)]
        [TestCase("friend_accept", "horizon.friends.accept", "userId", InboxSourceKind.FriendAccepted)]
        [TestCase("group_invite", "horizon.groups.invite", "groupId", InboxSourceKind.GroupInvitation)]
        [TestCase("group_request", "horizon.groups.request", "groupId", InboxSourceKind.GroupRequest)]
        [TestCase("group_accept-invite", "horizon.groups.accept-invite", "groupId", InboxSourceKind.GroupInvitationAccepted)]
        [TestCase("group_accept-request", "horizon.groups.accept-request", "groupId", InboxSourceKind.GroupRequestAccepted)]
        [TestCase("match_turn", "horizon.matches.your_turn", "matchId", InboxSourceKind.MatchTurn)]
        public async Task TypedSourcesPreserveOriginalEntryIdentity(string type, string key, string parameter, InboxSourceKind expected)
        {
            string parameters = "{\"" + parameter + "\":\"" + User + "\"" + (type == "match_turn" ? ",\"revision\":\"9007199254740991\"" : "") + "}";
            var result = await Load(WithEntries("[" + SourceEntry(type, key, User, parameters) + "]"));
            Assert.That(result.IsSuccess, Is.True); Assert.That(result.Data.entries[0].id, Is.EqualTo(Entry));
            Assert.That(result.Data.entries[0].TryGetSource(out var source), Is.True); Assert.That(source.Kind, Is.EqualTo(expected));
            Assert.That(source.SourceId, Is.EqualTo(User));
        }
        [Test] public async Task ItemReturnIsClaimableAndClaimReplayPreservesOnlyAuthoritativeReferences()
        {
            string attachment = "{\"values\":[],\"itemReturn\":{\"listingId\":\"" + User + "\",\"instanceId\":\"" + User + "\",\"itemKey\":\"sword\"}}";
            var loaded = await Load(WithEntries("[" + SourceEntry("marketplace", "marketplace.item_return", User, "{}", attachment) + "]"));
            Assert.That(loaded.IsSuccess, Is.True); Assert.That(loaded.Data.entries[0].CanClaim, Is.True);
            Assert.That(loaded.Data.entries[0].TryGetSource(out var source), Is.True); Assert.That(source.Kind, Is.EqualTo(InboxSourceKind.MarketReturn));
            loaded.Data.entries[0].attachment.itemReturn.itemKey = "changed";
            Assert.That(Manager.CurrentInbox.entries[0].attachment.itemReturn.itemKey, Is.EqualTo("sword"));
            for (int i = 0; i < 2; i++)
            {
                var incoming = _listener.GetContextAsync(); var claim = Manager.Claim(User, Entry);
                await Respond(await Timeout(incoming), 200, "{\"entryId\":\"" + Entry + "\",\"replayed\":" + (i == 1 ? "true" : "false")
                    + ",\"inbox\":" + WithEntries("[]", 100 + i) + ",\"playerValues\":" + Values()
                    + ",\"items\":[{\"instanceId\":\"" + User + "\",\"itemKey\":\"sword\"}]}");
                var result = await claim; Assert.That(result.IsSuccess, Is.True); Assert.That(result.Data.items.Length, Is.EqualTo(1));
                Assert.That(result.Data.items[0].instanceId, Is.EqualTo(User)); Assert.That(PlayerValuesManager.Instance.CurrentState.values[0].balance, Is.EqualTo(10));
            }
        }
        [Test] public async Task LegacyGroupAcceptanceKeepsRequestReferenceAndGroupNavigation()
        {
            var result = await Load(WithEntries("[" + SourceEntry("group_accept-request", "horizon.groups.accept-request", "00000000-0000-4000-8000-000000001007", "{\"groupId\":\"" + User + "\"}") + "]"));
            Assert.That(result.IsSuccess, Is.True); Assert.That(result.Data.entries[0].TryGetSource(out var source), Is.True);
            Assert.That(source.GroupId, Is.EqualTo(User)); Assert.That(source.SourceId, Is.Not.EqualTo(User));
        }
        [TestCase("revision")] [TestCase("source")] [TestCase("attachment")] [TestCase("item")]
        public async Task MalformedSourceAndValuableContentCannotReplaceTheCache(string malformed)
        {
            string attachment = "{\"values\":[],\"itemReturn\":{\"listingId\":\"" + User + "\",\"instanceId\":\"" + User + "\",\"itemKey\":\"sword\"}}";
            string json = WithEntries("[" + SourceEntry("marketplace", "marketplace.item_return", User, "{}", attachment) + "]");
            if (malformed == "revision") json = json.Replace("\"revision\":1", "\"revision\":9007199254740992");
            if (malformed == "source") json = json.Replace("\"reference\":\"" + User + "\"", "\"reference\":\"bad\"");
            if (malformed == "attachment") json = json.Replace("\"values\":[]", "\"values\":[],\"unknownReward\":1");
            if (malformed == "item") json = json.Replace("\"itemKey\":\"sword\"", "\"itemKey\":\"sword\",\"unknownReward\":1");
            Assert.That((await Load(json)).ErrorCode, Is.EqualTo("INVALID_RESPONSE")); Assert.That(Manager.CurrentInbox, Is.Null);
        }
        [Test] public async Task SnapshotUsesBearerAndPreservesDictionaryStringsAndSafeRewards()
        {
            var incoming = _listener.GetContextAsync(); var pending = Manager.GetInbox(User); var context = await Timeout(incoming);
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/inbox?userId=" + User));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer fixture-session")); Assert.That(context.Request.Headers["X-API-Key"], Is.EqualTo("fixture-inbox"));
            await Respond(context, 200, Snapshot()); var result = await pending;
            Assert.That(result.IsSuccess, Is.True); Assert.That(result.Data.counts["gift"].claimable, Is.EqualTo(1));
            Assert.That(result.Data.entries[0].parameters["display"], Is.EqualTo("Gold \"coin\" \\path\n⚡"));
            Assert.That(result.Data.entries[0].attachment.values[0].amount, Is.EqualTo(9007199254740991L));
            result.Data.entries[0].parameters["display"] = "mutated"; Assert.That(Manager.CurrentInbox.entries[0].parameters["display"], Is.Not.EqualTo("mutated"));
        }
        [Test] public async Task ConcurrentRefreshesCoalesceOneFollowupAndKeepUnchangedEntries()
        {
            var incoming = _listener.GetContextAsync(); var first = Manager.GetInbox(User); var context = await Timeout(incoming);
            var second = Manager.GetInbox(User); var third = Manager.GetInbox(User);
            var next = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(next, Task.Delay(80)), Is.Not.SameAs(next));
            await Respond(context, 200, Snapshot()); Assert.That((await first).IsSuccess, Is.True);
            context = await Timeout(next); Assert.That(context.Request.RawUrl, Does.Contain("since=one"));
            await Respond(context, 200, Snapshot(1, "one", true, second: 2));
            var a = await second; var b = await third; Assert.That(a.Data.entries.Length, Is.EqualTo(1));
            a.Data.entries[0].parameters.Clear(); Assert.That(b.Data.entries[0].parameters, Is.Not.Empty);
            Assert.That((await Load(Snapshot(1, "expiry", false, false, 3))).Data.entries, Is.Empty, "equal storage revisions still accept a later expiry cursor");
        }
        [Test] public async Task ConditionalReadWithoutCachedEntriesReloadsOneFullSnapshot()
        {
            var incoming = _listener.GetContextAsync(); var pending = Manager.GetInbox(User, "one");
            await Respond(await Timeout(incoming), 200, Snapshot(1, "one", true));
            var context = await Timeout(_listener.GetContextAsync()); Assert.That(context.Request.RawUrl, Does.Not.Contain("since="));
            await Respond(context, 200, Snapshot()); Assert.That((await pending).Data.entries.Length, Is.EqualTo(1));
        }
        [Test] public async Task ClaimReplayReplacesBalancesAndNeverAddsRewardTwice()
        {
            for (int i = 0; i < 2; i++)
            {
                var incoming = _listener.GetContextAsync(); var pending = Manager.Claim(User, Entry); var context = await Timeout(incoming);
                var body = await new StreamReader(context.Request.InputStream).ReadToEndAsync(); Assert.That(body, Does.Contain(Entry)); Assert.That(body, Does.Contain(User));
                Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/inbox/claim"));
                await Respond(context, 200, "{\"entryId\":\"" + Entry + "\",\"replayed\":" + (i == 1 ? "true" : "false") + ",\"inbox\":" + Snapshot(2, "claimed", second: 10) + ",\"playerValues\":" + Values() + "}");
                var result = await pending; Assert.That(result.IsSuccess, Is.True); Assert.That(result.Data.replayed, Is.EqualTo(i == 1));
                Assert.That(PlayerValuesManager.Instance.CurrentState.values[0].balance, Is.EqualTo(10));
            }
        }
        [Test] public async Task ReadAndRemoveUseOriginalEntryAndBusinessRefusalIsVisible()
        {
            var incoming = _listener.GetContextAsync(); var pending = Manager.MarkRead(User, Entry); var context = await Timeout(incoming);
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/inbox/read")); await Respond(context, 200, Snapshot(2)); Assert.That((await pending).IsSuccess, Is.True);
            incoming = _listener.GetContextAsync(); pending = Manager.Remove(User, Entry); context = await Timeout(incoming);
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/inbox/remove")); await Respond(context, 409, "{\"code\":\"UNCLAIMED_REWARD\",\"message\":\"Claim first\"}");
            Assert.That((await pending).ErrorCode, Is.EqualTo("UNCLAIMED_REWARD"));
            incoming = _listener.GetContextAsync(); pending = Manager.Remove(User, Entry); await Respond(await Timeout(incoming), 200, Snapshot(3, "removed", entry: false)); Assert.That((await pending).Data.entries, Is.Empty);
        }
        [TestCase("9007199254740991.1")]
        [TestCase("9007199254740992")]
        [TestCase("1e3")]
        public async Task UnsafeRewardDoesNotRoundOrReplaceCache(string number)
        {
            var result = await Load(Snapshot().Replace("\"amount\":9007199254740991", "\"amount\":" + number));
            Assert.That(result.ErrorCode, Is.EqualTo("INVALID_RESPONSE")); Assert.That(Manager.CurrentInbox, Is.Null);
        }
        [Test] public async Task LateReplyAfterClearOrProjectSwitchCannotOverwriteCurrentScope()
        {
            var incoming = _listener.GetContextAsync(); var pending = Manager.GetInbox(User); var context = await Timeout(incoming);
            Manager.ClearCache(); await Respond(context, 200, Snapshot()); Assert.That((await pending).ErrorCode, Is.EqualTo("STALE_RESPONSE"));
            incoming = _listener.GetContextAsync(); pending = Manager.GetInbox(User); context = await Timeout(incoming); NetworkService.Instance.SetSessionToken("new-session");
            await Respond(context, 200, Snapshot(2)); Assert.That((await pending).ErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That(Manager.CurrentInbox, Is.Null);
            NetworkService.Instance.SetSessionToken("fixture-session");
            incoming = _listener.GetContextAsync(); pending = Manager.GetInbox(User); context = await Timeout(incoming); _config.SetApiKey("other-project");
            await Respond(context, 200, Snapshot(3)); Assert.That((await pending).ErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That(Manager.CurrentInbox, Is.Null);
        }
        [Test] public async Task EscapedUnsafeRevisionCannotPoisonPlayerValueWatermark()
        {
            string poisoned = Values().Replace("\"revision\":1", "\"\\u0072evision\":1e3");
            var incoming = _listener.GetContextAsync(); var pending = Manager.Claim(User, Entry);
            await Respond(await Timeout(incoming), 200, "{\"entryId\":\"" + Entry + "\",\"replayed\":false,\"inbox\":" + Snapshot() + ",\"playerValues\":" + poisoned + "}");
            Assert.That((await pending).ErrorCode, Is.EqualTo("INVALID_RESPONSE")); Assert.That(PlayerValuesManager.Instance.CurrentState, Is.Null);
            incoming = _listener.GetContextAsync(); pending = Manager.Claim(User, Entry);
            await Respond(await Timeout(incoming), 200, "{\"entryId\":\"" + Entry + "\",\"replayed\":true,\"inbox\":" + Snapshot(2) + ",\"playerValues\":" + Values(2) + "}");
            Assert.That((await pending).IsSuccess, Is.True); Assert.That(PlayerValuesManager.Instance.CurrentState.revision, Is.EqualTo(2));
        }
        [Test] public async Task MutationFencesAnOlderInFlightRead()
        {
            var incoming = _listener.GetContextAsync(); var read = Manager.GetInbox(User); var old = await Timeout(incoming);
            incoming = _listener.GetContextAsync(); var claim = Manager.Claim(User, Entry);
            await Respond(await Timeout(incoming), 200, "{\"entryId\":\"" + Entry + "\",\"replayed\":false,\"inbox\":" + Snapshot(2, "claimed", second: 10) + ",\"playerValues\":" + Values() + "}");
            Assert.That((await claim).IsSuccess, Is.True);
            await Respond(old, 200, Snapshot(99, "late", second: 20));
            Assert.That((await read).ErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That(Manager.CurrentInbox.cursor, Is.EqualTo("claimed"));
        }
        [Test] public async Task ClearingValuesDuringClaimRejectsTheWholeLateSnapshot()
        {
            var incoming = _listener.GetContextAsync(); var pending = Manager.Claim(User, Entry); var context = await Timeout(incoming);
            PlayerValuesManager.Instance.ClearCache();
            await Respond(context, 200, "{\"entryId\":\"" + Entry + "\",\"replayed\":false,\"inbox\":" + Snapshot() + ",\"playerValues\":" + Values() + "}");
            Assert.That((await pending).ErrorCode, Is.EqualTo("STALE_RESPONSE"));
            Assert.That(PlayerValuesManager.Instance.CurrentState, Is.Null); Assert.That(Manager.CurrentInbox, Is.Null);
        }
        [Test] public async Task WatermarksSurviveClearingTheMaterializedSnapshot()
        {
            Assert.That((await Load(Snapshot(10, "new", second: 10))).IsSuccess, Is.True); Manager.ClearCache();
            Assert.That((await Load(Snapshot(9, "old", second: 11))).ErrorCode, Is.EqualTo("STALE_RESPONSE"));
            Assert.That((await Load(Snapshot(11, "oldclock", second: 9))).ErrorCode, Is.EqualTo("STALE_RESPONSE"));
        }
        [Test] public async Task InvalidIdentityOrForeignActionNeverCreatesAnHttpRequest()
        {
            Assert.That((await Manager.GetInbox("foreign")).ErrorCode, Is.EqualTo("SESSION_FORBIDDEN"));
            Assert.That((await Manager.Claim(User, "not-an-entry")).ErrorCode, Is.EqualTo("INVALID_REQUEST"));
            PlayerPrefs.SetString("horizOn_AnonymousToken", new string('x', 36));
            Assert.That(await UserManager.Instance.RestoreAnonymousSession(), Is.False); Assert.That(UserManager.Instance.HasCachedAnonymousToken(), Is.True);
            Assert.That(PlayerPrefs.GetString("horizOn_AnonymousToken").Length, Is.EqualTo(36));
            var unexpected = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(unexpected, Task.Delay(100)), Is.Not.SameAs(unexpected));
        }
        private static void SetUser(string id, string token) => typeof(UserManager).GetField("_currentUser", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(UserManager.Instance, new UserData { UserId = id, AccessToken = token });
        private static async Task<T> Timeout<T>(Task<T> task) { if (await Task.WhenAny(task, Task.Delay(5000)) != task) throw new TimeoutException(); return await task; }
        private static async Task Respond(HttpListenerContext context, int status, string body)
        { context.Response.StatusCode = status; context.Response.ContentType = "application/json"; using (var writer = new StreamWriter(context.Response.OutputStream)) await writer.WriteAsync(body); context.Response.Close(); }
    }
}
