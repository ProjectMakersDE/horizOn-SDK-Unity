using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Service;
using UnityEngine;
using UnityEngine.Networking;

namespace PM.horizOn.Cloud.Tests
{
    [Category("Transport")]
    public class RealtimeTransportTests
    {
        [Test]
        public void ReconnectBackoffIsBoundedAndChatNeverPolls()
        {
            Assert.That(RealtimeManager.ReconnectDelay(0, .5), Is.EqualTo(.5));
            Assert.That(RealtimeManager.ReconnectDelay(100, 1), Is.EqualTo(30));
            Assert.That(RealtimeManager.CanPoll("chat:channel"), Is.False);
            Assert.That(RealtimeManager.CanPoll("inbox:player"), Is.True);
        }
        [Test]
        public void SubscriptionsAreValidatedAndBounded()
        {
            var gameObject = new GameObject("RealtimePolicyTest");
            try
            {
                var manager = gameObject.AddComponent<RealtimeManager>();
                Assert.That(manager.Subscribe("chat:bad/path"), Is.False);
                for (int i = 0; i < 32; i++) Assert.That(manager.Subscribe("matches:" + i), Is.True);
                Assert.That(manager.Subscribe("matches:overflow"), Is.False);
                Assert.That(manager.Subscribe("matches:0"), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(gameObject); }
        }
        [Test]
        public void LateUnsubscribeAcknowledgementAllowsResubscription()
        {
            var gameObject = new GameObject("RealtimeAcknowledgementTest");
            try
            {
                var manager = gameObject.AddComponent<RealtimeManager>();
                manager.Subscribe("matches:game"); manager.Subscribe("matches:other");
                var sent = (HashSet<string>)typeof(RealtimeManager).GetField("_sent", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
                var handle = typeof(RealtimeManager).GetMethod("Handle", BindingFlags.NonPublic | BindingFlags.Instance);
                typeof(RealtimeManager).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, "matches:game");
                manager.Unsubscribe("matches:game"); manager.Subscribe("matches:game");
                handle.Invoke(manager, new object[] { "{\"type\":\"subscribed\",\"version\":1,\"topic\":\"matches:game\"}" });
                handle.Invoke(manager, new object[] { "{\"type\":\"unsubscribed\",\"version\":1,\"topic\":\"matches:game\"}" });
                Assert.That(sent.Contains("matches:game"), Is.False, "The next update must resubscribe the still-desired topic.");
                typeof(RealtimeManager).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, "matches:game");
                typeof(RealtimeManager).GetField("_pendingRequestId", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, 3L);
                typeof(RealtimeManager).GetField("_pendingCanceled", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, false);
                handle.Invoke(manager, new object[] { "{\"type\":\"unsubscribed\",\"version\":1,\"topic\":\"matches:game\",\"requestId\":2}" });
                Assert.That(typeof(RealtimeManager).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager), Is.EqualTo("matches:game"));
                handle.Invoke(manager, new object[] { "{\"type\":\"subscribed\",\"version\":1,\"topic\":\"matches:game\",\"requestId\":3}" });
                Assert.That(sent.Contains("matches:game"), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(gameObject); }
        }
        [Test]
        public async Task NativeWebSocketAuthenticatesSubscribesAndReceivesInvalidation()
        {
            string host = Environment.GetEnvironmentVariable("HORIZON_REALTIME_FIXTURE_URL");
            if (string.IsNullOrEmpty(host)) Assert.Ignore("Run locally with the Server realtime-gateway/test/sdk-fixture.js and HORIZON_REALTIME_FIXTURE_URL.");
            PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
            NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            var config = ScriptableObject.CreateInstance<HorizonConfig>();
            config.SetApiKey("fixture-key"); config.SetHosts(new[] { host });
            NetworkService.Instance.Initialize(config); NetworkService.Instance.SetActiveHost(host);
            NetworkService.Instance.SetSessionToken("fixture-session");
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false;
            typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(UserManager.Instance,
                new UserData { UserId = "00000000-0000-4000-8000-000000000003", AccessToken = "fixture-session" });
            typeof(HorizonApp).GetProperty("IsInitialized").SetValue(null, true);
            var manager = RealtimeManager.Instance;
            var update = typeof(RealtimeManager).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            RealtimeFrame received = null; int resync = 0;
            manager.EventReceived += value => received = value;
            manager.ResyncRequired += _ => resync++;
            string topic = "inbox:00000000-0000-4000-8000-000000000003";
            try
            {
                manager.Subscribe(topic);
                var sent = (HashSet<string>)typeof(RealtimeManager).GetField("_sent", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (!sent.Contains(topic) && DateTime.UtcNow < deadline) { update.Invoke(manager, null); await Task.Delay(20); }
                Assert.That(manager.IsReady, Is.True, manager.LastErrorCode);
                Assert.That(sent.Contains(topic), Is.True, manager.LastErrorCode);
                using (var request = UnityWebRequest.Get(host + "/fixture/event"))
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone) await Task.Delay(10);
                    Assert.That(request.responseCode, Is.EqualTo(200));
                }
                while (received == null && DateTime.UtcNow < deadline) { update.Invoke(manager, null); await Task.Delay(20); }
                Assert.That(received, Is.Not.Null);
                Assert.That(received.revision, Is.EqualTo(7));
                Assert.That(received.resourceId, Is.EqualTo("fixture-entry"));
                Assert.That(resync, Is.GreaterThan(0));
                Assert.That(manager.PollAfterSeconds, Is.EqualTo(2));
                // A login callback can subscribe before the next Update observes the account change.
                typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(UserManager.Instance,
                    new UserData { UserId = "00000000-0000-4000-8000-000000000099", AccessToken = "new-session" });
                NetworkService.Instance.SetSessionToken("new-session");
                manager.Subscribe("matches:new-user");
                var topics = (HashSet<string>)typeof(RealtimeManager).GetField("_topics", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
                Assert.That(topics.Contains(topic), Is.False);
                Assert.That(topics.Contains("matches:new-user"), Is.True);
                manager.Close(); Assert.That(manager.IsReady, Is.False);
            }
            finally
            {
                manager.Close(); UnityEngine.Object.DestroyImmediate(manager.gameObject);
                UnityEngine.Object.DestroyImmediate(UserManager.Instance.gameObject);
                UnityEngine.Object.DestroyImmediate(config);
                typeof(HorizonApp).GetProperty("IsInitialized").SetValue(null, false);
                PlayerPrefs.DeleteKey("horizOn_UserSession"); PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
            }
        }
    }
}
