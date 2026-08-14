using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SCG.UPPM
{
    /// <summary>
    /// Coordinates durable publication of Samples visibility completion notifications.
    /// Requests are serialized in FIFO order so every successful call receives its own completion.
    /// </summary>
    internal static class SamplesVisibilityNotificationCoordinator
    {
        #region State

        private static readonly string s_domainId = Guid.NewGuid().ToString("N");

        #endregion

        #region Requests

        /// <summary>
        /// Starts a visibility request or persists it behind the active request.
        /// </summary>
        /// <param name="visibility">Requested visibility.</param>
        /// <param name="rootPath">Absolute package root path.</param>
        /// <param name="requiresDomainReload">Whether publication requires a subsequent AppDomain.</param>
        /// <param name="applyChanges">Visibility and define mutations to run when the request starts.</param>
        /// <returns><see langword="true"/> when the request starts immediately; otherwise <see langword="false"/> when it is queued.</returns>
        internal static bool Enqueue(
            SamplesVisibility visibility,
            string rootPath,
            bool requiresDomainReload,
            Action applyChanges)
        {
            var notification = new CompletionNotification
            {
                Kind = visibility == SamplesVisibility.Visible
                    ? CompletionNotificationKind.SamplesVisible
                    : CompletionNotificationKind.SamplesHidden,
                RootPath = rootPath,
                OriginatingDomainId = s_domainId,
                RequiresDomainReload = requiresDomainReload
            };
            var state = CompletionNotificationStorage.LoadOrCreate();
            var started = TryEnqueueCore(
                state,
                notification,
                CompletionNotificationStorage.SaveOrClear,
                applyChanges,
                CompletionNotificationStorage.Clear);

            if (started && !requiresDomainReload)
                SchedulePendingPublication();

            return started;
        }

        /// <summary>
        /// Adds a request to persisted state and starts it only when no earlier request is active.
        /// </summary>
        /// <param name="state">Mutable notification state.</param>
        /// <param name="notification">Request to add.</param>
        /// <param name="saveState">Persistence action.</param>
        /// <param name="applyChanges">Changes to apply when the request starts.</param>
        /// <param name="clearState">Persistence cleanup used when initial application fails.</param>
        /// <returns><see langword="true"/> when the request starts immediately; otherwise <see langword="false"/>.</returns>
        internal static bool TryEnqueueCore(
            CompletionNotificationState state,
            CompletionNotification notification,
            Action<CompletionNotificationState> saveState,
            Action applyChanges,
            Action clearState)
        {
            state.PendingVisibility ??= new List<SamplesVisibility>();
            if (state.Notifications.Exists(IsSamplesNotification))
            {
                state.PendingVisibility.Add(GetVisibility(notification.Kind));
                saveState(state);
                return false;
            }

            state.Notifications.Add(notification);
            saveState(state);
            try
            {
                applyChanges?.Invoke();
            }
            catch
            {
                state.Notifications.Remove(notification);
                if (state.Notifications.Count == 0 && state.PendingVisibility.Count == 0) clearState();
                else saveState(state);
                throw;
            }

            return true;
        }

        #endregion

        #region Publication

        /// <summary>
        /// Schedules publication after InitializeOnLoad subscribers have registered.
        /// </summary>
        internal static void SchedulePendingPublication()
        {
            EditorApplication.delayCall -= TryPublishPending;
            EditorApplication.delayCall += TryPublishPending;
        }

        /// <summary>
        /// Publishes a pending notification when the editor is idle or reschedules the check while Unity is busy.
        /// </summary>
        internal static void TryPublishPending()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                SchedulePendingPublication();
                return;
            }

            var state = CompletionNotificationStorage.LoadOrCreate();
            var notification = state.Notifications.Find(IsSamplesNotification);
            if (notification == null)
            {
                StartNextPending(state);
                return;
            }

            _ = TryPublishCore(
                notification,
                s_domainId,
                () => CompleteActive(state, notification),
                null);
        }

        private static void CompleteActive(CompletionNotificationState state, CompletionNotification notification)
        {
            state.Notifications.Remove(notification);
            if (state.PendingVisibility == null || state.PendingVisibility.Count == 0)
            {
                CompletionNotificationStorage.SaveOrClear(state);
                return;
            }

            CompletionNotificationStorage.SaveOrClear(state);
            StartNextPending(state);
        }

        private static void StartNextPending(CompletionNotificationState state)
        {
            var started = TryStartNextPendingCore(
                state,
                SamplesRenamer.ApplyQueuedVisibility,
                CompletionNotificationStorage.LoadOrCreate,
                CompletionNotificationStorage.SaveOrClear,
                SchedulePendingRetry,
                Debug.LogException);
            if (started)
                EditorApplication.projectChanged -= RetryPending;
        }

        private static void SchedulePendingRetry()
        {
            EditorApplication.projectChanged -= RetryPending;
            EditorApplication.projectChanged += RetryPending;
        }

        private static void RetryPending()
        {
            EditorApplication.projectChanged -= RetryPending;
            SchedulePendingPublication();
        }

        /// <summary>
        /// Hands the first queued request to <see cref="SamplesRenamer"/> without removing it until a new active notification is persisted.
        /// </summary>
        /// <param name="state">State containing the pending FIFO.</param>
        /// <param name="applyVisibility">Action that attempts to start the pending visibility request.</param>
        /// <param name="loadState">Persistence read used to verify the handoff.</param>
        /// <param name="saveState">Persistence write used to commit the dequeue.</param>
        /// <param name="scheduleRetry">Action that schedules another attempt.</param>
        /// <param name="logException">Exception logger.</param>
        /// <returns><see langword="true"/> when a new active request is persisted and the FIFO head is committed; otherwise <see langword="false"/>.</returns>
        internal static bool TryStartNextPendingCore(
            CompletionNotificationState state,
            Action<SamplesVisibility> applyVisibility,
            Func<CompletionNotificationState> loadState,
            Action<CompletionNotificationState> saveState,
            Action scheduleRetry,
            Action<Exception> logException)
        {
            if (state?.PendingVisibility == null || state.PendingVisibility.Count == 0)
                return false;

            try
            {
                applyVisibility(state.PendingVisibility[0]);
            }
            catch (Exception exception)
            {
                state.PendingVisibility.RemoveAt(0);
                saveState(state);
                logException(exception);
                return false;
            }

            var resumedState = loadState();
            if (resumedState == null || !resumedState.Notifications.Exists(IsSamplesNotification))
            {
                scheduleRetry();
                return false;
            }

            resumedState.PendingVisibility.RemoveAt(0);
            saveState(resumedState);
            return true;
        }

        /// <summary>
        /// Publishes a ready notification and clears its marker after every subscriber is processed.
        /// </summary>
        /// <param name="notification">Persisted notification.</param>
        /// <param name="currentDomainId">Current AppDomain identifier.</param>
        /// <param name="clearNotification">Marker cleanup action.</param>
        /// <param name="logException">Optional subscriber exception logger used by tests.</param>
        /// <returns><see langword="true"/> when the notification is published and cleared; otherwise <see langword="false"/>.</returns>
        internal static bool TryPublishCore(
            CompletionNotification notification,
            string currentDomainId,
            Action clearNotification,
            Action<Exception> logException)
        {
            if (!CanPublish(notification, currentDomainId))
                return false;

            SamplesRenamer.NotifyVisibilityChangeCompleted(
                GetVisibility(notification.Kind),
                notification.RootPath,
                logException);
            clearNotification?.Invoke();
            return true;
        }

        /// <summary>
        /// Determines whether a notification is valid and belongs to a publication-ready AppDomain.
        /// </summary>
        /// <param name="notification">Persisted notification.</param>
        /// <param name="currentDomainId">Current AppDomain identifier.</param>
        /// <returns><see langword="true"/> when publication is allowed; otherwise <see langword="false"/>.</returns>
        internal static bool CanPublish(CompletionNotification notification, string currentDomainId) =>
            notification != null &&
            IsSamplesNotification(notification) &&
            !string.IsNullOrWhiteSpace(notification.RootPath) &&
            (!notification.RequiresDomainReload ||
             !string.Equals(notification.OriginatingDomainId, currentDomainId, StringComparison.Ordinal));

        private static bool IsSamplesNotification(CompletionNotification notification) =>
            notification is { Kind: CompletionNotificationKind.SamplesVisible or CompletionNotificationKind.SamplesHidden };

        private static SamplesVisibility GetVisibility(CompletionNotificationKind kind) =>
            kind == CompletionNotificationKind.SamplesVisible ? SamplesVisibility.Visible : SamplesVisibility.Hidden;

        #endregion
    }
}
