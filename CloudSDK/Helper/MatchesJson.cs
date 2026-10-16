using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Helper
{
    /// <summary>Original JSON lexemes and canonical opaque bytes; never project game state through floats or Unity JSON.</summary>
    internal static class MatchesJson
    {
        private const string SummaryFields = "id,name,revision,creatorId,capacity,visibility,strength,status,participantIds,invitedIds,forfeitedIds,currentPlayerId,turnDeadline,createdAt,startedAt,finishedAt,purgeAt,finalStateRevision,hasDisagreement,ranked,timeoutDue";
        private static Dictionary<string, string> Fields(string raw, string names)
        {
            var f = JsonFragments.Object(raw); var required = names.Split(',');
            if (f.Count != required.Length || required.Any(n => !f.ContainsKey(n))) throw new FormatException("Match fields invalid");
            return f;
        }
        private static string Text(string raw, int maximum = int.MaxValue)
        {
            string value = JsonFragments.String(raw);
            if (Encoding.UTF8.GetByteCount(value) > maximum) throw new FormatException("Match text bound");
            return value;
        }
        private static string Uuid(string raw) => CanonicalUuid(Text(raw));
        internal static string CanonicalUuid(string value)
        {
            if (!FriendsJson.IsUuid(value)) throw new FormatException("Match UUID invalid");
            return Guid.ParseExact(value, "D").ToString("D");
        }
        private static string Nullable(string raw, Func<string, string> parse) => raw == "null" ? null : parse(raw);
        private static long Number(string raw, bool signed = false) => ItemsJson.Integer(raw, signed);
        private static long? NullableNumber(string raw) => raw == "null" ? (long?)null : Number(raw);
        private static int Bound(string raw, int min, int max)
        { long n = Number(raw); if (n < min || n > max) throw new FormatException("Match integer bound"); return (int)n; }
        private static bool Boolean(string raw) => raw == "true" ? true : raw == "false" ? false : throw new FormatException("Match boolean invalid");
        private static string Choice(string raw, params string[] choices)
        { string value = Text(raw); if (!choices.Contains(value)) throw new FormatException("Match enum invalid"); return value; }
        private static string BoardKey(string raw)
        {
            string key = Text(raw, 64);
            if (key.Length == 0 || key.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '-'))) throw new FormatException("Match board key invalid");
            return key;
        }
        private static string Time(string raw) => ItemsJson.Instant(raw);
        internal static long Millis(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUnixTimeMilliseconds();
        private static int Poll(string raw) => Bound(raw, 1, 86400);
        private static string Cursor(string raw)
        { string s = Text(raw, 16384); if (s.Length == 0) throw new FormatException("Match cursor empty"); return s; }
        private static T[] Rows<T>(string raw, int max, Func<string, T> parse, Func<T, string> identity)
        {
            var a = JsonFragments.Array(raw); if (a.Count > max) throw new FormatException("Match rows bound");
            var result = a.Select(parse).ToArray();
            if (result.Select(identity).Distinct(StringComparer.Ordinal).Count() != result.Length) throw new FormatException("Match duplicate identity");
            return result;
        }
        private static string[] Ids(string raw, int max = 8) => Rows(raw, max, Uuid, x => x);
        internal static byte[] Bytes(string encoded, int max)
        {
            if (encoded == null || encoded.Length > ((max + 2) / 3) * 4) throw new FormatException("Match byte bound");
            byte[] bytes = Convert.FromBase64String(encoded);
            if (bytes.Length > max || Convert.ToBase64String(bytes) != encoded) throw new FormatException("Match canonical Base64 required");
            return bytes;
        }
        private static MatchLimits Limits(string raw)
        {
            var f = Fields(raw, "activeMatches,stateBytes,retainedMatches,pendingInvites");
            return new MatchLimits { activeMatches = Bound(f["activeMatches"], 1, 10), stateBytes = Bound(f["stateBytes"], 1024, 8192), retainedMatches = Bound(f["retainedMatches"], 1, 100), pendingInvites = Bound(f["pendingInvites"], 1, 20) };
        }
        internal static MatchLimitsView LimitView(string raw)
        {
            var f = Fields(raw, "limits,config,capabilities,pollAfterSeconds,serverTime");
            var c = Fields(f["config"], "revision,rankedLeaderboardKey"); var a = Fields(f["capabilities"], "matches,matchesRanked,realtime");
            return new MatchLimitsView
            {
                limits = Limits(f["limits"]), config = new MatchConfig { revision = Number(c["revision"]), rankedLeaderboardKey = Nullable(c["rankedLeaderboardKey"], BoardKey) },
                capabilities = new MatchCapabilities { matches = Boolean(a["matches"]), matchesRanked = Boolean(a["matchesRanked"]), realtime = Boolean(a["realtime"]) }, pollAfterSeconds = Poll(f["pollAfterSeconds"]), serverTime = Time(f["serverTime"])
            };
        }
        private static MatchSummary Summary(Dictionary<string, string> f, MatchSummary s)
        {
            s.id = Uuid(f["id"]); s.name = Text(f["name"], 120); s.revision = Number(f["revision"]); s.creatorId = Uuid(f["creatorId"]);
            s.capacity = Bound(f["capacity"], 2, 8); s.visibility = Choice(f["visibility"], "open", "private"); s.strength = Number(f["strength"], true);
            s.status = Choice(f["status"], "lobby", "active", "reporting", "confirmed", "disputed", "unconfirmed", "cancelled");
            s.participantIds = Ids(f["participantIds"]); s.invitedIds = Ids(f["invitedIds"], 7); s.forfeitedIds = Ids(f["forfeitedIds"]);
            s.currentPlayerId = Nullable(f["currentPlayerId"], Uuid); s.turnDeadline = Nullable(f["turnDeadline"], Time); s.createdAt = Time(f["createdAt"]);
            s.startedAt = Nullable(f["startedAt"], Time); s.finishedAt = Nullable(f["finishedAt"], Time); s.purgeAt = Nullable(f["purgeAt"], Time);
            s.finalStateRevision = NullableNumber(f["finalStateRevision"]); s.hasDisagreement = Boolean(f["hasDisagreement"]); s.ranked = Boolean(f["ranked"]); s.timeoutDue = Boolean(f["timeoutDue"]);
            if (s.revision < 1 || Blank(s.name) || s.participantIds.Length == 0 || s.participantIds.Length > s.capacity || !s.participantIds.Contains(s.creatorId)
                || s.forfeitedIds.Any(x => !s.participantIds.Contains(x)) || s.currentPlayerId != null && !s.participantIds.Contains(s.currentPlayerId)
                || s.invitedIds.Intersect(s.participantIds).Any() || s.finalStateRevision > s.revision) throw new FormatException("Match summary relation invalid");
            return s;
        }
        internal static MatchSummary Summary(string raw) => Summary(Fields(raw, SummaryFields), new MatchSummary());
        private static MatchReport Report(string raw)
        {
            var f = Fields(raw, "userId,finalStateRevision,resultBase64,winnerIds,reportedAt"); string encoded = Text(f["resultBase64"], 344);
            var r = new MatchReport { userId = Uuid(f["userId"]), finalStateRevision = Number(f["finalStateRevision"]), resultBase64 = encoded, result = Bytes(encoded, 256), winnerIds = Ids(f["winnerIds"]), reportedAt = Time(f["reportedAt"]) };
            if (!r.winnerIds.SequenceEqual(r.winnerIds.OrderBy(x => x, StringComparer.Ordinal))) throw new FormatException("Match winner order invalid");
            return r;
        }
        internal static MatchView View(string raw)
        {
            var f = Fields(raw, SummaryFields + ",turnOrder,turnSeconds,timeoutPolicy,dataBase64,reports,reportDeadline,absoluteDeadline,rankedLeaderboardKey,ownRankedRunId,notificationPending");
            var v = (MatchView)Summary(f, new MatchView());
            v.turnOrder = Ids(f["turnOrder"]); v.turnSeconds = Bound(f["turnSeconds"], 60, 604800); v.timeoutPolicy = Choice(f["timeoutPolicy"], "skip", "forfeit");
            v.dataBase64 = Nullable(f["dataBase64"], x => Text(x, 10924)); v.data = v.dataBase64 == null ? null : Bytes(v.dataBase64, 8192);
            v.reports = Rows(f["reports"], 8, Report, r => r.userId); v.reportDeadline = Nullable(f["reportDeadline"], Time); v.absoluteDeadline = Nullable(f["absoluteDeadline"], Time);
            v.rankedLeaderboardKey = Nullable(f["rankedLeaderboardKey"], BoardKey);
            v.ownRankedRunId = Nullable(f["ownRankedRunId"], Uuid); v.notificationPending = Boolean(f["notificationPending"]);
            if (v.turnOrder.Any(x => !v.participantIds.Contains(x)) || v.reports.Any(r => !v.participantIds.Contains(r.userId) || r.finalStateRevision != v.finalStateRevision || r.winnerIds.Any(x => !v.participantIds.Contains(x)))
                || v.data == null && (v.reports.Length != 0 || v.ownRankedRunId != null)) throw new FormatException("Match view relation invalid");
            return v;
        }
        internal static MyMatches Mine(string raw)
        {
            var f = Fields(raw, "userId,revision,active,invites,recent,limits,pollAfterSeconds,serverTime");
            var r = new MyMatches { userId = Uuid(f["userId"]), revision = Number(f["revision"]), active = Rows(f["active"], 10, Summary, x => x.id), invites = Rows(f["invites"], 20, Summary, x => x.id), recent = Rows(f["recent"], 100, Summary, x => x.id), limits = Limits(f["limits"]), pollAfterSeconds = Poll(f["pollAfterSeconds"]), serverTime = Time(f["serverTime"]) };
            if (r.active.Any(x => !x.participantIds.Contains(r.userId) || x.forfeitedIds.Contains(r.userId) || x.status != "lobby" && x.status != "active")
                || r.invites.Any(x => !x.invitedIds.Contains(r.userId) || x.status != "lobby")
                || r.recent.Any(x => !x.participantIds.Contains(r.userId))
                || r.active.Concat(r.invites).Concat(r.recent).Select(x => x.id).Distinct().Count() != r.active.Length + r.invites.Length + r.recent.Length) throw new FormatException("Match player lists invalid");
            // A forfeited player may retain an active match in recent while the other players continue.
            return r;
        }
        internal static MatchPage Page(string raw)
        {
            var f = Fields(raw, "matches,nextCursor,pollAfterSeconds,serverTime");
            return new MatchPage { matches = Rows(f["matches"], 50, Summary, x => x.id), nextCursor = Nullable(f["nextCursor"], Cursor), pollAfterSeconds = Poll(f["pollAfterSeconds"]), serverTime = Time(f["serverTime"]) };
        }
        internal static MatchRead Read(string raw)
        {
            var f = Fields(raw, "match,pollAfterSeconds,serverTime");
            return new MatchRead { match = View(f["match"]), pollAfterSeconds = Poll(f["pollAfterSeconds"]), serverTime = Time(f["serverTime"]) };
        }
        internal static MatchCreated Created(string raw)
        {
            var f = Fields(raw, "match,playerRevision,pollAfterSeconds,serverTime");
            return new MatchCreated { match = View(f["match"]), playerRevision = Number(f["playerRevision"]), pollAfterSeconds = Poll(f["pollAfterSeconds"]), serverTime = Time(f["serverTime"]) };
        }
        internal static MatchReported Reported(string raw)
        {
            var f = Fields(raw, "match,replayed,pollAfterSeconds,serverTime");
            return new MatchReported { match = View(f["match"]), replayed = Boolean(f["replayed"]), pollAfterSeconds = Poll(f["pollAfterSeconds"]), serverTime = Time(f["serverTime"]) };
        }
        internal static MatchRankedRunResponse RankedRun(string raw)
        {
            var f = Fields(raw, "runId,ticket,seed,leaderboardKey,issuedAt,expiresAt,expiresInSeconds");
            var run = new MatchRankedRunResponse { runId = Uuid(f["runId"]), ticket = Text(f["ticket"], 8192), seed = Bound(f["seed"], 0, 2147483646),
                leaderboardKey = BoardKey(f["leaderboardKey"]), issuedAt = Time(f["issuedAt"]), expiresAt = Time(f["expiresAt"]), expiresInSeconds = Bound(f["expiresInSeconds"], 0, int.MaxValue) };
            if (Blank(run.ticket) || Millis(run.expiresAt) < Millis(run.issuedAt)) throw new FormatException("Match run invalid");
            return run;
        }
        internal static bool Blank(string value) => value == null || value.All(c => c >= '\t' && c <= '\r' || c >= 0x1c && c <= 0x1f
            || char.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator || char.GetUnicodeCategory(c) == UnicodeCategory.LineSeparator || char.GetUnicodeCategory(c) == UnicodeCategory.ParagraphSeparator);
        internal static string UserJson(string user) => "{\"userId\":" + ItemsJson.Quote(CanonicalUuid(user));
        internal static string CreateJson(string user, MatchCreateInput value)
        {
            if (value == null || value.name == null) throw new FormatException("Match create missing");
            string name = ItemsJson.Quote(value.name), id = CanonicalUuid(user);
            if (Blank(value.name) || Encoding.UTF8.GetByteCount(value.name) > 120 || value.capacity < 2 || value.capacity > 8 || value.turnSeconds < 60 || value.turnSeconds > 604800
                || !ItemsJson.Safe(value.strength) || value.visibility != "open" && value.visibility != "private" || value.timeoutPolicy != "skip" && value.timeoutPolicy != "forfeit") throw new FormatException("Match create invalid");
            string[] invites = CanonicalIds(value.inviteeIds, 7); if (invites.Length >= value.capacity || invites.Contains(id)) throw new FormatException("Match invitees invalid");
            return UserJson(id) + ",\"name\":" + name + ",\"capacity\":" + value.capacity.ToString(CultureInfo.InvariantCulture) + ",\"visibility\":" + ItemsJson.Quote(value.visibility) + ",\"strength\":" + value.strength.ToString(CultureInfo.InvariantCulture)
                + ",\"turnSeconds\":" + value.turnSeconds.ToString(CultureInfo.InvariantCulture) + ",\"timeoutPolicy\":" + ItemsJson.Quote(value.timeoutPolicy) + ",\"ranked\":" + (value.ranked ? "true" : "false")
                + ",\"dataBase64\":" + Encode(value.data, 8192) + ",\"inviteeIds\":" + IdsJson(invites) + "}";
        }
        internal static string[] CanonicalIds(IEnumerable<string> ids, int max = 8)
        {
            if (ids == null) throw new FormatException("Match player IDs missing"); var a = ids.Take(max + 1).Select(CanonicalUuid).ToArray();
            if (a.Length > max || a.Distinct(StringComparer.Ordinal).Count() != a.Length) throw new FormatException("Match player IDs invalid"); return a;
        }
        internal static string IdsJson(IEnumerable<string> ids) => "[" + string.Join(",", ids.Select(ItemsJson.Quote)) + "]";
        internal static string Encode(byte[] data, int max)
        { if (data == null || data.Length > max) throw new FormatException("Match bytes invalid"); return ItemsJson.Quote(Convert.ToBase64String(data)); }
        private static MatchSummary CopySummary(MatchSummary s, MatchSummary d)
        {
            d.id = s.id; d.name = s.name; d.creatorId = s.creatorId; d.visibility = s.visibility; d.status = s.status; d.currentPlayerId = s.currentPlayerId; d.turnDeadline = s.turnDeadline;
            d.createdAt = s.createdAt; d.startedAt = s.startedAt; d.finishedAt = s.finishedAt; d.purgeAt = s.purgeAt; d.revision = s.revision; d.strength = s.strength; d.finalStateRevision = s.finalStateRevision; d.capacity = s.capacity;
            d.participantIds = (string[])s.participantIds.Clone(); d.invitedIds = (string[])s.invitedIds.Clone(); d.forfeitedIds = (string[])s.forfeitedIds.Clone(); d.hasDisagreement = s.hasDisagreement; d.ranked = s.ranked; d.timeoutDue = s.timeoutDue; return d;
        }
        internal static T Clone<T>(T value) where T : class
        {
            if (value == null) return null; object result;
            if (value is MatchView v)
            {
                var c = (MatchView)CopySummary(v, new MatchView()); c.turnOrder = (string[])v.turnOrder.Clone(); c.turnSeconds = v.turnSeconds; c.timeoutPolicy = v.timeoutPolicy;
                c.dataBase64 = v.dataBase64; c.data = v.data == null ? null : (byte[])v.data.Clone(); c.reports = Array.ConvertAll(v.reports, Clone); c.reportDeadline = v.reportDeadline; c.absoluteDeadline = v.absoluteDeadline;
                c.rankedLeaderboardKey = v.rankedLeaderboardKey; c.ownRankedRunId = v.ownRankedRunId; c.notificationPending = v.notificationPending; result = c;
            }
            else if (value is MatchSummary s) result = CopySummary(s, new MatchSummary());
            else if (value is MatchReport r) result = new MatchReport { userId = r.userId, finalStateRevision = r.finalStateRevision, resultBase64 = r.resultBase64, result = (byte[])r.result.Clone(), winnerIds = (string[])r.winnerIds.Clone(), reportedAt = r.reportedAt };
            else if (value is MatchCreated c) result = new MatchCreated { match = Clone(c.match), playerRevision = c.playerRevision, pollAfterSeconds = c.pollAfterSeconds, serverTime = c.serverTime };
            else if (value is MatchReported p) result = new MatchReported { match = Clone(p.match), replayed = p.replayed, pollAfterSeconds = p.pollAfterSeconds, serverTime = p.serverTime };
            else if (value is MatchRead m) result = new MatchRead { match = Clone(m.match), pollAfterSeconds = m.pollAfterSeconds, serverTime = m.serverTime };
            else if (value is MatchLimits l) result = new MatchLimits { activeMatches = l.activeMatches, stateBytes = l.stateBytes, retainedMatches = l.retainedMatches, pendingInvites = l.pendingInvites };
            else if (value is MatchLimitsView limitView) result = new MatchLimitsView { limits = Clone(limitView.limits), config = new MatchConfig { revision = limitView.config.revision, rankedLeaderboardKey = limitView.config.rankedLeaderboardKey }, capabilities = new MatchCapabilities { matches = limitView.capabilities.matches, matchesRanked = limitView.capabilities.matchesRanked, realtime = limitView.capabilities.realtime }, pollAfterSeconds = limitView.pollAfterSeconds, serverTime = limitView.serverTime };
            else if (value is MyMatches mine) result = new MyMatches { userId = mine.userId, revision = mine.revision, active = Array.ConvertAll(mine.active, Clone), invites = Array.ConvertAll(mine.invites, Clone), recent = Array.ConvertAll(mine.recent, Clone), limits = Clone(mine.limits), pollAfterSeconds = mine.pollAfterSeconds, serverTime = mine.serverTime };
            else if (value is MatchPage page) result = new MatchPage { matches = Array.ConvertAll(page.matches, Clone), nextCursor = page.nextCursor, pollAfterSeconds = page.pollAfterSeconds, serverTime = page.serverTime };
            else if (value is ValidatedRun run) result = new ValidatedRun { runId = run.runId, ticket = run.ticket, seed = run.seed, leaderboardKey = run.leaderboardKey, issuedAt = run.issuedAt, expiresAt = run.expiresAt, expiresInSeconds = run.expiresInSeconds };
            else throw new ArgumentException("Unknown Match snapshot type");
            return (T)result;
        }
    }
}
