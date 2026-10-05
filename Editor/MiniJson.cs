using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DreamLayer.SpriteImporter
{
    /// <summary>
    /// Minimal JSON reader. JsonUtility cannot read DreamLayer's atlas because frames are a
    /// dictionary keyed by frame name, so this parses into Dictionary / List / double / string / bool.
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            var p = new Parser(json);
            var value = p.ReadValue();
            p.SkipWhitespace();
            if (!p.AtEnd) throw new FormatException("Unexpected trailing characters in JSON");
            return value;
        }

        private sealed class Parser
        {
            private readonly string s;
            private int i;

            public Parser(string source)
            {
                s = source;
            }

            public bool AtEnd => i >= s.Length;

            public void SkipWhitespace()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            public object ReadValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new FormatException("Unexpected end of JSON");
                char c = s[i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException("Expected " + word + " at " + i);
                i += word.Length;
            }

            private Dictionary<string, object> ReadObject()
            {
                var obj = new Dictionary<string, object>();
                i++; // {
                SkipWhitespace();
                if (s[i] == '}') { i++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    string key = ReadString();
                    SkipWhitespace();
                    if (s[i] != ':') throw new FormatException("Expected ':' at " + i);
                    i++;
                    obj[key] = ReadValue();
                    SkipWhitespace();
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return obj; }
                    throw new FormatException("Expected ',' or '}' at " + i);
                }
            }

            private List<object> ReadArray()
            {
                var list = new List<object>();
                i++; // [
                SkipWhitespace();
                if (s[i] == ']') { i++; return list; }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return list; }
                    throw new FormatException("Expected ',' or ']' at " + i);
                }
            }

            private string ReadString()
            {
                if (s[i] != '"') throw new FormatException("Expected string at " + i);
                i++;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
                throw new FormatException("Unterminated string");
            }

            private double ReadNumber()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (start == i) throw new FormatException("Unexpected character '" + s[i] + "' at " + i);
                return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
            }
        }
    }
}
