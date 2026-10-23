using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Creates one demo purchase for a new player. Existing players only retry a known persisted intent.</summary>
    public class PlayerValuesExample : MonoBehaviour
    {
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();
                if (!await new HorizonServer().Connect()) return;
                var auth = UserManager.Instance;
                bool created = false;
                if (auth.CurrentUser == null || !auth.CurrentUser.IsValid())
                {
                    if (PlayerPrefs.HasKey("horizOn_AnonymousToken"))
                    { if (!await auth.RestoreAnonymousSession()) { HorizonApp.Log.Warning("Restore failed; original identity and purchase retained"); return; } }
                    else { if (!await auth.SignUpAnonymous("ValuesPlayer")) return; created = true; }
                }
                string userId = auth.CurrentUser.UserId, apiKey = HorizonApp.Network.GetApiKey();
                var values = PlayerValuesManager.Instance;
                if (!(await values.GetState(userId)).IsSuccess) return;
                var config = HorizonConfig.Load();
                if (!PendingPlayerValuesPurchase.TryLoad(apiKey, userId, config?.Hosts, out var pending))
                { HorizonApp.Log.Warning("Pending purchase conflicts or needs recovery. Original files retained."); return; }
                if (pending == null)
                {
                    // PlayerPrefs cannot enumerate unknown historical hosts. Existing identities
                    // need game-owned reconciliation before an automatic demo could create an ID.
                    if (!created) { HorizonApp.Log.Warning("Existing player has no known pending command. Reconcile previous host storage before creating a new purchase."); return; }
                    pending = new PendingPlayerValuesPurchase.Intent { userId = userId, offerKey = "energy-pack", operationId = values.CreateOperationId() };
                    if (!PendingPlayerValuesPurchase.Save(apiKey, pending)) return;
                }
                var result = await values.Purchase(pending.userId, pending.offerKey, pending.operationId);
                if (result.State != null) foreach (var balance in result.State.values) HorizonApp.Log.Info($"{balance.key}: {balance.balance}");
                if (result.IsSuccess) PendingPlayerValuesPurchase.Confirm(apiKey, pending);
                else HorizonApp.Log.Warning($"Purchase [{result.ErrorCode}]. Original intent retained; never replace an expired operation automatically.");
            }
            catch (Exception exception) { HorizonApp.Log.Warning($"Values example stopped: {exception.GetType().Name}; any pending intent remains saved"); }
        }
    }
}
