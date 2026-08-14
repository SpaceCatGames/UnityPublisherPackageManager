namespace SCG.UPPM
{
    /// <summary>
    /// Identifies a completed UPPM package transition.
    /// </summary>
    public enum UpmPackageAction
    {
        /// <summary>The project folder was converted into an embedded UPM package.</summary>
        Build = 0,

        /// <summary>The embedded UPM package was returned to its project folder.</summary>
        Return = 1
    }
}
