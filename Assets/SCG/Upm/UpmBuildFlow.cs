using SCG.UPPM.Helpers;
using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace SCG.UPPM.Upm
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
        /// Requests conversion of the configured project folder into an embedded package.
        /// The request waits for an idle editor, persists before every filesystem move, and enables package compilation after Package Manager registration.
        /// </summary>
        public static void Build()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (IsBuildPending(st.Stage))
                return;

            st.Stage = UpmStage.BuildRequested;
            UpmBuildStateStorage.Save(st);
            ScheduleWhenEditorIdle(BeginBuild);
        }

        /// <summary>
        /// Initializes paths and starts a requested build after the editor becomes idle.
        /// </summary>
        private static void BeginBuild()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.BuildRequested)
                return;

            ExecuteRequestedInitialization(() => InitializeBuild(st));
        }

        /// <summary>
        /// Validates build inputs and persists the first resumable stage before scheduling filesystem work.
        /// </summary>
        /// <param name="st">Persisted requested build state.</param>
        private static void InitializeBuild(UpmBuildState st)
        {
            var cfg = UppmSettings.Instance;
            if (!cfg.TrySyncImmediately())
            {
                ScheduleWhenEditorIdle(BeginBuild);
                return;
            }

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
        /// Requests conversion of the embedded package back into a project folder.
        /// The request waits for an idle editor and is idempotent while return work is pending.
        /// The flow moves the folder out of Packages before resolving Package Manager.
        /// On completion, the UPM define symbol is removed to recompile into project mode.
        /// </summary>
        public static void Return()
        {
#if !UPM_PACKAGE
            Debug.LogError($"[{nameof(UpmBuildFlow)}] Return requires the {UpmConstants.UpmDefine} define.");
#else
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (IsReturnPending(st.Stage))
                return;

            st.Stage = UpmStage.ReturnRequested;
            UpmBuildStateStorage.Save(st);
            ScheduleWhenEditorIdle(BeginReturn);
#endif
        }

#if UPM_PACKAGE
        /// <summary>
        /// Initializes and starts a requested return after the editor becomes idle.
        /// </summary>
        private static void BeginReturn()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.ReturnRequested)
                return;

            ExecuteRequestedInitialization(() => InitializeReturn(st));
        }

        /// <summary>
        /// Validates return inputs and persists the first resumable stage before scheduling filesystem work.
        /// </summary>
        /// <param name="st">Persisted requested return state.</param>
        private static void InitializeReturn(UpmBuildState st)
        {
            var cfg = UppmSettings.Instance;
            if (!cfg.TrySyncImmediately())
            {
                ScheduleWhenEditorIdle(BeginReturn);
                return;
            }

            if (string.IsNullOrWhiteSpace(st.PackageId))
                st.PackageId = cfg.PackageId;

            if (string.IsNullOrWhiteSpace(st.PackageId))
                throw new InvalidOperationException("Could not resolve package id for return.");

            st.Stage = UpmStage.ReturnStarted;
            UpmBuildStateStorage.Save(st);
            EditorApplication.delayCall += MovePackagesBackToProject;
        }
#endif

        /// <summary>
        /// Resumes the persisted build or return operation after a domain reload.
        /// The method schedules the next action and lets that action reconcile any filesystem move completed before the previous process stopped.
        /// </summary>
        public static void TryResumePendingWork()
        {
            switch (UpmBuildStateStorage.LoadOrCreate().Stage)
            {
                case UpmStage.BuildRequested:
                    ScheduleWhenEditorIdle(BeginBuild);
                    return;
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
                case UpmStage.ReturnRequested:
#if UPM_PACKAGE
                    ScheduleWhenEditorIdle(BeginReturn);
#endif
                    return;
                case UpmStage.ReturnStarted:
                    EditorApplication.delayCall += MovePackagesBackToProject;
                    return;
                case UpmStage.ReturnMovedToProject:
                case UpmStage.ReturnResolveStarted:
                    EditorApplication.delayCall += ResolveAfterReturnMove;
                    return;
                case UpmStage.ReturnResolved:
                    ScheduleWhenEditorIdle(ImportReturnedFolder);
                    return;
                case UpmStage.ReturnImported:
                    ScheduleWhenEditorIdle(CompleteReturnWhenEditorIdle);
                    return;
                case UpmStage.BuildResolved:
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
        private static void MoveOriginalToTemp() => ExecuteResumableStep(MoveOriginalToTempCore);

        private static void MoveOriginalToTempCore()
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
        private static void PrepareTempBuild() => ExecuteResumableStep(PrepareTempBuildCore);

        private static void PrepareTempBuildCore()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.BuildMovedToTemp)
                return;

            if (!Directory.Exists(st.TempRootAbs))
                throw new InvalidOperationException($"Staging folder is missing: {st.TempRootAbs}");

            var cfg = UppmSettings.Instance;
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
        private static void MoveTempToPackagesAndResolve() => ExecuteResumableStep(MoveTempToPackagesAndResolveCore);

        private static void MoveTempToPackagesAndResolveCore()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.BuildReadyToMove)
                return;

            UpmFileOperations.EnsureFolderMovedWithMeta(st.TempRootAbs, st.PackagesRootAbs);
            st.Stage = UpmStage.BuildMovedToPackages;
            UpmBuildStateStorage.Save(st);
            ResolveAfterMove();
        }

        /// <summary>
        /// Resolves Package Manager after moving the package into Packages.
        /// </summary>
        private static void ResolveAfterMove() => ExecuteResumableStep(ResolveAndWaitForPackageRegistration);

        #endregion

        #region Return Steps

        /// <summary>
        /// Moves the embedded package back into its project location and starts Package Manager resolution.
        /// When the folder is already under Assets, the method completes a pending meta move before resolving it.
        /// </summary>
        private static void MovePackagesBackToProject() => ExecuteResumableStep(MovePackagesBackToProjectCore);

        private static void MovePackagesBackToProjectCore()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.ReturnStarted)
                return;

            var cfg = UppmSettings.Instance;
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
            ResolveAfterReturnMove();
        }

        /// <summary>
        /// Resolves Package Manager after moving the package out of Packages.
        /// </summary>
        private static void ResolveAfterReturnMove() => ExecuteResumableStep(ResolveAfterReturnMoveCore);

        private static void ResolveAfterReturnMoveCore()
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
            if (TryCompleteBuildIfRegistered() || TryCompleteReturnIfUnregistered())
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
            ScheduleWhenEditorIdle(ImportReturnedFolder);
        }

        /// <summary>
        /// Completes the persisted build operation when Package Manager already registers the embedded package.
        /// </summary>
        /// <returns>True when the build operation was completed; otherwise false.</returns>
        private static bool TryCompleteBuildIfRegistered()
        {
            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.BuildMovedToPackages || !IsPackageRegistered(st.PackageId)) return false;
            st.Stage = UpmStage.BuildResolved;
            UpmBuildStateStorage.Save(st);
            var requiresDomainReload = !DefineSymbolsManager.HasDefineSymbol(UpmConstants.UpmDefine);
            UpmPackageBuilder.QueueCompletion(
                UpmPackageAction.Build,
                st.PackagesRootAbs,
                requiresDomainReload,
                () => DefineSymbolsManager.AddDefineSymbol(UpmConstants.UpmDefine));
            return true;
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
            ScheduleWhenEditorIdle(ImportReturnedFolder);
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
            ClearFailedWorkflowState();
            Debug.LogError($"[{nameof(UpmBuildFlow)}] Package Manager resolve timed out. Persisted workflow markers were cleared to prevent an editor reload loop.");
        }

        /// <summary>
        /// Imports the returned project folder after Package Manager releases its former package path.
        /// </summary>
        private static void ImportReturnedFolder() => ExecuteResumableStep(ImportReturnedFolderCore);

        private static void ImportReturnedFolderCore()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleWhenEditorIdle(ImportReturnedFolder);
                return;
            }

            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.ReturnResolved || IsPackageRegistered(st.PackageId))
                return;

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            st.Stage = UpmStage.ReturnImported;
            UpmBuildStateStorage.Save(st);
            ScheduleWhenEditorIdle(CompleteReturnWhenEditorIdle);
        }

        /// <summary>
        /// Restores project-only state after the returned folder finishes importing.
        /// </summary>
        private static void CompleteReturnWhenEditorIdle() => ExecuteResumableStep(CompleteReturnWhenEditorIdleCore);

        private static void CompleteReturnWhenEditorIdleCore()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleWhenEditorIdle(CompleteReturnWhenEditorIdle);
                return;
            }

            var st = UpmBuildStateStorage.LoadOrCreate();
            if (st.Stage != UpmStage.ReturnImported || IsPackageRegistered(st.PackageId))
                return;

            UpmSamplesWorkflow.RestoreIfNeeded(st);
            var requiresDomainReload = DefineSymbolsManager.HasDefineSymbol(UpmConstants.UpmDefine);
            UpmPackageBuilder.QueueCompletion(
                UpmPackageAction.Return,
                st.OriginalRootAbs,
                requiresDomainReload,
                () => DefineSymbolsManager.RemoveDefineSymbol(UpmConstants.UpmDefine));
            UpmBuildStateStorage.Clear();
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

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                if (string.Equals(package.name, packageId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        #endregion

        #region Scheduling

        /// <summary>
        /// Runs initialization before any filesystem move and clears its requested state when initialization fails.
        /// </summary>
        /// <param name="initialize">Initialization action to execute.</param>
        /// <param name="clearState">Optional state reset action used by tests.</param>
        internal static void ExecuteRequestedInitialization(Action initialize, Action clearState = null)
        {
            if (initialize == null)
                throw new ArgumentNullException(nameof(initialize));

            try
            {
                initialize();
            }
            catch
            {
                (clearState ?? ClearFailedWorkflowState)();
                throw;
            }
        }

        /// <summary>
        /// Executes a resumable step and clears every persisted marker if the step fails.
        /// Files already moved remain available for manual recovery, but editor reload cannot restart the failed step indefinitely.
        /// </summary>
        /// <param name="step">Workflow step to execute.</param>
        /// <param name="clearState">Optional cleanup action used by tests.</param>
        internal static void ExecuteResumableStep(Action step, Action clearState = null)
        {
            if (step == null)
                throw new ArgumentNullException(nameof(step));

            try
            {
                step();
            }
            catch
            {
                StopWaitingForPackageRegistration();
                (clearState ?? ClearFailedWorkflowState)();
                throw;
            }
        }

        private static void ClearFailedWorkflowState()
        {
            UpmBuildStateStorage.Clear();
            CompletionNotificationStorage.Clear();
        }

        /// <summary>
        /// Checks whether a persisted stage represents build work that can resume.
        /// </summary>
        /// <param name="stage">Persisted workflow stage.</param>
        /// <returns>True while a build request is active; otherwise false.</returns>
        internal static bool IsBuildPending(UpmStage stage) =>
            stage is >= UpmStage.BuildRequested and <= UpmStage.BuildMovedToPackages;

        /// <summary>
        /// Checks whether a persisted stage represents return work that can resume.
        /// </summary>
        /// <param name="stage">Persisted workflow stage.</param>
        /// <returns>True while a return request is active; otherwise false.</returns>
        internal static bool IsReturnPending(UpmStage stage) =>
            stage is >= UpmStage.ReturnRequested and <= UpmStage.ReturnImported;

        /// <summary>
        /// Debounces an editor callback that must run after compilation and asset updates finish.
        /// </summary>
        /// <param name="callback">Callback to schedule.</param>
        private static void ScheduleWhenEditorIdle(EditorApplication.CallbackFunction callback)
        {
            EditorApplication.delayCall -= callback;
            EditorApplication.delayCall += callback;
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
        private static string ResolveBuildPackageId(UppmSettings cfg, string originalRootAbs)
        {
            var packageId = string.IsNullOrWhiteSpace(cfg.PackageId)
                ? UpmPackageJsonStaging.GetEffectivePackageId(cfg, originalRootAbs)
                : cfg.PackageId;

            return !string.IsNullOrWhiteSpace(packageId) ? packageId : throw new InvalidOperationException("Could not resolve package id before staging the source folder.");
        }

        /// <summary>
        /// Synchronizes configured package metadata into the staged package manifest.
        /// </summary>
        /// <param name="cfg">Current publisher settings.</param>
        /// <param name="packageJsonAbs">Absolute staged package.json path.</param>
        private static void SyncPackageJsonFromSettings(UppmSettings cfg, string packageJsonAbs)
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
