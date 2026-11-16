using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
    [Category("Transport")] public sealed class ItemGiftsTransportTests
    {
        const string Project = "10000000-0000-4000-8000-000000001140", Sender = "20000000-0000-4000-8000-000000001140", Recipient = "30000000-0000-4000-8000-000000001140", Other = "30000000-0000-4000-8000-000000001149";
        const string Instance = "40000000-0000-4000-8000-000000001140", I2 = "40000000-0000-4000-8000-000000001141", Gift = "50000000-0000-4000-8000-000000001140", Unknown = "60000000-0000-4000-8000-000000009999";
        const string Offer2 = "message:70000000-0000-4000-8000-000000001142", Return = "message:70000000-0000-4000-8000-000000001141";
        Process _fixture; Task<string> _stdout, _stderr; HorizonConfig _config; MonoBehaviour _sample; string _directory; Type _sampleType, _storeType;
        ItemGiftsManager Manager => ItemGiftsManager.Instance;
        [SetUp] public async Task Setup()
        {
            _directory = Path.Combine(Path.GetTempPath(), "horizon-gift-http-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory);
            string package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NetworkService).Assembly).resolvedPath;
            var start = new ProcessStartInfo("python3", "\"" + Path.Combine(package, "Tests/Fixtures/mock_item_gifts_server.py") + "\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.EnvironmentVariables["HORIZON_ITEM_GIFTS_FIXTURE_PORT"] = "20140"; start.EnvironmentVariables["MOCK_SERVER_READY_FILE"] = Path.Combine(_directory, "ready");
            _fixture = Process.Start(start); _stdout = _fixture.StandardOutput.ReadToEndAsync(); _stderr = _fixture.StandardError.ReadToEndAsync();
            for (int i = 0; i < 100 && !File.Exists(Path.Combine(_directory, "ready")) && !_fixture.HasExited; i++) await Task.Delay(20);
            Assert.That(File.Exists(Path.Combine(_directory, "ready")), Is.True, "Strict fixture starts");
            PlayerPrefs.DeleteKey("horizOn_UserSession"); NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            _config = ScriptableObject.CreateInstance<HorizonConfig>(); _config.SetApiKey("gift-fixture-key"); _config.SetHosts(new[] { "http://127.0.0.1:20140" });
            NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost("http://127.0.0.1:20140");
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false; Player(Sender);
            _sampleType = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("PM.horizOn.Cloud.Examples.Features.ItemGiftsExample")).FirstOrDefault(x => x != null);
            _storeType = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("PM.horizOn.Cloud.Examples.Features.ItemGiftsSampleStore")).FirstOrDefault(x => x != null);
            Assert.That(_sampleType, Is.Not.Null, "Import actual ItemGiftsExample and ItemGiftsSampleStore"); Actor();
        }
        void Actor()
        {
            if (_sample) UnityEngine.Object.DestroyImmediate(_sample.gameObject);
            _sample = (MonoBehaviour)new GameObject("[ItemGiftsExampleTest]").AddComponent(_sampleType);
            _sampleType.GetField("projectId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_sample, Project);
            _sampleType.GetField("journalDirectory", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_sample, _directory);
        }
        void Player(string user)
        {
            string token = "gift-" + (user == Sender ? "sender" : "recipient") + "-session";
            typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(UserManager.Instance, new UserData { UserId = user, AccessToken = token }); NetworkService.Instance.SetSessionToken(token);
        }
        Task<bool> Invoke(string method, params object[] args) => (Task<bool>)_sampleType.GetMethod(method).Invoke(_sample, args);
        Task<T> Call<T>(string method, params object[] args) => (Task<T>)_sampleType.GetMethod(method).Invoke(_sample, args);
        object Store(string user) => Activator.CreateInstance(_storeType, _directory, Project, user);
        ItemGiftIntent Saved(string user, out bool confirmed)
        {
            object lease = _storeType.GetMethod("AcquireSend").Invoke(Store(user), null);
            using ((IDisposable)lease)
            {
                object prepared = lease.GetType().GetProperty("Original").GetValue(lease);
                confirmed = (bool)prepared.GetType().GetProperty("Confirmed").GetValue(prepared);
                return (ItemGiftIntent)prepared.GetType().GetProperty("Intent").GetValue(prepared);
            }
        }
        bool HasPending(string user) => (bool)_storeType.GetProperty("HasPending").GetValue(Store(user));
        [TearDown] public async Task Teardown()
        {
            if (_fixture != null)
            {
                if (!_fixture.HasExited) { _fixture.Kill(); _fixture.WaitForExit(3000); }
                UnityEngine.Debug.Log("ITEM_GIFTS_FIXTURE " + await _stdout + await _stderr); _fixture.Dispose();
            }
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (new[] { "[ItemGiftsExampleTest]", "[ItemGiftsManager]", "[ItemsManager]", "[UserManager]", "[PlayerValuesManager]", "[InboxManager]" }.Contains(go.name)) UnityEngine.Object.DestroyImmediate(go);
            if (_config) UnityEngine.Object.DestroyImmediate(_config); if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }
        [Test] public async Task SendRecoverDeclineAndInboxClaims_KeepOriginalIntentAndNeverCreateItems()
        {
            Assert.That((await Manager.GetConfig(Recipient)).ErrorCode, Is.EqualTo("SESSION_FORBIDDEN"), "another player's session is refused locally");
            Assert.That((await Manager.GetGift(Sender, "bad")).ErrorCode, Is.EqualTo("INVALID_REQUEST"));
            Assert.That((await Manager.Decline(Sender, Gift, -1)).ErrorCode, Is.EqualTo("INVALID_REQUEST"));
            var quickstart = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("PM.horizOn.Cloud.Examples.Features.ItemGiftsQuickstart")).First(x => x != null);
            Assert.That(await (Task<bool>)quickstart.GetMethod("ReadRulesAndInventory").Invoke(null, new object[] { _sample }), Is.True);
            Assert.That(Manager.CurrentConfig.config.fee, Is.EqualTo(5));
            // Destroy the screen while the single POST is held. Its Task finally must release the OS lease and keep the original.
            var interrupted = (Task<bool>)quickstart.GetMethod("SendReviewedGift").Invoke(null, new object[] { _sample, Recipient, Instance });
            Assert.That(_sampleType.GetProperty("ReviewedFee").GetValue(_sample), Is.EqualTo(5L));
            await Task.Delay(40); UnityEngine.Object.DestroyImmediate(_sample.gameObject);
            Assert.That(await interrupted, Is.False); Actor();
            var saved = Saved(Sender, out bool known); Assert.That(known, Is.False); Assert.That(saved.RecipientId, Is.EqualTo(Recipient)); Assert.That(saved.ExpectedInventoryRevision, Is.EqualTo(9));
            Assert.That(await Invoke("RecoverOriginalOperation"), Is.False, "a failed inventory refresh keeps the known original"); Saved(Sender, out known); Assert.That(known, Is.True);
            string minimal = File.ReadAllText(Directory.GetFiles(_directory, "*.json").Single()); Assert.That(minimal, Does.Not.Contain("itemReference").And.Not.Contain("balance").And.Not.Contain("recordedAt"));
            Actor(); Assert.That(await Invoke("RetryPending"), Is.True); Assert.That(HasPending(Sender), Is.False);
            Assert.That(ItemsManager.Instance.CurrentInventory.instances, Is.Empty); Assert.That(PlayerValuesManager.Instance.CurrentState.values[0].balance, Is.EqualTo(95));
            Assert.That(Manager.CurrentState.state.sent[0].id, Is.EqualTo(Gift)); Assert.That(Manager.CurrentState.state.limits.openGiftsPerPlayer, Is.EqualTo(1), "retained sent list survives a lowered quota");
            var replay = await Manager.SubmitPrepared(saved); Assert.That(replay.IsSuccess, Is.True, replay.Error); Assert.That(replay.Data.replayed, Is.True); Assert.That(replay.Data.receipt.giftId, Is.EqualTo(Gift));
            Assert.That((await Manager.SubmitPrepared(ItemGiftIntent.Restore(saved.Json.Replace(Recipient, Other)))).ErrorCode, Is.EqualTo("OPERATION_CONFLICT"));
            Assert.That((await Manager.SendGift(Sender, Unknown, saved.IssuedAt, Recipient, Instance, 9, 7)).ErrorCode, Is.EqualTo("RECIPIENT_NOT_FOUND"));

            Player(Recipient); Assert.That(Manager.CurrentState, Is.Null, "identity switch clears the sender's gift state");
            var inbox = await InboxManager.Instance.GetInbox(Recipient); Assert.That(inbox.IsSuccess, Is.True, inbox.Error);
            Assert.That(inbox.Data.entries[0].CanClaim, Is.True); Assert.That(inbox.Data.entries[0].attachment.itemGift.direction, Is.EqualTo("offer"));
            Assert.That(inbox.Data.entries[0].TryGetSource(out var source), Is.True); Assert.That(source.Kind, Is.EqualTo(InboxSourceKind.ItemGiftOffer)); Assert.That(source.UserId, Is.EqualTo(Sender));
            Assert.That((await Call<ItemGiftsResult<ItemGiftStateView>>("ReadGifts")).Data.state.received.Length, Is.EqualTo(2));
            Assert.That(await Invoke("DeclineGift", Gift), Is.True);
            var again = await Manager.Decline(Recipient, Gift, 2); Assert.That(again.Data.replayed, Is.True); Assert.That(again.Data.returnDelivery, Is.EqualTo("delivered"));
            var claimed = await Call<InboxClaimResult>("ClaimGiftEntry", Offer2); Assert.That(claimed.IsSuccess, Is.True, claimed.Error); Assert.That(claimed.Data.items[0].instanceId, Is.EqualTo(I2));
            var point = await Manager.GetGift(Recipient, Gift); Assert.That(point.Data.gift.status, Is.EqualTo("return_delivered")); Assert.That(point.Data.gift.returnEntryId, Is.EqualTo(Return));

            Player(Sender); Actor();
            var back = await Call<InboxClaimResult>("ClaimGiftEntry", Return); Assert.That(back.IsSuccess, Is.True, back.Error); Assert.That(back.Data.items[0].instanceId, Is.EqualTo(Instance));
            Assert.That((await Manager.GetGift(Sender, Gift)).Data.gift.status, Is.EqualTo("returned"));
            Assert.That((await Manager.GetOperation(Sender, Unknown)).ErrorCode, Is.EqualTo("OPERATION_NOT_FOUND"));
            var stale = Manager.GetState(Sender); await Task.Delay(40); NetworkService.Instance.SetSessionToken("other-session"); NetworkService.Instance.SetSessionToken("gift-sender-session");
            Assert.That((await stale).ErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That(Manager.CurrentState, Is.Null);
            for (int i = 0; i < 100 && !_fixture.HasExited; i++) await Task.Delay(20);
            Assert.That(_fixture.HasExited, Is.True, "All exact requests completed"); Assert.That(_fixture.ExitCode, Is.EqualTo(0), await _stdout + await _stderr);
            Assert.That(await _stdout, Does.Contain("PASS Item gifts strict HTTP: 21"));
        }
    }
}
