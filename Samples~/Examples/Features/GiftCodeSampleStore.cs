using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>Game-owned durable intent, scoped to project/player and independent of regional host.</summary>
    public static class GiftCodeSampleStore
    {
        private const string Token = "horizOn_AnonymousToken", Recovery = "horizOn_gift_identity_recovery", Started = "horizOn_gift_identity_started";
        public static bool PrepareIdentity(out bool restore)
        {
            restore = false;
            try
            {
                if (!PlayerPrefs.HasKey(Token) && PlayerPrefs.HasKey(Recovery))
                { PlayerPrefs.SetString(Token, PlayerPrefs.GetString(Recovery)); PlayerPrefs.Save(); }
                if (PlayerPrefs.HasKey(Token))
                {
                    string original = PlayerPrefs.GetString(Token);
                    if (!PlayerPrefs.HasKey(Recovery)) { PlayerPrefs.SetString(Recovery, original); PlayerPrefs.Save(); }
                    restore = true;
                    return !string.IsNullOrEmpty(original) && PlayerPrefs.GetString(Recovery) == original;
                }
                if (PlayerPrefs.HasKey(Started)) return false;
                PlayerPrefs.SetString(Started, "1"); PlayerPrefs.Save();
                return PlayerPrefs.HasKey(Started);
            }
            catch (Exception) { return false; }
        }
        public static string Key(string project, string user)
        {
            using (var sha = SHA256.Create()) return "horizOn_gift_pending_" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(project + "|" + user))).Replace("-", "");
        }
        public static bool TryLoad(string project, string user, out GiftCodeIntent pending)
        {
            pending = null;
            if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(user)) return false;
            try
            {
                string key = Key(project, user);
                if (!PlayerPrefs.HasKey(key)) return true;
                var value = JsonUtility.FromJson<GiftCodeIntent>(PlayerPrefs.GetString(key));
                if (value == null || !value.IsValidFor(project,user)) return false;
                pending = value; return true;
            }
            catch (Exception) { return false; }
        }
        public static bool Save(string project, GiftCodeIntent pending)
        {
            if (pending == null || !TryLoad(project, pending.userId, out var existing) || existing != null
                || !pending.IsValidFor(project,pending.userId)) return false;
            try
            {
                string json = JsonUtility.ToJson(pending), key = Key(project, pending.userId);
                PlayerPrefs.SetString(key, json); PlayerPrefs.Save();
                return TryLoad(project, pending.userId, out var saved) && saved != null && PlayerPrefs.GetString(key) == json;
            }
            catch (Exception) { return false; }
        }
        public static bool Confirm(string project, GiftCodeIntent pending)
        {
            if (pending == null || !TryLoad(project, pending.userId, out var saved) || saved == null
                || JsonUtility.ToJson(saved) != JsonUtility.ToJson(pending)) return false;
            try { string key = Key(project, pending.userId); PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); return !PlayerPrefs.HasKey(key); }
            catch (Exception) { return false; }
        }
    }
}
