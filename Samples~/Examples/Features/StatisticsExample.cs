using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Requests;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Configure "kills" (sum) and "best_score" (max) for the project in the Dashboard.
    /// Attach to an empty GameObject. One explicit batch reports a simulated completed session.
    /// Pending IDs and payloads survive restart; uncertain results never create another ID.
    /// </summary>
    public class StatisticsExample : MonoBehaviour
    {
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();
                if (!await new HorizonServer().Connect()) return;
                if (!UserManager.Instance.IsSignedIn && !await UserManager.Instance.RestoreAnonymousSession()
                    && !await UserManager.Instance.SignUpAnonymous("StatisticsPlayer")) return;
                var manager = StatisticsManager.Instance;
                var snapshot = await manager.GetStatistics(); // Explicit snapshot also supplies server time.
                if (snapshot == null) { Debug.LogError(manager.LastErrorCode); return; }
                string pendingKey = "horizOn_statistics_pending_" + snapshot.userId;
                UpdateStatisticsRequest pending = PlayerPrefs.HasKey(pendingKey)
                    ? JsonUtility.FromJson<UpdateStatisticsRequest>(PlayerPrefs.GetString(pendingKey))
                    : new UpdateStatisticsRequest { userId = snapshot.userId, operationId = manager.CreateOperationId(),
                        updates = new[] { new StatisticUpdate("kills", 3), new StatisticUpdate("best_score", 250) } };
                PlayerPrefs.SetString(pendingKey, JsonUtility.ToJson(pending));
                PlayerPrefs.Save();
                var result = await manager.UpdateStatistics(pending.updates, pending.operationId);
                if (result == null)
                {
                    // Inspect snapshot/status and retry this saved batch/ID. An expired ID requires
                    // game-specific reconciliation; blindly replacing it could report a session twice.
                    Debug.LogWarning("Statistics unresolved: " + manager.LastErrorCode + "; original batch retained");
                    return;
                }
                PlayerPrefs.DeleteKey(pendingKey); PlayerPrefs.Save();
                foreach (var value in result.values) Debug.Log(value.key + "=" + value.value + " (set=" + value.hasValue + ")");
            }
            catch (Exception error) { Debug.LogError(error.Message); }
        }
    }
}
