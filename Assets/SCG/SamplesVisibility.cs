namespace SCG.UPPM
{
    /// <summary>
    /// Identifies the requested visibility of Samples and Documentation folders.
    /// </summary>
    public enum SamplesVisibility
    {
        /// <summary>Folders use their tilde-suffixed package names.</summary>
        Hidden = 0,

        /// <summary>Folders use visible names that Unity imports into the project.</summary>
        Visible = 1
    }
}
