using System;
using System.IO;
using SCG.UnityAssetPublisherTools.Helpers;

namespace SCG.UnityAssetPublisherTools.Upm
{
    /// <summary>
    /// Keeps the <see cref="UpmConstants.UpmDefine"/> scripting define symbol synchronized with the current project state.
    /// The define is enabled when the configured embedded package exists under Packages and disabled otherwise.
    /// This helps avoid a mismatched compilation mode when the user manually moves or deletes folders.
    /// </summary>
    internal static class UpmDefineSynchronizer
    {
        /// <summary>
        /// Applies define symbol changes based on whether the configured package.json exists under Packages.
        /// The method is safe to call repeatedly and updates only when a change is required.
        /// It uses best-effort behavior for different build targets via <see cref="DefineSymbolsManager"/>.
        /// </summary>
        public static void SyncDefineWithPackagesFolder()
        {
            if (IsReturnPending())
                return;

            var cfg = AssetPublisherToolsSettings.Instance;
            if (ContainsPackageJson(cfg.PackageId))
                DefineSymbolsManager.AddDefineSymbol(UpmConstants.UpmDefine);
            else
                DefineSymbolsManager.RemoveDefineSymbol(UpmConstants.UpmDefine);
        }

        /// <summary>
        /// Checks whether an embedded package folder contains the configured package manifest.
        /// </summary>
        /// <param name="packageId">Expected package id.</param>
        /// <returns>True when the package manifest exists and declares the expected package id.</returns>
        private static bool ContainsPackageJson(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return false;

            var packageJsonAbs = Path.Combine(
                UpmPathUtility.ProjectRootAbs,
                UpmConstants.PackagesFolderName,
                UpmPathUtility.GetSafeFolderName(packageId),
                UpmConstants.PackageJsonFileName);
            return File.Exists(packageJsonAbs) &&
                   string.Equals(PackageJsonUtility.GetPackageName(packageJsonAbs), packageId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Checks whether the persisted state must retain the package compilation define until return completion.
        /// </summary>
        /// <returns>True when a return operation is pending.</returns>
        private static bool IsReturnPending()
        {
            var stage = UpmBuildStateStorage.LoadOrCreate().Stage;
            return stage is UpmStage.ReturnStarted or UpmStage.ReturnMovedToProject or UpmStage.ReturnResolveStarted or UpmStage.ReturnResolved;
        }
    }
}
