using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Diagnostics;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using PM.horizOn.Cloud.Transport;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")] public sealed class MarketplaceTransportTests
    {
        const string Project = "10000000-0000-4000-8000-000000001015", Seller = "20000000-0000-4000-8000-000000001015", Buyer = "30000000-0000-4000-8000-000000001015", Other = "30000000-0000-4000-8000-000000001016";
        const string Instance = "40000000-0000-4000-8000-000000001015", Listing = "50000000-0000-4000-8000-000000001015", L2 = "50000000-0000-4000-8000-000000001016", L3 = "50000000-0000-4000-8000-000000001017", I3 = "40000000-0000-4000-8000-000000001017", Unknown = "60000000-0000-4000-8000-000000009999", Entry = "message:70000000-0000-3000-8000-000000001015";
        Process _fixture; Task<string> _stdout, _stderr; HorizonConfig _config; MonoBehaviour _sample; string _directory; Type _sampleType, _storeType;
        MarketplaceManager Manager => MarketplaceManager.Instance;
        [SetUp] public async Task Setup()
        {
            _directory = Path.Combine(Path.GetTempPath(), "horizon-market-http-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory);
            string package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NetworkService).Assembly).resolvedPath;
            var start = new ProcessStartInfo("python3", "\"" + Path.Combine(package, "Tests/Fixtures/mock_marketplace_server.py") + "\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.EnvironmentVariables["HORIZON_MARKETPLACE_FIXTURE_PORT"] = "20115"; start.EnvironmentVariables["MOCK_SERVER_READY_FILE"] = Path.Combine(_directory, "ready");
            _fixture = Process.Start(start); _stdout = _fixture.StandardOutput.ReadToEndAsync(); _stderr = _fixture.StandardError.ReadToEndAsync();
            for (int i = 0; i < 100 && !File.Exists(Path.Combine(_directory, "ready")) && !_fixture.HasExited; i++) await Task.Delay(20);
            Assert.That(File.Exists(Path.Combine(_directory, "ready")), Is.True, "Strict fixture starts");
            PlayerPrefs.DeleteKey("horizOn_UserSession"); NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            _config = ScriptableObject.CreateInstance<HorizonConfig>(); _config.SetApiKey("market-fixture-key"); _config.SetHosts(new[] { "http://127.0.0.1:20115" });
            NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost("http://127.0.0.1:20115");
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false; Player(Seller);
            _sampleType = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("PM.horizOn.Cloud.Examples.Features.MarketplaceExample")).FirstOrDefault(x => x != null);
            _storeType = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("PM.horizOn.Cloud.Examples.Features.MarketplaceSampleStore")).FirstOrDefault(x => x != null);
            Assert.That(_sampleType, Is.Not.Null, "Import actual MarketplaceExample and MarketplaceSampleStore"); Actor();
        }
        void Actor()
        {
            if (_sample) UnityEngine.Object.DestroyImmediate(_sample.gameObject);
            _sample = (MonoBehaviour)new GameObject("[MarketplaceExampleTest]").AddComponent(_sampleType);
            _sampleType.GetField("projectId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_sample, Project);
            _sampleType.GetField("journalDirectory", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_sample, _directory);
        }
        void Player(string user)
        {
            string token = "market-" + (user == Seller ? "seller" : "buyer") + "-session";
            typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(UserManager.Instance, new UserData { UserId = user, AccessToken = token }); NetworkService.Instance.SetSessionToken(token);
        }
        Task<bool> Invoke(string method, params object[] args) => (Task<bool>)_sampleType.GetMethod(method).Invoke(_sample, args);
        bool Review(string method, params object[] args) => (bool)_sampleType.GetMethod(method).Invoke(_sample, args);
        object Store(string user) => Activator.CreateInstance(_storeType, _directory, Project, user);
        object Acquire(string user) { var store = Store(user); return _storeType.GetMethod("AcquireSend").Invoke(store, null); }
        MarketplaceIntent Saved(string user, out bool confirmed)
        {
            object lease = Acquire(user);
            using ((IDisposable)lease)
            {
                object prepared = lease.GetType().GetProperty("Original").GetValue(lease);
                confirmed = (bool)prepared.GetType().GetProperty("Confirmed").GetValue(prepared);
                return (MarketplaceIntent)prepared.GetType().GetProperty("Intent").GetValue(prepared);
            }
        }
        bool HasPending(string user) => (bool)_storeType.GetProperty("HasPending").GetValue(Store(user));
        [TearDown] public async Task Teardown()
        {
            if (_fixture != null)
            {
                if (!_fixture.HasExited) { _fixture.Kill(); _fixture.WaitForExit(3000); }
                UnityEngine.Debug.Log("MARKETPLACE_FIXTURE " + await _stdout + await _stderr); _fixture.Dispose();
            }
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (new[] { "[MarketplaceExampleTest]", "[MarketplaceManager]", "[ItemsManager]", "[UserManager]", "[PlayerValuesManager]", "[InboxManager]" }.Contains(go.name)) UnityEngine.Object.DestroyImmediate(go);
            if (_config) UnityEngine.Object.DestroyImmediate(_config); if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }
        [Test] public async Task ActualTenRouteOriginalEscrowRecoveryKeepsRetainedListsAndNeverRecreatesItems()
        {
            Assert.That((await Manager.GetLimits(Other)).ErrorCode, Is.EqualTo("SESSION_FORBIDDEN"));
            Assert.That((await Manager.GetListing(Seller, "bad")).ErrorCode, Is.EqualTo("INVALID_REQUEST"));
            Assert.That((await Manager.GetRules(Seller, "hat", 1)).ErrorCode, Is.EqualTo("INVALID_REQUEST"));
            Assert.That((await Manager.QueryListings(Seller, "hat", 1, 200, limit: 51)).ErrorCode, Is.EqualTo("INVALID_REQUEST"));
            Assert.That((await Manager.GetLimits(Seller)).Data.limits.retainedListingsPerProject, Is.EqualTo(12000));
            Assert.That((await Manager.GetRules(Seller, limit: 1)).Data.nextCursor, Is.EqualTo("hat"));
            Assert.That((await Manager.GetRules(Seller, "hat", 1, 7)).Data.rules[0].itemKey, Is.EqualTo("wand"));
            var large = await Manager.GetMarketplace(Seller); Assert.That(large.IsSuccess, Is.True, large.Error);
            Assert.That(large.Data.state.openListings.Length, Is.EqualTo(2051)); Assert.That(large.Data.state.history.Length, Is.EqualTo(151));
            var lower = await Manager.GetMarketplace(Seller); Assert.That(lower.Data.state.openListings.Length, Is.EqualTo(2051)); Assert.That(lower.Data.state.limits.openListingsPerPlayer, Is.EqualTo(1));
            lower.Data.state.openListings[0].item.properties[0].key = "edited"; Assert.That(Manager.CurrentMarketplace.state.openListings[0].item.properties[0].key, Is.Not.EqualTo("edited"));
            var first = await Manager.QueryListings(Seller, "hat", 1, 200, limit: 1); Assert.That(first.Data.listings, Is.Empty); Assert.That(first.Data.nextCursor, Is.Not.Null);
            Assert.That((await Manager.QueryListings(Seller, "hat", 1, 200, first.Data.nextCursor, 1)).Data.listings[0].id, Is.EqualTo(Listing));
            var original = (await Manager.GetListing(Seller, Listing)).Data.listing.item;
            Assert.That((await ItemsManager.Instance.GetInventory(Seller)).Data.instances[0].instanceId, Is.EqualTo(Instance));
            Assert.That((await Manager.GetRules(Seller)).IsSuccess, Is.True);
            Assert.That(Review("ReviewCreate", Instance, 101L, 300L), Is.True); Assert.That(_sampleType.GetProperty("ReviewedSellerNet").GetValue(_sample), Is.EqualTo(98L));
            // Destroy the actual UI actor while the first single POST is deliberately held. Its Task finally must release the OS lease.
            var interrupted = Invoke("ConfirmReviewedTrade"); await Task.Delay(40); UnityEngine.Object.DestroyImmediate(_sample.gameObject);
            Assert.That(await interrupted, Is.False); Actor();
            var saved = Saved(Seller, out bool known); Assert.That(known, Is.False); Assert.That(saved.Kind, Is.EqualTo("create"));
            Assert.That(await Invoke("RecoverOriginalOperation"), Is.False); Saved(Seller, out known); Assert.That(known, Is.True);
            string minimal = File.ReadAllText(Directory.GetFiles(_directory, "*.json").Single()); Assert.That(minimal, Does.Not.Contain("itemReference").And.Not.Contain("balance").And.Not.Contain("recordedAt"));
            Actor(); Assert.That(await Invoke("RetryPending"), Is.True); Assert.That(HasPending(Seller), Is.False);
            Assert.That(ItemsManager.Instance.CurrentInventory.instances, Is.Empty); Assert.That(PlayerValuesManager.Instance.CurrentState.values[0].balance, Is.EqualTo(98));
            Assert.That(Manager.CurrentMarketplace.state.history[0].status, Is.EqualTo("sold"));
            var replay = await Manager.SubmitPrepared(saved); Assert.That(replay.IsSuccess, Is.True); Assert.That(replay.Data.replayed, Is.True); Assert.That(replay.Data.receipt.listingRevision, Is.EqualTo(1)); Assert.That(replay.Data.listing.revision, Is.EqualTo(2)); Assert.That(replay.Data.listing.item, Is.Null);
            Assert.That((await Manager.SubmitPrepared(MarketplaceIntent.Restore("create", "", saved.Json.Replace("\"price\":101", "\"price\":102")))).ErrorCode, Is.EqualTo("OPERATION_CONFLICT"));
            var unknown = MarketplaceIntent.Restore("create", "", saved.Json.Replace(saved.OperationId, Unknown));
            using ((IDisposable)_storeType.GetMethod("BeginNew").Invoke(Store(Seller), new object[] { unknown })) { }
            Assert.That(await Invoke("RecoverOriginalOperation"), Is.False); Assert.That(Saved(Seller, out known).OperationId, Is.EqualTo(Unknown)); Assert.That(known, Is.False);
            // Fixture-only removal of an artificial unresolved intent; sample exposes no such acknowledgement.
            File.Delete(Directory.GetFiles(_directory, "*.json").Single());
            Player(Buyer); Assert.That(Manager.CurrentMarketplace, Is.Null); Assert.That((await Manager.GetListing(Buyer, L2)).IsSuccess, Is.True); Assert.That(Review("ReviewBuy", L2), Is.True);
            Assert.That(await Invoke("ConfirmReviewedTrade"), Is.False); var buy = Saved(Buyer, out known); Assert.That(known, Is.False);
            Actor(); Assert.That(await Invoke("RetryPending"), Is.True); Assert.That(HasPending(Buyer), Is.False); Assert.That(ItemsManager.Instance.CurrentInventory.instances, Is.Empty);
            string issued = DateTimeOffset.FromUnixTimeMilliseconds(Manager.ServerNowMilliseconds).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            Assert.That((await Manager.BuyListing(Buyer, Listing, Unknown, issued, 1, 101, "1coin.gold")).ErrorCode, Is.EqualTo("LISTING_UNAVAILABLE"));
            Player(Seller); Assert.That((await Manager.GetListing(Seller, L3)).IsSuccess, Is.True); Assert.That(Review("ReviewCancel", L3), Is.True); Assert.That(await Invoke("ConfirmReviewedTrade"), Is.True);
            Assert.That((await Manager.RetryReturn(Seller, L3)).Data.delivery, Is.EqualTo("pending"));
            var delivered = await Manager.RetryReturn(Seller, L3); Assert.That(delivered.Data.delivery, Is.EqualTo("delivered")); Assert.That(delivered.Data.listing.returnEntryId, Is.EqualTo(Entry));
            Assert.That((await InboxManager.Instance.Claim(Seller, Entry)).ErrorCode, Is.EqualTo("INVENTORY_FULL"));
            var claim = await InboxManager.Instance.Claim(Seller, Entry); Assert.That(claim.IsSuccess, Is.True, claim.Error); Assert.That(claim.Data.replayed, Is.False); Assert.That(claim.Data.items[0].instanceId, Is.EqualTo(I3));
            var inventory = (await ItemsManager.Instance.GetInventory(Seller)).Data; Assert.That(inventory.instances[0].instanceId, Is.EqualTo(I3)); Assert.That(inventory.instances[0].createdAt, Is.EqualTo(original.createdAt));
            foreach (var property in original.properties) { var found = inventory.instances[0].properties.Single(x => x.key == property.key); Assert.That(found.type, Is.EqualTo(property.type)); Assert.That(found.integerValue, Is.EqualTo(property.integerValue)); Assert.That(found.stringValue, Is.EqualTo(property.stringValue)); Assert.That(found.booleanValue, Is.EqualTo(property.booleanValue)); }
            Assert.That((await ItemsManager.Instance.DiscardItem(Seller, I3, 11)).IsSuccess, Is.True);
            claim = await InboxManager.Instance.Claim(Seller, Entry); Assert.That(claim.IsSuccess, Is.True); Assert.That(claim.Data.replayed, Is.True); Assert.That(claim.Data.items[0].instanceId, Is.EqualTo(I3));
            Assert.That((await ItemsManager.Instance.GetInventory(Seller)).Data.instances, Is.Empty); Assert.That((await Manager.RetryReturn(Seller, L3)).Data.delivery, Is.EqualTo("returned"));
            Player(Buyer); Assert.That((await Manager.GetListing(Buyer, L3)).ErrorCode, Is.EqualTo("LISTING_NOT_FOUND")); Assert.That((await Manager.GetLimits(Buyer)).IsSuccess, Is.False);
            var old = Manager.GetMarketplace(Buyer); await Task.Delay(40); Manager.ClearCache(); var newer = Manager.GetMarketplace(Buyer);
            Assert.That((await old).ErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That((await newer).Data.state.revision, Is.EqualTo(9)); Assert.That((await Manager.GetMarketplace(Buyer)).ErrorCode, Is.EqualTo("STALE_RESPONSE"));
            var aba = Manager.GetLimits(Buyer); await Task.Delay(40); NetworkService.Instance.SetSessionToken("other-session"); NetworkService.Instance.SetSessionToken("market-buyer-session");
            Assert.That((await aba).ErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That(Manager.CurrentMarketplace, Is.Null);
            for (int i = 0; i < 100 && !_fixture.HasExited; i++) await Task.Delay(20);
            Assert.That(_fixture.HasExited, Is.True, "All46 exact requests completed"); Assert.That(_fixture.ExitCode, Is.EqualTo(0), await _stdout + await _stderr);
            Assert.That(await _stdout, Does.Contain("PASS Marketplace strict HTTP: 46"));
        }
    }
}
