using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
namespace PM.horizOn.Cloud.Helper
{
    /// <summary>Validate decoded fields and original integer tokens before any Unity conversion.</summary>
    internal static class FriendsJson
    {
        internal static bool IsUuid(string value) => value != null && Regex.IsMatch(value, @"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z");
        internal static bool TryResolve(string query, out string userId)
        {
            userId = null;
            Guid id;
            if (IsUuid(query)) { userId = Guid.ParseExact(query, "D").ToString("D"); return true; }
            if (query == null || !Regex.IsMatch(query, @"\AH-[A-Za-z0-9_-]{22}\z")) return false;
            try
            {
                byte[] bytes = Convert.FromBase64String(query.Substring(2).Replace('-', '+').Replace('_', '/') + "==");
                string hex = BitConverter.ToString(bytes).Replace("-", "");
                id = Guid.ParseExact(hex, "N");
                if (Code(id.ToString("D")) != query) return false;
                userId = id.ToString("D"); return true;
            }
            catch (FormatException) { return false; }
        }
        internal static string Code(string userId)
        {
            // Guid.ToByteArray uses mixed endian order; server UUID bytes are network order.
            string hex = Guid.ParseExact(userId, "D").ToString("N"); var bytes = new byte[16];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return "H-" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        internal static FriendsSnapshot Snapshot(string json)
        {
            var f = JsonFragments.Object(json);
            var result = new FriendsSnapshot { userId = Uuid(f["userId"]), revision = Number(f["revision"]), friendCode = Text(f["friendCode"]), serverTime = Instant(f["serverTime"]) };
            if (result.friendCode != Code(result.userId)) throw new FormatException("Friend code does not identify player");
            result.friends = Rows(f["friends"], 200, raw =>
            {
                var x = JsonFragments.Object(raw);
                return new FriendView { userId = Uuid(x["userId"]), since = Instant(x["since"]), lastActiveAt = x["lastActiveAt"] == "null" ? null : Instant(x["lastActiveAt"]), display = Display(x["display"]) };
            }, r => r.userId, result.userId);
            result.incoming = Requests(f["incoming"], result.userId); result.outgoing = Requests(f["outgoing"], result.userId);
            result.blocked = Rows(f["blocked"], 100, raw => { var x = JsonFragments.Object(raw); return new BlockedPlayerView { userId = Uuid(x["userId"]), blockedAt = Instant(x["blockedAt"]) }; }, r => r.userId, result.userId);
            var l = JsonFragments.Object(f["limits"]);
            result.limits = new FriendsLimits { maxFriends = Limit(l["maxFriends"], 200), maxIncomingRequests = Limit(l["maxIncomingRequests"], 20), maxOutgoingRequests = Limit(l["maxOutgoingRequests"], 20), maxBlocked = Limit(l["maxBlocked"], 100) };
            // Lists above today's tier limit remain readable and removable after a downgrade.
            return result;
        }
        internal static FriendLookup Lookup(string json)
        {
            var f = JsonFragments.Object(json);
            var result = new FriendLookup { userId = Uuid(f["userId"]), friendCode = Text(f["friendCode"]), display = Display(f["display"]) };
            if (result.friendCode != Code(result.userId)) throw new FormatException("Friend lookup code invalid");
            return result;
        }
        private static FriendRequestView[] Requests(string json, string own) => Rows(json, 20, raw =>
        { var x = JsonFragments.Object(raw); return new FriendRequestView { userId = Uuid(x["userId"]), requestId = Uuid(x["requestId"]), createdAt = Instant(x["createdAt"]), display = Display(x["display"]) }; }, r => r.userId, own);
        private static FriendDisplay Display(string json)
        {
            var f = JsonFragments.Object(json); var p = JsonFragments.Object(f["profile"]);
            var badges = JsonFragments.Array(p["badges"]); if (badges.Count > 3) throw new FormatException("Friend badge bound");
            var strings = new string[badges.Count]; for (int i = 0; i < strings.Length; i++) strings[i] = Text(badges[i]);
            return new FriendDisplay { name = Optional(f["name"]), profile = new HorizonPlayerProfile { avatarId = Optional(p["avatarId"]), frameId = Optional(p["frameId"]), badges = strings } };
        }
        private static T[] Rows<T>(string json, int max, Func<string,T> parse, Func<T,string> identity, string own)
        {
            var raw = JsonFragments.Array(json); if (raw.Count > max) throw new FormatException("Friends list bound");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var result = new T[raw.Count];
            for (int i = 0; i < raw.Count; i++) { result[i] = parse(raw[i]); string id = identity(result[i]); if (id == own || !ids.Add(id)) throw new FormatException("Friends list identity invalid"); }
            return result;
        }
        private static string Text(string json) => JsonFragments.String(json);
        private static string Optional(string json) => json == "null" ? null : Text(json);
        private static string Uuid(string json) { string value = Text(json); if (!IsUuid(value)) throw new FormatException("Friends UUID invalid"); return Guid.ParseExact(value, "D").ToString("D"); }
        private static string Instant(string json) { string value = Text(json); if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)) throw new FormatException("Friends time invalid"); return value; }
        private static long Number(string raw) { if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value < 0 || value > PlayerValuesManager.MaxSafeInteger) throw new FormatException("Friends integer invalid"); return value; }
        private static int Limit(string raw, int max) { long value = Number(raw); if (value < 1 || value > max) throw new FormatException("Friends limit invalid"); return (int)value; }
        internal static FriendsSnapshot Clone(FriendsSnapshot s) => s == null ? null : new FriendsSnapshot
        {
            userId = s.userId, revision = s.revision, friendCode = s.friendCode, serverTime = s.serverTime,
            friends = Copy(s.friends, x => new FriendView { userId = x.userId, since = x.since, lastActiveAt = x.lastActiveAt, display = Clone(x.display) }),
            incoming = Copy(s.incoming, Clone), outgoing = Copy(s.outgoing, Clone),
            blocked = Copy(s.blocked, x => new BlockedPlayerView { userId = x.userId, blockedAt = x.blockedAt }),
            limits = new FriendsLimits { maxFriends = s.limits.maxFriends, maxIncomingRequests = s.limits.maxIncomingRequests, maxOutgoingRequests = s.limits.maxOutgoingRequests, maxBlocked = s.limits.maxBlocked }
        };
        internal static FriendLookup Clone(FriendLookup s) => s == null ? null : new FriendLookup { userId = s.userId, friendCode = s.friendCode, display = Clone(s.display) };
        private static FriendRequestView Clone(FriendRequestView x) => new FriendRequestView { userId = x.userId, requestId = x.requestId, createdAt = x.createdAt, display = Clone(x.display) };
        private static FriendDisplay Clone(FriendDisplay x) => new FriendDisplay { name = x.name, profile = new HorizonPlayerProfile { avatarId = x.profile.avatarId, frameId = x.profile.frameId, badges = Copy(x.profile.badges, value => value) } };
        private static T[] Copy<T>(T[] s, Func<T,T> clone) => Array.ConvertAll(s, value => clone(value));
    }
}
