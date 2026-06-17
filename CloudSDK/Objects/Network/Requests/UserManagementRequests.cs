using System;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Request object for checking authentication.
    /// </summary>
    [Serializable]
    public class CheckAuthRequest
    {
        public string userId;
        public string sessionToken;
    }

    /// <summary>
    /// Request object for email verification.
    /// </summary>
    [Serializable]
    public class VerifyEmailRequest
    {
        public string token;
    }

    /// <summary>
    /// Request object for forgot password.
    /// </summary>
    [Serializable]
    public class ForgotPasswordRequest
    {
        public string email;
    }

    /// <summary>
    /// Request object for password reset.
    /// </summary>
    [Serializable]
    public class ResetPasswordRequest
    {
        public string token;
        public string newPassword;
    }

    /// <summary>
    /// Request object for changing user display name.
    /// </summary>
    [Serializable]
    public class ChangeNameRequest
    {
        public string userId;
        public string sessionToken;
        public string newName;
    }
}
