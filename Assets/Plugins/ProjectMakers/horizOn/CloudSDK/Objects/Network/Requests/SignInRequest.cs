using System;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Request object for user sign in.
    /// </summary>
    [Serializable]
    public class SignInRequest
    {
        public string type; // EMAIL, ANONYMOUS, or GOOGLE
        public string email;
        public string password;
        public string anonymousToken;
        public string googleAuthorizationCode;
        public string googleRedirectUri;
    }
}
