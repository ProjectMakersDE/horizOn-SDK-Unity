using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// Response object for creating a user log.
    /// </summary>
    [Serializable]
    public class CreateUserLogResponse
    {
        /// <summary>
        /// The unique ID of the created log entry.
        /// </summary>
        public string id;

        /// <summary>
        /// Timestamp when the log was created (ISO 8601 format).
        /// </summary>
        public string createdAt;
    }
}
