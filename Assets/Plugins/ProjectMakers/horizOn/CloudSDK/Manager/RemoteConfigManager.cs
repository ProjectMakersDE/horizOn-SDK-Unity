using System.Collections.Generic;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Responses;
using UnityEngine;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Manager for remote configuration values.
    /// </summary>
    public class RemoteConfigManager : BaseManager<RemoteConfigManager>
    {
        private Dictionary<string, string> _configCache = new Dictionary<string, string>();

        /// <summary>
        /// Get a single configuration value by key.
        /// </summary>
        /// <param name="key">Configuration key</param>
        /// <param name="useCache">Whether to use cached value if available</param>
        /// <returns>Configuration value, or null if not found</returns>
        public async Task<string> GetConfig(string key, bool useCache = true)
        {
            if (string.IsNullOrEmpty(key))
            {
                HorizonApp.Log.Error("Config key is required");
                return null;
            }

            // Check cache
            if (useCache && _configCache.TryGetValue(key, out var config))
            {
                HorizonApp.Events.Publish(EventKeys.CacheHit, $"Config:{key}");
                return config;
            }

            var response = await HorizonApp.Network.GetAsync<GetRemoteConfigResponse>(
                $"/api/v1/app/remote-config/{key}",
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null && response.Data.found)
            {
                // Cache the result
                _configCache[key] = response.Data.configValue;
                HorizonApp.Events.Publish(EventKeys.ConfigDataLoaded, response.Data.configValue);

                return response.Data.configValue;
            }
            else
            {
                HorizonApp.Log.Warning($"Failed to get config '{key}': {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// Get all configuration values.
        /// </summary>
        /// <param name="useCache">Whether to return cached values if available</param>
        /// <returns>Dictionary of all configs, or null if failed</returns>
        public async Task<Dictionary<string, string>> GetAllConfigs(bool useCache = true)
        {
            // Return cache if available and requested
            if (useCache && _configCache.Count > 0)
            {
                HorizonApp.Events.Publish(EventKeys.CacheHit, "AllConfigs");
                return new Dictionary<string, string>(_configCache);
            }

            var response = await HorizonApp.Network.GetAsync<GetAllRemoteConfigResponse>(
                "/api/v1/app/remote-config/all",
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null)
            {
                // Update cache
                _configCache = response.Data.GetConfigsDictionary();

                HorizonApp.Log.Info($"Loaded {_configCache.Count} config values");
                HorizonApp.Events.Publish(EventKeys.ConfigDataLoaded, _configCache);

                return new Dictionary<string, string>(_configCache);
            }
            else
            {
                HorizonApp.Log.Error($"Failed to get all configs: {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// Get a config value as a string.
        /// </summary>
        public async Task<string> GetString(string key, string defaultValue = "", bool useCache = true)
        {
            var value = await GetConfig(key, useCache);
            return value ?? defaultValue;
        }

        /// <summary>
        /// Get a config value as an integer.
        /// </summary>
        public async Task<int> GetInt(string key, int defaultValue = 0, bool useCache = true)
        {
            var value = await GetConfig(key, useCache);
            if (value != null && int.TryParse(value, out int result))
            {
                return result;
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a config value as a float.
        /// </summary>
        public async Task<float> GetFloat(string key, float defaultValue = 0f, bool useCache = true)
        {
            var value = await GetConfig(key, useCache);
            if (value != null && float.TryParse(value, out float result))
            {
                return result;
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a config value as a boolean.
        /// </summary>
        public async Task<bool> GetBool(string key, bool defaultValue = false, bool useCache = true)
        {
            var value = await GetConfig(key, useCache);
            if (value != null && bool.TryParse(value, out bool result))
            {
                return result;
            }
            return defaultValue;
        }

        /// <summary>
        /// Get a config value as a JSON object.
        /// </summary>
        public async Task<T> GetJson<T>(string key, T defaultValue = default(T), bool useCache = true)
        {
            var value = await GetConfig(key, useCache);
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            try
            {
                return JsonUtility.FromJson<T>(value);
            }
            catch
            {
                HorizonApp.Log.Warning($"Failed to parse config '{key}' as JSON");
                return defaultValue;
            }
        }

        /// <summary>
        /// Check whether a config key exists.
        /// </summary>
        public async Task<bool> HasKey(string key, bool useCache = true)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (useCache && _configCache.ContainsKey(key))
            {
                HorizonApp.Events.Publish(EventKeys.CacheHit, $"Config:{key}");
                return true;
            }

            var value = await GetConfig(key, useCache);
            return value != null;
        }

        /// <summary>
        /// Clear the config cache.
        /// </summary>
        public void ClearCache()
        {
            _configCache.Clear();
            HorizonApp.Events.Publish(EventKeys.CacheCleared, "RemoteConfig");
            HorizonApp.Log.Info("Remote config cache cleared");
        }
    }
}
