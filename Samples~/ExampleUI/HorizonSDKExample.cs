using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples
{
    /// <summary>
    /// Example script demonstrating horizOn SDK usage.
    /// Attach this to a GameObject in your scene to test the SDK.
    /// </summary>
    public class HorizonSDKExample : MonoBehaviour
    {
        [Header("Test Settings")]
        [SerializeField] private string TestEmail = "test@example.com";
        [SerializeField] private string TestPassword = "password123";

        private async void Start()
        {
            // Step 1: Initialize the SDK
            Debug.Log("=== Initializing horizOn SDK ===");
            HorizonApp.Initialize();

            // Step 2: Connect to the server
            HorizonApp.Log.Info("=== Connecting to horizOn Server ===");
            var server = new HorizonServer();
            bool connected = await server.Connect();

            if (!connected)
            {
                HorizonApp.Log.Error("Failed to connect to horizOn server");
                return;
            }

            HorizonApp.Log.Info($"Connected to: {server.ActiveHost}");

            // Step 3: Test authentication
            await TestAuthentication();

            // Step 4: Test other features
            await TestRemoteConfig();
            await TestNews();
            await TestLeaderboard();
            await TestCloudSave();
            await TestGiftCode();
            await TestFeedback();
        }

        private async System.Threading.Tasks.Task TestAuthentication()
        {
            HorizonApp.Log.Info("\n=== Testing User Authentication ===");

            // Try signing in first (in case user already exists)
            bool signedIn = await UserManager.Instance.SignInEmail(TestEmail, TestPassword);

            if (!signedIn)
            {
                // User doesn't exist, create account
                HorizonApp.Log.Info("User not found, creating new account...");
                bool signedUp = await UserManager.Instance.SignUpEmail(TestEmail, TestPassword, "Test User");

                if (!signedUp)
                {
                    HorizonApp.Log.Error("Failed to sign up");
                    return;
                }
            }

            // Check if signed in
            if (UserManager.Instance.IsSignedIn)
            {
                var user = UserManager.Instance.CurrentUser;
                HorizonApp.Log.Info($"Signed in as: {user.DisplayName} ({user.Email})");
                HorizonApp.Log.Info($"User ID: {user.UserId}");
                HorizonApp.Log.Info($"Auth Type: {user.AuthType}");
            }

            // Verify session
            bool isValid = await UserManager.Instance.CheckAuth();
            HorizonApp.Log.Info($"Session valid: {isValid}");
        }

        private async System.Threading.Tasks.Task TestRemoteConfig()
        {
            HorizonApp.Log.Info("\n=== Testing Remote Configuration ===");

            // Get all configs
            var configs = await RemoteConfigManager.Instance.GetAllConfigs();

            if (configs != null)
            {
                HorizonApp.Log.Info($"Loaded {configs.Count} configuration values");
                foreach (var config in configs)
                {
                    HorizonApp.Log.Info($"- {config.Key}: {config.Value}");
                }
            }

            // Get specific values with type conversion
            string welcomeMsg = await RemoteConfigManager.Instance.GetString("welcome_message", "Welcome!");
            int maxPlayers = await RemoteConfigManager.Instance.GetInt("max_players", 4);
            float difficulty = await RemoteConfigManager.Instance.GetFloat("difficulty", 1.0f);
            bool newFeature = await RemoteConfigManager.Instance.GetBool("new_feature_enabled", false);

            HorizonApp.Log.Info($"Welcome Message: {welcomeMsg}");
            HorizonApp.Log.Info($"Max Players: {maxPlayers}");
            HorizonApp.Log.Info($"Difficulty: {difficulty}");
            HorizonApp.Log.Info($"New Feature Enabled: {newFeature}");
        }

        private async System.Threading.Tasks.Task TestNews()
        {
            HorizonApp.Log.Info("\n=== Testing News System ===");

            var news = await NewsManager.Instance.LoadNews();

            if (news != null)
            {
                HorizonApp.Log.Info($"Loaded {news.Count} news entries");

                foreach (var entry in news)
                {
                    HorizonApp.Log.Info($"\n{entry.title}");
                    HorizonApp.Log.Info($"  {entry.message}");
                    HorizonApp.Log.Info($"  Released: {entry.releaseDate}");
                    HorizonApp.Log.Info($"  Language: {entry.languageCode}");
                }
            }
        }

        private async System.Threading.Tasks.Task TestLeaderboard()
        {
            HorizonApp.Log.Info("\n=== Testing Leaderboard ===");

            // Submit a random score
            long randomScore = Random.Range(100, 10000);
            bool submitResult = await LeaderboardManager.Instance.SubmitScore(randomScore);

            if (submitResult)
            {
                HorizonApp.Log.Info($"Score submitted: {randomScore}");
            }

            // Get top players
            var topPlayers = await LeaderboardManager.Instance.GetTop(5);

            if (topPlayers != null)
            {
                HorizonApp.Log.Info($"\nTop {topPlayers.Count} players:");
                foreach (var entry in topPlayers)
                {
                    HorizonApp.Log.Info($"{entry.position}. {entry.username}: {entry.score}");
                }
            }

            // Get user rank
            var rankResult = await LeaderboardManager.Instance.GetRank();

            if (rankResult != null)
            {
                HorizonApp.Log.Info($"\nYour position: {rankResult.position}");
                HorizonApp.Log.Info($"Your score: {rankResult.score}");
                HorizonApp.Log.Info($"Username: {rankResult.username}");
            }

            // Get entries around user
            var nearby = await LeaderboardManager.Instance.GetAround(3);

            if (nearby != null)
            {
                HorizonApp.Log.Info($"\nEntries around you:");
                foreach (var entry in nearby)
                {
                    HorizonApp.Log.Info($"{entry.position}. {entry.username}: {entry.score}");
                }
            }
        }

        private async System.Threading.Tasks.Task TestCloudSave()
        {
            HorizonApp.Log.Info("\n=== Testing Cloud Save ===");

            // Create test data
            var gameData = new TestGameData
            {
                PlayerLevel = Random.Range(1, 100),
                Coins = Random.Range(0, 10000),
                LastPlayTime = System.DateTime.UtcNow.ToString()
            };

            HorizonApp.Log.Info($"Saving data: Level {gameData.PlayerLevel}, Coins {gameData.Coins}");

            // Save object to cloud
            bool saved = await CloudSaveManager.Instance.SaveObject(gameData);

            if (saved)
            {
                HorizonApp.Log.Info("Data saved successfully");
            }

            // Load object from cloud
            var loadedData = await CloudSaveManager.Instance.LoadObject<TestGameData>();

            if (loadedData != null)
            {
                HorizonApp.Log.Info($"Data loaded: Level {loadedData.PlayerLevel}, Coins {loadedData.Coins}");
                HorizonApp.Log.Info($"Last play time: {loadedData.LastPlayTime}");
            }
        }

        private async System.Threading.Tasks.Task TestGiftCode()
        {
            HorizonApp.Log.Info("\n=== Testing Gift Codes ===");

            string testCode = "TESTCODE2024";

            // Validate code first
            var validation = await GiftCodeManager.Instance.Validate(testCode);

            if (validation.HasValue)
            {
                HorizonApp.Log.Info($"Gift code '{testCode}' is valid: {validation.Value}");
            }

            // Note: Actual redemption would consume the code
            // Uncomment to test redemption (code can only be used once)
            /*
            var redemption = await GiftCodeManager.Instance.Redeem(testCode);
            if (redemption != null && redemption.success)
            {
                HorizonApp.Log.Info("Code redeemed successfully!");
            }
            */
        }

        private async System.Threading.Tasks.Task TestFeedback()
        {
            HorizonApp.Log.Info("\n=== Testing Feedback ===");

            // Submit test feedback
            bool submitted = await FeedbackManager.Instance.SendGeneral(
                "This is a test feedback from the SDK example script. Everything is working great!",
                TestEmail
            );

            if (submitted)
            {
                HorizonApp.Log.Info("Feedback submitted successfully");
            }
        }

        [System.Serializable]
        private class TestGameData
        {
            public int PlayerLevel;
            public int Coins;
            public string LastPlayTime;
        }

        // Test context menu methods (right-click in Inspector)
        #if UNITY_EDITOR
        [ContextMenu("Test Sign Out")]
        private void TestSignOut()
        {
            if (UserManager.Instance != null)
            {
                UserManager.Instance.SignOut();
                HorizonApp.Log.Info("User signed out");
            }
        }

        [ContextMenu("Clear All Caches")]
        private void ClearAllCaches()
        {
            if (RemoteConfigManager.Instance != null)
                RemoteConfigManager.Instance.ClearCache();

            if (NewsManager.Instance != null)
                NewsManager.Instance.ClearCache();

            if (LeaderboardManager.Instance != null)
                LeaderboardManager.Instance.ClearCache();

            HorizonApp.Log.Info("All caches cleared");
        }
        #endif
    }
}
