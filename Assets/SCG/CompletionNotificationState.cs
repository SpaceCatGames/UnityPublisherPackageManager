using System;
using System.Collections.Generic;

namespace SCG.UPPM
{
    /// <summary>
    /// Stores all durable completion notifications and queued Samples visibility requests in one persisted document.
    /// </summary>
    [Serializable]
    internal sealed class CompletionNotificationState
    {
        /// <summary>Completions waiting for publication.</summary>
        public List<CompletionNotification> Notifications = new();
        /// <summary>Samples visibility requests waiting behind an active request.</summary>
        public List<SamplesVisibility> PendingVisibility = new();
    }
}
