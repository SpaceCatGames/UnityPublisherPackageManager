using System;
using UnityEditor;
using UnityEngine;

namespace SCG.UPPM.Upm
{
    /// <summary>
    /// Coordinates Samples~ and Documentation~ folder visibility for the staged UPM workflow.
    /// Some publishing flows prefer these folders hidden while packaging and later restored for project editing.
    /// The helper records whether it toggled visibility so it can restore state reliably.
    /// </summary>
    internal static class UpmSamplesWorkflow
    {
        /// <summary>
        /// Ensures Samples~ are in a consistent state before running the provided action.
        /// When the folders are currently visible, the method toggles them back to tilde-suffixed names.
        /// The toggle flag is persisted so the return flow can restore visibility when appropriate.
        /// </summary>
        /// <param name="action">Operation entry point to execute after preparation.</param>
        public static void PrepareSamplesAndSchedule(Action action)
        {
            try
            {
                var st = UpmBuildStateStorage.LoadOrCreate();

                var toggled = false;
                if (!SamplesRenamer.AreFoldersHiddenWithTilda())
                {
                    SamplesRenamer.EnsureHidden();
                    toggled = true;
                }

                st.SamplesWereToggledByTool = toggled;
                UpmBuildStateStorage.Save(st);

                EditorApplication.delayCall += () => action?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{nameof(UpmSamplesWorkflow)}] Samples~ preparation failed: {ex.Message}");
                EditorApplication.delayCall += () => action?.Invoke();
            }
        }

        /// <summary>
        /// Restores Samples~ visibility when the tool toggled it during build.
        /// The method is guarded to avoid double toggles and persists the updated flag.
        /// Failures are logged as warnings without aborting the overall flow.
        /// </summary>
        /// <param name="st">State object containing the toggle flag.</param>
        public static void RestoreIfNeeded(UpmBuildState st)
        {
            if (st == null)
                return;

            try
            {
                if (st.SamplesWereToggledByTool)
                    SamplesRenamer.EnsureVisible();

                st.SamplesWereToggledByTool = false;
                UpmBuildStateStorage.Save(st);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{nameof(UpmSamplesWorkflow)}] Samples~ restore failed: {ex.Message}");
            }
        }
    }
}
