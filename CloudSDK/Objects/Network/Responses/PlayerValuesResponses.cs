using System;
namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    [Serializable] public class PlayerValueBalance
    {
        public string key;
        public long balance, earnedToday;
        /// <summary>-1 means no cap; zero is a real cap of zero.</summary>
        public long dailyCap = -1;
        public bool HasDailyCap => dailyCap >= 0;
    }
    [Serializable] public class PlayerValueAmount { public string key; public long amount; }
    [Serializable] public class PlayerValueOffer
    {
        public string key, name;
        public bool active;
        public PlayerValueAmount[] costs = Array.Empty<PlayerValueAmount>(), rewards = Array.Empty<PlayerValueAmount>();
        public ItemReward[] itemRewards = Array.Empty<ItemReward>();
        /// <summary>-1 means unlimited.</summary>
        public int purchasesPerDay = -1;
    }
    [Serializable] public class PlayerValuesOperationWindow { public string serverTime, oldestAcceptedAt; }
    [Serializable] public class PlayerValuesOperation
    {
        public string operationId;
        public bool replayed;
        public PlayerValueAmount[] applied = Array.Empty<PlayerValueAmount>();
        /// <summary>Original grant references, even after the current offer changes or an instance is discarded.</summary>
        public ItemReference[] items = Array.Empty<ItemReference>();
    }
    [Serializable] public class PlayerValuesState
    {
        public string userId, day;
        public long definitionVersion, revision;
        public PlayerValueBalance[] values = Array.Empty<PlayerValueBalance>();
        public PlayerValuesOperationWindow operationWindow;
        public PlayerValuesOperation operation;
    }
    [Serializable] public class PlayerValuesOffers
    {
        public long definitionVersion;
        public string serverTime;
        public PlayerValueBalance[] definitions = Array.Empty<PlayerValueBalance>();
        public PlayerValueOffer[] offers = Array.Empty<PlayerValueOffer>();
    }
    public class PlayerValuesResult
    {
        public bool IsSuccess { get; internal set; }
        public PlayerValuesState State { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }
    public class PlayerValuesOffersResult
    {
        public bool IsSuccess { get; internal set; }
        public PlayerValuesOffers Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }
}
