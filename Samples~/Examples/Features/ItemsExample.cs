using System;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using UnityEngine;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Explicit screen/button actions for an existing session: one catalog page, reviewed purchase, inventory, equip/adjust/discard.
    /// Persist before HTTP. Unknown outcomes retain their entire original command; confirmed purchases recover by inventory GET only.</summary>
    public class ItemsExample : MonoBehaviour
    {
        [SerializeField] private string projectId;
        private InventorySnapshot _reviewed;
        private string _reviewScope, _offersScope, _catalogScope, _inventoryReadForHash, _acknowledgeHash, _acknowledgeScope;
        private bool _busy;
        private int _generation;
        private string User => isActiveAndEnabled && UserManager.Instance.CurrentUser?.IsValid() == true && UserManager.Instance.CurrentUser.AccessToken == HorizonApp.Network.GetSessionToken() ? UserManager.Instance.CurrentUser.UserId : null;
        private string Scope => projectId + "|" + User + "|" + HorizonApp.Network.GetApiKey() + "|" + HorizonApp.Network.GetActiveHost() + "|" + HorizonApp.Network.GetSessionToken() + "|" + HorizonApp.Network.ContextGeneration + "|" + _generation;
        private ItemsSampleStore Store(string user) => new ItemsSampleStore(Application.persistentDataPath, projectId, user, HorizonApp.Network.GetApiKey());
        public async Task<bool> RefreshInventory()
        {
            string user = User; if (user == null) return false; string scope = Scope; var network = HorizonApp.Network;
            var result = await ItemsManager.Instance.GetInventory(user);
            if (!Current(scope, network)) return false;
            _reviewed = result.IsSuccess ? result.Data : null; _reviewScope = result.IsSuccess ? scope : null;
            if (!result.IsSuccess) HorizonApp.Log.Warning("Inventory: " + result.ErrorCode);
            return result.IsSuccess;
        }
        public async Task<bool> ReadOneCatalogPage(string afterKey = null, long? catalogRevision = null)
        {
            string user = User; if (user == null) return false; string scope = Scope; var network = HorizonApp.Network;
            var result = await ItemsManager.Instance.GetCatalog(user, afterKey, 50, catalogRevision);
            if (!Current(scope, network)) return false;
            _catalogScope = result.IsSuccess ? scope : null;
            if (!result.IsSuccess) HorizonApp.Log.Warning("Explicitly restart catalog pages after a conflict: " + result.ErrorCode);
            return result.IsSuccess;
        }
        public async Task<bool> ReviewOffers()
        {
            string user = User; if (user == null) return false; string scope = Scope; var network = HorizonApp.Network;
            if (!(await PlayerValuesManager.Instance.GetState(user)).IsSuccess || !Current(scope, network)) return false;
            var offers = await PlayerValuesManager.Instance.GetOffers(user);
            bool valid = Current(scope, network) && offers.IsSuccess; _offersScope = valid ? scope : null; return valid;
        }
        public async Task<bool> SavePurchaseAfterReview(string offerKey)
        {
            string user = User; if (user == null || _busy) return false;
            var offer = PlayerValuesManager.Instance.CurrentOffers?.offers.FirstOrDefault(x => x.key == offerKey);
            if (_offersScope != Scope || offer == null || !offer.active || offer.itemRewards.Length == 0 || !PlayerValuesManager.Instance.HasServerClock) return false;
            var catalog = ItemsManager.Instance.CurrentCatalogPage;
            if (_catalogScope != Scope || catalog == null || !offer.itemRewards.All(r => catalog.definitions.Any(d => d.key == r.itemKey && d.active))) return false;
            var store = Store(user);
            store.SaveNew(new ItemsSampleStore.Intent { projectId = projectId, userId = user, action = "purchase", offerKey = offer.key,
                operationId = PlayerValuesManager.Instance.CreateOperationId(), offerHash = OfferHash(offer) });
            return await ApplySavedIntent();
        }
        public Task<bool> SaveEquipAfterReview(string instanceId, bool equipped) => SaveInventoryAction("equip", instanceId, equipped, null);
        public Task<bool> SaveAdjustAfterReview(string instanceId, string key, long amount)
        {
            var instance = _reviewed?.instances.FirstOrDefault(x => x.instanceId == instanceId);
            if (key == null || !Regex.IsMatch(key, @"\A[a-z][a-z0-9_]{0,63}\z") || amount == 0 || amount < -PlayerValuesManager.MaxSafeInteger || amount > PlayerValuesManager.MaxSafeInteger || instance == null || !instance.properties.Any(x => x.key == key && x.type == ItemPropertyType.Integer)) return Task.FromResult(false);
            var definition = ItemsManager.Instance.CurrentCatalogPage?.definitions.FirstOrDefault(d => d.key == instance.itemKey && d.active);
            var rule = definition?.propertyRules.FirstOrDefault(r => r.key == key);
            long value = instance.properties.First(x => x.key == key).integerValue;
            if (_catalogScope != Scope || rule == null || amount < -rule.maxChangePerCall || amount > rule.maxChangePerCall || value + amount < rule.min || value + amount > rule.max) return Task.FromResult(false);
            return SaveInventoryAction("adjust", instanceId, false, new[] { new ItemPropertyDelta { key = key, amount = amount } });
        }
        /// <summary>Call only after an explicit destructive confirmation in the game's UI.</summary>
        public Task<bool> SaveDiscardAfterReview(string instanceId) => SaveInventoryAction("discard", instanceId, false, null);
        private async Task<bool> SaveInventoryAction(string action, string instance, bool equipped, ItemPropertyDelta[] changes)
        {
            string user = User;
            if (user == null || _busy || _reviewed == null || _reviewScope != Scope || ItemsManager.Instance.CurrentInventory?.revision != _reviewed.revision || !_reviewed.instances.Any(x => x.instanceId == instance)) return false;
            Store(user).SaveNew(new ItemsSampleStore.Intent { projectId = projectId, userId = user, action = action, instanceId = instance, revision = _reviewed.revision, equipped = equipped, changes = changes });
            return await ApplySavedIntent();
        }
        /// <param name="confirmOfferChanged">Explicit game/player acknowledgement that the offer changed or is unavailable.
        /// Replays the original ID so the server can return a retained receipt before checking today's offer.</param>
        public async Task<bool> ApplySavedIntent(bool confirmOfferChanged = false)
        {
            string user = User; if (user == null || _busy) return false;
            var store = Store(user); if (!store.HasIntent) return false; _busy = true;
            try
            {
                using (var lease = store.AcquireSend())
                {
                    var prepared = lease.Prepared; var x = prepared.Value; string scope = Scope; var network = HorizonApp.Network;
                    if (prepared.HasSuccess)
                    {
                        var receipt = lease.ReadSuccess();
                        if (receipt.action != "purchase") { _acknowledgeHash = prepared.Hash; _acknowledgeScope = scope; return true; }
                        // The purchase was confirmed durably, even if a previous GET failed or this actor restarted.
                        bool loaded = await RefreshInventory();
                        if (loaded && Current(scope, network)) { _inventoryReadForHash = _acknowledgeHash = prepared.Hash; _acknowledgeScope = scope; }
                        return loaded;
                    }
                    if (x.action == "purchase")
                    {
                        var offers = await PlayerValuesManager.Instance.GetOffers(user);
                        if (!Current(scope, network)) return false;
                        var offer = offers.IsSuccess ? offers.Data.offers.FirstOrDefault(o => o.key == x.offerKey) : null;
                        if ((offer == null || !offer.active || OfferHash(offer) != x.offerHash) && !confirmOfferChanged)
                        { HorizonApp.Log.Warning("Offer changed or unavailable. Keep original ID; confirm the changed terms/replay explicitly."); return false; }
                        var result = await PlayerValuesManager.Instance.Purchase(user, x.offerKey, x.operationId);
                        if (!result.IsSuccess) { HorizonApp.Log.Warning("Keep original purchase, including expired/uncertain results: " + result.ErrorCode); return false; }
                        if (result.State?.operation?.operationId != x.operationId) throw new InvalidOperationException("Purchase echoed another operation; original retained");
                        // Write and flush the matching receipt BEFORE the fallible inventory GET. No local item synthesis.
                        lease.SaveSuccess(x.operationId, result.State.operation.items);
                        if (!Current(scope, network)) return false;
                        bool loaded = await RefreshInventory(); if (loaded && Current(scope, network)) { _inventoryReadForHash = _acknowledgeHash = prepared.Hash; _acknowledgeScope = scope; }
                        return loaded;
                    }
                    ItemsResult<InventorySnapshot> changed;
                    switch (x.action)
                    {
                        case "equip": changed = await ItemsManager.Instance.SetEquipped(user, x.instanceId, x.revision, x.equipped); break;
                        case "adjust": changed = await ItemsManager.Instance.AdjustProperties(user, x.instanceId, x.revision, x.changes); break;
                        case "discard": changed = await ItemsManager.Instance.DiscardItem(user, x.instanceId, x.revision); break;
                        default: throw new InvalidOperationException("Unknown original Items action");
                    }
                    if (!changed.IsSuccess) { HorizonApp.Log.Warning("Keep original instance/revision and reconcile: " + changed.ErrorCode); return false; }
                    lease.SaveSuccess(null, null);
                    if (!Current(scope, network)) return false;
                    _reviewed = changed.Data; _reviewScope = _acknowledgeScope = scope; _acknowledgeHash = prepared.Hash; return true;
                }
            }
            finally { _busy = false; }
        }
        public void AcknowledgeSavedSuccess()
        {
            string user = User; if (user == null || _busy) return;
            using (var lease = Store(user).AcquireSend())
            {
                var original = lease.Prepared; var receipt = lease.ReadSuccess();
                if (_acknowledgeHash != original.Hash || _acknowledgeScope != Scope) throw new InvalidOperationException("Review the exact current original result before acknowledging");
                if (receipt.action == "purchase" && (_inventoryReadForHash != original.Hash || _reviewScope != Scope || ItemsManager.Instance.CurrentInventory == null)) throw new InvalidOperationException("Refresh confirmed purchase inventory before acknowledging");
                lease.AcknowledgeSavedSuccess(); _inventoryReadForHash = _acknowledgeHash = _acknowledgeScope = null;
            }
        }
        /// <summary>Explicit player/game decision after inspecting refreshed inventory against a saved equip/adjust/discard.
        /// Archives the uncertainty as a reconciliation, never claims the write succeeded and never closes an uncertain purchase.</summary>
        public bool ReconcileSavedMutationAfterReview()
        {
            string user = User;
            if (user == null || _busy || _reviewed == null || _reviewScope != Scope || ItemsManager.Instance.CurrentInventory?.revision != _reviewed.revision) return false;
            var store = Store(user); if (!store.HasIntent || store.HasSuccess) return false;
            using (var lease = store.AcquireSend())
            {
                var original = lease.Prepared; if (original.Value.action == "purchase" || original.HasSuccess || _reviewed.revision <= original.Value.revision) return false;
                lease.SaveReconciledOriginal(_reviewed.revision);
                _acknowledgeHash = original.Hash; _acknowledgeScope = Scope;
                HorizonApp.Log.Warning("Original Items mutation reconciled by player review; no server success inferred");
                return true;
            }
        }
        private bool Current(string scope, NetworkService network) => User != null && scope == Scope && ReferenceEquals(network, HorizonApp.Network);
        private static string OfferHash(PlayerValueOffer offer)
        {
            // Length framing preserves embedded NUL/Unicode names without Unity's native string serializer.
            var b = new StringBuilder(); Action<string> add = s => b.Append(s.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(s);
            add(offer.key); add(offer.name); add(offer.active ? "true" : "false"); add(offer.purchasesPerDay.ToString(CultureInfo.InvariantCulture));
            foreach (var list in new[] { offer.costs, offer.rewards }) { add(list.Length.ToString(CultureInfo.InvariantCulture)); foreach (var value in list) { add(value.key); add(value.amount.ToString(CultureInfo.InvariantCulture)); } }
            add(offer.itemRewards.Length.ToString(CultureInfo.InvariantCulture)); foreach (var reward in offer.itemRewards) { add(reward.itemKey); add(reward.quantity.ToString(CultureInfo.InvariantCulture)); }
            return ItemsSampleStore.Hash(b.ToString());
        }
        private void OnDisable() { _generation++; _reviewed = null; _reviewScope = _offersScope = _catalogScope = null; _inventoryReadForHash = _acknowledgeHash = _acknowledgeScope = null; }
    }
}
