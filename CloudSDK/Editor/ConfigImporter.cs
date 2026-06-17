using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PM.horizOn.Cloud.Core;

namespace PM.horizOn.Cloud.Editor
{
    /// <summary>
    /// JSON structure for horizOn config file.
    /// </summary>
    [Serializable]
    public class HorizonConfigJson
    {
        public string apiKey;
        public string[] backendDomains;
    }

    /// <summary>
    /// Editor window for importing horizOn config JSON file.
    /// Creates a HorizonConfig ScriptableObject in the Resources folder.
    /// </summary>
    public class ConfigImporter : EditorWindow
    {
        private string _configFilePath = "";
        private string _apiKey = "";
        private List<string> _hosts = new List<string>();
        private bool _isValid = false;
        private string _errorMessage = "";
        private Vector2 _scrollPosition;

        [MenuItem("Window/horizOn/Config Importer")]
        public static void ShowWindow()
        {
            var window = GetWindow<ConfigImporter>("horizOn Config Importer");
            window.minSize = new Vector2(500, 400);
            window.Show();
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            // Title
            GUILayout.Label("horizOn Configuration Importer", EditorStyles.boldLabel);
            GUILayout.Space(10);

            // Instructions
            EditorGUILayout.HelpBox(
                "Import your horizOn config JSON file downloaded from the horizOn dashboard. " +
                "This will create a HorizonConfig asset with your API key and backend domains.",
                MessageType.Info
            );
            GUILayout.Space(10);

            // File selection
            GUILayout.Label("1. Select Configuration File", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField("Config File:", _configFilePath);
            if (GUILayout.Button("Browse", GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFilePanel("Select horizOn config JSON", "", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    _configFilePath = path;
                    ParseConfigFile(path);
                }
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);

            // Show parsed configuration
            if (!string.IsNullOrEmpty(_apiKey) || _hosts.Count > 0)
            {
                GUILayout.Label("2. Review Configuration", EditorStyles.boldLabel);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // API Key (masked)
                EditorGUILayout.LabelField("API Key:", MaskApiKey(_apiKey));

                // Hosts
                EditorGUILayout.LabelField("Backend Domains:", $"{_hosts.Count} configured");
                EditorGUI.indentLevel++;
                foreach (var host in _hosts)
                {
                    EditorGUILayout.LabelField("•", host);
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.EndVertical();
                GUILayout.Space(10);
            }

            // Error message
            if (!string.IsNullOrEmpty(_errorMessage))
            {
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
                GUILayout.Space(10);
            }

            // Import button
            GUI.enabled = _isValid;
            if (GUILayout.Button("Import Configuration", GUILayout.Height(30)))
            {
                ImportConfiguration();
            }
            GUI.enabled = true;

            GUILayout.Space(20);

            // Manual configuration section
            GUILayout.Label("Manual Configuration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Or manually create/edit the configuration below:",
                MessageType.Info
            );

            _apiKey = EditorGUILayout.PasswordField("API Key:", _apiKey);

            GUILayout.Label("Backend Domains:");
            EditorGUI.indentLevel++;
            for (int i = 0; i < _hosts.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _hosts[i] = EditorGUILayout.TextField($"Domain {i + 1}:", _hosts[i]);
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    _hosts.RemoveAt(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;

            if (GUILayout.Button("Add Domain"))
            {
                _hosts.Add("https://");
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Validate & Import Manual Configuration", GUILayout.Height(30)))
            {
                ValidateManualConfig();
                if (_isValid)
                {
                    ImportConfiguration();
                }
            }

            GUILayout.Space(20);

            // Cache Management Section
            GUILayout.Label("Cache Management", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Clear cached user session data stored in PlayerPrefs. " +
                "Use this if you're experiencing authentication issues or want to start fresh.",
                MessageType.Info
            );

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Anonymous Token"))
            {
                ClearAnonymousToken();
            }
            if (GUILayout.Button("Clear User Session"))
            {
                ClearUserSession();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Clear All horizOn Cache", GUILayout.Height(25)))
            {
                ClearAllCache();
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Clear cached anonymous token from PlayerPrefs.
        /// </summary>
        private void ClearAnonymousToken()
        {
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
            PlayerPrefs.Save();
            EditorUtility.DisplayDialog("Cache Cleared", "Anonymous token has been cleared from PlayerPrefs.", "OK");
            Debug.Log("[horizOn] Anonymous token cleared from cache");
        }

        /// <summary>
        /// Clear cached user session from PlayerPrefs.
        /// </summary>
        private void ClearUserSession()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            PlayerPrefs.Save();
            EditorUtility.DisplayDialog("Cache Cleared", "User session has been cleared from PlayerPrefs.", "OK");
            Debug.Log("[horizOn] User session cleared from cache");
        }

        /// <summary>
        /// Clear all horizOn-related cached data from PlayerPrefs.
        /// </summary>
        private void ClearAllCache()
        {
            if (EditorUtility.DisplayDialog(
                "Clear All Cache",
                "This will clear all horizOn cached data including:\n\n" +
                "• Anonymous Token\n" +
                "• User Session\n\n" +
                "Are you sure?",
                "Clear All",
                "Cancel"))
            {
                PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
                PlayerPrefs.DeleteKey("horizOn_UserSession");
                PlayerPrefs.Save();
                EditorUtility.DisplayDialog("Cache Cleared", "All horizOn cache has been cleared from PlayerPrefs.", "OK");
                Debug.Log("[horizOn] All cache cleared from PlayerPrefs");
            }
        }

        /// <summary>
        /// Parse the JSON configuration file.
        /// </summary>
        private void ParseConfigFile(string filePath)
        {
            try
            {
                _errorMessage = "";
                _apiKey = "";
                _hosts.Clear();
                _isValid = false;

                if (!File.Exists(filePath))
                {
                    _errorMessage = "File not found: " + filePath;
                    return;
                }

                string jsonContent = File.ReadAllText(filePath);
                HorizonConfigJson config = JsonUtility.FromJson<HorizonConfigJson>(jsonContent);

                if (config == null)
                {
                    _errorMessage = "Failed to parse JSON file. Please ensure it's valid JSON.";
                    return;
                }

                // Extract data
                _apiKey = config.apiKey;
                if (config.backendDomains != null && config.backendDomains.Length > 0)
                {
                    _hosts.AddRange(config.backendDomains);
                }

                // Validate
                if (string.IsNullOrEmpty(_apiKey))
                {
                    _errorMessage = "API key not found in configuration file";
                    return;
                }

                if (_hosts.Count == 0)
                {
                    _errorMessage = "No backend domains found in configuration file";
                    return;
                }

                _isValid = true;
            }
            catch (Exception e)
            {
                _errorMessage = $"Error parsing config file: {e.Message}";
                _isValid = false;
            }
        }

        /// <summary>
        /// Validate manual configuration.
        /// </summary>
        private void ValidateManualConfig()
        {
            _errorMessage = "";
            _isValid = false;

            if (string.IsNullOrEmpty(_apiKey))
            {
                _errorMessage = "API key is required";
                return;
            }

            if (_hosts.Count == 0)
            {
                _errorMessage = "At least one backend domain is required";
                return;
            }

            foreach (var host in _hosts)
            {
                if (string.IsNullOrEmpty(host))
                {
                    _errorMessage = "All backend domain URLs must be filled in";
                    return;
                }

                if (!host.StartsWith("http://") && !host.StartsWith("https://"))
                {
                    _errorMessage = $"Invalid domain URL (must start with http:// or https://): {host}";
                    return;
                }
            }

            _isValid = true;
        }

        /// <summary>
        /// Import the configuration and create HorizonConfig asset.
        /// </summary>
        private void ImportConfiguration()
        {
            try
            {
                // Create Resources directory if it doesn't exist
                string resourcesPath = "Assets/Plugins/ProjectMakers/horizOn/CloudSDK/Resources/horizOn";
                if (!Directory.Exists(resourcesPath))
                {
                    Directory.CreateDirectory(resourcesPath);
                }

                // Create or load existing config
                string assetPath = $"{resourcesPath}/HorizonConfig.asset";
                HorizonConfig config = AssetDatabase.LoadAssetAtPath<HorizonConfig>(assetPath);

                if (config == null)
                {
                    config = ScriptableObject.CreateInstance<HorizonConfig>();
                    AssetDatabase.CreateAsset(config, assetPath);
                    Debug.Log($"[horizOn] Created new config asset at: {assetPath}");
                }
                else
                {
                    Debug.Log($"[horizOn] Updating existing config asset at: {assetPath}");
                }

                // Set configuration values
                config.SetApiKey(_apiKey);
                config.SetHosts(_hosts.ToArray());
                config.SetEnvironment("production");

                // Mark asset as dirty and save
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // Select the asset
                EditorGUIUtility.PingObject(config);
                Selection.activeObject = config;

                EditorUtility.DisplayDialog(
                    "Import Successful",
                    $"Configuration imported successfully!\n\nAsset created at:\n{assetPath}\n\n" +
                    $"Backend Domains: {_hosts.Count} configured\n\n" +
                    "You can now use HorizonServer.Connect() in your game.",
                    "OK"
                );

                // Clear form
                _configFilePath = "";
                _apiKey = "";
                _hosts.Clear();
                _isValid = false;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(
                    "Import Failed",
                    $"Failed to import configuration:\n\n{e.Message}\n\nStack trace:\n{e.StackTrace}",
                    "OK"
                );
                Debug.LogError($"[horizOn] Config import failed: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// Mask API key for display (show only first 4 and last 4 characters).
        /// </summary>
        private string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return "Not set";

            if (apiKey.Length <= 8)
                return new string('*', apiKey.Length);

            return $"{apiKey.Substring(0, 4)}{'*'}{new string('*', apiKey.Length - 8)}{'*'}{apiKey.Substring(apiKey.Length - 4)}";
        }
    }
}
