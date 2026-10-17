using System;
using System.Collections.Generic;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Network.Responses;

static class MatchesContractTests
{
    internal const string User = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", Peer = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", Id = "cccccccc-cccc-4ccc-8ccc-cccccccccccc", Time = "2026-10-16T00:00:00Z";
    static string Limits => "{\"activeMatches\":2,\"stateBytes\":1024,\"retainedMatches\":20,\"pendingInvites\":20,\"settlementMatches\":100,\"historyRetentionSeconds\":604800,\"rankedSettlementSeconds\":604800}";
    static string Summary => "{\"id\":\"" + Id + "\",\"name\":\"前\\u0000後😀\",\"revision\":9007199254740991,\"creatorId\":\"" + User + "\",\"capacity\":3,\"visibility\":\"open\",\"strength\":-9007199254740991,\"status\":\"active\",\"participantIds\":[\"" + User + "\",\"" + Peer + "\"],\"invitedIds\":[],\"forfeitedIds\":[\"" + User + "\"],\"currentPlayerId\":\"" + Peer + "\",\"turnDeadline\":\"2026-10-17T00:00:00Z\",\"createdAt\":\"" + Time + "\",\"startedAt\":\"" + Time + "\",\"finishedAt\":null,\"purgeAt\":null,\"finalStateRevision\":null,\"hasDisagreement\":false,\"ranked\":true,\"timeoutDue\":false}";
    static string View(string bytes) => Summary.Substring(0, Summary.Length - 1) + ",\"turnOrder\":[\"" + User + "\",\"" + Peer + "\"],\"turnSeconds\":60,\"timeoutPolicy\":\"forfeit\",\"dataBase64\":" + bytes + ",\"reports\":[],\"reportDeadline\":null,\"absoluteDeadline\":\"2026-11-15T00:00:00Z\",\"rankedLeaderboardKey\":\"1-ranked-board\",\"ownRankedRunId\":null,\"notificationPending\":true}";
    public static void Run()
    {
        var metadata = MatchesJson.View(View("null"));
        Require(metadata.data == null && metadata.dataBase64 == null && metadata.ownRankedRunId == null, "wire-null metadata stays null");
        var empty = MatchesJson.View(View("\"\"")); Require(empty.data.Length == 0 && empty.dataBase64 == "", "accepted empty state differs from metadata");
        var binary = MatchesJson.View(View("\"AP8A\"")); Require(binary.data[1] == 255 && binary.name == "前\0後😀" && binary.strength == -9007199254740991 && binary.revision == 9007199254740991, "opaque bytes/NUL/safe64 retained");
        string mine = "{\"userId\":\"" + User + "\",\"revision\":9007199254740991,\"active\":[],\"invites\":[],\"pendingResults\":[],\"recent\":[" + Summary + "],\"limits\":" + Limits + ",\"pollAfterSeconds\":120,\"serverTime\":\"" + Time + "\"}";
        Require(MatchesJson.Mine(mine).recent[0].status == "active", "own-forfeited ongoing match is valid recent history");
        string created = "{\"match\":" + View("\"AP8A\"") + ",\"playerRevision\":9007199254740991,\"pollAfterSeconds\":120,\"serverTime\":\"" + Time + "\"}";
        var original = MatchesJson.Created(created); var clone = MatchesJson.Clone(original); clone.match.data[1] = 0; clone.match.participantIds[0] = Id;
        Require(original.match.data[1] == 255 && original.match.participantIds[0] == User && clone.playerRevision == 9007199254740991, "derived envelope and nested bytes/IDs clone deeply");
        foreach (string number in new[] { "1.0000000000000001", "1e0", "-0", "9007199254740992", "null" })
            Reject(() => MatchesJson.Summary(Summary.Replace("\"revision\":9007199254740991", "\"rev\\u0069sion\":" + number)), "original unsafe revision " + number);
        foreach (string encoded in new[] { "AB==", "AA", "AA==\n", "_-8=" }) Reject(() => MatchesJson.Bytes(encoded, 8192), "noncanonical base64");
        Reject(() => MatchesJson.View(View("\"" + Convert.ToBase64String(new byte[MatchLimits.MaxStateBytes+1]) + "\"")), "bounded state bytes");
        Reject(() => MatchesJson.View(View("null").Replace("\"reports\":[]", "\"reports\":[{}]")), "metadata never exposes reports");
        Reject(() => MatchesJson.Summary(Summary.Replace("\"name\":\"前\\u0000後😀\"", "\"name\":\"\\ud800\"")), "invalid Unicode is not normalized");
        var input = new MatchCreateInput { name = "前\0後😀", strength = -9007199254740991, data = new byte[] { 0, 255, 0 }, inviteeIds = new[] { Peer.ToUpperInvariant() } };
        string body = MatchesJson.CreateJson(User, input); input.data[1] = 0;
        Require(body.Contains("\"dataBase64\":\"AP8A\"") && body.Contains("\"ranked\":false") && body.Contains("-9007199254740991") && body.Contains(Peer), "complete frozen canonical create request");
        foreach (string blank in new[] { "", "\u00a0", "\u2007", "\u202f", "\u001c" }) Reject(() => MatchesJson.CreateJson(User, new MatchCreateInput { name = blank }), "Kotlin blank name");
        Require(MatchesJson.CreateJson(User, new MatchCreateInput { name = "\u0085" }).Contains("\u0085"), "NEL is not Kotlin isBlank");
        Reject(() => MatchesJson.CreateJson(User, new MatchCreateInput { name = "ok", capacity = 3, inviteeIds = new[] { Peer, Peer.ToUpperInvariant() } }), "canonical duplicate invitee");
        Reject(() => MatchesJson.CanonicalIds(Unbounded(), 7), "bounded enumeration before any HTTP");
        Console.WriteLine("Matches typed/null projection, exact safe integers, canonical opaque bytes, Unicode/NUL, forfeited-active history and frozen bounded request contracts passed.");
    }
    static IEnumerable<string> Unbounded() { for (int i = 0; ; i++) { if (i > 8) throw new Exception("Enumerated beyond bounded match inputs"); yield return Peer; } }
    static void Reject(Action action, string message)
    {
        bool rejected = false; try { action(); } catch (FormatException) { rejected = true; } catch (KeyNotFoundException) { rejected = true; }
        Require(rejected, message);
    }
    static void Require(bool yes, string message) { if (!yes) throw new Exception("Matches: " + message); }
}
