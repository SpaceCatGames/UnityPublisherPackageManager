using System;
using SCG.UPPM.Upm;
using UnityEditor;
using UnityEngine;

namespace SCG.UPPM
{
    /// <summary>
    /// Automates switching a project folder into an embedded UPM package and back.
    /// The workflow stages files in the Temp folder first and only then moves them into Packages.
    /// Package Manager discovers the embedded package directly under Packages without a manifest self-reference.
    /// </summary>
    public static class UpmPackageBuilder
    {
        private static readonly string s_domainId = Guid.NewGuid().ToString("N");

        #region Events

        /// <summary>
        /// Occurs after any UPPM package transition completes and its required compilation and AppDomain reload finish.
        /// The callback receives the completed action and the absolute package root path at its new location.
        /// <para>
        /// Subscribe from <see cref="InitializeOnLoadMethodAttribute"/> to restore the static subscription after an AppDomain reload.
        /// A subscription made immediately before <see cref="BuildOrReturn"/> works only when the operation does not reload the AppDomain.
        /// </para>
        /// <code>
        /// [InitializeOnLoadMethod]
        /// private static void SubscribeToActionCompleted()
        /// {
        ///     UpmPackageBuilder.ActionCompleted -= OnActionCompleted;
        ///     UpmPackageBuilder.ActionCompleted += OnActionCompleted;
        /// }
        /// </code>
        /// </summary>
        public static event Action<UpmPackageAction, string> ActionCompleted;

        /// <summary>
        /// Occurs after a project folder is fully converted into an embedded UPM package and its required compilation and AppDomain reload finish.
        /// The callback receives the absolute embedded package root path.
        /// <para>
        /// Subscribe from <see cref="InitializeOnLoadMethodAttribute"/> to restore the static subscription after an AppDomain reload.
        /// A subscription made immediately before <see cref="BuildOrReturn"/> works only when the operation does not reload the AppDomain.
        /// </para>
        /// <code>
        /// [InitializeOnLoadMethod]
        /// private static void SubscribeToBuildCompleted()
        /// {
        ///     UpmPackageBuilder.BuildCompleted -= OnBuildCompleted;
        ///     UpmPackageBuilder.BuildCompleted += OnBuildCompleted;
        /// }
        /// </code>
        /// </summary>
        public static event Action<string> BuildCompleted;

        /// <summary>
        /// Occurs after an embedded package is fully returned, its persisted UPM state is cleared, and its required compilation and AppDomain reload finish.
        /// The callback receives the absolute path of the returned package root.
        /// <para>
        /// Subscribe from <see cref="InitializeOnLoadMethodAttribute"/> to restore the static subscription after an AppDomain reload.
        /// A subscription made immediately before <see cref="BuildOrReturn"/> works only when the operation does not reload the AppDomain.
        /// </para>
        /// <code>
        /// [InitializeOnLoadMethod]
        /// private static void SubscribeToReturnCompleted()
        /// {
        ///     UpmPackageBuilder.ReturnCompleted -= OnReturnCompleted;
        ///     UpmPackageBuilder.ReturnCompleted += OnReturnCompleted;
        /// }
        /// </code>
        /// </summary>
        public static event Action<string> ReturnCompleted;

        #endregion

        #region Menu Entry

#if !UPM_PACKAGE
        /// <summary>
        /// Starts the build flow that converts the configured project folder into an embedded UPM package.
        /// The operation first stages files in Temp and then moves them into Packages.
        /// The method resolves Package Manager after the package is moved into Packages.
        /// </summary>
        [MenuItem(Constants.MenuRoot + "Build for UPM Package", priority = UpmConstants.MenuPriority)]
        public static void BuildOrReturn() => UpmSamplesWorkflow.PrepareSamplesAndSchedule(UpmBuildFlow.Build);
#else
        /// <summary>
        /// Starts the return flow that converts the embedded UPM package back into a project folder.
        /// The operation restores files from Packages and then resolves Package Manager.
        /// The method also restores Samples~ visibility when it was toggled by this tool.
        /// </summary>
        [MenuItem(Constants.MenuRoot + "Return from UPM Package (to project)", priority = UpmConstants.MenuPriority)]
        public static void BuildOrReturn() => UpmBuildFlow.Return();
#endif

        #endregion

        #region Notifications

        /// <summary>
        /// Persists completion before a define change can reload the AppDomain.
        /// </summary>
        /// <param name="action">Completed package action.</param>
        /// <param name="packageRootPath">Absolute package root path at its completed location.</param>
        /// <param name="requiresDomainReload">True when publication must occur in a subsequent AppDomain.</param>
        /// <param name="applyDefineChange">Define mutation that may start compilation.</param>
        internal static void QueueCompletion(
            UpmPackageAction action,
            string packageRootPath,
            bool requiresDomainReload,
            Action applyDefineChange)
        {
            var notification = new CompletionNotification
            {
                Kind = action == UpmPackageAction.Build
                    ? CompletionNotificationKind.UpmBuild
                    : CompletionNotificationKind.UpmReturn,
                RootPath = packageRootPath,
                OriginatingDomainId = s_domainId,
                RequiresDomainReload = requiresDomainReload
            };
            var state = CompletionNotificationStorage.LoadOrCreate();
            state.Notifications.RemoveAll(IsUpmNotification);
            state.Notifications.Add(notification);
            CompletionNotificationStorage.SaveOrClear(state);

            try
            {
                applyDefineChange?.Invoke();
            }
            catch
            {
                state.Notifications.Remove(notification);
                CompletionNotificationStorage.SaveOrClear(state);
                throw;
            }

            if (!requiresDomainReload)
                SchedulePendingCompletionPublication();
        }

        /// <summary>
        /// Publishes a persisted completion after subscribers register and the editor becomes idle.
        /// </summary>
        internal static void TryPublishPendingCompletion()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                SchedulePendingCompletionPublication();
                return;
            }

            var state = CompletionNotificationStorage.LoadOrCreate();
            var notification = state.Notifications.Find(IsUpmNotification);
            _ = TryPublishPendingCompletionCore(
                notification,
                s_domainId,
                () =>
                {
                    state.Notifications.Remove(notification);
                    CompletionNotificationStorage.SaveOrClear(state);
                },
                null);
        }

        /// <summary>
        /// Publishes a ready notification and clears its marker after all subscribers have been processed.
        /// </summary>
        /// <param name="notification">Persisted notification.</param>
        /// <param name="currentDomainId">Current AppDomain identifier.</param>
        /// <param name="clearNotification">Marker cleanup action.</param>
        /// <param name="logException">Optional subscriber exception logger used by tests.</param>
        /// <returns>True when the notification was published and cleared; otherwise false.</returns>
        internal static bool TryPublishPendingCompletionCore(
            CompletionNotification notification,
            string currentDomainId,
            Action clearNotification,
            Action<Exception> logException)
        {
            if (!CanPublish(notification, currentDomainId))
                return false;

            NotifyCompleted(
                GetUpmAction(notification.Kind),
                notification.RootPath,
                GetSpecificHandlers(GetUpmAction(notification.Kind)),
                logException);
            if (notification.Kind == CompletionNotificationKind.UpmReturn)
                Debug.Log($"[{nameof(UpmBuildFlow)}] Returned package folder to: {notification.RootPath}");
            clearNotification?.Invoke();
            return true;
        }

        /// <summary>
        /// Checks whether a notification belongs to a publication-ready AppDomain.
        /// </summary>
        /// <param name="notification">Persisted notification.</param>
        /// <param name="currentDomainId">Current AppDomain identifier.</param>
        /// <returns>True when publication is allowed; otherwise false.</returns>
        internal static bool CanPublish(CompletionNotification notification, string currentDomainId) =>
            notification != null &&
            IsUpmNotification(notification) &&
            !string.IsNullOrWhiteSpace(notification.RootPath) &&
            (!notification.RequiresDomainReload ||
             !string.Equals(notification.OriginatingDomainId, currentDomainId, StringComparison.Ordinal));

        private static bool IsUpmNotification(CompletionNotification notification) =>
            notification is { Kind: CompletionNotificationKind.UpmBuild or CompletionNotificationKind.UpmReturn };

        private static UpmPackageAction GetUpmAction(CompletionNotificationKind kind) =>
            kind == CompletionNotificationKind.UpmBuild ? UpmPackageAction.Build : UpmPackageAction.Return;

        private static Action<string> GetSpecificHandlers(UpmPackageAction action) =>
            action == UpmPackageAction.Build ? BuildCompleted : ReturnCompleted;

        private static void SchedulePendingCompletionPublication()
        {
            EditorApplication.delayCall -= TryPublishPendingCompletion;
            EditorApplication.delayCall += TryPublishPendingCompletion;
        }

        /// <summary>
        /// Notifies consumers that a build operation completed without allowing subscriber failures to break the UPM workflow.
        /// </summary>
        /// <param name="packageRootPath">Absolute embedded package root path.</param>
        /// <param name="logException">Optional exception logger used by tests.</param>
        internal static void NotifyBuildCompleted(string packageRootPath, Action<Exception> logException = null) =>
            NotifyCompleted(UpmPackageAction.Build, packageRootPath, BuildCompleted, logException);

        /// <summary>
        /// Notifies consumers that a return operation completed without allowing subscriber failures to break the UPM workflow.
        /// </summary>
        /// <param name="returnedRootPath">Absolute path of the returned package root.</param>
        /// <param name="logException">Optional exception logger used by tests.</param>
        internal static void NotifyReturnCompleted(string returnedRootPath, Action<Exception> logException = null) =>
            NotifyCompleted(UpmPackageAction.Return, returnedRootPath, ReturnCompleted, logException);

        private static void NotifyCompleted(
            UpmPackageAction action,
            string packageRootPath,
            Action<string> specificHandlers,
            Action<Exception> logException)
        {
            logException ??= Debug.LogException;
            InvokeHandlers(specificHandlers, packageRootPath, logException);
            InvokeHandlers(ActionCompleted, action, packageRootPath, logException);
        }

        private static void InvokeHandlers(Action<string> handlers, string packageRootPath, Action<Exception> logException)
        {
            if (handlers == null)
                return;

            foreach (var @delegate in handlers.GetInvocationList())
            {
                var handler = (Action<string>)@delegate;
                try
                {
                    handler(packageRootPath);
                }
                catch (Exception exception)
                {
                    logException(exception);
                }
            }
        }

        private static void InvokeHandlers(
            Action<UpmPackageAction, string> handlers,
            UpmPackageAction action,
            string packageRootPath,
            Action<Exception> logException)
        {
            if (handlers == null)
                return;

            foreach (var @delegate in handlers.GetInvocationList())
            {
                var handler = (Action<UpmPackageAction, string>)@delegate;
                try
                {
                    handler(action, packageRootPath);
                }
                catch (Exception exception)
                {
                    logException(exception);
                }
            }
        }

        #endregion

        #region Initialize

        /// <summary>
        /// Synchronizes the scripting define with the actual folder placement on editor load.
        /// The method also resumes pending work, schedules mirror reconciliation, and publishes durable completion notifications.
        /// This restores available persisted state after editor load and keeps completion publication consistent across compilation and AppDomain reloads.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            EditorApplication.delayCall += () =>
            {
                UpmBuildFlow.TryResumePendingWork();
                UpmDefineSynchronizer.SyncDefineWithPackagesFolder();
                RootFolderSyncCoordinator.TryScheduleInitialSync();
                SchedulePendingCompletionPublication();
                SamplesVisibilityNotificationCoordinator.TryPublishPending();
            };
        }

        #endregion
    }
}
