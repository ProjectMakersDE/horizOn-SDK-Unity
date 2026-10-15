using System;

namespace PM.horizOn.Cloud.Objects.Network.Requests
{
    [Serializable]
    public class StatisticUpdate
    {
        public string key;
        public long value;

        public StatisticUpdate() { }
        public StatisticUpdate(string key, long value) { this.key = key; this.value = value; }
    }

    [Serializable]
    public class UpdateStatisticsRequest
    {
        public string userId;
        public string operationId;
        public StatisticUpdate[] updates;
    }
}
