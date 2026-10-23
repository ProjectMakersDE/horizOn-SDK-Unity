using System;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;
namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Explicit screen/button actions for an existing authenticated player; no signup or poll loop.
    /// The game durably stores each intent by project + player BEFORE ApplySavedAction. Keep its action,
    /// target, expectedRevision and optional requestId unchanged after timeout. Refresh/reconcile explicitly;
    /// a new revision requires a new deliberate player action, never an automatic replacement.</summary>
    public class FriendsExample : MonoBehaviour
    {
        private int _generation;
        public async Task Refresh()
        {
            string userId = CurrentPlayer(); if (userId == null) return;
            var network = HorizonApp.Network; long context = network.ContextGeneration; int generation = _generation;
            var result = await FriendsManager.Instance.GetFriends(userId);
            if (!Current(userId, network, context, generation)) return;
            if (!result.IsSuccess) { HorizonApp.Log.Warning("Friends refresh: " + result.ErrorCode); return; }
            HorizonApp.Log.Info("Your exact share code: " + result.Data.friendCode);
            foreach (var friend in result.Data.friends)
                HorizonApp.Log.Info((friend.display.name ?? friend.userId) + " / last active: " + (friend.lastActiveAt ?? "unknown"));
            // lastActiveAt is optional approximate activity. It is never an online/presence flag.
        }
        public async Task FindExact(string uuidOrCanonicalCode)
        {
            string userId = CurrentPlayer(); if (userId == null) return;
            var network = HorizonApp.Network; long context = network.ContextGeneration; int generation = _generation;
            var result = await FriendsManager.Instance.FindPlayer(userId, uuidOrCanonicalCode);
            if (!Current(userId, network, context, generation)) return;
            if (!result.IsSuccess) { HorizonApp.Log.Warning("Lookup: " + result.ErrorCode); return; }
            HorizonApp.Log.Info(result.Data.friendCode + " / " + (result.Data.display.name ?? "unnamed player"));
            // Show only these public display/profile fields; never search names or expose login data.
        }
        public async Task ApplySavedAction(string savedUserId, string action, string originalTargetUserId,
            long originalRevision, string originalRequestId = null)
        {
            string userId = CurrentPlayer(); if (userId == null || !string.Equals(savedUserId, userId, StringComparison.OrdinalIgnoreCase)) return;
            // The caller selected this saved command from its matching project + player store.
            var network = HorizonApp.Network; long context = network.ContextGeneration; int generation = _generation;
            FriendsResult result;
            switch (action)
            {
                case "request": result = await FriendsManager.Instance.SendRequest(userId, originalTargetUserId, originalRevision); break;
                case "accept": result = await FriendsManager.Instance.AcceptRequest(userId, originalTargetUserId, originalRevision, originalRequestId); break;
                case "decline": result = await FriendsManager.Instance.DeclineRequest(userId, originalTargetUserId, originalRevision, originalRequestId); break;
                case "cancel": result = await FriendsManager.Instance.CancelRequest(userId, originalTargetUserId, originalRevision, originalRequestId); break;
                case "remove": result = await FriendsManager.Instance.RemoveFriend(userId, originalTargetUserId, originalRevision); break;
                case "block": result = await FriendsManager.Instance.BlockPlayer(userId, originalTargetUserId, originalRevision); break;
                case "unblock": result = await FriendsManager.Instance.UnblockPlayer(userId, originalTargetUserId, originalRevision); break;
                default: return;
            }
            if (!Current(userId, network, context, generation)) return;
            if (!result.IsSuccess)
            {
                HorizonApp.Log.Warning("Keep the original saved intent and reconcile explicitly: " + result.ErrorCode);
                return;
            }
            // Remove the saved intent only after durably recording this successful result.
            // A later Refresh does not resubmit this action or replace its request identity.
            HorizonApp.Log.Info("Friends revision: " + result.Data.revision);
        }
        private string CurrentPlayer()
        {
            var player = UserManager.Instance.CurrentUser;
            return isActiveAndEnabled && player != null && player.IsValid() && player.AccessToken == HorizonApp.Network.GetSessionToken() ? player.UserId : null;
        }
        private bool Current(string userId, NetworkService network, long context, int generation)
            => generation == _generation && ReferenceEquals(network, HorizonApp.Network) && network.ContextGeneration == context && CurrentPlayer() == userId;
        private void OnDisable() { _generation++; }
    }
}
