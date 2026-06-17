using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// Response object for a single news entry.
    /// </summary>
    [Serializable]
    public class UserNewsResponse
    {
        public string id;
        public string title;
        public string message;
        public string releaseDate;
        public string languageCode;
    }
}
