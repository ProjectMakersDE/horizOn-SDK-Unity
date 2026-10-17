using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PM.horizOn.Cloud.Helper
{
    /// <summary>Byte-bounded JSON fragments preserve original integer/string lexemes. Collection quotas belong to feature DTOs.</summary>
    internal sealed class JsonFragments
    {
        private const int MaxBytes = 32 * 1024 * 1024; // Common technical frame; never a product/list quota.
        private static readonly Regex Primitive = new Regex(@"\G(?:true|false|null|-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)");
        private readonly string _json;
        private readonly int _maxElements;
        private readonly bool _finiteNumbers;
        private readonly int _maxDepth;
        private int _at;
        private JsonFragments(string json, int maxElements = int.MaxValue, int maxBytes = MaxBytes, bool finiteNumbers = false, int maxDepth = 24)
        {
            if (maxBytes < 1 || maxElements < 1 || json == null || json.Length > maxBytes || Encoding.UTF8.GetByteCount(json) > maxBytes) throw new FormatException("JSON size limit");
            _json = json;
            _maxElements = maxElements;
            _finiteNumbers = finiteNumbers;
            _maxDepth = maxDepth;
        }
        public static Dictionary<string, string> Object(string json, int maxElements = int.MaxValue, int maxBytes = MaxBytes, bool finiteNumbers = false, int maxDepth = 24)
        {
            var reader = new JsonFragments(json, maxElements, maxBytes, finiteNumbers, maxDepth); var result = reader.ReadObject(); reader.End(); return result;
        }
        public static List<string> Array(string json, int maxElements = int.MaxValue, int maxBytes = MaxBytes, int maxDepth = 24)
        {
            var reader = new JsonFragments(json, maxElements, maxBytes, maxDepth: maxDepth); var result = reader.ReadArray(); reader.End(); return result;
        }
        public static string String(string json) => String(json, MaxBytes);
        public static string String(string json, int maxBytes)
        {
            var reader = new JsonFragments(json, maxBytes: maxBytes); string value = reader.ReadString(); reader.End(); return value;
        }
        private Dictionary<string, string> ReadObject()
        {
            Expect('{'); var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Take('}')) return result;
            do
            {
                string key = ReadString(); Expect(':');
                Space(); int start = _at; ReadValue();
                if (result.Count >= _maxElements || result.ContainsKey(key)) throw new FormatException("JSON duplicate/key limit");
                result.Add(key, _json.Substring(start, _at - start));
                if (Take('}')) return result;
                Expect(',');
            } while (true);
        }
        private List<string> ReadArray()
        {
            Expect('['); var result = new List<string>();
            if (Take(']')) return result;
            do
            {
                Space(); int start = _at; ReadValue();
                if (result.Count >= _maxElements) throw new FormatException("JSON array limit");
                result.Add(_json.Substring(start, _at - start));
                if (Take(']')) return result;
                Expect(',');
            } while (true);
        }
        private sealed class Frame
        {
            internal bool Object, AllowEnd = true, AfterValue;
            internal int Count, Depth;
            internal readonly HashSet<string> Keys = new HashSet<string>(StringComparer.Ordinal);
        }
        private void ReadValue()
        {
            // Configurable group nesting must not consume the managed call stack.
            // Each frame belongs to bytes already admitted by the independent transport frame.
            var frames = new Stack<Frame>(); ReadAtom(frames, 1);
            while (frames.Count != 0)
            {
                var frame = frames.Peek(); char close = frame.Object ? '}' : ']';
                if (frame.AfterValue)
                {
                    if (Take(close)) { frames.Pop(); continue; }
                    Expect(','); frame.AfterValue = false; frame.AllowEnd = false; continue;
                }
                if (frame.AllowEnd && Take(close)) { frames.Pop(); continue; }
                if (++frame.Count > _maxElements) throw new FormatException("JSON collection limit");
                if (frame.Object)
                {
                    string key = ReadString();
                    if (!frame.Keys.Add(key)) throw new FormatException("JSON duplicate key");
                    Expect(':');
                }
                frame.AfterValue = true; ReadAtom(frames, frame.Depth + 1);
            }
        }
        private void ReadAtom(Stack<Frame> frames, int depth)
        {
            if (depth > _maxDepth) throw new FormatException("JSON nesting limit");
            Space(); if (_at >= _json.Length) throw new FormatException("JSON value missing");
            switch (_json[_at])
            {
                case '{': _at++; frames.Push(new Frame { Object = true, Depth = depth }); return;
                case '[': _at++; frames.Push(new Frame { Depth = depth }); return;
                case '"': ReadString(); return;
                default:
                    var match = Primitive.Match(_json, _at);
                    if (!match.Success) throw new FormatException("JSON primitive invalid");
                    if (_finiteNumbers && match.Value != "true" && match.Value != "false" && match.Value != "null"
                        && (!double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsInfinity(number) || double.IsNaN(number))) throw new FormatException("JSON finite number required");
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
    }
}
