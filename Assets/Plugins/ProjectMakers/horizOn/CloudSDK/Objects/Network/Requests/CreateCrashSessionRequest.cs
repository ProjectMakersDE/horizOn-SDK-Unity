using System;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Request object for registering a crash reporting session.
    /// </summary>
    [Serializable]
    public class CreateCrashSessionRequest
    {
        /// <summary>
        /// Unique session identifier.
        /// </summary>
        public string sessionId;

        /// <summary>
        /// Application version string.
        /// </summary>
        public string appVersion;

        /// <summary>
        /// Runtime platform (e.g., "Android", "iOS", "WindowsEditor").
        /// </summary>
        public string platform;

        /// <summary>
        /// User ID associated with this session.
        /// </summary>
        public string userId;
    }
}
