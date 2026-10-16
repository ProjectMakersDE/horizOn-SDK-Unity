using System;
namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    [Serializable] public class AchievementCondition { public string statisticKey; public long threshold; }
    [Serializable] public class AchievementDefinition
    {
        public string key, name, description, imageUrl, mode;
        public bool hidden, active;
        public AchievementCondition condition;
        public InboxAttachment rewards;
    }
    [Serializable] public class AchievementLimits { public int maxDefinitions, maxLifetimeKeys; }
    [Serializable] public class PendingAchievementReward { public string issuedAt; public PlayerValueAmount[] values = Array.Empty<PlayerValueAmount>(); }
    [Serializable] public class AchievementProgress
    {
        public string key, unlockedAt, rewardState, inboxEntryId;
        public PendingAchievementReward pendingReward;
    }
    [Serializable] public class AchievementsSnapshot
    {
        public string userId, definitionVersion, serverTime;
        public long revision;
        public AchievementDefinition[] definitions;
        public AchievementProgress[] progress = Array.Empty<AchievementProgress>();
        public AchievementLimits limits;
    }
    [Serializable] public class AchievementDeliveryOutcome { public string key, state, inboxEntryId, code; }
    [Serializable] public class AchievementUnlockResponse
    {
        public AchievementsSnapshot achievements;
        public string[] newlyUnlocked = Array.Empty<string>();
        public bool replayed;
        public AchievementDeliveryOutcome[] deliveryResults = Array.Empty<AchievementDeliveryOutcome>();
    }
    [Serializable] public class AchievementDeliveryResponse
    {
        public AchievementsSnapshot achievements;
        public AchievementDeliveryOutcome[] results = Array.Empty<AchievementDeliveryOutcome>();
    }
    public class AchievementsResult
    {
        public bool IsSuccess { get; internal set; }
        public AchievementsSnapshot Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }
    public class AchievementUnlockResult
    {
        public bool IsSuccess { get; internal set; }
        public AchievementUnlockResponse Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }
    public class AchievementDeliveryResult
    {
        public bool IsSuccess { get; internal set; }
        public AchievementDeliveryResponse Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }
}
