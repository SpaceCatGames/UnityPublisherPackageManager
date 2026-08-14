using System;

namespace SCG.UPPM.Upm
{
    /// <summary>
    /// Represents the last completed step of the staged UPM workflow.
    /// The stage value is persisted to disk so the tooling can resume after a domain reload.
    /// The enum is internal because it is not part of the public editor tooling API.
    /// </summary>
    [Serializable]
    internal enum UpmStage
    {
        /// <summary>The build flow was requested and waits for the editor to become idle.</summary>
        BuildRequested = 1,

        /// <summary>Build state was saved before moving files from the original location into Temp.</summary>
        BuildStarted = 5,

        /// <summary>Files were moved from the original location into Temp.</summary>
        BuildMovedToTemp = 10,

        /// <summary>The staged package is ready to move from Temp into Packages.</summary>
        BuildReadyToMove = 20,

        /// <summary>Files were moved from Temp into Packages.</summary>
        BuildMovedToPackages = 30,

        /// <summary>Package Manager resolve finished after the build flow.</summary>
        BuildResolved = 40,

        /// <summary>The return flow was requested and waits for the editor to become idle.</summary>
        ReturnRequested = 100,

        /// <summary>The return flow was started.</summary>
        ReturnStarted = 110,

        /// <summary>The package was moved from Packages back into Assets.</summary>
        ReturnMovedToProject = 120,

        /// <summary>Package Manager resolve was started after the package was moved back into Assets.</summary>
        ReturnResolveStarted = 125,

        /// <summary>Package Manager finished resolving the returned package.</summary>
        ReturnResolved = 130,

        /// <summary>The returned project folder was imported after Package Manager released its old path.</summary>
        ReturnImported = 140
    }
}
