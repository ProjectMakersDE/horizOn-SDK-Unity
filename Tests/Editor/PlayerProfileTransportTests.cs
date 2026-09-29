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
    /// Player profile (TASK-881): both endpoints need the player's Bearer session, SetProfile
    /// sends a PUT with the whole profile, and server error codes reach LastErrorCode.
    /// </summary>
    [Category("Transport")]
    public class PlayerProfileTransportTests
    {
        private const string Host = "http://127.0.0.1:18881";

        private const string ProfileBody =
            "{\"userId\":\"user-881\"," +
            "\"profile\":{\"avatarId\":\"avatar.zombie_07\",\"frameId\":null,\"badges\":[\"badge.supporter\"]}," +
            "\"unlocks\":[\"badge.supporter\"]," +
            "\"cosmetics\":[" +
            "{\"id\":\"avatar.zombie_07\",\"type\":\"avatar\",\"locked\":false,\"available\":true}," +
            "{\"id\":\"badge.supporter\",\"type\":\"badge\",\"locked\":true,\"available\":true}," +
            "{\"id\":\"frame.gold\",\"type\":\"frame\",\"locked\":true,\"available\":false}]," +
            "\"limits\":{\"maxBadges\":3,\"maxUnlocks\":25}}";

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
            config.SetApiKey("project-key-881");
            config.SetHosts(new[] { Host });

            NetworkService.Instance.Initialize(config);
            NetworkService.Instance.SetActiveHost(Host);
            NetworkService.Instance.SetSessionToken("session-token-881");
            LogService.Instance.EnableUnityLogging = false;
            LogService.Instance.EnableEventPublishing = false;

            SetCurrentUser(new UserData
            {
                UserId = "user-881",
                AccessToken = "session-token-881"
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
                    gameObject.name == "[PlayerProfileManager]" ||
                    gameObject.name == "[GiftCodeManager]")
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
        }

        [Test]
        public async Task GetProfile_SendsSessionBoundGet_AndCachesResult()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<PlayerProfileResponse> loading = PlayerProfileManager.Instance.GetProfile();

            HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            Assert.That(context.Request.HttpMethod, Is.EqualTo("GET"));
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/player-profile?userId=user-881"));
            Assert.That(context.Request.Headers["X-API-Key"], Is.EqualTo("project-key-881"));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer session-token-881"));

            await Respond(context, 200, ProfileBody);
            PlayerProfileResponse profile = await loading;

            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.userId, Is.EqualTo("user-881"));
            Assert.That(profile.profile.avatarId, Is.EqualTo("avatar.zombie_07"));
            Assert.That(profile.profile.HasAvatar, Is.True);
            Assert.That(profile.profile.HasFrame, Is.False, "JSON null must read as 'not set'");
            Assert.That(profile.profile.badges, Is.EqualTo(new[] { "badge.supporter" }));
            Assert.That(profile.unlocks, Is.EqualTo(new[] { "badge.supporter" }));
            Assert.That(profile.limits.maxBadges, Is.EqualTo(3));
            Assert.That(profile.limits.maxUnlocks, Is.EqualTo(25));
            Assert.That(profile.GetCosmetics("frame").Count, Is.EqualTo(1));
            Assert.That(profile.IsAvailable("badge.supporter"), Is.True);
            Assert.That(profile.IsAvailable("frame.gold"), Is.False);
            Assert.That(profile.IsAvailable("unknown.id"), Is.False);

            Assert.That(PlayerProfileManager.Instance.CurrentProfile, Is.SameAs(profile));
            Assert.That(PlayerProfileManager.Instance.LastErrorCode, Is.Null);

            // Sign-out (or another player) drops the cached profile.
            SetCurrentUser(new UserData());
            Assert.That(PlayerProfileManager.Instance.CurrentProfile, Is.Null);
        }

        [Test]
        public async Task SetProfile_SendsPutWithWholeProfile_AndExposesServerErrorCode()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<PlayerProfileResponse> saving = PlayerProfileManager.Instance.SetProfile(
                "avatar.zombie_07", "", new[] { "badge.supporter" });

            HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            string body = await ReadBody(context);

            Assert.That(context.Request.HttpMethod, Is.EqualTo("PUT"));
            Assert.That(context.Request.RawUrl, Is.EqualTo("/api/v1/app/player-profile"));
            Assert.That(context.Request.Headers["X-API-Key"], Is.EqualTo("project-key-881"));
            Assert.That(context.Request.Headers["Authorization"], Is.EqualTo("Bearer session-token-881"));
            Assert.That(context.Request.ContentType, Does.StartWith("application/json"));

            Assert.That(body, Does.Contain("\"badges\":[\"badge.supporter\"]"), body);
            Assert.That(body, Does.Not.Contain("frameId"), "a cleared slot is left out, the server clears it");
            var request = JsonUtility.FromJson<SetPlayerProfileRequest>(body);
            Assert.That(request.userId, Is.EqualTo("user-881"));
            Assert.That(request.avatarId, Is.EqualTo("avatar.zombie_07"));
            Assert.That(request.badges, Is.EqualTo(new[] { "badge.supporter" }));

            await Respond(context, 403,
                "{\"status\":403,\"error\":\"Forbidden\",\"code\":\"COSMETIC_LOCKED\"," +
                "\"message\":\"Cosmetic 'frame.gold' is locked for this player\",\"path\":\"/api/v1/app/player-profile\"}");

            Assert.That(await saving, Is.Null);
            Assert.That(PlayerProfileManager.Instance.LastErrorCode, Is.EqualTo(PlayerProfileErrorCodes.CosmeticLocked));
        }

        [Test]
        public async Task SetProfile_WithEmptyBadges_SendsEmptyArray()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<PlayerProfileResponse> saving = PlayerProfileManager.Instance.SetProfile(null, null, null);

            HttpListenerContext context = await WithTimeout(incoming, TimeSpan.FromSeconds(5));
            string body = await ReadBody(context);

            Assert.That(body, Does.Contain("\"userId\":\"user-881\""), body);
            Assert.That(body, Does.Contain("\"badges\":[]"), body);
            Assert.That(body, Does.Not.Contain("avatarId"), body);

            await Respond(context, 200, ProfileBody);
            Assert.That(await saving, Is.Not.Null);
        }

        [Test]
        public async Task ProfileCalls_WithoutSessionOrInvalidInput_FailLocallyWithoutRequest()
        {
            Task<HttpListenerContext> unexpectedRequest = _listener.GetContextAsync();

            Assert.That(await PlayerProfileManager.Instance.SetProfile(
                null, null, new[] { "badge.a", "badge.b", "badge.c", "badge.d" }), Is.Null);
            Assert.That(PlayerProfileManager.Instance.LastErrorCode, Is.EqualTo(PlayerProfileErrorCodes.InvalidBadges));

            Assert.That(await PlayerProfileManager.Instance.SetProfile("Not An ID", null, null), Is.Null);
            Assert.That(PlayerProfileManager.Instance.LastErrorCode, Is.EqualTo(PlayerProfileErrorCodes.InvalidCosmeticId));

            SetCurrentUser(new UserData());
            Assert.That(await PlayerProfileManager.Instance.GetProfile(), Is.Null);
            Assert.That(PlayerProfileManager.Instance.LastErrorCode, Is.EqualTo(PlayerProfileErrorCodes.SessionRequired));
            Assert.That(await PlayerProfileManager.Instance.SetProfile("avatar.zombie_07", null, null), Is.Null);
            Assert.That(PlayerProfileManager.Instance.LastErrorCode, Is.EqualTo(PlayerProfileErrorCodes.SessionRequired));

            Task completed = await Task.WhenAny(unexpectedRequest, Task.Delay(350));
            Assert.That(completed, Is.Not.SameAs(unexpectedRequest),
                "A player profile call sent a request although a local check failed");
        }

        [Test]
        public async Task Redeem_WithGrantedUnlocks_DropsCachedProfile()
        {
            Task<HttpListenerContext> incoming = _listener.GetContextAsync();
            Task<PlayerProfileResponse> loading = PlayerProfileManager.Instance.GetProfile();
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 200, ProfileBody);
            Assert.That(await loading, Is.Not.Null);
            Assert.That(PlayerProfileManager.Instance.CurrentProfile, Is.Not.Null);

            incoming = _listener.GetContextAsync();
            Task<RedeemGiftCodeResponse> redemption = GiftCodeManager.Instance.Redeem("FRAME2026");
            await Respond(await WithTimeout(incoming, TimeSpan.FromSeconds(5)), 200,
                "{\"success\":true,\"message\":\"ok\",\"giftData\":\"{}\",\"grantedUnlocks\":[\"frame.gold\"]}");

            RedeemGiftCodeResponse result = await redemption;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.grantedUnlocks, Is.EqualTo(new[] { "frame.gold" }));
            Assert.That(PlayerProfileManager.Instance.CurrentProfile, Is.Null);
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
                throw new TimeoutException("The local player profile contract server did not receive a request in time.");
            }
            return await task;
        }
    }
}
