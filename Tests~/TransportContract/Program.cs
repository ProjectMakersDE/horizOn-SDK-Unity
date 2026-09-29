using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PM.horizOn.Cloud.Objects.Data;
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

// Gift code redeem (TASK-886): the plan carries the session, and no plan exists without a matching session.
if (!GiftCodeTransportContract.TryCreateRedeemPlan(user, "session-token-720", "SUMMER2026", out var redeemPlan))
{
    throw new InvalidOperationException("signed user did not produce a redeem plan");
}
Require(GiftCodeRedeemPlan.Endpoint == "/api/v1/app/gift-codes/redeem", "redeem endpoint");
Require(redeemPlan.UseSessionToken, "redeem uses the session token");
Require(redeemPlan.Request.userId == "user-720", "redeem userId body field");
Require(redeemPlan.Request.code == "SUMMER2026", "redeem code body field");
var redeemHeaders = HorizonRequestHeaders.Create("project-key-720", "session-token-720", redeemPlan.UseSessionToken);
Require(redeemHeaders.TryGetValue("Authorization", out var redeemAuthorization) &&
    redeemAuthorization == "Bearer session-token-720", "redeem authorization");
Require(!GiftCodeTransportContract.TryCreateRedeemPlan(
    new UserData(), "session-token-720", "SUMMER2026", out _), "redeem missing user session gate");
Require(!GiftCodeTransportContract.TryCreateRedeemPlan(
    user, "stale-token", "SUMMER2026", out _), "redeem current transport session gate");
Require(!GiftCodeTransportContract.TryCreateRedeemPlan(
    user, "session-token-720", "", out _), "redeem empty code gate");

Console.WriteLine("Unity SDK leaderboard and gift code transport contract passed");

static void Require(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"transport contract failed: {name}");
    }
}
