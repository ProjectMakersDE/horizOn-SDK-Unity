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
    /// Server contract checked 2026-10-01: AppCloudSaveController requires the player's
    /// session for save/load. Load is POST with a JSON userId and negotiates binary via Accept.
    /// Revision contract checked 2026-10-10 (AppCloudSaveController, CloudSaveService.saveCloudSave):
    /// load sends X-Cloud-Save-Revision on JSON, bytes and 204 (0 = empty slot), save reads a plain
    /// numeric If-Match and answers 409 with {"error": "..."} on a mismatch.
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
            Assert.That(context.Request.Headers["If-Match"], Is.Null, "Save(data) stays an unconditional write");
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
            Assert.That(context.Request.Headers["If-Match"], Is.Null, "SaveBytes(data) stays an unconditional write");
            await RespondJson(context, "{\"success\":true,\"dataSizeBytes\":4}");
            Assert.That(await WithTimeout(saving), Is.True);
        }

        [TestCase("{\"found\":true,\"saveData\":\"saved value\"}", "3", true, 3L)]
        [TestCase("{\"found\":false}", "0", false, 0L)]
        [TestCase("{\"found\":false}", null, false, null)]
        [TestCase("{\"found\":true,\"saveData\":\"saved value\"}", "not-a-number", true, null)]
        public async Task LoadSnapshot_ReadsRevisionHeader(string body, string revisionHeader, bool found, long? revision)
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<CloudSaveSnapshot<string>> loading = CloudSaveManager.Instance.LoadSnapshot();
            var context = await WithTimeout(incoming);
            AssertSignedPost(context, "/api/v1/app/cloud-save/load");
            Assert.That(context.Request.Headers["Accept"], Is.Not.EqualTo("application/octet-stream"));
            Assert.That(JsonUtility.FromJson<LoadCloudDataRequest>(await ReadText(context)).userId, Is.EqualTo("user-892"));
            // Lower case like a WebGL player reports it: the lookup must not depend on case.
            if (revisionHeader != null) context.Response.AddHeader("x-cloud-save-revision", revisionHeader);
            await RespondJson(context, body);

            var snapshot = await WithTimeout(loading);
            Assert.That(snapshot.IsSuccess, Is.True);
            Assert.That(snapshot.Found, Is.EqualTo(found));
            Assert.That(snapshot.Data, Is.EqualTo(found ? "saved value" : null));
            Assert.That(snapshot.Revision, Is.EqualTo(revision), "A missing header is an unknown state, not revision 0");
            Assert.That(snapshot.HasRevision, Is.EqualTo(revision.HasValue));
        }

        [TestCase(200, "5", 5L)]
        [TestCase(204, "0", 0L)]
        [TestCase(204, null, null)]
        public async Task LoadBytesSnapshot_ReadsRevisionHeaderOnBytesAndNoContent(int status, string revisionHeader, long? revision)
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<CloudSaveSnapshot<byte[]>> loading = CloudSaveManager.Instance.LoadBytesSnapshot();
            var context = await WithTimeout(incoming);
            AssertSignedPost(context, "/api/v1/app/cloud-save/load");
            Assert.That(context.Request.Headers["Accept"], Is.EqualTo("application/octet-stream"));

            context.Response.StatusCode = status;
            if (revisionHeader != null) context.Response.AddHeader("X-Cloud-Save-Revision", revisionHeader);
            if (status == 200)
            {
                context.Response.ContentType = "application/octet-stream";
                context.Response.ContentLength64 = SaveBytes.Length;
                await context.Response.OutputStream.WriteAsync(SaveBytes, 0, SaveBytes.Length);
            }
            context.Response.Close();

            var snapshot = await WithTimeout(loading);
            Assert.That(snapshot.IsSuccess, Is.True);
            Assert.That(snapshot.Found, Is.EqualTo(status == 200));
            if (status == 200) CollectionAssert.AreEqual(SaveBytes, snapshot.Data);
            else Assert.That(snapshot.Data, Is.Null);
            Assert.That(snapshot.Revision, Is.EqualTo(revision), "A 204 without the header is an unknown state, not revision 0");
        }

        [TestCase("json", 0L)]
        [TestCase("json", 7L)]
        [TestCase("bytes", 0L)]
        [TestCase("bytes", 7L)]
        public async Task ConditionalSave_SendsIfMatchAndReturnsNewRevision(string mode, long expectedRevision)
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<CloudSaveWriteResult> saving = mode == "json"
                ? CloudSaveManager.Instance.Save("{\"level\":3}", expectedRevision)
                : CloudSaveManager.Instance.SaveBytes(SaveBytes, expectedRevision);
            var context = await WithTimeout(incoming);
            if (mode == "json")
            {
                AssertSignedPost(context, "/api/v1/app/cloud-save/save");
                Assert.That(context.Request.ContentType, Does.StartWith("application/json"));
                Assert.That(JsonUtility.FromJson<SaveCloudDataRequest>(await ReadText(context)).saveData, Is.EqualTo("{\"level\":3}"));
            }
            else
            {
                AssertSignedPost(context, "/api/v1/app/cloud-save/save?userId=user-892");
                Assert.That(context.Request.ContentType, Does.StartWith("application/octet-stream"));
            }
            Assert.That(context.Request.Headers["If-Match"], Is.EqualTo(expectedRevision.ToString()));
            await RespondJson(context, "{\"success\":true,\"dataSizeBytes\":4,\"revision\":" + (expectedRevision + 1) + "}");

            var result = await WithTimeout(saving);
            Assert.That(result.Status, Is.EqualTo(CloudSaveWriteStatus.Saved));
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Revision, Is.EqualTo(expectedRevision + 1));
            Assert.That(result.DataSizeBytes, Is.EqualTo(4));
        }

        [Test]
        public async Task ConditionalSave_WithoutRevisionInResponseReportsUnknownRevision()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<CloudSaveWriteResult> saving = CloudSaveManager.Instance.Save("value", 2);
            var context = await WithTimeout(incoming);
            await RespondJson(context, "{\"success\":true,\"dataSizeBytes\":5}");
            var result = await WithTimeout(saving);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Revision, Is.Null);
        }

        [TestCase("json")]
        [TestCase("bytes")]
        public async Task ConditionalSave_ConflictIsReportedWithoutRetry(string mode)
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<CloudSaveWriteResult> saving = mode == "json"
                ? CloudSaveManager.Instance.Save("stale", 4)
                : CloudSaveManager.Instance.SaveBytes(SaveBytes, 4);
            var context = await WithTimeout(incoming);
            Assert.That(context.Request.Headers["If-Match"], Is.EqualTo("4"));
            context.Response.StatusCode = 409;
            context.Response.ContentType = "application/json";
            using (var writer = new StreamWriter(context.Response.OutputStream))
                await writer.WriteAsync("{\"error\":\"Cloud save changed on another device (expected revision 4, current revision 5)\"}");
            context.Response.Close();

            Task<HttpListenerContext> retry = _listener.GetContextAsync();
            var result = await WithTimeout(saving);
            Assert.That(result.Status, Is.EqualTo(CloudSaveWriteStatus.Conflict));
            Assert.That(result.IsConflict, Is.True);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(409));
            Assert.That(result.Revision, Is.Null);
            Assert.That(result.Error, Does.Contain("another device"));
            Assert.That(await Task.WhenAny(retry, Task.Delay(300)), Is.Not.SameAs(retry), "A conflict must not be retried or overwritten");
        }

        [TestCase("json")]
        [TestCase("bytes")]
        public async Task ConditionalSave_NegativeRevisionIsRejectedBeforeTransport(string mode)
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            var result = mode == "json"
                ? await CloudSaveManager.Instance.Save("value", -1)
                : await CloudSaveManager.Instance.SaveBytes(SaveBytes, -1);
            Assert.That(result.Status, Is.EqualTo(CloudSaveWriteStatus.Failed));
            Assert.That(result.StatusCode, Is.EqualTo(0));
            Assert.That(await Task.WhenAny(incoming, Task.Delay(100)), Is.Not.SameAs(incoming));
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
        [TestCase("loadSnapshot", "user")]
        [TestCase("loadBytesSnapshot", "missingToken")]
        [TestCase("conditionalSave", "staleToken")]
        [TestCase("conditionalSaveBytes", "user")]
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
                case "loadSnapshot": Assert.That((await CloudSaveManager.Instance.LoadSnapshot()).IsSuccess, Is.False); break;
                case "loadBytesSnapshot": Assert.That((await CloudSaveManager.Instance.LoadBytesSnapshot()).IsSuccess, Is.False); break;
                case "conditionalSave": Assert.That((await CloudSaveManager.Instance.Save("save", 1)).Status, Is.EqualTo(CloudSaveWriteStatus.Failed)); break;
                case "conditionalSaveBytes": Assert.That((await CloudSaveManager.Instance.SaveBytes(SaveBytes, 1)).Status, Is.EqualTo(CloudSaveWriteStatus.Failed)); break;
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
