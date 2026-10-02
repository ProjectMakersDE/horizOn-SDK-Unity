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
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using PM.horizOn.Cloud.Transport;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    /// <summary>
    /// Validated Actions Part 1 (TASK-883): both endpoints need the player's Bearer session,
    /// the submit sends the ticket of the current run with the SHA-256 of the input log, the run
    /// is dropped exactly when the ticket is used up, and server codes reach LastErrorCode.
    /// </summary>
    [Category("Transport")]
    public class ValidatedActionsTransportTests
    {
        private const string Host = "http://127.0.0.1:18883";

        private const string Ticket = "hzn-rt1:2026-09:Qm9:c2Vj";

        private const string RunBody =
            "{\"runId\":\"run-883\",\"ticket\":\"" + Ticket + "\",\"seed\":1834201177," +
            "\"leaderboardKey\":\"weekly\",\"issuedAt\":\"2026-09-29T14:00:00.120Z\"," +
            "\"expiresAt\":\"2026-09-29T16:00:00.120Z\",\"expiresInSeconds\":7200}";

        private const string AcceptedBody =
            "{\"accepted\":true,\"runId\":\"run-883\",\"leaderboardKey\":\"weekly\",\"score\":18250," +
            "\"bestScore\":21000,\"isNewHighScore\":false,\"rank\":17,\"durationSeconds\":734," +
            "\"state\":null,\"evidence\":null}";

        // SHA-256 of the ASCII bytes "abc".
        private const string AbcHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

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
            config.SetApiKey("project-key-883");
            config.SetHosts(new[] { Host });

            NetworkService.Instance.Initialize(config);
            NetworkService.Instance.SetActiveHost(Host);
            NetworkService.Instance.SetSessionToken("session-token-883");
            LogService.Instance.EnableUnityLogging = false;
            LogService.Instance.EnableEventPublishing = false;

            SetCurrentUser(new UserData
            {
                UserId = "user-883",
                AccessToken = "session-token-883"
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
        public void ComputeInputLogHash_IsLowerCaseSha256()
        {
            Assert.That(ValidatedActionsManager.ComputeInputLogHash(Encoding.ASCII.GetBytes("abc")), Is.EqualTo(AbcHash));
            Assert.That(ValidatedActionsManager.ComputeInputLogHash(null),
                Is.EqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"),
                "a null log is hashed like an empty log");
            Assert.That(ValidatedActionsTransportContract.IsValidInputLogHash(AbcHash.ToUpperInvariant()), Is.True);
            Assert.That(ValidatedActionsTransportContract.IsValidInputLogHash(AbcHash.Substring(1)), Is.False);
            Assert.That(ValidatedActionsTransportContract.IsValidInputLogHash(AbcHash.Substring(1) + "g"), Is.False);
        }

        [Test]
        public void EndsRun_MatchesTheSingleUseTicketRules()
        {
            Assert.That(ValidatedActionsTransportContract.EndsRun(200, null), Is.True);
            Assert.That(ValidatedActionsTransportContract.EndsRun(422, ValidatedActionsErrorCodes.DurationTooShort), Is.True);
            Assert.That(ValidatedActionsTransportContract.EndsRun(422, ValidatedActionsErrorCodes.TicketConsumed), Is.True);
            Assert.That(ValidatedActionsTransportContract.EndsRun(422, ValidatedActionsErrorCodes.LeaderboardMismatch), Is.False);
            Assert.That(ValidatedActionsTransportContract.EndsRun(403, ValidatedActionsErrorCodes.ScoreLimitReached), Is.True);
            Assert.That(ValidatedActionsTransportContract.EndsRun(403, ValidatedActionsErrorCodes.SessionForbidden), Is.False);
            Assert.That(ValidatedActionsTransportContract.EndsRun(0, null), Is.False);
            Assert.That(ValidatedActionsTransportContract.EndsRun(401, ValidatedActionsErrorCodes.SessionRequired), Is.False);
            Assert.That(ValidatedActionsTransportContract.EndsRun(404, ValidatedActionsErrorCodes.LeaderboardNotFound), Is.False);
            Assert.That(ValidatedActionsTransportContract.EndsRun(429, ValidatedActionsErrorCodes.RunRateLimited), Is.False);
            Assert.That(ValidatedActionsTransportContract.EndsRun(503, ValidatedActionsErrorCodes.ValidatedActionsUnavailable), Is.False);

            Assert.That(ValidatedActionsErrorCodes.Resolve(404, null), Is.EqualTo(ValidatedActionsErrorCodes.NotSupported));
            Assert.That(ValidatedActionsErrorCodes.Resolve(404, "PLAYER_NOT_FOUND"), Is.EqualTo("PLAYER_NOT_FOUND"));
            Assert.That(ValidatedActionsErrorCodes.Resolve(404, "NOT_FOUND"), Is.EqualTo(ValidatedActionsErrorCodes.NotSupported));
            Assert.That(ValidatedActionsErrorCodes.Resolve(0, null), Is.EqualTo(ValidatedActionsErrorCodes.NetworkError));
            Assert.That(NetworkService.IsNonRetryableRateLimitCode(ValidatedActionsErrorCodes.RunRateLimited), Is.True);
            Assert.That(NetworkService.IsNonRetryableRateLimitCode(ValidatedActionsErrorCodes.RunCapacityReached), Is.True);
            Assert.That(NetworkService.IsNonRetryableRateLimitCode(null), Is.False);
        }

        [Test]
        public async Task StartAndSubmit_SendSessionBoundRequests_AndClearTheRunOnAccept()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedRun> starting = ValidatedActionsManager.Instance.StartRun("weekly");

            HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            string body = await ReadBody(context);
            Assert.That(context.Request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/validated-actions/runs"));
            Assert.That(context.Request.Headers["X-API-Key"], Is.EqualTo("project-key-883"));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer session-token-883"));
            Assert.That(body, Does.Contain("\"userId\":\"user-883\""), body);
            Assert.That(body, Does.Contain("\"leaderboardKey\":\"weekly\""), body);
            Assert.That(body, Does.Not.Contain("context"), "without a context the body equals the one of older SDKs");

            await Respond(context, 200, RunBody);
            ValidatedRun run = await starting;

            Assert.That(run, Is.Not.Null);
            Assert.That(run.seed, Is.EqualTo(1834201177));
            Assert.That(run.expiresInSeconds, Is.EqualTo(7200));
            Assert.That(ValidatedActionsManager.Instance.CurrentRun, Is.SameAs(run));
            Assert.That(ValidatedActionsManager.Instance.HasActiveRun, Is.True);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.Null);

            incoming = _listener.GetContextAsync();
            Task<ValidatedSubmitResult> submitting = ValidatedActionsManager.Instance.SubmitValidated(
                18250, Encoding.ASCII.GetBytes("abc"), stage: "wave_10",
                earned: new[] { new EarnedValue("gold", 250), new EarnedValue("chest.gold", -1) });

            context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            body = await ReadBody(context);
            Assert.That(context.Request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/validated-actions/submit"));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer session-token-883"));
            Assert.That(body, Does.Contain("\"ticket\":\"" + Ticket + "\""), body);
            Assert.That(body, Does.Contain("\"inputLogHash\":\"" + AbcHash + "\""), body);
            Assert.That(body, Does.Contain("\"score\":18250"), body);
            Assert.That(body, Does.Contain("\"stage\":\"wave_10\""), body);
            Assert.That(body, Does.Contain("\"earned\":[{\"key\":\"gold\",\"amount\":250},{\"key\":\"chest.gold\",\"amount\":-1}]"), body);
            Assert.That(body, Does.Not.Contain("leaderboardKey"), "without a key the ticket's board is used");

            await Respond(context, 200, AcceptedBody);
            ValidatedSubmitResult result = await submitting;

            Assert.That(result, Is.Not.Null);
            Assert.That(result.accepted, Is.True);
            Assert.That(result.rank, Is.EqualTo(17));
            Assert.That(result.bestScore, Is.EqualTo(21000));
            Assert.That(result.durationSeconds, Is.EqualTo(734));
            Assert.That(result.state, Is.Not.Null, "JSON null state must read as an empty state");
            Assert.That(result.state.IsEmpty, Is.True);
            Assert.That(result.evidence, Is.Not.Null);
            Assert.That(result.evidence.required, Is.False);
            Assert.That(result.sus, Is.False, "an absent sus field reads as false");
            Assert.That(ValidatedActionsManager.Instance.CurrentRun, Is.Null, "a ticket is single use");
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.Null);
        }

        [Test]
        public async Task Submit_RuleRejection_ExposesCode_PublishesEvent_AndDropsTheRun()
        {
            await StartRunWith(RunBody);

            ValidatedRunRejection rejection = null;
            Action<ValidatedRunRejection> handler = data => rejection = data;
            EventService.Instance.Subscribe(EventKeys.ValidatedRunRejected, handler);

            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedSubmitResult> submitting = ValidatedActionsManager.Instance.SubmitValidatedWithHash(99000, AbcHash.ToUpperInvariant());

            HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            string body = await ReadBody(context);
            Assert.That(body, Does.Contain("\"inputLogHash\":\"" + AbcHash + "\""), "the hash is sent in lower case");

            await Respond(context, 422,
                "{\"status\":422,\"error\":\"Unprocessable Entity\",\"code\":\"DURATION_TOO_SHORT\"," +
                "\"message\":\"The run was shorter than allowed\",\"path\":\"/api/v1/app/validated-actions/submit\",\"runId\":\"run-883\"}");

            Assert.That(await submitting, Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.DurationTooShort));
            Assert.That(ValidatedActionsManager.Instance.CurrentRun, Is.Null);
            Assert.That(rejection, Is.Not.Null);
            Assert.That(rejection.code, Is.EqualTo(ValidatedActionsErrorCodes.DurationTooShort));
            Assert.That(rejection.runId, Is.EqualTo("run-883"));
            Assert.That(rejection.runCleared, Is.True);

            EventService.Instance.Unsubscribe(EventKeys.ValidatedRunRejected, handler);
        }

        [Test]
        public async Task Submit_LeaderboardNotFound_KeepsTheRunForARetry()
        {
            await StartRunWith(RunBody);

            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedSubmitResult> submitting = ValidatedActionsManager.Instance.SubmitValidatedWithHash(10, AbcHash);
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 404,
                "{\"status\":404,\"code\":\"LEADERBOARD_NOT_FOUND\",\"message\":\"Leaderboard not found\"}");

            Assert.That(await submitting, Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.LeaderboardNotFound));
            Assert.That(ValidatedActionsManager.Instance.HasActiveRun, Is.True, "a 404 does not use up the ticket");
        }

        [Test]
        public async Task StartRun_RunRateLimited_IsNotRetried()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedRun> starting = ValidatedActionsManager.Instance.StartRun();

            HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            string body = await ReadBody(context);
            Assert.That(body, Does.Not.Contain("leaderboardKey"), "an unbound run leaves the key out");

            Task<HttpListenerContext> retry = _listener.GetContextAsync();
            context.Response.AddHeader("Retry-After", "1");
            await Respond(context, 429,
                "{\"status\":429,\"code\":\"RUN_RATE_LIMITED\",\"message\":\"Run limit reached\"}");

            // A retry would keep StartRun waiting for a second answer: StartRun must finish first.
            Task first = await Task.WhenAny(starting, retry, Task.Delay(5000));
            Assert.That(first, Is.SameAs(starting), "RUN_RATE_LIMITED must be reported without a retry");
            Assert.That(await starting, Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.RunRateLimited));

            Task completed = await Task.WhenAny(retry, Task.Delay(1500));
            Assert.That(completed, Is.Not.SameAs(retry), "RUN_RATE_LIMITED must not be retried automatically");
        }

        [Test]
        public async Task StartRun_NotFoundWithoutCode_ReportsNotSupported()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedRun> starting = ValidatedActionsManager.Instance.StartRun("weekly");
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 404, "");

            Assert.That(await starting, Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.NotSupported));
        }

        [Test]
        public async Task Calls_WithoutSessionRunOrValidHash_FailLocallyWithoutRequest()
        {
            Task<HttpListenerContext> unexpectedRequest = _listener.GetContextAsync();

            Assert.That(await ValidatedActionsManager.Instance.SubmitValidated(1, new byte[] { 1, 2, 3 }), Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.NoActiveRun));

            SetCurrentRun(new ValidatedRun { runId = "run-local", ticket = Ticket });
            Assert.That(await ValidatedActionsManager.Instance.SubmitValidatedWithHash(1, "not-a-hash"), Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.InvalidInputLogHash));
            Assert.That(ValidatedActionsManager.Instance.HasActiveRun, Is.True, "a local failure keeps the run");

            SetCurrentUser(new UserData());
            Assert.That(ValidatedActionsManager.Instance.CurrentRun, Is.Null, "sign-out drops the run");
            Assert.That(await ValidatedActionsManager.Instance.StartRun("weekly"), Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.SessionRequired));
            Assert.That(await ValidatedActionsManager.Instance.SubmitValidatedWithHash(1, AbcHash), Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.SessionRequired));

            Task completed = await Task.WhenAny(unexpectedRequest, Task.Delay(350));
            Assert.That(completed, Is.Not.SameAs(unexpectedRequest),
                "A validated actions call sent a request although a local check failed");
        }

        [Test]
        public async Task SubmitScore_ValidatedSubmitRequired_IsExposedOnLeaderboardManager()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<bool> submitting = LeaderboardManager.Instance.SubmitScore(500, boardKey: "weekly");
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 403,
                "{\"status\":403,\"error\":\"Forbidden\",\"code\":\"VALIDATED_SUBMIT_REQUIRED\"," +
                "\"message\":\"This leaderboard only accepts validated runs\"}");

            Assert.That(await submitting, Is.False);
            Assert.That(LeaderboardManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.ValidatedSubmitRequired));
        }

        [Test]
        public void BoardList_ReadsValidatedOnly()
        {
            var list = JsonUtility.FromJson<LeaderboardListResponseV2>(
                "{\"boards\":[{\"id\":\"b1\",\"key\":\"weekly\",\"validatedOnly\":true}],\"totalElements\":1}");
            Assert.That(list.boards[0].validatedOnly, Is.True);
        }

        [Test]
        public async Task StartRun_WithContext_SendsCamelCaseContext_WithBase64InitialState()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedRun> starting = ValidatedActionsManager.Instance.StartRun("weekly", new ValidatedRunContext(
                "1.4.2",
                contentVersion: "levels-7",
                simulationVersion: "sim-3",
                replayFormatVersion: "  ",
                contentDigest: AbcHash.ToUpperInvariant(),
                initialState: new byte[] { 0, 1, 2, 255 }));

            HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            string body = await ReadBody(context);
            Assert.That(body, Does.Contain(
                "\"context\":{\"gameVersion\":\"1.4.2\",\"contentVersion\":\"levels-7\",\"simulationVersion\":\"sim-3\"," +
                "\"contentDigest\":\"" + AbcHash + "\",\"initialState\":\"AAEC/w==\"}"), body);
            Assert.That(body, Does.Not.Contain("replayFormatVersion"), "a blank field is left out");

            await Respond(context, 200, RunBody);
            Assert.That(await starting, Is.Not.Null);
        }

        [Test]
        public async Task StartRun_UsesDefaultRunContext_AndLeavesAnEmptyContextOut()
        {
            ValidatedActionsManager.Instance.DefaultRunContext = new ValidatedRunContext { gameVersion = "1.4.2" };
            try
            {
                Task<HttpListenerContext> incoming = _listener.GetContextAsync();
                Task<ValidatedRun> starting = ValidatedActionsManager.Instance.StartRun("weekly");
                HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
                string body = await ReadBody(context);
                Assert.That(body, Does.Contain("\"context\":{\"gameVersion\":\"1.4.2\"}"), body);
                await Respond(context, 200, RunBody);
                Assert.That(await starting, Is.Not.Null);

                incoming = _listener.GetContextAsync();
                starting = ValidatedActionsManager.Instance.StartRun("weekly", new ValidatedRunContext { initialState = new byte[0] });
                context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
                body = await ReadBody(context);
                Assert.That(body, Does.Not.Contain("context"), "a passed empty context replaces the default and is left out");
                await Respond(context, 200, RunBody);
                Assert.That(await starting, Is.Not.Null);
            }
            finally
            {
                ValidatedActionsManager.Instance.DefaultRunContext = null;
            }
        }

        [Test]
        public async Task StartRun_InvalidContentDigest_FailsLocallyWithoutRequest()
        {
            Task<HttpListenerContext> unexpectedRequest = _listener.GetContextAsync();

            Assert.That(await ValidatedActionsManager.Instance.StartRun("weekly",
                new ValidatedRunContext { contentDigest = AbcHash.Substring(1) }), Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.InvalidContentDigest));

            Task completed = await Task.WhenAny(unexpectedRequest, Task.Delay(350));
            Assert.That(completed, Is.Not.SameAs(unexpectedRequest), "an invalid content digest must not send a request");
        }

        [Test]
        public async Task StartRun_InitialStateTooLarge_ExposesTheServerCode()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedRun> starting = ValidatedActionsManager.Instance.StartRun("weekly",
                new ValidatedRunContext { initialState = new byte[] { 1, 2, 3 } });
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 413,
                "{\"status\":413,\"code\":\"INITIAL_STATE_TOO_LARGE\",\"message\":\"initialState is too large\"}");

            Assert.That(await starting, Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.InitialStateTooLarge));
            Assert.That(ValidatedActionsManager.Instance.HasActiveRun, Is.False);
        }

        [Test]
        public void SubmitResult_ReadsSus_AndDefaultsToFalse()
        {
            var sus = JsonUtility.FromJson<ValidatedSubmitResult>(
                "{\"accepted\":true,\"runId\":\"run-911\",\"evidence\":{\"required\":true,\"runId\":\"run-911\"},\"sus\":true}");
            Assert.That(sus.sus, Is.True);
            Assert.That(sus.evidence.required, Is.True);

            var plain = JsonUtility.FromJson<ValidatedSubmitResult>(AcceptedBody);
            Assert.That(plain.sus, Is.False);
        }

        private async Task StartRunWith(string runBody)
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedRun> starting = ValidatedActionsManager.Instance.StartRun("weekly");
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 200, runBody);
            Assert.That(await starting, Is.Not.Null);
        }

        private static async Task<string> ReadBody(HttpListenerContext context)
        {
            using (var reader = new StreamReader(context.Request.InputStream))
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

        private static void SetCurrentRun(ValidatedRun run)
        {
            Type type = typeof(ValidatedActionsManager);
            type.GetField("_currentRun", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(ValidatedActionsManager.Instance, run);
            type.GetField("_currentRunUserId", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(ValidatedActionsManager.Instance, UserManager.Instance.CurrentUser.UserId);
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(timeout));
            if (completed != task)
            {
                throw new TimeoutException("The local validated actions contract server did not receive a request in time.");
            }
            return await task;
        }
    }
}
