using System;
using System.Collections.Generic;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: Player Profile.
    ///
    /// What it does: connects, signs in anonymously, loads the player's profile with the
    /// cosmetics catalog, picks the first available avatar and badge, saves the profile and
    /// prints the top leaderboard entries with their profile.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer,
    /// add a few cosmetics (avatar, frame, badge) for the API key in the horizOn Dashboard,
    /// then attach the script to an empty GameObject and press Play.
    /// Expected Debug.Log output: "Catalog: N cosmetics", "Profile saved: avatar=...",
    /// then one line per leaderboard entry.
    ///
    /// Reference: docs/wiki/sdks/features/player-profile.md
    /// </summary>
    public class PlayerProfileExample : MonoBehaviour
    {
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[PlayerProfileExample] Could not connect to horizOn");
                    return;
                }

                // Profile calls require a signed in player (Bearer session).
                if (!await UserManager.Instance.SignUpAnonymous("Player1"))
                {
                    Debug.LogError("[PlayerProfileExample] Anonymous sign up failed");
                    return;
                }

                PlayerProfileResponse profile = await PlayerProfileManager.Instance.GetProfile();
                if (profile == null)
                {
                    Debug.LogError($"[PlayerProfileExample] Loading failed: {PlayerProfileManager.Instance.LastErrorCode}");
                    return;
                }

                Debug.Log($"[PlayerProfileExample] Catalog: {profile.cosmetics.Length} cosmetics, unlocks: {profile.unlocks.Length}");

                // Build a picker from the catalog: only "available" entries may be selected.
                string avatarId = FirstAvailable(profile.GetCosmetics("avatar"));
                string badgeId = FirstAvailable(profile.GetCosmetics("badge"));
                var badges = new List<string>();
                if (badgeId != null)
                {
                    badges.Add(badgeId);
                }

                // PUT replaces the whole profile: keep the current frame by passing it again.
                PlayerProfileResponse updated = await PlayerProfileManager.Instance.SetProfile(
                    avatarId, profile.profile.frameId, badges);
                if (updated == null)
                {
                    // For example COSMETIC_LOCKED, COSMETIC_NOT_FOUND, INVALID_BADGES.
                    Debug.LogWarning($"[PlayerProfileExample] Saving failed: {PlayerProfileManager.Instance.LastErrorCode}");
                    return;
                }

                Debug.Log($"[PlayerProfileExample] Profile saved: avatar={updated.profile.avatarId}, " +
                          $"frame={updated.profile.frameId}, badges={string.Join(",", updated.profile.badges)}");

                // Leaderboard entries carry the profile of each player.
                var top = await LeaderboardManager.Instance.GetTop(10);
                if (top == null)
                {
                    return;
                }

                foreach (var entry in top)
                {
                    string avatar = entry.profile.HasAvatar ? entry.profile.avatarId : "(none)";
                    Debug.Log($"[PlayerProfileExample] #{entry.position} {entry.username} {entry.score} avatar={avatar}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerProfileExample] Unexpected error: {e.Message}");
            }
        }

        private static string FirstAvailable(List<PlayerCosmetic> cosmetics)
        {
            foreach (var cosmetic in cosmetics)
            {
                if (cosmetic.available)
                {
                    return cosmetic.id;
                }
            }
            return null;
        }
    }
}
