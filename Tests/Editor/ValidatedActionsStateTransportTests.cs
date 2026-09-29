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
    /// Validated Actions Part 2 (TASK-887): GET state carries the player's Bearer session and
    /// userId, the state of an accepted submit updates CurrentState without its per-run fields,
    /// a submit without a state keeps the loaded one, and the value codes reach LastErrorCode.
    /// </summary>
    [Category("Transport")]
    public class ValidatedActionsStateTransportTests
    {
        private const string Host = "http://127.0.0.1:18887";

        private const string Ticket = "hzn-rt1:2026-09:Qm9:c2Vj";

        private const string RunBody =
            "{\"runId\":\"run-887\",\"ticket\":\"" + Ticket + "\",\"seed\":7," +
            "\"leaderboardKey\":null,\"issuedAt\":\"2026-09-29T14:00:00.120Z\"," +
            "\"expiresAt\":\"2026-09-29T16:00:00.120Z\",\"expiresInSeconds\":7200}";

        private const string StateBody =
            "{\"userId\":\"user-887\",\"day\":\"2026-09-29\",\"values\":[" +
            "{\"key\":\"chest.gold\",\"balance\":2,\"earnedToday\":0,\"dailyCap\":null}," +
            "{\"key\":\"gold\",\"balance\":9007199254740991,\"earnedToday\":250,\"dailyCap\":5000}]}";

        private const string AcceptedWithStateBody =
            "{\"accepted\":true,\"runId\":\"run-887\",\"leaderboardKey\":null,\"score\":null," +
            "\"bestScore\":null,\"isNewHighScore\":false,\"rank\":null,\"durationSeconds\":95," +
            "\"state\":{\"day\":\"2026-09-29\",\"values\":[" +
            "{\"key\":\"chest.gold\",\"balance\":1,\"earnedToday\":0,\"dailyCap\":null,\"requested\":-1,\"credited\":-1}," +
            "{\"key\":\"gold\",\"balance\":1500,\"earnedToday\":500,\"dailyCap\":500,\"requested\":400,\"credited\":250}]}," +
            "\"evidence\":null}";

        private const string AcceptedWithoutStateBody =
            "{\"accepted\":true,\"runId\":\"run-887\",\"leaderboardKey\":null,\"score\":null," +
            "\"bestScore\":null,\"isNewHighScore\":false,\"rank\":null,\"durationSeconds\":95," +
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
            config.SetApiKey("project-key-887");
            config.SetHosts(new[] { Host });

            NetworkService.Instance.Initialize(config);
            NetworkService.Instance.SetActiveHost(Host);
            NetworkService.Instance.SetSessionToken("session-token-887");
            LogService.Instance.EnableUnityLogging = false;
            LogService.Instance.EnableEventPublishing = false;

            SetCurrentUser(new UserData
            {
                UserId = "user-887",
                AccessToken = "session-token-887"
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
        public async Task GetState_SendsSessionAndUserId_CachesTheState_AndPublishesTheEvent()
        {
            PlayerState published = null;
            Action<PlayerState> handler = data => published = data;
            EventService.Instance.Subscribe(EventKeys.ValidatedStateLoaded, handler);

            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<PlayerState> loading = ValidatedActionsManager.Instance.GetState();

            HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            Assert.That(context.Request.HttpMethod, Is.EqualTo("GET"));
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/validated-actions/state?userId=user-887"));
            Assert.That(context.Request.Headers["X-API-Key"], Is.EqualTo("project-key-887"));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer session-token-887"));

            await Respond(context, 200, StateBody);
            PlayerState state = await loading;

            Assert.That(state, Is.Not.Null);
            Assert.That(state.HasData, Is.True);
            Assert.That(state.day, Is.EqualTo("2026-09-29"));
            Assert.That(state.values.Length, Is.EqualTo(2));
            Assert.That(state.GetBalance("gold"), Is.EqualTo(9007199254740991L), "balances are read as long without loss");
            Assert.That(state.GetBalance("chest.gold"), Is.EqualTo(2));
            Assert.That(state.GetBalance("gems"), Is.EqualTo(0));
            Assert.That(state.GetValue("gold").dailyCap, Is.EqualTo(5000));
            Assert.That(state.GetValue("gold").RemainingToday, Is.EqualTo(4750));
            Assert.That(state.GetValue("chest.gold").dailyCap, Is.EqualTo(0), "JSON null dailyCap reads as 0");
            Assert.That(state.GetValue("chest.gold").HasDailyCap, Is.False);
            Assert.That(state.GetValue("chest.gold").RemainingToday, Is.EqualTo(long.MaxValue));

            Assert.That(ValidatedActionsManager.Instance.CurrentState, Is.Not.Null);
            Assert.That(ValidatedActionsManager.Instance.CurrentState.GetBalance("chest.gold"), Is.EqualTo(2));
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.Null);
            Assert.That(published, Is.SameAs(state));

            SetCurrentUser(new UserData());
            Assert.That(ValidatedActionsManager.Instance.CurrentState, Is.Null, "sign-out drops the state");

            EventService.Instance.Unsubscribe(EventKeys.ValidatedStateLoaded, handler);
        }

        [Test]
        public async Task Submit_WithEarned_UpdatesCurrentStateWithoutRunDetails()
        {
            await StartRunWith(RunBody);

            PlayerState published = null;
            Action<PlayerState> handler = data => published = data;
            EventService.Instance.Subscribe(EventKeys.ValidatedStateLoaded, handler);

            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedSubmitResult> submitting = ValidatedActionsManager.Instance.SubmitValidated(
                0, Encoding.ASCII.GetBytes("abc"),
                earned: new[] { new EarnedValue("gold", 400), new EarnedValue("chest.gold", -1) });

            HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            string body = await ReadBody(context);
            Assert.That(body, Does.Contain("\"earned\":[{\"key\":\"gold\",\"amount\":400},{\"key\":\"chest.gold\",\"amount\":-1}]"), body);

            await Respond(context, 200, AcceptedWithStateBody);
            ValidatedSubmitResult result = await submitting;

            Assert.That(result, Is.Not.Null);
            Assert.That(result.HasLeaderboard, Is.False);
            PlayerStateValue gold = result.state.GetValue("gold");
            Assert.That(gold.requested, Is.EqualTo(400));
            Assert.That(gold.credited, Is.EqualTo(250), "the daily cap clamped the credit");
            Assert.That(gold.IsFullyCredited, Is.False);
            Assert.That(result.state.GetValue("chest.gold").IsFullyCredited, Is.True, "the spend was applied");

            PlayerState current = ValidatedActionsManager.Instance.CurrentState;
            Assert.That(current, Is.Not.Null);
            Assert.That(current, Is.Not.SameAs(result.state));
            Assert.That(current.GetBalance("gold"), Is.EqualTo(1500));
            Assert.That(current.GetValue("gold").requested, Is.EqualTo(0), "CurrentState has no per-run fields");
            Assert.That(current.GetValue("gold").credited, Is.EqualTo(0));
            Assert.That(published, Is.SameAs(current));

            EventService.Instance.Unsubscribe(EventKeys.ValidatedStateLoaded, handler);
        }

        [Test]
        public async Task Submit_WithoutState_KeepsTheLoadedState()
        {
            SetCurrentState(new PlayerState
            {
                day = "2026-09-29",
                values = new[] { new PlayerStateValue { key = "gold", balance = 1250 } }
            });
            await StartRunWith(RunBody);

            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedSubmitResult> submitting = ValidatedActionsManager.Instance.SubmitValidatedWithHash(0, AbcHash);
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 200, AcceptedWithoutStateBody);

            ValidatedSubmitResult result = await submitting;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.state, Is.Not.Null);
            Assert.That(result.state.HasData, Is.False);
            Assert.That(ValidatedActionsManager.Instance.CurrentState.GetBalance("gold"), Is.EqualTo(1250));
        }

        [Test]
        public async Task Submit_InsufficientBalance_ExposesCode_AndDropsTheRun()
        {
            await StartRunWith(RunBody);

            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedSubmitResult> submitting = ValidatedActionsManager.Instance.SubmitValidatedWithHash(
                0, AbcHash, earned: new[] { new EarnedValue("gold", -5000) });
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 422,
                "{\"status\":422,\"error\":\"Unprocessable Entity\",\"code\":\"INSUFFICIENT_BALANCE\"," +
                "\"message\":\"The player does not have enough of a value spent in this run\",\"runId\":\"run-887\"}");

            Assert.That(await submitting, Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.InsufficientBalance));
            Assert.That(ValidatedActionsManager.Instance.HasActiveRun, Is.False, "a value rejection uses up the ticket");
        }

        [Test]
        public async Task GetState_Failures_SetLastErrorCode_AndKeepTheCachedState()
        {
            SetCurrentState(new PlayerState
            {
                day = "2026-09-29",
                values = new[] { new PlayerStateValue { key = "gold", balance = 1250 } }
            });

            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<PlayerState> loading = ValidatedActionsManager.Instance.GetState();
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 404, "");

            Assert.That(await loading, Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.NotSupported));
            Assert.That(ValidatedActionsManager.Instance.CurrentState.GetBalance("gold"), Is.EqualTo(1250));

            Task<HttpListenerContext> unexpectedRequest = _listener.GetContextAsync();
            NetworkService.Instance.SetSessionToken("stale-token");
            Assert.That(await ValidatedActionsManager.Instance.GetState(), Is.Null);
            Assert.That(ValidatedActionsManager.Instance.LastErrorCode, Is.EqualTo(ValidatedActionsErrorCodes.SessionRequired));

            Task completed = await Task.WhenAny(unexpectedRequest, Task.Delay(350));
            Assert.That(completed, Is.Not.SameAs(unexpectedRequest),
                "GetState sent a request without a matching session");
        }

        [Test]
        public void ValueCodes_EndTheRun()
        {
            foreach (string code in new[]
                     {
                         ValidatedActionsErrorCodes.UnknownValueKey,
                         ValidatedActionsErrorCodes.DuplicateValueKey,
                         ValidatedActionsErrorCodes.EarnedAboveMax,
                         ValidatedActionsErrorCodes.EarnedBelowMin,
                         ValidatedActionsErrorCodes.InsufficientBalance
                     })
            {
                Assert.That(ValidatedActionsTransportContract.EndsRun(422, code), Is.True, code);
            }
            Assert.That(ValidatedActionsTransportContract.StateToCache(null), Is.Null);
            Assert.That(ValidatedActionsTransportContract.StateToCache(new PlayerState()), Is.Null);
        }

        private async Task StartRunWith(string runBody)
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<ValidatedRun> starting = ValidatedActionsManager.Instance.StartRun();
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

        private static void SetCurrentState(PlayerState state)
        {
            Type type = typeof(ValidatedActionsManager);
            type.GetField("_currentState", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(ValidatedActionsManager.Instance, state);
            type.GetField("_currentStateUserId", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(ValidatedActionsManager.Instance, UserManager.Instance.CurrentUser.UserId);
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(timeout));
            if (completed != task)
            {
                throw new TimeoutException("The local validated actions state contract server did not receive a request in time.");
            }
            return await task;
        }
    }
}
