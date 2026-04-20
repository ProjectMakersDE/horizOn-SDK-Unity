using System;

namespace PM.horizOn.Cloud.Objects.Data
{
    /// <summary>
    /// User data container.
    /// Stores current user information.
    /// </summary>
    [Serializable]
    public class UserData
    {
        public string UserId = string.Empty;
        public string Email = string.Empty;
        public string DisplayName = string.Empty;
        public string AuthType = string.Empty;
        public string AccessToken = string.Empty;
        public string AnonymousToken = string.Empty;
        public string AppleUserId = string.Empty;
        public bool IsPrivateRelayEmail = false;
        public bool IsEmailVerified = false;
        public bool IsAnonymous = false;
        public DateTime LastLoginTime = DateTime.UtcNow;

        public bool IsValid()
        {
            return !string.IsNullOrEmpty(UserId) && !string.IsNullOrEmpty(AccessToken);
        }

        public void Clear()
        {
            UserId = string.Empty;
            Email = string.Empty;
            DisplayName = string.Empty;
            AuthType = string.Empty;
            AccessToken = string.Empty;
            AnonymousToken = string.Empty;
            AppleUserId = string.Empty;
            IsPrivateRelayEmail = false;
            IsEmailVerified = false;
            IsAnonymous = false;
            LastLoginTime = DateTime.UtcNow;
        }
    }
}
