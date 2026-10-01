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
    /// <summary>
    /// Server contract checked 2026-10-01: AppCloudSaveController requires the player's
    /// session for save/load. Load is POST with a JSON userId and negotiates binary via Accept.
    /// </summary>
    [Category("Transport")]
    public class CloudSaveTransportTests
    {
        private const string Host = "http://127.0.0.1:18730";
        private static readonly byte[] SaveBytes = { 0, 255, 128, 42 };
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
            config.SetApiKey("project-key-892");
            config.SetHosts(new[] { Host });
            NetworkService.Instance.Initialize(config);
            NetworkService.Instance.SetActiveHost(Host);
            NetworkService.Instance.SetSessionToken("session-token-892");
            LogService.Instance.EnableUnityLogging = false;
            LogService.Instance.EnableEventPublishing = false;
            SetCurrentUser(new UserData { UserId = "user-892", AccessToken = "session-token-892" });

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
                if (gameObject.name == "[UserManager]" || gameObject.name == "[CloudSaveManager]")
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }

        [Test]
        public async Task Save_SendsJsonWithSession()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<bool> saving = CloudSaveManager.Instance.Save("{\"level\":2}");
            var context = await WithTimeout(incoming);
            var request = JsonUtility.FromJson<SaveCloudDataRequest>(await ReadText(context));
            AssertSignedPost(context, "/api/v1/app/cloud-save/save");
            Assert.That(context.Request.ContentType, Does.StartWith("application/json"));
            Assert.That(request.userId, Is.EqualTo("user-892"));
            Assert.That(request.saveData, Is.EqualTo("{\"level\":2}"));
            await RespondJson(context, "{\"success\":true,\"dataSizeBytes\":11}");
            Assert.That(await WithTimeout(saving), Is.True);
        }

        [Test]
        public async Task Load_SendsJsonWithSession()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<string> loading = CloudSaveManager.Instance.Load();
            var context = await WithTimeout(incoming);
            AssertSignedPost(context, "/api/v1/app/cloud-save/load");
            Assert.That(context.Request.ContentType, Does.StartWith("application/json"));
            Assert.That(JsonUtility.FromJson<LoadCloudDataRequest>(await ReadText(context)).userId, Is.EqualTo("user-892"));
            await RespondJson(context, "{\"found\":true,\"saveData\":\"saved value\"}");
            Assert.That(await WithTimeout(loading), Is.EqualTo("saved value"));
        }

        [Test]
        public async Task SaveBytes_SendsRawBytesWithSession()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<bool> saving = CloudSaveManager.Instance.SaveBytes(SaveBytes);
            var context = await WithTimeout(incoming);
            using (var stream = new MemoryStream())
            {
                await context.Request.InputStream.CopyToAsync(stream);
                CollectionAssert.AreEqual(SaveBytes, stream.ToArray());
            }
            AssertSignedPost(context, "/api/v1/app/cloud-save/save?userId=user-892");
            Assert.That(context.Request.ContentType, Does.StartWith("application/octet-stream"));
            await RespondJson(context, "{\"success\":true,\"dataSizeBytes\":4}");
            Assert.That(await WithTimeout(saving), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LoadBytes_PostsJsonAndAcceptsRawBytesOrNoContent(bool notFound)
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<byte[]> loading = CloudSaveManager.Instance.LoadBytes();
            var context = await WithTimeout(incoming);
            AssertSignedPost(context, "/api/v1/app/cloud-save/load");
            Assert.That(context.Request.ContentType, Does.StartWith("application/json"));
            Assert.That(context.Request.Headers["Accept"], Is.EqualTo("application/octet-stream"));
            Assert.That(JsonUtility.FromJson<LoadCloudDataRequest>(await ReadText(context)).userId, Is.EqualTo("user-892"));

            context.Response.StatusCode = notFound ? 204 : 200;
            context.Response.ContentType = "application/octet-stream";
            if (!notFound)
            {
                context.Response.ContentLength64 = SaveBytes.Length;
                await context.Response.OutputStream.WriteAsync(SaveBytes, 0, SaveBytes.Length);
            }
            context.Response.Close();
            if (notFound)
            {
                Assert.That(await WithTimeout(loading), Is.Null);
            }
            else
            {
                CollectionAssert.AreEqual(SaveBytes, await WithTimeout(loading));
            }
        }

        [Test]
        public async Task BinaryTransport_PreservesGetAndReportsPostRejection()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<BinaryNetworkResponse> getting = NetworkService.Instance.GetBinaryAsync("/binary", useSessionToken: true);
            var context = await WithTimeout(incoming);
            Assert.That(context.Request.HttpMethod, Is.EqualTo("GET"));
            Assert.That(context.Request.Headers["Accept"], Is.EqualTo("application/octet-stream"));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer session-token-892"));
            context.Response.StatusCode = 204;
            context.Response.Close();
            var missing = await WithTimeout(getting);
            Assert.That(missing.IsSuccess, Is.True);
            Assert.That(missing.Found, Is.False);

            incoming = _listener.GetContextAsync();
            Task<BinaryNetworkResponse> posting = NetworkService.Instance.PostForBinaryAsync(
                "/api/v1/app/cloud-save/load", new LoadCloudDataRequest { userId = "user-892" }, useSessionToken: true);
            context = await WithTimeout(incoming);
            AssertSignedPost(context, "/api/v1/app/cloud-save/load");
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            using (var writer = new StreamWriter(context.Response.OutputStream))
                await writer.WriteAsync("{\"message\":\"Session expired\"}");
            context.Response.Close();
            var rejected = await WithTimeout(posting);
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.StatusCode, Is.EqualTo(401));
            Assert.That(rejected.Error, Does.Contain("Session expired"));
        }

        [TestCase("save", "user")]
        [TestCase("load", "user")]
        [TestCase("saveBytes", "user")]
        [TestCase("loadBytes", "user")]
        [TestCase("save", "missingToken")]
        [TestCase("load", "missingToken")]
        [TestCase("saveBytes", "missingToken")]
        [TestCase("loadBytes", "missingToken")]
        [TestCase("save", "staleToken")]
        [TestCase("load", "staleToken")]
        [TestCase("saveBytes", "staleToken")]
        [TestCase("loadBytes", "staleToken")]
        public async Task MissingOrMismatchedSession_DoesNotSendRequest(string operation, string missing)
        {
            if (missing == "user") SetCurrentUser(new UserData());
            else NetworkService.Instance.SetSessionToken(missing == "missingToken" ? "" : "stale-session");

            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task request = RunOperation(operation);
            Task first = await Task.WhenAny(request, incoming, Task.Delay(2000));
            Assert.That(first, Is.SameAs(request), "Cloud Save must reject a missing or mismatched session before transport");
            await request;
            Assert.That(await Task.WhenAny(incoming, Task.Delay(100)), Is.Not.SameAs(incoming));
        }

        private static async Task RunOperation(string operation)
        {
            switch (operation)
            {
                case "save": Assert.That(await CloudSaveManager.Instance.Save("save"), Is.False); break;
                case "load": Assert.That(await CloudSaveManager.Instance.Load(), Is.Null); break;
                case "saveBytes": Assert.That(await CloudSaveManager.Instance.SaveBytes(SaveBytes), Is.False); break;
                case "loadBytes": Assert.That(await CloudSaveManager.Instance.LoadBytes(), Is.Null); break;
                default: Assert.Fail("Unknown Cloud Save operation"); break;
            }
        }

        private static void AssertSignedPost(HttpListenerContext context, string endpoint)
        {
            Assert.That(context.Request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(context.Request.RawUrl, Is.EqualTo(endpoint));
            Assert.That(context.Request.Headers["X-API-Key"], Is.EqualTo("project-key-892"));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer session-token-892"));
        }

        private static async Task<string> ReadText(HttpListenerContext context)
        {
            using (var reader = new StreamReader(context.Request.InputStream)) return await reader.ReadToEndAsync();
        }

        private static async Task RespondJson(HttpListenerContext context, string json)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            using (var writer = new StreamWriter(context.Response.OutputStream)) await writer.WriteAsync(json);
            context.Response.Close();
        }

        private static void SetCurrentUser(UserData user)
        {
            FieldInfo field = typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(UserManager.Instance, user);
        }

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(5000)) != task)
                throw new TimeoutException("The local Cloud Save transport did not complete in time.");
            return await task;
        }
    }
}
