using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

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
            var reader = new JsonFragments(json); string value = reader.ReadString(); reader.End(); return value;
        }
        private Dictionary<string, string> ReadObject(int depth)
        {
            CheckDepth(depth); Expect('{'); var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Take('}')) return result;
            do
            {
                string key = ReadString(); Expect(':');
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
        private string ReadString()
        {
            Expect('"'); var decoded = new StringBuilder();
            while (_at < _json.Length)
            {
                char value = _json[_at++];
                if (value == '"')
                {
                    string result = decoded.ToString();
                    for (int i = 0; i < result.Length; i++)
                    {
                        if (char.IsHighSurrogate(result[i])) { if (++i >= result.Length || !char.IsLowSurrogate(result[i])) throw new FormatException("JSON unpaired surrogate"); }
                        else if (char.IsLowSurrogate(result[i])) throw new FormatException("JSON unpaired surrogate");
                    }
                    return result;
                }
                if (value < 32) throw new FormatException("JSON control character");
                if (value != '\\') { decoded.Append(value); continue; }
                if (_at >= _json.Length) throw new FormatException("JSON escape missing");
                char escaped = _json[_at++];
                switch (escaped)
                {
                    case '"': decoded.Append('"'); continue; case '\\': decoded.Append('\\'); continue; case '/': decoded.Append('/'); continue;
                    case 'b': decoded.Append('\b'); continue; case 'f': decoded.Append('\f'); continue;
                    case 'n': decoded.Append('\n'); continue; case 'r': decoded.Append('\r'); continue; case 't': decoded.Append('\t'); continue;
                }
                if (escaped != 'u' || _at + 4 > _json.Length) throw new FormatException("JSON escape invalid");
                int code = 0;
                for (int i = 0; i < 4; i++)
                {
                    char hex = _json[_at++]; if (!Uri.IsHexDigit(hex)) throw new FormatException("JSON unicode escape invalid");
                    code = code * 16 + (hex <= '9' ? hex - '0' : char.ToLowerInvariant(hex) - 'a' + 10);
                }
                decoded.Append((char)code);
            }
            throw new FormatException("JSON string unterminated");
        }
        private void Expect(char value) { if (!Take(value)) throw new FormatException("JSON delimiter missing"); }
        private bool Take(char value) { Space(); if (_at >= _json.Length || _json[_at] != value) return false; _at++; return true; }
        private void Space() { while (_at < _json.Length && (_json[_at] == ' ' || _json[_at] == '\r' || _json[_at] == '\n' || _json[_at] == '\t')) _at++; }
        private void End() { Space(); if (_at != _json.Length) throw new FormatException("JSON trailing content"); }
        private static void CheckDepth(int depth) { if (depth > 24) throw new FormatException("JSON nesting limit"); }
    }
}
