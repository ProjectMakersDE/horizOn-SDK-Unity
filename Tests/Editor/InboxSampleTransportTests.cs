using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Service;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    // CI imports the original sample into Assembly-CSharp. Reflection executes that shipped source,
    // rather than duplicating its policy in the package test assembly.
    [Category("Transport")]
    public class InboxSampleTransportTests
    {
        private const string Host = "http://127.0.0.1:18916";
        private const string User = "00000000-0000-4000-8000-000000001006";
        private const string Reward = "message:00000000-0000-4000-8000-000000001001";
        private const string Information = "message:00000000-0000-4000-8000-000000001002";
        private const string Claimed = "message:00000000-0000-4000-8000-000000001003";
        private HttpListener _listener;
        private HorizonConfig _config;
        private MonoBehaviour _sample;

        [SetUp] public void Setup()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            _config = ScriptableObject.CreateInstance<HorizonConfig>();
            _config.SetApiKey("fixture-inbox-sample"); _config.SetHosts(new[] { Host });
            NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost(Host);
            NetworkService.Instance.SetSessionToken("fixture-session");
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false;
            typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(UserManager.Instance, new UserData { UserId = User, AccessToken = "fixture-session" });
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("PM.horizOn.Cloud.Examples.Features.InboxExample")).FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, "Import the actual InboxExample.cs into the QA project's Assets before running these tests.");
            _sample = (MonoBehaviour)new GameObject("[InboxExampleTest]").AddComponent(type);
            type.GetField("_userId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_sample, User);
            _listener = new HttpListener(); _listener.Prefixes.Add(Host + "/"); _listener.Start();
        }
        [TearDown] public void Teardown()
        {
            _listener?.Close();
            if (_sample != null) UnityEngine.Object.DestroyImmediate(_sample.gameObject);
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.name == "[UserManager]" || go.name == "[InboxManager]" || go.name == "[PlayerValuesManager]") UnityEngine.Object.DestroyImmediate(go);
            if (_config != null) UnityEngine.Object.DestroyImmediate(_config);
            PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }
        private Task Invoke(string method, params object[] args) => (Task)_sample.GetType().GetMethod(method).Invoke(_sample, args);
        private static string Entry(string id, bool claimed = false, bool values = true) =>
            "{\"id\":\"" + id + "\",\"kind\":\"message\",\"type\":\"gift\",\"reference\":\"owned\",\"textKey\":\"gift.ready\",\"createdAt\":\"2026-10-16T00:00:00Z\",\"expiresAt\":\"2026-11-16T00:00:00Z\",\"overdue\":false,\"read\":false,\"claimed\":" + (claimed ? "true" : "false")
            + ",\"parameters\":{},\"attachment\":{\"values\":" + (values ? "[{\"key\":\"gold\",\"amount\":1}]" : "[]") + "}}";
        private static string Snapshot(bool after = false) =>
            "{\"userId\":\"" + User + "\",\"cursor\":\"" + (after ? "after" : "before") + "\",\"revision\":" + (after ? 2 : 1) + ",\"broadcastRevision\":1,\"serverTime\":\"2026-10-16T00:00:0" + (after ? 2 : 1) + "Z\",\"pollAfterSeconds\":120,\"unchanged\":false,\"entries\":["
            + Entry(Reward, after) + "," + Entry(Information, values: false) + "," + Entry(Claimed, true) + "],\"counts\":{},\"limits\":{\"targeted\":10,\"broadcasts\":10}}";
        private async Task Refresh()
        {
            var incoming = _listener.GetContextAsync(); var pending = Invoke("Refresh");
            var context = await Timeout(incoming);
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer fixture-session"));
            await Respond(context, Snapshot()); await pending;
            Assert.That(InboxManager.Instance.CurrentInbox, Is.Not.Null);
        }
        private async Task NoRequest(Task action)
        {
            var unexpected = _listener.GetContextAsync();
            Assert.That(await Task.WhenAny(action, unexpected, Task.Delay(5000)), Is.SameAs(action));
            await action;
            Assert.That(await Task.WhenAny(unexpected, Task.Delay(80)), Is.Not.SameAs(unexpected));
        }
        [Test] public async Task NoSnapshotOrMissingPersisterCannotStartANewIntent()
        {
            bool persisted = false;
            await NoRequest(Invoke("ClaimNewEntry", Reward, new Func<string, bool>(_ => persisted = true)));
            Assert.That(persisted, Is.False);
        }
        [Test] public async Task NullPersisterCannotSendAnEligibleClaim()
        { await Refresh(); await NoRequest(Invoke("ClaimNewEntry", Reward, null)); }
        [TestCase(Information)] [TestCase(Claimed)] [TestCase("missing")] [TestCase("")]
        public async Task InvalidNewSelectionNeverCreatesAPendingIntentOrHttpClaim(string selected)
        {
            await Refresh(); bool persisted = false;
            await NoRequest(Invoke("ClaimNewEntry", selected, new Func<string, bool>(_ => persisted = true)));
            Assert.That(persisted, Is.False);
        }
        [Test] public async Task DisplayCopyCannotAuthorizeAnInformationalReward()
        {
            await Refresh();
            var display = (PM.horizOn.Cloud.Objects.Network.Responses.InboxSnapshot)_sample.GetType().GetProperty("CurrentInbox").GetValue(_sample);
            display.entries.First(e => e.id == Information).attachment.values = display.entries.First(e => e.id == Reward).attachment.values;
            bool persisted = false;
            await NoRequest(Invoke("ClaimNewEntry", Information, new Func<string, bool>(_ => persisted = true)));
            Assert.That(persisted, Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public async Task FailedPersistenceSendsNoClaim(bool throws)
        {
            await Refresh(); int calls = 0;
            await NoRequest(Invoke("ClaimNewEntry", Reward, new Func<string, bool>(_ => { calls++; if (throws) throw new IOException("owned fixture write failure"); return false; })));
            Assert.That(calls, Is.EqualTo(1));
        }
        [TestCase("host")] [TestCase("project")] [TestCase("session")] [TestCase("cache")]
        public async Task InvalidationOrIdentityAbaDuringPersistenceCannotSendOldSelection(string kind)
        {
            await Refresh(); string saved = null;
            await NoRequest(Invoke("ClaimNewEntry", Reward, new Func<string, bool>(id =>
            {
                saved = id;
                switch (kind)
                {
                    case "host": NetworkService.Instance.SetActiveHost("http://127.0.0.1:18917"); NetworkService.Instance.SetActiveHost(Host); break;
                    case "project": _config.SetApiKey("other-project"); _config.SetApiKey("fixture-inbox-sample"); break;
                    case "session": NetworkService.Instance.SetSessionToken("other-session"); NetworkService.Instance.SetSessionToken("fixture-session"); break;
                    default: InboxManager.Instance.ClearCache(); break;
                }
                return true;
            })));
            Assert.That(saved, Is.EqualTo(Reward), "keep the original saved intent for explicit reconciliation");
            Assert.That(InboxManager.Instance.CurrentInbox, Is.Null);
        }
        [Test] public async Task EligibleSelectionPersistsOriginalEntryBeforeHttpAndReplaysAfterItWasClaimed()
        {
            await Refresh(); string saved = null;
            var incoming = _listener.GetContextAsync();
            var pending = Invoke("ClaimNewEntry", Reward, new Func<string, bool>(id => { saved = id; return true; }));
            await ConfirmClaim(await Timeout(incoming), saved, false); await pending;
            Assert.That(InboxManager.Instance.LastErrorCode, Is.Null);
            Assert.That(InboxManager.Instance.CurrentInbox.entries.First(e => e.id == Reward).claimed, Is.True);
            // A saved original intent must bypass the new-selection guard on retry.
            incoming = _listener.GetContextAsync(); pending = Invoke("ClaimEntry", saved);
            await ConfirmClaim(await Timeout(incoming), saved, true); await pending;
            Assert.That(InboxManager.Instance.LastErrorCode, Is.Null);
            Assert.That(PlayerValuesManager.Instance.CurrentState.values[0].balance, Is.EqualTo(1), "adopt the full snapshot, never add the replayed amount");
        }
        [Test] public async Task SavedOriginalEntryRemainsReplayableWithoutACurrentSnapshot()
        {
            var incoming = _listener.GetContextAsync(); var pending = Invoke("ClaimEntry", Reward);
            await ConfirmClaim(await Timeout(incoming), Reward, true); await pending;
            Assert.That(InboxManager.Instance.LastErrorCode, Is.Null);
        }
        private static async Task ConfirmClaim(HttpListenerContext context, string saved, bool replayed)
        {
            Assert.That(saved, Is.EqualTo(Reward)); Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/inbox/claim"));
            var body = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
            Assert.That(body, Does.Contain("\"entryId\":\"" + Reward + "\""));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer fixture-session"));
            string values = "{\"userId\":\"" + User + "\",\"definitionVersion\":1,\"revision\":1,\"day\":\"2026-10-16\",\"values\":[{\"key\":\"gold\",\"balance\":1,\"earnedToday\":1,\"dailyCap\":null}],\"operationWindow\":{\"serverTime\":\"2026-10-16T00:00:02Z\",\"oldestAcceptedAt\":\"2026-09-16T00:00:00Z\"}}";
            await Respond(context, "{\"entryId\":\"" + Reward + "\",\"replayed\":" + (replayed ? "true" : "false") + ",\"inbox\":" + Snapshot(true) + ",\"playerValues\":" + values + "}");
        }
        private static async Task<T> Timeout<T>(Task<T> task)
        { if (await Task.WhenAny(task, Task.Delay(5000)) != task) throw new TimeoutException(); return await task; }
        private static async Task Respond(HttpListenerContext context, string body)
        { context.Response.StatusCode = 200; context.Response.ContentType = "application/json"; using (var writer = new StreamWriter(context.Response.OutputStream)) await writer.WriteAsync(body); context.Response.Close(); }
    }
}
