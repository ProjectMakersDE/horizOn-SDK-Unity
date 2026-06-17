using System;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Request object for submitting user feedback.
    /// </summary>
    [Serializable]
    public class SubmitFeedbackRequest
    {
        public string userId; // Required - User ID submitting feedback
        public string title; // Required, 1-100 characters
        public string category; // BUG, FEATURE, GENERAL
        public string message;
        public string email;
        public string deviceInfo;
    }
}
