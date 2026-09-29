using System;
using System.Collections.Generic;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: Validated Actions (server-checked runs).
    ///
    /// What it does: connects, signs in anonymously, starts a run bound to a leaderboard,
    /// seeds a deterministic random generator with the server seed, "plays" by recording a few
    /// inputs into an input log, and submits score plus the log. The server checks ticket and rules
    /// before it writes the score.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer, create the
    /// leaderboard in the horizOn Dashboard (key "weekly", optionally "Validated submissions only")
    /// and, if you like, a rule set under Validated Actions. Attach the script to an empty
    /// GameObject and press Play.
    /// Expected Debug.Log output: "Run started: seed N", then "Run accepted: rank N" or
    /// "Run rejected: CODE" (for example DURATION_TOO_SHORT when your rules ask for a minimum
    /// duration, since this example submits right away).
    ///
    /// Reference: docs/wiki/sdks/features/validated-actions.md
    /// </summary>
    public class ValidatedActionsExample : MonoBehaviour
    {
        [SerializeField] private string leaderboardKey = "weekly";

        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[ValidatedActionsExample] Could not connect to horizOn");
                    return;
                }

                // Validated runs require a signed in player (Bearer session) with a display name
                // when a leaderboard is targeted.
                if (!await UserManager.Instance.SignUpAnonymous("Player1"))
                {
                    Debug.LogError("[ValidatedActionsExample] Anonymous sign up failed");
                    return;
                }

                ValidatedRun run = await ValidatedActionsManager.Instance.StartRun(leaderboardKey);
                if (run == null)
                {
                    // For example RUN_RATE_LIMITED, LEADERBOARD_NOT_FOUND or NOT_SUPPORTED (simpleServer).
                    Debug.LogError($"[ValidatedActionsExample] Start failed: {ValidatedActionsManager.Instance.LastErrorCode}");
                    return;
                }

                Debug.Log($"[ValidatedActionsExample] Run started: seed {run.seed}, expires {run.expiresAt}");

                // Deterministic gameplay: the same seed and the same inputs give the same result.
                var random = new System.Random(run.seed);
                var inputLog = new List<byte>();
                long score = 0;
                for (int tick = 0; tick < 20; tick++)
                {
                    byte input = (byte)random.Next(0, 4); // stands in for the player's input this tick
                    inputLog.Add(input);
                    score += input * 10;
                }

                ValidatedSubmitResult result = await ValidatedActionsManager.Instance.SubmitValidated(
                    score, inputLog.ToArray());
                if (result == null)
                {
                    // Rule rejections (422) and SCORE_LIMIT_REACHED use up the ticket: start a new run.
                    // Network errors, 429 and 5xx keep the run, so SubmitValidated may be called again.
                    Debug.LogWarning($"[ValidatedActionsExample] Run rejected: {ValidatedActionsManager.Instance.LastErrorCode} " +
                                     $"(run kept: {ValidatedActionsManager.Instance.HasActiveRun})");
                    return;
                }

                Debug.Log($"[ValidatedActionsExample] Run accepted: rank {result.rank}, best {result.bestScore}, " +
                          $"new high score {result.isNewHighScore}, {result.durationSeconds}s measured by the server");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ValidatedActionsExample] Unexpected error: {e.Message}");
            }
        }
    }
}
