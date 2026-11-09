using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DriverSim.Net
{
    /// <summary>
    /// Tiny JSON reader/writer for NetworkTables 4 control messages. Parses into Dictionary&lt;string, object&gt;,
    /// List&lt;object&gt;, string, double, bool and null.
    /// </summary>
    public static class MiniJson
    {
        /// <summary>Parses one JSON value.</summary>
        /// <exception cref="FormatException">When the text is not valid JSON.</exception>
        public static object Parse(string text)
        {
            int position = 0;
            object value = ParseValue(text, ref position);
            SkipWhitespace(text, ref position);
            if (position != text.Length)
            {
                throw new FormatException("trailing characters after JSON value");
            }
            return value;
        }

        /// <summary>Quotes and escapes a string for JSON.</summary>
        public static string Quote(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        private static object ParseValue(string text, ref int position)
        {
            SkipWhitespace(text, ref position);
            if (position >= text.Length)
            {
                throw new FormatException("unexpected end of JSON");
            }
            char c = text[position];
            if (c == '{')
            {
                return ParseObject(text, ref position);
            }
            if (c == '[')
            {
                return ParseArray(text, ref position);
            }
            if (c == '"')
            {
                return ParseString(text, ref position);
            }
            if (Matches(text, position, "true"))
            {
                position += 4;
                return true;
            }
            if (Matches(text, position, "false"))
            {
                position += 5;
                return false;
            }
            if (Matches(text, position, "null"))
            {
                position += 4;
                return null;
            }
            return ParseNumber(text, ref position);
        }

        private static Dictionary<string, object> ParseObject(string text, ref int position)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            position++; // '{'
            SkipWhitespace(text, ref position);
            if (position < text.Length && text[position] == '}')
            {
                position++;
                return result;
            }
            while (true)
            {
                SkipWhitespace(text, ref position);
                string key = ParseString(text, ref position);
                SkipWhitespace(text, ref position);
                Expect(text, ref position, ':');
                result[key] = ParseValue(text, ref position);
                SkipWhitespace(text, ref position);
                if (position < text.Length && text[position] == ',')
                {
                    position++;
                    continue;
                }
                Expect(text, ref position, '}');
                return result;
            }
        }

        private static List<object> ParseArray(string text, ref int position)
        {
            List<object> result = new List<object>();
            position++; // '['
            SkipWhitespace(text, ref position);
            if (position < text.Length && text[position] == ']')
            {
                position++;
                return result;
            }
            while (true)
            {
                result.Add(ParseValue(text, ref position));
                SkipWhitespace(text, ref position);
                if (position < text.Length && text[position] == ',')
                {
                    position++;
                    continue;
                }
                Expect(text, ref position, ']');
                return result;
            }
        }

        private static string ParseString(string text, ref int position)
        {
            Expect(text, ref position, '"');
            StringBuilder builder = new StringBuilder();
            while (position < text.Length)
            {
                char c = text[position];
                position++;
                if (c == '"')
                {
                    return builder.ToString();
                }
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }
                if (position >= text.Length)
                {
                    break;
                }
                char escaped = text[position];
                position++;
                switch (escaped)
                {
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'u':
                        if (position + 4 > text.Length)
                        {
                            throw new FormatException("truncated \\u escape");
                        }
                        builder.Append((char)int.Parse(text.Substring(position, 4), NumberStyles.HexNumber));
                        position += 4;
                        break;
                    default: builder.Append(escaped); break;
                }
            }
            throw new FormatException("unterminated JSON string");
        }

        private static double ParseNumber(string text, ref int position)
        {
            int start = position;
            while (position < text.Length && "+-0123456789.eE".IndexOf(text[position]) >= 0)
            {
                position++;
            }
            if (start == position)
            {
                throw new FormatException("unexpected character '" + text[position] + "' in JSON");
            }
            return double.Parse(text.Substring(start, position - start), CultureInfo.InvariantCulture);
        }

        private static void SkipWhitespace(string text, ref int position)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }
        }

        private static void Expect(string text, ref int position, char expected)
        {
            if (position >= text.Length || text[position] != expected)
            {
                throw new FormatException("expected '" + expected + "' in JSON");
            }
            position++;
        }

        private static bool Matches(string text, int position, string word)
        {
            return string.CompareOrdinal(text, position, word, 0, word.Length) == 0;
        }
    }
}
