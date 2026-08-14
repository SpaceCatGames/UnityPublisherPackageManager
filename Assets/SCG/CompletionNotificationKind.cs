namespace SCG.UPPM
{
    /// <summary>
    /// Identifies an operation represented by a durable completion notification.
    /// </summary>
    internal enum CompletionNotificationKind
    {
        /// <summary>An Assets folder was converted into an embedded UPM package.</summary>
        UpmBuild = 0,
        /// <summary>An embedded UPM package was returned to Assets.</summary>
        UpmReturn = 1,
        /// <summary>Samples and Documentation became visible.</summary>
        SamplesVisible = 2,
        /// <summary>Samples and Documentation became hidden.</summary>
        SamplesHidden = 3
    }
}
