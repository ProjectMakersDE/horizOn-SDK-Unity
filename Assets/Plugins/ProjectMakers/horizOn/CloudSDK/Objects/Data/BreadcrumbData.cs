using System;

namespace PM.horizOn.Cloud.Objects.Data
{
    /// <summary>
    /// Data container for a crash reporting breadcrumb.
    /// Records a timestamped event for crash context.
    /// </summary>
    [Serializable]
    public class BreadcrumbData
    {
        /// <summary>
        /// ISO 8601 timestamp of when the breadcrumb was recorded.
        /// </summary>
        public string timestamp;

        /// <summary>
        /// Type/category of the breadcrumb (e.g., "navigation", "user", "error", "log").
        /// </summary>
        public string type;

        /// <summary>
        /// Human-readable breadcrumb message.
        /// </summary>
        public string message;
    }
}
