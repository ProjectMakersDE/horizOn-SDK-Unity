using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")] public class ItemsTransportTests
    {
        internal const string Host = "http://127.0.0.1:18911", User = "00000000-0000-4000-8000-000000001011", Item = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", Project = "11111111-1111-4111-8111-111111111111";
        internal const string Time = "2026-10-16T00:00:00Z", ApiKey = "fixture-items";
        private HttpListener _listener; private HorizonConfig _config;
        private static readonly Dictionary<string, int> HttpCounts = new Dictionary<string, int>();
        private ItemsManager Manager => ItemsManager.Instance;
        internal static string Limits => "{\"catalog\":20,\"inventory\":10,\"receipts\":10}";
        internal static string Instance(bool equipped = false, string properties = "{\"hp\":9007199254740991,\"label\":\"前\\u0000後😀\",\"bound\":true}") => "{\"instanceId\":\"" + Item + "\",\"itemKey\":\"sword\",\"properties\":" + properties + ",\"equipped\":" + (equipped ? "true" : "false") + ",\"createdAt\":\"" + Time + "\"}";
        internal static string Inventory(long revision = 0, string instances = null, string user = User, string time = Time) => "{\"userId\":\"" + user + "\",\"revision\":" + revision + ",\"instances\":" + (instances ?? "[" + Instance() + "]") + ",\"limits\":" + Limits + ",\"serverTime\":\"" + time + "\"}";
        internal static string Definition(string key = "sword") => "{\"key\":\"" + key + "\",\"name\":\"前\\u0000後😀\",\"type\":\"weapon\",\"rarity\":\"rare\",\"active\":false,\"baseProperties\":{\"hp\":9007199254740991,\"label\":\"前\\u0000後😀\"},\"propertyRules\":[{\"key\":\"hp\",\"min\":0,\"max\":9007199254740991,\"maxChangePerCall\":5}]}";
        internal static string Catalog(long revision = 0, string definitions = null, string next = "null", string time = Time) => "{\"revision\":" + revision + ",\"definitions\":" + (definitions ?? "[" + Definition() + "]") + ",\"nextAfterKey\":" + next + ",\"total\":21,\"limits\":" + Limits + ",\"serverTime\":\"" + time + "\"}";
        [SetUp] public void Setup()
        {
            HttpCounts.Clear();
            PlayerPrefs.DeleteKey("horizOn_UserSession"); NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            _config = ScriptableObject.CreateInstance<HorizonConfig>(); _config.SetApiKey(ApiKey); _config.SetHosts(new[] { Host });
            NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost(Host); SetUser(User);
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false;
            _listener = new HttpListener(); _listener.Prefixes.Add(Host + "/"); _listener.Start();
        }
        [TearDown] public void Stop()
        {
            Debug.Log("ITEMS_HTTP_COUNTS " + string.Join(",", HttpCounts.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)));
            _listener?.Close();
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (new[] { "[ItemsManager]", "[UserManager]", "[PlayerValuesManager]", "[GroupsManager]", "[ItemsExampleTest]" }.Contains(go.name)) UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(_config); PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }
        [Test] public async Task InventoryAndCatalogUseBearerAndPreserveTypedNulAndSafeIntegers()
        {
            var request = _listener.GetContextAsync(); var call = Manager.GetInventory(User); var c = await Timeout(request);
            Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/items?userId=" + User)); Headers(c); await Respond(c, Inventory());
            var result = await call; Assert.That(result.IsSuccess, Is.True); Assert.That(result.Data.instances[0].properties.Single(x => x.key == "hp").integerValue, Is.EqualTo(9007199254740991));
            Assert.That(result.Data.instances[0].properties.Single(x => x.key == "label").stringValue, Is.EqualTo("前\0後😀"));
            result.Data.instances[0].properties[0].key = "changed"; Assert.That(Manager.CurrentInventory.instances[0].properties[0].key, Is.Not.EqualTo("changed"));
            request = _listener.GetContextAsync(); var page = Manager.GetCatalog(User, "spear", 7, 0); c = await Timeout(request);
            Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/items/catalog?userId=" + User + "&limit=7&afterKey=spear&catalogRevision=0")); Headers(c); await Respond(c, Catalog());
            var catalog = await page; Assert.That(catalog.IsSuccess, Is.True); Assert.That(catalog.Data.definitions[0].name, Is.EqualTo("前\0後😀"));
            Assert.That(catalog.Data.definitions[0].active, Is.False); Assert.That(catalog.Data.total, Is.EqualTo(21));
            catalog.Data.definitions[0].baseProperties[0].integerValue = 1; Assert.That(Manager.CurrentCatalogPage.definitions[0].baseProperties[0].integerValue, Is.EqualTo(9007199254740991));
        }
        [TestCase("adjust")][TestCase("equip")][TestCase("discard")]
        public async Task MutationsFreezeOriginalInstanceRevisionAndCompleteFalseBody(string action)
        {
            var changes = new[] { new ItemPropertyDelta { key = "hp", amount = -9007199254740991 } };
            var request = _listener.GetContextAsync();
            var call = action == "adjust" ? Manager.AdjustProperties(User, Item, 0, changes) : action == "equip" ? Manager.SetEquipped(User, Item, 0, false) : Manager.DiscardItem(User, Item, 0);
            changes[0].amount = 2; var c = await Timeout(request); Headers(c); Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/items/" + action));
            string body = await new StreamReader(c.Request.InputStream).ReadToEndAsync();
            Assert.That(body, Does.Contain("\"expectedRevision\":0").And.Contain(Item));
            if (action == "adjust") Assert.That(body, Does.Contain("\"changes\":[{\"key\":\"hp\",\"amount\":-9007199254740991}]"));
            if (action == "equip") Assert.That(body, Does.Contain("\"equipped\":false"));
            await Respond(c, Inventory(1)); Assert.That((await call).IsSuccess, Is.True);
        }
        [TestCase(429)][TestCase(503)] public async Task InventoryWritesHaveExactlyOneTransportAttempt(int status)
        {
            var request = _listener.GetContextAsync(); var task = Manager.SetEquipped(User, Item, 9, true); var c = await Timeout(request);
            await Respond(c, "{\"message\":\"uncertain\"}", status); Assert.That((await Timeout(task)).IsSuccess, Is.False);
            var extra = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(extra, Task.Delay(150)), Is.Not.SameAs(extra)); Assert.That(Manager.CurrentInventory, Is.Null);
        }
        [Test] public async Task InvalidInputsAndForeignPlayersNeverReachHttp()
        {
            Assert.That((await Manager.GetInventory(Project)).ErrorCode, Is.EqualTo("SESSION_FORBIDDEN"));
            Assert.That((await Manager.GetCatalog(User, "bad\0key")).IsSuccess, Is.False);
            Assert.That((await Manager.GetCatalog(User, limit: 51)).IsSuccess, Is.False);
            Assert.That((await Manager.GetCatalog(User, catalogRevision: -1)).IsSuccess, Is.False);
            foreach (long r in new[] { -1L, 9007199254740992L }) Assert.That((await Manager.DiscardItem(User, Item, r)).IsSuccess, Is.False);
            Assert.That((await Manager.SetEquipped(User, "wrong", 0, true)).IsSuccess, Is.False);
            foreach (long n in new[] { 0L, 9007199254740992L, -9007199254740992L }) Assert.That((await Manager.AdjustProperties(User, Item, 0, new[] { new ItemPropertyDelta { key = "hp", amount = n } })).IsSuccess, Is.False);
            Assert.That((await Manager.AdjustProperties(User, Item, 0, new[] { new ItemPropertyDelta { key = "hp", amount = 1 }, new ItemPropertyDelta { key = "hp", amount = 2 } })).IsSuccess, Is.False);
            NetworkService.Instance.ClearSessionToken(); Assert.That((await Manager.GetInventory(User)).ErrorCode, Is.EqualTo("SESSION_REQUIRED"));
            var request = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(request, Task.Delay(100)), Is.Not.SameAs(request));
        }
        [TestCase("1.0000000000000001")][TestCase("1e0")][TestCase("-0")][TestCase("9007199254740992")][TestCase("null")]
        public async Task UnsafeOriginalIntegerTokensAreRejected(string number)
        {
            var r = await Reply(Manager.GetInventory(User), Inventory().Replace("\"hp\":9007199254740991", "\"h\\u0070\":" + number)); Assert.That(r.IsSuccess, Is.False); Assert.That(Manager.CurrentInventory, Is.Null);
        }
        [TestCase("{\"hp\":1,\"h\\u0070\":2}")][TestCase("{\"hp\\u0000suffix\":1}")][TestCase("{\"s\":\"\\ud800\"}")][TestCase("{\"s\":null}")]
        public async Task InvalidPropertyShapesAreNeverNormalized(string properties)
        { Assert.That((await Reply(Manager.GetInventory(User), Inventory(instances: "[" + Instance(properties: properties) + "]"))).IsSuccess, Is.False); }
        [Test] public async Task CatalogRejectsMixedRevisionsBackwardOrNonmatchingCursors()
        {
            Assert.That((await Reply(Manager.GetCatalog(User, catalogRevision: 0), Catalog(1))).IsSuccess, Is.False);
            Assert.That((await Reply(Manager.GetCatalog(User, afterKey: "sword"), Catalog())).IsSuccess, Is.False);
            Assert.That((await Reply(Manager.GetCatalog(User), Catalog(next: "\"wrong\""))).IsSuccess, Is.False);
            Assert.That((await Reply(Manager.GetCatalog(User), Catalog(definitions: "[" + Definition("z") + "," + Definition("a") + "]"))).IsSuccess, Is.False);
        }
        [Test] public async Task InventoryAndCatalogWatermarksStayIndependentAndSurviveExplicitClear()
        {
            Assert.That((await Reply(Manager.GetInventory(User), Inventory(9, time: "2026-10-16T00:00:09Z"))).IsSuccess, Is.True);
            Assert.That((await Reply(Manager.GetCatalog(User), Catalog(1))).IsSuccess, Is.True);
            Manager.ClearCache(); Assert.That((await Reply(Manager.GetInventory(User), Inventory(8, time: "2026-10-16T00:00:10Z"))).ErrorCode, Is.EqualTo("STALE_RESPONSE"));
            Assert.That((await Reply(Manager.GetInventory(User), Inventory(9))).ErrorCode, Is.EqualTo("STALE_RESPONSE"));
            Assert.That((await Reply(Manager.GetInventory(User), Inventory(10, instances: "[]", time: "2026-10-16T00:00:10Z"))).IsSuccess, Is.True);
        }
        [TestCase("host")][TestCase("project")][TestCase("session")]
        public async Task DelayedRepliesAreFencedAcrossAwayAndBack(string kind)
        {
            var request = _listener.GetContextAsync(); var task = Manager.GetInventory(User); var c = await Timeout(request);
            if (kind == "host") { NetworkService.Instance.SetActiveHost("http://unused"); NetworkService.Instance.SetActiveHost(Host); }
            if (kind == "project") { _config.SetApiKey("other"); _config.SetApiKey(ApiKey); }
            if (kind == "session") { NetworkService.Instance.SetSessionToken("other"); NetworkService.Instance.SetSessionToken("session-" + User); }
            await Respond(c, Inventory()); Assert.That((await task).ErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That(Manager.CurrentInventory, Is.Null);
        }
        [Test] public async Task ClearDuringReadAllowsExplicitQueuedFollowupWithoutParallelHttp()
        {
            var incoming = _listener.GetContextAsync(); var old = Manager.GetInventory(User); var c = await Timeout(incoming);
            Manager.ClearCache(); var next = Manager.GetInventory(User); var nextIncoming = _listener.GetContextAsync();
            Assert.That(await Task.WhenAny(nextIncoming, Task.Delay(100)), Is.Not.SameAs(nextIncoming));
            await Respond(c, Inventory()); Assert.That((await old).ErrorCode, Is.EqualTo("STALE_RESPONSE"));
            c = await Timeout(nextIncoming); await Respond(c, Inventory(1)); Assert.That((await next).IsSuccess, Is.True);
        }
        [Test] public async Task ForeignAndOlderMutationSnapshotsNeverEnterCache()
        {
            Assert.That((await Reply(Manager.GetInventory(User), Inventory(user: Project))).IsSuccess, Is.False);
            Assert.That((await Reply(Manager.DiscardItem(User, Item, 9), Inventory(8))).IsSuccess, Is.False);
        }
        private static string ValuesState(string operationId = null, bool replayed = false) => "{\"userId\":\"" + User + "\",\"day\":\"2026-10-16\",\"definitionVersion\":1,\"revision\":1,\"values\":[{\"key\":\"coins\",\"balance\":80,\"earnedToday\":0,\"dailyCap\":null}],\"operationWindow\":{\"serverTime\":\"" + Time + "\",\"oldestAcceptedAt\":\"2026-09-16T00:00:00Z\"}" + (operationId == null ? "" : ",\"operation\":{\"operationId\":\"" + operationId + "\",\"replayed\":" + (replayed ? "true" : "false") + ",\"applied\":[],\"items\":" + (replayed ? "[]" : "[{\"instanceId\":\"" + Item + "\",\"itemKey\":\"sword\"}]") + "}") + "}";
        private static string Offers(bool include = true) => "{\"definitionVersion\":1,\"serverTime\":\"" + Time + "\",\"definitions\":[],\"offers\":" + (include ? "[{\"key\":\"buy_sword\",\"name\":\"前\\u0000後\",\"active\":true,\"costs\":[{\"key\":\"coins\",\"amount\":20}],\"rewards\":[],\"itemRewards\":[{\"itemKey\":\"sword\",\"quantity\":1}],\"purchasesPerDay\":null}]" : "[]") + "}";
        private static Type SampleType => Type.GetType("PM.horizOn.Cloud.Examples.Features.ItemsExample, Assembly-CSharp", true);
        private static Component Actor()
        {
            var actor = new GameObject("[ItemsExampleTest]").AddComponent(SampleType);
            SampleType.GetField("projectId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(actor, Project); return actor;
        }
        private static Task<bool> Act(Component actor, string method, params object[] arguments) => (Task<bool>)SampleType.GetMethod(method).Invoke(actor, arguments);
        private static void Ack(Component actor) => SampleType.GetMethod("AcknowledgeSavedSuccess").Invoke(actor, null);
        private static string JournalDirectory => Path.Combine(Application.persistentDataPath, "items-" + Hash(ApiKey) + "-" + User);
        private static string JournalPath => Path.Combine(JournalDirectory, "intent.json");
        private static string SuccessPath => Path.Combine(JournalDirectory, "result.json");
        private static string Hash(string s) { using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s))).Replace("-", "").ToLowerInvariant(); }
        private static void CleanupJournal()
        {
            foreach (var directory in Directory.GetDirectories(Application.persistentDataPath, Path.GetFileName(JournalDirectory) + "*")) Directory.Delete(directory, true);
            string gate = JournalDirectory + ".lock"; if (File.Exists(gate)) File.Delete(gate);
        }
        private async Task<bool> OneAction(Component actor, string method, object[] arguments, string json)
        { var task = Act(actor, method, arguments); await Respond(await Timeout(_listener.GetContextAsync()), json); return await Timeout(task); }
        private async Task ReviewOffers(Component actor)
        {
            Assert.That(await OneAction(actor, "ReadOneCatalogPage", new object[] { null, null }, Catalog().Replace("\"active\":false", "\"active\":true")), Is.True);
            var task = Act(actor, "ReviewOffers"); await Respond(await Timeout(_listener.GetContextAsync()), ValuesState()); await Respond(await Timeout(_listener.GetContextAsync()), Offers()); Assert.That(await Timeout(task), Is.True);
        }
        private async Task<string> SendPurchase(Component actor, bool inventoryFails, bool purchaseFails = false)
        {
            var task = Act(actor, "SavePurchaseAfterReview", "buy_sword");
            await Respond(await Timeout(_listener.GetContextAsync()), Offers());
            var c = await Timeout(_listener.GetContextAsync()); string body = await new StreamReader(c.Request.InputStream).ReadToEndAsync(); var wire = JsonUtility.FromJson<PurchaseWire>(body);
            Assert.That(File.Exists(JournalPath), Is.True); Assert.That(File.ReadAllText(JournalPath), Does.Contain(wire.operationId));
            if (purchaseFails)
            {
                for (int attempt = 0; attempt <= _config.MaxRetryAttempts; attempt++)
                {
                    if (attempt > 0) { c = await Timeout(_listener.GetContextAsync()); Assert.That(await new StreamReader(c.Request.InputStream).ReadToEndAsync(), Is.EqualTo(body)); }
                    Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/player-values/purchase"));
                    await Respond(c, "{\"code\":\"TEST_UNCERTAIN\",\"message\":\"lost acknowledgement\"}", 503);
                }
                Assert.That(await Timeout(task), Is.False); return wire.operationId;
            }
            await Respond(c, ValuesState(wire.operationId)); c = await Timeout(_listener.GetContextAsync());
            Assert.That(c.Request.HttpMethod, Is.EqualTo("GET")); Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/items?userId=" + User));
            Assert.That(File.Exists(SuccessPath), Is.True); Assert.That(File.ReadAllText(SuccessPath), Does.Contain(wire.operationId).And.Contain(Item));
            if (inventoryFails)
            {
                for (int attempt = 0; attempt <= _config.MaxRetryAttempts; attempt++)
                {
                    if (attempt > 0) c = await Timeout(_listener.GetContextAsync());
                    Assert.That(c.Request.HttpMethod, Is.EqualTo("GET")); Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/items?userId=" + User));
                    Assert.That(File.Exists(SuccessPath), Is.True); await Respond(c, "{\"code\":\"TEST_UNAVAILABLE\"}", 503);
                }
            }
            else await Respond(c, Inventory(1));
            Assert.That(await Timeout(task), Is.EqualTo(!inventoryFails)); return wire.operationId;
        }
        [Serializable] private class PurchaseWire { public string userId, offerKey, operationId; }
        [Test] public async Task ActualItemsSampleExecutesCatalogPurchaseInventoryEquipAdjustAndExplicitDiscard()
        {
            Assert.That(File.Exists(JournalPath), Is.False); var actor = Actor();
            try
            {
                File.WriteAllText(JournalDirectory + ".lock", "a former terminated opener");
                Assert.That(await OneAction(actor, "ReadOneCatalogPage", new object[] { null, null }, Catalog()), Is.True);
                await ReviewOffers(actor); await SendPurchase(actor, false);
                Assert.That(PlayerValuesManager.Instance.CurrentOffers.offers[0].name, Is.EqualTo("前\0後"));
                Assert.That(PlayerValuesManager.Instance.CurrentState.operation.items[0].instanceId, Is.EqualTo(Item)); Ack(actor);
                Assert.That(await OneAction(actor, "SaveEquipAfterReview", new object[] { Item, true }, Inventory(2, "[" + Instance(true) + "]")), Is.True); Ack(actor);
                Assert.That(await OneAction(actor, "SaveAdjustAfterReview", new object[] { Item, "hp", -1L }, Inventory(3, "[" + Instance(true, "{\"hp\":9007199254740990}") + "]")), Is.True); Ack(actor);
                Assert.That(await OneAction(actor, "SaveDiscardAfterReview", new object[] { Item }, Inventory(4, "[]")), Is.True); Ack(actor);
                Assert.That(Manager.CurrentInventory.instances, Is.Empty); var archives = Directory.GetDirectories(Application.persistentDataPath, Path.GetFileName(JournalDirectory) + ".*.acknowledged"); Assert.That(archives.Length, Is.EqualTo(4));
                foreach (string archive in archives) Assert.That(Directory.GetFiles(archive).Length, Is.EqualTo(2));
            }
            finally { CleanupJournal(); }
        }
        [Test] public async Task ConfirmedPurchaseSurvivesFailedInventoryGetAndActorRecreationWithGetOnlyRecovery()
        {
            Assert.That(File.Exists(JournalPath), Is.False); var actor = Actor();
            try
            {
                await ReviewOffers(actor); await SendPurchase(actor, true);
                Assert.Throws<TargetInvocationException>(() => Ack(actor));
                UnityEngine.Object.DestroyImmediate(actor.gameObject); actor = Actor();
                var task = Act(actor, "ApplySavedIntent", false); var c = await Timeout(_listener.GetContextAsync());
                Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/items?userId=" + User)); await Respond(c, Inventory(2, "[]")); Assert.That(await Timeout(task), Is.True);
                Ack(actor); Assert.That(File.Exists(JournalPath), Is.False); Assert.That(PlayerValuesManager.Instance.CurrentState.operation.items[0].instanceId, Is.EqualTo(Item));
            }
            finally { CleanupJournal(); }
        }
        [Test] public async Task LostPurchaseThenRemovedOfferRequiresExplicitConfirmationAndUsesOriginalReceiptId()
        {
            Assert.That(File.Exists(JournalPath), Is.False); var actor = Actor();
            try
            {
                await ReviewOffers(actor); string original = await SendPurchase(actor, false, true);
                Assert.That(await OneAction(actor, "ApplySavedIntent", new object[] { false }, Offers(false)), Is.False);
                var task = Act(actor, "ApplySavedIntent", true); await Respond(await Timeout(_listener.GetContextAsync()), Offers(false));
                var c = await Timeout(_listener.GetContextAsync()); string body = await new StreamReader(c.Request.InputStream).ReadToEndAsync(); Assert.That(JsonUtility.FromJson<PurchaseWire>(body).operationId, Is.EqualTo(original));
                await Respond(c, ValuesState(original, true)); c = await Timeout(_listener.GetContextAsync()); Assert.That(File.Exists(SuccessPath), Is.True);
                Assert.That(c.Request.HttpMethod, Is.EqualTo("GET")); await Respond(c, Inventory(3, "[]")); Assert.That(await Timeout(task), Is.True); Ack(actor);
                Assert.That(PlayerValuesManager.Instance.CurrentState.operation.replayed, Is.True); Assert.That(PlayerValuesManager.Instance.CurrentState.operation.items, Is.Empty);
            }
            finally { CleanupJournal(); }
        }
        [Test] public async Task OriginalInventoryMutationKeepsRevisionAfterConflictRefreshAndSavedReplay()
        {
            Assert.That(File.Exists(JournalPath), Is.False); var actor = Actor();
            try
            {
                Assert.That(await OneAction(actor, "RefreshInventory", Array.Empty<object>(), Inventory(4)), Is.True);
                var first = Act(actor, "SaveEquipAfterReview", Item, true); var c = await Timeout(_listener.GetContextAsync()); string original = await new StreamReader(c.Request.InputStream).ReadToEndAsync();
                await Respond(c, "{\"code\":\"REVISION_CONFLICT\"}", 409); Assert.That(await Timeout(first), Is.False);
                Assert.That(await OneAction(actor, "RefreshInventory", Array.Empty<object>(), Inventory(5)), Is.True);
                var replay = Act(actor, "ApplySavedIntent", false); c = await Timeout(_listener.GetContextAsync()); Assert.That(await new StreamReader(c.Request.InputStream).ReadToEndAsync(), Is.EqualTo(original));
                await Respond(c, "{\"code\":\"REVISION_CONFLICT\"}", 409); Assert.That(await Timeout(replay), Is.False);
                Assert.That(File.Exists(JournalPath), Is.True); Assert.That(File.Exists(SuccessPath), Is.False);
                Assert.That(await OneAction(actor, "RefreshInventory", Array.Empty<object>(), Inventory(5)), Is.True);
                Assert.That((bool)SampleType.GetMethod("ReconcileSavedMutationAfterReview").Invoke(actor, null), Is.True);
                Assert.That(File.ReadAllText(SuccessPath), Does.Contain("player-reviewed-current-inventory").And.Contain("\"serverConfirmed\":false"));
                Ack(actor); Assert.That(File.Exists(JournalPath), Is.False);
            }
            finally { CleanupJournal(); }
        }
        [Test] public async Task ChangedValueOnlyOfferCanConfirmAnOriginalPurchaseWithZeroItems()
        {
            Assert.That(File.Exists(JournalPath), Is.False); var actor = Actor();
            try
            {
                await ReviewOffers(actor); string original = await SendPurchase(actor, false, true);
                var task = Act(actor, "ApplySavedIntent", true);
                string offer = Offers().Replace("\"rewards\":[]", "\"rewards\":[{\"key\":\"coins\",\"amount\":1}]").Replace("\"itemRewards\":[{\"itemKey\":\"sword\",\"quantity\":1}]", "\"itemRewards\":[]");
                await Respond(await Timeout(_listener.GetContextAsync()), offer);
                var c = await Timeout(_listener.GetContextAsync()); Assert.That(JsonUtility.FromJson<PurchaseWire>(await new StreamReader(c.Request.InputStream).ReadToEndAsync()).operationId, Is.EqualTo(original));
                string state = ValuesState(original).Replace("\"items\":[{\"instanceId\":\"" + Item + "\",\"itemKey\":\"sword\"}]", "\"items\":[]");
                await Respond(c, state); c = await Timeout(_listener.GetContextAsync());
                Assert.That(c.Request.HttpMethod, Is.EqualTo("GET")); Assert.That(File.ReadAllText(SuccessPath), Does.Contain("\"items\":[]").And.Contain("\"serverConfirmed\":true"));
                await Respond(c, Inventory(1, "[]")); Assert.That(await Timeout(task), Is.True); Ack(actor);
                Assert.That(PlayerValuesManager.Instance.CurrentState.operation.items, Is.Empty);
            }
            finally { CleanupJournal(); }
        }
        [Test] public async Task SharedDecoderPreservesDistinctNulKeysAndRawNumbersInNativeGroups()
        {
            const string data = "{\"a\\u0000b\":9007199254740993,\"a\\u0000c\":1.000000000000000000001,\"a\":null}";
            string group = "{\"id\":\"" + Item + "\",\"revision\":0,\"name\":\"Group\",\"description\":\"\",\"joinMode\":\"open\",\"leaderId\":\"" + User + "\",\"memberCount\":1,\"data\":" + data + ",\"myRole\":\"leader\",\"createdAt\":\"" + Time + "\",\"updatedAt\":\"" + Time + "\"}";
            var task = GroupsManager.Instance.GetGroup(User, Item); await Respond(await Timeout(_listener.GetContextAsync()), group);
            var result = await Timeout(task); Assert.That(result.IsSuccess, Is.True); Assert.That(result.Data.data.Json, Is.EqualTo(data));
        }
        [Test] public async Task OriginalItemReferencesAndInventoryNeverAppearInRawResponseLogs()
        {
            var messages = new List<string>(); LogService.Instance.EnableEventPublishing = true;
            EventService.Instance.Subscribe<LogEventData>(EventKeys.ErrorOccurred, entry => messages.Add(entry.Message));
            string operation = "1792108800000:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
            var task = PlayerValuesManager.Instance.GetState(User); await Respond(await Timeout(_listener.GetContextAsync()), ValuesState(operation)); Assert.That((await Timeout(task)).IsSuccess, Is.True);
            Assert.That((await Reply(Manager.GetInventory(User), Inventory())).IsSuccess, Is.True);
            Assert.That(messages.Any(x => x.StartsWith("Private player response:", StringComparison.Ordinal)), Is.True);
            Assert.That(string.Join("\n", messages), Does.Not.Contain(Item).And.Not.Contain("前"));
        }
        [Test] public async Task InvalidNewSampleActionNeverCreatesADeadlockedIntent()
        {
            Assert.That(File.Exists(JournalPath), Is.False); var actor = Actor();
            try
            {
                Assert.That(await OneAction(actor, "RefreshInventory", Array.Empty<object>(), Inventory()), Is.True);
                Assert.That(await Act(actor, "SaveAdjustAfterReview", Item, "label", 1L), Is.False);
                Assert.That(await Act(actor, "SaveAdjustAfterReview", Item, "hp", 0L), Is.False);
                Assert.That(await Act(actor, "SaveEquipAfterReview", Project, true), Is.False);
                Assert.That(File.Exists(JournalPath), Is.False);
                _config.SetApiKey("other"); _config.SetApiKey(ApiKey);
                Assert.That(await Act(actor, "SaveEquipAfterReview", Item, true), Is.False); Assert.That(File.Exists(JournalPath), Is.False);
            }
            finally { CleanupJournal(); }
        }
        private static Type StoreType => Type.GetType("PM.horizOn.Cloud.Examples.Features.ItemsSampleStore, Assembly-CSharp", true);
        private static object Store() => Activator.CreateInstance(StoreType, Application.persistentDataPath, Project, User, ApiKey);
        private static object StoreCall(object store, string method, params object[] args) => StoreType.GetMethod(method).Invoke(store, args);
        [Test] public async Task ActualTwoActorsCannotOverlapHttpOrAcknowledgeANewerJournal()
        {
            Assert.That(File.Exists(JournalPath), Is.False); var first = Actor(); var second = Actor();
            try
            {
                Assert.That(await OneAction(first, "RefreshInventory", Array.Empty<object>(), Inventory(4)), Is.True);
                var write = Act(first, "SaveEquipAfterReview", Item, true); var held = await Timeout(_listener.GetContextAsync());
                var pending = _listener.GetContextAsync();
                Assert.ThrowsAsync<IOException>(async () => await Act(second, "ApplySavedIntent", false));
                Assert.That(await Task.WhenAny(pending, Task.Delay(100)), Is.Not.SameAs(pending));
                await Respond(held, Inventory(5)); Assert.That(await Timeout(write), Is.True);
                // Second actor explicitly observes the first receipt, without another mutation.
                Assert.That(await Act(second, "ApplySavedIntent", false), Is.True);
                Assert.That(await Task.WhenAny(pending, Task.Delay(100)), Is.Not.SameAs(pending));
                Ack(first); Assert.That(File.Exists(JournalPath), Is.False);
                var newer = Act(first, "SaveEquipAfterReview", Item, false); var c = await Timeout(pending);
                Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/items/equip"));
                Assert.That(await new StreamReader(c.Request.InputStream).ReadToEndAsync(), Does.Contain("\"expectedRevision\":5"));
                await Respond(c, Inventory(6)); Assert.That(await Timeout(newer), Is.True);
                var stale = Assert.Throws<TargetInvocationException>(() => Ack(second));
                Assert.That(stale.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(File.Exists(JournalPath), Is.True, "stale actor must not acknowledge a newer original/result");
                Ack(first); Assert.That(File.Exists(JournalPath), Is.False);
            }
            finally { CleanupJournal(); }
        }
        [Test] public async Task UnpublishedPurchaseReceiptRecoversBeforeAnyReplayAndUsesInventoryGetOnly()
        {
            Assert.That(File.Exists(JournalPath), Is.False); var actor = Actor();
            try
            {
                await ReviewOffers(actor); string operation = await SendPurchase(actor, false, true);
                object store = Store(), prepared = StoreCall(store, "PrepareSend");
                string hash = (string)prepared.GetType().GetProperty("Hash").GetValue(prepared);
                StoreCall(store, "SaveSuccess", hash, operation, new[] { new ItemReference { instanceId = Item, itemKey = "sword" } });
                // Exact publication gap after flushed receipt bytes and before its atomic rename.
                File.Move(SuccessPath, Path.Combine(JournalDirectory, "result.complete.writing"));
                UnityEngine.Object.DestroyImmediate(actor.gameObject); actor = Actor();
                var retry = Act(actor, "ApplySavedIntent", false); var c = await Timeout(_listener.GetContextAsync());
                Assert.That(c.Request.HttpMethod, Is.EqualTo("GET"));
                Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/items?userId=" + User));
                Assert.That(File.Exists(SuccessPath), Is.True); Assert.That(File.ReadAllText(SuccessPath), Does.Contain(operation));
                await Respond(c, Inventory(3)); Assert.That(await Timeout(retry), Is.True);
                Ack(actor); Assert.That(File.Exists(JournalPath), Is.False);
            }
            finally { CleanupJournal(); }
        }
        [Test] public async Task EqualInventoryRevisionCannotReleaseAnUncertainOriginalCas()
        {
            Assert.That(File.Exists(JournalPath), Is.False); var actor = Actor();
            try
            {
                Assert.That(await OneAction(actor, "RefreshInventory", Array.Empty<object>(), Inventory(4)), Is.True);
                var first = Act(actor, "SaveEquipAfterReview", Item, true); var c = await Timeout(_listener.GetContextAsync());
                await Respond(c, "{\"code\":\"TEST_UNCERTAIN\"}", 409); Assert.That(await Timeout(first), Is.False);
                Assert.That(await OneAction(actor, "RefreshInventory", Array.Empty<object>(), Inventory(4)), Is.True);
                Assert.That((bool)SampleType.GetMethod("ReconcileSavedMutationAfterReview").Invoke(actor, null), Is.False);
                Assert.That(File.Exists(JournalPath), Is.True); Assert.That(File.Exists(SuccessPath), Is.False);
                Assert.That(await OneAction(actor, "RefreshInventory", Array.Empty<object>(), Inventory(5)), Is.True);
                Assert.That((bool)SampleType.GetMethod("ReconcileSavedMutationAfterReview").Invoke(actor, null), Is.True);
                Ack(actor); Assert.That(File.Exists(JournalPath), Is.False);
            }
            finally { CleanupJournal(); }
        }
        [Test] public async Task PartialUnpublishedReceiptFailsClosedBeforeAnotherHttpRequest()
        {
            Assert.That(File.Exists(JournalPath), Is.False); var actor = Actor();
            try
            {
                Assert.That(await OneAction(actor, "RefreshInventory", Array.Empty<object>(), Inventory(4)), Is.True);
                var first = Act(actor, "SaveEquipAfterReview", Item, true); var c = await Timeout(_listener.GetContextAsync());
                await Respond(c, "{\"code\":\"TEST_UNCERTAIN\"}", 409); Assert.That(await Timeout(first), Is.False);
                File.WriteAllText(Path.Combine(JournalDirectory, "result.partial.writing"), "{\"intentHash\":");
                var pending = _listener.GetContextAsync();
                Assert.ThrowsAsync<IOException>(async () => await Act(actor, "ApplySavedIntent", false));
                Assert.That(await Task.WhenAny(pending, Task.Delay(100)), Is.Not.SameAs(pending));
                Assert.That(File.Exists(JournalPath), Is.True);
            }
            finally { CleanupJournal(); }
        }
        private const string ConfiguredLimits = "{\"catalog\":6000,\"inventory\":250,\"receipts\":80}";
        private static string ConfiguredItemId(int index) => "10000000-0000-4000-8000-" + index.ToString("D12", System.Globalization.CultureInfo.InvariantCulture);
        private static string ConfiguredInventory(long revision, string limits, bool selectedEquipped = false)
        {
            string rows = string.Join(",", Enumerable.Range(0, 201).Select(i => Instance(selectedEquipped && i == 200).Replace(Item, ConfiguredItemId(i))));
            return Inventory(revision, "[" + rows + "]").Replace(Limits, limits);
        }
        [Test] public async Task ConfiguredQuotasAndRetainedInventorySurviveLowering()
        {
            var incoming = _listener.GetContextAsync(); var loaded = Manager.GetInventory(User);
            await Respond(await Timeout(incoming), ConfiguredInventory(1, ConfiguredLimits)); var high = await loaded;
            Assert.That(high.IsSuccess, Is.True); Assert.That(high.Data.instances.Length, Is.EqualTo(201));
            Assert.That(high.Data.limits.catalog, Is.EqualTo(6000)); Assert.That(high.Data.limits.inventory, Is.EqualTo(250)); Assert.That(high.Data.limits.receipts, Is.EqualTo(80));
            incoming = _listener.GetContextAsync(); loaded = Manager.GetInventory(User);
            await Respond(await Timeout(incoming), ConfiguredInventory(2, Limits)); var lower = await loaded;
            Assert.That(lower.IsSuccess, Is.True); Assert.That(lower.Data.instances.Length, Is.EqualTo(201)); Assert.That(lower.Data.limits.inventory, Is.EqualTo(10));
            Assert.That(Manager.CurrentInventory.instances.Length, Is.EqualTo(201), "Lowered quota must not erase retained instances");
            incoming = _listener.GetContextAsync(); var equipped = Manager.SetEquipped(User, ConfiguredItemId(200), 2, true); var context = await Timeout(incoming);
            string body = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
            Assert.That(body, Does.Contain("\"expectedRevision\":2").And.Contain(ConfiguredItemId(200)).And.Contain("\"equipped\":true"));
            await Respond(context, ConfiguredInventory(3, Limits, true)); var changed = await equipped;
            Assert.That(changed.IsSuccess, Is.True); Assert.That(changed.Data.instances.Length, Is.EqualTo(201)); Assert.That(changed.Data.instances[200].equipped, Is.True);
        }
        [Test] public async Task ConfiguredCatalogTotalAndOriginalPageRevisionSurviveLowering()
        {
            var incoming = _listener.GetContextAsync(); var loaded = Manager.GetCatalog(User, limit: 1);
            await Respond(await Timeout(incoming), Catalog(1, next: "\"sword\"").Replace("\"total\":21", "\"total\":6000").Replace(Limits, ConfiguredLimits));
            var page = await loaded; Assert.That(page.IsSuccess, Is.True); Assert.That(page.Data.total, Is.EqualTo(6000)); Assert.That(page.Data.limits.catalog, Is.EqualTo(6000));
            incoming = _listener.GetContextAsync(); loaded = Manager.GetCatalog(User, "sword", 1, 1); var context = await Timeout(incoming);
            Assert.That(context.Request.RawUrl, Does.Contain("afterKey=sword").And.Contain("catalogRevision=1"));
            await Respond(context, Catalog(1, "[" + Definition("wand") + "]", "\"wand\"").Replace("\"total\":21", "\"total\":6000"));
            page = await loaded; Assert.That(page.IsSuccess, Is.True); Assert.That(page.Data.total, Is.EqualTo(6000)); Assert.That(page.Data.limits.catalog, Is.EqualTo(20));
            Assert.That(Manager.CurrentCatalogPage.definitions[0].key, Is.EqualTo("wand"));
        }
        [TestCase("0")][TestCase("2147483648")][TestCase("1.0")][TestCase("1e3")][TestCase("9007199254740992")]
        public async Task ConfigurableQuotasStillRequirePositiveInt32OriginalTokens(string invalid)
        {
            var incoming = _listener.GetContextAsync(); var loaded = Manager.GetInventory(User);
            await Respond(await Timeout(incoming), Inventory().Replace("\"catalog\":20", "\"catalog\":" + invalid));
            Assert.That((await loaded).ErrorCode, Is.EqualTo("INVALID_RESPONSE")); Assert.That(Manager.CurrentInventory, Is.Null);
        }
        [TestCase(65536, true)][TestCase(65537, false)]
        public async Task PhysicalTemplateSlotBoundIsSeparateFromOperatorQuotas(int total, bool accepted)
        {
            var incoming = _listener.GetContextAsync(); var loaded = Manager.GetCatalog(User);
            string reply = Catalog().Replace("\"total\":21", "\"total\":" + total)
                .Replace(Limits, "{\"catalog\":2147483647,\"inventory\":2147483647,\"receipts\":2147483647}");
            await Respond(await Timeout(incoming), reply); var result = await loaded;
            Assert.That(result.IsSuccess, Is.EqualTo(accepted));
            if (accepted) { Assert.That(result.Data.total, Is.EqualTo(65536)); Assert.That(result.Data.limits.catalog, Is.EqualTo(int.MaxValue)); }
            else { Assert.That(result.ErrorCode, Is.EqualTo("INVALID_RESPONSE")); Assert.That(Manager.CurrentCatalogPage, Is.Null); }
        }
        internal static void SetUser(string user)
        {
            typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(UserManager.Instance, new UserData { UserId = user, AccessToken = "session-" + user });
            NetworkService.Instance.SetSessionToken("session-" + user);
        }
        private static void Headers(HttpListenerContext c) { Assert.That(c.Request.Headers["X-API-Key"], Is.EqualTo(ApiKey)); Assert.That(c.Request.Headers["Authorization"], Is.EqualTo("Bearer session-" + User)); }
        private async Task<ItemsResult<T>> Reply<T>(Task<ItemsResult<T>> call, string body) where T : class { await Respond(await Timeout(_listener.GetContextAsync()), body); return await Timeout(call); }
        internal static async Task Respond(HttpListenerContext c, string body, int status = 200)
        {
            string route = c.Request.HttpMethod + " " + c.Request.Url.AbsolutePath;
            HttpCounts[route] = HttpCounts.TryGetValue(route, out var count) ? count + 1 : 1;
            c.Response.StatusCode = status; c.Response.ContentType = "application/json"; byte[] b = System.Text.Encoding.UTF8.GetBytes(body); c.Response.ContentLength64 = b.Length; await c.Response.OutputStream.WriteAsync(b, 0, b.Length); c.Response.Close();
        }
        internal static async Task<T> Timeout<T>(Task<T> task) { if (await Task.WhenAny(task, Task.Delay(8000)) != task) throw new TimeoutException("Items fixture"); return await task; }
    }
}
