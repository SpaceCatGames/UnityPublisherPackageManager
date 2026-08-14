using System.Globalization;
using System.Text;

namespace SCG.UPPM.Helpers
{
    /// <summary>
    /// Provides small JSON string helpers that are safe to use in editor utilities.
    /// The API focuses on string tokens and does not implement full JSON serialization.
    /// All methods operate on JSON string content and expect no surrounding quotes unless stated otherwise.
    /// </summary>
    public static class JsonStringUtility
    {
        #region Public API

        /// <summary>
        /// Escapes a .NET string into JSON string content without surrounding quotes.
        /// Control characters are converted to standard JSON escape sequences.
        /// The method returns an empty string for null input.
        /// </summary>
        /// <param name="value">Input string to escape.</param>
        public static string Escape(string value)
        {
            if (value == null)
                return string.Empty;

            var sb = new StringBuilder(value.Length + 16);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        sb.Append(@"\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(ch))
                        {
                            sb.Append("\\u");
                            sb.Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Unescapes JSON string content without surrounding quotes into a .NET string.
        /// Supports common JSON escapes and unicode sequences.
        /// The method returns an empty string when input is null or empty.
        /// </summary>
        /// <param name="value">Escaped JSON string content to unescape.</param>
        public static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch != '\\' || i + 1 >= value.Length)
                {
                    sb.Append(ch);
                    continue;
                }

                var next = value[i + 1];
                switch (next)
                {
                    case '\\':
                        sb.Append('\\');
                        i++;
                        break;
                    case '"':
                        sb.Append('"');
                        i++;
                        break;
                    case 'n':
                        sb.Append('\n');
                        i++;
                        break;
                    case 'r':
                        sb.Append('\r');
                        i++;
                        break;
                    case 't':
                        sb.Append('\t');
                        i++;
                        break;
                    case 'u':
                        if (TryReadUnicode(value, i + 2, out var unicodeChar))
                        {
                            sb.Append(unicodeChar);
                            i += 5;
                            break;
                        }
                        sb.Append('\\');
                        break;
                    default:
                        sb.Append(next);
                        i++;
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Reads a quoted JSON string token starting at the provided index.
        /// When successful, the index is advanced to the character after the closing quote.
        /// The returned value is unescaped into a .NET string.
        /// </summary>
        /// <param name="json">JSON text to read from.</param>
        /// <param name="index">Current index that must point to a quote character.</param>
        /// <param name="value">Parsed and unescaped string value when successful.</param>
        public static bool TryReadQuotedString(string json, ref int index, out string value)
        {
            value = string.Empty;

            if (string.IsNullOrEmpty(json) || index < 0 || index >= json.Length)
                return false;

            if (json[index] != '"')
                return false;

            index++;

            var sb = new StringBuilder();
            while (index < json.Length)
            {
                var ch = json[index++];
                if (ch == '"')
                {
                    value = sb.ToString();
                    value = Unescape(value);
                    return true;
                }

                if (ch != '\\')
                {
                    sb.Append(ch);
                    continue;
                }

                if (index >= json.Length)
                    return false;

                var next = json[index++];
                sb.Append('\\');
                sb.Append(next);

                if (next == 'u')
                {
                    if (index + 4 > json.Length)
                        return false;

                    sb.Append(json, index, 4);
                    index += 4;
                }
            }

            return false;
        }

        #endregion

        #region Internals

        private static bool TryReadUnicode(string value, int start, out char ch)
        {
            ch = '\0';

            if (start < 0 || start + 4 > value.Length)
                return false;

            var hex = value.Substring(start, 4);
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                return false;

            ch = (char)code;
            return true;
        }

        #endregion
    }
}
