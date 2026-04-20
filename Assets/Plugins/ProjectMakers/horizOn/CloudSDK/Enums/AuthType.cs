namespace PM.horizOn.Cloud.Enums
{
    /// <summary>
    /// Authentication types supported by horizOn.
    /// </summary>
    public enum AuthType
    {
        /// <summary>
        /// Anonymous authentication (no email/password required)
        /// </summary>
        ANONYMOUS,

        /// <summary>
        /// Email and password authentication
        /// </summary>
        EMAIL,

        /// <summary>
        /// Google OAuth authentication
        /// </summary>
        GOOGLE,

        /// <summary>
        /// Apple Sign-In authentication
        /// </summary>
        APPLE
    }
}
