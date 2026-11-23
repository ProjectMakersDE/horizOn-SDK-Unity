using System;
using System.IO;
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
    /// <summary>
    /// TASK-1149: a guest account is linked to EMAIL, GOOGLE or APPLE with the same user ID.
    /// The session token keeps working, the locally stored anonymous token is deleted.
    /// </summary>
    [Category("Transport")]
    public class UserManagerLinkTransportTests
    {
        private const string Host = "http://127.0.0.1:18149";

        private HttpListener _listener;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            PlayerPrefs.SetString("horizOn_AnonymousToken", "anon-token-1149");
            NetworkService.ResetInstance();
            LogService.ResetInstance();
            EventService.ResetInstance();

            var config = ScriptableObject.CreateInstance<HorizonConfig>();
            config.SetApiKey("project-key-1149");
            config.SetHosts(new[] { Host });

            NetworkService.Instance.Initialize(config);
            NetworkService.Instance.SetActiveHost(Host);
            NetworkService.Instance.SetSessionToken("session-token-1149");
            LogService.Instance.EnableUnityLogging = false;
            LogService.Instance.EnableEventPublishing = false;

            SetCurrentUser(new UserData
            {
                UserId = "user-1149",
                AccessToken = "session-token-1149",
                AnonymousToken = "anon-token-1149",
                AuthType = "ANONYMOUS",
                IsAnonymous = true
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
                if (gameObject.name == "[UserManager]")
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }

        [Test]
        public async Task LinkEmail_PostsLinkRequest_AndKeepsUserIdWithUnverifiedEmail()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<bool> linking = UserManager.Instance.LinkEmail("player@example.com", "Secret-1149");

            HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            string body = await ReadBody(context);
            Assert.That(context.Request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/user-management/link"));
            Assert.That(context.Request.Headers["X-API-Key"], Is.EqualTo("project-key-1149"));
            Assert.That(body, Does.Contain("\"userId\":\"user-1149\""));
            Assert.That(body, Does.Contain("\"sessionToken\":\"session-token-1149\""));
            Assert.That(body, Does.Contain("\"type\":\"EMAIL\""));
            Assert.That(body, Does.Contain("\"email\":\"player@example.com\""));

            await Respond(context, 200,
                "{\"userId\":\"user-1149\",\"email\":\"player@example.com\",\"isPrivateRelayEmail\":false," +
                "\"isAnonymous\":false,\"isVerified\":false,\"authStatus\":\"USER_NOT_VERIFIED\",\"message\":\"ok\"}");

            Assert.That(await linking, Is.True);
            UserData user = UserManager.Instance.CurrentUser;
            Assert.That(user.UserId, Is.EqualTo("user-1149"));
            Assert.That(user.IsAnonymous, Is.False);
            Assert.That(user.AuthType, Is.EqualTo("EMAIL"));
            Assert.That(user.Email, Is.EqualTo("player@example.com"));
            Assert.That(user.IsEmailVerified, Is.False);
            Assert.That(user.AccessToken, Is.EqualTo("session-token-1149"));
            Assert.That(user.AnonymousToken, Is.Empty);
            Assert.That(UserManager.Instance.HasCachedAnonymousToken(), Is.False);
        }

        [Test]
        public async Task LinkApple_SendsIdentityToken_AndAuthenticates()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<bool> linking = UserManager.Instance.LinkApple("apple-jwt-1149");

            HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            string body = await ReadBody(context);
            Assert.That(body, Does.Contain("\"type\":\"APPLE\""));
            Assert.That(body, Does.Contain("\"appleIdentityToken\":\"apple-jwt-1149\""));

            await Respond(context, 200,
                "{\"userId\":\"user-1149\",\"appleUserId\":\"apple-sub\",\"isPrivateRelayEmail\":true," +
                "\"isAnonymous\":false,\"isVerified\":true,\"authStatus\":\"AUTHENTICATED\",\"message\":\"ok\"}");

            Assert.That(await linking, Is.True);
            UserData user = UserManager.Instance.CurrentUser;
            Assert.That(user.AuthType, Is.EqualTo("APPLE"));
            Assert.That(user.AppleUserId, Is.EqualTo("apple-sub"));
            Assert.That(user.IsPrivateRelayEmail, Is.True);
            Assert.That(user.IsEmailVerified, Is.True);
            Assert.That(user.IsAnonymous, Is.False);
        }

        [Test]
        public async Task LinkGoogle_Conflict_KeepsGuestAccountAndToken()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<bool> linking = UserManager.Instance.LinkGoogle("google-code-1149", "https://example.com/cb");

            HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            string body = await ReadBody(context);
            Assert.That(body, Does.Contain("\"type\":\"GOOGLE\""));
            Assert.That(body, Does.Contain("\"googleAuthorizationCode\":\"google-code-1149\""));
            Assert.That(body, Does.Contain("\"googleRedirectUri\":\"https://example.com/cb\""));

            await Respond(context, 409, "{\"error\":\"Google account already in use\"}");

            Assert.That(await linking, Is.False);
            UserData user = UserManager.Instance.CurrentUser;
            Assert.That(user.IsAnonymous, Is.True);
            Assert.That(user.UserId, Is.EqualTo("user-1149"));
            Assert.That(UserManager.Instance.HasCachedAnonymousToken(), Is.True);
        }

        [Test]
        public async Task LinkEmail_WithoutGuestSession_FailsWithoutRequest()
        {
            SetCurrentUser(new UserData { UserId = "user-1149", AccessToken = "session-token-1149", IsAnonymous = false });

            Assert.That(await UserManager.Instance.LinkEmail("player@example.com", "Secret-1149"), Is.False);
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

        private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(timeout));
            if (completed != task)
            {
                throw new TimeoutException("The local link contract server did not receive a request in time.");
            }
            return await task;
        }
    }
}
