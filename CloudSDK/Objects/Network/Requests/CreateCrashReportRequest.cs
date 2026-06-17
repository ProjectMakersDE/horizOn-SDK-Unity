using System;
using System.Collections.Generic;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Request object for creating a crash report.
    /// </summary>
    [Serializable]
    public class CreateCrashReportRequest
    {
        /// <summary>
        /// Crash type: CRASH, NON_FATAL, or ANR.
        /// </summary>
        public string type;

        /// <summary>
        /// Error message or exception message.
        /// </summary>
        public string message;

        /// <summary>
        /// Full stack trace of the crash.
        /// </summary>
        public string stackTrace;

        /// <summary>
        /// SHA-256 fingerprint for grouping similar crashes.
        /// </summary>
        public string fingerprint;

        /// <summary>
        /// Application version string.
        /// </summary>
        public string appVersion;

        /// <summary>
        /// horizOn SDK version string.
        /// </summary>
        public string sdkVersion;

        /// <summary>
        /// Runtime platform (e.g., "Android", "iOS", "WindowsEditor").
        /// </summary>
        public string platform;

        /// <summary>
        /// Operating system name and version.
        /// </summary>
        public string os;

        /// <summary>
        /// Device model name.
        /// </summary>
        public string deviceModel;

        /// <summary>
        /// Device system memory in megabytes.
        /// </summary>
        public int deviceMemoryMb;

        /// <summary>
        /// Session ID for correlating crashes with a session.
        /// </summary>
        public string sessionId;

        /// <summary>
        /// User ID of the affected user.
        /// </summary>
        public string userId;

        /// <summary>
        /// List of breadcrumb entries leading up to the crash.
        /// </summary>
        public List<BreadcrumbEntry> breadcrumbs = new List<BreadcrumbEntry>();

        /// <summary>
        /// Custom key-value pairs for additional crash context.
        /// </summary>
        public Dictionary<string, string> customKeys = new Dictionary<string, string>();

        /// <summary>
        /// A single breadcrumb entry within a crash report.
        /// </summary>
        [Serializable]
        public class BreadcrumbEntry
        {
            public string timestamp;
            public string type;
            public string message;
        }
    }
}
