using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: User Logs.
    ///
    /// What it does: connects, signs in anonymously, then writes one INFO, one WARN
    /// and one ERROR log entry to the server.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer,
    /// then attach this script to an empty GameObject and press Play.
    /// Expected Debug.Log output: "Info log created: <id>" plus the warn and error results.
    /// Note: user logs are not available on FREE tier accounts.
    ///
    /// Reference: docs/wiki/sdks/features/user-logs.md
    /// </summary>
    public class UserLogsExample : MonoBehaviour
    {
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[UserLogsExample] Could not connect to horizOn");
                    return;
                }

                // Logs are attributed to a user, so a sign in is required first.
                if (!await UserManager.Instance.SignUpAnonymous("Player1"))
                {
                    Debug.LogError("[UserLogsExample] Anonymous sign up failed");
                    return;
                }

                var info = await UserLogManager.Instance.Info("Tutorial completed");
                if (info != null)
                {
                    Debug.Log($"[UserLogsExample] Info log created: {info.id}");
                }

                var warn = await UserLogManager.Instance.Warn("Low memory detected");
                if (warn != null)
                {
                    Debug.Log($"[UserLogsExample] Warn log created: {warn.id}");
                }

                var error = await UserLogManager.Instance.Error("Save failed", errorCode: "SAVE_001");
                if (error != null)
                {
                    Debug.Log($"[UserLogsExample] Error log created: {error.id}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UserLogsExample] Unexpected error: {e.Message}");
            }
        }
    }
}
