using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    public partial class SharedObjectsTransportTests
    {
        private static Type SampleType => Type.GetType("PM.horizOn.Cloud.Examples.Features.SharedObjectsExample, Assembly-CSharp", true);
        private static Component Actor()
        {
            var value = new GameObject("[SharedObjectsExampleTest]").AddComponent(SampleType);
            SampleType.GetField("projectId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, Project); return value;
        }
        private static Task<bool> Act(Component actor, string method, params object[] arguments) => (Task<bool>)SampleType.GetMethod(method).Invoke(actor, arguments);
        private static string JournalDirectory => Path.Combine(Application.persistentDataPath, JournalPrefix);
        private async Task<bool> Action(Component actor, string method, string json, params object[] args)
        { var call = Act(actor, method, args); await Respond(await Timeout(_listener.GetContextAsync()), json); return await Timeout(call); }
        [Test] public async Task ActualSampleUnknownCreateNeverResendsAndRequiresConsistentMonotoneOwnerReview()
        {
            var actor = Actor(); Assert.That(await Action(actor, "ReadUsage", Usage()), Is.True);
            var call = Act(actor, "SaveCreateAfterUsageReview", Input(), null); var c = await Timeout(_listener.GetContextAsync());
            Assert.That(File.Exists(Path.Combine(JournalDirectory, "intent.json")), Is.True); Assert.That(File.Exists(Path.Combine(JournalDirectory, "sent.json")), Is.True);
            string original = File.ReadAllText(Path.Combine(JournalDirectory, "intent.json"));
            await Respond(c, "{\"code\":\"TEST_UNCERTAIN\"}", 503); Assert.That(await call, Is.False);
            UnityEngine.Object.DestroyImmediate(actor.gameObject); actor = Actor();
            Assert.That(await Act(actor, "ApplySavedIntent"), Is.False, "recreated actor cannot resend unknown create");
            Assert.That(File.ReadAllText(Path.Combine(JournalDirectory, "intent.json")), Is.EqualTo(original));
            // Old CAS still usable: even a stable empty snapshot cannot release the original.
            call = Act(actor, "ReconcileCreateAfterOwnerReview");
            await Respond(await Timeout(_listener.GetContextAsync()), Usage()); await Respond(await Timeout(_listener.GetContextAsync()), Query("[]")); await Respond(await Timeout(_listener.GetContextAsync()), Usage());
            Assert.That(await call, Is.False); Assert.That(File.Exists(Path.Combine(JournalDirectory, "result.json")), Is.False);
            // A completed owner snapshot now fences the original usage revision forever.
            call = Act(actor, "ReconcileCreateAfterOwnerReview");
            await Respond(await Timeout(_listener.GetContextAsync()), Usage(1, 1)); await Respond(await Timeout(_listener.GetContextAsync()), Query()); await Respond(await Timeout(_listener.GetContextAsync()), Usage(1, 1));
            Assert.That(await call, Is.True); Assert.That(File.ReadAllText(Path.Combine(JournalDirectory, "result.json")), Does.Contain("reviewed-monotone-snapshot").And.Contain("\"serverConfirmed\":false"));
            SampleType.GetMethod("AcknowledgeSavedResult").Invoke(actor, null); Assert.That(Directory.Exists(JournalDirectory), Is.False);
        }
        [Test] public async Task ActualTwoActorsHoldExclusiveOriginalThroughHttpAndConfirmedResult()
        {
            var a = Actor(); var b = Actor(); Assert.That(await Action(a, "ReadObject", Read(4294967296), Id), Is.True);
            var call = Act(a, "SaveUpdateAfterObjectReview", Input()); var c = await Timeout(_listener.GetContextAsync());
            string raw = await Body(c); Assert.That(c.Request.Headers["If-Match"], Is.EqualTo("4294967296")); Assert.That(raw, Does.Contain("前\\u0000後😀"));
            var other = Act(b, "ApplySavedIntent"); bool blocked = false; try { await other; } catch (IOException) { blocked = true; } Assert.That(blocked, Is.True, "other actor cannot own preparation/HTTP while first holds OS handle");
            await Respond(c, Write(4294967297, 8)); Assert.That(await call, Is.True);
            UnityEngine.Object.DestroyImmediate(a.gameObject); UnityEngine.Object.DestroyImmediate(b.gameObject); var recreated = Actor();
            Assert.That(await Act(recreated, "ApplySavedIntent"), Is.True, "saved result recovers with no replacement HTTP");
            Assert.That(File.ReadAllText(Path.Combine(JournalDirectory, "result.json")), Does.Contain("4294967297"));
            var extra = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(extra, Task.Delay(100)), Is.Not.SameAs(extra));
            SampleType.GetMethod("AcknowledgeSavedResult").Invoke(recreated, null);
        }
        [Test] public async Task OwnerRecoveryCompletesMultiplePagesOnlyThroughExplicitOriginalCursorActions()
        {
            var actor = Actor(); Assert.That(await Action(actor, "ReadUsage", Usage()), Is.True);
            var call = Act(actor, "SaveCreateAfterUsageReview", Input(), null); await Respond(await Timeout(_listener.GetContextAsync()), "{\"code\":\"UNKNOWN\"}", 503); Assert.That(await call, Is.False);
            string[] rows = new string[50]; for (int i = 0; i < rows.Length; i++) rows[i] = Object(id: (i + 1).ToString("x8") + "-cccc-4ccc-8ccc-cccccccccccc");
            call = Act(actor, "ReconcileCreateAfterOwnerReview");
            await Respond(await Timeout(_listener.GetContextAsync()), Usage(1, 51)); await Respond(await Timeout(_listener.GetContextAsync()), Query("[" + string.Join(",", rows) + "]", "\"explicit-page-two\"")); await Respond(await Timeout(_listener.GetContextAsync()), Usage(1, 51));
            Assert.That(await call, Is.False); Assert.That(SampleType.GetProperty("NextRecoveryCursor").GetValue(actor), Is.EqualTo("explicit-page-two"));
            Assert.That(File.Exists(Path.Combine(JournalDirectory, "result.json")), Is.False);
            Assert.That(await Act(actor, "ReviewNextCreateOwnerPage", "wrong-cursor"), Is.False);
            call = Act(actor, "ReviewNextCreateOwnerPage", "explicit-page-two");
            await Respond(await Timeout(_listener.GetContextAsync()), Usage(1, 51)); var c = await Timeout(_listener.GetContextAsync()); Assert.That(await Body(c), Does.Contain("\"cursor\":\"explicit-page-two\"")); await Respond(c, Query()); await Respond(await Timeout(_listener.GetContextAsync()), Usage(1, 51));
            Assert.That(await call, Is.True); Assert.That(File.Exists(Path.Combine(JournalDirectory, "result.json")), Is.True);
        }
        private static string RunBody => "{\"runId\":\"" + Other + "\",\"ticket\":\"fixture-run-ticket\",\"seed\":5,\"leaderboardKey\":null,\"issuedAt\":\"" + Time + "\",\"expiresAt\":\"2026-10-16T02:00:00Z\",\"expiresInSeconds\":7200}";
        [Test] public async Task GenericAndSharedDefenseStartsCannotOverlapOrReplaceAnActiveRun()
        {
            var actor = Actor(); var selected = new SharedObject { id = Id, collection = "levels", revision = 7, data = new byte[] { 0, 255, 0 } };
            var pending = ValidatedActionsManager.Instance.StartRun(); var c = await Timeout(_listener.GetContextAsync());
            Assert.That(ValidatedActionsManager.Instance.HasPendingOperation, Is.True);
            Assert.That(await Act(actor, "StartDefense", selected), Is.False); Assert.That(Directory.Exists(JournalDirectory), Is.False, "locally blocked start never poisons a new durable original");
            await Respond(c, RunBody); Assert.That(await pending, Is.Not.Null);
            Assert.That(await ValidatedActionsManager.Instance.StartRun(), Is.Null); Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo("PENDING_RUN"));
            ValidatedActionsManager.Instance.DiscardRun(); var defense = Act(actor, "StartDefense", selected); c = await Timeout(_listener.GetContextAsync());
            string body = await Body(c); Assert.That(body, Does.Contain("\"initialState\":\"AP8A\"").And.Contain(Id + ":7").And.Contain(ValidatedActionsManager.ComputeInputLogHash(selected.data)));
            selected.data[1] = 1; Assert.That(await ValidatedActionsManager.Instance.StartRun(), Is.Null); Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo("RUN_OPERATION_PENDING"));
            ValidatedActionsManager.Instance.DiscardRun();
            Assert.That(await ValidatedActionsManager.Instance.StartRun(), Is.Null, "discard invalidates reply but cannot release in-flight owner");
            await Respond(c, RunBody); Assert.That(await defense, Is.False); Assert.That(ValidatedActionsManager.Instance.CurrentRun, Is.Null); Assert.That(ValidatedActionsManager.Instance.HasPendingOperation, Is.False);
            Assert.That(File.Exists(Path.Combine(JournalDirectory, "sent.json")), Is.True, "unknown original defense retained durably");
        }
        [Test] public async Task OriginalDefenseSampleStartsAndSubmitsExactlySelectedBytesWithoutClientReward()
        {
            var actor = Actor(); var selected = new SharedObject { id = Id, collection = "levels", revision = 7, data = new byte[] { 0, 255, 0 } };
            Assert.That(await Action(actor, "StartDefense", RunBody, selected), Is.True);
            var input = new byte[] { 1, 0, 255 }; var call = Act(actor, "SubmitOriginalDefense", input); var c = await Timeout(_listener.GetContextAsync());
            Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/validated-actions/submit")); string body = await Body(c);
            Assert.That(body, Does.Contain("fixture-run-ticket").And.Contain(ValidatedActionsManager.ComputeInputLogHash(input))); input[0] = 2;
            await Respond(c, "{\"accepted\":true,\"runId\":\"" + Other + "\",\"state\":null,\"evidence\":null}");
            Assert.That(await call, Is.True); Assert.That(ValidatedActionsManager.Instance.CurrentRun, Is.Null);
            Assert.That(File.ReadAllText(Path.Combine(JournalDirectory, "result.json")), Does.Contain("validated-submit-confirmed").And.Contain(Id));
            SampleType.GetMethod("AcknowledgeSavedResult").Invoke(actor, null);
        }
        [TestCase("host")][TestCase("session")]
        public async Task ValidatedStartAbaAndCallbackReentryCannotAdoptAnotherRun(string scope)
        {
            var call = ValidatedActionsManager.Instance.StartRun(); var c = await Timeout(_listener.GetContextAsync());
            if (scope == "host") { NetworkService.Instance.SetActiveHost("http://other"); NetworkService.Instance.SetActiveHost(Host); }
            else { NetworkService.Instance.SetSessionToken("other"); NetworkService.Instance.SetSessionToken("session-" + User); }
            await Respond(c, RunBody); Assert.That(await call, Is.Null); Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That(ValidatedActionsManager.Instance.CurrentRun, Is.Null);
            Task<ValidatedRun> reentrant = null;
            HorizonApp.Events.Subscribe<ValidatedRun>(EventKeys.ValidatedRunStarted, _ => { ValidatedActionsManager.Instance.DiscardRun(); reentrant = ValidatedActionsManager.Instance.StartRun(); });
            call = ValidatedActionsManager.Instance.StartRun(); c = await Timeout(_listener.GetContextAsync()); await Respond(c, RunBody);
            Assert.That(await call, Is.Null); Assert.That(reentrant, Is.Not.Null); Assert.That(await reentrant, Is.Null); Assert.That(ValidatedActionsManager.Instance.CurrentRun, Is.Null);
        }
        [TestCase("host")][TestCase("project")][TestCase("session")][TestCase("network")]
        public async Task SynchronousConstructionLogAbaCannotSendAnOriginalCommandUnderAnotherContext(string kind)
        {
            bool changed = false; LogService.Instance.EnableEventPublishing = true;
            HorizonApp.Events.Subscribe<LogEventData>(EventKeys.ErrorOccurred, log =>
            {
                if (changed || !log.Message.StartsWith("Private player request:", StringComparison.Ordinal)) return;
                changed = true;
                if (kind == "host") { NetworkService.Instance.SetActiveHost("http://other"); NetworkService.Instance.SetActiveHost(Host); }
                if (kind == "project") { _config.SetApiKey("other"); _config.SetApiKey(ApiKey); }
                if (kind == "session") { NetworkService.Instance.SetSessionToken("other"); NetworkService.Instance.SetSessionToken("session-" + User); }
                if (kind == "network") { NetworkService.ResetInstance(); NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost(Host); NetworkService.Instance.SetSessionToken("session-" + User); }
            });
            Assert.That((await Manager.CreateObject(User, "levels", 0, Input())).ErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That(changed, Is.True);
            var incoming = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(incoming, Task.Delay(100)), Is.Not.SameAs(incoming), "zero HTTP attempts after a synchronous callback changes context");
        }
        [TestCase("binary")][TestCase("binary-response")]
        public async Task BinaryTransportsAlsoFenceConstructionCallbacksBeforeDispatch(string kind)
        {
            bool changed = false; LogService.Instance.EnableEventPublishing = true;
            HorizonApp.Events.Subscribe<LogEventData>(EventKeys.ErrorOccurred, log =>
            {
                if (changed || !log.Message.StartsWith("Configured project key added", StringComparison.Ordinal)) return;
                changed = true; _config.SetApiKey("other"); _config.SetApiKey(ApiKey);
            });
            if (kind == "binary") Assert.That((await NetworkService.Instance.PostBinaryAsync<MessageResponse>("/fixture-binary", new byte[] { 0, 255 }, true)).ErrorCode, Is.EqualTo("SESSION_CHANGED"));
            else Assert.That((await NetworkService.Instance.PostForBinaryAsync("/fixture-binary-response", new object(), true)).ErrorCode, Is.EqualTo("SESSION_CHANGED"));
            Assert.That(changed, Is.True); var incoming = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(incoming, Task.Delay(100)), Is.Not.SameAs(incoming));
        }
        [TestCase("accepted-log")][TestCase("state-event")][TestCase("submitted-event")]
        public async Task SubmitCallbackScopeChangeStopsLaterStateEvidenceAndSuccess(string callback)
        {
            var started = ValidatedActionsManager.Instance.StartRun(); await Respond(await Timeout(_listener.GetContextAsync()), RunBody); Assert.That(await started, Is.Not.Null);
            int stateEvents = 0, submittedEvents = 0;
            Action change = () => { NetworkService.Instance.SetActiveHost("http://other"); NetworkService.Instance.SetActiveHost(Host); };
            HorizonApp.Events.Subscribe<PlayerState>(EventKeys.ValidatedStateLoaded, _ => { stateEvents++; if (callback == "state-event") change(); });
            HorizonApp.Events.Subscribe<ValidatedSubmitResult>(EventKeys.ValidatedRunSubmitted, _ => { submittedEvents++; if (callback == "submitted-event") change(); });
            if (callback == "accepted-log")
            {
                LogService.Instance.EnableEventPublishing = true;
                HorizonApp.Events.Subscribe<LogEventData>(EventKeys.ErrorOccurred, entry => { if (entry.Message.StartsWith("Validated run accepted:", StringComparison.Ordinal)) change(); });
            }
            var submitted = ValidatedActionsManager.Instance.SubmitValidated(0, new byte[] { 0, 255, 0 }); var c = await Timeout(_listener.GetContextAsync());
            // Submitted callback happens after evidence dispatch, so use explicit (nonautomatic) evidence in that case.
            ValidatedActionsManager.Instance.AutoUploadEvidence = callback != "submitted-event";
            string result = "{\"accepted\":true,\"runId\":\"" + Other + "\",\"state\":{\"day\":\"2026-10-16\",\"values\":[{\"key\":\"coins\",\"balance\":13}]},\"evidence\":{\"required\":true,\"runId\":\"" + Other + "\",\"uploadBefore\":\"2026-10-17T00:00:00Z\",\"maxBytes\":1024}}";
            await Respond(c, result); Assert.That(await submitted, Is.Null); Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo("STALE_RESPONSE")); Assert.That(ValidatedActionsManager.Instance.CurrentState, Is.Null);
            Assert.That(stateEvents, Is.EqualTo(callback == "accepted-log" ? 0 : 1)); Assert.That(submittedEvents, Is.EqualTo(callback == "submitted-event" ? 1 : 0));
            var extra = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(extra, Task.Delay(100)), Is.Not.SameAs(extra), "no evidence request from the old identity");
        }
        [Test] public async Task GenericSubmitRetainsOriginalEvidenceBytesWhenCallerReusesArrayDuringHttp()
        {
            var started = ValidatedActionsManager.Instance.StartRun(); await Respond(await Timeout(_listener.GetContextAsync()), RunBody); Assert.That(await started, Is.Not.Null);
            byte[] log = { 0, 255, 0 }; var submit = ValidatedActionsManager.Instance.SubmitValidated(0, log); var c = await Timeout(_listener.GetContextAsync());
            Assert.That(await Body(c), Does.Contain(ValidatedActionsManager.ComputeInputLogHash(log))); log[1] = 1;
            await Respond(c, "{\"accepted\":true,\"runId\":\"" + Other + "\",\"state\":null,\"evidence\":{\"required\":true,\"runId\":\"" + Other + "\",\"maxBytes\":1024}}");
            Assert.That(await submit, Is.Not.Null); c = await Timeout(_listener.GetContextAsync());
            Assert.That(c.Request.HttpMethod, Is.EqualTo("PUT")); Assert.That(c.Request.RawUrl, Is.EqualTo("/api/v1/app/validated-actions/runs/" + Other + "/evidence"));
            Assert.That(await Body(c), Does.Contain("\"log\":\"AP8A\""));
            var completed = new TaskCompletionSource<bool>(); HorizonApp.Events.Subscribe<EvidenceUploadResult>(EventKeys.ValidatedEvidenceUploaded, _ => completed.TrySetResult(true));
            await Respond(c, "{\"runId\":\"" + Other + "\",\"status\":\"UPLOADED\",\"bytes\":3}"); Assert.That(await Timeout(completed.Task), Is.True);
        }
    }
}
