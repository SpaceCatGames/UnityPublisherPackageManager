using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SCG.UnityAssetPublisherTools.Helpers
{
    /// <summary>
    /// Provides editor-friendly helpers for manipulating Packages/manifest.json.
    /// The utility is intentionally limited to operations required by local or embedded package workflows.
    /// It updates only the "dependencies" object and preserves the rest of the file text.
    /// </summary>
    public static class ManifestJsonUtility
    {
        private static readonly Encoding s_utf8WithoutBom = new UTF8Encoding(false);

        #region Public API

        /// <summary>
        /// Adds or updates a dependency entry inside the manifest.json "dependencies" object.
        /// The method preserves existing dependency order and appends new keys to the end.
        /// The file is rewritten only when the dependency set actually changes.
        /// </summary>
        /// <param name="manifestAbs">Absolute path to Packages/manifest.json.</param>
        /// <param name="packageId">Dependency key to add or update.</param>
        /// <param name="dependencyValue">Dependency value to write.</param>
        public static void SetDependency(string manifestAbs, string packageId, string dependencyValue) =>
            UpdateDependency(manifestAbs, packageId, dependencyValue, addOrUpdate: true);

        /// <summary>
        /// Removes a dependency entry from the manifest.json "dependencies" object.
        /// The file is rewritten only when the dependency existed and was removed.
        /// </summary>
        /// <param name="manifestAbs">Absolute path to Packages/manifest.json.</param>
        /// <param name="packageId">Dependency key to remove.</param>
        public static void RemoveDependency(string manifestAbs, string packageId) =>
            UpdateDependency(manifestAbs, packageId, dependencyValue: string.Empty, addOrUpdate: false);

        #endregion

        #region Implementation

        private static void UpdateDependency(string manifestAbs, string packageId, string dependencyValue, bool addOrUpdate)
        {
            if (string.IsNullOrWhiteSpace(manifestAbs))
                throw new ArgumentException("Manifest path is empty.", nameof(manifestAbs));

            if (string.IsNullOrWhiteSpace(packageId))
                throw new ArgumentException("Package id is empty.", nameof(packageId));

            if (!File.Exists(manifestAbs))
                throw new FileNotFoundException("manifest.json not found.", manifestAbs);

            var json = File.ReadAllText(manifestAbs, Encoding.UTF8);
            if (!TryLocateDependenciesObject(json, out var objectStart, out var objectEnd, out var baseIndent, out var newline))
                throw new InvalidOperationException("Could not locate dependencies object in manifest.json.");

            var dict = ParseStringMap(json, objectStart, objectEnd, out var order);

            var changed = false;
            if (addOrUpdate)
            {
                var escaped = JsonStringUtility.Escape(dependencyValue ?? string.Empty);
                if (!dict.TryGetValue(packageId, out var old) || !string.Equals(old, escaped, StringComparison.Ordinal))
                {
                    dict[packageId] = escaped;
                    if (!order.Contains(packageId))
                        order.Add(packageId);
                    changed = true;
                }
            }
            else
            {
                if (dict.Remove(packageId))
                {
                    order.Remove(packageId);
                    changed = true;
                }
            }

            if (!changed)
                return;

            var rebuilt = BuildObjectText(dict, order, baseIndent, newline);
            var newJson = json[..objectStart] + rebuilt + json[objectEnd..];

            if (string.Equals(newJson, json, StringComparison.Ordinal))
                return;

            File.SetAttributes(manifestAbs, FileAttributes.Normal);
            File.WriteAllText(manifestAbs, newJson, s_utf8WithoutBom);
        }

        private static bool TryLocateDependenciesObject(
            string json,
            out int objectStart,
            out int objectEnd,
            out string baseIndent,
            out string newline)
        {
            objectStart = -1;
            objectEnd = -1;
            baseIndent = string.Empty;
            newline = json != null && json.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

            if (string.IsNullOrEmpty(json))
                return false;

            var braceDepth = 0;
            var inString = false;
            var escape = false;
            var i = 0;

            while (i < json.Length)
            {
                var ch = json[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        i++;
                        continue;
                    }

                    switch (ch)
                    {
                        case '\\':
                            escape = true;
                            break;
                        case '"':
                            inString = false;
                            break;
                    }

                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    if (braceDepth == 1)
                    {
                        var start = i;
                        if (!JsonStringUtility.TryReadQuotedString(json, ref i, out var propName))
                            return false;

                        var j = SkipWhitespace(json, i);
                        if (j < json.Length && json[j] == ':')
                        {
                            j = SkipWhitespace(json, j + 1);
                            if (string.Equals(propName, "dependencies", StringComparison.Ordinal) && j < json.Length && json[j] == '{')
                            {
                                objectStart = j;
                                objectEnd = FindMatchingBrace(json, objectStart);
                                if (objectEnd <= objectStart)
                                    return false;

                                baseIndent = ReadLineIndent(json, start);
                                return true;
                            }
                        }

                        i = j;
                        continue;
                    }

                    inString = true;
                    i++;
                    continue;
                }

                switch (ch)
                {
                    case '{':
                        braceDepth++;
                        break;
                    case '}':
                        braceDepth = Math.Max(0, braceDepth - 1);
                        break;
                }

                i++;
            }

            return false;
        }

        private static int SkipWhitespace(string json, int index)
        {
            while (index < json.Length)
            {
                var ch = json[index];
                if (!char.IsWhiteSpace(ch))
                    break;
                index++;
            }

            return index;
        }

        private static int FindMatchingBrace(string json, int openBraceIndex)
        {
            var depth = 0;
            var inString = false;
            var escape = false;

            for (var i = openBraceIndex; i < json.Length; i++)
            {
                var ch = json[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    switch (ch)
                    {
                        case '\\':
                            escape = true;
                            continue;
                        case '"':
                            inString = false;
                            break;
                    }

                    continue;
                }

                switch (ch)
                {
                    case '"':
                        inString = true;
                        continue;
                    case '{':
                        depth++;
                        continue;
                }

                if (ch != '}')
                    continue;

                depth--;
                if (depth == 0)
                    return i + 1;
            }

            return -1;
        }

        private static string ReadLineIndent(string json, int indexInLine)
        {
            var lineStart = json.LastIndexOf('\n', Math.Max(0, indexInLine - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;

            var i = lineStart;
            while (i < json.Length)
            {
                var ch = json[i];
                if (ch != ' ' && ch != '\t' && ch != '\r')
                    break;
                i++;
            }

            return json.Substring(lineStart, i - lineStart);
        }

        private static Dictionary<string, string> ParseStringMap(string json, int objectStart, int objectEnd, out List<string> order)
        {
            order = new List<string>();
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);

            var i = objectStart + 1;
            while (i < objectEnd)
            {
                i = SkipWhitespace(json, i);
                if (i >= objectEnd)
                    break;

                if (json[i] == '}')
                    break;

                if (json[i] != '"')
                {
                    i++;
                    continue;
                }

                if (!JsonStringUtility.TryReadQuotedString(json, ref i, out var key))
                    break;

                i = SkipWhitespace(json, i);
                if (i >= objectEnd || json[i] != ':')
                    break;

                i = SkipWhitespace(json, i + 1);
                if (i >= objectEnd || json[i] != '"')
                    break;

                var valueStart = i;
                if (!JsonStringUtility.TryReadQuotedString(json, ref i, out _))
                    break;

                var rawValueContent = json.Substring(valueStart + 1, i - valueStart - 2);

                if (!dict.ContainsKey(key))
                    order.Add(key);

                dict[key] = rawValueContent;

                i = SkipWhitespace(json, i);
                if (i < objectEnd && json[i] == ',')
                    i++;
            }

            return dict;
        }

        private static string BuildObjectText(Dictionary<string, string> dict, List<string> order, string baseIndent, string newline)
        {
            var itemIndent = baseIndent + "  ";
            var sb = new StringBuilder();
            sb.Append('{');

            if (order.Count == 0)
            {
                sb.Append(newline);
                sb.Append(baseIndent);
                sb.Append('}');
                return sb.ToString();
            }

            sb.Append(newline);
            for (var idx = 0; idx < order.Count; idx++)
            {
                var key = order[idx];
                if (!dict.TryGetValue(key, out var rawValue))
                    continue;

                var comma = idx < order.Count - 1 ? "," : string.Empty;
                sb.Append(itemIndent);
                sb.Append('"');
                sb.Append(key);
                sb.Append("\": \"");
                sb.Append(rawValue);
                sb.Append('"');
                sb.Append(comma);
                sb.Append(newline);
            }

            sb.Append(baseIndent);
            sb.Append('}');
            return sb.ToString();
        }

        #endregion
    }
}
