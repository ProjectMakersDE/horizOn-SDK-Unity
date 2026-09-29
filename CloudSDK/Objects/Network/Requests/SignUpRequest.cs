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
        public string appleIdentityToken;
        public string appleFirstName;
        public string appleLastName;

        public static SignUpRequest CreateAnonymous(string username = null, string anonymousToken = null)
        {
            // Keep the optional argument for source compatibility. The server is
            // the sole issuer, so the request must omit anonymousToken.
            return new SignUpRequest
            {
                type = nameof(AuthType.ANONYMOUS),
                username = username
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

        public static SignUpRequest CreateApple(string identityToken, string firstName = null,
            string lastName = null, string username = null)
        {
            return new SignUpRequest
            {
                type = nameof(AuthType.APPLE),
                appleIdentityToken = identityToken,
                appleFirstName = firstName,
                appleLastName = lastName,
                username = username
            };
        }
    }
}
