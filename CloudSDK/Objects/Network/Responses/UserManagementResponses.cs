using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// Response object for checking authentication.
    /// </summary>
    [Serializable]
    public class CheckAuthResponse
    {
        public string userId;
        public bool isAuthenticated;
        public string authStatus; // AUTHENTICATED, TOKEN_EXPIRED, etc.
        public string message;
    }

    /// <summary>
    /// Generic message response.
    /// </summary>
    [Serializable]
    public class MessageResponse
    {
        public bool success;
        public string message;
    }
}
