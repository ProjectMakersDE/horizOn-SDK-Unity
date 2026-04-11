using System;
using System.Collections.Generic;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Request object for sending an email via a template.
    /// </summary>
    [Serializable]
    public class SendEmailRequest
    {
        public string userId;
        public string templateSlug;
        public Dictionary<string, string> variables;
        public string language;
        public string scheduledAt;  // ISO 8601 or null
    }
}
