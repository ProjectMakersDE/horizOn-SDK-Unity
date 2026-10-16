using System;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;
namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Call explicitly from an authenticated game's screen/buttons. This example never signs up,
    /// polls, invents reward IDs or credits local currency. Keep the existing player's identity.
    /// </summary>
    public class AchievementsExample : MonoBehaviour
    {
        private int _generation;
        public async Task Refresh()
        {
            string userId = CurrentPlayer(); if (userId == null) return;
            int generation = _generation;
            var result = await AchievementsManager.Instance.GetAchievements(userId);
            if (!Current(userId, generation)) return;
            if (!result.IsSuccess) { HorizonApp.Log.Warning("Achievements: " + result.ErrorCode); return; }
            Show(result.Data);
        }
        public async Task UnlockManual(string originalKey)
        {
            string userId = CurrentPlayer(); if (userId == null) return;
            int generation = _generation;
            var result = await AchievementsManager.Instance.Unlock(userId, originalKey);
            if (!Current(userId, generation)) return;
            if (!result.IsSuccess) { HorizonApp.Log.Warning("Keep the original manual key for retry: " + result.ErrorCode); return; }
            Show(result.Data.achievements);
            if (!result.Data.replayed)
                foreach (var key in result.Data.newlyUnlocked) HorizonApp.Log.Info("New achievement: " + key);
            foreach (var outcome in result.Data.deliveryResults)
                if (outcome.state == "pending") HorizonApp.Log.Warning("Reward remains pending: " + outcome.key + " / " + outcome.code);
        }
        public async Task RetryPendingRewards(string[] originalKeys)
        {
            string userId = CurrentPlayer(); if (userId == null) return;
            int generation = _generation;
            // At most8 original server-owned achievement keys; no new operation/source IDs.
            var result = await AchievementsManager.Instance.DeliverRewards(userId, originalKeys);
            if (!Current(userId, generation)) return;
            if (!result.IsSuccess) { HorizonApp.Log.Warning("Keep the original delivery keys: " + result.ErrorCode); return; }
            Show(result.Data.achievements);
            foreach (var outcome in result.Data.results)
                HorizonApp.Log.Info(outcome.key + ": " + outcome.state + " / " + outcome.code);
        }
        public Task RefreshAfterStatistics(StatisticsSnapshot report)
        {
            // An already committed replay must not produce another new-unlock notification.
            return report != null && !report.replayed && report.newlyUnlocked != null && report.newlyUnlocked.Length > 0
                ? Refresh() : Task.CompletedTask;
        }
        public async Task ClaimSavedInboxReward(string originalEntryId)
        {
            string userId = CurrentPlayer(); if (userId == null) return;
            int generation = _generation;
            // The game's Inbox UI already durably saved this exact entry ID by project/player.
            // A NEW selection uses InboxExample.ClaimNewEntry before saving. A saved retry can
            // be absent/already claimed; never replace its ID or add the attachment locally.
            var result = await InboxManager.Instance.Claim(userId, originalEntryId);
            if (!Current(userId, generation)) return;
            if (!result.IsSuccess) { HorizonApp.Log.Warning("Keep original Inbox claim: " + result.ErrorCode); return; }
            foreach (var value in result.Data.playerValues.values)
                HorizonApp.Log.Info("Authoritative " + value.key + ": " + value.balance);
        }
        private string CurrentPlayer()
        {
            var player = UserManager.Instance.CurrentUser;
            return isActiveAndEnabled && player != null && player.IsValid() && player.AccessToken == HorizonApp.Network.GetSessionToken() ? player.UserId : null;
        }
        private bool Current(string userId, int generation) => generation == _generation && CurrentPlayer() == userId;
        private static void Show(AchievementsSnapshot snapshot)
        {
            foreach (var row in snapshot.progress)
                HorizonApp.Log.Info(row.key + ": " + row.rewardState + (row.inboxEntryId == null ? "" : " -> " + row.inboxEntryId));
            // Display snapshot.definitions; hidden locked metadata is supplied only after unlock.
            // pendingReward preserves the original values even if the catalogue is later edited.
        }
        private void OnDisable() { _generation++; }
    }
}
