using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Helper
{
    /// <summary>Decode the original JSON lexemes, never a floating-point or JsonUtility dictionary projection.</summary>
    internal static class ItemsJson
    {
        private static readonly Regex KeyPattern = new Regex(@"\A[a-z][a-z0-9_]{0,63}\z");
        private static readonly Regex IntegerPattern = new Regex(@"\A(?:0|-?[1-9][0-9]*)\z");
        internal static bool Key(string key) => key != null && KeyPattern.IsMatch(key);
        internal static bool Revision(long n) => n >= 0 && n <= PlayerValuesManager.MaxSafeInteger;
        internal static bool Safe(long n) => n >= -PlayerValuesManager.MaxSafeInteger && n <= PlayerValuesManager.MaxSafeInteger;
        internal static long Integer(string raw, bool signed = false)
        {
            if (!IntegerPattern.IsMatch(raw) || !long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
                || !Safe(n) || !signed && n < 0) throw new FormatException("Items integer invalid");
            return n;
        }
        private static string Text(string raw) => JsonFragments.String(raw);
        private static string KeyValue(string raw) { string k = Text(raw); if (!Key(k)) throw new FormatException("Items key invalid"); return k; }
        private static string Uuid(string raw) { string s = Text(raw); if (!FriendsJson.IsUuid(s)) throw new FormatException("Items UUID invalid"); return Guid.ParseExact(s, "D").ToString("D"); }
        private static bool Boolean(string raw) => raw == "true" ? true : raw == "false" ? false : throw new FormatException("Items boolean invalid");
        internal static string Instant(string raw)
        {
            string s = Text(raw);
            if (!DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)) throw new FormatException("Items time invalid");
            return s;
        }
        private static int Bound(string raw, int min, int max) { long n = Integer(raw); if (n < min || n > max) throw new FormatException("Items limit invalid"); return (int)n; }
        private static ItemLimits Limits(string raw)
        {
            var f = JsonFragments.Object(raw);
            // Operator quotas are positive Int32 values, not the old tariff defaults.
            return new ItemLimits { catalog = Bound(f["catalog"], 1, int.MaxValue), inventory = Bound(f["inventory"], 1, int.MaxValue), receipts = Bound(f["receipts"], 1, int.MaxValue) };
        }
        private static T[] Rows<T>(string raw, int max, Func<string, T> parse, Func<T, string> key)
        {
            var rows = JsonFragments.Array(raw);
            if (rows.Count > max) throw new FormatException("Items list bound");
            var keys = new HashSet<string>(StringComparer.Ordinal); var result = new T[rows.Count];
            for (int i = 0; i < rows.Count; i++) { result[i] = parse(rows[i]); if (!keys.Add(key(result[i]))) throw new FormatException("Items duplicate identity"); }
            return result;
        }
        internal static string Quote(string s)
        {
            if (s == null) throw new FormatException("Items string missing");
            var b = new StringBuilder("\"");
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c)) { if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) throw new FormatException("Items invalid Unicode"); b.Append(c).Append(s[++i]); continue; }
                if (char.IsLowSurrogate(c)) throw new FormatException("Items invalid Unicode");
                switch (c)
                {
                    case '"': b.Append("\\\""); break; case '\\': b.Append("\\\\"); break;
                    case '\b': b.Append("\\b"); break; case '\f': b.Append("\\f"); break;
                    case '\n': b.Append("\\n"); break; case '\r': b.Append("\\r"); break; case '\t': b.Append("\\t"); break;
                    default: if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); else b.Append(c); break;
                }
            }
            return b.Append('"').ToString();
        }
        internal static ItemProperty[] Properties(string raw)
        {
            var fields = JsonFragments.Object(raw);
            if (fields.Count > 16) throw new FormatException("Items property count");
            var result = fields.Select(f =>
            {
                if (!Key(f.Key)) throw new FormatException("Items property key invalid");
                var p = new ItemProperty { key = f.Key };
                if (f.Value.StartsWith("\"", StringComparison.Ordinal)) { p.type = ItemPropertyType.String; p.stringValue = Text(f.Value); }
                else if (f.Value == "true" || f.Value == "false") { p.type = ItemPropertyType.Boolean; p.booleanValue = Boolean(f.Value); }
                else { p.type = ItemPropertyType.Integer; p.integerValue = Integer(f.Value, true); }
                return p;
            }).OrderBy(p => p.key, StringComparer.Ordinal).ToArray();
            if (Encoding.UTF8.GetByteCount(PropertiesJson(result)) > 256) throw new FormatException("Items property bytes");
            return result;
        }
        internal static string PropertiesJson(IEnumerable<ItemProperty> values) => "{" + string.Join(",", values.OrderBy(p => p.key, StringComparer.Ordinal).Select(p => Quote(p.key) + ":" +
            (p.type == ItemPropertyType.Integer ? p.integerValue.ToString(CultureInfo.InvariantCulture) : p.type == ItemPropertyType.Boolean ? p.booleanValue ? "true" : "false" : Quote(p.stringValue)))) + "}";
        internal static InventorySnapshot Inventory(string raw)
        {
            var f = JsonFragments.Object(raw);
            return new InventorySnapshot { userId = Uuid(f["userId"]), revision = Integer(f["revision"]), serverTime = Instant(f["serverTime"]), limits = Limits(f["limits"]), instances = Rows(f["instances"], int.MaxValue, Instance, x => x.instanceId) };
        }
        private static ItemInstance Instance(string raw)
        {
            var f = JsonFragments.Object(raw);
            return new ItemInstance { instanceId = Uuid(f["instanceId"]), itemKey = KeyValue(f["itemKey"]), properties = Properties(f["properties"]), equipped = Boolean(f["equipped"]), createdAt = Instant(f["createdAt"]) };
        }
        internal static ItemCatalogPage Catalog(string raw)
        {
            var f = JsonFragments.Object(raw); string next = f["nextAfterKey"] == "null" ? null : KeyValue(f["nextAfterKey"]);
            // The permanent UInt16 template-slot format has 65536 slots; a page remains at most 50 rows.
            return new ItemCatalogPage { revision = Integer(f["revision"]), definitions = Rows(f["definitions"], 50, Definition, x => x.key), nextAfterKey = next, total = Bound(f["total"], 0, 65536), limits = Limits(f["limits"]), serverTime = Instant(f["serverTime"]) };
        }
        private static ItemDefinition Definition(string raw)
        {
            var f = JsonFragments.Object(raw); string name = Text(f["name"]);
            if (string.IsNullOrWhiteSpace(name) || Encoding.UTF8.GetByteCount(name) > 120) throw new FormatException("Items name invalid");
            var result = new ItemDefinition { key = KeyValue(f["key"]), name = name, type = KeyValue(f["type"]), rarity = KeyValue(f["rarity"]), active = Boolean(f["active"]), baseProperties = Properties(f["baseProperties"]), propertyRules = Rows(f["propertyRules"], 16, Rule, x => x.key) };
            foreach (var rule in result.propertyRules)
            {
                var value = Array.Find(result.baseProperties, p => p.key == rule.key);
                if (value == null || value.type != ItemPropertyType.Integer || value.integerValue < rule.min || value.integerValue > rule.max) throw new FormatException("Items property rule mismatch");
            }
            return result;
        }
        private static ItemPropertyRule Rule(string raw)
        {
            var f = JsonFragments.Object(raw); var r = new ItemPropertyRule { key = KeyValue(f["key"]), min = Integer(f["min"], true), max = Integer(f["max"], true), maxChangePerCall = Integer(f["maxChangePerCall"]) };
            if (r.min > r.max || r.maxChangePerCall == 0) throw new FormatException("Items property bounds");
            return r;
        }
        internal static ItemReward[] Rewards(string raw)
        {
            var result = Rows(raw, 4, row => { var f = JsonFragments.Object(row); return new ItemReward { itemKey = KeyValue(f["itemKey"]), quantity = Bound(f["quantity"], 1, 4) }; }, x => x.itemKey);
            if (result.Sum(x => x.quantity) > 4) throw new FormatException("Items reward bound");
            return result;
        }
        internal static ItemReference[] References(string raw) => Rows(raw, 4, row => { var f = JsonFragments.Object(row); return new ItemReference { instanceId = Uuid(f["instanceId"]), itemKey = KeyValue(f["itemKey"]) }; }, x => x.instanceId);
        internal static T Clone<T>(T value) where T : class
        {
            if (value == null) return null;
            object copy = value is InventorySnapshot s ? new InventorySnapshot { userId = s.userId, revision = s.revision, serverTime = s.serverTime, limits = Clone(s.limits), instances = Array.ConvertAll(s.instances, Clone) }
                : value is ItemCatalogPage c ? new ItemCatalogPage { revision = c.revision, definitions = Array.ConvertAll(c.definitions, Clone), nextAfterKey = c.nextAfterKey, total = c.total, limits = Clone(c.limits), serverTime = c.serverTime }
                : value is ItemInstance i ? new ItemInstance { instanceId = i.instanceId, itemKey = i.itemKey, properties = Array.ConvertAll(i.properties, Clone), equipped = i.equipped, createdAt = i.createdAt }
                : value is ItemDefinition d ? new ItemDefinition { key = d.key, name = d.name, type = d.type, rarity = d.rarity, active = d.active, baseProperties = Array.ConvertAll(d.baseProperties, Clone), propertyRules = Array.ConvertAll(d.propertyRules, Clone) }
                : value is ItemProperty p ? new ItemProperty { key = p.key, type = p.type, integerValue = p.integerValue, booleanValue = p.booleanValue, stringValue = p.stringValue }
                : value is ItemPropertyRule r ? new ItemPropertyRule { key = r.key, min = r.min, max = r.max, maxChangePerCall = r.maxChangePerCall }
                : value is ItemLimits l ? new ItemLimits { catalog = l.catalog, inventory = l.inventory, receipts = l.receipts }
                : throw new ArgumentException("Unknown Items copy");
            return (T)copy;
        }
    }
}
