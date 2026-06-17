using System;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Request object for creating a user log.
    /// </summary>
    [Serializable]
    public class CreateUserLogRequest
    {
        /// <summary>
        /// Log message (max 1000 characters)
        /// </summary>
        public string message;

        /// <summary>
        /// Log type: INFO, WARN, or ERROR
        /// </summary>
        public string type;

        /// <summary>
        /// User ID who created the log
        /// </summary>
        public string userId;

        /// <summary>
        /// Optional error code (max 50 characters)
        /// </summary>
        public string errorCode;
    }
}
