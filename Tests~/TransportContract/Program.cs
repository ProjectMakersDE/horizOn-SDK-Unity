using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Service;
using UnityEngine;
using PM.horizOn.Cloud.Transport;

const string host = "http://127.0.0.1:18722/";
using var listener = new HttpListener();
listener.Prefixes.Add(host);
listener.Start();

var user = new UserData { UserId = "user-720", AccessToken = "session-token-720" };
if (!LeaderboardTransportContract.TryCreateSubmitPlan(
        user, "session-token-720", 4242, "season one", out var plan))
{
    throw new InvalidOperationException("signed user did not produce a submit plan");
}

using var client = new HttpClient { BaseAddress = new Uri(host) };
using var message = new HttpRequestMessage(HttpMethod.Post, plan.Endpoint.TrimStart('/'));
foreach (var header in HorizonRequestHeaders.Create("project-key-720", "session-token-720", plan.UseSessionToken))
{
    message.Headers.TryAddWithoutValidation(header.Key, header.Value);
}
message.Content = JsonContent.Create(plan.Request, options: new JsonSerializerOptions { IncludeFields = true });

Task<HttpListenerContext> incoming = listener.GetContextAsync();
Task<HttpResponseMessage> outgoing = client.SendAsync(message);
HttpListenerContext context = await incoming.WaitAsync(TimeSpan.FromSeconds(5));
using var reader = new StreamReader(context.Request.InputStream);
string body = await reader.ReadToEndAsync();

Require(context.Request.HttpMethod == "POST", "method");
Require(context.Request.RawUrl == "/api/v1/app/leaderboards/season%20one/submit", "endpoint");
Require(context.Request.Headers["X-API-Key"] == "project-key-720", "project key");
Require(context.Request.Headers["Authorization"] == "Bearer session-token-720", "authorization");
Require(body.Contains("\"userId\":\"user-720\""), "userId body field");
Require(body.Contains("\"score\":4242"), "score body field");
Require(body.Contains("\"leaderboardKey\":\"season one\""), "board body field");

context.Response.StatusCode = 200;
context.Response.Close();
(await outgoing).EnsureSuccessStatusCode();

Require(!LeaderboardTransportContract.TryCreateSubmitPlan(
    new UserData(), "session-token-720", 99, "season one", out _), "missing user session gate");
Require(!LeaderboardTransportContract.TryCreateSubmitPlan(
    user, "stale-token", 99, "season one", out _), "current transport session gate");

var signup = SignUpRequest.CreateAnonymous("Player");
Require(signup.type == "ANONYMOUS" && signup.username == "Player", "anonymous signup identity");
Require(signup.anonymousToken == null, "anonymous signup must let the server issue the token");
Require(!JsonHelper.ToJsonExcludeEmpty(signup).Contains("anonymousToken"), "serialized signup omits client token");
Require(SignUpRequest.CreateAnonymous("Player", "legacy-client-token").anonymousToken == null,
    "legacy factory argument cannot override server-issued token");

PlayerPrefs.DeleteAll();
var authTransport = new NetworkService();
HorizonApp.Network = authTransport;
int signins = 0;
authTransport.Respond = (endpoint, request) =>
{
    if (endpoint.EndsWith("/signup"))
    {
        var sent = (SignUpRequest)request;
        Require(sent.type == "ANONYMOUS" && sent.username == "Player", "manager signup fields");
        Require(sent.anonymousToken == null, "manager must omit client anonymous token");
        return NetworkResponse<AuthResponse>.Success(new AuthResponse
        {
            userId = "user-721",
            username = "Player",
            isAnonymous = true,
            anonymousToken = "server-issued-token"
        }, 201);
    }

    var signin = (SignInRequest)request;
    Require(signin.type == "ANONYMOUS", "anonymous signin type");
    Require(signin.anonymousToken == "server-issued-token", "signin uses server-issued token");
    signins++;
    return NetworkResponse<AuthResponse>.Success(new AuthResponse
    {
        userId = "user-721",
        username = "Player",
        authStatus = "AUTHENTICATED",
        accessToken = $"session-72{signins}"
        // The server does not send isAnonymous or anonymousToken on signin.
    });
};

var manager = new UserManager();
manager.Init();
Require(await manager.SignUpAnonymous("Player"), "signup establishes a session");
Require(manager.IsSignedIn, "manager is signed in after signup");
Require(manager.CurrentUser.IsAnonymous && manager.CurrentUser.AuthType == "ANONYMOUS", "signin retains anonymous identity");
Require(manager.CurrentUser.AnonymousToken == "server-issued-token", "signin retains server token");
Require(PlayerPrefs.GetString("horizOn_AnonymousToken") == "server-issued-token", "server token is cached");

var staleAuthResponse = new TaskCompletionSource<object>();
string checkedToken = "session-721";
int authChecks = 0;
authTransport.RespondAsync = (endpoint, request) =>
{
    if (endpoint.EndsWith("/check-auth"))
    {
        var check = (CheckAuthRequest)request;
        Require(check.userId == "user-721" && check.sessionToken == checkedToken,
            "cached session check uses original token");
        authChecks++;
        return staleAuthResponse.Task;
    }

    return Task.FromResult(authTransport.Respond(endpoint, request));
};

var restoredManager = new UserManager();
restoredManager.Init();
Require(authChecks == 1, "cached session starts an auth check");
Task<bool> secondOldCheck = restoredManager.CheckAuth();
Require(authChecks == 2, "explicit auth check awaits the same old token");
Require(await restoredManager.RestoreAnonymousSession(), "cached token restores a session");
Require(restoredManager.IsSignedIn, "restored manager is signed in");
Require(restoredManager.CurrentUser.UserId == manager.CurrentUser.UserId, "restore retains the user ID");
Require(restoredManager.CurrentUser.AnonymousToken == manager.CurrentUser.AnonymousToken, "restore retains the token");
Require(restoredManager.CurrentUser.AccessToken == "session-722", "restore replaces cached access token");

HorizonApp.Events.Clear();
staleAuthResponse.SetResult(NetworkResponse<CheckAuthResponse>.Failure("AUTH_EXPIRED", 401));
Require(!await secondOldCheck, "stale auth check is rejected");
Require(restoredManager.IsSignedIn && restoredManager.CurrentUser.AccessToken == "session-722",
    "late old-session 401 does not sign out the fresh session");
Require(authTransport.SessionToken == "session-722", "late 401 preserves transport session");
Require(PlayerPrefs.HasKey("horizOn_UserSession"), "late 401 preserves cached fresh session");
Require(!HorizonApp.Events.WasPublished(EventKeys.UserAuthCheckFailed)
    && !HorizonApp.Events.WasPublished(EventKeys.UserAuthCheckSuccess)
    && !HorizonApp.Events.WasPublished(EventKeys.UserSignOutSuccess),
    "late 401 emits no stale auth or signout events");

checkedToken = "session-722";
staleAuthResponse = new TaskCompletionSource<object>();
Task<bool> oldSuccessfulCheck = restoredManager.CheckAuth();
Require(await restoredManager.RestoreAnonymousSession(), "second restore establishes another session");
HorizonApp.Events.Clear();
staleAuthResponse.SetResult(NetworkResponse<CheckAuthResponse>.Success(new CheckAuthResponse
{
    userId = "user-721",
    isAuthenticated = true
}));
Require(!await oldSuccessfulCheck, "stale success is rejected");
Require(restoredManager.CurrentUser.AccessToken == "session-723", "late success preserves newest session");
Require(!HorizonApp.Events.WasPublished(EventKeys.UserAuthCheckSuccess),
    "late success emits no stale auth event");

Console.WriteLine("Unity SDK leaderboard transport contract passed");
Console.WriteLine("Unity SDK anonymous session contract passed");

static void Require(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"transport contract failed: {name}");
    }
}
