using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// Response object for saving cloud data.
    /// </summary>
    [Serializable]
    public class SaveCloudSaveResponse
    {
        public bool success;
        public int dataSizeBytes;
    }

    /// <summary>
    /// Response object for loading cloud data.
    /// </summary>
    [Serializable]
    public class LoadCloudSaveResponse
    {
        public bool found;
        public string saveData; // UTF-8 string
    }
}
