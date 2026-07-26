using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace SCG.UnityAssetPublisherTools.Upm
{
    /// <summary>
    /// Provides path helpers used by the staged UPM workflow.
    /// The utility normalizes paths and converts Unity project-relative paths into absolute file system locations.
    /// All methods assume Editor context and may use AssetDatabase when inspector references are provided.
    /// </summary>
    internal static class UpmPathUtility
    {
        /// <summary>
        /// Gets the absolute path to the Unity project root directory.
        /// The root is resolved as the parent directory of <see cref="Application.dataPath"/>.
        /// The value is used to construct absolute filesystem paths for move and write operations.
        /// </summary>
        public static string ProjectRootAbs
        {
            get
            {
                var dir = Directory.GetParent(Application.dataPath);
                return dir != null ? dir.FullName : Application.dataPath;
            }
        }

        /// <summary>
        /// Converts a Unity project-relative path into an absolute filesystem path.
        /// Absolute inputs are passed through and normalized using <see cref="Path.GetFullPath(string)"/>.
        /// Package paths are resolved through Package Manager to support Library/PackageCache locations.
        /// The helper supports forward and backward slash separators.
        /// </summary>
        /// <param name="path">Project-relative or absolute path.</param>
        public static string ToAbsolute(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);

            var normalized = path.Replace('\\', '/');
            return TryResolvePackageAssetPath(normalized, out var packagePath)
                ? packagePath
                : Path.GetFullPath(Path.Combine(ProjectRootAbs, normalized));
        }

        /// <summary>
        /// Compares two filesystem paths after normalization.
        /// The method uses case-insensitive comparison to match common editor behavior on Windows.
        /// Trailing separators are trimmed to avoid false negatives.
        /// </summary>
        /// <param name="a">First path.</param>
        /// <param name="b">Second path.</param>
        public static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;

            var na = Path.GetFullPath(a).Replace('\\', '/').TrimEnd('/');
            var nb = Path.GetFullPath(b).Replace('\\', '/').TrimEnd('/');
            return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a safe folder name from an arbitrary user input.
        /// The result is stripped from invalid filesystem characters and falls back to "SCG" when empty.
        /// This method is used to normalize the configured asset root folder name.
        /// </summary>
        /// <param name="folderName">Input folder name.</param>
        public static string GetSafeFolderName(string folderName)
        {
            var safe = string.IsNullOrWhiteSpace(folderName) ? nameof(SCG) : folderName.Trim();
            safe = Path.GetInvalidFileNameChars().Aggregate(safe, (current, c) => current.Replace(c.ToString(), string.Empty));

            return string.IsNullOrWhiteSpace(safe) ? nameof(SCG) : safe;
        }

        /// <summary>
        /// Resolves the absolute source folder path using <see cref="AssetPublisherToolsSettings"/>.
        /// When BaseFolder is assigned, its AssetDatabase path is preferred.
        /// Otherwise, the method falls back to a conventional Assets/folderName location.
        /// </summary>
        /// <param name="cfg">Settings instance.</param>
        /// <param name="folderName">Fallback folder name under Assets.</param>
        public static string ResolveOriginalRootAbs(AssetPublisherToolsSettings cfg, string folderName)
        {
            if (cfg == null || cfg.BaseFolder == null)
                return ToAbsolute(UpmConstants.AssetsFolderName + "/" + folderName);

            var assetPath = AssetDatabase.GetAssetPath(cfg.BaseFolder);
            return !string.IsNullOrEmpty(assetPath) ? ToAbsolute(assetPath) : ToAbsolute(UpmConstants.AssetsFolderName + "/" + folderName);
        }

        /// <summary>
        /// Maps an absolute file path from the original root into the Temp root after a folder move.
        /// When the file is under the original root, the relative part is preserved.
        /// Otherwise, the file name is placed directly under Temp.
        /// </summary>
        /// <param name="originalRootAbs">Absolute original root folder path.</param>
        /// <param name="tempRootAbs">Absolute temp root folder path.</param>
        /// <param name="originalFileAbs">Absolute file path before move.</param>
        public static string MapMovedFileToTemp(string originalRootAbs, string tempRootAbs, string originalFileAbs)
        {
            var o = Path.GetFullPath(originalRootAbs).Replace('\\', '/').TrimEnd('/') + "/";
            var f = Path.GetFullPath(originalFileAbs).Replace('\\', '/');

            if (!f.StartsWith(o, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(tempRootAbs, Path.GetFileName(originalFileAbs));

            var rel = f[o.Length..].Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(tempRootAbs, rel);
        }

        private static bool TryResolvePackageAssetPath(string assetPath, out string absolutePath)
        {
            absolutePath = string.Empty;

            if (!assetPath.StartsWith(Constants.PackagesRoot, StringComparison.Ordinal))
                return false;

            var packageRootPath = GetPackageRootAssetPath(assetPath);
            if (string.IsNullOrWhiteSpace(packageRootPath))
                return false;

            var packageInfo = PackageInfo.FindForAssetPath(assetPath) ??
                              PackageInfo.FindForAssetPath(packageRootPath);
            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
                return false;

            var packageAssetPath = packageInfo.assetPath?.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(packageAssetPath))
                return false;

            var normalizedAssetPath = assetPath.TrimEnd('/');
            var isPackageRoot = string.Equals(normalizedAssetPath, packageAssetPath, StringComparison.Ordinal);
            switch (isPackageRoot)
            {
                case false when !normalizedAssetPath.StartsWith(packageAssetPath + "/", StringComparison.Ordinal):
                    return false;
                case true:
                    absolutePath = Path.GetFullPath(packageInfo.resolvedPath);
                    return true;
            }

            var relativePath = normalizedAssetPath[(packageAssetPath.Length + 1)..];
            absolutePath = Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, relativePath));
            return true;
        }

        private static string GetPackageRootAssetPath(string assetPath)
        {
            var packageNameStart = Constants.PackagesRoot.Length;
            if (assetPath.Length <= packageNameStart)
                return string.Empty;

            var nextSeparatorIndex = assetPath.IndexOf('/', packageNameStart);
            return nextSeparatorIndex < 0
                ? assetPath.TrimEnd('/')
                : assetPath[..nextSeparatorIndex];
        }
    }
}
