using System;

namespace SCG.UnityAssetPublisherTools.Upm
{
    /// <summary>
    /// Stores staged operation data for the UPM build/return workflow.
    /// The state is serialized as JSON into the Temp folder so the workflow can continue after reload.
    /// Only fields that are required for deterministic file operations are persisted.
    /// </summary>
    [Serializable]
    internal sealed class UpmBuildState
    {
        /// <summary>Folder name used under Assets, Temp, and Packages.</summary>
        public string AssetRootFolder;

        /// <summary>Original project folder absolute path.</summary>
        public string OriginalRootAbs;

        /// <summary>Temp staging folder absolute path.</summary>
        public string TempRootAbs;

        /// <summary>Packages destination folder absolute path.</summary>
        public string PackagesRootAbs;

        /// <summary>UPM package id read from package.json.</summary>
        public string PackageId;

        /// <summary>True when Samples~ were toggled by this tool.</summary>
        public bool SamplesWereToggledByTool;

        /// <summary>Current staged operation step.</summary>
        public UpmStage Stage;
    }
}
