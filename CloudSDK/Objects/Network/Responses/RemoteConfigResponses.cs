using System;
using System.Collections.Generic;
using UnityEngine;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// Single configuration value response.
    /// </summary>
    [Serializable]
    public class GetRemoteConfigResponse
    {
        public string configKey;
        public string configValue;
        public bool found;
    }

    /// <summary>
    /// Response containing all configuration values.
    /// Note: This class uses a custom parser because Unity's JsonUtility
    /// cannot deserialize dictionary objects.
    /// </summary>
    [Serializable]
    public class GetAllRemoteConfigResponse
    {
        public int total;

        // This will be populated by custom parsing
        [NonSerialized]
        private Dictionary<string, string> _configs;

        /// <summary>
        /// Get the configs dictionary.
        /// </summary>
        public Dictionary<string, string> GetConfigsDictionary()
        {
            return _configs ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Parse the response from JSON text.
        /// Unity's JsonUtility cannot handle dictionaries, so we parse manually.
        /// </summary>
        public static GetAllRemoteConfigResponse ParseFromJson(string json)
        {
            var response = new GetAllRemoteConfigResponse();

            try
            {
                // Use Unity's simple JSON parser to get the structure
                var wrapper = JsonUtility.FromJson<ResponseWrapper>(json);
                response.total = wrapper.total;

                // Manually parse the configs object
                response._configs = new Dictionary<string, string>();

                // Find the "configs" object in the JSON
                int configsStart = json.IndexOf("\"configs\":");
                if (configsStart >= 0)
                {
                    // Find the opening brace of the configs object
                    int braceStart = json.IndexOf('{', configsStart + 10);
                    if (braceStart >= 0)
                    {
                        // Find the matching closing brace
                        int braceEnd = FindMatchingBrace(json, braceStart);
                        if (braceEnd > braceStart)
                        {
                            string configsJson = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
                            ParseConfigsObject(configsJson, response._configs);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse GetAllRemoteConfigResponse: {e.Message}");
            }

            return response;
        }

        /// <summary>
        /// Parse a JSON object containing key-value pairs into a dictionary.
        /// </summary>
        private static void ParseConfigsObject(string json, Dictionary<string, string> dict)
        {
            if (string.IsNullOrEmpty(json.Trim()))
                return;

            // Split by commas, but need to be careful about commas inside quoted strings
            var pairs = new List<string>();
            int start = 0;
            bool inQuotes = false;

            for (int i = 0; i < json.Length; i++)
            {
                switch (json[i])
                {
                    case '"' when (i == 0 || json[i - 1] != '\\'):
                        inQuotes = !inQuotes;
                        break;
                    case ',' when !inQuotes:
                        pairs.Add(json.Substring(start, i - start));
                        start = i + 1;
                        break;
                }
            }
            // Add the last pair
            if (start < json.Length)
            {
                pairs.Add(json[start..]);
            }

            // Parse each key-value pair
            foreach (var pair in pairs)
            {
                int colonIndex = pair.IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = pair[..colonIndex].Trim();
                    string value = pair[(colonIndex + 1)..].Trim();

                    // Remove quotes
                    key = key.Trim('"');
                    value = value.Trim('"');

                    dict[key] = value;
                }
            }
        }

        /// <summary>
        /// Find the matching closing brace for an opening brace.
        /// </summary>
        private static int FindMatchingBrace(string json, int openBraceIndex)
        {
            int depth = 1;
            bool inQuotes = false;

            for (int i = openBraceIndex + 1; i < json.Length; i++)
            {
                if (json[i] == '"' && (i == 0 || json[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes)
                {
                    switch (json[i])
                    {
                        case '{':
                            depth++;
                            break;
                        case '}':
                        {
                            depth--;
                            if (depth == 0)
                                return i;
                            break;
                        }
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Wrapper class for basic JSON parsing with JsonUtility.
        /// </summary>
        [Serializable]
        private class ResponseWrapper
        {
            public int total;
        }
    }
}
