using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;
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

// Player profile (TASK-881): GET and PUT carry the session; PUT sends the whole profile.
if (!PlayerProfileTransportContract.TryCreateGetPlan(user, "session-token-720", out var profileGetPlan))
{
    throw new InvalidOperationException("signed user did not produce a profile get plan");
}
Require(profileGetPlan.Endpoint == "/api/v1/app/player-profile?userId=user-720", "profile get endpoint");
Require(profileGetPlan.UseSessionToken, "profile get uses the session token");
Require(!PlayerProfileTransportContract.TryCreateGetPlan(new UserData(), "session-token-720", out _), "profile get missing user session gate");
Require(!PlayerProfileTransportContract.TryCreateGetPlan(user, "stale-token", out _), "profile get current transport session gate");

if (!PlayerProfileTransportContract.TryCreateSetPlan(
        user, "session-token-720", " avatar.zombie_07 ", "", new[] { "badge.supporter", "badge.early-bird" },
        out var profileSetPlan, out var profileSetError))
{
    throw new InvalidOperationException($"signed user did not produce a profile set plan: {profileSetError}");
}
Require(profileSetError == null, "profile set error code is null on success");
Require(profileSetPlan.UseSessionToken, "profile set uses the session token");

using var profileMessage = new HttpRequestMessage(HttpMethod.Put, PlayerProfileSetPlan.Endpoint.TrimStart('/'));
foreach (var header in HorizonRequestHeaders.Create("project-key-720", "session-token-720", profileSetPlan.UseSessionToken))
{
    profileMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
}
profileMessage.Content = JsonContent.Create(profileSetPlan.Request, options: new JsonSerializerOptions { IncludeFields = true });

Task<HttpListenerContext> profileIncoming = listener.GetContextAsync();
Task<HttpResponseMessage> profileOutgoing = client.SendAsync(profileMessage);
HttpListenerContext profileContext = await profileIncoming.WaitAsync(TimeSpan.FromSeconds(5));
using var profileReader = new StreamReader(profileContext.Request.InputStream);
string profileBody = await profileReader.ReadToEndAsync();

Require(profileContext.Request.HttpMethod == "PUT", "profile set method");
Require(profileContext.Request.RawUrl == "/api/v1/app/player-profile", "profile set endpoint");
Require(profileContext.Request.Headers["X-API-Key"] == "project-key-720", "profile set project key");
Require(profileContext.Request.Headers["Authorization"] == "Bearer session-token-720", "profile set authorization");
Require(profileBody.Contains("\"userId\":\"user-720\""), "profile set userId body field");
Require(profileBody.Contains("\"avatarId\":\"avatar.zombie_07\""), "profile set trimmed avatarId body field");
Require(profileBody.Contains("\"frameId\":null"), "profile set cleared frame slot");
Require(profileBody.Contains("\"badges\":[\"badge.supporter\",\"badge.early-bird\"]"), "profile set badges in order");

profileContext.Response.StatusCode = 200;
profileContext.Response.Close();
(await profileOutgoing).EnsureSuccessStatusCode();

Require(PlayerProfileTransportContract.TryCreateSetPlan(user, "session-token-720", null, null, null, out var clearPlan, out _) &&
    clearPlan.Request.avatarId == null && clearPlan.Request.frameId == null && clearPlan.Request.badges.Length == 0,
    "profile set clears every slot");
RequireSetError(new UserData(), "session-token-720", "avatar.a", null, null, PlayerProfileErrorCodes.SessionRequired, "profile set missing user session gate");
RequireSetError(user, "stale-token", "avatar.a", null, null, PlayerProfileErrorCodes.SessionRequired, "profile set current transport session gate");
RequireSetError(user, "session-token-720", null, null, new[] { "b.1", "b.2", "b.3", "b.4" }, PlayerProfileErrorCodes.InvalidBadges, "profile set more than 3 badges");
RequireSetError(user, "session-token-720", null, null, new[] { "b.1", "b.1" }, PlayerProfileErrorCodes.InvalidBadges, "profile set duplicate badge");
RequireSetError(user, "session-token-720", "Avatar.Upper", null, null, PlayerProfileErrorCodes.InvalidCosmeticId, "profile set uppercase id");
RequireSetError(user, "session-token-720", null, "-frame", null, PlayerProfileErrorCodes.InvalidCosmeticId, "profile set id starting with a dash");
RequireSetError(user, "session-token-720", null, null, new[] { new string('a', 33) }, PlayerProfileErrorCodes.InvalidCosmeticId, "profile set id longer than 32");
Require(PlayerProfileTransportContract.IsValidCosmeticId(new string('a', 32)), "profile id of 32 characters is valid");
Require(!PlayerProfileTransportContract.IsValidCosmeticId("avatar.a\n"), "profile id with a trailing newline is invalid");
Require(PlayerProfileErrorCodes.FromHttpStatus(429) == PlayerProfileErrorCodes.RateLimited, "profile 429 fallback code");
Require(PlayerProfileErrorCodes.FromHttpStatus(0) == PlayerProfileErrorCodes.NetworkError, "profile network fallback code");

// Validated Actions Part 1 (TASK-883): start and submit carry the session, the submit sends the
// ticket of the current run with the lower case SHA-256 of the input log.
const string abcHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
Require(ValidatedActionsTransportContract.ComputeInputLogHash(System.Text.Encoding.ASCII.GetBytes("abc")) == abcHash, "input log hash is lower case SHA-256");
Require(ValidatedActionsTransportContract.ComputeInputLogHash(null) ==
    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "null input log hashes like an empty log");
Require(ValidatedActionsTransportContract.IsValidInputLogHash(abcHash.ToUpperInvariant()), "upper case hash is accepted");
Require(!ValidatedActionsTransportContract.IsValidInputLogHash(abcHash + "\n"), "hash with a trailing newline is invalid");

if (!ValidatedActionsTransportContract.TryCreateStartRunPlan(user, "session-token-720", " weekly ", out var startPlan, out var startError))
{
    throw new InvalidOperationException($"signed user did not produce a start run plan: {startError}");
}
Require(ValidatedStartRunPlan.Endpoint == "/api/v1/app/validated-actions/runs", "start run endpoint");
Require(startPlan.UseSessionToken, "start run uses the session token");
Require(startPlan.Request.userId == "user-720" && startPlan.Request.leaderboardKey == "weekly", "start run body fields");
Require(ValidatedActionsTransportContract.TryCreateStartRunPlan(user, "session-token-720", "  ", out var unboundPlan, out _) &&
    unboundPlan.Request.leaderboardKey == null, "blank board key leaves the run unbound");
Require(!ValidatedActionsTransportContract.TryCreateStartRunPlan(new UserData(), "session-token-720", null, out _, out var noSessionCode) &&
    noSessionCode == ValidatedActionsErrorCodes.SessionRequired, "start run missing user session gate");
Require(!ValidatedActionsTransportContract.TryCreateStartRunPlan(user, "stale-token", null, out _, out _), "start run current transport session gate");

var currentRun = new ValidatedRun { runId = "run-720", ticket = "hzn-rt1:2026-09:abc:def", seed = 42, leaderboardKey = "weekly" };
if (!ValidatedActionsTransportContract.TryCreateSubmitPlan(
        user, "session-token-720", currentRun, 18250, abcHash.ToUpperInvariant(), "wave_10", "",
        new[] { new EarnedValue("gold", 250), null }, out var submitPlan, out var submitError))
{
    throw new InvalidOperationException($"signed user did not produce a submit plan: {submitError}");
}
Require(ValidatedSubmitPlan.Endpoint == "/api/v1/app/validated-actions/submit", "submit endpoint");
Require(submitPlan.UseSessionToken, "submit uses the session token");

using var submitMessage = new HttpRequestMessage(HttpMethod.Post, ValidatedSubmitPlan.Endpoint.TrimStart('/'));
foreach (var header in HorizonRequestHeaders.Create("project-key-720", "session-token-720", submitPlan.UseSessionToken))
{
    submitMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
}
submitMessage.Content = JsonContent.Create(submitPlan.Request, options: new JsonSerializerOptions { IncludeFields = true });

Task<HttpListenerContext> submitIncoming = listener.GetContextAsync();
Task<HttpResponseMessage> submitOutgoing = client.SendAsync(submitMessage);
HttpListenerContext submitContext = await submitIncoming.WaitAsync(TimeSpan.FromSeconds(5));
using var submitReader = new StreamReader(submitContext.Request.InputStream);
string submitBody = await submitReader.ReadToEndAsync();

Require(submitContext.Request.HttpMethod == "POST", "submit method");
Require(submitContext.Request.RawUrl == "/api/v1/app/validated-actions/submit", "submit raw url");
Require(submitContext.Request.Headers["Authorization"] == "Bearer session-token-720", "submit authorization");
Require(submitBody.Contains("\"userId\":\"user-720\""), "submit userId body field");
Require(submitBody.Contains("\"ticket\":\"hzn-rt1:2026-09:abc:def\""), "submit ticket of the current run");
Require(submitBody.Contains($"\"inputLogHash\":\"{abcHash}\""), "submit hash in lower case");
Require(submitBody.Contains("\"score\":18250"), "submit score body field");
Require(submitBody.Contains("\"stage\":\"wave_10\""), "submit stage body field");
Require(submitBody.Contains("\"leaderboardKey\":null"), "blank board key uses the ticket's board");
Require(submitBody.Contains("\"earned\":[{\"key\":\"gold\",\"amount\":250}]"), "submit earned values without null entries");

submitContext.Response.StatusCode = 200;
submitContext.Response.Close();
(await submitOutgoing).EnsureSuccessStatusCode();

RequireSubmitError(new UserData(), "session-token-720", currentRun, abcHash, ValidatedActionsErrorCodes.SessionRequired, "submit missing user session gate");
RequireSubmitError(user, "stale-token", currentRun, abcHash, ValidatedActionsErrorCodes.SessionRequired, "submit current transport session gate");
RequireSubmitError(user, "session-token-720", null, abcHash, ValidatedActionsErrorCodes.NoActiveRun, "submit without a current run");
RequireSubmitError(user, "session-token-720", currentRun, abcHash.Substring(2), ValidatedActionsErrorCodes.InvalidInputLogHash, "submit short hash");
RequireSubmitError(user, "session-token-720", currentRun, null, ValidatedActionsErrorCodes.InvalidInputLogHash, "submit missing hash");

Require(ValidatedActionsTransportContract.EndsRun(200, null), "accepted run ends the run");
Require(ValidatedActionsTransportContract.EndsRun(422, "SCORE_ABOVE_MAX"), "rule rejection ends the run");
Require(ValidatedActionsTransportContract.EndsRun(422, "TICKET_EXPIRED"), "ticket rejection ends the run");
Require(!ValidatedActionsTransportContract.EndsRun(422, "LEADERBOARD_MISMATCH"), "board mismatch keeps the run");
Require(ValidatedActionsTransportContract.EndsRun(403, "SCORE_LIMIT_REACHED"), "score limit ends the run");
Require(!ValidatedActionsTransportContract.EndsRun(403, "SESSION_FORBIDDEN"), "forbidden session keeps the run");
Require(!ValidatedActionsTransportContract.EndsRun(0, null), "network error keeps the run");
Require(!ValidatedActionsTransportContract.EndsRun(429, "RUN_RATE_LIMITED"), "rate limit keeps the run");
Require(!ValidatedActionsTransportContract.EndsRun(503, "VALIDATED_ACTIONS_UNAVAILABLE"), "unavailable keeps the run");
Require(ValidatedActionsErrorCodes.Resolve(404, null) == ValidatedActionsErrorCodes.NotSupported, "404 without code means not supported");
Require(ValidatedActionsErrorCodes.Resolve(404, "PLAYER_NOT_FOUND") == "PLAYER_NOT_FOUND", "server code wins");
Require(new PlayerState { values = new[] { new PlayerStateValue { key = "gold", balance = 1250 } } }.GetBalance("gold") == 1250, "state balance helper");
Require(new PlayerState().GetBalance("gold") == 0, "state balance of a missing key");

Console.WriteLine("Unity SDK leaderboard, gift code, player profile and validated actions transport contract passed");

void RequireSubmitError(UserData submitUser, string token, ValidatedRun run, string hash, string expectedCode, string name)
{
    Require(!ValidatedActionsTransportContract.TryCreateSubmitPlan(submitUser, token, run, 1, hash, null, null, null, out var rejectedPlan, out var code) &&
        rejectedPlan == null && code == expectedCode, name);
}

void RequireSetError(UserData setUser, string token, string avatarId, string frameId, string[] badges, string expectedCode, string name)
{
    Require(!PlayerProfileTransportContract.TryCreateSetPlan(setUser, token, avatarId, frameId, badges, out var rejectedPlan, out var code) &&
        rejectedPlan == null && code == expectedCode, name);
}

static void Require(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"transport contract failed: {name}");
    }
}
