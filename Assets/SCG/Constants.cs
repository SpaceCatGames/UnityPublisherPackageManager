namespace SCG.UPPM
{
    /// <summary>
    /// Defines constants shared by editor utilities and settings synchronization code.
    /// Centralizes common Unity project paths, file names, and menu roots to avoid duplicated literals.
    /// Also includes names and defines used by Samples/Documentation rename workflows.
    /// </summary>
    public static class Constants
    {
        /// <summary>Root menu path prefix for all SCG editor entries.</summary>
        public const string MenuRoot = nameof(SCG) + "/";

        /// <summary>Menu item used to toggle root mirror synchronization.</summary>
        public const string RootFolderSyncMenuPath = MenuRoot + "Enable Samples and Documentation root sync";

        /// <summary>Unity project folder name used for regular assets.</summary>
        public const string AssetsRoot = "Assets/";

        /// <summary>Unity project folder name used for UPM packages.</summary>
        public const string PackagesRoot = "Packages/";

        /// <summary>File name of the UPM package description file.</summary>
        public const string PackageJsonFileName = "package.json";

        /// <summary>Renamed folder name used after Samples are made visible.</summary>
        public const string SamplesRenamed = "Samples";

        /// <summary>Base folder name used by Unity for hidden Samples folders.</summary>
        public const string SamplesBase = "Samples" + "~";

        /// <summary>Renamed folder name used after Documentation is made visible.</summary>
        public const string DocumentationRenamed = "Documentation";

        /// <summary>Base folder name used by Unity for hidden Documentation folders.</summary>
        public const string DocumentationBase = "Documentation" + "~";

        /// <summary>Compilation define that marks Samples as already renamed.</summary>
        public const string SamplesRenamedDefineSymbol = "SAMPLES_RENAMED";

        /// <summary>Root mirror folder name used for editable Samples copies under Assets.</summary>
        public const string RootSamplesSyncFolder = "~Samples~";

        /// <summary>Root mirror folder name used for editable Documentation copies under Assets.</summary>
        public const string RootDocumentationSyncFolder = "~Documentation~";
    }

}
