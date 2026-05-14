using System;
using System.Threading.Tasks;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: Authentication (Anonymous + Email).
    ///
    /// What it does: initializes the SDK, connects, creates an anonymous account,
    /// then demonstrates the email sign up and sign in calls.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer,
    /// then attach this script to an empty GameObject and press Play.
    /// Expected Debug.Log output: "Anonymous user: <id>" followed by the email account result.
    ///
    /// Reference: docs/wiki/sdks/features/auth.md
    /// </summary>
    public class AuthExample : MonoBehaviour
    {
        [SerializeField] private string testEmail = "player@example.com";
        [SerializeField] private string testPassword = "password123";

        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                bool connected = await server.Connect();
                if (!connected)
                {
                    Debug.LogError("[AuthExample] Could not connect to horizOn");
                    return;
                }

                // Anonymous sign up: the token is cached so the session can be restored later.
                bool anon = await UserManager.Instance.SignUpAnonymous("Player1");
                if (anon)
                {
                    Debug.Log($"[AuthExample] Anonymous user: {UserManager.Instance.CurrentUser.UserId}");
                }

                // Email sign in, falling back to sign up when the account does not exist yet.
                bool signedIn = await UserManager.Instance.SignInEmail(testEmail, testPassword);
                if (!signedIn)
                {
                    signedIn = await UserManager.Instance.SignUpEmail(testEmail, testPassword, "Player1");
                }

                if (signedIn && UserManager.Instance.IsSignedIn)
                {
                    var user = UserManager.Instance.CurrentUser;
                    Debug.Log($"[AuthExample] Signed in as {user.DisplayName} ({user.AuthType})");
                }
                else
                {
                    Debug.LogError("[AuthExample] Email authentication failed");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AuthExample] Unexpected error: {e.Message}");
            }
        }
    }
}
