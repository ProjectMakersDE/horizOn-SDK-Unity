using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PM.horizOn.Cloud.Helper
{
    /// <summary>Bounded JSON fragments preserve integer lexemes and dictionary strings before JsonUtility conversion.</summary>
    internal sealed class JsonFragments
    {
        private const int MaxBytes = 8 * 1024 * 1024;
        private static readonly Regex Primitive = new Regex(@"\G(?:true|false|null|-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)");
        private readonly string _json;
        private int _at;
        private JsonFragments(string json)
        {
            if (json == null || json.Length > MaxBytes || Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new FormatException("JSON size limit");
            _json = json;
        }
        public static Dictionary<string, string> Object(string json)
        {
            var reader = new JsonFragments(json); var result = reader.ReadObject(0); reader.End(); return result;
        }
        public static List<string> Array(string json)
        {
            var reader = new JsonFragments(json); var result = reader.ReadArray(0); reader.End(); return result;
        }
        public static string String(string json)
        {
            var reader = new JsonFragments(json); reader.ReadString(); reader.End();
            return JsonUtility.FromJson<StringValue>("{\"value\":" + json + "}").value;
        }
        private Dictionary<string, string> ReadObject(int depth)
        {
            CheckDepth(depth); Expect('{'); var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Take('}')) return result;
            do
            {
                Space(); int keyStart = _at; ReadString(); string key = String(_json.Substring(keyStart, _at - keyStart)); Expect(':');
                Space(); int start = _at; ReadValue(depth + 1);
                if (result.Count >= 2048 || result.ContainsKey(key)) throw new FormatException("JSON duplicate/key limit");
                result.Add(key, _json.Substring(start, _at - start));
                if (Take('}')) return result;
                Expect(',');
            } while (true);
        }
        private List<string> ReadArray(int depth)
        {
            CheckDepth(depth); Expect('['); var result = new List<string>();
            if (Take(']')) return result;
            do
            {
                Space(); int start = _at; ReadValue(depth + 1);
                if (result.Count >= 2048) throw new FormatException("JSON array limit");
                result.Add(_json.Substring(start, _at - start));
                if (Take(']')) return result;
                Expect(',');
            } while (true);
        }
        private void ReadValue(int depth)
        {
            CheckDepth(depth); Space(); if (_at >= _json.Length) throw new FormatException("JSON value missing");
            switch (_json[_at])
            {
                case '{': ReadObject(depth); return;
                case '[': ReadArray(depth); return;
                case '"': ReadString(); return;
                default:
                    var match = Primitive.Match(_json, _at);
                    if (!match.Success) throw new FormatException("JSON primitive invalid");
                    _at += match.Length; return;
            }
        }
        private void ReadString()
        {
            Expect('"');
            while (_at < _json.Length)
            {
                char value = _json[_at++];
                if (value == '"') return;
                if (value < 32) throw new FormatException("JSON control character");
                if (value != '\\') continue;
                if (_at >= _json.Length) throw new FormatException("JSON escape missing");
                char escaped = _json[_at++];
                if ("\"\\/bfnrt".IndexOf(escaped) >= 0) continue;
                if (escaped != 'u' || _at + 4 > _json.Length) throw new FormatException("JSON escape invalid");
                for (int i = 0; i < 4; i++) if (!Uri.IsHexDigit(_json[_at++])) throw new FormatException("JSON unicode escape invalid");
            }
            throw new FormatException("JSON string unterminated");
        }
        private void Expect(char value) { if (!Take(value)) throw new FormatException("JSON delimiter missing"); }
        private bool Take(char value) { Space(); if (_at >= _json.Length || _json[_at] != value) return false; _at++; return true; }
        private void Space() { while (_at < _json.Length && (_json[_at] == ' ' || _json[_at] == '\r' || _json[_at] == '\n' || _json[_at] == '\t')) _at++; }
        private void End() { Space(); if (_at != _json.Length) throw new FormatException("JSON trailing content"); }
        private static void CheckDepth(int depth) { if (depth > 24) throw new FormatException("JSON nesting limit"); }
        [Serializable] private sealed class StringValue { public string value = null; }
    }
}
