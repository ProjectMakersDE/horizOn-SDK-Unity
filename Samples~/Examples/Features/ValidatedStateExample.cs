using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: server-owned player state (Validated Actions Part 2).
    ///
    /// What it does: connects, signs in anonymously, loads the player's server-owned values with
    /// GetState, plays a validated run without a leaderboard that earns gold, checks how much the
    /// server actually credited, and mirrors the new state into the cloud save for display and
    /// offline start. The cloud save copy is never sent back as a balance.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer and, in the
    /// horizOn Dashboard under Validated Actions, define a value "gold" in the rules of the API key
    /// (for example maxPerRun 500, dailyCap 5000). Attach the script to an empty GameObject and
    /// press Play.
    /// Expected Debug.Log output: "Gold on the server: N", then "Run accepted: +C of R gold,
    /// balance B" and "Mirrored into the cloud save". Without a "gold" value in the rules the run
    /// is rejected with UNKNOWN_VALUE_KEY.
    ///
    /// Reference: docs/wiki/sdks/features/validated-actions.md ("Server-owned state")
    /// </summary>
    public class ValidatedStateExample : MonoBehaviour
    {
        [SerializeField] private string valueKey = "gold";
        [SerializeField] private long earnedPerRun = 100;

        /// <summary>
        /// The game's own cloud save. serverValues is only a mirror of the server state.
        /// </summary>
        [Serializable]
        public class SaveGame
        {
            public int level = 1;
            public PlayerState serverValues = new PlayerState();
        }

        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[ValidatedStateExample] Could not connect to horizOn");
                    return;
                }

                if (!await UserManager.Instance.SignUpAnonymous("Player1"))
                {
                    Debug.LogError("[ValidatedStateExample] Anonymous sign up failed");
                    return;
                }

                SaveGame save = await CloudSaveManager.Instance.LoadObject<SaveGame>() ?? new SaveGame();

                // 1. On start the server wins: overwrite the mirror with the server state.
                PlayerState state = await ValidatedActionsManager.Instance.GetState();
                if (state != null)
                {
                    save.serverValues = state;
                }
                else
                {
                    // Offline or NOT_SUPPORTED: show the mirror, but never send it back as a balance.
                    Debug.LogWarning($"[ValidatedStateExample] GetState failed: {ValidatedActionsManager.Instance.LastErrorCode}, showing the cloud save copy");
                }
                Debug.Log($"[ValidatedStateExample] Gold on the server: {save.serverValues.GetBalance(valueKey)}");

                // 2. Values change only through earned of an accepted validated run.
                ValidatedRun run = await ValidatedActionsManager.Instance.StartRun();
                if (run == null)
                {
                    Debug.LogError($"[ValidatedStateExample] Start failed: {ValidatedActionsManager.Instance.LastErrorCode}");
                    return;
                }

                byte[] inputLog = { 1, 2, 3 }; // stands in for the recorded inputs of the run
                ValidatedSubmitResult result = await ValidatedActionsManager.Instance.SubmitValidated(
                    0, inputLog, earned: new[] { new EarnedValue(valueKey, earnedPerRun) });
                if (result == null)
                {
                    // UNKNOWN_VALUE_KEY, EARNED_ABOVE_MAX, INSUFFICIENT_BALANCE, ... (the ticket is used up)
                    Debug.LogWarning($"[ValidatedStateExample] Run rejected: {ValidatedActionsManager.Instance.LastErrorCode}");
                    return;
                }

                PlayerStateValue gold = result.state.GetValue(valueKey);
                if (gold != null)
                {
                    // credited < requested: the daily cap or the maximum balance clamped the credit.
                    // For a spend (negative amount) grant the purchase only when gold.IsFullyCredited.
                    Debug.Log($"[ValidatedStateExample] Run accepted: +{gold.credited} of {gold.requested} {valueKey}, " +
                              $"balance {gold.balance}, today {gold.earnedToday}" +
                              (gold.HasDailyCap ? $" / {gold.dailyCap}" : string.Empty));
                }

                // 3. Mirror the new state into the cloud save (CurrentState has no per-run fields).
                if (ValidatedActionsManager.Instance.CurrentState != null)
                {
                    save.serverValues = ValidatedActionsManager.Instance.CurrentState;
                }
                if (await CloudSaveManager.Instance.SaveObject(save))
                {
                    Debug.Log("[ValidatedStateExample] Mirrored into the cloud save");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ValidatedStateExample] Unexpected error: {e.Message}");
            }
        }
    }
}
