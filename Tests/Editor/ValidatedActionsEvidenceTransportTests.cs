using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using PM.horizOn.Cloud.Transport;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    /// <summary>
    /// Validated Actions Part 3 (TASK-888): the automatic evidence upload after a submit with the
    /// raw log, the manual UploadEvidence (after a submit with a hash), local checks, evidence codes
    /// that never change the submit result, and PLAYER_BANNED on both submit paths.
    /// </summary>
    [Category("Transport")]
    public class ValidatedActionsEvidenceTransportTests
    {
        private const string Host = "http://127.0.0.1:18888";

        private const string Ticket = "hzn-rt1:2026-09:Qm9:c2Vj";

        private const string RunBody =
            "{\"runId\":\"run-888\",\"ticket\":\"" + Ticket + "\",\"seed\":7," +
            "\"leaderboardKey\":\"weekly\",\"issuedAt\":\"2026-09-29T14:00:00.120Z\"," +
            "\"expiresAt\":\"2026-09-29T16:00:00.120Z\",\"expiresInSeconds\":7200}";

        private const string AcceptedWithEvidenceBody =
            "{\"accepted\":true,\"runId\":\"run-888\",\"leaderboardKey\":\"weekly\",\"score\":18250," +
            "\"bestScore\":18250,\"isNewHighScore\":true,\"rank\":1,\"durationSeconds\":734," +
            "\"state\":null,\"evidence\":{\"required\":true,\"runId\":\"run-888\"," +
            "\"uploadBefore\":\"2026-09-30T14:12:14.120Z\",\"maxBytes\":32768}}";

        private const string AcceptedWithSmallLimitBody =
            "{\"accepted\":true,\"runId\":\"run-888\",\"leaderboardKey\":\"weekly\",\"score\":18250," +
            "\"bestScore\":18250,\"isNewHighScore\":true,\"rank\":1,\"durationSeconds\":734," +
            "\"state\":null,\"evidence\":{\"required\":true,\"runId\":\"run-888\"," +
            "\"uploadBefore\":\"2026-09-30T14:12:14.120Z\",\"maxBytes\":2}}";

        private const string UploadedBody = "{\"runId\":\"run-888\",\"status\":\"UPLOADED\",\"bytes\":4}";

        // Four bytes, so the base64 needs padding: "AAECAw==".
        private static readonly byte[] InputLog = { 0, 1, 2, 3 };
        private const string InputLogBase64 = "AAECAw==";

        private HttpListener _listener;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
            NetworkService.ResetInstance();
            LogService.ResetInstance();
            EventService.ResetInstance();

            var config = ScriptableObject.CreateInstance<HorizonConfig>();
            config.SetApiKey("project-key-888");
            config.SetHosts(new[] { Host });

            NetworkService.Instance.Initialize(config);
            NetworkService.Instance.SetActiveHost(Host);
            NetworkService.Instance.SetSessionToken("session-token-888");
            LogService.Instance.EnableUnityLogging = false;
            LogService.Instance.EnableEventPublishing = false;

            SetCurrentUser(new UserData
            {
                UserId = "user-888",
                AccessToken = "session-token-888"
            });

            _listener = new HttpListener();
            _listener.Prefixes.Add(Host + "/");
            _listener.Start();
        }

        [TearDown]
        public void TearDown()
        {
            _listener?.Close();
            foreach (var gameObject in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (gameObject.name == "[UserManager]" ||
                    gameObject.name == "[ValidatedActionsManager]" ||
                    gameObject.name == "[LeaderboardManager]")
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }

        [Test]
        public async Task SubmitWithLog_EvidenceRequired_UploadsTheLogInTheBackground()
        {
            await StartRun();

            EvidenceUploadResult uploadedEvent = null;
            Action<EvidenceUploadResult> handler = data => uploadedEvent = data;
            EventService.Instance.Subscribe(EventKeys.ValidatedEvidenceUploaded, handler);

            Task<HttpListenerContext> submitIncoming = _listener.GetContextAsync();
            Task<ValidatedSubmitResult> submitting = ValidatedActionsManager.Instance.SubmitValidated(18250, InputLog);
            await Respond(await WithTimeout(submitIncoming, TimeSpan.FromSeconds(5)), 200, AcceptedWithEvidenceBody);

            Task<HttpListenerContext> uploadIncoming = _listener.GetContextAsync();
            ValidatedSubmitResult result = await submitting;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.evidence.required, Is.True);
            Assert.That(result.evidence.runId, Is.EqualTo("run-888"));
            Assert.That(result.evidence.maxBytes, Is.EqualTo(32768));
            Assert.That(result.evidence.UploadBeforeUtc, Is.Not.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.Null);

            HttpListenerContext upload = await WithTimeout(uploadIncoming, TimeSpan.FromSeconds(5));
            Assert.That(upload.Request.HttpMethod, Is.EqualTo("PUT"));
            Assert.That(upload.Request.RawUrl, Is.EqualTo("/api/v1/app/validated-actions/runs/run-888/evidence"));
            Assert.That(upload.Request.Headers["X-API-Key"], Is.EqualTo("project-key-888"));
            Assert.That(upload.Request.Headers["Authorization"], Is.EqualTo("Bearer session-token-888"));
            string body = await ReadBody(upload);
            Assert.That(body, Does.Contain("\"userId\":\"user-888\""), body);
            Assert.That(body, Does.Contain("\"log\":\"" + InputLogBase64 + "\""), "standard base64 with padding");

            await Respond(upload, 200, UploadedBody);
            await WaitUntil(() => uploadedEvent != null);

            Assert.That(uploadedEvent.runId, Is.EqualTo("run-888"));
            Assert.That(uploadedEvent.status, Is.EqualTo("UPLOADED"));
            Assert.That(uploadedEvent.bytes, Is.EqualTo(4));
            Assert.That(ValidatedActionsManager.Instance.LastEvidenceErrorCode, Is.Null);

            EventService.Instance.Unsubscribe(EventKeys.ValidatedEvidenceUploaded, handler);
        }

        [Test]
        public async Task AutoUploadFailure_IsReportedSeparately_AndKeepsTheSubmitResult()
        {
            await StartRun();

            ValidatedEvidenceFailure failure = null;
            Action<ValidatedEvidenceFailure> handler = data => failure = data;
            EventService.Instance.Subscribe(EventKeys.ValidatedEvidenceUploadFailed, handler);

            Task<HttpListenerContext> submitIncoming = _listener.GetContextAsync();
            Task<ValidatedSubmitResult> submitting = ValidatedActionsManager.Instance.SubmitValidated(18250, InputLog);
            await Respond(await WithTimeout(submitIncoming, TimeSpan.FromSeconds(5)), 200, AcceptedWithEvidenceBody);

            Task<HttpListenerContext> uploadIncoming = _listener.GetContextAsync();
            Assert.That(await submitting, Is.Not.Null);
            await Respond(await WithTimeout(uploadIncoming, TimeSpan.FromSeconds(5)), 410,
                "{\"status\":410,\"error\":\"Gone\",\"code\":\"EVIDENCE_EXPIRED\"," +
                "\"message\":\"The upload window of this run has passed\",\"runId\":\"run-888\"}");
            await WaitUntil(() => failure != null);

            Assert.That(failure.code, Is.EqualTo(ValidatedActionsErrorCodes.EvidenceExpired));
            Assert.That(failure.runId, Is.EqualTo("run-888"));
            Assert.That(failure.httpStatus, Is.EqualTo(410));
            Assert.That(failure.retryable, Is.False);
            Assert.That(failure.automatic, Is.True);
            Assert.That(ValidatedActionsManager.Instance.LastEvidenceErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.EvidenceExpired));
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.Null, "the automatic upload never overwrites the submit outcome");

            EventService.Instance.Unsubscribe(EventKeys.ValidatedEvidenceUploadFailed, handler);
        }

        [Test]
        public async Task SubmitWithHash_DoesNotUpload_ManualUploadReportsHashMismatch()
        {
            await StartRun();

            ValidatedEvidenceFailure failure = null;
            Action<ValidatedEvidenceFailure> handler = data => failure = data;
            EventService.Instance.Subscribe(EventKeys.ValidatedEvidenceUploadFailed, handler);

            Task<HttpListenerContext> submitIncoming = _listener.GetContextAsync();
            Task<ValidatedSubmitResult> submitting = ValidatedActionsManager.Instance.SubmitValidatedWithHash(
                18250, ValidatedActionsManager.ComputeInputLogHash(InputLog));
            await Respond(await WithTimeout(submitIncoming, TimeSpan.FromSeconds(5)), 200, AcceptedWithEvidenceBody);
            ValidatedSubmitResult result = await submitting;
            Assert.That(result.evidence.required, Is.True);

            Task<HttpListenerContext> uploadIncoming = _listener.GetContextAsync();
            Task completed = await Task.WhenAny(uploadIncoming, Task.Delay(350));
            Assert.That(completed, Is.Not.SameAs(uploadIncoming), "a submit with a hash has no bytes to upload");

            Task<bool> uploading = ValidatedActionsManager.Instance.UploadEvidence(result.evidence.runId, InputLog);
            HttpListenerContext upload = await WithTimeout(uploadIncoming, TimeSpan.FromSeconds(5));
            Assert.That(upload.Request.RawUrl, Is.EqualTo("/api/v1/app/validated-actions/runs/run-888/evidence"));
            await Respond(upload, 422,
                "{\"status\":422,\"error\":\"Unprocessable Entity\",\"code\":\"EVIDENCE_HASH_MISMATCH\"," +
                "\"message\":\"The log does not match the input log hash of the run\",\"runId\":\"run-888\"}");

            Assert.That(await uploading, Is.False);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.EvidenceHashMismatch));
            Assert.That(ValidatedActionsManager.Instance.LastEvidenceErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.EvidenceHashMismatch));
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure.retryable, Is.True);
            Assert.That(failure.automatic, Is.False);
            Assert.That(ValidatedActionsManager.IsEvidenceRetryable(failure.code), Is.True);

            // Retry with the right bytes succeeds and clears both codes.
            Task<HttpListenerContext> retryIncoming = _listener.GetContextAsync();
            Task<bool> retrying = ValidatedActionsManager.Instance.UploadEvidence("run-888", InputLog);
            await Respond(await WithTimeout(retryIncoming, TimeSpan.FromSeconds(5)), 200, UploadedBody);
            Assert.That(await retrying, Is.True);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastEvidenceErrorCode, Is.Null);

            EventService.Instance.Unsubscribe(EventKeys.ValidatedEvidenceUploadFailed, handler);
        }

        [Test]
        public async Task AutoUploadOff_SendsNothing()
        {
            await StartRun();
            ValidatedActionsManager.Instance.AutoUploadEvidence = false;
            try
            {
                Task<HttpListenerContext> submitIncoming = _listener.GetContextAsync();
                Task<ValidatedSubmitResult> submitting = ValidatedActionsManager.Instance.SubmitValidated(18250, InputLog);
                await Respond(await WithTimeout(submitIncoming, TimeSpan.FromSeconds(5)), 200, AcceptedWithEvidenceBody);
                Assert.That(await submitting, Is.Not.Null);

                Task<HttpListenerContext> unexpected = _listener.GetContextAsync();
                Task completed = await Task.WhenAny(unexpected, Task.Delay(350));
                Assert.That(completed, Is.Not.SameAs(unexpected), "auto upload is off");
            }
            finally
            {
                ValidatedActionsManager.Instance.AutoUploadEvidence = true;
            }
        }

        [Test]
        public async Task AutoUpload_LogAboveMaxBytes_FailsLocally()
        {
            await StartRun();

            ValidatedEvidenceFailure failure = null;
            Action<ValidatedEvidenceFailure> handler = data => failure = data;
            EventService.Instance.Subscribe(EventKeys.ValidatedEvidenceUploadFailed, handler);

            Task<HttpListenerContext> submitIncoming = _listener.GetContextAsync();
            Task<ValidatedSubmitResult> submitting = ValidatedActionsManager.Instance.SubmitValidated(18250, InputLog);
            await Respond(await WithTimeout(submitIncoming, TimeSpan.FromSeconds(5)), 200, AcceptedWithSmallLimitBody);
            Assert.That(await submitting, Is.Not.Null);

            Task<HttpListenerContext> unexpected = _listener.GetContextAsync();
            await WaitUntil(() => failure != null);
            Assert.That(failure.code, Is.EqualTo(ValidatedActionsErrorCodes.EvidenceTooLarge));
            Assert.That(failure.httpStatus, Is.EqualTo(0));
            Assert.That(failure.retryable, Is.False);

            Task completed = await Task.WhenAny(unexpected, Task.Delay(350));
            Assert.That(completed, Is.Not.SameAs(unexpected), "a log above maxBytes is not sent");

            EventService.Instance.Unsubscribe(EventKeys.ValidatedEvidenceUploadFailed, handler);
        }

        [Test]
        public async Task UploadEvidence_LocalChecks_SendNothing()
        {
            Task<HttpListenerContext> unexpected = _listener.GetContextAsync();

            Assert.That(await ValidatedActionsManager.Instance.UploadEvidence("  ", InputLog), Is.False);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.InvalidRunId));

            Assert.That(await ValidatedActionsManager.Instance.UploadEvidence("run-888", new byte[0]), Is.False);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.EmptyInputLog));

            Assert.That(await ValidatedActionsManager.Instance.UploadEvidence("run-888", null), Is.False);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.EmptyInputLog));

            NetworkService.Instance.SetSessionToken("stale-token");
            Assert.That(await ValidatedActionsManager.Instance.UploadEvidence("run-888", InputLog), Is.False);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.SessionRequired));
            Assert.That(ValidatedActionsManager.Instance.LastEvidenceErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.SessionRequired));

            Task completed = await Task.WhenAny(unexpected, Task.Delay(350));
            Assert.That(completed, Is.Not.SameAs(unexpected), "UploadEvidence sent a request although a local check failed");
        }

        [Test]
        public async Task ValidatedSubmit_PlayerBanned_KeepsTheRun_AndPublishesTheRejection()
        {
            await StartRun();

            ValidatedRunRejection rejection = null;
            Action<ValidatedRunRejection> handler = data => rejection = data;
            EventService.Instance.Subscribe(EventKeys.ValidatedRunRejected, handler);

            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedSubmitResult> submitting = ValidatedActionsManager.Instance.SubmitValidated(18250, InputLog);
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 403,
                "{\"status\":403,\"error\":\"Forbidden\",\"code\":\"PLAYER_BANNED\"," +
                "\"message\":\"The player is banned from this leaderboard\",\"runId\":\"run-888\"}");

            Assert.That(await submitting, Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.PlayerBanned));
            Assert.That(ValidatedActionsManager.Instance.HasActiveRun, Is.True, "the ban is checked before the ticket is used");
            Assert.That(rejection, Is.Not.Null);
            Assert.That(rejection.code, Is.EqualTo(ValidatedActionsErrorCodes.PlayerBanned));
            Assert.That(rejection.httpStatus, Is.EqualTo(403));
            Assert.That(rejection.runCleared, Is.False);

            EventService.Instance.Unsubscribe(EventKeys.ValidatedRunRejected, handler);
        }

        [Test]
        public async Task SubmitScore_PlayerBanned_ExposesTheCode()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<bool> submitting = LeaderboardManager.Instance.SubmitScore(4242, boardKey: "weekly");
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 403,
                "{\"status\":403,\"error\":\"Forbidden\",\"code\":\"PLAYER_BANNED\"," +
                "\"message\":\"The player is banned from this leaderboard\"}");

            Assert.That(await submitting, Is.False);
            Assert.That(LeaderboardManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.PlayerBanned));
        }

        [Test]
        public void EvidenceContract_RetryRules_EndpointAndRunLifecycle()
        {
            Assert.That(ValidatedActionsTransportContract.IsEvidenceRetryable(ValidatedActionsErrorCodes.EvidenceHashMismatch), Is.True);
            Assert.That(ValidatedActionsTransportContract.IsEvidenceRetryable(ValidatedActionsErrorCodes.NetworkError), Is.True);
            foreach (string code in new[]
                     {
                         ValidatedActionsErrorCodes.EvidenceInvalidEncoding,
                         ValidatedActionsErrorCodes.EvidenceNotRequested,
                         ValidatedActionsErrorCodes.EvidenceAlreadyUploaded,
                         ValidatedActionsErrorCodes.EvidenceExpired,
                         ValidatedActionsErrorCodes.EvidenceTooLarge,
                         ValidatedActionsErrorCodes.SessionRequired,
                         null
                     })
            {
                Assert.That(ValidatedActionsTransportContract.IsEvidenceRetryable(code), Is.False, code ?? "null");
            }

            Assert.That(ValidatedActionsTransportContract.EvidenceEndpoint("run 1/x"),
                Is.EqualTo("/api/v1/app/validated-actions/runs/run%201%2Fx/evidence"));
            Assert.That(ValidatedActionsTransportContract.EndsRun(403, ValidatedActionsErrorCodes.PlayerBanned), Is.False);
            Assert.That(ValidatedActionsTransportContract.IsRejection(403), Is.True);

            var evidence = new EvidenceRequest { required = true, runId = "run-888", maxBytes = 32768 };
            Assert.That(ValidatedActionsTransportContract.ShouldAutoUploadEvidence(true, evidence, InputLog), Is.True);
            Assert.That(ValidatedActionsTransportContract.ShouldAutoUploadEvidence(false, evidence, InputLog), Is.False);
            Assert.That(ValidatedActionsTransportContract.ShouldAutoUploadEvidence(true, evidence, null), Is.False);
            Assert.That(ValidatedActionsTransportContract.ShouldAutoUploadEvidence(true, new EvidenceRequest(), InputLog), Is.False);
            Assert.That(ValidatedActionsTransportContract.EvidenceRunId(
                new ValidatedSubmitResult { runId = "run-888", evidence = new EvidenceRequest { required = true } }), Is.EqualTo("run-888"));
        }

        private async Task StartRun()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedRun> starting = ValidatedActionsManager.Instance.StartRun("weekly");
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 200, RunBody);
            Assert.That(await starting, Is.Not.Null);
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("The evidence upload did not finish in time.");
                }
                await Task.Delay(20);
            }
        }

        private static async Task<string> ReadBody(HttpListenerContext context)
        {
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                return await reader.ReadToEndAsync();
            }
        }

        private static async Task Respond(HttpListenerContext context, int status, string json)
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            using (var writer = new StreamWriter(context.Response.OutputStream))
            {
                await writer.WriteAsync(json);
            }
            context.Response.Close();
        }

        private static void SetCurrentUser(UserData user)
        {
            FieldInfo field = typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(UserManager.Instance, user);
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(timeout));
            if (completed != task)
            {
                throw new TimeoutException("The local validated actions evidence contract server did not receive a request in time.");
            }
            return await task;
        }
    }
}
