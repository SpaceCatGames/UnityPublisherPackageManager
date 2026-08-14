using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SCG.UPPM.Upm
{
    /// <summary>
    /// Provides file operations used by the staged UPM workflow.
    /// The helpers move folder roots together with their meta files and make interrupted moves resumable.
    /// Exceptions are propagated so the workflow can clear persisted markers and avoid reload retry loops.
    /// </summary>
    internal static class UpmFileOperations
    {
        /// <summary>
        /// Ensures the destination folder and its root meta file are absent.
        /// The method does not create directories or modify files.
        /// </summary>
        /// <param name="dstAbs">Absolute destination directory path.</param>
        /// <exception cref="ArgumentException">Thrown when the destination path is empty.</exception>
        /// <exception cref="IOException">Thrown when the destination folder or its meta file exists.</exception>
        public static void EnsureDestinationIsAvailable(string dstAbs)
        {
            if (string.IsNullOrWhiteSpace(dstAbs))
                throw new ArgumentException("Destination path is empty.", nameof(dstAbs));

            if (Directory.Exists(dstAbs))
                throw new IOException($"Destination folder already exists: {dstAbs}");

            var metaDst = GetMetaPath(dstAbs);
            if (File.Exists(metaDst))
                throw new IOException($"Destination meta file already exists: {metaDst}");
        }

        /// <summary>
        /// Ensures a folder is located at the destination and its root meta file is moved with it.
        /// When a previous process moved the folder but not its meta file, this method completes only the pending meta move.
        /// </summary>
        /// <param name="srcAbs">Absolute source directory path.</param>
        /// <param name="dstAbs">Absolute destination directory path.</param>
        /// <returns>True when the folder was moved during this call; otherwise false.</returns>
        /// <exception cref="ArgumentException">Thrown when a source or destination path is empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when neither location contains the source folder.</exception>
        /// <exception cref="IOException">Thrown when both locations contain the folder or their meta files conflict.</exception>
        public static bool EnsureFolderMovedWithMeta(string srcAbs, string dstAbs)
        {
            if (string.IsNullOrWhiteSpace(srcAbs))
                throw new ArgumentException("Source path is empty.", nameof(srcAbs));

            if (string.IsNullOrWhiteSpace(dstAbs))
                throw new ArgumentException("Destination path is empty.", nameof(dstAbs));

            if (UpmPathUtility.PathsEqual(srcAbs, dstAbs))
                return false;

            var sourceExists = Directory.Exists(srcAbs);
            var destinationExists = Directory.Exists(dstAbs);
            if (sourceExists && destinationExists)
                throw new IOException($"Source and destination folders both exist: {srcAbs}; {dstAbs}");

            if (!sourceExists && !destinationExists)
                throw new InvalidOperationException($"Neither source nor destination folder exists: {srcAbs}; {dstAbs}");

            if (!sourceExists)
            {
                CompletePendingMetaMove(srcAbs, dstAbs);
                return false;
            }

            MoveFolderWithMeta(srcAbs, dstAbs);
            return true;
        }

        /// <summary>
        /// Moves a folder and its root meta file from source to destination.
        /// The method creates parent directories for the destination and requires the destination folder and meta file to be absent.
        /// If the meta move fails after the folder move, it attempts to restore the source folder before propagating the failure.
        /// </summary>
        /// <param name="srcAbs">Absolute source directory path.</param>
        /// <param name="dstAbs">Absolute destination directory path.</param>
        /// <exception cref="DirectoryNotFoundException">Thrown when the source folder is missing.</exception>
        /// <exception cref="IOException">Thrown when the destination folder or its meta file exists.</exception>
        public static void MoveFolderWithMeta(string srcAbs, string dstAbs)
        {
            if (!Directory.Exists(srcAbs))
                throw new DirectoryNotFoundException(srcAbs);

            if (UpmPathUtility.PathsEqual(srcAbs, dstAbs))
                return;

            EnsureDestinationIsAvailable(dstAbs);
            EnsureDestinationParentExists(dstAbs);

            var folderMoved = false;
            try
            {
                FileUtil.MoveFileOrDirectory(srcAbs, dstAbs);
                folderMoved = true;
                MoveMetaIfPresent(srcAbs, dstAbs);
            }
            catch (Exception moveException)
            {
                var rollbackException = folderMoved ? TryRestoreSourceFolder(srcAbs, dstAbs) : null;
                Debug.LogError("Failed to move files. " +
                    "Close any applications that may lock project files " +
                    "(File Explorer windows, IDEs/code editors, VCS clients, antivirus scanners, and any external processes touching the folder) " +
                    "and try again.");

                if (rollbackException != null)
                    throw new AggregateException("Folder move failed and the source folder could not be restored.", moveException, rollbackException);

                throw;
            }
        }

        /// <summary>
        /// Moves a root meta file left at the source location by an interrupted folder move.
        /// </summary>
        /// <param name="srcAbs">Absolute former source directory path.</param>
        /// <param name="dstAbs">Absolute current destination directory path.</param>
        private static void CompletePendingMetaMove(string srcAbs, string dstAbs)
        {
            var metaSrc = GetMetaPath(srcAbs);
            if (!File.Exists(metaSrc))
                return;

            var metaDst = GetMetaPath(dstAbs);
            if (File.Exists(metaDst))
                throw new IOException($"Source and destination meta files both exist: {metaSrc}; {metaDst}");

            FileUtil.MoveFileOrDirectory(metaSrc, metaDst);
        }

        /// <summary>
        /// Moves the root meta file when the source folder has one.
        /// </summary>
        /// <param name="srcAbs">Absolute source directory path.</param>
        /// <param name="dstAbs">Absolute destination directory path.</param>
        private static void MoveMetaIfPresent(string srcAbs, string dstAbs)
        {
            var metaSrc = GetMetaPath(srcAbs);
            if (!File.Exists(metaSrc))
                return;

            var metaDst = GetMetaPath(dstAbs);
            if (File.Exists(metaDst))
                throw new IOException($"Destination meta file already exists: {metaDst}");

            FileUtil.MoveFileOrDirectory(metaSrc, metaDst);
        }

        /// <summary>
        /// Attempts to move a destination folder back to its source location after a meta move failure.
        /// </summary>
        /// <param name="srcAbs">Absolute original source directory path.</param>
        /// <param name="dstAbs">Absolute destination directory path.</param>
        /// <returns>The rollback exception when restoration fails; otherwise null.</returns>
        private static Exception TryRestoreSourceFolder(string srcAbs, string dstAbs)
        {
            try
            {
                if (!Directory.Exists(dstAbs) || Directory.Exists(srcAbs))
                    return null;

                FileUtil.MoveFileOrDirectory(dstAbs, srcAbs);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        /// <summary>
        /// Creates the parent directory required for a destination folder.
        /// </summary>
        /// <param name="dstAbs">Absolute destination directory path.</param>
        private static void EnsureDestinationParentExists(string dstAbs)
        {
            var dstParent = Path.GetDirectoryName(dstAbs);
            if (!string.IsNullOrEmpty(dstParent) && !Directory.Exists(dstParent))
                Directory.CreateDirectory(dstParent);
        }

        /// <summary>
        /// Gets the root meta file path for a directory path.
        /// </summary>
        /// <param name="directoryAbs">Absolute directory path.</param>
        /// <returns>Absolute root meta file path.</returns>
        private static string GetMetaPath(string directoryAbs) => directoryAbs.TrimEnd('\\', '/') + ".meta";
    }
}
