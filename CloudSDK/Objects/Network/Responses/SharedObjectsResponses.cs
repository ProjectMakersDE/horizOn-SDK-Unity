using System;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    public enum SharedScalarType { Integer, String }
    public enum SharedQueryMode { List, Owner, Field, Rectangle, Random }
    [Serializable] public class SharedScalar { public SharedScalarType type; public long integerValue; public string stringValue; }
    [Serializable] public class SharedSearchValue { public string key; public SharedScalar value; }
    [Serializable] public class SharedSearchField { public string key, type; }
    [Serializable] public class SharedPosition { public long x, y; }
    [Serializable] public class SharedLimits { public int collections, objectBytes; public long storageBytes; }
    [Serializable] public class SharedDefinition
    {
        public string key, name, readPolicy, writePolicy;
        public bool active, positionEnabled, used;
        public int maxObjectBytes, maxObjectsPerOwner;
        public long? ttlSeconds;
        public long revision;
        public SharedSearchField[] searchFields = Array.Empty<SharedSearchField>();
    }
    [Serializable] public class SharedCollectionPage
    {
        public long revision;
        public SharedDefinition[] definitions = Array.Empty<SharedDefinition>();
        public string nextAfterKey, serverTime;
        public SharedLimits limits;
    }
    [Serializable] public class SharedUsage
    {
        public string userId, collection, serverTime;
        public long revision;
        public int count, pollAfterSeconds;
        public SharedLimits limits;
    }
    [Serializable] public class SharedObject
    {
        public string id, collection, ownerId, groupId, dataBase64, createdAt, updatedAt, expiresAt;
        public long revision;
        /// <summary>Decoded opaque bytes; never parsed as game JSON by the SDK.</summary>
        public byte[] data = Array.Empty<byte>();
        public SharedSearchValue[] search = Array.Empty<SharedSearchValue>();
        public SharedPosition position;
    }
    [Serializable] public class SharedObjectRead { public SharedObject @object; public int pollAfterSeconds; public string serverTime; }
    [Serializable] public class SharedQueryResult
    {
        public SharedObject[] objects = Array.Empty<SharedObject>();
        public string nextCursor, serverTime;
        public int pollAfterSeconds;
    }
    [Serializable] public class SharedWriteResult { public SharedObject @object; public SharedUsage usage; public int pollAfterSeconds; public string serverTime; }
    [Serializable] public class SharedDeleteResult { public string objectId; public bool deleted; public SharedUsage usage; public int pollAfterSeconds; public string serverTime; }
    /// <summary>The manager snapshots all input fields before queueing or sending.</summary>
    [Serializable] public class SharedObjectInput
    {
        public byte[] data = Array.Empty<byte>();
        public SharedSearchValue[] search = Array.Empty<SharedSearchValue>();
        public SharedPosition position;
    }
    /// <summary>Only fields belonging to the selected mode are sent. Cursor remains opaque.</summary>
    [Serializable] public class SharedQuery
    {
        public SharedQueryMode mode;
        public string ownerId, groupId, cursor, field;
        public int limit = 20;
        public SharedScalar equal, min, max;
        public long minX, maxX, minY, maxY;
    }
    public class SharedObjectsResult<T> where T : class
    {
        public bool IsSuccess { get; internal set; }
        public T Data { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
        /// <summary>Opaque, scope-bound identity for local cache/cadence lookups. Never grants authorization.</summary>
        public string Identity { get; internal set; }
        public double RetryAfterSeconds { get; internal set; }
    }
    internal sealed class SharedObjectsJsonRequest
    {
        internal readonly string Json;
        internal SharedObjectsJsonRequest(string json) { Json = json; }
    }
}
