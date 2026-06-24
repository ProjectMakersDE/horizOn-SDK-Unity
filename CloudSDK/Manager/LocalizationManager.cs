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
    /// Manager for runtime localization (translated strings).
    /// </summary>
    public class LocalizationManager : BaseManager<LocalizationManager>
    {
        /// <summary>
        /// The languages supported by the localization backend.
        /// </summary>
        private static readonly string[] SupportedLanguages =
        {
            "en", "de", "es", "fr", "it", "pt", "nl", "pl", "ru", "ja", "zh", "ar", "ko", "tr", "id"
        };

        private const string DefaultLanguage = "en";

        // Translation cache keyed by language: language -> (key -> value)
        private Dictionary<string, Dictionary<string, string>> _translationCache =
            new Dictionary<string, Dictionary<string, string>>();

        // Languages whose FULL set has been loaded via GetAllLocalizations.
        // Single-key GetLocalization fills _translationCache piecemeal but must NOT
        // mark a language as fully loaded, otherwise GetAllLocalizations would return
        // a partial set without hitting the network.
        private readonly HashSet<string> _fullyLoadedLanguages = new HashSet<string>();

        private string _currentLanguage;

        /// <summary>
        /// The active language used when no explicit language is supplied.
        /// Defaults to the device language (mapped to one of the supported
        /// languages) or "en" when the device language is not supported.
        /// </summary>
        public string CurrentLanguage
        {
            get
            {
                if (string.IsNullOrEmpty(_currentLanguage))
                {
                    _currentLanguage = ResolveSystemLanguage();
                }
                return _currentLanguage;
            }
        }

        /// <summary>
        /// Set the active language. Accepts one of the supported language codes.
        /// Changing the language clears the cached translations.
        /// </summary>
        /// <param name="lang">Two-letter language code (e.g. "de")</param>
        public void SetLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang))
            {
                HorizonApp.Log.Warning("Language code is required");
                return;
            }

            string normalized = lang.ToLowerInvariant();

            if (!IsSupported(normalized))
            {
                HorizonApp.Log.Warning($"Unsupported language '{lang}', keeping '{CurrentLanguage}'");
                return;
            }

            if (normalized == CurrentLanguage)
            {
                return;
            }

            _currentLanguage = normalized;

            // Changing the language invalidates the cached translations.
            _translationCache.Clear();
            _fullyLoadedLanguages.Clear();
            HorizonApp.Events.Publish(EventKeys.CacheCleared, "Localization");
            HorizonApp.Log.Info($"Language set to '{normalized}'");
        }

        /// <summary>
        /// Get a single localized string by key.
        /// </summary>
        /// <param name="key">Localization key</param>
        /// <param name="lang">Language code, or null to use CurrentLanguage</param>
        /// <returns>The localized value, or null if not found</returns>
        public async Task<string> GetLocalization(string key, string lang = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                HorizonApp.Log.Error("Localization key is required");
                return null;
            }

            string language = ResolveLanguage(lang);

            // Check cache
            if (_translationCache.TryGetValue(language, out var cached) &&
                cached.TryGetValue(key, out var value))
            {
                HorizonApp.Events.Publish(EventKeys.CacheHit, $"Localization:{language}:{key}");
                return value;
            }

            var response = await HorizonApp.Network.GetAsync<LocalizationValueResponse>(
                $"/api/v1/app/localization/{key}?lang={language}",
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null && response.Data.found)
            {
                // Cache the result
                if (!_translationCache.TryGetValue(language, out cached))
                {
                    cached = new Dictionary<string, string>();
                    _translationCache[language] = cached;
                }
                cached[key] = response.Data.value;

                HorizonApp.Events.Publish(EventKeys.LocalizationDataLoaded, response.Data.value);

                return response.Data.value;
            }
            else
            {
                HorizonApp.Log.Warning($"Failed to get localization '{key}': {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// Get all translations for a language.
        /// </summary>
        /// <param name="lang">Language code, or null to use CurrentLanguage</param>
        /// <returns>Dictionary of all translations, or null if failed</returns>
        public async Task<Dictionary<string, string>> GetAllLocalizations(string lang = null)
        {
            string language = ResolveLanguage(lang);

            // Return cache only if the FULL set for this language was loaded.
            if (_fullyLoadedLanguages.Contains(language) &&
                _translationCache.TryGetValue(language, out var cached))
            {
                HorizonApp.Events.Publish(EventKeys.CacheHit, $"AllLocalizations:{language}");
                return new Dictionary<string, string>(cached);
            }

            var response = await HorizonApp.Network.GetAsync<LocalizationAllResponse>(
                $"/api/v1/app/localization/all?lang={language}",
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null)
            {
                // Update cache and mark this language as fully loaded.
                var translations = response.Data.GetTranslationsDictionary();
                _translationCache[language] = translations;
                _fullyLoadedLanguages.Add(language);

                HorizonApp.Log.Info($"Loaded {translations.Count} translations for '{language}'");
                HorizonApp.Events.Publish(EventKeys.LocalizationDataLoaded, translations);

                return new Dictionary<string, string>(translations);
            }
            else
            {
                HorizonApp.Log.Error($"Failed to get all localizations: {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// Get the languages that have translations available.
        /// </summary>
        /// <returns>Array of language codes, or null if failed</returns>
        public async Task<string[]> GetAvailableLanguages()
        {
            var response = await HorizonApp.Network.GetAsync<LocalizationLanguagesResponse>(
                "/api/v1/app/localization/languages",
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null)
            {
                HorizonApp.Log.Info($"Loaded {response.Data.total} available languages");
                return response.Data.languages;
            }
            else
            {
                HorizonApp.Log.Error($"Failed to get available languages: {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// Check whether a localization key exists.
        /// </summary>
        /// <param name="key">Localization key</param>
        /// <param name="lang">Language code, or null to use CurrentLanguage</param>
        public async Task<bool> HasKey(string key, string lang = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            string language = ResolveLanguage(lang);

            if (_translationCache.TryGetValue(language, out var cached) && cached.ContainsKey(key))
            {
                HorizonApp.Events.Publish(EventKeys.CacheHit, $"Localization:{language}:{key}");
                return true;
            }

            var value = await GetLocalization(key, language);
            return value != null;
        }

        /// <summary>
        /// Clear the translation cache.
        /// </summary>
        public void ClearCache()
        {
            _translationCache.Clear();
            _fullyLoadedLanguages.Clear();
            HorizonApp.Events.Publish(EventKeys.CacheCleared, "Localization");
            HorizonApp.Log.Info("Localization cache cleared");
        }

        /// <summary>
        /// Resolve a language argument to a concrete language code.
        /// </summary>
        private string ResolveLanguage(string lang)
        {
            return string.IsNullOrEmpty(lang) ? CurrentLanguage : lang.ToLowerInvariant();
        }

        /// <summary>
        /// Whether the given language code is one of the supported languages.
        /// </summary>
        private static bool IsSupported(string lang)
        {
            foreach (var supported in SupportedLanguages)
            {
                if (supported == lang)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Map the device language to one of the supported language codes,
        /// falling back to "en" when unsupported.
        /// </summary>
        private static string ResolveSystemLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.English: return "en";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Dutch: return "nl";
                case SystemLanguage.Polish: return "pl";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Chinese: return "zh";
                case SystemLanguage.ChineseSimplified: return "zh";
                case SystemLanguage.ChineseTraditional: return "zh";
                case SystemLanguage.Arabic: return "ar";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Indonesian: return "id";
                default: return DefaultLanguage;
            }
        }
    }
}
