using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SCG.UPPM.Helpers
{
    /// <summary>
    /// Implements lightweight read/write access for package.json string fields.
    /// The helper is intended for editor tooling where package.json is a writable asset.
    /// Regex matching is used for minimal dependencies and for preserving existing formatting.
    /// </summary>
    internal static class PackageJsonTextHelper
    {
        #region Cache

        private static readonly Dictionary<string, Regex> s_stringFieldRegexCache = new(StringComparer.Ordinal);

        #endregion

        #region Public API

        /// <summary>
        /// Reads a string field from the package.json referenced by the provided TextAsset.
        /// Returns an empty string when the key is missing or when the asset path is invalid.
        /// The returned value is unescaped into a .NET string.
        /// </summary>
        /// <param name="packageAsset">TextAsset referencing a package.json file.</param>
        /// <param name="key">JSON key to read.</param>
        public static string GetStringField(TextAsset packageAsset, string key) =>
            packageAsset == null || !TryGetPackageJsonPath(packageAsset, out var path)
                ? string.Empty
                : GetStringField(path, key);

        /// <summary>
        /// Reads a string field from a package.json file on disk.
        /// Returns an empty string when the key is missing or when the path is invalid.
        /// The returned value is unescaped into a .NET string.
        /// </summary>
        /// <param name="packageJsonAbs">Absolute path to a package.json file.</param>
        /// <param name="key">JSON key to read.</param>
        public static string GetStringField(string packageJsonAbs, string key)
        {
            if (!TryValidateFilePath(packageJsonAbs, out var path))
                return string.Empty;

            var json = File.ReadAllText(path);
            var rx = GetStringFieldRegex(key);
            var m = rx.Match(json);
            if (!m.Success)
                return string.Empty;

            var raw = m.Groups["value"].Value;
            return JsonStringUtility.Unescape(raw);
        }

        /// <summary>
        /// Updates or inserts a string field into the package.json referenced by the provided TextAsset.
        /// The asset is reimported only when file contents actually changed.
        /// </summary>
        /// <param name="packageAsset">TextAsset referencing a writable package.json file.</param>
        /// <param name="key">JSON key to update or insert.</param>
        /// <param name="value">New value to write.</param>
        public static void SetStringField(TextAsset packageAsset, string key, string value)
        {
            if (packageAsset == null)
                return;

            if (!TryGetPackageJsonPath(packageAsset, out var path))
                return;

            SetStringField(path, key, value);
        }

        /// <summary>
        /// Updates or inserts a string field into the package.json file on disk.
        /// The file is rewritten only when the content changes.
        /// When the file belongs to the Unity project, it is reimported.
        /// </summary>
        /// <param name="packageJsonAbs">Absolute path to a writable package.json file.</param>
        /// <param name="key">JSON key to update or insert.</param>
        /// <param name="value">New value to write.</param>
        public static void SetStringField(string packageJsonAbs, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (string.IsNullOrWhiteSpace(value))
                return;

            if (!TryValidateFilePath(packageJsonAbs, out var path))
                return;

            var json = File.ReadAllText(path);
            var rx = GetStringFieldRegex(key);
            var newJson = UpsertStringField(json, key, value, rx);

            if (string.Equals(newJson, json, StringComparison.Ordinal))
                return;

            WriteTextFile(path, newJson);
            TryReimportUnityAsset(path);
        }

        #endregion

        #region Regex

        private static Regex GetStringFieldRegex(string key)
        {
            if (string.IsNullOrEmpty(key))
                return BuildStringFieldRegex(string.Empty);

            if (s_stringFieldRegexCache.TryGetValue(key, out var rx))
                return rx;

            rx = BuildStringFieldRegex(key);
            s_stringFieldRegexCache[key] = rx;
            return rx;
        }

        private static Regex BuildStringFieldRegex(string key) =>
            new(
                $"\"{Regex.Escape(key)}\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"(?<comma>\\s*,?)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

        #endregion

        #region Upsert

        private static string UpsertStringField(string json, string key, string value, Regex rx)
        {
            if (string.IsNullOrEmpty(json))
                return json;

            var escaped = JsonStringUtility.Escape(value);

            var m = rx.Match(json);
            if (!m.Success)
                return InsertStringField(json, key, escaped);

            var comma = m.Groups["comma"].Value;
            var newSegment = $"\"{key}\": \"{escaped}\"{comma}";
            return rx.Replace(json, newSegment, 1);
        }

        private static string InsertStringField(string json, string key, string escapedValue)
        {
            var open = json.IndexOf('{');
            if (open < 0)
                return json;

            var newline = json.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var indent = DetectIndent(json, open, newline) ?? "  ";

            var insertAt = open + 1;
            var hasAnyProps = HasAnyPropertiesAfterBrace(json, insertAt);

            var sb = new StringBuilder(json.Length + 64);
            sb.Append(json, 0, insertAt);

            sb.Append(newline);
            sb.Append(indent);
            sb.Append('"');
            sb.Append(key);
            sb.Append("\": \"");
            sb.Append(escapedValue);
            sb.Append('"');

            if (hasAnyProps)
                sb.Append(',');

            sb.Append(newline);
            sb.Append(json, insertAt, json.Length - insertAt);

            return sb.ToString();
        }

        private static string DetectIndent(string json, int openBraceIndex, string newline)
        {
            var nl = json.IndexOf(newline, openBraceIndex + 1, StringComparison.Ordinal);
            if (nl < 0)
                return null;

            var i = nl + newline.Length;
            if (i >= json.Length)
                return null;

            var start = i;
            while (i < json.Length)
            {
                var ch = json[i];
                if (ch != ' ' && ch != '\t')
                    break;

                i++;
            }

            var len = i - start;
            return len > 0 ? json.Substring(start, len) : null;
        }

        private static bool HasAnyPropertiesAfterBrace(string json, int startIndex)
        {
            for (var i = startIndex; i < json.Length; i++)
            {
                var ch = json[i];
                if (char.IsWhiteSpace(ch))
                    continue;

                return ch != '}';
            }

            return false;
        }

        #endregion

        #region IO

        private static bool TryGetPackageJsonPath(TextAsset packageAsset, out string absPath)
        {
            var assetPath = AssetDatabase.GetAssetPath(packageAsset);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                absPath = null;
                return false;
            }

            absPath = ToAbsoluteProjectPath(assetPath);
            return File.Exists(absPath);
        }

        private static bool TryValidateFilePath(string packageJsonAbs, out string absPath)
        {
            if (string.IsNullOrWhiteSpace(packageJsonAbs))
            {
                absPath = null;
                return false;
            }

            absPath = ToAbsoluteProjectPath(packageJsonAbs);
            return File.Exists(absPath);
        }

        private static void WriteTextFile(string absPath, string text)
        {
            File.SetAttributes(absPath, FileAttributes.Normal);

            var utf8NoBom = new UTF8Encoding(false);
            File.WriteAllText(absPath, text, utf8NoBom);
        }

        private static void TryReimportUnityAsset(string absPath)
        {
            var projectRoot = GetProjectRootAbs().Replace('\\', '/').TrimEnd('/') + "/";
            var normalizedPath = Path.GetFullPath(absPath).Replace('\\', '/');

            if (!normalizedPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                return;

            var relative = normalizedPath[projectRoot.Length..].Replace('\\', '/');
            if (!IsAssetDatabasePath(relative))
                return;

            AssetDatabase.ImportAsset(
                relative,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            if (relative.StartsWith(Constants.PackagesRoot, StringComparison.OrdinalIgnoreCase))
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool IsAssetDatabasePath(string projectRelative)
        {
            if (string.IsNullOrEmpty(projectRelative))
                return false;

            projectRelative = projectRelative.Replace('\\', '/');
            return projectRelative.StartsWith(Constants.AssetsRoot, StringComparison.OrdinalIgnoreCase)
                   || projectRelative.StartsWith(Constants.PackagesRoot, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Path

        private static string ToAbsoluteProjectPath(string path)
        {
            path = path.Replace('\\', '/');

            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);

            var projectRoot = GetProjectRootAbs();
            return Path.GetFullPath(Path.Combine(projectRoot, path));
        }

        private static string GetProjectRootAbs() =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        #endregion
    }
}
