using System;

namespace SCG.UPPM
{
    /// <summary>
    /// Describes a durable editor operation completion that may cross an AppDomain reload.
    /// </summary>
    [Serializable]
    internal sealed class CompletionNotification
    {
        /// <summary>Completed operation.</summary>
        public CompletionNotificationKind Kind;

        /// <summary>Absolute package root path at the completed location.</summary>
        public string RootPath;

        /// <summary>Identifier of the AppDomain that queued the notification.</summary>
        public string OriginatingDomainId;

        /// <summary>Whether publication must wait for a subsequent AppDomain.</summary>
        public bool RequiresDomainReload;
    }
}
