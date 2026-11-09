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
        /// <param name="text">JSON text.</param>
        /// <returns>The parsed value.</returns>
        /// <exception cref="FormatException">When the text is not valid JSON.</exception>
        public static object Parse(string text)
        {
            Parser parser = new Parser(text);
            return parser.ParseDocument();
        }

        /// <summary>Makes a quoted, escaped JSON string literal.</summary>
        /// <param name="value">Text to quote.</param>
        /// <returns>The JSON string literal, quotes included.</returns>
        public static string MakeJsonString(string value)
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
                            string hex = ((int)c).ToString("x4");
                            builder.Append("\\u").Append(hex);
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

        /// <summary>
        /// Recursive-descent JSON parser over one string. Carries its read position; use one parser per document.
        /// </summary>
        private sealed class Parser
        {
            private readonly string text; // JSON being parsed
            private int position; // next character to read

            public Parser(string text)
            {
                this.text = text;
                this.position = 0;
            }

            /// <summary>Parses the whole text as one JSON value.</summary>
            public object ParseDocument()
            {
                object value = ParseValue();
                SkipWhitespace();
                if (position != text.Length)
                {
                    throw new FormatException("trailing characters after JSON value");
                }
                return value;
            }

            private object ParseValue()
            {
                SkipWhitespace();
                if (position >= text.Length)
                {
                    throw new FormatException("unexpected end of JSON");
                }
                char c = text[position];
                if (c == '{')
                {
                    return ParseObject();
                }
                if (c == '[')
                {
                    return ParseArray();
                }
                if (c == '"')
                {
                    return ParseString();
                }
                if (IsWordAt("true"))
                {
                    position += 4;
                    return true;
                }
                if (IsWordAt("false"))
                {
                    position += 5;
                    return false;
                }
                if (IsWordAt("null"))
                {
                    position += 4;
                    return null;
                }
                return ParseNumber();
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> result = new Dictionary<string, object>();
                position++; // '{'
                SkipWhitespace();
                if (position < text.Length && text[position] == '}')
                {
                    position++;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    CheckAndSkip(':');
                    object value = ParseValue();
                    result[key] = value;
                    SkipWhitespace();
                    if (position < text.Length && text[position] == ',')
                    {
                        position++;
                        continue;
                    }
                    CheckAndSkip('}');
                    return result;
                }
            }

            private List<object> ParseArray()
            {
                List<object> result = new List<object>();
                position++; // '['
                SkipWhitespace();
                if (position < text.Length && text[position] == ']')
                {
                    position++;
                    return result;
                }
                while (true)
                {
                    object item = ParseValue();
                    result.Add(item);
                    SkipWhitespace();
                    if (position < text.Length && text[position] == ',')
                    {
                        position++;
                        continue;
                    }
                    CheckAndSkip(']');
                    return result;
                }
            }

            private string ParseString()
            {
                CheckAndSkip('"');
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

                    // escape sequence
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
                            string hex = text.Substring(position, 4);
                            int code = int.Parse(hex, NumberStyles.HexNumber);
                            builder.Append((char)code);
                            position += 4;
                            break;
                        default: builder.Append(escaped); break;
                    }
                }
                throw new FormatException("unterminated JSON string");
            }

            private double ParseNumber()
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
                string number = text.Substring(start, position - start);
                return double.Parse(number, CultureInfo.InvariantCulture);
            }

            private void SkipWhitespace()
            {
                while (position < text.Length && char.IsWhiteSpace(text[position]))
                {
                    position++;
                }
            }

            private void CheckAndSkip(char expected)
            {
                if (position >= text.Length || text[position] != expected)
                {
                    throw new FormatException("expected '" + expected + "' in JSON");
                }
                position++;
            }

            private bool IsWordAt(string word)
            {
                return string.CompareOrdinal(text, position, word, 0, word.Length) == 0;
            }
        }
    }
}
