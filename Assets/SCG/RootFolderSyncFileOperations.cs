using System;
using System.Collections.Generic;
using System.IO;
using SCG.UPPM.Upm;
using UnityEditor;

namespace SCG.UPPM
{
    /// <summary>
    /// Provides filesystem helpers for the root mirror synchronization workflow.
    /// The helpers mirror complete folder trees together with .meta files, compare folder identity,
    /// and remove folders using delayed retries when Unity leaves temporary remnants behind.
    /// </summary>
    internal static class RootFolderSyncFileOperations
    {
        #region Constants

        private const int DeleteRetryCount = 3;
        private const string RootMetaKey = ".root.meta";

        #endregion

        #region Public API

        public static bool MirrorDirectoryWithMeta(string sourceAbs, string targetAbs)
        {
            if (string.IsNullOrWhiteSpace(sourceAbs) || string.IsNullOrWhiteSpace(targetAbs))
                return false;

            var sourceState = GetPathState(sourceAbs);
            switch (sourceState)
            {
                case PathState.File or PathState.MetaOnly:
                    throw new IOException($"Expected a directory at '{sourceAbs}', but found an invalid path state.");
                case PathState.Missing:
                    return DeleteDirectoryWithMeta(targetAbs);
            }

            if (AreDirectoriesIdentical(sourceAbs, targetAbs))
                return false;

            var targetState = GetPathState(targetAbs);
            if (targetState == PathState.File)
                throw new IOException($"Cannot mirror directory into '{targetAbs}' because a file exists at that path.");

            EnsureParentDirectoryExists(targetAbs);
            DeleteDirectoryWithMeta(targetAbs);

            if (GetPathState(targetAbs) != PathState.Missing)
                throw new IOException($"Failed to clear '{targetAbs}' before synchronization.");

            FileUtil.CopyFileOrDirectory(sourceAbs, targetAbs);
            CopyFileIfPresent(sourceAbs + ".meta", targetAbs + ".meta");
            return true;
        }

        public static bool AreDirectoriesIdentical(string leftAbs, string rightAbs)
        {
            if (!Directory.Exists(leftAbs) || !Directory.Exists(rightAbs))
                return false;

            var leftFiles = EnumerateComparableFiles(leftAbs);
            var rightFiles = EnumerateComparableFiles(rightAbs);
            if (leftFiles.Count != rightFiles.Count)
                return false;

            foreach (var pair in leftFiles)
            {
                if (!rightFiles.TryGetValue(pair.Key, out var rightFile))
                    return false;

                if (!FileContentsEqual(pair.Value, rightFile))
                    return false;
            }

            return true;
        }

        public static bool AreDirectoryGuidsEqual(string leftAbs, string rightAbs)
        {
            if (!Directory.Exists(leftAbs) || !Directory.Exists(rightAbs))
                return false;

            var leftGuids = EnumerateMetaGuids(leftAbs);
            var rightGuids = EnumerateMetaGuids(rightAbs);
            if (leftGuids.Count != rightGuids.Count)
                return false;

            foreach (var pair in leftGuids)
            {
                if (!rightGuids.TryGetValue(pair.Key, out var rightGuid))
                    return false;

                if (!string.Equals(pair.Value, rightGuid, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        public static bool DeleteDirectoryWithMeta(string pathAbs)
        {
            var state = GetPathState(pathAbs);
            switch (state)
            {
                case PathState.Missing:
                    return false;
                case PathState.File:
                    throw new IOException($"Cannot delete expected directory path '{pathAbs}' because a file exists at that location.");
                case PathState.MetaOnly:
                    TryDeleteFileRaw(pathAbs + ".meta");
                    return true;
                case PathState.Directory:
                default:
                    break;
            }

            DeleteDirectoryWithMetaImmediately(pathAbs);

            if (GetPathState(pathAbs) != PathState.Missing)
                ScheduleDeleteRetry(pathAbs, DeleteRetryCount);

            return true;
        }

        public static PathState GetPathState(string pathAbs)
        {
            var hasDirectory = Directory.Exists(pathAbs);
            var hasFile = File.Exists(pathAbs);
            var hasMeta = File.Exists(pathAbs + ".meta");

            if (hasDirectory)
                return PathState.Directory;

            if (hasFile)
                return PathState.File;

            return hasMeta ? PathState.MetaOnly : PathState.Missing;
        }

        #endregion

        #region Enumeration

        private static Dictionary<string, string> EnumerateComparableFiles(string rootAbs)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rootMeta = rootAbs + ".meta";
            if (File.Exists(rootMeta))
                files[RootMetaKey] = rootMeta;

            foreach (var file in Directory.GetFiles(rootAbs, "*", SearchOption.AllDirectories))
                files[MakeRelative(file, rootAbs)] = file;

            return files;
        }

        private static Dictionary<string, string> EnumerateMetaGuids(string rootAbs)
        {
            var guids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rootMeta = rootAbs + ".meta";
            if (File.Exists(rootMeta) && TryReadGuid(rootMeta, out var rootGuid))
                guids[RootMetaKey] = rootGuid;

            foreach (var metaPath in Directory.GetFiles(rootAbs, "*.meta", SearchOption.AllDirectories))
            {
                if (!TryReadGuid(metaPath, out var guid))
                    continue;

                guids[MakeRelative(metaPath, rootAbs)] = guid;
            }

            return guids;
        }

        #endregion

        #region Delete

        private static void DeleteDirectoryWithMetaImmediately(string pathAbs)
        {
            var assetPath = TryGetAssetPath(pathAbs);
            if (!string.IsNullOrEmpty(assetPath) && AssetDatabase.IsValidFolder(assetPath))
                AssetDatabase.DeleteAsset(assetPath);

            TryDeleteDirectoryRaw(pathAbs);
            TryDeleteFileRaw(pathAbs + ".meta");
        }

        private static void ScheduleDeleteRetry(string pathAbs, int retriesRemaining)
        {
            if (retriesRemaining <= 0)
                return;

            EditorApplication.delayCall += () =>
            {
                DeleteDirectoryWithMetaImmediately(pathAbs);

                if (GetPathState(pathAbs) != PathState.Missing)
                {
                    ScheduleDeleteRetry(pathAbs, retriesRemaining - 1);
                    return;
                }

                AssetDatabase.Refresh();
            };
        }

        private static void TryDeleteDirectoryRaw(string pathAbs)
        {
            if (!Directory.Exists(pathAbs))
                return;

            try
            {
                foreach (var filePath in Directory.GetFiles(pathAbs, "*", SearchOption.AllDirectories))
                    TryClearReadOnly(filePath);

                Directory.Delete(pathAbs, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void TryDeleteFileRaw(string fileAbs)
        {
            if (!File.Exists(fileAbs))
                return;

            try
            {
                TryClearReadOnly(fileAbs);
                File.Delete(fileAbs);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        #endregion

        #region Helpers

        private static bool FileContentsEqual(string leftFileAbs, string rightFileAbs)
        {
            if (!File.Exists(leftFileAbs) || !File.Exists(rightFileAbs))
                return false;

            var leftInfo = new FileInfo(leftFileAbs);
            var rightInfo = new FileInfo(rightFileAbs);
            if (leftInfo.Length != rightInfo.Length)
                return false;

            using var leftStream = File.OpenRead(leftFileAbs);
            using var rightStream = File.OpenRead(rightFileAbs);

            const int bufferSize = 8192;
            var leftBuffer = new byte[bufferSize];
            var rightBuffer = new byte[bufferSize];

            while (true)
            {
                var leftRead = leftStream.Read(leftBuffer, 0, leftBuffer.Length);
                var rightRead = rightStream.Read(rightBuffer, 0, rightBuffer.Length);
                if (leftRead != rightRead)
                    return false;

                if (leftRead == 0)
                    return true;

                for (var i = 0; i < leftRead; i++)
                {
                    if (leftBuffer[i] != rightBuffer[i])
                        return false;
                }
            }
        }

        private static string MakeRelative(string fullPath, string rootAbs)
        {
            var full = Path.GetFullPath(fullPath).Replace('\\', '/');
            var root = Path.GetFullPath(rootAbs).Replace('\\', '/').TrimEnd('/') + "/";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full[root.Length..]
                : Path.GetFileName(fullPath);
        }

        private static string TryGetAssetPath(string pathAbs)
        {
            var projectRoot = Path.GetFullPath(UpmPathUtility.ProjectRootAbs).Replace('\\', '/').TrimEnd('/');
            var full = Path.GetFullPath(pathAbs).Replace('\\', '/');
            return !full.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : full[(projectRoot.Length + 1)..];
        }

        private static bool TryReadGuid(string metaPath, out string guid)
        {
            guid = string.Empty;

            foreach (var line in File.ReadLines(metaPath))
            {
                const string prefix = "guid: ";
                if (!line.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                guid = line[prefix.Length..].Trim();
                return !string.IsNullOrWhiteSpace(guid);
            }

            return false;
        }

        private static void EnsureParentDirectoryExists(string targetAbs)
        {
            var parent = Path.GetDirectoryName(targetAbs);
            if (!string.IsNullOrWhiteSpace(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);
        }

        private static void CopyFileIfPresent(string sourceAbs, string targetAbs)
        {
            if (!File.Exists(sourceAbs))
            {
                TryDeleteFileRaw(targetAbs);
                return;
            }

            EnsureParentDirectoryExists(targetAbs);
            FileUtil.CopyFileOrDirectory(sourceAbs, targetAbs);
        }

        private static void TryClearReadOnly(string filePath)
        {
            try
            {
                var attributes = File.GetAttributes(filePath);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        #endregion
    }
}
