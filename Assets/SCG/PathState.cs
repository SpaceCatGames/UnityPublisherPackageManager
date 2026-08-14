namespace SCG.UPPM
{
    /// <summary>
    /// Describes how a filesystem path is currently occupied.
    /// The value is used to distinguish between valid directories, conflicting files,
    /// orphaned meta files, and fully missing paths before editor file operations run.
    /// </summary>
    internal enum PathState
    {
        /// <summary>The path and its adjacent .meta file are both absent.</summary>
        Missing = 0,

        /// <summary>The path is occupied by a directory.</summary>
        Directory = 1,

        /// <summary>The path is occupied by a regular file instead of a directory.</summary>
        File = 2,

        /// <summary>The directory is missing, but the adjacent .meta file still exists.</summary>
        MetaOnly = 3
    }
}
