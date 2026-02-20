using System;
using PM.horizOn.Cloud.Enums;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Request object for user signup.
    /// </summary>
    [Serializable]
    public class SignUpRequest
    {
        public string type; // ANONYMOUS, EMAIL, GOOGLE
        public string username;
        public string email;
        public string password;
        public string anonymousToken;
        public string googleAuthorizationCode;
        public string googleRedirectUri;

        public static SignUpRequest CreateAnonymous(string username = null, string anonymousToken = null)
        {
            // Generate a unique anonymous token if not provided (max 32 chars per API spec)
            if (string.IsNullOrEmpty(anonymousToken))
            {
                // Remove dashes from GUID to fit within 32 char limit
                anonymousToken = System.Guid.NewGuid().ToString("N");
            }

            return new SignUpRequest
            {
                type = nameof(AuthType.ANONYMOUS),
                username = username,
                anonymousToken = anonymousToken
            };
        }

        public static SignUpRequest CreateEmail(string email, string password, string username = null)
        {
            return new SignUpRequest
            {
                type = nameof(AuthType.EMAIL),
                email = email,
                password = password,
                username = username
            };
        }

        public static SignUpRequest CreateGoogle(string googleAuthorizationCode, string redirectUri = "", string username = null)
        {
            return new SignUpRequest
            {
                type = nameof(AuthType.GOOGLE),
                googleAuthorizationCode = googleAuthorizationCode,
                googleRedirectUri = redirectUri,
                username = username
            };
        }
    }
}
