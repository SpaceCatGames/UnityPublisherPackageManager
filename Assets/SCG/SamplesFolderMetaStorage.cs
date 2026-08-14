using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SCG.UPPM.Upm;

namespace SCG.UPPM
{
    /// <summary>
    /// Stores folder metadata outside the imported asset tree while <b>Samples</b> and <b>Documentation</b> are hidden.
    /// Backups are isolated by normalized absolute package root and survive Temp cleanup and editor restarts.
    /// </summary>
    internal sealed class SamplesFolderMetaStorage
    {
        private readonly string _basePath;

        internal SamplesFolderMetaStorage(string basePath) => _basePath = Path.GetFullPath(basePath);

        internal static SamplesFolderMetaStorage CreateForProject() => new(
            Path.Combine(UpmPathUtility.ProjectRootAbs, "ProjectSettings", nameof(UPPM), "FolderMeta"));

        internal byte[] Load(string packageRootPath, string folderName)
        {
            var path = GetFilePath(packageRootPath, folderName);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        internal void Save(string packageRootPath, string folderName, byte[] contents)
        {
            if (contents == null)
                throw new ArgumentNullException(nameof(contents));

            var path = GetFilePath(packageRootPath, folderName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("Folder metadata path has no parent."));
            File.WriteAllBytes(path, contents);
        }

        internal void Delete(string packageRootPath, string folderName)
        {
            var path = GetFilePath(packageRootPath, folderName);
            if (File.Exists(path))
                File.Delete(path);

            DeleteIfEmpty(Path.GetDirectoryName(path));
            DeleteIfEmpty(_basePath);
        }

        internal string GetPackageStoragePath(string packageRootPath) =>
            Path.Combine(_basePath, GetRootKey(packageRootPath));

        private string GetFilePath(string packageRootPath, string folderName) =>
            Path.Combine(GetPackageStoragePath(packageRootPath), folderName.TrimEnd('~') + ".meta");

        private static string GetRootKey(string packageRootPath)
        {
            var normalized = Path.GetFullPath(packageRootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
            using var sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(normalized)))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static void DeleteIfEmpty(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
                Directory.Delete(path);
        }
    }
}
