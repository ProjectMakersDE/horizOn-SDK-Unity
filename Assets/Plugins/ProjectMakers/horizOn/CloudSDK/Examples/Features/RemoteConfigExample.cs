using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: Remote Config.
    ///
    /// What it does: connects, then reads remote configuration values using the
    /// type-safe getters and the bulk GetAllConfigs call.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer,
    /// then attach this script to an empty GameObject and press Play.
    /// Expected Debug.Log output: "max_lives = <n>" and "Loaded <n> config values".
    ///
    /// Reference: docs/wiki/sdks/features/remote-config.md
    /// </summary>
    public class RemoteConfigExample : MonoBehaviour
    {
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[RemoteConfigExample] Could not connect to horizOn");
                    return;
                }

                // Remote config does not require authentication.
                // Type-safe getters return the supplied default when a key is missing.
                int maxLives = await RemoteConfigManager.Instance.GetInt("max_lives", 3);
                float difficulty = await RemoteConfigManager.Instance.GetFloat("difficulty", 1.0f);
                bool eventActive = await RemoteConfigManager.Instance.GetBool("holiday_event", false);

                Debug.Log($"[RemoteConfigExample] max_lives = {maxLives}");
                Debug.Log($"[RemoteConfigExample] difficulty = {difficulty}");
                Debug.Log($"[RemoteConfigExample] holiday_event = {eventActive}");

                var all = await RemoteConfigManager.Instance.GetAllConfigs();
                if (all != null)
                {
                    Debug.Log($"[RemoteConfigExample] Loaded {all.Count} config values");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[RemoteConfigExample] Unexpected error: {e.Message}");
            }
        }
    }
}
