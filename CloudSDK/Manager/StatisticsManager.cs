using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;
using PM.horizOn.Cloud.Transport;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>One report per session/batch. Calls are explicit; sign-in never fetches statistics.</summary>
    public class StatisticsManager : BaseManager<StatisticsManager>
    {
        private StatisticsSnapshot _current;
        private StatisticDefinition[] _definitions;
        private long _definitionVersion = -1;
        private long _clockOffset;
        private long _maxObservedRevision = -1;
        private long _lastObservedServerTime;
        private long _cacheEpoch;
        private string _userId, _token, _host;

        public string LastErrorCode { get; private set; }
        /// <summary>Keep this ID and the original batch after a timeout; never replace it automatically.</summary>
        public string LastSubmittedOperationId { get; private set; }
        public StatisticsSnapshot CurrentStatistics { get { EnsureScope(); return _current; } }
        public StatisticDefinition[] CurrentDefinitions { get { EnsureScope(); return _definitions; } }

        /// <summary>Create once, persist with the batch, and use the same ID on every retry.</summary>
        public string CreateOperationId()
        {
            EnsureScope();
            return StatisticsTransportContract.CreateOperationId(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _clockOffset);
        }

        public async Task<StatisticsSnapshot> GetStatistics()
        {
            EnsureScope();
            var user = UserManager.Instance.CurrentUser;
            if (!StatisticsTransportContract.HasSession(user, _token)) return Fail<StatisticsSnapshot>(StatisticsErrorCodes.SessionRequired);
            string owner = _userId, token = _token, host = _host;
            long epoch = _cacheEpoch;
            var response = await HorizonApp.Network.GetAsync<StatisticsSnapshot>(
                StatisticsTransportContract.GetEndpoint(user, KnownVersion), useSessionToken: true);
            if (!SameScope(owner, token, host, epoch)) return Fail<StatisticsSnapshot>(LastErrorCode);
            if (!response.IsSuccess) return NetworkFailure(response);
            return Accept(response.Data);
        }

        public async Task<StatisticsDefinitionsResponse> GetDefinitions()
        {
            EnsureScope();
            if (!StatisticsTransportContract.HasSession(UserManager.Instance.CurrentUser, _token)) return Fail<StatisticsDefinitionsResponse>(StatisticsErrorCodes.SessionRequired);
            string owner = _userId, token = _token, host = _host;
            long epoch = _cacheEpoch;
            var response = await HorizonApp.Network.GetAsync<StatisticsDefinitionsResponse>(
                StatisticsTransportContract.DefinitionsEndpoint(KnownVersion), useSessionToken: true);
            if (!SameScope(owner, token, host, epoch)) return Fail<StatisticsDefinitionsResponse>(LastErrorCode);
            if (!response.IsSuccess) return NetworkFailure(response);
            if (response.Data == null || !AcceptDefinitions(response.Data.definitionVersion, response.Data.definitions))
                return Fail<StatisticsDefinitionsResponse>(StatisticsErrorCodes.InvalidResponse);
            response.Data.definitions = _definitions;
            LastErrorCode = null;
            return response.Data;
        }

        /// <summary>
        /// Does not create IDs or change values optimistically. After an uncertain response load a
        /// snapshot and retry the original ID/batch. Older evicted IDs can be rejected as expired.
        /// </summary>
        public async Task<StatisticsSnapshot> UpdateStatistics(IList<StatisticUpdate> updates, string operationId)
        {
            EnsureScope();
            if (!StatisticsTransportContract.TryUpdate(UserManager.Instance.CurrentUser, _token, updates, operationId, out var body, out var error))
                return Fail<StatisticsSnapshot>(error);
            string owner = _userId, token = _token, host = _host;
            long epoch = _cacheEpoch;
            LastSubmittedOperationId = operationId;
            var response = await HorizonApp.Network.PostAsync<StatisticsSnapshot>(StatisticsTransportContract.Endpoint, body, useSessionToken: true);
            if (!SameScope(owner, token, host, epoch)) return Fail<StatisticsSnapshot>(LastErrorCode);
            if (!response.IsSuccess) return NetworkFailure(response);
            if (response.Data == null || response.Data.operationId != operationId) return Fail<StatisticsSnapshot>(StatisticsErrorCodes.InvalidResponse);
            return Accept(response.Data);
        }

        public void ClearCache()
        {
            ClearData();
            HorizonApp.Events.Publish(EventKeys.CacheCleared, "Statistics");
        }

        private long? KnownVersion => _definitions == null ? (long?)null : _definitionVersion;
        private void ClearData()
        {
            _current = null; _definitions = null; _definitionVersion = -1; _clockOffset = 0;
            _maxObservedRevision = -1; _lastObservedServerTime = 0; _cacheEpoch++;
            LastErrorCode = null; LastSubmittedOperationId = null;
        }
        private void EnsureScope()
        {
            string user = UserManager.Instance.CurrentUser?.UserId;
            string token = HorizonApp.Network.GetSessionToken();
            string host = HorizonApp.Network.GetActiveHost();
            if (_userId != user || _token != token || _host != host)
            {
                ClearData(); _userId = user; _token = token; _host = host;
            }
        }
        private bool SameScope(string user, string token, string host, long epoch)
        {
            EnsureScope();
            if (_userId != user || _token != token || _host != host)
            {
                LastErrorCode = StatisticsErrorCodes.SessionChanged;
                return false;
            }
            if (epoch != _cacheEpoch)
            {
                LastErrorCode = StatisticsErrorCodes.StateConflict;
                return false;
            }
            return true;
        }
        private bool AcceptDefinitions(long version, StatisticDefinition[] definitions)
        {
            // JsonUtility materializes a JSON null array as an empty array. Equal catalog
            // versions cannot change their contents, so an empty conditional result keeps it.
            if (version == _definitionVersion && _definitions != null && (definitions == null || definitions.Length == 0)) definitions = _definitions;
            if (version < 0 || version < _definitionVersion || (definitions == null && (version != _definitionVersion || _definitions == null))) return false;
            if (version > _definitionVersion) _current = null;
            _definitionVersion = version;
            if (definitions != null) _definitions = definitions;
            return true;
        }
        private StatisticsSnapshot Accept(StatisticsSnapshot snapshot)
        {
            if (snapshot == null || !string.Equals(snapshot.userId, _userId, StringComparison.OrdinalIgnoreCase) || snapshot.values == null
                || snapshot.operationWindow == null || snapshot.revision < 0 || snapshot.operationWindow.serverTimeUnixMillis < 1000000000000L
                || snapshot.operationWindow.serverTimeUnixMillis > 9999999999999L || snapshot.operationWindow.maxAgeSeconds <= 0
                || snapshot.operationWindow.maxFutureSkewSeconds < 0)
                return Fail<StatisticsSnapshot>(StatisticsErrorCodes.InvalidResponse);
            if (snapshot.definitionVersion < _definitionVersion || snapshot.revision < _maxObservedRevision
                || snapshot.operationWindow.serverTimeUnixMillis < _lastObservedServerTime)
                return Fail<StatisticsSnapshot>(StatisticsErrorCodes.StateConflict);
            if (!AcceptDefinitions(snapshot.definitionVersion, snapshot.definitions)) return Fail<StatisticsSnapshot>(StatisticsErrorCodes.InvalidResponse);
            if (snapshot.values.Length != _definitions.Length) return Fail<StatisticsSnapshot>(StatisticsErrorCodes.InvalidResponse);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in snapshot.values)
                if (value == null || !StatisticsTransportContract.ValidKey(value.key) || !keys.Add(value.key)
                    || value.value < -StatisticsTransportContract.MaxValue || value.value > StatisticsTransportContract.MaxValue)
                    return Fail<StatisticsSnapshot>(StatisticsErrorCodes.InvalidResponse);
            foreach (var definition in _definitions)
                if (definition == null || !keys.Contains(definition.key)) return Fail<StatisticsSnapshot>(StatisticsErrorCodes.InvalidResponse);
            snapshot.definitions = _definitions;
            _clockOffset = snapshot.operationWindow.serverTimeUnixMillis - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _current = snapshot;
            _maxObservedRevision = snapshot.revision;
            _lastObservedServerTime = snapshot.operationWindow.serverTimeUnixMillis;
            LastErrorCode = null;
            return snapshot;
        }
        private T NetworkFailure<T>(NetworkResponse<T> response) where T : class
        {
            if (response.StatusCode == 401) ClearData();
            return Fail<T>(response.ErrorCode ?? StatisticsErrorCodes.FromHttpStatus(response.StatusCode));
        }
        private T Fail<T>(string error) where T : class
        {
            LastErrorCode = error;
            HorizonApp.Log.Error($"Statistics request failed ({error})");
            return null;
        }
    }
}
