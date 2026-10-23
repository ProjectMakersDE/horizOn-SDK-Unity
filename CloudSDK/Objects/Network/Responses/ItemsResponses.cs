using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    public enum ItemPropertyType { Integer, Boolean, String }
    /// <summary>A flat JSON property. Only the field selected by type contains its value; strings preserve Unicode and embedded NUL.</summary>
    [Serializable] public class ItemProperty
    {
        public string key;
        public ItemPropertyType type;
        public long integerValue;
        public bool booleanValue;
        public string stringValue;
    }
    [Serializable] public class ItemPropertyDelta { public string key; public long amount; }
    [Serializable] public class ItemPropertyRule { public string key; public long min, max, maxChangePerCall; }
    [Serializable] public class ItemLimits { public int catalog, inventory, receipts; }
    [Serializable] public class ItemDefinition
    {
        public string key, name, type, rarity;
        public bool active;
        public ItemProperty[] baseProperties = Array.Empty<ItemProperty>();
        public ItemPropertyRule[] propertyRules = Array.Empty<ItemPropertyRule>();
    }
    [Serializable] public class ItemInstance
    {
        public string instanceId, itemKey, createdAt;
        public ItemProperty[] properties = Array.Empty<ItemProperty>();
        public bool equipped;
    }
    [Serializable] public class InventorySnapshot
    {
        public string userId, serverTime;
        public long revision;
        public ItemInstance[] instances = Array.Empty<ItemInstance>();
        public ItemLimits limits;
    }
    [Serializable] public class ItemCatalogPage
    {
        public long revision;
        public ItemDefinition[] definitions = Array.Empty<ItemDefinition>();
        public string nextAfterKey, serverTime;
        public int total;
        public ItemLimits limits;
    }
    [Serializable] public class ItemReward { public string itemKey; public int quantity; }
    [Serializable] public class ItemReference { public string instanceId, itemKey; }
    public class ItemsResult<T> where T : class
    {
        public bool IsSuccess { get; internal set; }
        public T Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }
    internal sealed class ItemsJsonRequest
    {
        internal readonly string Json;
        internal ItemsJsonRequest(string json) { Json = json; }
    }
}
