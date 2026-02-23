using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// Response object for creating a crash report.
    /// </summary>
    [Serializable]
    public class CreateCrashReportResponse
    {
        /// <summary>
        /// The unique ID of the created crash report.
        /// </summary>
        public string id;

        /// <summary>
        /// The crash group ID this report was assigned to.
        /// </summary>
        public string groupId;

        /// <summary>
        /// Timestamp when the report was created (ISO 8601 format).
        /// </summary>
        public string createdAt;
    }
}
