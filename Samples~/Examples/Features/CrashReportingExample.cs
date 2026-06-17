using System;
using System.Collections.Generic;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: Crash Reporting.
    ///
    /// What it does: connects, signs in anonymously, starts automatic crash capture,
    /// records breadcrumbs and a custom key, then reports one non-fatal exception.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer,
    /// then attach this script to an empty GameObject and press Play.
    /// Expected Debug.Log output: "Capture started" then "Recorded non-fatal exception".
    /// Note: crash reporting is not available on FREE tier accounts.
    ///
    /// Reference: docs/wiki/sdks/features/crash-reporting.md
    /// </summary>
    public class CrashReportingExample : MonoBehaviour
    {
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[CrashReportingExample] Could not connect to horizOn");
                    return;
                }

                // Reports are attributed to the signed in user when one is available.
                await UserManager.Instance.SignUpAnonymous("Player1");

                // StartCapture hooks Unity log and unhandled exception callbacks. Call it once.
                CrashManager.Instance.StartCapture();
                Debug.Log("[CrashReportingExample] Capture started");

                CrashManager.Instance.RecordBreadcrumb("navigation", "Entered main menu");
                CrashManager.Instance.SetCustomKey("build", Application.version);

                // Manually report a non-fatal exception with extra context.
                try
                {
                    throw new InvalidOperationException("Example non-fatal exception");
                }
                catch (Exception inner)
                {
                    CrashManager.Instance.RecordException(inner, new Dictionary<string, string>
                    {
                        { "scene", "MainMenu" }
                    });
                    Debug.Log("[CrashReportingExample] Recorded non-fatal exception");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[CrashReportingExample] Unexpected error: {e.Message}");
            }
        }
    }
}
