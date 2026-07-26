using SCG.UnityAssetPublisherTools.Upm;
using UnityEditor;

namespace SCG.UnityAssetPublisherTools
{
    /// <summary>
    /// Automates switching a project folder into an embedded UPM package and back.
    /// The workflow stages files in the Temp folder first and only then moves them into Packages.
    /// Package Manager discovers the embedded package directly under Packages without a manifest self-reference.
    /// </summary>
    public static class UpmPackageBuilder
    {
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

        #region Initialize

        /// <summary>
        /// Synchronizes the scripting define with the actual folder placement on editor load.
        /// The method also attempts to resume a pending staged operation after a domain reload.
        /// This keeps the menu label and compilation mode consistent across editor restarts.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            EditorApplication.delayCall += () =>
            {
                UpmBuildFlow.TryResumePendingWork();
                UpmDefineSynchronizer.SyncDefineWithPackagesFolder();
            };
        }

        #endregion
    }
}
