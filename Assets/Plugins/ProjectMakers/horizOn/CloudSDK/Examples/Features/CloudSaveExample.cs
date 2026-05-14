using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: Cloud Save.
    ///
    /// What it does: connects, signs in anonymously, saves a small game state object,
    /// then loads it back from the cloud.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer,
    /// then attach this script to an empty GameObject and press Play.
    /// Expected Debug.Log output: "Saved: Level <n>" then "Loaded: Level <n>, Coins <n>".
    ///
    /// Reference: docs/wiki/sdks/features/cloud-save.md
    /// </summary>
    public class CloudSaveExample : MonoBehaviour
    {
        [Serializable]
        private class GameState
        {
            public int Level;
            public int Coins;
        }

        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[CloudSaveExample] Could not connect to horizOn");
                    return;
                }

                // Cloud save is scoped to a user, so a sign in is required first.
                if (!await UserManager.Instance.SignUpAnonymous("Player1"))
                {
                    Debug.LogError("[CloudSaveExample] Anonymous sign up failed");
                    return;
                }

                var state = new GameState { Level = 5, Coins = 1200 };
                bool saved = await CloudSaveManager.Instance.SaveObject(state);
                if (saved)
                {
                    Debug.Log($"[CloudSaveExample] Saved: Level {state.Level}");
                }

                var loaded = await CloudSaveManager.Instance.LoadObject<GameState>();
                if (loaded != null)
                {
                    Debug.Log($"[CloudSaveExample] Loaded: Level {loaded.Level}, Coins {loaded.Coins}");
                }
                else
                {
                    Debug.LogWarning("[CloudSaveExample] No cloud save found, using defaults");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[CloudSaveExample] Unexpected error: {e.Message}");
            }
        }
    }
}
