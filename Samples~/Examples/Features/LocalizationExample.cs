using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: Localization.
    ///
    /// What it does: connects, picks a language, then reads localized strings
    /// using the single-key getter and the bulk GetAllLocalizations call.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer,
    /// then attach this script to an empty GameObject and press Play.
    /// Expected Debug.Log output: "menu.play = <value>" and "Loaded <n> translations".
    ///
    /// Reference: docs/wiki/sdks/features/localization.md
    /// </summary>
    public class LocalizationExample : MonoBehaviour
    {
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[LocalizationExample] Could not connect to horizOn");
                    return;
                }

                // Localization does not require authentication.
                // The active language defaults to the device language; override it explicitly.
                LocalizationManager.Instance.SetLanguage("de");
                Debug.Log($"[LocalizationExample] current language = {LocalizationManager.Instance.CurrentLanguage}");

                // Single key in the current language; returns null when the key is missing.
                string play = await LocalizationManager.Instance.GetLocalization("menu.play");
                Debug.Log($"[LocalizationExample] menu.play = {play}");

                // Every translation for the current language at once.
                var all = await LocalizationManager.Instance.GetAllLocalizations();
                if (all != null)
                {
                    Debug.Log($"[LocalizationExample] Loaded {all.Count} translations");
                }

                // The languages the backend has translations for.
                string[] languages = await LocalizationManager.Instance.GetAvailableLanguages();
                if (languages != null)
                {
                    Debug.Log($"[LocalizationExample] {languages.Length} languages available");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LocalizationExample] Unexpected error: {e.Message}");
            }
        }
    }
}
