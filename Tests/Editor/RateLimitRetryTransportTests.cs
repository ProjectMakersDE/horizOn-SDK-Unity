using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using PM.horizOn.Cloud.Transport;
using UnityEngine;

namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")]
    public class RateLimitRetryTransportTests
    {
        private const string Host = "http://127.0.0.1:18996";
        private HttpListener _listener;
        private HorizonConfig _config;
        [Serializable] private class Payload { public string idempotencyId = "original-intent"; public int amount = 7; }
        private class Outcome { public bool success; public long status; public string code, retryAfter; }
        [SetUp] public void Setup()
        {
            NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            _config = ScriptableObject.CreateInstance<HorizonConfig>(); _config.SetApiKey("fixture-retry"); _config.SetHosts(new[] { Host });
            typeof(HorizonConfig).GetField("_retryDelaySeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 0f);
            NetworkService.Instance.Initialize(_config); NetworkService.Instance.SetActiveHost(Host); NetworkService.Instance.SetSessionToken("fixture-session");
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false;
            _listener = new HttpListener(); _listener.Prefixes.Add(Host + "/"); _listener.Start();
        }
        [TearDown] public void Teardown() { _listener?.Close(); UnityEngine.Object.DestroyImmediate(_config); }
        private static async Task<Outcome> Send(string mode, object payload, byte[] bytes, IReadOnlyDictionary<string, string> headers)
        {
            var network = NetworkService.Instance;
            if (mode == "json" || mode == "upload")
            {
                var r = mode == "json" ? await network.PostAsync<MessageResponse>("/retry/json", payload, true, headers)
                    : await network.PostBinaryAsync<MessageResponse>("/retry/upload", bytes, true, headers);
                return new Outcome { success = r.IsSuccess, status = r.StatusCode, code = r.ErrorCode, retryAfter = r.GetHeader("Retry-After") };
            }
            var b = mode == "download" ? await network.PostForBinaryAsync("/retry/download", payload, true, headers) : await network.GetBinaryAsync("/retry/download", true);
            return new Outcome { success = b.IsSuccess, status = b.StatusCode, code = b.ErrorCode, retryAfter = b.GetHeader("Retry-After") };
        }
        [Test] public void NativeMonoParsesAllHttpDatesAndPreservesOversizedWaits()
        {
            var now = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
            foreach (string date in new[] { "Fri, 16 Oct 2026 01:00:00 GMT", "Friday, 16-Oct-26 01:00:00 GMT", "Fri Oct 16 01:00:00 2026" })
                Assert.That(RateLimitRetryPolicy.RetryAfterSeconds(date, 1, now), Is.EqualTo(3600));
            Assert.That(RateLimitRetryPolicy.RetryAfterSeconds("Fri Nov  6 01:00:00 2026", 1, new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero)), Is.EqualTo(3600));
            Assert.That(RateLimitRetryPolicy.RetryAfterSeconds("Wednesday, 16-Oct-75 01:00:00 GMT", 1, new DateTimeOffset(2075, 10, 16, 0, 0, 0, TimeSpan.Zero)), Is.EqualTo(3600));
            Assert.That(RateLimitRetryPolicy.DelayMilliseconds(RateLimitRetryPolicy.RetryAfterSeconds(new string('9', 400), 1, now), 0), Is.Null);
            Assert.That(RateLimitRetryPolicy.DelayMilliseconds(10, 0), Is.EqualTo(10000)); Assert.That(RateLimitRetryPolicy.DelayMilliseconds(10, 1), Is.EqualTo(12000));
        }
        [TestCase("json")][TestCase("upload")][TestCase("download")][TestCase("get")]
        public async Task Account429WaitsAndRetriesAnImmutableBodyAndHeaders(string mode)
        {
            var payload = new Payload(); var bytes = new byte[] { 1, 2, 3 }; var headers = new Dictionary<string, string> { ["If-Match"] = "original-revision" };
            var incoming = _listener.GetContextAsync(); var pending = Send(mode, payload, bytes, headers); var first = await Timeout(incoming);
            byte[] original; using (var copy = new MemoryStream()) { await first.Request.InputStream.CopyToAsync(copy); original = copy.ToArray(); }
            payload.idempotencyId = "replacement"; payload.amount = 99; bytes[0] = 99; headers["If-Match"] = "replacement";
            incoming = _listener.GetContextAsync(); var watch = Stopwatch.StartNew(); await Respond(first, 429, "{}", "1");
            var second = await Timeout(incoming); Assert.That(watch.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(.95), "retry must not precede Retry-After");
            using (var copy = new MemoryStream()) { await second.Request.InputStream.CopyToAsync(copy); Assert.That(copy.ToArray(), Is.EqualTo(original)); }
            Assert.That(second.Request.Headers["Authorization"], Is.EqualTo("Bearer fixture-session"));
            if (mode != "get") Assert.That(second.Request.Headers["If-Match"], Is.EqualTo("original-revision"));
            if (mode == "json" || mode == "download") Assert.That(System.Text.Encoding.UTF8.GetString(original), Does.Contain("original-intent"));
            await Respond(second, 200, "{\"success\":true,\"message\":\"ok\"}"); Assert.That((await pending).success, Is.True);
        }
        [TestCase("json", "CUSTOM_LIMIT")][TestCase("upload", "CUSTOM_LIMIT")][TestCase("download", "CUSTOM_LIMIT")][TestCase("get", "CUSTOM_LIMIT")]
        [TestCase("json", "RUN_RATE_LIMITED")][TestCase("upload", "RUN_CAPACITY_REACHED")][TestCase("download", "INBOX_RATE_LIMITED")]
        public async Task EveryFeatureCodeReturnsImmediatelyWithOriginalStatusAndHeaders(string mode, string code)
        {
            var incoming = _listener.GetContextAsync(); var pending = Send(mode, new Payload(), new byte[] { 1 }, null);
            await Respond(await Timeout(incoming), 429, "{\"code\":\"" + code + "\",\"message\":\"Feature quota\"}", "3600");
            Assert.That(await Task.WhenAny(pending, Task.Delay(1500)), Is.SameAs(pending), "feature quota must not be retried or slept");
            var result = await pending; Assert.That(result.success, Is.False); Assert.That(result.status, Is.EqualTo(429)); Assert.That(result.code, Is.EqualTo(code)); Assert.That(result.retryAfter, Is.EqualTo("3600"));
            var unexpected = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(unexpected, Task.Delay(80)), Is.Not.SameAs(unexpected));
        }
        [TestCase("json")][TestCase("upload")][TestCase("download")][TestCase("get")]
        public async Task OversizedValidWaitReturnsWithoutAnEarlyReplacementRetry(string mode)
        {
            var incoming = _listener.GetContextAsync(); var pending = Send(mode, new Payload(), new byte[] { 1 }, null);
            await Respond(await Timeout(incoming), 429, "{}", new string('9', 400));
            Assert.That(await Task.WhenAny(pending, Task.Delay(500)), Is.SameAs(pending));
            var result = await pending; Assert.That(result.success, Is.False); Assert.That(result.status, Is.EqualTo(429));
            var unexpected = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(unexpected, Task.Delay(80)), Is.Not.SameAs(unexpected));
        }
        [TestCase("upload", "session")][TestCase("download", "session")][TestCase("get", "session")]
        [TestCase("upload", "host")][TestCase("download", "host")][TestCase("get", "host")]
        public async Task BinaryRetryCannotCrossAHostOrSessionSwitchAwayAndBack(string mode, string change)
        {
            var incoming = _listener.GetContextAsync(); var pending = Send(mode, new Payload(), new byte[] { 1 }, null); var first = await Timeout(incoming);
            if (change == "session") { NetworkService.Instance.SetSessionToken("other"); NetworkService.Instance.SetSessionToken("fixture-session"); }
            else { NetworkService.Instance.SetActiveHost("http://other.invalid"); NetworkService.Instance.SetActiveHost(Host); }
            await Respond(first, 503, "{}"); var result = await pending; Assert.That(result.success, Is.False); Assert.That(result.code, Is.EqualTo("SESSION_CHANGED"));
            var unexpected = _listener.GetContextAsync(); Assert.That(await Task.WhenAny(unexpected, Task.Delay(80)), Is.Not.SameAs(unexpected));
        }
        private static async Task<T> Timeout<T>(Task<T> task)
        { if (await Task.WhenAny(task, Task.Delay(6000)) != task) throw new TimeoutException("Retry loopback request timed out"); return await task; }
        private static async Task Respond(HttpListenerContext context, int status, string body, string retryAfter = null)
        { context.Response.StatusCode = status; context.Response.ContentType = "application/json"; if (retryAfter != null) context.Response.Headers["Retry-After"] = retryAfter; using (var writer = new StreamWriter(context.Response.OutputStream)) await writer.WriteAsync(body); context.Response.Close(); }
    }
}
