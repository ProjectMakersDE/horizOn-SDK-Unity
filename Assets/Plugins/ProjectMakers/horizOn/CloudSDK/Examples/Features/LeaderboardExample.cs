using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: Leaderboards.
    ///
    /// What it does: connects, signs in anonymously, submits a score, then reads the
    /// top entries and the current player rank.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer,
    /// then attach this script to an empty GameObject and press Play.
    /// Expected Debug.Log output: "Score submitted: <n>", a list of top entries,
    /// and "Your rank: <position>".
    ///
    /// Reference: docs/wiki/sdks/features/leaderboards.md
    /// </summary>
    public class LeaderboardExample : MonoBehaviour
    {
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[LeaderboardExample] Could not connect to horizOn");
                    return;
                }

                // Leaderboard calls require a signed in user.
                if (!await UserManager.Instance.SignUpAnonymous("Player1"))
                {
                    Debug.LogError("[LeaderboardExample] Anonymous sign up failed");
                    return;
                }

                long score = UnityEngine.Random.Range(100, 10000);
                bool submitted = await LeaderboardManager.Instance.SubmitScore(score);
                if (submitted)
                {
                    Debug.Log($"[LeaderboardExample] Score submitted: {score}");
                }

                var top = await LeaderboardManager.Instance.GetTop(5);
                if (top != null)
                {
                    foreach (var entry in top)
                    {
                        Debug.Log($"[LeaderboardExample] {entry.position}. {entry.username}: {entry.score}");
                    }
                }

                var rank = await LeaderboardManager.Instance.GetRank();
                if (rank != null)
                {
                    Debug.Log($"[LeaderboardExample] Your rank: {rank.position} (score {rank.score})");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LeaderboardExample] Unexpected error: {e.Message}");
            }
        }
    }
}
