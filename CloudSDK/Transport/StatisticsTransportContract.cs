using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Transport
{
    internal static class StatisticsTransportContract
    {
        internal const string Endpoint = "/api/v1/app/statistics";
        internal const long MaxValue = 9007199254740991L;
        private static readonly Regex Key = new Regex(@"^[a-z0-9][a-z0-9._-]{0,23}\z", RegexOptions.CultureInvariant);
        private static readonly Regex Operation = new Regex(@"^[0-9]{13}:[0-9a-fA-F-]{36}\z", RegexOptions.CultureInvariant);

        internal static bool HasSession(UserData user, string token) => user != null && !string.IsNullOrEmpty(user.UserId)
            && !string.IsNullOrEmpty(token) && string.Equals(user.AccessToken, token, StringComparison.Ordinal);

        internal static bool ValidKey(string key) => key != null && Key.IsMatch(key);
        internal static bool ValidOperationId(string id) => id != null && Operation.IsMatch(id)
            && Guid.TryParseExact(id.Substring(14), "D", out _);
        internal static string CreateOperationId(long unixMillis) => unixMillis.ToString(CultureInfo.InvariantCulture) + ":" + Guid.NewGuid().ToString("D");

        internal static string GetEndpoint(UserData user, long? version) => Endpoint + "?userId=" + Uri.EscapeDataString(user.UserId)
            + (version.HasValue ? "&definitionVersion=" + version.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
        internal static string DefinitionsEndpoint(long? version) => Endpoint + "/definitions"
            + (version.HasValue ? "?definitionVersion=" + version.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);

        internal static bool TryUpdate(UserData user, string token, IList<StatisticUpdate> updates, string operationId,
            out UpdateStatisticsRequest request, out string error)
        {
            request = null;
            error = StatisticsErrorCodes.SessionRequired;
            if (!HasSession(user, token)) return false;
            error = StatisticsErrorCodes.InvalidRequest;
            if (!ValidOperationId(operationId) || updates == null || updates.Count < 1 || updates.Count > 100) return false;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var copy = new StatisticUpdate[updates.Count];
            for (int i = 0; i < updates.Count; i++)
            {
                var update = updates[i];
                if (update == null || !ValidKey(update.key) || !keys.Add(update.key) || update.value < -MaxValue || update.value > MaxValue) return false;
                copy[i] = new StatisticUpdate(update.key, update.value);
            }
            request = new UpdateStatisticsRequest { userId = user.UserId, operationId = operationId, updates = copy };
            error = null;
            return true;
        }
    }
}
