using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples
{
    /// <summary>
    /// Hello horizOn: the smallest end-to-end entry point for the horizOn Unity SDK.
    ///
    /// Flow: initialize the SDK, connect to the server, sign up an anonymous user,
    /// submit a score, then display the resulting leaderboard rank.
    /// This is the front door for new integrators. For per-feature examples see the
    /// Examples/Features folder, and for the full feature tour see ExampleUI.
    ///
    /// Setup (3 steps):
    ///   1. Import the horizOn SDK package.
    ///   2. Set your API key via Window > horizOn > Config Importer.
    ///   3. Add this component to an empty GameObject in a scene and press Play.
    /// Optionally assign a UI Text element to ResultText to see the result on screen.
    ///
    /// Expected Debug.Log output: "Connected", "Signed in: <userId>",
    /// "Score submitted: <score>", "Your rank: <position>".
    /// </summary>
    public class HelloHorizon : MonoBehaviour
    {
        [Header("Optional UI")]
        [Tooltip("Optional. Assign a UI Text to mirror the result on screen.")]
        [SerializeField] private UnityEngine.UI.Text resultText;

        [Header("Demo Settings")]
        [SerializeField] private string displayName = "Player1";
        [SerializeField] private long demoScore = 1000;

        private async void Start()
        {
            try
            {
                // Step 1: initialize the SDK.
                HorizonApp.Initialize();

                // Step 2: connect to the horizOn backend.
                var server = new HorizonServer();
                bool connected = await server.Connect();
                if (!connected)
                {
                    Report("Could not connect to horizOn. Check your API key and network.");
                    return;
                }
                Debug.Log("[HelloHorizon] Connected");

                // Step 3: create an anonymous user. The token is cached for session restore.
                bool signedIn = await UserManager.Instance.SignUpAnonymous(displayName);
                if (!signedIn)
                {
                    Report("Anonymous sign up failed.");
                    return;
                }
                Debug.Log($"[HelloHorizon] Signed in: {UserManager.Instance.CurrentUser.UserId}");

                // Step 4: submit a score to the leaderboard.
                bool submitted = await LeaderboardManager.Instance.SubmitScore(demoScore);
                if (!submitted)
                {
                    Report("Score submission failed.");
                    return;
                }
                Debug.Log($"[HelloHorizon] Score submitted: {demoScore}");

                // Step 5: read back the rank and display the result.
                var rank = await LeaderboardManager.Instance.GetRank();
                if (rank != null)
                {
                    Report($"Hello horizOn! {displayName} is rank {rank.position} with score {rank.score}.");
                }
                else
                {
                    Report($"Hello horizOn! Score {demoScore} submitted, rank not available yet.");
                }
            }
            catch (Exception e)
            {
                Report($"Unexpected error: {e.Message}");
            }
        }

        /// <summary>
        /// Log the message and mirror it to the optional on-screen Text element.
        /// </summary>
        private void Report(string message)
        {
            Debug.Log($"[HelloHorizon] {message}");
            if (resultText != null)
            {
                resultText.text = message;
            }
        }
    }
}
