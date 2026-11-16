using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using PM.horizOn.Cloud.Transport;
using UnityEngine;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Explicit screen actions. One durable original send per project/player; restart confirmation uses current GETs only.</summary>
    public sealed class ItemGiftsExample : MonoBehaviour
    {
        [SerializeField] private string projectId;
        [SerializeField] private string journalDirectory;
        private sealed class Review
        {
            internal string scope, recipient, instance;
            internal long inventoryRevision, configRevision;
        }
        private Review _review;
        private int _generation;
        private bool _busy;
        public string LastErrorCode { get; private set; }
        public long ReviewedFee { get; private set; }
        public string ReviewedCurrencyKey { get; private set; }
        public FriendLookup ReviewedRecipient { get; private set; }
        private string User => this && isActiveAndEnabled && UserManager.Instance.CurrentUser?.IsValid() == true && UserManager.Instance.CurrentUser.AccessToken == HorizonApp.Network.GetSessionToken() ? UserManager.Instance.CurrentUser.UserId : null;
        private string Scope => projectId + "|" + User + "|" + HorizonApp.Network.GetApiKey() + "|" + HorizonApp.Network.GetActiveHost() + "|" + HorizonApp.Network.GetSessionToken() + "|" + HorizonApp.Network.ContextGeneration + "|" + _generation;
        private void OnDisable() { _generation++; _review = null; ReviewedRecipient = null; }
        private ItemGiftsSampleStore Store(string user) => new ItemGiftsSampleStore(string.IsNullOrEmpty(journalDirectory) ? Application.persistentDataPath : journalDirectory, projectId, user);
        private bool Current(string scope, NetworkService network) => User != null && Scope == scope && ReferenceEquals(network, HorizonApp.Network);
        private bool Failed(string code) { LastErrorCode = code; return false; }
        public Task<ItemGiftsResult<ItemGiftConfigView>> ReadConfig() => ItemGiftsManager.Instance.GetConfig(User);
        public Task<ItemGiftsResult<ItemGiftStateView>> ReadGifts() => ItemGiftsManager.Instance.GetState(User);
        public Task<ItemGiftsResult<ItemGiftView>> ReadGift(string giftId) => ItemGiftsManager.Instance.GetGift(User, giftId);
        public Task<ItemsResult<InventorySnapshot>> ReadInventory() => ItemsManager.Instance.GetInventory(User);
        /// <summary>Resolves a public friend code to a player ID and profile to show before the player confirms.</summary>
        public async Task<bool> FindRecipient(string friendCode)
        {
            string user = User, scope = Scope; ReviewedRecipient = null; _review = null;
            if (user == null) return Failed("SESSION_REQUIRED");
            var found = await FriendsManager.Instance.FindPlayer(user, friendCode);
            if (Scope != scope) return Failed("STALE_RESPONSE");
            if (!found.IsSuccess) return Failed(found.ErrorCode);
            ReviewedRecipient = found.Data; LastErrorCode = null; return true;
        }
        /// <summary>Freezes recipient, item, inventory revision and fee rules after the player saw them. Needs current config and inventory reads.</summary>
        public bool ReviewSend(string recipientId, string instanceId)
        {
            string user = User; var config = ItemGiftsManager.Instance.CurrentConfig?.config; var inventory = ItemsManager.Instance.CurrentInventory;
            var item = inventory?.instances.FirstOrDefault(x => x.instanceId == instanceId);
            if (user == null || config?.enabled != true || item == null || item.equipped || string.IsNullOrEmpty(recipientId) || recipientId == user) return Failed("REVIEW_REQUIRED");
            _review = new Review { scope = Scope, recipient = recipientId, instance = instanceId, inventoryRevision = inventory.revision, configRevision = config.revision };
            ReviewedFee = config.fee; ReviewedCurrencyKey = config.currencyKey; LastErrorCode = null; return true;
        }
        public async Task<bool> ConfirmReviewedSend()
        {
            string user = User;
            if (_busy || user == null || _review == null || _review.scope != Scope) return Failed("REVIEW_REQUIRED");
            long now = ItemGiftsManager.Instance.ServerNowMilliseconds; if (now < 0) return Failed("REVIEW_REQUIRED");
            string issued = DateTimeOffset.FromUnixTimeMilliseconds(now).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            var terms = _review;
            var original = ItemGiftIntent.Send(user, Guid.NewGuid().ToString("D"), issued, terms.recipient, terms.instance, terms.inventoryRevision, terms.configRevision);
            _busy = true;
            try { using (var lease = Store(user).BeginNew(original)) return await Apply(lease, false); }
            finally { _busy = false; }
        }
        public Task<bool> RetryPending() => Resume(false);
        public Task<bool> RecoverOriginalOperation() => Resume(true);
        private async Task<bool> Resume(bool lookupOnly)
        {
            string user = User; if (_busy || user == null) return Failed("SESSION_REQUIRED");
            _busy = true;
            try { using (var lease = Store(user).AcquireSend()) return await Apply(lease, lookupOnly); }
            finally { _busy = false; }
        }
        private async Task<bool> Apply(ItemGiftsSampleStore.SendLease lease, bool lookupOnly)
        {
            string scope = Scope; var network = HorizonApp.Network; var original = lease.Original.Intent; string user = original.UserId;
            if (!lease.Original.Confirmed)
            {
                ItemGiftReceipt receipt;
                if (lookupOnly)
                {
                    var found = await ItemGiftsManager.Instance.GetOperation(user, original.OperationId);
                    if (!Current(scope, network)) return Failed("STALE_RESPONSE");
                    if (!found.IsSuccess) return Failed(found.ErrorCode); receipt = found.Data.receipt;
                }
                else
                {
                    var written = await ItemGiftsManager.Instance.SubmitPrepared(original);
                    if (!Current(scope, network)) return Failed("STALE_RESPONSE");
                    if (!written.IsSuccess) return Failed(written.ErrorCode); receipt = written.Data.receipt;
                }
                // The matching identity is flushed before the first fallible current-state read. Never archive item or fee results.
                lease.MarkConfirmed(receipt);
            }
            var inventory = await ItemsManager.Instance.GetInventory(user);
            if (!Current(scope, network)) return Failed("STALE_RESPONSE"); if (!inventory.IsSuccess) return Failed(inventory.ErrorCode);
            var values = await PlayerValuesManager.Instance.GetState(user);
            if (!Current(scope, network)) return Failed("STALE_RESPONSE"); if (!values.IsSuccess) return Failed(values.ErrorCode);
            var gifts = await ItemGiftsManager.Instance.GetState(user);
            if (!Current(scope, network)) return Failed("STALE_RESPONSE"); if (!gifts.IsSuccess) return Failed(gifts.ErrorCode);
            lease.AcknowledgeAfterRefresh(); _review = null; LastErrorCode = null; return true;
        }
        /// <summary>Declines a received offer at the revision shown in the last gift state read. The item returns to the sender.</summary>
        public async Task<bool> DeclineGift(string giftId)
        {
            string user = User; var offer = ItemGiftsManager.Instance.CurrentState?.state.received.FirstOrDefault(x => x.id == giftId);
            if (user == null || offer == null) return Failed("REVIEW_REQUIRED");
            var declined = await ItemGiftsManager.Instance.Decline(user, giftId, offer.revision);
            if (!declined.IsSuccess) return Failed(declined.ErrorCode);
            LastErrorCode = null; return true;
        }
        /// <summary>Claims a gift offer or a returned gift through the original Inbox entry. Refresh the inventory afterwards.</summary>
        public Task<InboxClaimResult> ClaimGiftEntry(string entryId) => InboxManager.Instance.Claim(User, entryId);
    }
}
