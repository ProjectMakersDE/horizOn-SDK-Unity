using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>Explicit inventory/catalog reads and single-attempt revision-fenced writes. No grant, polling or automatic pages.</summary>
    public class ItemsManager : BaseManager<ItemsManager>
    {
        private const string Endpoint = "/api/v1/app/items";
        private readonly SemaphoreSlim _requests = new SemaphoreSlim(1, 1);
        private NetworkService _network;
        private string _identity;
        private long _epoch, _inventoryRevision = -1, _inventoryTime = -1, _catalogRevision = -1, _catalogTime = -1;
        private InventorySnapshot _inventory;
        private ItemCatalogPage _catalog;
        public string LastErrorCode { get; private set; }
        public InventorySnapshot CurrentInventory { get { SyncIdentity(); return ItemsJson.Clone(_inventory); } }
        public ItemCatalogPage CurrentCatalogPage { get { SyncIdentity(); return ItemsJson.Clone(_catalog); } }
        protected override void RegisterEvents()
        {
            RegisterEvent<DateTime>(EventKeys.UserSignOutSuccess, _ => ResetIdentity());
            RegisterEvent<UserData>(EventKeys.UserSignInSuccess, _ => SyncIdentity());
        }
        protected override void OnDisable() { ResetIdentity(); base.OnDisable(); }
        /// <summary>Explicit invalidation preserves independent revision/server-time fences until identity changes.</summary>
        public void ClearCache() { SyncIdentity(); _epoch++; _inventory = null; _catalog = null; }
        public Task<ItemsResult<InventorySnapshot>> GetInventory(string userId) => InventoryCall(userId, null, null, null);
        public async Task<ItemsResult<ItemCatalogPage>> GetCatalog(string userId, string afterKey = null, int limit = 50, long? catalogRevision = null)
        {
            if (!Begin(userId, out var identity, out var epoch)) return Failure<ItemCatalogPage>(LastErrorCode);
            if (afterKey != null && !ItemsJson.Key(afterKey) || limit < 1 || limit > 50 || catalogRevision.HasValue && !ItemsJson.Revision(catalogRevision.Value)) return Failure<ItemCatalogPage>("INVALID_PAGE");
            string path = Endpoint + "/catalog?userId=" + Uri.EscapeDataString(userId) + "&limit=" + limit.ToString(CultureInfo.InvariantCulture)
                + (afterKey == null ? "" : "&afterKey=" + Uri.EscapeDataString(afterKey))
                + (catalogRevision.HasValue ? "&catalogRevision=" + catalogRevision.Value.ToString(CultureInfo.InvariantCulture) : "");
            await _requests.WaitAsync();
            try
            {
                if (!IsCurrent(identity, epoch)) return Failure<ItemCatalogPage>("STALE_RESPONSE");
                var response = await HorizonApp.Network.GetAsync<ItemCatalogPage>(path, true);
                if (!IsCurrent(identity, epoch)) return Failure<ItemCatalogPage>("STALE_RESPONSE");
                if (!response.IsSuccess) return Failure<ItemCatalogPage>(ErrorCode(response), response.Error);
                var page = response.Data;
                if (page == null || page.definitions.Length > limit || page.total < page.definitions.Length || catalogRevision.HasValue && page.revision != catalogRevision.Value) return Failure<ItemCatalogPage>("INVALID_RESPONSE");
                string previous = afterKey;
                foreach (var definition in page.definitions)
                {
                    if (previous != null && string.CompareOrdinal(definition.key, previous) <= 0) return Failure<ItemCatalogPage>("INVALID_RESPONSE");
                    previous = definition.key;
                }
                if (page.nextAfterKey != null && (page.definitions.Length == 0 || page.nextAfterKey != previous)) return Failure<ItemCatalogPage>("INVALID_RESPONSE");
                long time = Millis(page.serverTime);
                if (page.revision < _catalogRevision || time < _catalogTime) return Failure<ItemCatalogPage>("STALE_RESPONSE");
                _catalogRevision = page.revision; _catalogTime = time; _catalog = ItemsJson.Clone(page); LastErrorCode = null;
                return new ItemsResult<ItemCatalogPage> { IsSuccess = true, Data = ItemsJson.Clone(page) };
            }
            finally { _requests.Release(); }
        }
        public Task<ItemsResult<InventorySnapshot>> AdjustProperties(string userId, string instanceId, long expectedRevision, IList<ItemPropertyDelta> changes)
        {
            if (changes == null || changes.Count < 1 || changes.Count > 8) return Task.FromResult(Failure<InventorySnapshot>("INVALID_REQUEST"));
            var keys = new HashSet<string>(StringComparer.Ordinal); var frozen = new List<string>();
            foreach (var change in changes)
            {
                if (change == null || !ItemsJson.Key(change.key) || !keys.Add(change.key) || change.amount == 0 || !ItemsJson.Safe(change.amount)) return Task.FromResult(Failure<InventorySnapshot>("INVALID_REQUEST"));
                frozen.Add("{\"key\":" + ItemsJson.Quote(change.key) + ",\"amount\":" + change.amount.ToString(CultureInfo.InvariantCulture) + "}");
            }
            return Mutation(userId, instanceId, expectedRevision, "/adjust", ",\"changes\":[" + string.Join(",", frozen) + "]");
        }
        public Task<ItemsResult<InventorySnapshot>> SetEquipped(string userId, string instanceId, long expectedRevision, bool equipped)
            => Mutation(userId, instanceId, expectedRevision, "/equip", ",\"equipped\":" + (equipped ? "true" : "false"));
        public Task<ItemsResult<InventorySnapshot>> DiscardItem(string userId, string instanceId, long expectedRevision)
            => Mutation(userId, instanceId, expectedRevision, "/discard", "");
        private Task<ItemsResult<InventorySnapshot>> Mutation(string user, string instance, long revision, string route, string fields)
        {
            if (!Begin(user, out _, out _)) return Task.FromResult(Failure<InventorySnapshot>(LastErrorCode));
            if (!FriendsJson.IsUuid(instance) || !ItemsJson.Revision(revision)) return Task.FromResult(Failure<InventorySnapshot>("INVALID_REQUEST"));
            string body = "{\"userId\":" + ItemsJson.Quote(user) + ",\"instanceId\":" + ItemsJson.Quote(instance)
                + ",\"expectedRevision\":" + revision.ToString(CultureInfo.InvariantCulture) + fields + "}";
            return InventoryCall(user, route, new ItemsJsonRequest(body), revision);
        }
        private async Task<ItemsResult<InventorySnapshot>> InventoryCall(string user, string route, ItemsJsonRequest body, long? expectedRevision)
        {
            if (!Begin(user, out var identity, out var epoch)) return Failure<InventorySnapshot>(LastErrorCode);
            await _requests.WaitAsync();
            try
            {
                if (!IsCurrent(identity, epoch)) return Failure<InventorySnapshot>("STALE_RESPONSE");
                if (route != null) _inventory = null;
                var response = route == null ? await HorizonApp.Network.GetAsync<InventorySnapshot>(Endpoint + "?userId=" + Uri.EscapeDataString(user), true)
                    : await HorizonApp.Network.PostOnceAsync<InventorySnapshot>(Endpoint + route, body, true);
                if (!IsCurrent(identity, epoch)) return Failure<InventorySnapshot>("STALE_RESPONSE");
                if (!response.IsSuccess) return Failure<InventorySnapshot>(ErrorCode(response), response.Error);
                var snapshot = response.Data;
                if (snapshot == null || !string.Equals(snapshot.userId, user, StringComparison.OrdinalIgnoreCase) || expectedRevision.HasValue && snapshot.revision < expectedRevision.Value) return Failure<InventorySnapshot>("INVALID_RESPONSE");
                long time = Millis(snapshot.serverTime);
                if (snapshot.revision < _inventoryRevision || time < _inventoryTime) return Failure<InventorySnapshot>("STALE_RESPONSE");
                _inventoryRevision = snapshot.revision; _inventoryTime = time; _inventory = ItemsJson.Clone(snapshot); LastErrorCode = null;
                return new ItemsResult<InventorySnapshot> { IsSuccess = true, Data = ItemsJson.Clone(snapshot) };
            }
            finally { _requests.Release(); }
        }
        private static long Millis(string time) => DateTimeOffset.Parse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUnixTimeMilliseconds();
        private bool Begin(string user, out string identity, out long epoch)
        {
            SyncIdentity(); identity = _identity; epoch = _epoch; var current = UserManager.Instance.CurrentUser;
            LastErrorCode = current == null || !current.IsValid() || string.IsNullOrEmpty(_network.GetSessionToken()) || current.AccessToken != _network.GetSessionToken() ? "SESSION_REQUIRED"
                : !string.Equals(current.UserId, user, StringComparison.OrdinalIgnoreCase) ? "SESSION_FORBIDDEN" : !FriendsJson.IsUuid(user) ? "INVALID_REQUEST" : null;
            return LastErrorCode == null;
        }
        private void SyncIdentity()
        {
            var network = HorizonApp.Network;
            string identity = UserManager.Instance.CurrentUser?.UserId + "|" + network.GetSessionToken() + "|" + network.GetApiKey() + "|" + network.GetActiveHost() + "|" + network.ContextGeneration;
            if (ReferenceEquals(network, _network) && identity == _identity) return;
            ResetIdentity(); _network = network; _identity = identity;
        }
        private void ResetIdentity() { _epoch++; _network = null; _identity = null; _inventory = null; _catalog = null; _inventoryRevision = _inventoryTime = _catalogRevision = _catalogTime = -1; LastErrorCode = null; }
        private bool IsCurrent(string identity, long epoch) { SyncIdentity(); return _identity == identity && _epoch == epoch; }
        private static string ErrorCode<T>(NetworkResponse<T> response) where T : class => response.ErrorCode ?? (response.StatusCode >= 200 && response.StatusCode < 300 ? "INVALID_RESPONSE" : "HTTP_" + response.StatusCode);
        private ItemsResult<T> Failure<T>(string code, string error = null) where T : class { LastErrorCode = code; return new ItemsResult<T> { ErrorCode = code, Error = error ?? code }; }
    }
}
