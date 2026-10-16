using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Wire the two public actions to explicit UI buttons. Startup never redeems automatically.</summary>
    public class GiftCodesExample : MonoBehaviour
    {
        [SerializeField] private string giftCode = "SUMMER2026";
        private bool _ready, _busy;
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();
                if (!await new HorizonServer().Connect()) return;
                var auth = UserManager.Instance;
                if (auth.CurrentUser == null || !auth.CurrentUser.IsValid())
                {
                    if (!GiftCodeSampleStore.PrepareIdentity(out var restore)) return;
                    if (restore ? !await auth.RestoreAnonymousSession() : !await auth.SignUpAnonymous("GiftPlayer")) return;
                }
                _ready = true;
                HorizonApp.Log.Info("Gift code ready. An unresolved intent must be retried or reconciled before another redemption.");
            }
            catch (Exception exception) { HorizonApp.Log.Warning($"Gift code setup stopped: {exception.GetType().Name}. Existing identity retained."); }
        }
        public void RedeemConfiguredCode() => Redeem(false);
        public void RetryPendingRedemption() => Redeem(true);
        private async void Redeem(bool retryOnly)
        {
            if (!_ready || _busy || UserManager.Instance.CurrentUser == null || !UserManager.Instance.CurrentUser.IsValid()) return;
            _busy = true;
            try
            {
                string project = HorizonApp.Network.GetApiKey(), user = UserManager.Instance.CurrentUser.UserId;
                if (!GiftCodeSampleStore.TryLoad(project, user, out var pending))
                { HorizonApp.Log.Warning("Saved redemption needs recovery; original storage retained."); return; }
                if (pending == null)
                {
                    if (retryOnly) return;
                    pending = new GiftCodeSampleStore.Intent { userId = user, code = giftCode, idempotencyId = Guid.NewGuid().ToString("D") };
                    if (!GiftCodeSampleStore.Save(project, pending)) return; // Persist original code and UUID before HTTP.
                }
                else if (!retryOnly)
                { HorizonApp.Log.Warning("Unresolved redemption exists. Use RetryPendingRedemption with its original code and UUID."); return; }
                var manager = GiftCodeManager.Instance;
                var result = await manager.Redeem(pending.code, pending.idempotencyId);
                if (project != HorizonApp.Network.GetApiKey() || UserManager.Instance.CurrentUser?.UserId != user) return;
                if (result == null)
                { HorizonApp.Log.Warning($"Redemption [{manager.LastErrorCode}]. Intent retained. Retry uncertain outcomes explicitly; reconcile permanent errors."); return; }
                if (result.playerValues != null) foreach (var value in result.playerValues.values) HorizonApp.Log.Info($"{value.key}: {value.balance}");
                // grantedValues reports the original grant even on replay; never add those amounts locally.
                if (!GiftCodeSampleStore.Confirm(project, pending)) HorizonApp.Log.Warning("Confirmation was not saved. Keep and reconcile the original intent.");
            }
            catch (Exception exception) { HorizonApp.Log.Warning($"Gift code stopped: {exception.GetType().Name}. Pending intent retained."); }
            finally { _busy = false; }
        }
    }
}
