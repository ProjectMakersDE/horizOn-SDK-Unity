using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Explicit sample storage, stable across regional hosts. No manager books or replaces an intent automatically.</summary>
    public static class PendingPlayerValuesPurchase
    {
        [Serializable] public sealed class Intent { public string userId, offerKey, operationId; }
        public static string Key(string apiKey, string userId) => Hash(apiKey + "|" + userId);
        private static string Hash(string scope)
        {
            using (var sha = SHA256.Create()) return "horizon_values_pending_" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(scope))).Replace("-", "");
        }
        public static bool TryLoad(string apiKey, string userId, IEnumerable<string> oldHosts, out Intent pending)
        {
            pending = null;
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(userId)) return false;
            string stable = Key(apiKey, userId);
            var candidates = new HashSet<string>(StringComparer.Ordinal) { stable };
            if (oldHosts != null) foreach (string host in oldHosts) if (!string.IsNullOrEmpty(host)) candidates.Add(Hash(host + "|" + apiKey + "|" + userId));
            var found = new List<string>();
            try
            {
                foreach (string key in candidates)
                {
                    if (!PlayerPrefs.HasKey(key)) continue;
                    var value = JsonUtility.FromJson<Intent>(PlayerPrefs.GetString(key));
                    var parts = value?.operationId?.Split(':');
                    if (value == null || value.userId != userId || string.IsNullOrEmpty(value.offerKey) || parts?.Length != 2
                        || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var millis) || millis <= 0 || !Guid.TryParseExact(parts[1], "D", out _)) return false;
                    if (pending != null && (pending.operationId != value.operationId || pending.offerKey != value.offerKey)) return false;
                    pending = value; found.Add(key);
                }
                if (pending == null) return true;
                string json = JsonUtility.ToJson(pending);
                PlayerPrefs.SetString(stable, json); PlayerPrefs.Save();
                if (PlayerPrefs.GetString(stable) != json) return false;
                foreach (string key in found) if (key != stable) PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save(); return true;
            }
            catch (Exception) { return false; }
        }
        public static bool Save(string apiKey, Intent pending)
        {
            if (pending == null || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(pending.userId) || string.IsNullOrEmpty(pending.operationId) || string.IsNullOrEmpty(pending.offerKey)) return false;
            string key = Key(apiKey, pending.userId);
            if (PlayerPrefs.HasKey(key)) return false;
            try { string json = JsonUtility.ToJson(pending); PlayerPrefs.SetString(key, json); PlayerPrefs.Save(); return PlayerPrefs.GetString(key) == json; }
            catch (Exception) { return false; }
        }
        public static bool Confirm(string apiKey, Intent pending)
        {
            if (pending == null || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(pending.userId) || string.IsNullOrEmpty(pending.operationId) || string.IsNullOrEmpty(pending.offerKey)) return false;
            string key = Key(apiKey, pending.userId);
            try
            {
                if (!PlayerPrefs.HasKey(key)) return false;
                var current = JsonUtility.FromJson<Intent>(PlayerPrefs.GetString(key));
                if (current?.operationId != pending.operationId || current?.offerKey != pending.offerKey || current?.userId != pending.userId) return false;
                PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); return !PlayerPrefs.HasKey(key);
            }
            catch (Exception) { return false; }
        }
    }
}
