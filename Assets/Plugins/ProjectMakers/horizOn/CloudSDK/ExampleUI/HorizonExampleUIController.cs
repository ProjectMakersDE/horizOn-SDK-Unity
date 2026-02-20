using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Manager;
using LogType = PM.horizOn.Cloud.Enums.LogType;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PM.horizOn.Cloud.ExampleUI
{
    /// <summary>
    /// Controller for the horizOn SDK Example UI.
    /// Handles all user interactions and displays server responses.
    /// </summary>
    public class HorizonExampleUIController
    {
        private VisualElement _root;
        private HorizonServer _server;

        // UI Elements - Connection
        private Button _btnPlayMode;
        private Button _btnInit;
        private Button _btnConnect;
        private Label _lblConnectionStatus;
        private TextField _txtServerUrl;

        // UI Elements - Authentication
        private TextField _txtEmail;
        private TextField _txtPassword;
        private TextField _txtDisplayName;
        private TextField _txtAnonymousToken;
        private Button _btnSignUp;
        private Button _btnSignIn;
        private Button _btnSignInAnonymous;
        private Button _btnSignOut;
        private Button _btnCheckAuth;
        private Button _btnChangeName;
        private TextField _txtGoogleAuthCode;
        private Button _btnSignUpGoogle;
        private Button _btnSignInGoogle;
        private Label _lblAuthStatus;
        private TextField _txtAuthResponse;

        // UI Elements - Password Reset
        private TextField _txtForgotEmail;
        private Button _btnForgotPassword;
        private TextField _txtResetToken;
        private TextField _txtNewPassword;
        private Button _btnResetPassword;

        // UI Elements - Email Verification
        private TextField _txtVerifyToken;
        private Button _btnVerifyEmail;

        // UI Elements - Remote Config
        private Button _btnConfigAll;
        private Button _btnConfigClear;
        private TextField _txtConfigKey;
        private Button _btnConfigGet;
        private TextField _txtConfigResponse;

        // UI Elements - News
        private TextField _txtNewsLimit;
        private TextField _txtNewsLanguage;
        private Button _btnNewsLoad;
        private Button _btnNewsClear;
        private TextField _txtNewsResponse;

        // UI Elements - Leaderboard
        private TextField _txtScore;
        private Button _btnLeaderboardSubmit;
        private Button _btnLeaderboardTop;
        private Button _btnLeaderboardRank;
        private Button _btnLeaderboardAround;
        private TextField _txtLeaderboardResponse;

        // UI Elements - Cloud Save
        private DropdownField _ddSaveContentType;
        private TextField _txtSaveData;
        private Button _btnSaveSave;
        private Button _btnSaveLoad;
        private TextField _txtSaveResponse;

        // UI Elements - Gift Code
        private TextField _txtGiftCode;
        private Button _btnGiftCodeValidate;
        private Button _btnGiftCodeRedeem;
        private TextField _txtGiftCodeResponse;

        // UI Elements - Feedback
        private TextField _txtFeedbackTitle;
        private DropdownField _ddFeedbackCategory;
        private TextField _txtFeedbackMessage;
        private TextField _txtFeedbackEmail;
        private Button _btnFeedbackSubmit;
        private TextField _txtFeedbackResponse;

        // UI Elements - User Log
        private TextField _txtUserLogMessage;
        private DropdownField _ddUserLogType;
        private TextField _txtUserLogErrorCode;
        private Button _btnUserLogInfo;
        private Button _btnUserLogWarn;
        private Button _btnUserLogError;
        private Button _btnUserLogCreate;
        private TextField _txtUserLogResponse;

        public void Initialize(VisualElement root)
        {
            _root = root;
            _server = new HorizonServer();

            QueryUIElements();
            RegisterCallbacks();
            InitializeDropdowns();
            LoadCachedAnonymousToken();
            UpdateUIState();
        }

        private void QueryUIElements()
        {
            // Connection
            _btnPlayMode = _root.Q<Button>("btn-play-mode");
            _btnInit = _root.Q<Button>("btn-init");
            _btnConnect = _root.Q<Button>("btn-connect");
            _lblConnectionStatus = _root.Q<Label>("lbl-connection-status");
            _txtServerUrl = _root.Q<TextField>("txt-server-url");

            // Authentication
            _txtEmail = _root.Q<TextField>("txt-email");
            _txtPassword = _root.Q<TextField>("txt-password");
            _txtDisplayName = _root.Q<TextField>("txt-displayname");
            _txtAnonymousToken = _root.Q<TextField>("txt-anonymous-token");
            _btnSignUp = _root.Q<Button>("btn-signup");
            _btnSignIn = _root.Q<Button>("btn-signin");
            _btnSignInAnonymous = _root.Q<Button>("btn-signin-anonymous");
            _btnSignOut = _root.Q<Button>("btn-signout");
            _btnCheckAuth = _root.Q<Button>("btn-checkauth");
            _btnChangeName = _root.Q<Button>("btn-changename");
            _txtGoogleAuthCode = _root.Q<TextField>("txt-google-auth-code");
            _btnSignUpGoogle = _root.Q<Button>("btn-signup-google");
            _btnSignInGoogle = _root.Q<Button>("btn-signin-google");
            _lblAuthStatus = _root.Q<Label>("lbl-auth-status");
            _txtAuthResponse = _root.Q<TextField>("txt-auth-response");

            // Password Reset
            _txtForgotEmail = _root.Q<TextField>("txt-forgot-email");
            _btnForgotPassword = _root.Q<Button>("btn-forgot-password");
            _txtResetToken = _root.Q<TextField>("txt-reset-token");
            _txtNewPassword = _root.Q<TextField>("txt-new-password");
            _btnResetPassword = _root.Q<Button>("btn-reset-password");

            // Email Verification
            _txtVerifyToken = _root.Q<TextField>("txt-verify-token");
            _btnVerifyEmail = _root.Q<Button>("btn-verify-email");

            // Remote Config
            _btnConfigAll = _root.Q<Button>("btn-config-all");
            _btnConfigClear = _root.Q<Button>("btn-config-clear");
            _txtConfigKey = _root.Q<TextField>("txt-config-key");
            _btnConfigGet = _root.Q<Button>("btn-config-get");
            _txtConfigResponse = _root.Q<TextField>("txt-config-response");

            // News
            _txtNewsLimit = _root.Q<TextField>("txt-news-limit");
            _txtNewsLanguage = _root.Q<TextField>("txt-news-language");
            _btnNewsLoad = _root.Q<Button>("btn-news-load");
            _btnNewsClear = _root.Q<Button>("btn-news-clear");
            _txtNewsResponse = _root.Q<TextField>("txt-news-response");

            // Leaderboard
            _txtScore = _root.Q<TextField>("txt-score");
            _btnLeaderboardSubmit = _root.Q<Button>("btn-leaderboard-submit");
            _btnLeaderboardTop = _root.Q<Button>("btn-leaderboard-top");
            _btnLeaderboardRank = _root.Q<Button>("btn-leaderboard-rank");
            _btnLeaderboardAround = _root.Q<Button>("btn-leaderboard-around");
            _txtLeaderboardResponse = _root.Q<TextField>("txt-leaderboard-response");

            // Cloud Save
            _ddSaveContentType = _root.Q<DropdownField>("dd-save-content-type");
            _txtSaveData = _root.Q<TextField>("txt-save-data");
            _btnSaveSave = _root.Q<Button>("btn-save-save");
            _btnSaveLoad = _root.Q<Button>("btn-save-load");
            _txtSaveResponse = _root.Q<TextField>("txt-save-response");

            // Gift Code
            _txtGiftCode = _root.Q<TextField>("txt-giftcode");
            _btnGiftCodeValidate = _root.Q<Button>("btn-giftcode-validate");
            _btnGiftCodeRedeem = _root.Q<Button>("btn-giftcode-redeem");
            _txtGiftCodeResponse = _root.Q<TextField>("txt-giftcode-response");

            // Feedback
            _ddFeedbackCategory = _root.Q<DropdownField>("dd-feedback-category");
            _txtFeedbackTitle = _root.Q<TextField>("txt-feedback-title");
            _txtFeedbackMessage = _root.Q<TextField>("txt-feedback-message");
            _txtFeedbackEmail = _root.Q<TextField>("txt-feedback-email");
            _btnFeedbackSubmit = _root.Q<Button>("btn-feedback-submit");
            _txtFeedbackResponse = _root.Q<TextField>("txt-feedback-response");

            // User Log
            _txtUserLogMessage = _root.Q<TextField>("txt-userlog-message");
            _ddUserLogType = _root.Q<DropdownField>("dd-userlog-type");
            _txtUserLogErrorCode = _root.Q<TextField>("txt-userlog-errorcode");
            _btnUserLogInfo = _root.Q<Button>("btn-userlog-info");
            _btnUserLogWarn = _root.Q<Button>("btn-userlog-warn");
            _btnUserLogError = _root.Q<Button>("btn-userlog-error");
            _btnUserLogCreate = _root.Q<Button>("btn-userlog-create");
            _txtUserLogResponse = _root.Q<TextField>("txt-userlog-response");
        }

        private void RegisterCallbacks()
        {
            // Connection
            _btnPlayMode.clicked += OnPlayModeClicked;
            _btnInit.clicked += OnInitializeClicked;
            _btnConnect.clicked += OnConnectClicked;

            // Authentication
            _btnSignUp.clicked += OnSignUpClicked;
            _btnSignIn.clicked += OnSignInClicked;
            _btnSignInAnonymous.clicked += OnSignInAnonymousClicked;
            _btnSignOut.clicked += OnSignOutClicked;
            _btnCheckAuth.clicked += OnCheckAuthClicked;
            _btnChangeName.clicked += OnChangeNameClicked;
            _btnSignUpGoogle.clicked += OnSignUpGoogleClicked;
            _btnSignInGoogle.clicked += OnSignInGoogleClicked;

            // Password Reset
            _btnForgotPassword.clicked += OnForgotPasswordClicked;
            _btnResetPassword.clicked += OnResetPasswordClicked;

            // Email Verification
            _btnVerifyEmail.clicked += OnVerifyEmailClicked;

            // Remote Config
            _btnConfigAll.clicked += OnConfigAllClicked;
            _btnConfigClear.clicked += OnConfigClearClicked;
            _btnConfigGet.clicked += OnConfigGetClicked;

            // News
            _btnNewsLoad.clicked += OnNewsLoadClicked;
            _btnNewsClear.clicked += OnNewsClearClicked;

            // Leaderboard
            _btnLeaderboardSubmit.clicked += OnLeaderboardSubmitClicked;
            _btnLeaderboardTop.clicked += OnLeaderboardTopClicked;
            _btnLeaderboardRank.clicked += OnLeaderboardRankClicked;
            _btnLeaderboardAround.clicked += OnLeaderboardAroundClicked;

            // Cloud Save
            _btnSaveSave.clicked += OnSaveSaveClicked;
            _btnSaveLoad.clicked += OnSaveLoadClicked;

            // Gift Code
            _btnGiftCodeValidate.clicked += OnGiftCodeValidateClicked;
            _btnGiftCodeRedeem.clicked += OnGiftCodeRedeemClicked;

            // Feedback
            _btnFeedbackSubmit.clicked += OnFeedbackSubmitClicked;

            // User Log
            _btnUserLogInfo.clicked += OnUserLogInfoClicked;
            _btnUserLogWarn.clicked += OnUserLogWarnClicked;
            _btnUserLogError.clicked += OnUserLogErrorClicked;
            _btnUserLogCreate.clicked += OnUserLogCreateClicked;
        }

        private void InitializeDropdowns()
        {
            _ddFeedbackCategory.choices = new List<string> { "GENERAL", "BUG", "FEATURE" };
            _ddFeedbackCategory.value = "GENERAL";

            _ddUserLogType.choices = new List<string> { "INFO", "WARN", "ERROR" };
            _ddUserLogType.value = "INFO";

            _ddSaveContentType.choices = new List<string> { "JSON (application/json)", "Binary (application/octet-stream)" };
            _ddSaveContentType.value = "JSON (application/json)";
        }

        private void UpdateUIState()
        {
#if UNITY_EDITOR
            bool isInPlayMode = EditorApplication.isPlaying;
#else
            bool isInPlayMode = true; // Always true in runtime builds
#endif
            bool sdkInitialized = HorizonApp.IsInitialized;
            bool isConnected = _server is { IsConnected: true };
            bool isSignedIn = UserManager.Instance != null && UserManager.Instance.IsSignedIn;

            // Update connection status
            if (isConnected)
            {
                _lblConnectionStatus.text = $"Status: Connected to {_server.ActiveHost}";
                _txtServerUrl.value = _server.ActiveHost;
            }
            else if (sdkInitialized)
            {
                _lblConnectionStatus.text = "Status: SDK Initialized, Not Connected";
            }
            else if (isInPlayMode)
            {
                _lblConnectionStatus.text = "Status: In Play Mode - Not Initialized";
            }
            else
            {
                _lblConnectionStatus.text = "Status: Editor Mode - Click 'Enter Play Mode'";
            }

            // Update auth status
            if (isSignedIn && UserManager.Instance.CurrentUser != null)
            {
                var user = UserManager.Instance.CurrentUser;
                string userInfo = user.Email ?? user.UserId;
                _lblAuthStatus.text = $"Signed in as: {user.DisplayName} ({userInfo})";
            }
            else
            {
                _lblAuthStatus.text = "Not Signed In";
            }

            // Enable/disable buttons based on state
            _btnPlayMode.SetEnabled(!isInPlayMode);
            _btnInit.SetEnabled(isInPlayMode && !sdkInitialized);
            _btnConnect.SetEnabled(sdkInitialized);

            bool requiresAuth = isConnected && isSignedIn;
            _btnCheckAuth.SetEnabled(isSignedIn);
            _btnChangeName.SetEnabled(isSignedIn);
            _btnSignOut.SetEnabled(isSignedIn);
            _btnLeaderboardSubmit.SetEnabled(requiresAuth);
            _btnLeaderboardRank.SetEnabled(requiresAuth);
            _btnLeaderboardAround.SetEnabled(requiresAuth);
            _btnSaveSave.SetEnabled(requiresAuth);
            _btnSaveLoad.SetEnabled(requiresAuth);
            _btnGiftCodeValidate.SetEnabled(requiresAuth);
            _btnGiftCodeRedeem.SetEnabled(requiresAuth);
            _btnFeedbackSubmit.SetEnabled(requiresAuth);
            _btnUserLogInfo.SetEnabled(requiresAuth);
            _btnUserLogWarn.SetEnabled(requiresAuth);
            _btnUserLogError.SetEnabled(requiresAuth);
            _btnUserLogCreate.SetEnabled(requiresAuth);

            // Update play mode button text
            if (_btnPlayMode != null)
            {
                _btnPlayMode.text = isInPlayMode ? "Exit Play Mode" : "Enter Play Mode";
            }
        }

        /// <summary>
        /// Loads the cached anonymous token into the UI field if available.
        /// </summary>
        private void LoadCachedAnonymousToken()
        {
            if (UserManager.Instance != null && UserManager.Instance.HasCachedAnonymousToken())
            {
                string cachedToken = PlayerPrefs.GetString("horizOn_AnonymousToken", "");
                if (!string.IsNullOrEmpty(cachedToken))
                {
                    _txtAnonymousToken.value = cachedToken;
                }
            }
        }

        /// <summary>
        /// Called when play mode state changes in the editor.
        /// </summary>
        public void OnPlayModeChanged()
        {
            UpdateUIState();
        }

        // Connection Handlers
        private void OnPlayModeClicked()
        {
#if UNITY_EDITOR
            if (EditorApplication.isPlaying)
            {
                // Exit play mode
                EditorApplication.isPlaying = false;
            }
            else
            {
                // Enter play mode
                EditorApplication.isPlaying = true;
            }
#endif
        }

        private void OnInitializeClicked()
        {
            bool success = HorizonApp.Initialize();
            _txtServerUrl.value = success ? "SDK Initialized" : "Failed to initialize";
            UpdateUIState();
        }

        private async void OnConnectClicked()
        {
            _lblConnectionStatus.text = "Status: Connecting...";
            bool connected = await _server.Connect();

            if (connected)
            {
                _lblConnectionStatus.text = $"Status: Connected to {_server.ActiveHost}";
                _txtServerUrl.value = _server.ActiveHost;
            }
            else
            {
                _lblConnectionStatus.text = "Status: Connection Failed";
                _txtServerUrl.value = "Connection failed";
            }

            UpdateUIState();
        }

        // Authentication Handlers
        private async void OnSignUpClicked()
        {
            string email = _txtEmail.value;
            string password = _txtPassword.value;
            string displayName = _txtDisplayName.value;

            // Check if email/password provided (for email auth) or empty (for anonymous)
            bool isEmailAuth = !string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(password);

            if (!isEmailAuth)
            {
                _txtAuthResponse.value = "Error: Email and password are required for email sign up.\nUse 'Anonymous' button for anonymous authentication.";
                return;
            }

            _txtAuthResponse.value = "Signing up...";
            bool success = await UserManager.Instance.SignUpEmail(email, password, displayName);

            if (success)
            {
                var user = UserManager.Instance.CurrentUser;
                _txtDisplayName.value = user.DisplayName;
                _txtAuthResponse.value = $"Success!\nUser ID: {user.UserId}\nEmail: {user.Email}\nDisplay Name: {user.DisplayName}\nAuth Type: {user.AuthType}\nAccess Token: {user.AccessToken}";
            }
            else
            {
                _txtAuthResponse.value = "Sign up failed. Check console for details.";
            }

            UpdateUIState();
        }

        private async void OnSignInClicked()
        {
            string email = _txtEmail.value;
            string password = _txtPassword.value;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                _txtAuthResponse.value = "Error: Email and password are required for email sign in.\nUse 'Anonymous' button for anonymous authentication.";
                return;
            }

            _txtAuthResponse.value = "Signing in...";
            bool success = await UserManager.Instance.SignInEmail(email, password);

            if (success)
            {
                var user = UserManager.Instance.CurrentUser;
                _txtDisplayName.value = user.DisplayName;
                _txtAuthResponse.value = $"Success!\nUser ID: {user.UserId}\nEmail: {user.Email}\nDisplay Name: {user.DisplayName}\nAuth Type: {user.AuthType}\nAccess Token: {user.AccessToken}";
            }
            else
            {
                _txtAuthResponse.value = "Sign in failed. Check console for details.";
            }

            UpdateUIState();
        }

        private async void OnSignInAnonymousClicked()
        {
            bool success = false;
            string manualToken = _txtAnonymousToken.value;

            // Check if user provided a manual token
            if (!string.IsNullOrWhiteSpace(manualToken))
            {
                // User wants to sign in with an existing token
                _txtAuthResponse.value = "Signing in with provided anonymous token...";
                success = await UserManager.Instance.SignInAnonymous(manualToken);

                if (success)
                {
                    var user = UserManager.Instance.CurrentUser;
                    _txtDisplayName.value = user.DisplayName;
                    _txtAuthResponse.value = $"Signed in with anonymous token!\nUser ID: {user.UserId}\nDisplay Name: {user.DisplayName}\nAuth Type: {user.AuthType}\nAnonymous Token: {user.AnonymousToken}";
                }
                else
                {
                    _txtAuthResponse.value = "Anonymous sign in failed. Check console for details.";
                }
            }
            else
            {
                // Check if there's a cached token first
                if (UserManager.Instance.HasCachedAnonymousToken())
                {
                    _txtAuthResponse.value = "Restoring anonymous session...";
                    success = await UserManager.Instance.RestoreAnonymousSession();

                    if (success)
                    {
                        var user = UserManager.Instance.CurrentUser;
                        _txtDisplayName.value = user.DisplayName;
                        _txtAuthResponse.value = $"Anonymous session restored!\nUser ID: {user.UserId}\nDisplay Name: {user.DisplayName}\nAuth Type: {user.AuthType}\nAnonymous Token: {user.AnonymousToken}";
                    }
                    else
                    {
                        // Cached token invalid, create new user
                        _txtAuthResponse.value = "Cached token invalid, creating new anonymous user...";
                        string displayName = string.IsNullOrWhiteSpace(_txtDisplayName.value) ? null : _txtDisplayName.value;
                        success = await UserManager.Instance.SignUpAnonymous(displayName);

                        if (success)
                        {
                            var user = UserManager.Instance.CurrentUser;
                            _txtDisplayName.value = user.DisplayName;
                            _txtAuthResponse.value = $"Anonymous user created!\nUser ID: {user.UserId}\nDisplay Name: {user.DisplayName}\nAuth Type: {user.AuthType}\nAnonymous Token: {user.AnonymousToken}";
                            _txtAnonymousToken.value = user.AnonymousToken;
                        }
                        else
                        {
                            _txtAuthResponse.value = "Anonymous sign up failed. Check console for details.";
                        }
                    }
                }
                else
                {
                    // No cached token, create new user
                    _txtAuthResponse.value = "Creating new anonymous user...";
                    string displayName = string.IsNullOrWhiteSpace(_txtDisplayName.value) ? null : _txtDisplayName.value;
                    success = await UserManager.Instance.SignUpAnonymous(displayName);

                    if (success)
                    {
                        var user = UserManager.Instance.CurrentUser;
                        _txtDisplayName.value = user.DisplayName;
                        _txtAuthResponse.value = $"Anonymous user created!\nUser ID: {user.UserId}\nDisplay Name: {user.DisplayName}\nAuth Type: {user.AuthType}\nAnonymous Token: {user.AnonymousToken}";
                        _txtAnonymousToken.value = user.AnonymousToken;
                    }
                    else
                    {
                        _txtAuthResponse.value = "Anonymous sign up failed. Check console for details.";
                    }
                }
            }

            UpdateUIState();
        }

        private void OnSignOutClicked()
        {
            UserManager.Instance.SignOut();
            _txtAuthResponse.value = "Signed out successfully";
            UpdateUIState();
        }

        private async void OnCheckAuthClicked()
        {
            _txtAuthResponse.value = "Checking authentication...";
            bool isValid = await UserManager.Instance.CheckAuth();
            _txtAuthResponse.value = isValid ? "Session is valid!" : "Session is invalid or expired";
            UpdateUIState();
        }

        private async void OnChangeNameClicked()
        {
            string newName = _txtDisplayName.value;

            if (string.IsNullOrWhiteSpace(newName))
            {
                _txtAuthResponse.value = "Error: Please enter a new display name in the 'Display Name' field.";
                return;
            }

            _txtAuthResponse.value = "Changing display name...";
            bool success = await UserManager.Instance.ChangeName(newName);

            if (success)
            {
                var user = UserManager.Instance.CurrentUser;
                _txtAuthResponse.value = $"Display name changed successfully!\nNew Name: {user.DisplayName}";
            }
            else
            {
                _txtAuthResponse.value = "Failed to change display name. Check console for details.";
            }

            UpdateUIState();
        }

        // Google Authentication Handlers
        private async void OnSignUpGoogleClicked()
        {
            string authCode = _txtGoogleAuthCode.value;

            if (string.IsNullOrWhiteSpace(authCode))
            {
                _txtAuthResponse.value = "Error: Google authorization code is required.\nObtain it from the Google Sign-In SDK.";
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(_txtDisplayName.value) ? null : _txtDisplayName.value;

            _txtAuthResponse.value = "Signing up with Google...";
            bool success = await UserManager.Instance.SignUpGoogle(authCode, username: displayName);

            if (success)
            {
                var user = UserManager.Instance.CurrentUser;
                _txtDisplayName.value = user.DisplayName;
                _txtAuthResponse.value = $"Google sign up successful!\nUser ID: {user.UserId}\nEmail: {user.Email}\nDisplay Name: {user.DisplayName}\nAuth Type: {user.AuthType}\nAccess Token: {user.AccessToken}";
            }
            else
            {
                _txtAuthResponse.value = "Google sign up failed. Check console for details.";
            }

            UpdateUIState();
        }

        private async void OnSignInGoogleClicked()
        {
            string authCode = _txtGoogleAuthCode.value;

            if (string.IsNullOrWhiteSpace(authCode))
            {
                _txtAuthResponse.value = "Error: Google authorization code is required.\nObtain it from the Google Sign-In SDK.";
                return;
            }

            _txtAuthResponse.value = "Signing in with Google...";
            bool success = await UserManager.Instance.SignInGoogle(authCode);

            if (success)
            {
                var user = UserManager.Instance.CurrentUser;
                _txtDisplayName.value = user.DisplayName;
                _txtAuthResponse.value = $"Google sign in successful!\nUser ID: {user.UserId}\nEmail: {user.Email}\nDisplay Name: {user.DisplayName}\nAuth Type: {user.AuthType}\nAccess Token: {user.AccessToken}";
            }
            else
            {
                _txtAuthResponse.value = "Google sign in failed. Check console for details.\nUser may not exist - try Sign Up Google first.";
            }

            UpdateUIState();
        }

        // Password Reset Handlers
        private async void OnForgotPasswordClicked()
        {
            string email = _txtForgotEmail.value;

            if (string.IsNullOrWhiteSpace(email))
            {
                _txtAuthResponse.value = "Error: Please enter an email address for password reset.";
                return;
            }

            _txtAuthResponse.value = "Sending password reset email...";
            bool success = await UserManager.Instance.ForgotPassword(email);

            if (success)
            {
                _txtAuthResponse.value = $"Password reset email sent to: {email}\n\nCheck your email for the reset token.";
            }
            else
            {
                _txtAuthResponse.value = "Failed to send password reset email. Check console for details.";
            }
        }

        private async void OnResetPasswordClicked()
        {
            string token = _txtResetToken.value;
            string newPassword = _txtNewPassword.value;

            if (string.IsNullOrWhiteSpace(token))
            {
                _txtAuthResponse.value = "Error: Please enter the reset token from your email.";
                return;
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                _txtAuthResponse.value = "Error: Please enter a new password.";
                return;
            }

            if (newPassword.Length < 4 || newPassword.Length > 128)
            {
                _txtAuthResponse.value = "Error: New password must be between 4 and 128 characters.";
                return;
            }

            _txtAuthResponse.value = "Resetting password...";
            bool success = await UserManager.Instance.ResetPassword(token, newPassword);

            if (success)
            {
                _txtAuthResponse.value = "Password reset successfully!\n\nYou can now sign in with your new password.";
                _txtResetToken.value = "";
                _txtNewPassword.value = "";
            }
            else
            {
                _txtAuthResponse.value = "Failed to reset password. Check console for details.\n\nThe token may be invalid or expired.";
            }
        }

        // Email Verification Handler
        private async void OnVerifyEmailClicked()
        {
            string token = _txtVerifyToken.value;

            if (string.IsNullOrWhiteSpace(token))
            {
                _txtAuthResponse.value = "Error: Please enter the verification token from your email.";
                return;
            }

            _txtAuthResponse.value = "Verifying email...";
            bool success = await UserManager.Instance.VerifyEmail(token);

            if (success)
            {
                _txtAuthResponse.value = "Email verified successfully!\n\nYour account is now fully activated.";
                _txtVerifyToken.value = "";
            }
            else
            {
                _txtAuthResponse.value = "Failed to verify email. Check console for details.\n\nThe token may be invalid or expired.";
            }

            UpdateUIState();
        }

        // Remote Config Handlers
        private async void OnConfigAllClicked()
        {
            _txtConfigResponse.value = "Loading all configs...";
            var configs = await RemoteConfigManager.Instance.GetAllConfigs(useCache: false);

            if (configs != null && configs.Count > 0)
            {
                string response = $"Loaded {configs.Count} configuration values:\n\n";
                foreach (var config in configs)
                {
                    response += $"{config.Key}: {config.Value}\n";
                }
                _txtConfigResponse.value = response;
            }
            else
            {
                _txtConfigResponse.value = "No configs found or request failed";
            }
        }

        private void OnConfigClearClicked()
        {
            RemoteConfigManager.Instance.ClearCache();
            _txtConfigResponse.value = "Cache cleared";
        }

        private async void OnConfigGetClicked()
        {
            string key = _txtConfigKey.value;
            if (string.IsNullOrEmpty(key))
            {
                _txtConfigResponse.value = "Error: Config key is required";
                return;
            }

            _txtConfigResponse.value = $"Loading config '{key}'...";
            var config = await RemoteConfigManager.Instance.GetConfig(key, useCache: false);

            if (config != null)
            {
                _txtConfigResponse.value = $"Key: {key}\nValue: {config}";
            }
            else
            {
                _txtConfigResponse.value = $"Config '{key}' not found";
            }
        }

        // News Handlers
        private async void OnNewsLoadClicked()
        {
            // Parse limit from input field
            int limit = 20; // default
            if (!string.IsNullOrEmpty(_txtNewsLimit.value) && int.TryParse(_txtNewsLimit.value, out int parsedLimit))
            {
                limit = parsedLimit;
            }

            // Get language code from input field (can be empty)
            string languageCode = string.IsNullOrWhiteSpace(_txtNewsLanguage.value) ? null : _txtNewsLanguage.value.Trim();

            _txtNewsResponse.value = "Loading news...";
            var news = await NewsManager.Instance.LoadNews(limit: limit, languageCode: languageCode, useCache: false);

            if (news != null && news.Count > 0)
            {
                string response = $"Loaded {news.Count} news entries:\n\n";
                foreach (var entry in news)
                {
                    response += $"{entry.title}\n";
                    response += $"  {entry.message}\n";
                    response += $"  Released: {entry.releaseDate}\n";
                    response += $"  Language: {entry.languageCode}\n\n";
                }
                _txtNewsResponse.value = response;
            }
            else
            {
                _txtNewsResponse.value = "No news found or request failed";
            }
        }

        private void OnNewsClearClicked()
        {
            NewsManager.Instance.ClearCache();
            _txtNewsResponse.value = "News cache cleared";
        }

        // Leaderboard Handlers
        private async void OnLeaderboardSubmitClicked()
        {
            if (!long.TryParse(_txtScore.value, out long score))
            {
                _txtLeaderboardResponse.value = "Error: Invalid score";
                return;
            }

            _txtLeaderboardResponse.value = "Submitting score...";
            bool success = await LeaderboardManager.Instance.SubmitScore(score);

            if (success)
            {
                _txtLeaderboardResponse.value = $"Score submitted successfully!\nScore: {score}";
            }
            else
            {
                _txtLeaderboardResponse.value = "Score submission failed";
            }
        }

        private async void OnLeaderboardTopClicked()
        {
            _txtLeaderboardResponse.value = "Loading top players...";
            var entries = await LeaderboardManager.Instance.GetTop(10, useCache: false);

            if (entries != null && entries.Count > 0)
            {
                string response = $"Top {entries.Count} players:\n\n";
                foreach (var entry in entries)
                {
                    response += $"{entry.position}. {entry.username}: {entry.score}\n";
                }
                _txtLeaderboardResponse.value = response;
            }
            else
            {
                _txtLeaderboardResponse.value = "No entries found";
            }
        }

        private async void OnLeaderboardRankClicked()
        {
            _txtLeaderboardResponse.value = "Getting your rank...";
            var result = await LeaderboardManager.Instance.GetRank();

            if (result != null)
            {
                _txtLeaderboardResponse.value = $"Your Position: {result.position}\nYour Score: {result.score}\nUsername: {result.username}";
            }
            else
            {
                _txtLeaderboardResponse.value = "Failed to get rank";
            }
        }

        private async void OnLeaderboardAroundClicked()
        {
            _txtLeaderboardResponse.value = "Loading nearby players...";
            var entries = await LeaderboardManager.Instance.GetAround(3, useCache: false);

            if (entries != null && entries.Count > 0)
            {
                string response = $"Players around you:\n\n";
                foreach (var entry in entries)
                {
                    response += $"{entry.position}. {entry.username}: {entry.score}\n";
                }
                _txtLeaderboardResponse.value = response;
            }
            else
            {
                _txtLeaderboardResponse.value = "No entries found";
            }
        }

        // Cloud Save Handlers
        private async void OnSaveSaveClicked()
        {
            string data = _txtSaveData.value;

            if (string.IsNullOrEmpty(data))
            {
                _txtSaveResponse.value = "Error: Data is required";
                return;
            }

            bool useBinary = _ddSaveContentType.value.Contains("Binary");

            if (useBinary)
            {
                // Binary mode: treat input as Base64 or convert UTF-8 text to bytes
                byte[] binaryData;
                try
                {
                    // Try to decode as Base64 first
                    binaryData = Convert.FromBase64String(data);
                    _txtSaveResponse.value = "Saving binary data (Base64 decoded)...";
                }
                catch
                {
                    // Not valid Base64, convert text to UTF-8 bytes
                    binaryData = System.Text.Encoding.UTF8.GetBytes(data);
                    _txtSaveResponse.value = "Saving binary data (UTF-8 encoded)...";
                }

                bool success = await CloudSaveManager.Instance.SaveBytes(binaryData);

                if (success)
                {
                    _txtSaveResponse.value = $"Binary data saved successfully!\nContent-Type: application/octet-stream\nSize: {binaryData.Length} bytes";
                }
                else
                {
                    _txtSaveResponse.value = "Binary save failed";
                }
            }
            else
            {
                // JSON mode
                _txtSaveResponse.value = "Saving JSON data...";
                bool success = await CloudSaveManager.Instance.Save(data);

                if (success)
                {
                    _txtSaveResponse.value = $"JSON data saved successfully!\nContent-Type: application/json\nSize: {data.Length} characters";
                }
                else
                {
                    _txtSaveResponse.value = "JSON save failed";
                }
            }
        }

        private async void OnSaveLoadClicked()
        {
            bool useBinary = _ddSaveContentType.value.Contains("Binary");

            if (useBinary)
            {
                // Binary mode
                _txtSaveResponse.value = "Loading binary data...";
                byte[] binaryData = await CloudSaveManager.Instance.LoadBytes();

                if (binaryData != null && binaryData.Length > 0)
                {
                    // Display as Base64 for binary data
                    string base64Data = Convert.ToBase64String(binaryData);
                    _txtSaveResponse.value = $"Binary data loaded:\nContent-Type: application/octet-stream\nSize: {binaryData.Length} bytes\n\nBase64:\n{base64Data}";
                    _txtSaveData.value = base64Data;
                }
                else
                {
                    _txtSaveResponse.value = "No binary data found or load failed";
                }
            }
            else
            {
                // JSON mode
                _txtSaveResponse.value = "Loading JSON data...";
                string data = await CloudSaveManager.Instance.Load();

                if (!string.IsNullOrEmpty(data))
                {
                    _txtSaveResponse.value = $"JSON data loaded:\nContent-Type: application/json\nSize: {data.Length} characters\n\n{data}";
                    _txtSaveData.value = data;
                }
                else
                {
                    _txtSaveResponse.value = "No JSON data found or load failed";
                }
            }
        }

        // Gift Code Handlers
        private async void OnGiftCodeValidateClicked()
        {
            string code = _txtGiftCode.value;

            if (string.IsNullOrEmpty(code))
            {
                _txtGiftCodeResponse.value = "Error: Gift code is required";
                return;
            }

            _txtGiftCodeResponse.value = "Validating code...";
            var result = await GiftCodeManager.Instance.Validate(code);

            if (result.HasValue)
            {
                _txtGiftCodeResponse.value = $"Code: {code}\nValid: {result.Value}";
            }
            else
            {
                _txtGiftCodeResponse.value = "Validation request failed";
            }
        }

        private async void OnGiftCodeRedeemClicked()
        {
            string code = _txtGiftCode.value;

            if (string.IsNullOrEmpty(code))
            {
                _txtGiftCodeResponse.value = "Error: Gift code is required";
                return;
            }

            _txtGiftCodeResponse.value = "Redeeming code...";
            var result = await GiftCodeManager.Instance.Redeem(code);

            if (result != null && result.success)
            {
                _txtGiftCodeResponse.value = $"Code redeemed successfully!\n\nGift Data:\n{result.giftData}";
            }
            else
            {
                _txtGiftCodeResponse.value = result != null ? $"Redemption failed: {result.message}" : "Redemption failed";
            }
        }

        // Feedback Handlers
        private async void OnFeedbackSubmitClicked()
        {
            string title = _txtFeedbackTitle.value;
            string category = _ddFeedbackCategory.value;
            string message = _txtFeedbackMessage.value;
            string email = _txtFeedbackEmail.value;

            if (string.IsNullOrEmpty(title))
            {
                _txtFeedbackResponse.value = "Error: Title is required";
                return;
            }

            if (string.IsNullOrEmpty(message))
            {
                _txtFeedbackResponse.value = "Error: Message is required";
                return;
            }

            _txtFeedbackResponse.value = "Submitting feedback...";
            bool success = await FeedbackManager.Instance.Submit(title, category, message, email, includeDeviceInfo: true);

            if (success)
            {
                _txtFeedbackResponse.value = $"Feedback submitted successfully!\n\nTitle: {title}\nCategory: {category}\nMessage: {message}";
                _txtFeedbackTitle.value = "";
                _txtFeedbackMessage.value = "";
            }
            else
            {
                _txtFeedbackResponse.value = "Feedback submission failed";
            }
        }

        // User Log Handlers
        private async void OnUserLogInfoClicked()
        {
            await CreateUserLog(LogType.INFO);
        }

        private async void OnUserLogWarnClicked()
        {
            await CreateUserLog(LogType.WARN);
        }

        private async void OnUserLogErrorClicked()
        {
            await CreateUserLog(LogType.ERROR);
        }

        private async void OnUserLogCreateClicked()
        {
            string typeValue = _ddUserLogType.value;
            LogType logType = LogType.INFO;

            if (typeValue == "WARN")
                logType = LogType.WARN;
            else if (typeValue == "ERROR")
                logType = LogType.ERROR;

            await CreateUserLog(logType);
        }

        private async System.Threading.Tasks.Task CreateUserLog(LogType logType)
        {
            string message = _txtUserLogMessage.value;
            string errorCode = _txtUserLogErrorCode.value;

            if (string.IsNullOrEmpty(message))
            {
                _txtUserLogResponse.value = "Error: Message is required";
                return;
            }

            _txtUserLogResponse.value = $"Creating {logType} log...";

            var result = await UserLogManager.Instance.CreateLog(
                logType,
                message,
                string.IsNullOrWhiteSpace(errorCode) ? null : errorCode
            );

            if (result != null)
            {
                _txtUserLogResponse.value = $"Log created successfully!\n\nID: {result.id}\nCreated At: {result.createdAt}\nType: {logType}\nMessage: {message}";
                if (!string.IsNullOrEmpty(errorCode))
                {
                    _txtUserLogResponse.value += $"\nError Code: {errorCode}";
                }
            }
            else
            {
                _txtUserLogResponse.value = "Log creation failed. Check console for details.\n\nNote: User log feature is not available for FREE accounts.";
            }
        }
    }
}
