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

        // Marks the portable (machine-independent) key format. Legacy values
        // are plain Base64 and can never contain ':', so the prefix is unambiguous.
        private const string KEY_FORMAT_PREFIX = "hzn1:";

        // Static obfuscation key. MUST NOT contain anything machine- or
        // device-specific: the asset is encrypted in the editor on the developer
        // machine and decrypted at runtime on the player device (TASK-451).
        // This is obfuscation, not security - the key ships inside the build either way.
        private const string OBFUSCATION_KEY = "horizOn.CloudSDK.ApiKey.v1";

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

        [Header("Apple Sign-In")]
        [Tooltip("Apple Services ID (web client_id) - required for the non-iOS web fallback OAuth flow")]
        [SerializeField] private string _appleServicesId;

        [Tooltip("OAuth redirect URI registered with Apple - must be reachable by the customer-side handler (deep-link or localhost listener)")]
        [SerializeField] private string _appleRedirectUri;

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
        /// Get the Apple Services ID (used as client_id in the non-iOS web OAuth fallback).
        /// </summary>
        public string AppleServicesId => _appleServicesId;

        /// <summary>
        /// Get the OAuth redirect URI registered with Apple for the web fallback flow.
        /// </summary>
        public string AppleRedirectUri => _appleRedirectUri;

        /// <summary>
        /// Set the Apple Services ID.
        /// </summary>
        public void SetAppleServicesId(string servicesId)
        {
            _appleServicesId = servicesId;
        }

        /// <summary>
        /// Set the Apple OAuth redirect URI.
        /// </summary>
        public void SetAppleRedirectUri(string redirectUri)
        {
            _appleRedirectUri = redirectUri;
        }
        
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

            if (string.IsNullOrEmpty(ApiKey))
            {
                // DecryptApiKey already logged the specific reason.
                Debug.LogError("[HorizonConfig] API key could not be decrypted");
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
        /// Simple encryption for API key (XOR with a static key).
        /// Note: This is obfuscation, not true security. The key material must stay
        /// machine-independent so assets encrypted in the editor decrypt on player devices.
        /// </summary>
        private string EncryptApiKey(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return KEY_FORMAT_PREFIX + Convert.ToBase64String(XorWithKey(plainBytes, OBFUSCATION_KEY));
        }

        /// <summary>
        /// Simple decryption for API key (XOR with the same key).
        /// Values without the format prefix fall back to the legacy device-bound scheme.
        /// </summary>
        private string DecryptApiKey(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return string.Empty;

            if (!encryptedText.StartsWith(KEY_FORMAT_PREFIX, StringComparison.Ordinal))
            {
                return DecryptLegacyApiKey(encryptedText);
            }

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedText.Substring(KEY_FORMAT_PREFIX.Length));
                return System.Text.Encoding.UTF8.GetString(XorWithKey(encryptedBytes, OBFUSCATION_KEY));
            }
            catch (Exception e)
            {
                Debug.LogError($"[HorizonConfig] Failed to decrypt API key: {e.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Decrypt a pre-hzn1 value. Those were XORed with the importing machine's
        /// deviceUniqueIdentifier, so they only decrypt on that machine - in a shipped
        /// build the result is garbage and every request gets 401 (TASK-451).
        /// </summary>
        private string DecryptLegacyApiKey(string encryptedText)
        {
            string decrypted;
            try
            {
                string legacyKey = $"{SystemInfo.deviceUniqueIdentifier}{Application.productName}";
                decrypted = System.Text.Encoding.UTF8.GetString(
                    XorWithKey(Convert.FromBase64String(encryptedText), legacyKey));
            }
            catch (Exception)
            {
                decrypted = null;
            }

            if (!LooksLikeApiKey(decrypted))
            {
                Debug.LogError(
                    "[HorizonConfig] The stored API key uses the legacy device-bound format " +
                    "and was encrypted on a different machine, so it cannot be decrypted here. " +
                    "Re-import horizOn_config.ini via Window > horizOn > Config Importer, " +
                    "or inject the key at runtime with HorizonConfig.SetApiKey().");
                return string.Empty;
            }

#if UNITY_EDITOR
            _encryptedApiKey = EncryptApiKey(decrypted);
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.LogWarning(
                "[HorizonConfig] Migrated legacy device-bound API key to the portable format. " +
                "Save the project (or re-import the config) to persist the migration before building.");
#else
            Debug.LogWarning(
                "[HorizonConfig] The stored API key uses the legacy device-bound format. " +
                "It decrypts on this device only because it was encrypted here - it will fail " +
                "on every other device. Re-import the config with an updated SDK and rebuild.");
#endif

            return decrypted;
        }

        private static byte[] XorWithKey(byte[] data, string key)
        {
            byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
            byte[] result = new byte[data.Length];

            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ keyBytes[i % keyBytes.Length]);
            }

            return result;
        }

        /// <summary>
        /// XOR with the wrong key almost surely yields bytes outside printable ASCII,
        /// while real API keys are printable ASCII. Used to tell a legacy value
        /// encrypted on this machine apart from one encrypted elsewhere.
        /// </summary>
        private static bool LooksLikeApiKey(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (char c in value)
            {
                if (c < 0x20 || c > 0x7E)
                    return false;
            }

            return true;
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
