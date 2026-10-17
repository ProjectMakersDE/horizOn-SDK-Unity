using System;
using System.IO;
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
    public class ChatSocketTests
    {
        const string User = "00000000-0000-4000-8000-000000001014", Channel = "00000000-0000-4000-8000-000000003014";
        [Test] public async Task NativeSocketChangesOnlyCurrentWindowAndNeverDeletesDeviceHistory()
        {
            string host = Environment.GetEnvironmentVariable("HORIZON_CHAT_FIXTURE_URL");
            if (string.IsNullOrEmpty(host)) Assert.Ignore("Run Tests~/ChatSocketFixture/mock_chat_server.py and set HORIZON_CHAT_FIXTURE_URL and HORIZON_CHAT_FIXTURE_WS.");
            PlayerPrefs.DeleteKey("horizOn_UserSession"); NetworkService.ResetInstance(); LogService.ResetInstance(); EventService.ResetInstance();
            var config = ScriptableObject.CreateInstance<HorizonConfig>(); config.SetApiKey("chat-key"); config.SetHosts(new[] { host });
            NetworkService.Instance.Initialize(config); NetworkService.Instance.SetActiveHost(host); NetworkService.Instance.SetSessionToken("chat-session");
            LogService.Instance.EnableUnityLogging = false; LogService.Instance.EnableEventPublishing = false;
            typeof(UserManager).GetField("_currentUser", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(UserManager.Instance, new UserData { UserId = User, AccessToken = "chat-session" });
            typeof(HorizonApp).GetProperty("IsInitialized").SetValue(null, true);
            var realtime = RealtimeManager.Instance; realtime.GatewayUrl = Environment.GetEnvironmentVariable("HORIZON_CHAT_FIXTURE_WS");
            var chat = ChatManager.Instance; string root = Path.Combine(Path.GetTempPath(), "horizon-chat-socket-" + Guid.NewGuid().ToString("N")); chat.HistoryRootOverride = root;
            var update = typeof(RealtimeManager).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            var chatUpdate = typeof(ChatManager).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            void Tick() { update.Invoke(realtime, null); chatUpdate.Invoke(chat, null); }
            async Task Until(Func<bool> condition)
            {
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (!condition() && DateTime.UtcNow < deadline) { Tick(); await Task.Delay(20); }
                Assert.That(condition(), Is.True, "Chat WS bounded wait: " + chat.LastErrorCode + "/" + realtime.LastErrorCode);
            }
            async Task<string> Control(string action)
            {
                using (var r = UnityWebRequest.Get(host + "/fixture/" + action))
                { var op = r.SendWebRequest(); while (!op.isDone) await Task.Delay(10); Assert.That(r.responseCode, Is.EqualTo(200)); return r.downloadHandler.text; }
            }
            try
            {
                Assert.That(chat.WatchChannel(User, Channel), Is.True);
                await Until(() => realtime.IsSubscribed("chat:" + Channel));
                var sent = await chat.SendMessage(User, Channel, "00000000-0000-4000-8000-000000005014", "2026-10-16T12:00:00.000Z", "first local message");
                Assert.That(sent.IsSuccess, Is.True, sent.ErrorCode); await Until(() => chat.GetFeed(Channel).Length == 1);
                await Control("bulk"); await Until(() => chat.GetFeed(Channel).Length == 20);
                Assert.That(chat.ReadLocalHistory(Channel).Data.messages.Length, Is.EqualTo(20));
                await Control("shift"); await Until(() => chat.ReadLocalHistory(Channel).Data.messages.Length == 21);
                Assert.That(chat.GetFeed(Channel).Length, Is.EqualTo(20));
                await Control("bad"); await Until(() => chat.GetFeed(Channel).Length == 0);
                Assert.That(chat.ReadLocalHistory(Channel).Data.messages.Length, Is.EqualTo(21));
                await Control("restore"); await Until(() => chat.ReadLocalHistory(Channel).Data.messages.Length == 22);
                await Control("burst"); for (int i = 0; i < 60; i++) { Tick(); await Task.Delay(20); }
                string status = await Control("status"); Assert.That(status, Does.Contain("\"failure\": null"));
                await Control("disconnect"); await Until(() => chat.GetFeed(Channel).Length == 0);
                Assert.That(chat.ReadLocalHistory(Channel).Data.messages.Length, Is.EqualTo(22));
                realtime.Close(); chat.UnwatchChannel(Channel);
                Assert.That(chat.ClearAllLocalHistory(), Is.True); Assert.That(chat.ReadLocalHistory(Channel).Data.messages, Is.Empty);
                Debug.Log("CHAT_REAL_SOCKET_STATUS " + status);
            }
            finally
            {
                await Control("finish"); realtime.Close(); UnityEngine.Object.DestroyImmediate(chat.gameObject); UnityEngine.Object.DestroyImmediate(realtime.gameObject);
                UnityEngine.Object.DestroyImmediate(UserManager.Instance.gameObject); UnityEngine.Object.DestroyImmediate(config);
                typeof(HorizonApp).GetProperty("IsInitialized").SetValue(null, false); PlayerPrefs.DeleteKey("horizOn_UserSession");
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
