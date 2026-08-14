using System;
using System.Linq;
using SCG.UPPM.Upm;
using UnityEditor;
using UnityEngine;

namespace SCG.UPPM
{
    /// <summary>
    /// Coordinates synchronization between hidden package folders and editable root mirrors under Assets.
    /// The workflow keeps hidden Samples~/Documentation~ folders mirrored from root copies while they are hidden,
    /// removes the mirrors when users reveal the original folders, and stops on unsafe GUID or content conflicts.
    /// </summary>
    internal static class RootFolderSyncCoordinator
    {
        #region Constants

        private const int MenuPriority = 10005;

        #endregion

        #region Fields

        private static readonly (string HiddenName, string VisibleName, string MirrorAssetPath)[] s_folderPairs =
        {
            (Constants.SamplesBase, Constants.SamplesRenamed, Constants.AssetsRoot + Constants.RootSamplesSyncFolder),
            (Constants.DocumentationBase, Constants.DocumentationRenamed, Constants.AssetsRoot + Constants.RootDocumentationSyncFolder)
        };

        private static bool s_syncScheduled;
        private static bool s_syncRunning;
        private static bool s_syncDirty;

        #endregion

        #region Initialization

        /// <summary>
        /// Schedules the initial mirror reconciliation when persistent settings exist and the current process may access project assets.
        /// </summary>
        internal static void TryScheduleInitialSync()
        {
            if (IsAssetImportWorkerProcess())
                return;

            if (!UppmSettings.TryGetPersistentInstance(out _))
                return;

            ScheduleSync();
        }

        #endregion

        #region Menu

        /// <summary>
        /// Toggles synchronization of hidden Samples~/Documentation~ folders into root mirror folders under Assets.
        /// The setting is stored in UppmSettings and immediately schedules a reconciliation pass.
        /// The menu entry remains available even when synchronization is currently disabled.
        /// </summary>
        [MenuItem(Constants.RootFolderSyncMenuPath, priority = MenuPriority)]
        private static void ToggleSync()
        {
            if (!UppmSettings.TryGetPersistentInstance(out var settings))
            {
                Debug.LogError($"[{nameof(RootFolderSyncCoordinator)}] Create an {nameof(UppmSettings)} asset before enabling root synchronization.");
                return;
            }

            if (settings.SetRootFolderSyncEnabled(!settings.IsRootFolderSyncEnabled))
                ScheduleSync();
        }

        [MenuItem(Constants.RootFolderSyncMenuPath, true)]
        private static bool ValidateToggleSync()
        {
            var enabled = UppmSettings.TryGetPersistentInstance(out var settings) && settings.IsRootFolderSyncEnabled;
            Menu.SetChecked(Constants.RootFolderSyncMenuPath, enabled);
            return true;
        }

        #endregion

        #region Scheduling

        internal static bool IsSyncEnabled() =>
            UppmSettings.TryGetPersistentInstance(out var settings) && settings.IsRootFolderSyncEnabled;

        internal static void ScheduleSync()
        {
            if (IsAssetImportWorkerProcess())
                return;

            s_syncDirty = true;
            if (s_syncScheduled)
                return;

            s_syncScheduled = true;
            EditorApplication.delayCall -= RunScheduledSync;
            EditorApplication.delayCall += RunScheduledSync;
        }

        internal static bool ShouldReactToAssetChanges(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (IsAssetImportWorkerProcess())
                return false;

            if (!IsSyncEnabled())
                return false;

            return HasRelevantAssetPath(importedAssets) ||
                   HasRelevantAssetPath(deletedAssets) ||
                   HasRelevantAssetPath(movedAssets) ||
                   HasRelevantAssetPath(movedFromAssetPaths);
        }

        internal static bool PrepareForVisibleFolders(string packageRootPath)
        {
            if (IsAssetImportWorkerProcess())
                return false;

            if (!IsSyncEnabled() || string.IsNullOrWhiteSpace(packageRootPath))
                return false;

            var changed = FlushRootMirrorsToHidden(packageRootPath);

            changed = s_folderPairs.Aggregate(changed,
                (current, pair) => current | DeleteMirrorIfSafe(GetMirrorAbs(pair.MirrorAssetPath), pair.MirrorAssetPath));

            return changed;
        }

        private static void RunScheduledSync()
        {
            EditorApplication.delayCall -= RunScheduledSync;
            s_syncScheduled = false;

            if (IsAssetImportWorkerProcess())
                return;

            if (s_syncRunning)
            {
                s_syncDirty = true;
                return;
            }

            if (EditorApplication.isUpdating || EditorApplication.isCompiling)
            {
                ScheduleSync();
                return;
            }

            s_syncDirty = false;
            s_syncRunning = true;

            try
            {
                PerformSync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(RootFolderSyncCoordinator)}] Root synchronization failed: {ex.Message}");
            }
            finally
            {
                s_syncRunning = false;

                if (s_syncDirty)
                    ScheduleSync();
            }
        }

        #endregion

        #region Core

        private static void PerformSync()
        {
            bool changed;

            if (!UppmSettings.TryGetPersistentInstance(out var settings))
                return;

            if (!settings.IsRootFolderSyncEnabled)
            {
                changed = CleanupAllMirrors();
                if (changed)
                    AssetDatabase.Refresh();

                return;
            }

            if (!SamplesRenamer.TryGetPackageRootPath(out var packageRootPath))
                return;

            changed = s_folderPairs.Aggregate(false,
                (current, pair) => current | SyncPair(packageRootPath, pair.HiddenName, pair.VisibleName, pair.MirrorAssetPath));

            if (changed)
                AssetDatabase.Refresh();
        }

        private static bool SyncPair(string packageRootPath, string hiddenName, string visibleName, string mirrorAssetPath)
        {
            var hiddenAbs = GetPackageFolderAbs(packageRootPath, hiddenName);
            var visibleAbs = GetPackageFolderAbs(packageRootPath, visibleName);
            var mirrorAbs = GetMirrorAbs(mirrorAssetPath);

            var hiddenState = RootFolderSyncFileOperations.GetPathState(hiddenAbs);
            var visibleState = RootFolderSyncFileOperations.GetPathState(visibleAbs);
            var mirrorState = RootFolderSyncFileOperations.GetPathState(mirrorAbs);

            if (HasFileConflict(hiddenState, visibleState, mirrorState))
            {
                Debug.LogError($"[{nameof(RootFolderSyncCoordinator)}] A file occupies '{hiddenName}', '{visibleName}', or '{mirrorAssetPath}'. Resolve the file-vs-folder conflict manually before synchronization can continue.");
                return false;
            }

            if (hiddenState == PathState.MetaOnly ||
                visibleState == PathState.MetaOnly)
            {
                Debug.LogError($"[{nameof(RootFolderSyncCoordinator)}] An orphan .meta exists for '{hiddenName}' or '{visibleName}'. Resolve the invalid folder state manually before synchronization can continue.");
                return false;
            }

            var hiddenExists = hiddenState == PathState.Directory;
            var visibleExists = visibleState == PathState.Directory;
            if (hiddenExists && visibleExists)
            {
                Debug.LogError($"[{nameof(RootFolderSyncCoordinator)}] Both '{hiddenName}' and '{visibleName}' exist. Resolve the folder conflict manually before synchronization can continue.");
                return false;
            }

            if (visibleExists)
                return HandleVisibleState(visibleAbs, mirrorAbs, visibleName, mirrorAssetPath, mirrorState);

            if (hiddenExists)
                return HandleHiddenState(hiddenAbs, mirrorAbs, mirrorState);

            return mirrorState != PathState.Missing &&
                   DeleteMirrorIfSafe(mirrorAbs, mirrorAssetPath);
        }

        private static bool HandleHiddenState(
            string hiddenAbs,
            string mirrorAbs,
            PathState mirrorState) =>
            mirrorState == PathState.Directory
                ? RootFolderSyncFileOperations.MirrorDirectoryWithMeta(mirrorAbs, hiddenAbs)
                : RootFolderSyncFileOperations.MirrorDirectoryWithMeta(hiddenAbs, mirrorAbs);

        private static bool HandleVisibleState(
            string visibleAbs,
            string mirrorAbs,
            string visibleName,
            string mirrorAssetPath,
            PathState mirrorState)
        {
            if (mirrorState == PathState.Missing)
                return false;

            if (mirrorState != PathState.Directory)
                return DeleteMirrorIfSafe(mirrorAbs, mirrorAssetPath);

            if (!RootFolderSyncFileOperations.AreDirectoryGuidsEqual(visibleAbs, mirrorAbs))
            {
                Debug.LogError($"[{nameof(RootFolderSyncCoordinator)}] GUID mismatch detected between '{visibleName}' and '{mirrorAssetPath}'. Root sync was stopped to avoid overwriting user data.");
                return false;
            }

            if (RootFolderSyncFileOperations.AreDirectoriesIdentical(visibleAbs, mirrorAbs))
                return RootFolderSyncFileOperations.DeleteDirectoryWithMeta(mirrorAbs);
            Debug.LogError($"[{nameof(RootFolderSyncCoordinator)}] Content mismatch detected between '{visibleName}' and '{mirrorAssetPath}'. Root sync was stopped because the visible folder no longer matches the mirror copy.");
            return false;
        }

        private static bool CleanupAllMirrors() =>
            s_folderPairs.Aggregate(false,
                (current, pair) => current | DeleteMirrorIfSafe(GetMirrorAbs(pair.MirrorAssetPath), pair.MirrorAssetPath));

        private static bool FlushRootMirrorsToHidden(string packageRootPath)
        {
            var changed = false;

            foreach (var (hiddenName, _, mirrorAssetPath) in s_folderPairs)
            {
                var hiddenAbs = GetPackageFolderAbs(packageRootPath, hiddenName);
                var mirrorAbs = GetMirrorAbs(mirrorAssetPath);

                var hiddenState = RootFolderSyncFileOperations.GetPathState(hiddenAbs);
                var mirrorState = RootFolderSyncFileOperations.GetPathState(mirrorAbs);

                if (hiddenState == PathState.File ||
                    mirrorState == PathState.File)
                {
                    Debug.LogError($"[{nameof(RootFolderSyncCoordinator)}] A file occupies '{hiddenName}' or '{mirrorAssetPath}'. Resolve the file-vs-folder conflict manually before synchronization can continue.");
                    continue;
                }

                if (hiddenState == PathState.MetaOnly)
                {
                    Debug.LogError($"[{nameof(RootFolderSyncCoordinator)}] An orphan .meta exists for '{hiddenName}'. Resolve the invalid folder state manually before synchronization can continue.");
                    continue;
                }

                if (hiddenState != PathState.Directory ||
                    mirrorState != PathState.Directory)
                    continue;

                changed |= RootFolderSyncFileOperations.MirrorDirectoryWithMeta(mirrorAbs, hiddenAbs);
            }

            return changed;
        }

        #endregion

        #region Path Matching

        private static bool HasRelevantAssetPath(string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return false;

            foreach (var rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                    continue;

                var path = rawPath.Replace("\\", "/");
                if (s_folderPairs.Any(pair => MatchesAssetFolder(path, pair.MirrorAssetPath)))
                    return true;

                if (!TryGetPackageRootAssetPath(out var packageRootPath))
                    continue;

                if ((from pair
                     in s_folderPairs
                     let hiddenPath = CombineAssetPath(packageRootPath, pair.HiddenName)
                     let visiblePath = CombineAssetPath(packageRootPath, pair.VisibleName)
                     where MatchesAssetFolder(path, hiddenPath) || MatchesAssetFolder(path, visiblePath)
                     select hiddenPath).Any())
                    return true;
            }

            return false;
        }

        private static bool MatchesAssetFolder(string assetPath, string folderAssetPath) =>
            string.Equals(assetPath, folderAssetPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assetPath, folderAssetPath + ".meta", StringComparison.OrdinalIgnoreCase) ||
            assetPath.StartsWith(folderAssetPath + "/", StringComparison.OrdinalIgnoreCase);

        private static string CombineAssetPath(string basePath, string childName) =>
            $"{basePath.TrimEnd('/')}/{childName}";

        private static bool HasFileConflict(
            PathState hiddenState,
            PathState visibleState,
            PathState mirrorState) =>
            hiddenState == PathState.File ||
            visibleState == PathState.File ||
            mirrorState == PathState.File;

        private static bool DeleteMirrorIfSafe(string mirrorAbs, string mirrorAssetPath)
        {
            if (RootFolderSyncFileOperations.GetPathState(mirrorAbs) != PathState.File)
                return RootFolderSyncFileOperations.DeleteDirectoryWithMeta(mirrorAbs);
            Debug.LogError($"[{nameof(RootFolderSyncCoordinator)}] Cannot clean '{mirrorAssetPath}' because a file exists at that path. Resolve the file-vs-folder conflict manually.");
            return false;
        }

        private static bool IsAssetImportWorkerProcess() =>
            AssetDatabase.IsAssetImportWorkerProcess();

        private static bool TryGetPackageRootAssetPath(out string packageRootPath)
        {
            packageRootPath = string.Empty;

            if (!UppmSettings.TryGetPersistentInstance(out var settings) || settings.BaseFolder == null)
                return false;

            packageRootPath = AssetDatabase.GetAssetPath(settings.BaseFolder)?.Replace("\\", "/") ?? string.Empty;
            return !string.IsNullOrWhiteSpace(packageRootPath);
        }

        private static string GetPackageFolderAbs(string packageRootPath, string folderName) =>
            UpmPathUtility.ToAbsolute(CombineAssetPath(packageRootPath, folderName));

        private static string GetMirrorAbs(string mirrorAssetPath) =>
            UpmPathUtility.ToAbsolute(mirrorAssetPath);

        #endregion
    }
}
