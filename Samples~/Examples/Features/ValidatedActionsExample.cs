using System;
using System.Collections.Generic;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: Validated Actions (server-checked runs).
    ///
    /// What it does: connects, signs in anonymously, starts a run bound to a leaderboard with a
    /// start context (build versions, content digest and the initial state of the simulation),
    /// seeds a deterministic random generator with the server seed, "plays" by recording a few
    /// inputs into an input log, and submits score plus the log. The server checks ticket and rules
    /// before it writes the score.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer, create the
    /// leaderboard in the horizOn Dashboard (key "weekly", optionally "Validated submissions only")
    /// and, if you like, a rule set under Validated Actions. Attach the script to an empty
    /// GameObject and press Play.
    /// Expected Debug.Log output: "Run started: seed N", then "Run accepted: rank N, sus False" or
    /// "Run rejected: CODE" (for example DURATION_TOO_SHORT when your rules ask for a minimum
    /// duration, since this example submits right away). When the server asks for the input log
    /// (a new top entry with "Evidence top N" set on the board, or a sus run), the SDK uploads
    /// it on its own and logs "Evidence uploaded: N bytes".
    ///
    /// Reference: docs/wiki/sdks/features/validated-actions.md
    /// </summary>
    public class ValidatedActionsExample : MonoBehaviour
    {
        [SerializeField] private string leaderboardKey = "weekly";

        private void OnDestroy()
        {
            HorizonApp.Events.Unsubscribe<EvidenceUploadResult>(EventKeys.ValidatedEvidenceUploaded, OnEvidenceUploaded);
            HorizonApp.Events.Unsubscribe<ValidatedEvidenceFailure>(EventKeys.ValidatedEvidenceUploadFailed, OnEvidenceUploadFailed);
        }

        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                // The evidence upload runs in the background after an accepted submit; its outcome
                // arrives as an event and never changes the submit result.
                HorizonApp.Events.Subscribe<EvidenceUploadResult>(EventKeys.ValidatedEvidenceUploaded, OnEvidenceUploaded);
                HorizonApp.Events.Subscribe<ValidatedEvidenceFailure>(EventKeys.ValidatedEvidenceUploadFailed, OnEvidenceUploadFailed);

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

                // Optional start context: what this run starts from. The server binds it to the run
                // together with what it fixes itself (rules, cloud save, seed, start time) and keeps
                // it with a sus run, so the run can be replayed with the same build and state.
                // Set the versions once (DefaultRunContext) or pass a context per run as here.
                byte[] levelData = System.Text.Encoding.UTF8.GetBytes("level-1:walls=12;coins=40");
                byte[] initialState = System.Text.Encoding.UTF8.GetBytes("hp=100;x=0;y=0");
                var context = new ValidatedRunContext(
                    Application.version,
                    contentVersion: "levels-1",
                    simulationVersion: "sim-1",
                    replayFormatVersion: "inputs-v1",
                    // SHA-256 of the content bytes: the input log hash helper works for any bytes.
                    contentDigest: ValidatedActionsManager.ComputeInputLogHash(levelData),
                    initialState: initialState);

                ValidatedRun run = await ValidatedActionsManager.Instance.StartRun(leaderboardKey, context);
                if (run == null)
                {
                    // For example RUN_RATE_LIMITED, LEADERBOARD_NOT_FOUND, NOT_SUPPORTED (simpleServer),
                    // INITIAL_STATE_TOO_LARGE (413) or INVALID_CONTENT_DIGEST (local).
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
                    // PLAYER_BANNED keeps the run too, but this board refuses the player: discard it.
                    if (ValidatedActionsManager.Instance.LastErrorCode == ValidatedActionsErrorCodes.PlayerBanned)
                    {
                        ValidatedActionsManager.Instance.DiscardRun();
                    }
                    Debug.LogWarning($"[ValidatedActionsExample] Run rejected: {ValidatedActionsManager.Instance.LastErrorCode} " +
                                     $"(run kept: {ValidatedActionsManager.Instance.HasActiveRun})");
                    return;
                }

                Debug.Log($"[ValidatedActionsExample] Run accepted: rank {result.rank}, best {result.bestScore}, " +
                          $"new high score {result.isNewHighScore}, {result.durationSeconds}s measured by the server, sus {result.sus}");

                if (result.sus)
                {
                    // The run counts, but it crossed a soft threshold of your rules. The server keeps it
                    // with its start context for a review and asks for the input log (evidence.required),
                    // which the SDK uploads on its own, just like for a top N record. The reasons stay
                    // on the server; pass the flag on to your own analytics if you like.
                    Debug.Log("[ValidatedActionsExample] The run was marked sus and is kept for review");
                }

                if (result.evidence.required)
                {
                    // SubmitValidated knows the raw log, so the SDK uploads it (AutoUploadEvidence).
                    // After SubmitValidatedWithHash call it yourself with the exact bytes you hashed:
                    // await ValidatedActionsManager.Instance.UploadEvidence(result.evidence.runId, inputLog.ToArray());
                    Debug.Log($"[ValidatedActionsExample] Evidence requested until {result.evidence.uploadBefore}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ValidatedActionsExample] Unexpected error: {e.Message}");
            }
        }

        private void OnEvidenceUploaded(EvidenceUploadResult uploaded)
        {
            Debug.Log($"[ValidatedActionsExample] Evidence uploaded: {uploaded.bytes} bytes");
        }

        private void OnEvidenceUploadFailed(ValidatedEvidenceFailure failure)
        {
            // Only EVIDENCE_HASH_MISMATCH (wrong bytes) and NETWORK_ERROR are worth another
            // UploadEvidence call; every other code is final. The run stays accepted either way.
            Debug.LogWarning($"[ValidatedActionsExample] Evidence upload failed: {failure.code} (retryable: {failure.retryable})");
        }
    }
}
