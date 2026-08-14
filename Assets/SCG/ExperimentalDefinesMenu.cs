using SCG.UPPM.Helpers;
using UnityEditor;

namespace SCG.UPPM
{
    /// <summary>
    /// Provides editor menu entries for optional scripting defines.
    /// The defines are used to unlock experimental tooling without editing Project Settings manually.
    /// Menu items are compiled conditionally to avoid duplicates when the define is already enabled.
    /// </summary>
    public static class ExperimentalDefinesMenu
    {
        private const string ExperimentalDefine = "UNITY_ASTOOLS_EXPERIMENTAL";

#if !UNITY_ASTOOLS_EXPERIMENTAL
        /// <summary>
        /// Enables the experimental define used by Unity Publisher Package Manager.
        /// The define is added for the selected build target and also applied to Android and iOS.
        /// The operation may trigger a script recompile due to define changes.
        /// </summary>
        [MenuItem(Constants.MenuRoot + "Enable UNITY_ASTOOLS_EXPERIMENTAL Define", priority = 10010)]
        public static void EnableExperimentalDefine() => DefineSymbolsManager.AddDefineSymbol(ExperimentalDefine);
#endif
    }
}
