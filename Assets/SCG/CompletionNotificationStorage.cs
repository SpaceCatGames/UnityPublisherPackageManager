using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SCG.UPPM.Upm;
using UnityEngine;

namespace SCG.UPPM
{
    /// <summary>
    /// Persists every durable completion notification in one state file and deletes invalid state immediately.
    /// </summary>
    internal static class CompletionNotificationStorage
    {
        #region Public API

        internal static CompletionNotificationState LoadOrCreate() => LoadOrCreate(GetFilePath(), Debug.LogException);

        internal static CompletionNotificationState LoadOrCreate(string path, Action<Exception> logException)
        {
            if (!File.Exists(path))
                return new CompletionNotificationState();

            try
            {
                var state = JsonUtility.FromJson<CompletionNotificationState>(File.ReadAllText(path, Encoding.UTF8))
                    ?? throw new InvalidDataException("Completion notification state is empty.");
                state.Notifications ??= new List<CompletionNotification>();
                state.PendingVisibility ??= new List<SamplesVisibility>();
                var notificationCount = state.Notifications.Count;
                var pendingCount = state.PendingVisibility.Count;
                state.Notifications.RemoveAll(notification =>
                    notification == null ||
                    !Enum.IsDefined(typeof(CompletionNotificationKind), notification.Kind) ||
                    string.IsNullOrWhiteSpace(notification.RootPath) ||
                    string.IsNullOrWhiteSpace(notification.OriginatingDomainId));
                state.PendingVisibility.RemoveAll(visibility => !Enum.IsDefined(typeof(SamplesVisibility), visibility));
                if (notificationCount != state.Notifications.Count || pendingCount != state.PendingVisibility.Count)
                    SaveOrClear(state, path);

                return state;
            }
            catch (Exception exception)
            {
                if (string.Equals(path, GetFilePath(), StringComparison.OrdinalIgnoreCase)) Clear();
                else if (File.Exists(path)) File.Delete(path);
                logException?.Invoke(exception);
                return new CompletionNotificationState();
            }
        }

        internal static void SaveOrClear(CompletionNotificationState state) => SaveOrClear(state, GetFilePath());

        internal static void SaveOrClear(CompletionNotificationState state, string path)
        {
            if (state == null ||
                ((state.Notifications == null || state.Notifications.Count == 0) &&
                 (state.PendingVisibility == null || state.PendingVisibility.Count == 0)))
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Notification path has no parent directory."));
            File.WriteAllText(path, JsonUtility.ToJson(state), Encoding.UTF8);
        }

        internal static void Clear()
        {
            var path = GetFilePath();
            if (File.Exists(path))
                File.Delete(path);
        }

        #endregion

        #region Paths

        private static string GetFilePath() =>
            Path.Combine(UpmPathUtility.ProjectRootAbs, UpmConstants.TempFolderName, UpmConstants.CompletionNotificationFileName);

        #endregion
    }
}
