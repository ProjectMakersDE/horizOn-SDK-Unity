using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace PM.horizOn.Cloud.Helper
{
    /// <summary>
    /// Helper class for JSON serialization/deserialization.
    /// Extends Unity's JsonUtility with additional functionality.
    /// </summary>
    public static class JsonHelper
    {
        /// <summary>
        /// Serialize an object to JSON.
        /// </summary>
        /// <typeparam name="T">The object type</typeparam>
        /// <param name="obj">The object to serialize</param>
        /// <param name="prettyPrint">Whether to format the JSON with indentation</param>
        /// <returns>JSON string</returns>
        public static string ToJson<T>(T obj, bool prettyPrint = false)
        {
            if (obj == null)
            {
                return "{}";
            }

            try
            {
                return JsonUtility.ToJson(obj, prettyPrint);
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonHelper] Serialization failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// Serialize an object to JSON, excluding null or empty string fields.
        /// This is useful for API requests where empty strings would fail validation.
        /// </summary>
        /// <param name="obj">The object to serialize</param>
        /// <returns>JSON string without null/empty fields</returns>
        public static string ToJsonExcludeEmpty(object obj)
        {
            if (obj == null)
            {
                return "{}";
            }

            try
            {
                var sb = new StringBuilder();
                sb.Append("{");

                var fields = obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
                bool first = true;

                foreach (var field in fields)
                {
                    var value = field.GetValue(obj);

                    // Skip null values
                    if (value == null)
                        continue;

                    // Skip empty strings
                    if (value is string strValue && string.IsNullOrEmpty(strValue))
                        continue;

                    if (!first)
                        sb.Append(",");
                    first = false;

                    sb.Append("\"");
                    sb.Append(field.Name);
                    sb.Append("\":");

                    if (value is string)
                    {
                        sb.Append("\"");
                        sb.Append(EscapeJsonString((string)value));
                        sb.Append("\"");
                    }
                    else if (value is bool boolValue)
                    {
                        sb.Append(boolValue ? "true" : "false");
                    }
                    else if (value is int || value is long || value is float || value is double)
                    {
                        sb.Append(value.ToString());
                    }
                    else
                    {
                        // For complex types, use JsonUtility
                        sb.Append(JsonUtility.ToJson(value));
                    }
                }

                sb.Append("}");
                return sb.ToString();
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonHelper] ToJsonExcludeEmpty failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// Escape special characters in a JSON string.
        /// </summary>
        private static string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            var sb = new StringBuilder();
            foreach (char c in str)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Deserialize JSON to an object.
        /// </summary>
        /// <typeparam name="T">The target type</typeparam>
        /// <param name="json">The JSON string</param>
        /// <returns>Deserialized object, or default if failed</returns>
        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return default(T);
            }

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonHelper] Deserialization failed: {e.Message}");
                return default(T);
            }
        }

        /// <summary>
        /// Deserialize JSON array to a list.
        /// Unity's JsonUtility doesn't support arrays directly, so we wrap them.
        /// </summary>
        /// <typeparam name="T">The element type</typeparam>
        /// <param name="json">The JSON array string</param>
        /// <returns>List of deserialized objects</returns>
        public static List<T> FromJsonArray<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new List<T>();
            }

            try
            {
                // Wrap array in an object
                string wrappedJson = $"{{\"items\":{json}}}";
                var wrapper = JsonUtility.FromJson<ArrayWrapper<T>>(wrappedJson);
                return new List<T>(wrapper.items);
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonHelper] Array deserialization failed: {e.Message}");
                return new List<T>();
            }
        }

        /// <summary>
        /// Serialize a list to JSON array.
        /// </summary>
        /// <typeparam name="T">The element type</typeparam>
        /// <param name="list">The list to serialize</param>
        /// <param name="prettyPrint">Whether to format the JSON with indentation</param>
        /// <returns>JSON array string</returns>
        public static string ToJsonArray<T>(List<T> list, bool prettyPrint = false)
        {
            if (list == null || list.Count == 0)
            {
                return "[]";
            }

            try
            {
                var wrapper = new ArrayWrapper<T> { items = list.ToArray() };
                string json = JsonUtility.ToJson(wrapper, prettyPrint);

                // Extract the array part from the wrapper
                int startIndex = json.IndexOf('[');
                int endIndex = json.LastIndexOf(']');

                if (startIndex >= 0 && endIndex >= 0)
                {
                    return json.Substring(startIndex, endIndex - startIndex + 1);
                }

                return "[]";
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonHelper] Array serialization failed: {e.Message}");
                return "[]";
            }
        }

        /// <summary>
        /// Try to parse JSON, returns true if successful.
        /// </summary>
        /// <typeparam name="T">The target type</typeparam>
        /// <param name="json">The JSON string</param>
        /// <param name="result">The deserialized object (if successful)</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        public static bool TryFromJson<T>(string json, out T result)
        {
            result = default(T);

            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            try
            {
                result = JsonUtility.FromJson<T>(json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Wrapper class for JSON array serialization.
        /// </summary>
        [Serializable]
        private class ArrayWrapper<T>
        {
            public T[] items;
        }
    }
}
