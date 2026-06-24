using System;
using System.Collections.Generic;
using UnityEngine;

namespace PM.horizOn.Cloud.Objects.Network.Responses
{
    /// <summary>
    /// Single localization value response.
    /// </summary>
    [Serializable]
    public class LocalizationValueResponse
    {
        public string localizationKey;
        public string value;
        public string language;
        public bool found;
    }

    /// <summary>
    /// Response listing the languages that have translations available.
    /// </summary>
    [Serializable]
    public class LocalizationLanguagesResponse
    {
        public string[] languages;
        public int total;
    }

    /// <summary>
    /// Response containing all translations for a language.
    /// Note: This class uses a custom parser because Unity's JsonUtility
    /// cannot deserialize dictionary objects.
    /// </summary>
    [Serializable]
    public class LocalizationAllResponse
    {
        public string language;
        public int total;

        // This will be populated by custom parsing
        [NonSerialized]
        private Dictionary<string, string> _translations;

        /// <summary>
        /// Get the translations dictionary.
        /// </summary>
        public Dictionary<string, string> GetTranslationsDictionary()
        {
            return _translations ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Parse the response from JSON text.
        /// Unity's JsonUtility cannot handle dictionaries, so we parse manually.
        /// </summary>
        public static LocalizationAllResponse ParseFromJson(string json)
        {
            var response = new LocalizationAllResponse();

            try
            {
                // Use Unity's simple JSON parser to get the structure
                var wrapper = JsonUtility.FromJson<ResponseWrapper>(json);
                response.total = wrapper.total;
                response.language = wrapper.language;

                // Manually parse the translations object
                response._translations = new Dictionary<string, string>();

                // Find the "translations" object in the JSON
                int translationsStart = json.IndexOf("\"translations\":");
                if (translationsStart >= 0)
                {
                    // Find the opening brace of the translations object
                    int braceStart = json.IndexOf('{', translationsStart + 15);
                    if (braceStart >= 0)
                    {
                        // Find the matching closing brace
                        int braceEnd = FindMatchingBrace(json, braceStart);
                        if (braceEnd > braceStart)
                        {
                            string translationsJson = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
                            ParseTranslationsObject(translationsJson, response._translations);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse LocalizationAllResponse: {e.Message}");
            }

            return response;
        }

        /// <summary>
        /// Parse a JSON object containing key-value pairs into a dictionary.
        /// </summary>
        private static void ParseTranslationsObject(string json, Dictionary<string, string> dict)
        {
            // Proper JSON object-of-strings parser: reads quoted keys and values while
            // honoring escape sequences (\" \\ \/ \n \r \t \b \f \uXXXX). Localized text
            // routinely contains quotes, commas, colons and newlines, so naive comma
            // splitting + Trim('"') would corrupt values.
            int i = 0;
            int n = json.Length;
            while (i < n)
            {
                while (i < n && (char.IsWhiteSpace(json[i]) || json[i] == ',')) i++;
                if (i >= n || json[i] != '"') break;
                string key = ReadJsonString(json, ref i);

                while (i < n && json[i] != ':') i++;
                if (i >= n) break;
                i++; // skip ':'

                while (i < n && char.IsWhiteSpace(json[i])) i++;
                if (i >= n || json[i] != '"') break; // only string values are expected
                string value = ReadJsonString(json, ref i);

                dict[key] = value;
            }
        }

        /// <summary>
        /// Read a JSON string token starting at json[i] == '"'. Decodes standard
        /// escape sequences and advances i past the closing quote.
        /// </summary>
        private static string ReadJsonString(string json, ref int i)
        {
            var sb = new System.Text.StringBuilder();
            i++; // skip opening quote
            while (i < json.Length)
            {
                char c = json[i++];
                if (c == '\\' && i < json.Length)
                {
                    char e = json[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 <= json.Length &&
                                int.TryParse(json.Substring(i, 4),
                                    System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out int code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            else
                            {
                                sb.Append(e);
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
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
            public string language;
            public int total;
        }
    }
}
