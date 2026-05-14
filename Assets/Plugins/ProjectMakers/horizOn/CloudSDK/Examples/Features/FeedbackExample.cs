using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: Feedback.
    ///
    /// What it does: connects, then submits a bug report and a feature request.
    /// Feedback can be sent with or without a signed in user.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer,
    /// then attach this script to an empty GameObject and press Play.
    /// Expected Debug.Log output: "Bug report submitted" then "Feature request submitted".
    ///
    /// Reference: docs/wiki/sdks/features/feedback.md
    /// </summary>
    public class FeedbackExample : MonoBehaviour
    {
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[FeedbackExample] Could not connect to horizOn");
                    return;
                }

                // Optional: sign in so the feedback is attributed to a user.
                await UserManager.Instance.SignUpAnonymous("Player1");

                // ReportBug attaches device info automatically.
                bool bug = await FeedbackManager.Instance.ReportBug(
                    title: "Crash on level 5",
                    message: "The game freezes when opening the inventory.");
                if (bug)
                {
                    Debug.Log("[FeedbackExample] Bug report submitted");
                }

                bool feature = await FeedbackManager.Instance.RequestFeature(
                    title: "Dark mode",
                    message: "Please add a dark mode option to the settings menu.");
                if (feature)
                {
                    Debug.Log("[FeedbackExample] Feature request submitted");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[FeedbackExample] Unexpected error: {e.Message}");
            }
        }
    }
}
