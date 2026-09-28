using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Service;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")]
    public class LeaderboardTransportTests
    {
        private const string Host = "http://127.0.0.1:18721";
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
            config.SetApiKey("project-key-720");
            config.SetHosts(new[] { Host });

            NetworkService.Instance.Initialize(config);
            NetworkService.Instance.SetActiveHost(Host);
            NetworkService.Instance.SetSessionToken("session-token-720");
            LogService.Instance.EnableUnityLogging = false;
            LogService.Instance.EnableEventPublishing = false;

            SetCurrentUser(new UserData
            {
                UserId = "user-720",
                AccessToken = "session-token-720"
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
                if (gameObject.name == "[UserManager]" || gameObject.name == "[LeaderboardManager]")
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }

        [Test]
        public async Task SubmitScore_SendsSignedRequest_AndRejectsMissingSessionBeforeTransport()
        {
            Task<HttpListenerContext> signedRequest = _listener.GetContextAsync();
            Task<bool> submission = LeaderboardManager.Instance.SubmitScore(4242, boardKey: "season one");

            HttpListenerContext context = await WithTimeout(signedRequest, TimeSpan.FromSeconds(5));
            string body;
            using (var reader = new StreamReader(context.Request.InputStream))
            {
                body = await reader.ReadToEndAsync();
            }

            Assert.That(context.Request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/leaderboards/season%20one/submit"));
            Assert.That(context.Request.Headers["X-API-Key"], Is.EqualTo("project-key-720"));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer session-token-720"));
            Assert.That(context.Request.ContentType, Does.StartWith("application/json"));

            var request = JsonUtility.FromJson<SubmitScoreRequest>(body);
            Assert.That(request.userId, Is.EqualTo("user-720"));
            Assert.That(request.score, Is.EqualTo(4242));
            Assert.That(request.leaderboardKey, Is.EqualTo("season one"));

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            using (var writer = new StreamWriter(context.Response.OutputStream))
            {
                await writer.WriteAsync("{}");
            }
            context.Response.Close();
            Assert.That(await submission, Is.True);

            Task<HttpListenerContext> unexpectedRequest = _listener.GetContextAsync();
            SetCurrentUser(new UserData());
            Assert.That(await LeaderboardManager.Instance.SubmitScore(99, boardKey: "season one"), Is.False);

            Task completed = await Task.WhenAny(unexpectedRequest, Task.Delay(350));
            Assert.That(completed, Is.Not.SameAs(unexpectedRequest),
                "SubmitScore sent a network request even though no user session existed");
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
                throw new TimeoutException("The local leaderboard contract server did not receive a request in time.");
            }
            return await task;
        }
    }
}
