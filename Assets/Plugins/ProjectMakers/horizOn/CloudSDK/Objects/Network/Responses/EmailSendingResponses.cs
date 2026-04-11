using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// Response object for sending an email.
    /// </summary>
    [Serializable]
    public class SendEmailResponse
    {
        public string id;
        public string status;       // "pending"
        public string scheduledAt;  // ISO 8601 or null
    }

    /// <summary>
    /// Response object for cancelling a pending email.
    /// </summary>
    [Serializable]
    public class CancelEmailResponse
    {
        public string message;
    }

    /// <summary>
    /// Response object for querying email status.
    /// </summary>
    [Serializable]
    public class EmailStatusResponse
    {
        public string id;
        public string status;        // "pending", "sent", "failed"
        public string templateSlug;
        public string userId;
        public string language;
        public string scheduledAt;   // nullable
        public string processedAt;   // nullable
        public string errorReason;   // nullable
        public string createdAt;
    }
}
