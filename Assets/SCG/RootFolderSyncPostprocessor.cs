using UnityEditor;

namespace SCG.UPPM
{
    /// <summary>
    /// Observes asset pipeline changes that are relevant to hidden folder root synchronization.
    /// The postprocessor does not perform filesystem work directly and only schedules a delayed pass.
    /// This keeps synchronization out of import callbacks and avoids unstable AssetDatabase timing.
    /// </summary>
    internal sealed class RootFolderSyncPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!RootFolderSyncCoordinator.ShouldReactToAssetChanges(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths))
                return;

            RootFolderSyncCoordinator.ScheduleSync();
        }
    }
}
