using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Requests;
namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Sample-owned identity recovery and immutable project/player batches.</summary>
    public static class StatisticsSampleStore
    {
        private const string Token = "horizOn_AnonymousToken", Recovery = "horizOn_statistics_identity_recovery", Started = "horizOn_statistics_identity_started";
        public static bool PrepareIdentity(out bool restore)
        {
            restore = false;
            if (!PlayerPrefs.HasKey(Token) && PlayerPrefs.HasKey(Recovery))
            { PlayerPrefs.SetString(Token, PlayerPrefs.GetString(Recovery)); PlayerPrefs.Save(); }
            if (PlayerPrefs.HasKey(Token))
            {
                string original = PlayerPrefs.GetString(Token);
                if (!PlayerPrefs.HasKey(Recovery)) { PlayerPrefs.SetString(Recovery, original); PlayerPrefs.Save(); }
                restore = true;
                return !string.IsNullOrEmpty(original);
            }
            if (PlayerPrefs.HasKey(Started)) return false;
            PlayerPrefs.SetString(Started, "1"); PlayerPrefs.Save();
            return PlayerPrefs.HasKey(Started);
        }
        public static string Key(string project, string user)
        {
            using (var sha = SHA256.Create()) return "horizOn_statistics_pending_v2_" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(project + "|" + user))).Replace("-", "");
        }
        public static bool TryLoad(string project, string user, out UpdateStatisticsRequest pending)
        {
            pending = null;
            if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(user)) return false;
            // A projectless legacy batch cannot safely be assigned to the current project.
            if (PlayerPrefs.HasKey("horizOn_statistics_pending_" + user)) return false;
            string key = Key(project, user);
            if (!PlayerPrefs.HasKey(key)) return true;
            try
            {
                var value = JsonUtility.FromJson<UpdateStatisticsRequest>(PlayerPrefs.GetString(key));
                var parts = value?.operationId?.Split(':');
                if (value?.userId != user || parts?.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var millis)
                    || millis <= 0 || !Guid.TryParseExact(parts[1], "D", out _) || value.updates == null || value.updates.Length == 0) return false;
                var keys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (var update in value.updates)
                    if (update == null || string.IsNullOrEmpty(update.key) || !keys.Add(update.key) || update.value < -PlayerValuesManager.MaxSafeInteger || update.value > PlayerValuesManager.MaxSafeInteger) return false;
                pending = value; return true;
            }
            catch (Exception) { return false; }
        }
        public static bool Save(string project, UpdateStatisticsRequest pending)
        {
            if (pending == null || !TryLoad(project, pending.userId, out var existing) || existing != null) return false;
            string key = Key(project, pending.userId), json = JsonUtility.ToJson(pending);
            PlayerPrefs.SetString(key, json); PlayerPrefs.Save();
            return TryLoad(project, pending.userId, out var saved) && saved != null && PlayerPrefs.GetString(key) == json;
        }
        public static bool Confirm(string project, UpdateStatisticsRequest pending)
        {
            if (pending == null || !TryLoad(project, pending.userId, out var stored) || stored == null || JsonUtility.ToJson(stored) != JsonUtility.ToJson(pending)) return false;
            string key = Key(project, pending.userId); PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); return !PlayerPrefs.HasKey(key);
        }
    }
}
