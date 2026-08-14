namespace SCG.UPPM.Upm
{
    /// <summary>
    /// Defines constants used by the UPM build/return workflows.
    /// Values are centralized to avoid duplicated string literals across editor utilities.
    /// The constants are kept internal because they describe implementation details of the tool.
    /// </summary>
    internal static class UpmConstants
    {
        /// <summary>Default menu priority for build/return entries.</summary>
        public const int MenuPriority = 5000;

        /// <summary>Compilation define that marks the package mode.</summary>
        public const string UpmDefine = "UPM_PACKAGE";

        /// <summary>Unity project folder name used for staging.</summary>
        public const string TempFolderName = "Temp";

        /// <summary>Unity project folder name used for embedded packages.</summary>
        public const string PackagesFolderName = "Packages";

        /// <summary>Unity project folder name used for regular assets.</summary>
        public const string AssetsFolderName = "Assets";

        /// <summary>File name of the Unity Package Manager manifest.</summary>
        public const string ManifestFileName = "manifest.json";

        /// <summary>File name of the UPM package description file.</summary>
        public const string PackageJsonFileName = "package.json";

        /// <summary>Optional alternative package json file name used by some workflows.</summary>
        public const string FreePackageJsonFileName = "package.free.json";

        /// <summary>State file used to resume staged operations across editor reloads.</summary>
        public const string StateFileName = nameof(SCG) + "_UpmBuildState.json";

        /// <summary>Notification file used to publish completion after compilation and AppDomain reload.</summary>
        public const string CompletionNotificationFileName = nameof(SCG) + "_CompletionNotifications.json";
    }
}
