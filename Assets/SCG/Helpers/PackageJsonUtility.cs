using System.Text.RegularExpressions;
using UnityEngine;

namespace SCG.UPPM.Helpers
{
    /// <summary>
    /// Provides editor-only helpers for reading and updating package.json metadata.
    /// The API supports both TextAsset references and raw file system paths.
    /// The implementation is intentionally lightweight to avoid a hard dependency on a JSON serializer.
    /// </summary>
    public static class PackageJsonUtility
    {
        #region Regex

        private static readonly Regex s_semVerRegex = new(
            @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex s_majorOnlyRegex = new(
            @"^(0|[1-9]\d*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex s_majorMinorRegex = new(
            @"^(0|[1-9]\d*)\.(0|[1-9]\d*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Version

        /// <summary>
        /// Reads the version field from a package.json asset.
        /// Returns an empty string when the asset path is invalid or the key is missing.
        /// The value is returned after JSON unescaping.
        /// </summary>
        /// <param name="packageAsset">TextAsset pointing to a package.json file.</param>
        public static string GetPackageVersion(TextAsset packageAsset) =>
            PackageJsonTextHelper.GetStringField(packageAsset, "version");

        /// <summary>
        /// Reads the version field from a package.json file.
        /// Returns an empty string when the path is invalid or the key is missing.
        /// The value is returned after JSON unescaping.
        /// </summary>
        /// <param name="packageJsonAbs">Absolute path to a package.json file.</param>
        public static string GetPackageVersion(string packageJsonAbs) =>
            PackageJsonTextHelper.GetStringField(packageJsonAbs, "version");

        /// <summary>
        /// Updates the version field in the target package.json file.
        /// Preserves an optional trailing comma after the value when the key already exists.
        /// The method inserts the key when it is missing and the document looks like a JSON object.
        /// </summary>
        /// <param name="packageAsset">TextAsset pointing to a writable package.json file.</param>
        /// <param name="version">New version value to write into the JSON document.</param>
        public static void SetPackageVersion(TextAsset packageAsset, string version)
        {
            if (!TryNormalizeSemVer(version, out var normalized))
            {
                Debug.LogError($"Invalid package version '{version}'. Expected SemVer (e.g. '1.2.3').");
                return;
            }

            PackageJsonTextHelper.SetStringField(packageAsset, "version", normalized);
        }

        /// <summary>
        /// Updates the version field in the target package.json file.
        /// Preserves an optional trailing comma after the value when the key already exists.
        /// The method inserts the key when it is missing and the document looks like a JSON object.
        /// </summary>
        /// <param name="packageJsonAbs">Absolute path to a writable package.json file.</param>
        /// <param name="version">New version value to write into the JSON document.</param>
        public static void SetPackageVersion(string packageJsonAbs, string version)
        {
            if (!TryNormalizeSemVer(version, out var normalized))
            {
                Debug.LogError($"Invalid package version '{version}'. Expected SemVer (e.g. '1.2.3').");
                return;
            }

            PackageJsonTextHelper.SetStringField(packageJsonAbs, "version", normalized);
        }

        #endregion

        #region Name

        /// <summary>
        /// Reads the name field from a package.json asset.
        /// Returns an empty string when the asset path is invalid or the key is missing.
        /// The value is returned after JSON unescaping.
        /// </summary>
        /// <param name="packageAsset">TextAsset pointing to a package.json file.</param>
        public static string GetPackageName(TextAsset packageAsset) =>
            PackageJsonTextHelper.GetStringField(packageAsset, "name");

        /// <summary>
        /// Reads the name field from a package.json file.
        /// Returns an empty string when the path is invalid or the key is missing.
        /// The value is returned after JSON unescaping.
        /// </summary>
        /// <param name="packageJsonAbs">Absolute path to a package.json file.</param>
        public static string GetPackageName(string packageJsonAbs) =>
            PackageJsonTextHelper.GetStringField(packageJsonAbs, "name");

        /// <summary>
        /// Updates the name field in the target package.json file.
        /// Preserves an optional trailing comma after the value when the key already exists.
        /// The method inserts the key when it is missing and the document looks like a JSON object.
        /// </summary>
        /// <param name="packageAsset">TextAsset pointing to a writable package.json file.</param>
        /// <param name="packageId">New package id to write into the JSON document.</param>
        public static void SetPackageName(TextAsset packageAsset, string packageId) =>
            PackageJsonTextHelper.SetStringField(packageAsset, "name", packageId);

        /// <summary>
        /// Updates the name field in the target package.json file.
        /// Preserves an optional trailing comma after the value when the key already exists.
        /// The method inserts the key when it is missing and the document looks like a JSON object.
        /// </summary>
        /// <param name="packageJsonAbs">Absolute path to a writable package.json file.</param>
        /// <param name="packageId">New package id to write into the JSON document.</param>
        public static void SetPackageName(string packageJsonAbs, string packageId) =>
            PackageJsonTextHelper.SetStringField(packageJsonAbs, "name", packageId);

        #endregion

        #region Display Name

        /// <summary>
        /// Reads the displayName field from a package.json asset.
        /// Returns an empty string when the asset path is invalid or the key is missing.
        /// The value is returned after JSON unescaping.
        /// </summary>
        /// <param name="packageAsset">TextAsset pointing to a package.json file.</param>
        public static string GetPackageDisplayName(TextAsset packageAsset) =>
            PackageJsonTextHelper.GetStringField(packageAsset, "displayName");

        /// <summary>
        /// Reads the displayName field from a package.json file.
        /// Returns an empty string when the path is invalid or the key is missing.
        /// The value is returned after JSON unescaping.
        /// </summary>
        /// <param name="packageJsonAbs">Absolute path to a package.json file.</param>
        public static string GetPackageDisplayName(string packageJsonAbs) =>
            PackageJsonTextHelper.GetStringField(packageJsonAbs, "displayName");

        /// <summary>
        /// Updates the displayName field in the target package.json file.
        /// Preserves an optional trailing comma after the value when the key already exists.
        /// The method inserts the key when it is missing and the document looks like a JSON object.
        /// </summary>
        /// <param name="packageAsset">TextAsset pointing to a writable package.json file.</param>
        /// <param name="displayName">New display name to write into the JSON document.</param>
        public static void SetPackageDisplayName(TextAsset packageAsset, string displayName) =>
            PackageJsonTextHelper.SetStringField(packageAsset, "displayName", displayName);

        /// <summary>
        /// Updates the displayName field in the target package.json file.
        /// Preserves an optional trailing comma after the value when the key already exists.
        /// The method inserts the key when it is missing and the document looks like a JSON object.
        /// </summary>
        /// <param name="packageJsonAbs">Absolute path to a writable package.json file.</param>
        /// <param name="displayName">New display name to write into the JSON document.</param>
        public static void SetPackageDisplayName(string packageJsonAbs, string displayName) =>
            PackageJsonTextHelper.SetStringField(packageJsonAbs, "displayName", displayName);

        #endregion

        #region Description

        /// <summary>
        /// Reads the description field from a package.json asset.
        /// Returns an empty string when the asset path is invalid or the key is missing.
        /// The value is returned after JSON unescaping.
        /// </summary>
        /// <param name="packageAsset">TextAsset pointing to a package.json file.</param>
        public static string GetPackageDescription(TextAsset packageAsset) =>
            PackageJsonTextHelper.GetStringField(packageAsset, "description");

        /// <summary>
        /// Reads the description field from a package.json file.
        /// Returns an empty string when the path is invalid or the key is missing.
        /// The value is returned after JSON unescaping.
        /// </summary>
        /// <param name="packageJsonAbs">Absolute path to a package.json file.</param>
        public static string GetPackageDescription(string packageJsonAbs) =>
            PackageJsonTextHelper.GetStringField(packageJsonAbs, "description");

        /// <summary>
        /// Updates the description field in the target package.json file.
        /// Preserves an optional trailing comma after the value when the key already exists.
        /// The method inserts the key when it is missing and the document looks like a JSON object.
        /// </summary>
        /// <param name="packageAsset">TextAsset pointing to a writable package.json file.</param>
        /// <param name="description">New description value to write into the JSON document.</param>
        public static void SetPackageDescription(TextAsset packageAsset, string description) =>
            PackageJsonTextHelper.SetStringField(packageAsset, "description", description);

        /// <summary>
        /// Updates the description field in the target package.json file.
        /// Preserves an optional trailing comma after the value when the key already exists.
        /// The method inserts the key when it is missing and the document looks like a JSON object.
        /// </summary>
        /// <param name="packageJsonAbs">Absolute path to a writable package.json file.</param>
        /// <param name="description">New description value to write into the JSON document.</param>
        public static void SetPackageDescription(string packageJsonAbs, string description) =>
            PackageJsonTextHelper.SetStringField(packageJsonAbs, "description", description);

        #endregion

        #region Helpers

        /// <summary>
        /// Tries to normalize an input string into a SemVer-compatible value.
        /// Accepts full SemVer and also "major" or "major.minor" shorthands.
        /// Returns false when the input cannot be normalized into SemVer.
        /// </summary>
        /// <param name="input">Input version string.</param>
        /// <param name="normalized">Normalized SemVer when successful.</param>
        public static bool TryNormalizeSemVer(string input, out string normalized)
        {
            normalized = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            var v = input.Trim();

            if (s_semVerRegex.IsMatch(v))
            {
                normalized = v;
                return true;
            }

            if (s_majorOnlyRegex.IsMatch(v))
            {
                normalized = v + ".0.0";
                return true;
            }

            if (!s_majorMinorRegex.IsMatch(v)) return false;
            normalized = v + ".0";
            return true;
        }

        #endregion
    }
}
