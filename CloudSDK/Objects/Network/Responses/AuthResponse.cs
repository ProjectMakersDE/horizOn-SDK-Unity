using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// Response object for authentication requests (signup, signin).
    /// </summary>
    [Serializable]
    public class AuthResponse
    {
        public string userId;
        public string username;
        public string email;
        public string accessToken;
        public string authStatus; // AUTHENTICATED, USER_NOT_FOUND, etc.
        public string message;
        public bool isAnonymous;
        public bool isVerified;
        public string anonymousToken;
        public string googleId;
        public string appleUserId;        // Apple `sub` claim. null for non-Apple users.
        public bool isPrivateRelayEmail;  // true if email is an Apple private relay alias.
        public string createdAt;
    }
}
