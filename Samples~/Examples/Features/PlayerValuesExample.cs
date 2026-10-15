using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Configure energy-pack in the Dashboard. Persists one immutable purchase intent before sending.</summary>
    public class PlayerValuesExample : MonoBehaviour
    {
        [Serializable] private class Intent { public string userId, offerKey, operationId; }
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();
                if (!await new HorizonServer().Connect()) return;
                var auth = UserManager.Instance;
                if (auth.CurrentUser == null || !auth.CurrentUser.IsValid())
                {
                    if (auth.HasCachedAnonymousToken())
                    { if (!await auth.RestoreAnonymousSession()) { Debug.LogWarning("Restore failed; original identity and purchase retained"); return; } }
                    else if (!await auth.SignUpAnonymous("ValuesPlayer")) return;
                }
                var userId = auth.CurrentUser.UserId; var values = PlayerValuesManager.Instance;
                if (!(await values.GetState(userId)).IsSuccess) return;
                string scope = HorizonApp.Network.GetActiveHost() + "|" + HorizonApp.Network.GetApiKey() + "|" + userId;
                string key;
                using (var sha = SHA256.Create()) key = "horizon_values_pending_" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(scope))).Replace("-", "");
                Intent pending;
                if (PlayerPrefs.HasKey(key))
                {
                    pending = JsonUtility.FromJson<Intent>(PlayerPrefs.GetString(key));
                    if (pending == null || pending.userId != userId || string.IsNullOrEmpty(pending.operationId) || string.IsNullOrEmpty(pending.offerKey))
                    { Debug.LogWarning("Pending purchase needs reconciliation; retained"); return; }
                }
                else
                {
                    pending = new Intent { userId = userId, offerKey = "energy-pack", operationId = values.CreateOperationId() };
                    PlayerPrefs.SetString(key, JsonUtility.ToJson(pending)); PlayerPrefs.Save();
                }
                var result = await values.Purchase(pending.userId, pending.offerKey, pending.operationId);
                if (result.State != null) foreach (var balance in result.State.values) Debug.Log($"{balance.key}: {balance.balance}");
                if (result.IsSuccess) { PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); }
                else Debug.LogWarning($"Purchase [{result.ErrorCode}]. Original intent retained; never replace an expired operation automatically.");
            }
            catch (Exception exception) { Debug.LogWarning($"Values example stopped: {exception.GetType().Name}; any pending intent remains saved"); }
        }
    }
}
