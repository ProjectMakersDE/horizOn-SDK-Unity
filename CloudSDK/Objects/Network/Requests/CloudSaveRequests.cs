using System;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    /// <summary>
    /// Request object for saving cloud data.
    /// </summary>
    [Serializable]
    public class SaveCloudDataRequest
    {
        public string userId;
        public string saveData; // UTF-8 string
    }

    /// <summary>
    /// Request object for loading cloud data.
    /// </summary>
    [Serializable]
    public class LoadCloudDataRequest
    {
        public string userId;
    }
}
