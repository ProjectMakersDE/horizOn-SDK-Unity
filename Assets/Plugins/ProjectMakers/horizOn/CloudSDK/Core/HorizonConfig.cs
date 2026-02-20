using System;
using UnityEngine;
using PM.horizOn.Cloud.Enums;
using LogType = PM.horizOn.Cloud.Enums.LogType;

namespace PM.horizOn.Cloud.Core
{
    /// <summary>
    /// Configuration ScriptableObject for horizOn SDK.
    /// Stores API key, hosts, and other settings.
    /// Created automatically by ConfigImporter from horizOn_config.ini file.
    /// </summary>
    [CreateAssetMenu(fileName = "HorizonConfig", menuName = "horizOn/Configuration", order = 1)]
    public class HorizonConfig : ScriptableObject
    {
        private const string RESOURCE_PATH = "horizOn/HorizonConfig";

        [Header("API Configuration")]
        [Tooltip("Your horizOn API key (encrypted in builds)")]
        [SerializeField] private string _encryptedApiKey;

        [Tooltip("Backend host URLs. Single URL uses direct connection; multiple URLs enable ping-based selection")]
        [SerializeField] private string[] _hosts;

        [Header("Environment Settings")]
        [Tooltip("Environment name (production, staging, development)")]
        [SerializeField] private string _environment = "production";

        [Header("Connection Settings")]
        [Tooltip("Connection timeout in seconds")]
        [SerializeField] private int _connectionTimeoutSeconds = 10;

        [Tooltip("Number of retry attempts for failed requests")]
        [SerializeField] private int _maxRetryAttempts = 3;

        [Tooltip("Delay between retry attempts in seconds")]
        [SerializeField] private float _retryDelaySeconds = 1.0f;

        [Tooltip("Logging level for SDK messages")]
        [SerializeField] private LogType _logLevel = LogType.INFO;
        
        // Cached decrypted API key (only in memory, never serialized)
        private string _cachedApiKey;

        /// <summary>
        /// Get the API key (decrypted).
        /// </summary>
        public string ApiKey
        {
            get
            {
                if (string.IsNullOrEmpty(_cachedApiKey))
                {
                    _cachedApiKey = DecryptApiKey(_encryptedApiKey);
                }
                return _cachedApiKey;
            }
        }

        /// <summary>
        /// Get the list of host URLs.
        /// </summary>
        public string[] Hosts => _hosts;

        /// <summary>
        /// Get the environment name.
        /// </summary>
        public string Environment => _environment;

        /// <summary>
        /// Get the connection timeout in seconds.
        /// </summary>
        public int ConnectionTimeoutSeconds => _connectionTimeoutSeconds;

        /// <summary>
        /// Get the maximum number of retry attempts.
        /// </summary>
        public int MaxRetryAttempts => _maxRetryAttempts;

        /// <summary>
        /// Get the delay between retry attempts in seconds.
        /// </summary>
        public float RetryDelaySeconds => _retryDelaySeconds;

        /// <summary>
        /// Get the logging level.
        /// </summary>
        public LogType LogLevel => _logLevel;
        
        /// <summary>
        /// Set the API key (will be encrypted for storage).
        /// </summary>
        /// <param name="apiKey">The plain-text API key</param>
        public void SetApiKey(string apiKey)
        {
            _encryptedApiKey = EncryptApiKey(apiKey);
            _cachedApiKey = apiKey;
        }

        /// <summary>
        /// Set the host URLs.
        /// </summary>
        /// <param name="hosts">Array of host URLs</param>
        public void SetHosts(string[] hosts)
        {
            _hosts = hosts;
        }

        /// <summary>
        /// Set the environment name.
        /// </summary>
        /// <param name="environment">Environment name</param>
        public void SetEnvironment(string environment)
        {
            _environment = environment;
        }

        /// <summary>
        /// Validate the configuration.
        /// </summary>
        /// <returns>True if configuration is valid, false otherwise</returns>
        public bool IsValid()
        {
            if (string.IsNullOrEmpty(_encryptedApiKey))
            {
                Debug.LogError("[HorizonConfig] API key is not set");
                return false;
            }

            if (_hosts == null || _hosts.Length == 0)
            {
                Debug.LogError("[HorizonConfig] No hosts configured");
                return false;
            }

            foreach (var host in _hosts)
            {
                if (string.IsNullOrEmpty(host))
                {
                    Debug.LogError("[HorizonConfig] Invalid host URL found");
                    return false;
                }

                if (!host.StartsWith("http://") && !host.StartsWith("https://"))
                {
                    Debug.LogError($"[HorizonConfig] Host URL must start with http:// or https://: {host}");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Load the configuration from Resources.
        /// </summary>
        /// <returns>The loaded configuration, or null if not found</returns>
        public static HorizonConfig Load()
        {
            var config = Resources.Load<HorizonConfig>(RESOURCE_PATH);
            if (config == null)
            {
                Debug.LogError($"[HorizonConfig] Configuration not found at Resources/{RESOURCE_PATH}. Please import horizOn_config.ini using Window > horizOn > Config Importer");
            }
            return config;
        }

        /// <summary>
        /// Simple encryption for API key (XOR with a key).
        /// Note: This is obfuscation, not true security. For production, consider using Unity's built-in encryption.
        /// </summary>
        private string EncryptApiKey(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            // Simple XOR encryption with a key derived from SystemInfo
            string key = $"{SystemInfo.deviceUniqueIdentifier}{Application.productName}";
            byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
            byte[] encryptedBytes = new byte[plainBytes.Length];

            for (int i = 0; i < plainBytes.Length; i++)
            {
                encryptedBytes[i] = (byte)(plainBytes[i] ^ keyBytes[i % keyBytes.Length]);
            }

            return Convert.ToBase64String(encryptedBytes);
        }

        /// <summary>
        /// Simple decryption for API key (XOR with the same key).
        /// </summary>
        private string DecryptApiKey(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return string.Empty;

            try
            {
                string key = $"{SystemInfo.deviceUniqueIdentifier}{Application.productName}";
                byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
                byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
                byte[] decryptedBytes = new byte[encryptedBytes.Length];

                for (int i = 0; i < encryptedBytes.Length; i++)
                {
                    decryptedBytes[i] = (byte)(encryptedBytes[i] ^ keyBytes[i % keyBytes.Length]);
                }

                return System.Text.Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (Exception e)
            {
                Debug.LogError($"[HorizonConfig] Failed to decrypt API key: {e.Message}");
                return string.Empty;
            }
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Get the expected path for this config asset in the Resources folder.
        /// </summary>
        public static string GetResourcesPath()
        {
            return "Assets/Plugins/ProjectMakers/horizOn/CloudSDK/Resources/horizOn";
        }
        #endif
    }
}
