using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Helper
{
    /// <summary>Strict wire lexemes: no floating-point projection, dictionary stripping or NUL truncation.</summary>
    internal static class SharedObjectsJson
    {
        private static Dictionary<string, string> Fields(string raw, string names)
        {
            var f = JsonFragments.Object(raw); var expected = names.Split(',');
            if (f.Count != expected.Length || expected.Any(n => !f.ContainsKey(n))) throw new FormatException("Shared fields invalid");
            return f;
        }
        private static string Text(string raw, int bytes = int.MaxValue)
        {
            string s = JsonFragments.String(raw);
            if (Encoding.UTF8.GetByteCount(s) > bytes) throw new FormatException("Shared string bound");
            return s;
        }
        private static string Key(string raw) { var s = Text(raw); if (!ItemsJson.Key(s)) throw new FormatException("Shared key invalid"); return s; }
        private static string Uuid(string raw) { var s = Text(raw); if (!FriendsJson.IsUuid(s)) throw new FormatException("Shared UUID invalid"); return Guid.ParseExact(s, "D").ToString("D"); }
        private static string Nullable(string raw, Func<string, string> parse) => raw == "null" ? null : parse(raw);
        private static long Number(string raw, bool signed = false) => ItemsJson.Integer(raw, signed);
        private static int Bound(string raw, int min, int max) { long n = Number(raw); if (n < min || n > max) throw new FormatException("Shared integer bound"); return (int)n; }
        private static bool Boolean(string raw) => raw == "true" ? true : raw == "false" ? false : throw new FormatException("Shared boolean invalid");
        private static string Choice(string raw, params string[] values) { var s = Text(raw); if (!values.Contains(s)) throw new FormatException("Shared enum invalid"); return s; }
        internal static long Millis(string time) => DateTimeOffset.Parse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUnixTimeMilliseconds();
        private static string Time(string raw) => ItemsJson.Instant(raw);
        private static int Poll(string raw) => Bound(raw, 1, 86400);
        private static T[] Rows<T>(string raw, int maximum, Func<string, T> parse, Func<T, string> key)
        {
            var rows = JsonFragments.Array(raw); if (rows.Count > maximum) throw new FormatException("Shared rows bound");
            var seen = new HashSet<string>(StringComparer.Ordinal); var values = new T[rows.Count];
            for (int i = 0; i < rows.Count; i++) { values[i] = parse(rows[i]); if (!seen.Add(key(values[i]))) throw new FormatException("Shared duplicate row"); }
            return values;
        }
        private static SharedLimits Limits(string raw)
        {
            var f = Fields(raw, "collections,storageBytes,objectBytes"); long storage = Number(f["storageBytes"]);
            if (storage < 1048576 || storage > 9007199254740991L) throw new FormatException("Shared storage bound");
            return new SharedLimits { collections = Bound(f["collections"], 1, int.MaxValue), storageBytes = storage, objectBytes = Bound(f["objectBytes"], 1, 1048576) };
        }
        private static SharedDefinition Definition(string raw)
        {
            var f = Fields(raw, "key,name,active,readPolicy,writePolicy,maxObjectBytes,maxObjectsPerOwner,ttlSeconds,searchFields,positionEnabled,used,revision");
            string name = Text(f["name"], 120); if (string.IsNullOrWhiteSpace(name)) throw new FormatException("Shared name invalid");
            long? ttl = f["ttlSeconds"] == "null" ? (long?)null : Number(f["ttlSeconds"]);
            if (ttl.HasValue && (ttl < 60 || ttl > 2592000)) throw new FormatException("Shared TTL invalid");
            var d = new SharedDefinition { key = Key(f["key"]), name = name, active = Boolean(f["active"]), readPolicy = Choice(f["readPolicy"], "owner", "friends", "group", "all", "dashboard"), writePolicy = Choice(f["writePolicy"], "owner", "group", "dashboard"), maxObjectBytes = Bound(f["maxObjectBytes"], 1, 1048576), maxObjectsPerOwner = Bound(f["maxObjectsPerOwner"], 1, int.MaxValue), ttlSeconds = ttl, searchFields = Rows(f["searchFields"], 3, SearchField, x => x.key), positionEnabled = Boolean(f["positionEnabled"]), used = Boolean(f["used"]), revision = Number(f["revision"]) };
            if (d.readPolicy == "dashboard" && d.writePolicy != "dashboard") throw new FormatException("Shared policy invalid");
            return d;
        }
        private static SharedSearchField SearchField(string raw) { var f = Fields(raw, "key,type"); return new SharedSearchField { key = Key(f["key"]), type = Choice(f["type"], "integer", "string") }; }
        internal static SharedScalar Scalar(string raw) => raw.StartsWith("\"", StringComparison.Ordinal)
            ? new SharedScalar { type = SharedScalarType.String, stringValue = Text(raw, 96) }
            : new SharedScalar { type = SharedScalarType.Integer, integerValue = Number(raw, true) };
        private static SharedSearchValue[] Search(string raw)
        {
            var f = JsonFragments.Object(raw); if (f.Count > 3 || f.Keys.Any(k => !ItemsJson.Key(k))) throw new FormatException("Shared search invalid");
            return f.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new SharedSearchValue { key = p.Key, value = Scalar(p.Value) }).ToArray();
        }
        private static SharedPosition Position(string raw)
        {
            if (raw == "null") return null; var f = Fields(raw, "x,y");
            return new SharedPosition { x = Number(f["x"], true), y = Number(f["y"], true) };
        }
        internal static SharedObject Object(string raw)
        {
            var f = Fields(raw, "id,collection,ownerId,groupId,revision,dataBase64,search,position,createdAt,updatedAt,expiresAt");
            string encoded = Text(f["dataBase64"], 1398104); byte[] bytes = Convert.FromBase64String(encoded);
            if (bytes.Length > 1048576 || Convert.ToBase64String(bytes) != encoded) throw new FormatException("Shared Base64 invalid");
            var result = new SharedObject { id = Uuid(f["id"]), collection = Key(f["collection"]), ownerId = Uuid(f["ownerId"]), groupId = Nullable(f["groupId"], Uuid), revision = Number(f["revision"]), dataBase64 = encoded, data = bytes, search = Search(f["search"]), position = Position(f["position"]), createdAt = Time(f["createdAt"]), updatedAt = Time(f["updatedAt"]), expiresAt = Nullable(f["expiresAt"], Time) };
            if (result.revision < 1 || Millis(result.updatedAt) < Millis(result.createdAt) || result.expiresAt != null && Millis(result.expiresAt) <= Millis(result.createdAt)) throw new FormatException("Shared object chronology invalid");
            return result;
        }
        internal static SharedCollectionPage Collections(string raw)
        {
            var f = Fields(raw, "revision,definitions,nextAfterKey,limits,serverTime");
            return new SharedCollectionPage { revision = Number(f["revision"]), definitions = Rows(f["definitions"], 50, Definition, x => x.key), nextAfterKey = Nullable(f["nextAfterKey"], Key), limits = Limits(f["limits"]), serverTime = Time(f["serverTime"]) };
        }
        internal static SharedUsage Usage(string raw)
        {
            var f = Fields(raw, "userId,collection,revision,count,limits,pollAfterSeconds,serverTime");
            return new SharedUsage { userId = Uuid(f["userId"]), collection = Key(f["collection"]), revision = Number(f["revision"]), count = Bound(f["count"], 0, int.MaxValue), limits = Limits(f["limits"]), pollAfterSeconds = Poll(f["pollAfterSeconds"]), serverTime = Time(f["serverTime"]) };
        }
        internal static SharedObjectRead Read(string raw)
        {
            var f = Fields(raw, "object,pollAfterSeconds,serverTime"); return new SharedObjectRead { @object = Object(f["object"]), pollAfterSeconds = Poll(f["pollAfterSeconds"]), serverTime = Time(f["serverTime"]) };
        }
        internal static SharedQueryResult Query(string raw)
        {
            var f = Fields(raw, "objects,nextCursor,pollAfterSeconds,serverTime"); var objects = Rows(f["objects"], 50, Object, x => x.id);
            if (objects.Sum(x => (long)x.data.Length) > 1048576) throw new FormatException("Shared page byte bound");
            string cursor = Nullable(f["nextCursor"], s => Text(s, 16384));
            if (cursor != null && cursor.Length == 0) throw new FormatException("Shared cursor empty");
            return new SharedQueryResult { objects = objects, nextCursor = cursor, pollAfterSeconds = Poll(f["pollAfterSeconds"]), serverTime = Time(f["serverTime"]) };
        }
        internal static SharedWriteResult Write(string raw)
        {
            var f = Fields(raw, "object,usage,pollAfterSeconds,serverTime"); return new SharedWriteResult { @object = Object(f["object"]), usage = Usage(f["usage"]), pollAfterSeconds = Poll(f["pollAfterSeconds"]), serverTime = Time(f["serverTime"]) };
        }
        internal static SharedDeleteResult Delete(string raw)
        {
            var f = Fields(raw, "objectId,deleted,usage,pollAfterSeconds,serverTime"); bool deleted = Boolean(f["deleted"]); if (!deleted) throw new FormatException("Shared deletion not confirmed");
            return new SharedDeleteResult { objectId = Uuid(f["objectId"]), deleted = true, usage = Usage(f["usage"]), pollAfterSeconds = Poll(f["pollAfterSeconds"]), serverTime = Time(f["serverTime"]) };
        }
        internal static string ScalarJson(SharedScalar value)
        {
            if (value == null) throw new FormatException("Shared scalar missing");
            if (value.type == SharedScalarType.Integer && ItemsJson.Safe(value.integerValue)) return value.integerValue.ToString(CultureInfo.InvariantCulture);
            if (value.type == SharedScalarType.String && value.stringValue != null && Encoding.UTF8.GetByteCount(value.stringValue) <= 96) return ItemsJson.Quote(value.stringValue);
            throw new FormatException("Shared scalar invalid");
        }
        internal static string SearchJson(IEnumerable<SharedSearchValue> values)
        {
            if (values == null) throw new FormatException("Shared search missing"); var rows = values.ToArray();
            if (rows.Length > 3 || rows.Any(v => v == null || !ItemsJson.Key(v.key)) || rows.Select(v => v.key).Distinct(StringComparer.Ordinal).Count() != rows.Length) throw new FormatException("Shared search invalid");
            return "{" + string.Join(",", rows.OrderBy(v => v.key, StringComparer.Ordinal).Select(v => ItemsJson.Quote(v.key) + ":" + ScalarJson(v.value))) + "}";
        }
        internal static string PositionJson(SharedPosition value)
        {
            if (value == null) return "null";
            if (!ItemsJson.Safe(value.x) || !ItemsJson.Safe(value.y)) throw new FormatException("Shared position invalid");
            return "{\"x\":" + value.x.ToString(CultureInfo.InvariantCulture) + ",\"y\":" + value.y.ToString(CultureInfo.InvariantCulture) + "}";
        }
        internal static string InputJson(string user, SharedObjectInput value, string group, bool create)
        {
            if (value == null || value.data == null || value.data.Length > 1048576 || group != null && !FriendsJson.IsUuid(group)) throw new FormatException("Shared input invalid");
            return "{\"userId\":" + ItemsJson.Quote(user) + ",\"dataBase64\":" + ItemsJson.Quote(Convert.ToBase64String(value.data)) + ",\"search\":" + SearchJson(value.search) + ",\"position\":" + PositionJson(value.position) + (create && group != null ? ",\"groupId\":" + ItemsJson.Quote(group) : "") + "}";
        }
        internal static string QueryJson(string user, SharedQuery q)
        {
            if (q == null || q.limit < 1 || q.limit > 50 || q.ownerId != null && !FriendsJson.IsUuid(q.ownerId) || q.groupId != null && !FriendsJson.IsUuid(q.groupId) || q.cursor != null && (q.cursor.Length == 0 || Encoding.UTF8.GetByteCount(q.cursor) > 16384)) throw new FormatException("Shared query invalid");
            var b = new StringBuilder("{\"userId\":" + ItemsJson.Quote(user) + ",\"limit\":" + q.limit.ToString(CultureInfo.InvariantCulture));
            string mode;
            switch (q.mode)
            {
                case SharedQueryMode.List: mode = "list"; break;
                case SharedQueryMode.Owner: mode = "owner"; if (q.ownerId == null) throw new FormatException("Shared owner missing"); break;
                case SharedQueryMode.Field: mode = "field"; break;
                case SharedQueryMode.Rectangle: mode = "rectangle"; break;
                case SharedQueryMode.Random: mode = "random"; break;
                default: throw new FormatException("Shared mode invalid");
            }
            b.Append(",\"mode\":").Append(ItemsJson.Quote(mode));
            if (q.ownerId != null) b.Append(",\"ownerId\":").Append(ItemsJson.Quote(q.ownerId));
            if (q.groupId != null) b.Append(",\"groupId\":").Append(ItemsJson.Quote(q.groupId));
            if (q.cursor != null) b.Append(",\"cursor\":").Append(ItemsJson.Quote(q.cursor));
            if (q.mode == SharedQueryMode.Field || q.mode == SharedQueryMode.Random)
            {
                if (!ItemsJson.Key(q.field)) throw new FormatException("Shared field invalid");
                b.Append(",\"field\":").Append(ItemsJson.Quote(q.field));
                if (q.equal != null && q.mode == SharedQueryMode.Field && q.min == null && q.max == null) b.Append(",\"equal\":").Append(ScalarJson(q.equal));
                else
                {
                    if (q.equal != null || q.min == null || q.max == null || q.min.type != q.max.type || q.mode == SharedQueryMode.Random && (q.cursor != null || q.min.type != SharedScalarType.Integer)) throw new FormatException("Shared range invalid");
                    string min = ScalarJson(q.min), max = ScalarJson(q.max);
                    if (q.min.type == SharedScalarType.Integer ? q.min.integerValue > q.max.integerValue : CompareUtf8(q.min.stringValue, q.max.stringValue) > 0) throw new FormatException("Shared range order invalid");
                    b.Append(",\"min\":").Append(min).Append(",\"max\":").Append(max);
                }
            }
            else if (q.field != null || q.equal != null || q.min != null || q.max != null) throw new FormatException("Shared mode fields invalid");
            if (q.mode == SharedQueryMode.Rectangle)
            {
                if (!ItemsJson.Safe(q.minX) || !ItemsJson.Safe(q.maxX) || !ItemsJson.Safe(q.minY) || !ItemsJson.Safe(q.maxY) || q.minX > q.maxX || q.minY > q.maxY) throw new FormatException("Shared rectangle invalid");
                b.Append(",\"minX\":").Append(q.minX.ToString(CultureInfo.InvariantCulture)).Append(",\"maxX\":").Append(q.maxX.ToString(CultureInfo.InvariantCulture)).Append(",\"minY\":").Append(q.minY.ToString(CultureInfo.InvariantCulture)).Append(",\"maxY\":").Append(q.maxY.ToString(CultureInfo.InvariantCulture));
            }
            return b.Append('}').ToString();
        }
        private static int CompareUtf8(string a, string b)
        {
            var x = Encoding.UTF8.GetBytes(a); var y = Encoding.UTF8.GetBytes(b);
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++) { int c = x[i].CompareTo(y[i]); if (c != 0) return c; }
            return x.Length.CompareTo(y.Length);
        }
        internal static bool MatchesQuery(SharedObject o, Dictionary<string, string> fields)
        {
            string mode = JsonFragments.String(fields["mode"]);
            if (mode == "rectangle") return o.position != null && o.position.x >= Number(fields["minX"], true) && o.position.x <= Number(fields["maxX"], true)
                && o.position.y >= Number(fields["minY"], true) && o.position.y <= Number(fields["maxY"], true);
            if (mode != "field" && mode != "random") return true;
            string key = JsonFragments.String(fields["field"]); var found = o.search.FirstOrDefault(s => s.key == key)?.value;
            if (found == null) return false;
            Func<SharedScalar, SharedScalar, int> compare = (a, b) => a.type == SharedScalarType.Integer ? a.integerValue.CompareTo(b.integerValue) : CompareUtf8(a.stringValue, b.stringValue);
            if (fields.TryGetValue("equal", out string equal)) { var expected = Scalar(equal); return found.type == expected.type && compare(found, expected) == 0; }
            var min = Scalar(fields["min"]); var max = Scalar(fields["max"]);
            return found.type == min.type && found.type == max.type && compare(found, min) >= 0 && compare(found, max) <= 0;
        }
        internal static T Clone<T>(T value) where T : class
        {
            if (value == null) return null;
            object v = value is SharedCollectionPage p ? new SharedCollectionPage { revision = p.revision, definitions = Array.ConvertAll(p.definitions, Clone), nextAfterKey = p.nextAfterKey, limits = Clone(p.limits), serverTime = p.serverTime }
                : value is SharedUsage u ? new SharedUsage { userId = u.userId, collection = u.collection, revision = u.revision, count = u.count, limits = Clone(u.limits), pollAfterSeconds = u.pollAfterSeconds, serverTime = u.serverTime }
                : value is SharedObject o ? new SharedObject { id = o.id, collection = o.collection, ownerId = o.ownerId, groupId = o.groupId, revision = o.revision, dataBase64 = o.dataBase64, data = (byte[])o.data.Clone(), search = Array.ConvertAll(o.search, Clone), position = Clone(o.position), createdAt = o.createdAt, updatedAt = o.updatedAt, expiresAt = o.expiresAt }
                : value is SharedObjectRead r ? new SharedObjectRead { @object = Clone(r.@object), pollAfterSeconds = r.pollAfterSeconds, serverTime = r.serverTime }
                : value is SharedQueryResult q ? new SharedQueryResult { objects = Array.ConvertAll(q.objects, Clone), nextCursor = q.nextCursor, pollAfterSeconds = q.pollAfterSeconds, serverTime = q.serverTime }
                : value is SharedWriteResult w ? new SharedWriteResult { @object = Clone(w.@object), usage = Clone(w.usage), pollAfterSeconds = w.pollAfterSeconds, serverTime = w.serverTime }
                : value is SharedDeleteResult d ? new SharedDeleteResult { objectId = d.objectId, deleted = d.deleted, usage = Clone(d.usage), pollAfterSeconds = d.pollAfterSeconds, serverTime = d.serverTime }
                : value is SharedDefinition f ? new SharedDefinition { key = f.key, name = f.name, active = f.active, readPolicy = f.readPolicy, writePolicy = f.writePolicy, maxObjectBytes = f.maxObjectBytes, maxObjectsPerOwner = f.maxObjectsPerOwner, ttlSeconds = f.ttlSeconds, searchFields = Array.ConvertAll(f.searchFields, Clone), positionEnabled = f.positionEnabled, used = f.used, revision = f.revision }
                : value is SharedLimits l ? new SharedLimits { collections = l.collections, objectBytes = l.objectBytes, storageBytes = l.storageBytes }
                : value is SharedSearchValue s ? new SharedSearchValue { key = s.key, value = Clone(s.value) }
                : value is SharedScalar scalar ? new SharedScalar { type = scalar.type, integerValue = scalar.integerValue, stringValue = scalar.stringValue }
                : value is SharedPosition pos ? new SharedPosition { x = pos.x, y = pos.y }
                : value is SharedSearchField field ? new SharedSearchField { key = field.key, type = field.type }
                : throw new ArgumentException("Unknown Shared copy");
            return (T)v;
        }
    }
}
