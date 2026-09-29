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
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    /// <summary>
    /// Gift code redemption is bound to the player's session on the server (TASK-886).
    /// Redeem must send the Bearer session and must not send anything without one.
    /// </summary>
    [Category("Transport")]
    public class GiftCodeTransportTests
    {
        private const string Host = "http://127.0.0.1:18725";
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
            config.SetApiKey("project-key-886");
            config.SetHosts(new[] { Host });

            NetworkService.Instance.Initialize(config);
            NetworkService.Instance.SetActiveHost(Host);
            NetworkService.Instance.SetSessionToken("session-token-886");
            LogService.Instance.EnableUnityLogging = false;
            LogService.Instance.EnableEventPublishing = false;

            SetCurrentUser(new UserData
            {
                UserId = "user-886",
                AccessToken = "session-token-886"
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
                if (gameObject.name == "[UserManager]" || gameObject.name == "[GiftCodeManager]")
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }

        [Test]
        public async Task Redeem_SendsSessionBoundRequest_AndRejectsMissingSessionBeforeTransport()
        {
            Task<HttpListenerContext> signedRequest = _listener.GetContextAsync();
            Task<RedeemGiftCodeResponse> redemption = GiftCodeManager.Instance.Redeem("SUMMER2026");

            HttpListenerContext context = await WithTimeout(signedRequest, TimeSpan.FromSeconds(5));
            string body;
            using (var reader = new StreamReader(context.Request.InputStream))
            {
                body = await reader.ReadToEndAsync();
            }

            Assert.That(context.Request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/gift-codes/redeem"));
            Assert.That(context.Request.Headers["X-API-Key"], Is.EqualTo("project-key-886"));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer session-token-886"));

            var request = JsonUtility.FromJson<RedeemGiftCodeRequest>(body);
            Assert.That(request.userId, Is.EqualTo("user-886"));
            Assert.That(request.code, Is.EqualTo("SUMMER2026"));

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            using (var writer = new StreamWriter(context.Response.OutputStream))
            {
                await writer.WriteAsync("{\"success\":true,\"message\":\"ok\",\"giftData\":\"{}\"}");
            }
            context.Response.Close();
            Assert.That(await redemption, Is.Not.Null);

            Task<HttpListenerContext> unexpectedRequest = _listener.GetContextAsync();
            SetCurrentUser(new UserData());
            Assert.That(await GiftCodeManager.Instance.Redeem("SUMMER2026"), Is.Null);

            Task completed = await Task.WhenAny(unexpectedRequest, Task.Delay(350));
            Assert.That(completed, Is.Not.SameAs(unexpectedRequest),
                "Redeem sent a network request even though no user session existed");
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
                throw new TimeoutException("The local gift code contract server did not receive a request in time.");
            }
            return await task;
        }
    }
}
