using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Requests;
namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Configure kills(sum) and best_score(max). One simulated completed session keeps one saved batch/ID until confirmed.</summary>
    public class StatisticsExample : MonoBehaviour
    {
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();
                if (!await new HorizonServer().Connect()) return;
                if (!UserManager.Instance.IsSignedIn)
                {
                    if (!StatisticsSampleStore.PrepareIdentity(out bool restore))
                    { HorizonApp.Log.Warning("Existing identity needs recovery. No new account created."); return; }
                    bool authenticated = restore ? await UserManager.Instance.RestoreAnonymousSession() : await UserManager.Instance.SignUpAnonymous("StatisticsPlayer");
                    if (!authenticated) return;
                }
                var manager = StatisticsManager.Instance;
                var snapshot = await manager.GetStatistics();
                if (snapshot == null) { HorizonApp.Log.Error(manager.LastErrorCode); return; }
                string project = HorizonApp.Network.GetApiKey();
                if (!StatisticsSampleStore.TryLoad(project, snapshot.userId, out var pending))
                { HorizonApp.Log.Warning("Stored statistics batch needs reconciliation. Original data retained."); return; }
                if (pending == null)
                {
                    pending = new UpdateStatisticsRequest { userId = snapshot.userId, operationId = manager.CreateOperationId(),
                        updates = new[] { new StatisticUpdate("kills", 3), new StatisticUpdate("best_score", 250) } };
                    if (!StatisticsSampleStore.Save(project, pending)) return;
                }
                var result = await manager.UpdateStatistics(pending.updates, pending.operationId);
                if (result == null)
                { HorizonApp.Log.Warning("Statistics unresolved: " + manager.LastErrorCode + ". Original batch retained."); return; }
                StatisticsSampleStore.Confirm(project, pending);
                foreach (var value in result.values) HorizonApp.Log.Info(value.key + "=" + value.value + " (set=" + value.hasValue + ")");
            }
            catch (Exception) { HorizonApp.Log.Error("Statistics sample failed. Saved identity and batch retained."); }
        }
    }
}
