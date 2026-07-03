using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StrangeApe.OpenUnityMcp
{
    internal static class McpJson
    {
        public static object Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var parser = new Parser(json);
            return parser.ParseRoot();
        }

        public static string Stringify(object value)
        {
            var builder = new StringBuilder(256);
            WriteValue(builder, value);
            return builder.ToString();
        }

        public static Dictionary<string, object> Object(params object[] pairs)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                result[(string)pairs[i]] = pairs[i + 1];
            }

            return result;
        }

        public static List<object> Array(params object[] values)
        {
            return new List<object>(values);
        }

        public static string AsString(Dictionary<string, object> map, string key, string fallback = null)
        {
            if (map == null || !map.TryGetValue(key, out var value) || value == null)
            {
                return fallback;
            }

            return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static int AsInt(Dictionary<string, object> map, string key, int fallback = 0)
        {
            if (map == null || !map.TryGetValue(key, out var value) || value == null)
            {
                return fallback;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        public static long AsLong(Dictionary<string, object> map, string key, long fallback = 0)
        {
            if (map == null || !map.TryGetValue(key, out var value) || value == null)
            {
                return fallback;
            }

            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        public static bool AsBool(Dictionary<string, object> map, string key, bool fallback = false)
        {
            if (map == null || !map.TryGetValue(key, out var value) || value == null)
            {
                return fallback;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed) ? parsed : fallback;
        }

        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static List<object> AsArray(object value)
        {
            return value as List<object>;
        }

        private static void WriteValue(StringBuilder builder, object value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            if (value is string stringValue)
            {
                WriteString(builder, stringValue);
                return;
            }

            if (value is bool boolValue)
            {
                builder.Append(boolValue ? "true" : "false");
                return;
            }

            if (value is IDictionary dictionary)
            {
                builder.Append('{');
                var first = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteString(builder, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                    builder.Append(':');
                    WriteValue(builder, entry.Value);
                }

                builder.Append('}');
                return;
            }

            if (value is IEnumerable enumerable && !(value is byte[]))
            {
                builder.Append('[');
                var first = true;
                foreach (var item in enumerable)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteValue(builder, item);
                }

                builder.Append(']');
                return;
            }

            if (value is float || value is double || value is decimal)
            {
                var doubleValue = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                var text = doubleValue.ToString("R", CultureInfo.InvariantCulture);
                if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
                {
                    // Bare NaN/Infinity tokens are invalid JSON and would corrupt the whole document.
                    WriteString(builder, text);
                    return;
                }

                builder.Append(text);
                return;
            }

            if (value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong)
            {
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            WriteString(builder, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private sealed class Parser
        {
            // Bounds mutually recursive ParseValue/ParseObject/ParseArray stack growth; without it a
            // deeply nested body overflows the stack and StackOverflowException cannot be caught.
            private const int MaxDepth = 64;

            private readonly string _json;
            private int _index;
            private int _depth;

            public Parser(string json)
            {
                _json = json;
            }

            public object ParseRoot()
            {
                SkipWhitespace();
                var value = ParseValue();
                SkipWhitespace();
                if (_index != _json.Length)
                {
                    throw Error("Unexpected trailing JSON content.");
                }

                return value;
            }

            private object ParseValue()
            {
                SkipWhitespace();
                if (_index >= _json.Length)
                {
                    throw Error("Unexpected end of JSON.");
                }

                switch (_json[_index])
                {
                    case '{':
                        return ParseNested(true);
                    case '[':
                        return ParseNested(false);
                    case '"':
                        return ParseString();
                    case 't':
                        ConsumeLiteral("true");
                        return true;
                    case 'f':
                        ConsumeLiteral("false");
                        return false;
                    case 'n':
                        ConsumeLiteral("null");
                        return null;
                    default:
                        return ParseNumber();
                }
            }

            private object ParseNested(bool isObject)
            {
                if (_depth >= MaxDepth)
                {
                    throw Error("JSON exceeds the maximum nesting depth of " + MaxDepth + ".");
                }

                _depth++;
                try
                {
                    return isObject ? (object)ParseObject() : ParseArray();
                }
                finally
                {
                    _depth--;
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                Expect('{');
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    var key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                Expect('[');
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (TryConsume(']'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (_index < _json.Length)
                {
                    var c = _json[_index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (_index >= _json.Length)
                    {
                        throw Error("Unexpected end of escape sequence.");
                    }

                    var escaped = _json[_index++];
                    switch (escaped)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escaped);
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            builder.Append(ParseUnicodeEscape());
                            break;
                        default:
                            throw Error("Invalid escape sequence.");
                    }
                }

                throw Error("Unterminated string.");
            }

            private char ParseUnicodeEscape()
            {
                if (_index + 4 > _json.Length)
                {
                    throw Error("Invalid unicode escape.");
                }

                var hex = _json.Substring(_index, 4);
                _index += 4;
                return (char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            private object ParseNumber()
            {
                var start = _index;
                if (_json[_index] == '-')
                {
                    _index++;
                }

                ConsumeDigits();
                var isFloating = false;
                if (_index < _json.Length && _json[_index] == '.')
                {
                    isFloating = true;
                    _index++;
                    ConsumeDigits();
                }

                if (_index < _json.Length && (_json[_index] == 'e' || _json[_index] == 'E'))
                {
                    isFloating = true;
                    _index++;
                    if (_index < _json.Length && (_json[_index] == '+' || _json[_index] == '-'))
                    {
                        _index++;
                    }

                    ConsumeDigits();
                }

                var text = _json.Substring(start, _index - start);
                if (!isFloating && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
                {
                    return longValue;
                }

                // Integers beyond long range fall back to double, matching JavaScript JSON semantics.
                double doubleValue;
                try
                {
                    doubleValue = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    throw Error("Number is out of range.");
                }

                if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
                {
                    throw Error("Number is out of range.");
                }

                return doubleValue;
            }

            private void ConsumeDigits()
            {
                var start = _index;
                while (_index < _json.Length && char.IsDigit(_json[_index]))
                {
                    _index++;
                }

                if (start == _index)
                {
                    throw Error("Expected digit.");
                }
            }

            private void ConsumeLiteral(string literal)
            {
                if (_index + literal.Length > _json.Length || string.CompareOrdinal(_json, _index, literal, 0, literal.Length) != 0)
                {
                    throw Error("Invalid literal.");
                }

                _index += literal.Length;
            }

            private void SkipWhitespace()
            {
                while (_index < _json.Length && char.IsWhiteSpace(_json[_index]))
                {
                    _index++;
                }
            }

            private void Expect(char expected)
            {
                if (_index >= _json.Length || _json[_index] != expected)
                {
                    throw Error("Expected '" + expected + "'.");
                }

                _index++;
            }

            private bool TryConsume(char c)
            {
                if (_index < _json.Length && _json[_index] == c)
                {
                    _index++;
                    return true;
                }

                return false;
            }

            private FormatException Error(string message)
            {
                return new FormatException(message + " Position " + _index + ".");
            }
        }
    }
}
