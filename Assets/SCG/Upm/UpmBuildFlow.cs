using SCG.UnityAssetPublisherTools.Helpers;
using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace SCG.UnityAssetPublisherTools.Upm
{
    /// <summary>
    /// Implements the staged UPM build and return flows.
    /// The build flow moves the asset folder into Temp and then into Packages as an embedded package.
    /// The return flow moves the embedded package back into Assets and resolves packages afterwards.
    /// All steps persist a small state so the flow can resume after a domain reload or interrupted file move.
    /// </summary>
    internal static class UpmBuildFlow
    {
        private const double ResolveTimeoutSeconds = 60;

        private static bool s_waitingForPackageRegistration;
        private static double s_packageRegistrationDeadline;

        #region Public API

        /// <summary>
        /// Starts the build flow for converting the configured project folder into an embedded package.
        /// The method persists its state before every filesystem move and enables package compilation after Package Manager registration.
        /// </summary>
        public static void Build()
        {
            var cfg = AssetPublisherToolsSettings.Instance;
            if (!cfg.TrySyncImmediately())
                throw new InvalidOperationException("Unity editor is busy. Try again after compilation or import completes.");

            var folderName = UpmPathUtility.GetSafeFolderName(cfg.AssetRootFolder);
            var originalRootAbs = UpmPathUtility.ResolveOriginalRootAbs(cfg, folderName);
            if (string.IsNullOrEmpty(originalRootAbs) || !Directory.Exists(originalRootAbs))
                throw new InvalidOperationException($"Source folder does not exist: {originalRootAbs}");

            var packageId = ResolveBuildPackageId(cfg, originalRootAbs);
            var packagesRootAbs = Path.Combine(
                UpmPathUtility.ProjectRootAbs,
                UpmConstants.PackagesFolderName,
                UpmPathUtility.GetSafeFolderName(packageId));
            UpmFileOperations.EnsureDestinationIsAvailable(packagesRootAbs);

            var tempRootAbs = Path.Combine(UpmPathUtility.ProjectRootAbs, UpmConstants.TempFolderName, folderName);
            if (Directory.Exists(tempRootAbs))
                throw new InvalidOperationException($"Staging folder already exists: {tempRootAbs}");

            var st = UpmBuildStateStorage.LoadOrCreate();
            st.AssetRootFolder = folderName;
            st.OriginalRootAbs = originalRootAbs;
            st.TempRootAbs = tempRootAbs;
            st.PackagesRootAbs = packagesRootAbs;
            st.PackageId = packageId;
            st.Stage = UpmStage.BuildStarted;
            UpmBuildStateStorage.Save(st);

            EditorApplication.delayCall += MoveOriginalToTemp;
        }

        /// <summary>
        /// Starts the return flow for converting the embedded package back into a project folder.
        /// The flow moves the folder out of Packages before resolving Package Manager.
        /// On completion, the UPM define symbol is removed to recompile into project mode.
        /// </summary>
        public static void Return()
        {
#if !UPM_PACKAGE
            Debug.LogError($"[{nameof(UpmBuildFlow)}] Return requires the {UpmConstants.UpmDefine} define.");
#else
            var cfg = AssetPublisherToolsSettings.Instance;
            if (!cfg.TrySyncImmediately())
                throw new InvalidOperationException("Unity editor is busy. Try again after compilation or import completes.");

            var st = UpmBuildStateStorage.LoadOrCreate();
            if (string.IsNullOrWhiteSpace(st.PackageId))
                st.PackageId = cfg.PackageId;

            if (string.IsNullOrWhiteSpace(st.PackageId))
                throw new InvalidOperationException("Could not resolve package id for return.");

            st.Stage = UpmStage.ReturnStarted;
            UpmBuildStateStorage.Save(st);
            EditorApplication.delayCall += MovePackagesBackToProject;
#endif
        }

        /// <summary>
        /// Resumes the persisted build or return operation after a domain reload.
        /// The method schedules the next action and lets that action reconcile any filesystem move completed before the previous process stopped.
        /// </summary>
        public static void TryResumePendingWork()
        {
            switch (UpmBuildStateStorage.LoadOrCreate().Stage)
            {
                case UpmStage.BuildStarted:
                    EditorApplication.delayCall += MoveOriginalToTemp;
                    return;
                case UpmStage.BuildMovedToTemp:
                    EditorApplication.delayCall += PrepareTempBuild;
                    return;
                case UpmStage.BuildReadyToMove:
                    EditorApplication.delayCall += MoveTempToPackagesAndResolve;
                    return;
                case UpmStage.BuildMovedToPackages:
                    EditorApplication.delayCall += ResolveAfterMove;
                    return;
                case UpmStage.ReturnStarted:
                    EditorApplication.delayCall += MovePackagesBackToProject;
                    return;
                case UpmStage.ReturnMovedToProject:
                case UpmStage.ReturnResolveStarted:
                    EditorApplication.delayCall += ResolveAfterReturnMove;
                    return;
                case UpmStage.ReturnResolved:
                    EditorApplication.delayCall += CompleteReturnWhenEditorIdle;
                    return;
                default:
                    return;
            }
        }

        #endregion

        #region Build Steps

        /// <summary>
        /// Moves the configured project folder into Temp and records the completed move.
        /// When the folder is already in Temp, the method completes a pending meta move and continues the workflow.
        /// </summary>
        private static void MoveOriginalToTemp()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.BuildStarted)
                return;

            UpmFileOperations.EnsureDestinationIsAvailable(st.PackagesRootAbs);
            UpmFileOperations.EnsureFolderMovedWithMeta(st.OriginalRootAbs, st.TempRootAbs);
            st.Stage = UpmStage.BuildMovedToTemp;
            UpmBuildStateStorage.Save(st);
            EditorApplication.delayCall += PrepareTempBuild;
        }

        /// <summary>
        /// Prepares package metadata in the staging folder and records that the package can move into Packages.
        /// </summary>
        private static void PrepareTempBuild()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.BuildMovedToTemp)
                return;

            if (!Directory.Exists(st.TempRootAbs))
                throw new InvalidOperationException($"Staging folder is missing: {st.TempRootAbs}");

            var cfg = AssetPublisherToolsSettings.Instance;
            SamplesMetaBaker.Bake(st.TempRootAbs);

            var tempPackageJsonAbs = UpmPackageJsonStaging.EnsureEffectivePackageJson(cfg, st.OriginalRootAbs, st.TempRootAbs);
            SyncPackageJsonFromSettings(cfg, tempPackageJsonAbs);
            var packageId = PackageJsonUtility.GetPackageName(tempPackageJsonAbs);
            if (string.IsNullOrWhiteSpace(packageId))
                throw new InvalidOperationException("package.json does not contain a valid \"name\" field.");

            if (!string.Equals(packageId, st.PackageId, StringComparison.Ordinal))
                throw new InvalidOperationException("package.json name changed while the package was being staged.");

            st.Stage = UpmStage.BuildReadyToMove;
            UpmBuildStateStorage.Save(st);
            EditorApplication.delayCall += MoveTempToPackagesAndResolve;
        }

        /// <summary>
        /// Moves the prepared package into Packages and starts Package Manager resolution.
        /// When the package is already in Packages, the method completes a pending meta move before resolving it.
        /// </summary>
        private static void MoveTempToPackagesAndResolve()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.BuildReadyToMove)
                return;

            UpmFileOperations.EnsureFolderMovedWithMeta(st.TempRootAbs, st.PackagesRootAbs);
            st.Stage = UpmStage.BuildMovedToPackages;
            UpmBuildStateStorage.Save(st);
            AssetDatabase.Refresh();
            ResolveAfterMove();
        }

        /// <summary>
        /// Resolves Package Manager after moving the package into Packages.
        /// </summary>
        private static void ResolveAfterMove()
        {
            ResolveAndWaitForPackageRegistration();
        }

        #endregion

        #region Return Steps

        /// <summary>
        /// Moves the embedded package back into its project location and starts Package Manager resolution.
        /// When the folder is already under Assets, the method completes a pending meta move before resolving it.
        /// </summary>
        private static void MovePackagesBackToProject()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.ReturnStarted)
                return;

            var cfg = AssetPublisherToolsSettings.Instance;
            var folderName = UpmPathUtility.GetSafeFolderName(cfg.AssetRootFolder);
            var packagesRootAbs = !string.IsNullOrWhiteSpace(st.PackagesRootAbs)
                ? st.PackagesRootAbs
                : Path.Combine(
                    UpmPathUtility.ProjectRootAbs,
                    UpmConstants.PackagesFolderName,
                    UpmPathUtility.GetSafeFolderName(st.PackageId));
            var originalRootAbs = !string.IsNullOrWhiteSpace(st.OriginalRootAbs)
                ? st.OriginalRootAbs
                : UpmPathUtility.ToAbsolute(UpmConstants.AssetsFolderName + "/" + folderName);

            UpmFileOperations.EnsureFolderMovedWithMeta(packagesRootAbs, originalRootAbs);
            st.OriginalRootAbs = originalRootAbs;
            st.Stage = UpmStage.ReturnResolveStarted;
            UpmBuildStateStorage.Save(st);
            AssetDatabase.Refresh();
            ResolveAfterReturnMove();
        }

        /// <summary>
        /// Resolves Package Manager after moving the package out of Packages.
        /// </summary>
        private static void ResolveAfterReturnMove()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage == UpmStage.ReturnMovedToProject)
            {
                st.Stage = UpmStage.ReturnResolveStarted;
                UpmBuildStateStorage.Save(st);
            }

            ResolveAndWaitForPackageRegistration();
        }

        #endregion

        #region Package Manager Resolution

        /// <summary>
        /// Starts Package Manager resolution and waits for package registration changes.
        /// The wait is removed after completion, failure to start, or timeout so the persisted state can retry after a reload.
        /// </summary>
        private static void ResolveAndWaitForPackageRegistration()
        {
            if (TryCompleteBuildIfRegistered() || TryCompleteReturnIfUnregistered() || s_waitingForPackageRegistration)
                return;

            s_waitingForPackageRegistration = true;
            s_packageRegistrationDeadline = EditorApplication.timeSinceStartup + ResolveTimeoutSeconds;
            Events.registeredPackages += OnPackagesRegistered;
            EditorApplication.update += CheckPackageRegistrationTimeout;

            try
            {
                Client.Resolve();
            }
            catch
            {
                StopWaitingForPackageRegistration();
                throw;
            }
        }

        /// <summary>
        /// Processes package registration notifications and completes the pending workflow when its expected state is visible.
        /// </summary>
        /// <param name="_">Package registration event payload.</param>
        private static void OnPackagesRegistered(PackageRegistrationEventArgs _)
        {
            if (TryCompleteBuildIfRegistered())
            {
                StopWaitingForPackageRegistration();
                return;
            }

            if (TryCompleteReturnIfUnregistered())
            {
                StopWaitingForPackageRegistration();
                return;
            }

            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.ReturnResolveStarted || IsPackageRegistered(st.PackageId))
                return;

            StopWaitingForPackageRegistration();
            st.Stage = UpmStage.ReturnResolved;
            UpmBuildStateStorage.Save(st);
            EditorApplication.delayCall += CompleteReturnWhenEditorIdle;
        }

        /// <summary>
        /// Completes the persisted build operation when Package Manager already registers the embedded package.
        /// </summary>
        /// <returns>True when the build operation was completed; otherwise false.</returns>
        private static bool TryCompleteBuildIfRegistered()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage == UpmStage.BuildMovedToPackages && IsPackageRegistered(st.PackageId))
            {
                st.Stage = UpmStage.BuildResolved;
                UpmBuildStateStorage.Save(st);
                DefineSymbolsManager.AddDefineSymbol(UpmConstants.UpmDefine);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Completes the persisted return operation when Package Manager no longer registers the returned package.
        /// This handles a domain reload that occurs after a resolve request is started but before its event handler can run.
        /// </summary>
        /// <returns>True when the return operation was scheduled for completion; otherwise false.</returns>
        private static bool TryCompleteReturnIfUnregistered()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.ReturnResolveStarted || IsPackageRegistered(st.PackageId))
                return false;

            st.Stage = UpmStage.ReturnResolved;
            UpmBuildStateStorage.Save(st);
            EditorApplication.delayCall += CompleteReturnWhenEditorIdle;
            return true;
        }

        /// <summary>
        /// Stops listening for Package Manager registration events and clears the pending timeout.
        /// </summary>
        private static void StopWaitingForPackageRegistration()
        {
            Events.registeredPackages -= OnPackagesRegistered;
            EditorApplication.update -= CheckPackageRegistrationTimeout;
            s_waitingForPackageRegistration = false;
            s_packageRegistrationDeadline = 0;
        }

        /// <summary>
        /// Ends the current Package Manager wait when no registration event arrives before the configured deadline.
        /// </summary>
        private static void CheckPackageRegistrationTimeout()
        {
            if (EditorApplication.timeSinceStartup < s_packageRegistrationDeadline)
                return;

            if (TryCompleteReturnIfUnregistered())
            {
                StopWaitingForPackageRegistration();
                return;
            }

            StopWaitingForPackageRegistration();
            Debug.LogError($"[{nameof(UpmBuildFlow)}] Package Manager resolve timed out. The staged operation remains pending and will resume after the next editor reload.");
        }

        /// <summary>
        /// Restores project-only state after Package Manager no longer registers the returned package.
        /// </summary>
        private static void CompleteReturnWhenEditorIdle()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += CompleteReturnWhenEditorIdle;
                return;
            }

            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.ReturnResolved || IsPackageRegistered(st.PackageId))
                return;

            UpmSamplesWorkflow.RestoreIfNeeded(st);
            DefineSymbolsManager.RemoveDefineSymbol(UpmConstants.UpmDefine);
            UpmBuildStateStorage.Clear();
            Debug.Log($"[{nameof(UpmBuildFlow)}] Returned package folder to: {st.OriginalRootAbs}");
        }

        /// <summary>
        /// Checks whether Package Manager currently registers the specified package id.
        /// </summary>
        /// <param name="packageId">Package id to locate.</param>
        /// <returns>True when the package id is registered; otherwise false.</returns>
        private static bool IsPackageRegistered(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return false;

            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                if (string.Equals(package.name, packageId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        #endregion

        #region Package Metadata

        /// <summary>
        /// Resolves the package id that the build flow must use before moving the source folder.
        /// </summary>
        /// <param name="cfg">Current publisher settings.</param>
        /// <param name="originalRootAbs">Absolute source folder path.</param>
        /// <returns>Configured or discovered package id.</returns>
        /// <exception cref="InvalidOperationException">Thrown when package metadata has no valid package id.</exception>
        private static string ResolveBuildPackageId(AssetPublisherToolsSettings cfg, string originalRootAbs)
        {
            var packageId = string.IsNullOrWhiteSpace(cfg.PackageId)
                ? UpmPackageJsonStaging.GetEffectivePackageId(cfg, originalRootAbs)
                : cfg.PackageId;

            if (string.IsNullOrWhiteSpace(packageId))
                throw new InvalidOperationException("Could not resolve package id before staging the source folder.");

            return packageId;
        }

        /// <summary>
        /// Synchronizes configured package metadata into the staged package manifest.
        /// </summary>
        /// <param name="cfg">Current publisher settings.</param>
        /// <param name="packageJsonAbs">Absolute staged package.json path.</param>
        private static void SyncPackageJsonFromSettings(AssetPublisherToolsSettings cfg, string packageJsonAbs)
        {
            if (cfg == null)
                return;

            if (!string.IsNullOrWhiteSpace(cfg.PackageVersion))
                PackageJsonUtility.SetPackageVersion(packageJsonAbs, cfg.PackageVersion);

            if (!string.IsNullOrWhiteSpace(cfg.PackageId))
                PackageJsonUtility.SetPackageName(packageJsonAbs, cfg.PackageId);

            if (!string.IsNullOrWhiteSpace(cfg.PackageDisplayName))
                PackageJsonUtility.SetPackageDisplayName(packageJsonAbs, cfg.PackageDisplayName);

            if (!string.IsNullOrWhiteSpace(cfg.PackageDescription))
                PackageJsonUtility.SetPackageDescription(packageJsonAbs, cfg.PackageDescription);
        }

        #endregion
    }
}
