using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DroneStar.Core
{
    public enum JsonType
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    /// <summary>
    /// A small JSON DOM with a strict parser and a deterministic writer. It needs no reflection, so it
    /// behaves identically in the editor, IL2CPP/WebGL builds and plain .NET test runs.
    /// </summary>
    public sealed class JsonValue
    {
        public static readonly JsonValue Null = new JsonValue(JsonType.Null);

        readonly List<JsonValue> items;
        readonly List<KeyValuePair<string, JsonValue>> members;

        JsonValue(JsonType type)
        {
            Type = type;
            if (type == JsonType.Array) items = new List<JsonValue>();
            if (type == JsonType.Object) members = new List<KeyValuePair<string, JsonValue>>();
        }

        public JsonType Type { get; }
        public bool BoolValue { get; private set; }
        public double NumberValue { get; private set; }
        public string StringValue { get; private set; }

        public IReadOnlyList<JsonValue> Items => items ?? (IReadOnlyList<JsonValue>)Array.Empty<JsonValue>();
        public IReadOnlyList<KeyValuePair<string, JsonValue>> Members => members ?? (IReadOnlyList<KeyValuePair<string, JsonValue>>)Array.Empty<KeyValuePair<string, JsonValue>>();

        public static JsonValue NewObject() => new JsonValue(JsonType.Object);
        public static JsonValue NewArray() => new JsonValue(JsonType.Array);
        public static JsonValue From(bool b) => new JsonValue(JsonType.Bool) { BoolValue = b };

        public static JsonValue From(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return Null;
            return new JsonValue(JsonType.Number) { NumberValue = d };
        }

        /// <summary>Stores a float by its shortest round-trip text, so 0.1f is written as 0.1 rather than 0.100000001490116.</summary>
        public static JsonValue From(float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) return Null;
            return From(double.Parse(f.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        public static JsonValue From(string s) => s == null ? Null : new JsonValue(JsonType.String) { StringValue = s };

        public JsonValue Add(JsonValue item)
        {
            if (items == null) throw new InvalidOperationException("Not a JSON array.");
            items.Add(item ?? Null);
            return this;
        }

        public JsonValue Set(string key, JsonValue value)
        {
            if (members == null) throw new InvalidOperationException("Not a JSON object.");
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].Key == key)
                {
                    members[i] = new KeyValuePair<string, JsonValue>(key, value ?? Null);
                    return this;
                }
            }
            members.Add(new KeyValuePair<string, JsonValue>(key, value ?? Null));
            return this;
        }

        public JsonValue Set(string key, double value) => Set(key, From(value));
        public JsonValue Set(string key, float value) => Set(key, From(value));
        public JsonValue Set(string key, int value) => Set(key, From((double)value));
        public JsonValue Set(string key, string value) => Set(key, From(value));
        public JsonValue Set(string key, bool value) => Set(key, From(value));

        /// <summary>Member lookup; returns null when absent or when this is not an object.</summary>
        public JsonValue Get(string key)
        {
            if (members == null) return null;
            foreach (KeyValuePair<string, JsonValue> kv in members)
            {
                if (kv.Key == key) return kv.Value;
            }
            return null;
        }

        public double GetNumber(string key, double fallback)
        {
            JsonValue v = Get(key);
            return v != null && v.Type == JsonType.Number ? v.NumberValue : fallback;
        }

        public float GetFloat(string key, float fallback) => (float)GetNumber(key, fallback);

        public int GetInt(string key, int fallback)
        {
            double d = GetNumber(key, fallback);
            if (d > int.MaxValue) return int.MaxValue;
            if (d < int.MinValue) return int.MinValue;
            return (int)Math.Round(d);
        }

        public bool GetBool(string key, bool fallback)
        {
            JsonValue v = Get(key);
            return v != null && v.Type == JsonType.Bool ? v.BoolValue : fallback;
        }

        public string GetString(string key, string fallback)
        {
            JsonValue v = Get(key);
            return v != null && v.Type == JsonType.String ? v.StringValue : fallback;
        }

        // ------------------------------------------------------------------ writing

        public string ToJson(bool pretty = true)
        {
            var sb = new StringBuilder();
            Write(sb, pretty, 0);
            return sb.ToString();
        }

        void Write(StringBuilder sb, bool pretty, int depth)
        {
            switch (Type)
            {
                case JsonType.Null:
                    sb.Append("null");
                    break;
                case JsonType.Bool:
                    sb.Append(BoolValue ? "true" : "false");
                    break;
                case JsonType.Number:
                    sb.Append(FormatNumber(NumberValue));
                    break;
                case JsonType.String:
                    WriteString(sb, StringValue);
                    break;
                case JsonType.Array:
                    if (items.Count == 0)
                    {
                        sb.Append("[]");
                        break;
                    }
                    bool inline = true;
                    foreach (JsonValue item in items)
                    {
                        if (item.Type == JsonType.Array || item.Type == JsonType.Object) inline = false;
                    }
                    sb.Append('[');
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (i > 0) sb.Append(inline && pretty ? ", " : ",");
                        if (!inline) NewLine(sb, pretty, depth + 1);
                        items[i].Write(sb, pretty, depth + 1);
                    }
                    if (!inline) NewLine(sb, pretty, depth);
                    sb.Append(']');
                    break;
                case JsonType.Object:
                    if (members.Count == 0)
                    {
                        sb.Append("{}");
                        break;
                    }
                    sb.Append('{');
                    for (int i = 0; i < members.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        NewLine(sb, pretty, depth + 1);
                        WriteString(sb, members[i].Key);
                        sb.Append(pretty ? ": " : ":");
                        members[i].Value.Write(sb, pretty, depth + 1);
                    }
                    NewLine(sb, pretty, depth);
                    sb.Append('}');
                    break;
            }
        }

        static void NewLine(StringBuilder sb, bool pretty, int depth)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        static string FormatNumber(double d)
        {
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------------ parsing

        public const int MaxDepth = 64;

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var parser = new Parser(text);
            parser.SkipWhitespace();
            JsonValue value = parser.ParseValue(0);
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw parser.Error("Unexpected text after the JSON value");
            return value;
        }

        sealed class Parser
        {
            readonly string s;
            int pos;

            public Parser(string text)
            {
                s = text;
                // Tolerate a UTF-8 byte order mark decoded as U+FEFF.
                if (s.Length > 0 && s[0] == '﻿') pos = 1;
            }

            public bool AtEnd => pos >= s.Length;

            static bool IsDigit(char c) => c >= '0' && c <= '9';

            static int HexValue(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            public FormatException Error(string message)
            {
                int line = 1, col = 1;
                for (int i = 0; i < pos && i < s.Length; i++)
                {
                    if (s[i] == '\n')
                    {
                        line++;
                        col = 1;
                    }
                    else
                    {
                        col++;
                    }
                }
                return new FormatException(string.Format(CultureInfo.InvariantCulture, "{0} at line {1}, column {2}.", message, line, col));
            }

            public void SkipWhitespace()
            {
                while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\n' || s[pos] == '\r')) pos++;
            }

            public JsonValue ParseValue(int depth)
            {
                if (depth > MaxDepth) throw Error("JSON is nested too deeply");
                if (AtEnd) throw Error("Unexpected end of JSON");
                char c = s[pos];
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return From(ParseString());
                    case 't': Expect("true"); return From(true);
                    case 'f': Expect("false"); return From(false);
                    case 'n': Expect("null"); return Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, pos, word, 0, word.Length) != 0) throw Error("Expected '" + word + "'");
                pos += word.Length;
            }

            JsonValue ParseObject(int depth)
            {
                JsonValue obj = NewObject();
                pos++;
                SkipWhitespace();
                if (!AtEnd && s[pos] == '}')
                {
                    pos++;
                    return obj;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || s[pos] != '"') throw Error("Expected a property name");
                    string key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || s[pos] != ':') throw Error("Expected ':'");
                    pos++;
                    SkipWhitespace();
                    obj.Set(key, ParseValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated object");
                    if (s[pos] == ',')
                    {
                        pos++;
                        continue;
                    }
                    if (s[pos] == '}')
                    {
                        pos++;
                        return obj;
                    }
                    throw Error("Expected ',' or '}'");
                }
            }

            JsonValue ParseArray(int depth)
            {
                JsonValue arr = NewArray();
                pos++;
                SkipWhitespace();
                if (!AtEnd && s[pos] == ']')
                {
                    pos++;
                    return arr;
                }
                while (true)
                {
                    SkipWhitespace();
                    arr.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated array");
                    if (s[pos] == ',')
                    {
                        pos++;
                        continue;
                    }
                    if (s[pos] == ']')
                    {
                        pos++;
                        return arr;
                    }
                    throw Error("Expected ',' or ']'");
                }
            }

            string ParseString()
            {
                pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    char c = s[pos++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Error("Control character in string");
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd) throw Error("Unterminated escape");
                    char e = s[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > s.Length) throw Error("Truncated \\u escape");
                            int code = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int digit = HexValue(s[pos + k]);
                                if (digit < 0) throw Error("Bad \\u escape");
                                code = code * 16 + digit;
                            }
                            sb.Append((char)code);
                            pos += 4;
                            break;
                        default:
                            throw Error("Unknown escape '\\" + e + "'");
                    }
                }
            }

            JsonValue ParseNumber()
            {
                int start = pos;
                if (s[pos] == '-') pos++;
                if (AtEnd || !IsDigit(s[pos])) throw Error("Malformed number");
                if (s[pos] == '0')
                {
                    pos++;
                }
                else
                {
                    while (!AtEnd && IsDigit(s[pos])) pos++;
                }
                if (!AtEnd && s[pos] == '.')
                {
                    pos++;
                    if (AtEnd || !IsDigit(s[pos])) throw Error("Malformed number");
                    while (!AtEnd && IsDigit(s[pos])) pos++;
                }
                if (!AtEnd && (s[pos] == 'e' || s[pos] == 'E'))
                {
                    pos++;
                    if (!AtEnd && (s[pos] == '+' || s[pos] == '-')) pos++;
                    if (AtEnd || !IsDigit(s[pos])) throw Error("Malformed number");
                    while (!AtEnd && IsDigit(s[pos])) pos++;
                }
                string token = s.Substring(start, pos - start);
                // Mono (Unity) rejects out-of-range literals while .NET Core returns ±∞; treat both alike.
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsInfinity(d))
                {
                    throw Error("Number out of range");
                }
                return From(d);
            }
        }
    }
}
