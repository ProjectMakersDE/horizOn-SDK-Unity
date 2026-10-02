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
Require(ValidatedActionsErrorCodes.Resolve(404, "NOT_FOUND") == ValidatedActionsErrorCodes.NotSupported, "simpleServer unknown route means not supported");
Require(new PlayerState { values = new[] { new PlayerStateValue { key = "gold", balance = 1250 } } }.GetBalance("gold") == 1250, "state balance helper");
Require(new PlayerState().GetBalance("gold") == 0, "state balance of a missing key");

// Validated Actions run start context (TASK-911): camelCase fields, blank fields and an empty
// context left out, initialState as standard base64 with padding, the digest checked locally.
Require(JsonHelper.ToJsonExcludeEmpty(startPlan.Request) == "{\"userId\":\"user-720\",\"leaderboardKey\":\"weekly\"}",
    "start run without context sends the old body");
Require(ValidatedActionsTransportContract.TryCreateStartRunPlan(user, "session-token-720", "weekly",
        new ValidatedRunContext { gameVersion = " ", initialState = new byte[0] }, out var emptyContextPlan, out _) &&
    emptyContextPlan.Request.context == null, "empty context is left out");
if (!ValidatedActionsTransportContract.TryCreateStartRunPlan(user, "session-token-720", null,
        new ValidatedRunContext("1.4.2", simulationVersion: "sim-3", replayFormatVersion: "",
            contentDigest: " " + abcHash.ToUpperInvariant() + " ", initialState: new byte[] { 0, 1, 2, 255 }),
        out var contextPlan, out var contextError))
{
    throw new InvalidOperationException($"context did not produce a start run plan: {contextError}");
}
string contextJson = JsonHelper.ToJsonExcludeEmpty(contextPlan.Request);
Require(contextJson == "{\"userId\":\"user-720\",\"context\":{\"gameVersion\":\"1.4.2\",\"simulationVersion\":\"sim-3\"," +
        $"\"contentDigest\":\"{abcHash}\",\"initialState\":\"AAEC/w==\"}}}}", $"start run context body: {contextJson}");
Require(ValidatedActionsTransportContract.TryCreateStartRunPlan(user, "session-token-720", null,
        new ValidatedRunContext { initialState = new byte[] { 0x68, 0x69 } }, out var stateOnlyPlan, out _) &&
    stateOnlyPlan.Request.context.initialState == "aGk=" && stateOnlyPlan.Request.context.gameVersion == null,
    "initial state alone is sent with padding");
Require(!ValidatedActionsTransportContract.TryCreateStartRunPlan(user, "session-token-720", null,
        new ValidatedRunContext { contentDigest = "abc" }, out var badDigestPlan, out var badDigestCode) &&
    badDigestPlan == null && badDigestCode == ValidatedActionsErrorCodes.InvalidContentDigest, "short content digest is rejected locally");
Require(!ValidatedActionsTransportContract.TryCreateStartRunPlan(new UserData(), "session-token-720", null,
        new ValidatedRunContext { contentDigest = "abc" }, out _, out var contextSessionCode) &&
    contextSessionCode == ValidatedActionsErrorCodes.SessionRequired, "session is checked before the context");
Require(ValidatedActionsErrorCodes.Resolve(413, "INITIAL_STATE_TOO_LARGE") == ValidatedActionsErrorCodes.InitialStateTooLarge,
    "initial state too large code");
Require(ValidatedActionsErrorCodes.Resolve(400, "INITIAL_STATE_INVALID_ENCODING") == ValidatedActionsErrorCodes.InitialStateInvalidEncoding,
    "initial state encoding code");
Require(JsonUtility.FromJson<ValidatedSubmitResult>("{\"accepted\":true,\"runId\":\"run-911\",\"sus\":true}").sus,
    "submit result reads sus");
Require(!JsonUtility.FromJson<ValidatedSubmitResult>("{\"accepted\":true,\"runId\":\"run-911\"}").sus,
    "submit result without sus reads false");

// Validated Actions Part 2 (TASK-887): GET state carries the session and the player's userId,
// value rejections use up the ticket, a submit without state keeps the cached state.
if (!ValidatedActionsTransportContract.TryCreateGetStatePlan(user, "session-token-720", out var statePlan, out var stateError))
{
    throw new InvalidOperationException($"signed user did not produce a state plan: {stateError}");
}
Require(statePlan.Endpoint == "/api/v1/app/validated-actions/state?userId=user-720", "state endpoint with userId");
Require(statePlan.UseSessionToken, "state uses the session token");
Require(statePlan.UserId == "user-720", "state plan remembers the player");
Require(!ValidatedActionsTransportContract.TryCreateGetStatePlan(new UserData(), "session-token-720", out var noStatePlan, out var noStateCode) &&
    noStatePlan == null && noStateCode == ValidatedActionsErrorCodes.SessionRequired, "state missing user session gate");
Require(!ValidatedActionsTransportContract.TryCreateGetStatePlan(user, "stale-token", out _, out var staleStateCode) &&
    staleStateCode == ValidatedActionsErrorCodes.SessionRequired, "state current transport session gate");
Require(ValidatedActionsTransportContract.TryCreateGetStatePlan(
        new UserData { UserId = "user 720/x", AccessToken = "t" }, "t", out var escapedPlan, out _) &&
    escapedPlan.Endpoint == "/api/v1/app/validated-actions/state?userId=user%20720%2Fx", "state userId is escaped");

using var stateMessage = new HttpRequestMessage(HttpMethod.Get, statePlan.Endpoint.TrimStart('/'));
foreach (var header in HorizonRequestHeaders.Create("project-key-720", "session-token-720", statePlan.UseSessionToken))
{
    stateMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
}
Task<HttpListenerContext> stateIncoming = listener.GetContextAsync();
Task<HttpResponseMessage> stateOutgoing = client.SendAsync(stateMessage);
HttpListenerContext stateContext = await stateIncoming.WaitAsync(TimeSpan.FromSeconds(5));
Require(stateContext.Request.HttpMethod == "GET", "state method");
Require(stateContext.Request.RawUrl == "/api/v1/app/validated-actions/state?userId=user-720", "state raw url");
Require(stateContext.Request.Headers["X-API-Key"] == "project-key-720", "state api key");
Require(stateContext.Request.Headers["Authorization"] == "Bearer session-token-720", "state authorization");
stateContext.Response.StatusCode = 200;
stateContext.Response.Close();
(await stateOutgoing).EnsureSuccessStatusCode();

foreach (string valueCode in new[]
         {
             ValidatedActionsErrorCodes.UnknownValueKey, ValidatedActionsErrorCodes.DuplicateValueKey,
             ValidatedActionsErrorCodes.EarnedAboveMax, ValidatedActionsErrorCodes.EarnedBelowMin,
             ValidatedActionsErrorCodes.InsufficientBalance
         })
{
    Require(ValidatedActionsTransportContract.EndsRun(422, valueCode), $"{valueCode} ends the run");
}
Require(ValidatedActionsErrorCodes.InsufficientBalance == "INSUFFICIENT_BALANCE" &&
    ValidatedActionsErrorCodes.UnknownValueKey == "UNKNOWN_VALUE_KEY" &&
    ValidatedActionsErrorCodes.DuplicateValueKey == "DUPLICATE_VALUE_KEY" &&
    ValidatedActionsErrorCodes.EarnedAboveMax == "EARNED_ABOVE_MAX" &&
    ValidatedActionsErrorCodes.EarnedBelowMin == "EARNED_BELOW_MIN", "value code strings");

var submitState = new PlayerState
{
    day = "2026-09-29",
    values = new[]
    {
        new PlayerStateValue { key = "chest.gold", balance = 1, requested = -1, credited = -1 },
        null,
        new PlayerStateValue { key = "gold", balance = 9007199254740991, earnedToday = 500, dailyCap = 500, requested = 400, credited = 250 }
    }
};
Require(submitState.GetValue("gold").credited == 250 && !submitState.GetValue("gold").IsFullyCredited, "clamped credit is not full");
Require(submitState.GetValue("chest.gold").IsFullyCredited, "applied spend is full");
Require(submitState.GetValue("gold").RemainingToday == 0 && submitState.GetValue("gold").HasDailyCap, "daily cap reached");
Require(submitState.GetValue("chest.gold").RemainingToday == long.MaxValue, "no daily cap means no daily limit");
Require(submitState.GetValue("gems") == null && submitState.GetValue(null) == null, "missing state value");
PlayerState cachedState = ValidatedActionsTransportContract.StateToCache(submitState);
Require(cachedState != null && !ReferenceEquals(cachedState, submitState), "submit state is copied into the cache");
Require(cachedState.values.Length == 2 && cachedState.day == "2026-09-29", "cached state skips null entries");
Require(cachedState.GetBalance("gold") == 9007199254740991 && cachedState.GetValue("gold").dailyCap == 500, "cached state keeps balances as long");
Require(cachedState.GetValue("gold").requested == 0 && cachedState.GetValue("gold").credited == 0, "cached state has no per-run fields");
Require(ValidatedActionsTransportContract.StateToCache(null) == null, "no submit state keeps the cache");
Require(ValidatedActionsTransportContract.StateToCache(new PlayerState()) == null, "empty submit state keeps the cache");
Require(ValidatedActionsTransportContract.StateToCache(new PlayerState { day = "2026-09-29" }) != null,
    "a state without values (rules define none) still replaces the cache");

// Validated Actions Part 3 (TASK-888): the evidence upload is a PUT with the session, the userId and
// the raw log as standard base64; only a hash mismatch and a network error may be retried;
// PLAYER_BANNED keeps the run (checked before the ticket is used).
byte[] evidenceLog = { 0, 1, 2, 3 };
if (!ValidatedActionsTransportContract.TryCreateEvidencePlan(
        user, "session-token-720", " run-720 ", evidenceLog, 32768, out var evidencePlan, out var evidenceError))
{
    throw new InvalidOperationException($"signed user did not produce an evidence plan: {evidenceError}");
}
Require(evidencePlan.Endpoint == "/api/v1/app/validated-actions/runs/run-720/evidence", "evidence endpoint with trimmed run ID");
Require(evidencePlan.RunId == "run-720" && evidencePlan.LogBytes == 4, "evidence plan remembers run and size");
Require(evidencePlan.UseSessionToken, "evidence uses the session token");
Require(evidencePlan.Request.userId == "user-720" && evidencePlan.Request.log == "AAECAw==", "evidence body is userId plus padded base64");
evidenceLog[0] = 9;
Require(evidencePlan.Request.log == "AAECAw==", "the log is encoded when the plan is built");
Require(ValidatedActionsTransportContract.EvidenceEndpoint("run 1/x") == "/api/v1/app/validated-actions/runs/run%201%2Fx/evidence", "evidence run ID is escaped");

using var evidenceMessage = new HttpRequestMessage(HttpMethod.Put, evidencePlan.Endpoint.TrimStart('/'));
foreach (var header in HorizonRequestHeaders.Create("project-key-720", "session-token-720", evidencePlan.UseSessionToken))
{
    evidenceMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
}
evidenceMessage.Content = JsonContent.Create(evidencePlan.Request, options: new JsonSerializerOptions { IncludeFields = true });
Task<HttpListenerContext> evidenceIncoming = listener.GetContextAsync();
Task<HttpResponseMessage> evidenceOutgoing = client.SendAsync(evidenceMessage);
HttpListenerContext evidenceContext = await evidenceIncoming.WaitAsync(TimeSpan.FromSeconds(5));
using var evidenceReader = new StreamReader(evidenceContext.Request.InputStream);
string evidenceBody = await evidenceReader.ReadToEndAsync();
Require(evidenceContext.Request.HttpMethod == "PUT", "evidence method");
Require(evidenceContext.Request.RawUrl == "/api/v1/app/validated-actions/runs/run-720/evidence", "evidence raw url");
Require(evidenceContext.Request.Headers["Authorization"] == "Bearer session-token-720", "evidence authorization");
Require(evidenceBody.Contains("\"userId\":\"user-720\""), "evidence userId body field");
Require(evidenceBody.Contains("\"log\":\"AAECAw==\""), "evidence log body field");
evidenceContext.Response.StatusCode = 200;
evidenceContext.Response.Close();
(await evidenceOutgoing).EnsureSuccessStatusCode();

RequireEvidenceError(new UserData(), "session-token-720", "run-720", evidenceLog, 0, ValidatedActionsErrorCodes.SessionRequired, "evidence missing user session gate");
RequireEvidenceError(user, "stale-token", "run-720", evidenceLog, 0, ValidatedActionsErrorCodes.SessionRequired, "evidence current transport session gate");
RequireEvidenceError(user, "session-token-720", " ", evidenceLog, 0, ValidatedActionsErrorCodes.InvalidRunId, "evidence without run ID");
RequireEvidenceError(user, "session-token-720", "run-720", null, 0, ValidatedActionsErrorCodes.EmptyInputLog, "evidence without log");
RequireEvidenceError(user, "session-token-720", "run-720", new byte[0], 0, ValidatedActionsErrorCodes.EmptyInputLog, "evidence with empty log");
RequireEvidenceError(user, "session-token-720", "run-720", evidenceLog, 3, ValidatedActionsErrorCodes.EvidenceTooLarge, "evidence above the known limit");
Require(ValidatedActionsTransportContract.TryCreateEvidencePlan(user, "session-token-720", "run-720", evidenceLog, 4, out _, out _), "evidence at the limit");

Require(ValidatedActionsTransportContract.IsEvidenceRetryable(ValidatedActionsErrorCodes.EvidenceHashMismatch), "hash mismatch may be retried");
Require(ValidatedActionsTransportContract.IsEvidenceRetryable(ValidatedActionsErrorCodes.NetworkError), "network error may be retried");
foreach (string finalCode in new[]
         {
             ValidatedActionsErrorCodes.EvidenceInvalidEncoding, ValidatedActionsErrorCodes.EvidenceNotRequested,
             ValidatedActionsErrorCodes.EvidenceAlreadyUploaded, ValidatedActionsErrorCodes.EvidenceExpired,
             ValidatedActionsErrorCodes.EvidenceTooLarge
         })
{
    Require(!ValidatedActionsTransportContract.IsEvidenceRetryable(finalCode), $"{finalCode} is final");
}
Require(ValidatedActionsErrorCodes.EvidenceInvalidEncoding == "EVIDENCE_INVALID_ENCODING" &&
    ValidatedActionsErrorCodes.EvidenceNotRequested == "EVIDENCE_NOT_REQUESTED" &&
    ValidatedActionsErrorCodes.EvidenceAlreadyUploaded == "EVIDENCE_ALREADY_UPLOADED" &&
    ValidatedActionsErrorCodes.EvidenceExpired == "EVIDENCE_EXPIRED" &&
    ValidatedActionsErrorCodes.EvidenceTooLarge == "EVIDENCE_TOO_LARGE" &&
    ValidatedActionsErrorCodes.EvidenceHashMismatch == "EVIDENCE_HASH_MISMATCH" &&
    ValidatedActionsErrorCodes.PlayerBanned == "PLAYER_BANNED", "evidence and moderation code strings");
Require(!ValidatedActionsTransportContract.EndsRun(403, ValidatedActionsErrorCodes.PlayerBanned), "a ban keeps the run");

var evidenceRequest = new EvidenceRequest { required = true, runId = "run-720", maxBytes = 32768 };
Require(ValidatedActionsTransportContract.ShouldAutoUploadEvidence(true, evidenceRequest, evidenceLog), "auto upload with the raw log");
Require(!ValidatedActionsTransportContract.ShouldAutoUploadEvidence(false, evidenceRequest, evidenceLog), "auto upload switched off");
Require(!ValidatedActionsTransportContract.ShouldAutoUploadEvidence(true, evidenceRequest, null), "no auto upload after a hash submit");
Require(!ValidatedActionsTransportContract.ShouldAutoUploadEvidence(true, new EvidenceRequest(), evidenceLog), "no auto upload without a request");
var acceptedWithoutEvidence = new ValidatedSubmitResult { runId = "run-720", evidence = null };
acceptedWithoutEvidence.Normalize();
Require(acceptedWithoutEvidence.evidence != null && !acceptedWithoutEvidence.evidence.required &&
    acceptedWithoutEvidence.evidence.runId == "", "null evidence reads as an empty request");
Require(ValidatedActionsTransportContract.EvidenceRunId(acceptedWithoutEvidence) == "run-720", "evidence run ID falls back to the result");

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

Console.WriteLine("Unity SDK leaderboard, gift code, player profile and validated actions (runs, start context, state and evidence) transport contract passed");
Console.WriteLine("Unity SDK anonymous session contract passed");

void RequireSubmitError(UserData submitUser, string token, ValidatedRun run, string hash, string expectedCode, string name)
{
    Require(!ValidatedActionsTransportContract.TryCreateSubmitPlan(submitUser, token, run, 1, hash, null, null, null, out var rejectedPlan, out var code) &&
        rejectedPlan == null && code == expectedCode, name);
}

void RequireEvidenceError(UserData evidenceUser, string token, string runId, byte[] log, int maxBytes, string expectedCode, string name)
{
    Require(!ValidatedActionsTransportContract.TryCreateEvidencePlan(evidenceUser, token, runId, log, maxBytes, out var rejectedPlan, out var code) &&
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
