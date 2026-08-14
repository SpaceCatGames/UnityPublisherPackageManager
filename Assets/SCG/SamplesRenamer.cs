using System;
using System.IO;
using SCG.UPPM.Helpers;
using SCG.UPPM.Upm;
using UnityEditor;
using UnityEngine;
using static SCG.UPPM.Constants;

namespace SCG.UPPM
{
    /// <summary>
    /// Toggles visibility of sample and documentation folders for an embedded package.
    /// Renames "Samples~" and "Documentation~" folders to their non-tilde variants and back.
    /// Uses a scripting define to keep menu items and state consistent across reloads.
    /// </summary>
    public static class SamplesRenamer
    {
        private static readonly SamplesFolderMetaStorage s_metaStorage = SamplesFolderMetaStorage.CreateForProject();

        #region Events

        /// <summary>
        /// Occurs after a requested Samples and Documentation visibility state is applied and any required import, compilation, and AppDomain reload finish.
        /// The callback receives the completed visibility and the absolute package root path.
        /// <para>
        /// Subscribe from <see cref="InitializeOnLoadMethodAttribute"/> to restore the static subscription after an AppDomain reload.
        /// A subscription made immediately before <see cref="EnsureVisible"/> or <see cref="EnsureHidden"/> works only when the operation does not reload the AppDomain.
        /// </para>
        /// <code>
        /// [InitializeOnLoadMethod]
        /// private static void SubscribeToVisibilityChangeCompleted()
        /// {
        ///     SamplesRenamer.VisibilityChangeCompleted -= OnVisibilityChangeCompleted;
        ///     SamplesRenamer.VisibilityChangeCompleted += OnVisibilityChangeCompleted;
        /// }
        /// </code>
        /// </summary>
        public static event Action<SamplesVisibility, string> VisibilityChangeCompleted;

        #endregion

        #region Public API

        /// <summary>
        /// Checks whether Samples and Documentation folders are currently hidden.
        /// Resolves the package root from BaseFolder and configured Assets or Packages fallbacks during UPM transitions.
        /// The result requires both a matching scripting define state and a matching on-disk folder state.
        /// The method returns true only when Samples are in the tilde-suffixed form and Documentation matches.
        /// </summary>
        public static bool AreFoldersHiddenWithTilda()
        {
            if (DefineSymbolsManager.HasDefineSymbol(SamplesRenamedDefineSymbol))
                return false;

            if (!TryGetPackageRootPath(out var rootPath))
                return false;

            var samples = GetPairState(rootPath, SamplesBase, SamplesRenamed);
            if (samples != FolderPairState.Base)
                return false;

            var docs = GetPairState(rootPath, DocumentationBase, DocumentationRenamed);
            return docs is FolderPairState.Base or FolderPairState.Missing;
        }

        /// <summary>
        /// Ensures Samples and Documentation folders are hidden with tilde suffixes.
        /// The operation fixes partial states by applying the requested visibility to both folders.
        /// A completion notification is published even when the folders already have the requested visibility.
        /// Requests made while another visibility change is active are applied later in FIFO order.
        /// Returns true when any folder or define was changed.
        /// </summary>
        public static bool EnsureHidden() => EnsureVisibility(SamplesVisibility.Hidden);

        /// <summary>
        /// Ensures Samples and Documentation folders are visible without tilde suffixes.
        /// The operation fixes partial states by applying the requested visibility to both folders.
        /// A completion notification is published even when the folders already have the requested visibility.
        /// Requests made while another visibility change is active are applied later in FIFO order.
        /// Returns true when any folder or define was changed.
        /// </summary>
        public static bool EnsureVisible() => EnsureVisibility(SamplesVisibility.Visible);

        /// <summary>
        /// Renames samples and documentation folders to hide or show them in the Project view.
        /// The method mirrors folder renames for both Samples and Documentation to keep structure aligned.
        /// Triggers AssetDatabase refresh via delayed call to let Unity reimport safely.
        /// </summary>
#if !SAMPLES_RENAMED
        [MenuItem(MenuRoot + "Show Samples and Documentation folders", priority = 10000)]
#else
        [MenuItem(MenuRoot + "Hide Samples and Documentation folders", priority = 10000)]
#endif
        public static void HideOrShowSamplesFolder()
        {
            var defineIsSet = DefineSymbolsManager.HasDefineSymbol(SamplesRenamedDefineSymbol);

            if (defineIsSet)
            {
                EnsureHidden();
                return;
            }

            EnsureVisible();
        }

        #endregion

        #region Completion Notifications

        /// <summary>
        /// Publishes a completed visibility request without allowing one subscriber failure to prevent later subscribers from running.
        /// </summary>
        /// <param name="visibility">Applied visibility.</param>
        /// <param name="rootPath">Absolute package root path.</param>
        /// <param name="logException">Optional exception logger used by tests.</param>
        internal static void NotifyVisibilityChangeCompleted(
            SamplesVisibility visibility,
            string rootPath,
            Action<Exception> logException = null)
        {
            var handlers = VisibilityChangeCompleted;
            if (handlers == null)
                return;

            logException ??= Debug.LogException;
            foreach (var @delegate in handlers.GetInvocationList())
            {
                var handler = (Action<SamplesVisibility, string>)@delegate;
                try
                {
                    handler(visibility, rootPath);
                }
                catch (Exception exception)
                {
                    logException(exception);
                }
            }
        }

        #endregion

        #region Internals

        private static bool EnsureVisibility(SamplesVisibility visibility)
        {
            if (!TryGetPackageRootPath(out var rootPath))
                return false;

            var shouldBeVisible = visibility == SamplesVisibility.Visible;
            var defineIsSet = DefineSymbolsManager.HasDefineSymbol(SamplesRenamedDefineSymbol);
            var requiresDomainReload = defineIsSet != shouldBeVisible;
            var changed = false;
            var started = SamplesVisibilityNotificationCoordinator.Enqueue(
                visibility,
                UpmPathUtility.ToAbsolute(rootPath),
                requiresDomainReload,
                () =>
                {
                    var usesAssetDatabasePath = IsAssetDatabasePath(rootPath);
                    if (usesAssetDatabasePath)
                        AssetDatabase.StartAssetEditing();

                    try
                    {
                        if (shouldBeVisible)
                            changed |= RootFolderSyncCoordinator.PrepareForVisibleFolders(rootPath);

                        changed |= EnsurePairState(rootPath, SamplesBase, SamplesRenamed, shouldBeVisible, s_metaStorage);
                        changed |= EnsurePairState(rootPath, DocumentationBase, DocumentationRenamed, shouldBeVisible, s_metaStorage);
                        changed |= shouldBeVisible
                            ? DefineSymbolsManager.AddDefineSymbol(SamplesRenamedDefineSymbol)
                            : DefineSymbolsManager.RemoveDefineSymbol(SamplesRenamedDefineSymbol);
                    }
                    finally
                    {
                        if (usesAssetDatabasePath)
                            AssetDatabase.StopAssetEditing();
                    }

                    if (usesAssetDatabasePath)
                        changed |= RemoveHiddenFolderMetaFiles(rootPath, s_metaStorage);

                    if (!changed)
                        return;

                    if (usesAssetDatabasePath)
                        AssetDatabase.Refresh();

                    RootFolderSyncCoordinator.ScheduleSync();
                });

            return started && changed;
        }

        /// <summary>
        /// Starts a visibility request dequeued after the preceding completion is published.
        /// </summary>
        /// <param name="visibility">Visibility to apply.</param>
        internal static void ApplyQueuedVisibility(SamplesVisibility visibility) => EnsureVisibility(visibility);

        private enum FolderPairState
        {
            Missing = 0,
            Base = 1,
            Renamed = 2,
            Conflict = 3,
            FileConflict = 4
        }

        internal static bool TryGetPackageRootPath(out string rootPath)
        {
            rootPath = string.Empty;

            var settings = UppmSettings.Instance;
            if (settings == null)
            {
                Debug.LogError("UppmSettings is missing.");
                return false;
            }

            var baseFolderPath = settings.BaseFolder == null
                ? string.Empty
                : AssetDatabase.GetAssetPath(settings.BaseFolder);
            _ = TryCombineChildRootPath(AssetsRoot, settings.AssetRootFolder, out var assetsRootPath);
            _ = TryCombineChildRootPath(PackagesRoot, settings.PackageId, out var packagesRootPath);
            if (TryResolveExistingRoot(out rootPath, baseFolderPath, assetsRootPath, packagesRootPath))
                return true;

            var stage = UpmBuildStateStorage.LoadOrCreate().Stage;
            if (UpmBuildFlow.IsBuildPending(stage) || UpmBuildFlow.IsReturnPending(stage))
                return false;

            Debug.LogError("Package root is missing. Assign BaseFolder or configure AssetRootFolder/PackageId in UppmSettings.");
            return false;
        }

        /// <summary>
        /// Selects the first existing package root from candidates ordered by preference.
        /// </summary>
        /// <param name="rootPath">Selected normalized path, or an empty string when no candidate exists.</param>
        /// <param name="candidates">AssetDatabase or absolute paths ordered by preference.</param>
        /// <returns>True when an existing directory was found; otherwise false.</returns>
        internal static bool TryResolveExistingRoot(out string rootPath, params string[] candidates)
        {
            rootPath = string.Empty;
            if (candidates == null)
                return false;

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(UpmPathUtility.ToAbsolute(candidate)))
                    continue;

                rootPath = candidate.Replace("\\", "/").TrimEnd('/');
                return true;
            }

            return false;
        }

        /// <summary>
        /// Combines a Unity root with exactly one safe child folder name.
        /// </summary>
        /// <param name="root">Unity root path.</param>
        /// <param name="folder">Single child folder name.</param>
        /// <param name="combinedPath">Combined Unity path, or an empty string when the child is invalid.</param>
        /// <returns>True when the child is a safe single folder name; otherwise false.</returns>
        internal static bool TryCombineChildRootPath(string root, string folder, out string combinedPath)
        {
            combinedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(folder))
                return false;

            var child = folder.Trim();
            if (child is "." or ".." ||
                Path.IsPathRooted(child) ||
                child.IndexOfAny(new[] { '/', '\\' }) >= 0 ||
                child.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;

            combinedPath = root.Replace("\\", "/").TrimEnd('/') + "/" + child;
            return true;
        }

        private static FolderPairState GetPairState(string rootPath, string baseName, string renamedName)
        {
            var basePath = Path.Combine(rootPath, baseName);
            var renamedPath = Path.Combine(rootPath, renamedName);

            var baseState = GetPathState(basePath);
            var renamedState = GetPathState(renamedPath);
            if (baseState == PathState.File || renamedState == PathState.File)
                return FolderPairState.FileConflict;

            var hasBase = baseState == PathState.Directory;
            var hasRenamed = renamedState == PathState.Directory;
            return hasBase switch
            {
                true when hasRenamed => FolderPairState.Conflict,
                true => FolderPairState.Base,
                _ => hasRenamed ? FolderPairState.Renamed : FolderPairState.Missing
            };
        }

        private static bool EnsurePairState(
            string rootPath,
            string baseName,
            string renamedName,
            bool shouldBeVisible,
            SamplesFolderMetaStorage metaStorage)
        {
            var state = GetPairState(rootPath, baseName, renamedName);

            switch (state)
            {
                case FolderPairState.Missing:
                    return false;
                case FolderPairState.Conflict:
                    throw new InvalidOperationException($"Both '{baseName}' and '{renamedName}' exist. Resolve the conflict manually.");
                case FolderPairState.FileConflict:
                    throw new InvalidOperationException($"A file occupies '{baseName}' or '{renamedName}'. Resolve the path conflict manually.");
                case FolderPairState.Base:
                case FolderPairState.Renamed:
                default:
                    break;
            }

            var basePath = Path.Combine(rootPath, baseName);
            var renamedPath = Path.Combine(rootPath, renamedName);

            if (shouldBeVisible)
            {
                if (state == FolderPairState.Renamed)
                    return false;

                MoveFolder(basePath, renamedPath, rootPath, baseName, true, metaStorage);
                return true;
            }

            if (state == FolderPairState.Base)
                return false;

            MoveFolder(renamedPath, basePath, rootPath, baseName, false, metaStorage);
            return true;
        }

        /// <summary>
        /// Moves a Samples or Documentation folder with filesystem operations and keeps folder metadata outside the hidden tilde path.
        /// The caller owns the shared AssetDatabase editing scope and refresh for the complete visibility transaction.
        /// Do not replace this implementation with <see cref="AssetDatabase.MoveAsset(string, string)"/>.
        /// Unity excludes tilde-suffixed folders such as Samples~ and Documentation~ from the normal AssetDatabase pipeline,
        /// so both visibility directions must use <see cref="Directory.Move(string, string)"/>.
        /// </summary>
        /// <param name="srcPath">Source Unity or absolute filesystem path.</param>
        /// <param name="dstPath">Destination Unity or absolute filesystem path.</param>
        /// <param name="rootPath">Package root used to isolate persisted metadata.</param>
        /// <param name="baseName">Tilde-suffixed folder name.</param>
        /// <param name="shouldBeVisible">Whether metadata must be restored beside the destination.</param>
        /// <param name="metaStorage">Project or test-specific metadata storage.</param>
        internal static void MoveFolder(
            string srcPath,
            string dstPath,
            string rootPath,
            string baseName,
            bool shouldBeVisible,
            SamplesFolderMetaStorage metaStorage)
        {
            var srcFileSystemPath = UpmPathUtility.ToAbsolute(srcPath);
            var dstFileSystemPath = UpmPathUtility.ToAbsolute(dstPath);

            if (!Directory.Exists(srcFileSystemPath))
                return;

            if (UpmPathUtility.PathsEqual(srcFileSystemPath, dstFileSystemPath))
                return;

            EnsureDestinationCanReceiveFolder(dstPath);

            var srcMeta = srcFileSystemPath + ".meta";
            var dstMeta = dstFileSystemPath + ".meta";
            var srcMetaContents = ReadMeta(srcMeta);
            var dstMetaContents = ReadMeta(dstMeta);
            var absoluteRootPath = UpmPathUtility.ToAbsolute(rootPath);
            var storedMeta = metaStorage.Load(absoluteRootPath, baseName);
            var metaToRestore = shouldBeVisible ? storedMeta ?? srcMetaContents ?? dstMetaContents : null;

            if (!shouldBeVisible && srcMetaContents != null)
                metaStorage.Save(absoluteRootPath, baseName, srcMetaContents);
            else if (!shouldBeVisible && storedMeta == null && dstMetaContents != null)
                metaStorage.Save(absoluteRootPath, baseName, dstMetaContents);

            try
            {
                DeleteMeta(srcMeta);
                DeleteMeta(dstMeta);
                Directory.Move(srcFileSystemPath, dstFileSystemPath);
                if (shouldBeVisible && metaToRestore != null)
                {
                    File.WriteAllBytes(dstMeta, metaToRestore);
                    metaStorage.Delete(absoluteRootPath, baseName);
                }
            }
            catch (Exception exception)
            {
                RollBackFolderMove(srcFileSystemPath, dstFileSystemPath, srcMeta, dstMeta, srcMetaContents, dstMetaContents);
                Debug.LogError("Failed to move files. " +
                    "Close any applications that may lock project files " +
                    "(File Explorer windows, IDEs/code editors, VCS clients, antivirus scanners, and any external processes touching the folder) " +
                    "and try again.");
                throw new IOException($"Failed to move '{srcPath}' to '{dstPath}' with its metadata.", exception);
            }
        }

        /// <summary>
        /// Removes metadata that Unity may recreate beside tilde-suffixed folders when an asset-editing scope ends.
        /// Keep this filesystem cleanup between <see cref="AssetDatabase.StopAssetEditing"/> and the transaction's
        /// single <see cref="AssetDatabase.Refresh()"/> call. Tilde folders are excluded from import and must never
        /// retain adjacent metadata; the visible-folder GUID is preserved by <see cref="SamplesFolderMetaStorage"/>.
        /// </summary>
        /// <param name="rootPath">Package root containing hidden Samples and Documentation folders.</param>
        /// <param name="metaStorage">Storage that preserves a legacy hidden-folder GUID before its metadata is removed.</param>
        /// <returns><see langword="true"/> when at least one hidden-folder metadata file was removed; otherwise <see langword="false"/>.</returns>
        internal static bool RemoveHiddenFolderMetaFiles(string rootPath, SamplesFolderMetaStorage metaStorage)
        {
            if (metaStorage == null)
                throw new ArgumentNullException(nameof(metaStorage));

            var absoluteRootPath = UpmPathUtility.ToAbsolute(rootPath);
            var samplesChanged = PreserveAndDeleteHiddenMeta(absoluteRootPath, SamplesBase, metaStorage);
            var documentationChanged = PreserveAndDeleteHiddenMeta(absoluteRootPath, DocumentationBase, metaStorage);
            return samplesChanged || documentationChanged;
        }

        private static bool PreserveAndDeleteHiddenMeta(
            string absoluteRootPath,
            string hiddenFolderName,
            SamplesFolderMetaStorage metaStorage)
        {
            var metaPath = Path.Combine(absoluteRootPath, hiddenFolderName) + ".meta";
            if (!File.Exists(metaPath))
                return false;

            if (metaStorage.Load(absoluteRootPath, hiddenFolderName) == null)
                metaStorage.Save(absoluteRootPath, hiddenFolderName, File.ReadAllBytes(metaPath));

            DeleteMeta(metaPath);
            return true;
        }

        private static byte[] ReadMeta(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

        private static void DeleteMeta(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        internal static bool IsAssetDatabasePath(string path)
        {
            var normalized = NormalizeUnityPath(path);
            return normalized.StartsWith(AssetsRoot, StringComparison.Ordinal) ||
                   normalized.StartsWith(PackagesRoot, StringComparison.Ordinal);
        }

        private static string NormalizeUnityPath(string path) => path.Replace('\\', '/').TrimStart('/');

        private static void RollBackFolderMove(
            string srcFileSystemPath,
            string dstFileSystemPath,
            string srcMeta,
            string dstMeta,
            byte[] srcMetaContents,
            byte[] dstMetaContents)
        {
            try
            {
                if (!Directory.Exists(srcFileSystemPath) && Directory.Exists(dstFileSystemPath))
                    Directory.Move(dstFileSystemPath, srcFileSystemPath);

                RestoreMeta(srcMeta, srcMetaContents);
                RestoreMeta(dstMeta, dstMetaContents);
            }
            catch (Exception rollbackException)
            {
                Debug.LogException(rollbackException);
            }
        }

        private static void RestoreMeta(string path, byte[] contents)
        {
            if (contents != null && !File.Exists(path))
                File.WriteAllBytes(path, contents);
        }

        private static void EnsureDestinationCanReceiveFolder(string dstPath)
        {
            var dstState = GetPathState(dstPath);
            switch (dstState)
            {
                case PathState.File:
                    throw new IOException($"Cannot move folder to '{dstPath}' because a file already exists at that path.");
                case PathState.Directory:
                    throw new IOException($"Cannot move folder to '{dstPath}' because a directory already exists at that path.");
                case PathState.Missing:
                case PathState.MetaOnly:
                default:
                    break;
            }

        }

        private static PathState GetPathState(string path)
        {
            var fileSystemPath = UpmPathUtility.ToAbsolute(path);
            return Directory.Exists(fileSystemPath)
                ? PathState.Directory
                : File.Exists(fileSystemPath)
                    ? PathState.File
                    : PathState.Missing;
        }

        #endregion
    }
}
