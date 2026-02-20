using System;
using System.Threading.Tasks;
using UnityEngine;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Data;
using PM.horizOn.Cloud.Objects.Network.Requests;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Service;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Manager for user authentication and account management.
    /// Handles signup, signin, email verification, password reset, and session management.
    /// </summary>
    public class UserManager : BaseManager<UserManager>
    {
        private UserData _currentUser;

        /// <summary>
        /// Get the current authenticated user.
        /// </summary>
        public UserData CurrentUser => _currentUser;

        /// <summary>
        /// Check if a user is currently signed in.
        /// </summary>
        public bool IsSignedIn => _currentUser != null && _currentUser.IsValid();

        protected override void OnInit()
        {
            base.OnInit();
            _currentUser = new UserData();

            // Try to load cached session
            LoadCachedSession();
        }

        // ===== SIGN UP =====

        /// <summary>
        /// Sign up with anonymous authentication.
        /// </summary>
        /// <param name="displayName">Optional display name</param>
        /// <param name="anonymousToken">Optional anonymous token. If not provided, a new unique token will be generated.</param>
        /// <returns>True if signup succeeded, false otherwise</returns>
        public async Task<bool> SignUpAnonymous(string displayName = null, string anonymousToken = null)
        {
            // Check if already signed in
            if (IsSignedIn)
            {
                HorizonApp.Log.Warning("User is already signed in. Sign out first to create a new anonymous account.");
                return false;
            }

            // If no token provided, generate a new unique one (max 32 chars per API spec)
            if (string.IsNullOrEmpty(anonymousToken))
            {
                anonymousToken = System.Guid.NewGuid().ToString("N");
            }

            return await SignUp(SignUpRequest.CreateAnonymous(displayName, anonymousToken));
        }

        /// <summary>
        /// Sign up with email and password.
        /// </summary>
        /// <param name="email">User email</param>
        /// <param name="password">User password</param>
        /// <param name="username">Optional username</param>
        /// <returns>True if signup succeeded, false otherwise</returns>
        public async Task<bool> SignUpEmail(string email, string password, string username = null)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                HorizonApp.Log.Error("Email and password are required");
                return false;
            }

            return await SignUp(SignUpRequest.CreateEmail(email, password, username));
        }

        /// <summary>
        /// Sign up with Google authentication.
        /// </summary>
        /// <param name="googleAuthorizationCode">Google authorization code from Google Sign-In SDK</param>
        /// <param name="redirectUri">OAuth redirect URI. Leave empty for mobile apps (Android/iOS).</param>
        /// <param name="username">Optional username</param>
        /// <returns>True if signup succeeded, false otherwise</returns>
        public async Task<bool> SignUpGoogle(string googleAuthorizationCode, string redirectUri = "", string username = null)
        {
            if (string.IsNullOrEmpty(googleAuthorizationCode))
            {
                HorizonApp.Log.Error("Google authorization code is required");
                return false;
            }

            return await SignUp(SignUpRequest.CreateGoogle(googleAuthorizationCode, redirectUri, username));
        }

        /// <summary>
        /// Internal signup method.
        /// </summary>
        private async Task<bool> SignUp(SignUpRequest request)
        {
            HorizonApp.Events.Publish(EventKeys.UserSignUpRequested, request);

            var response = await HorizonApp.Network.PostAsync<AuthResponse>("/api/v1/app/user-management/signup", request);

            if (response.IsSuccess && response.Data != null && !string.IsNullOrEmpty(response.Data.userId))
            {
                UpdateCurrentUser(response.Data);
                CacheSession();

                HorizonApp.Log.Info($"User signed up successfully: {response.Data.userId}");
                HorizonApp.Events.Publish(EventKeys.UserSignUpSuccess, _currentUser);

                return true;
            }
            else
            {
                string errorMessage = response.Error ?? response.Data?.message;

                // Provide more specific error messages based on status code
                if (response.StatusCode == 409)
                {
                    errorMessage = "User already exists. Try signing in instead or use a different email/account.";
                }

                HorizonApp.Log.Error($"Signup failed: {errorMessage}");
                HorizonApp.Events.Publish(EventKeys.UserSignUpFailed, errorMessage);

                return false;
            }
        }

        // ===== SIGN IN =====

        /// <summary>
        /// Sign in with email and password.
        /// </summary>
        /// <param name="email">User email</param>
        /// <param name="password">User password</param>
        /// <returns>True if signin succeeded, false otherwise</returns>
        public async Task<bool> SignInEmail(string email, string password)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                HorizonApp.Log.Error("Email and password are required");
                return false;
            }

            var request = new SignInRequest
            {
                email = email,
                password = password,
                type = AuthType.EMAIL.ToString()
            };

            return await SignIn(request);
        }

        /// <summary>
        /// Sign in with Google authentication.
        /// </summary>
        /// <param name="googleAuthorizationCode">Google authorization code from Google Sign-In SDK</param>
        /// <param name="redirectUri">OAuth redirect URI. Leave empty for mobile apps (Android/iOS).</param>
        /// <returns>True if signin succeeded, false otherwise</returns>
        public async Task<bool> SignInGoogle(string googleAuthorizationCode, string redirectUri = "")
        {
            if (string.IsNullOrEmpty(googleAuthorizationCode))
            {
                HorizonApp.Log.Error("Google authorization code is required");
                return false;
            }

            var request = new SignInRequest
            {
                googleAuthorizationCode = googleAuthorizationCode,
                googleRedirectUri = redirectUri,
                type = AuthType.GOOGLE.ToString()
            };

            return await SignIn(request);
        }

        /// <summary>
        /// Sign in with anonymous token.
        /// </summary>
        /// <param name="anonymousToken">The anonymous token from previous session</param>
        /// <returns>True if signin succeeded, false otherwise</returns>
        public async Task<bool> SignInAnonymous(string anonymousToken)
        {
            if (string.IsNullOrEmpty(anonymousToken))
            {
                HorizonApp.Log.Error("Anonymous token is required for sign in");
                return false;
            }

            var request = new SignInRequest
            {
                anonymousToken = anonymousToken,
                type = AuthType.ANONYMOUS.ToString()
            };

            return await SignIn(request);
        }

        /// <summary>
        /// Try to restore anonymous session from cached token.
        /// </summary>
        /// <returns>True if session was restored, false otherwise</returns>
        public async Task<bool> RestoreAnonymousSession()
        {
            string cachedToken = GetCachedAnonymousToken();

            if (string.IsNullOrEmpty(cachedToken))
            {
                HorizonApp.Log.Warning("No cached anonymous token found");
                return false;
            }

            HorizonApp.Log.Info("Attempting to restore anonymous session...");
            return await SignInAnonymous(cachedToken);
        }

        /// <summary>
        /// Internal signin method.
        /// </summary>
        private async Task<bool> SignIn(SignInRequest request)
        {
            HorizonApp.Events.Publish(EventKeys.UserSignInRequested, request);

            var response = await HorizonApp.Network.PostAsync<AuthResponse>("/api/v1/app/user-management/signin", request);

            if (response.IsSuccess && response.Data != null && response.Data.authStatus == "AUTHENTICATED")
            {
                UpdateCurrentUser(response.Data);
                CacheSession();

                HorizonApp.Log.Info($"User signed in successfully: {response.Data.userId}");
                HorizonApp.Events.Publish(EventKeys.UserSignInSuccess, _currentUser);

                return true;
            }
            else
            {
                HorizonApp.Log.Error($"Signin failed: {response.Error ?? response.Data?.message}");
                HorizonApp.Events.Publish(EventKeys.UserSignInFailed, response.Error ?? response.Data?.message);

                return false;
            }
        }

        // ===== CHECK AUTH =====

        /// <summary>
        /// Check if the current session token is still valid.
        /// </summary>
        /// <returns>True if session is valid, false otherwise</returns>
        public async Task<bool> CheckAuth()
        {
            if (!IsSignedIn)
            {
                HorizonApp.Log.Warning("No user signed in");
                return false;
            }

            var request = new CheckAuthRequest
            {
                userId = _currentUser.UserId,
                sessionToken = _currentUser.AccessToken
            };

            var response = await HorizonApp.Network.PostAsync<CheckAuthResponse>("/api/v1/app/user-management/check-auth", request);

            if (response.IsSuccess && response.Data != null && response.Data.isAuthenticated)
            {
                HorizonApp.Log.Info("Session token is valid");
                HorizonApp.Events.Publish(EventKeys.UserAuthCheckSuccess, _currentUser);
                return true;
            }
            else
            {
                HorizonApp.Log.Warning("Session token is invalid or expired");
                HorizonApp.Events.Publish(EventKeys.UserAuthCheckFailed, response.Error);
                SignOut(); // Clear invalid session
                return false;
            }
        }

        // ===== EMAIL VERIFICATION =====

        /// <summary>
        /// Verify email with verification token.
        /// </summary>
        /// <param name="token">Verification token from email</param>
        /// <returns>True if verification succeeded, false otherwise</returns>
        public async Task<bool> VerifyEmail(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                HorizonApp.Log.Error("Verification token is required");
                return false;
            }

            var request = new VerifyEmailRequest { token = token };

            var response = await HorizonApp.Network.PostAsync<MessageResponse>("/api/v1/app/user-management/verify-email", request);

            if (response.IsSuccess && response.Data.success)
            {
                if (_currentUser != null)
                {
                    _currentUser.IsEmailVerified = true;
                    CacheSession();
                }

                HorizonApp.Log.Info("Email verified successfully");
                HorizonApp.Events.Publish(EventKeys.UserEmailVerified, token);

                return true;
            }
            else
            {
                HorizonApp.Log.Error($"Email verification failed: {response.Error ?? response.Data?.message}");
                return false;
            }
        }

        // ===== PASSWORD RESET =====

        /// <summary>
        /// Request a password reset email.
        /// </summary>
        /// <param name="email">User email</param>
        /// <returns>True if request succeeded, false otherwise</returns>
        public async Task<bool> ForgotPassword(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                HorizonApp.Log.Error("Email is required");
                return false;
            }

            var request = new ForgotPasswordRequest { email = email };

            var response = await HorizonApp.Network.PostAsync<MessageResponse>("/api/v1/app/user-management/forgot-password", request);

            if (response.IsSuccess && response.Data.success)
            {
                HorizonApp.Log.Info("Password reset email sent");
                HorizonApp.Events.Publish(EventKeys.UserPasswordResetRequested, email);

                return true;
            }
            else
            {
                HorizonApp.Log.Error($"Password reset request failed: {response.Error ?? response.Data?.message}");
                return false;
            }
        }

        /// <summary>
        /// Reset password with reset token.
        /// </summary>
        /// <param name="token">Reset token from email</param>
        /// <param name="newPassword">New password</param>
        /// <returns>True if reset succeeded, false otherwise</returns>
        public async Task<bool> ResetPassword(string token, string newPassword)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(newPassword))
            {
                HorizonApp.Log.Error("Token and new password are required");
                return false;
            }

            var request = new ResetPasswordRequest
            {
                token = token,
                newPassword = newPassword
            };

            var response = await HorizonApp.Network.PostAsync<MessageResponse>("/api/v1/app/user-management/reset-password", request);

            if (response.IsSuccess && response.Data.success)
            {
                HorizonApp.Log.Info("Password reset successful");
                HorizonApp.Events.Publish(EventKeys.UserPasswordResetSuccess, token);

                return true;
            }
            else
            {
                HorizonApp.Log.Error($"Password reset failed: {response.Error ?? response.Data?.message}");
                return false;
            }
        }

        // ===== CHANGE NAME =====

        /// <summary>
        /// Change the display name of the current user.
        /// </summary>
        /// <param name="newName">The new display name</param>
        /// <returns>True if name change succeeded, false otherwise</returns>
        public async Task<bool> ChangeName(string newName)
        {
            if (!IsSignedIn)
            {
                HorizonApp.Log.Error("User must be signed in to change name");
                return false;
            }

            if (string.IsNullOrEmpty(newName))
            {
                HorizonApp.Log.Error("New name is required");
                return false;
            }

            var request = new ChangeNameRequest
            {
                userId = _currentUser.UserId,
                sessionToken = _currentUser.AccessToken,
                newName = newName
            };

            var response = await HorizonApp.Network.PostAsync<CheckAuthResponse>("/api/v1/app/user-management/change-name", request);

            if (response.IsSuccess && response.Data != null && response.Data.isAuthenticated)
            {
                _currentUser.DisplayName = newName;
                CacheSession();

                HorizonApp.Log.Info($"Display name changed to: {newName}");
                HorizonApp.Events.Publish(EventKeys.UserDataChanged, _currentUser);

                return true;
            }
            else
            {
                HorizonApp.Log.Error($"Name change failed: {response.Error ?? response.Data?.authStatus ?? response.Data?.message}");
                return false;
            }
        }

        // ===== SIGN OUT =====

        /// <summary>
        /// Sign out the current user.
        /// </summary>
        /// <param name="keepAnonymousToken">If true and user is anonymous, preserves the anonymous token for future sign-in</param>
        public void SignOut(bool keepAnonymousToken = true)
        {
            // Save anonymous token before clearing if needed
            string anonymousTokenToKeep = null;
            if (keepAnonymousToken && _currentUser != null && _currentUser.IsAnonymous && !string.IsNullOrEmpty(_currentUser.AnonymousToken))
            {
                anonymousTokenToKeep = _currentUser.AnonymousToken;
                HorizonApp.Log.Info("Preserving anonymous token for future sign-in");
            }

            _currentUser?.Clear();
            ClearCachedSession();

            if (NetworkService.Instance != null)
            {
                NetworkService.Instance.ClearSessionToken();
            }

            // Restore anonymous token to cache if we're keeping it
            if (!string.IsNullOrEmpty(anonymousTokenToKeep))
            {
                SaveAnonymousToken(anonymousTokenToKeep);
            }

            HorizonApp.Log.Info("User signed out");
            HorizonApp.Events.Publish(EventKeys.UserSignOutSuccess, DateTime.UtcNow);
        }

        // ===== HELPER METHODS =====

        /// <summary>
        /// Update current user data from auth response.
        /// </summary>
        private void UpdateCurrentUser(AuthResponse response)
        {
            // Ensure _currentUser is initialized
            if (_currentUser == null)
            {
                _currentUser = new UserData();
            }

            _currentUser.UserId = response.userId;
            _currentUser.Email = response.email ?? string.Empty;
            _currentUser.DisplayName = response.username ?? string.Empty;
            _currentUser.AuthType = response.isAnonymous ? "ANONYMOUS" : (!string.IsNullOrEmpty(response.googleId) ? "GOOGLE" : "EMAIL");
            _currentUser.AccessToken = response.accessToken ?? string.Empty;
            _currentUser.AnonymousToken = response.anonymousToken ?? string.Empty;
            _currentUser.IsEmailVerified = response.isVerified;
            _currentUser.IsAnonymous = response.isAnonymous;
            _currentUser.LastLoginTime = DateTime.UtcNow;

            // Set session token in network service
            if (NetworkService.Instance != null && !string.IsNullOrEmpty(response.accessToken))
            {
                NetworkService.Instance.SetSessionToken(response.accessToken);
            }

            // Save anonymous token separately for future sign-in
            if (response.isAnonymous && !string.IsNullOrEmpty(response.anonymousToken))
            {
                SaveAnonymousToken(response.anonymousToken);
            }

            HorizonApp.Events.Publish(EventKeys.UserDataChanged, _currentUser);
        }

        /// <summary>
        /// Cache the current session to PlayerPrefs.
        /// </summary>
        private void CacheSession()
        {
            if (_currentUser != null && _currentUser.IsValid())
            {
                string json = JsonUtility.ToJson(_currentUser);
                PlayerPrefs.SetString("horizOn_UserSession", json);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Load cached session from PlayerPrefs.
        /// </summary>
        private void LoadCachedSession()
        {
            if (PlayerPrefs.HasKey("horizOn_UserSession"))
            {
                try
                {
                    string json = PlayerPrefs.GetString("horizOn_UserSession");
                    var cachedUser = JsonUtility.FromJson<UserData>(json);

                    if (cachedUser != null && cachedUser.IsValid())
                    {
                        _currentUser = cachedUser;

                        if (NetworkService.Instance != null)
                        {
                            NetworkService.Instance.SetSessionToken(_currentUser.AccessToken);
                        }

                        HorizonApp.Log.Info("Cached session loaded");

                        // Verify session is still valid
                        _ = CheckAuth(); // Fire and forget
                    }
                    else
                    {
                        HorizonApp.Log.Warning("Cached session is invalid or empty");
                    }
                }
                catch (Exception e)
                {
                    HorizonApp.Log.Error($"Failed to load cached session: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Clear cached session from PlayerPrefs.
        /// </summary>
        private void ClearCachedSession()
        {
            PlayerPrefs.DeleteKey("horizOn_UserSession");
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Save anonymous token to PlayerPrefs for future sign-in.
        /// </summary>
        /// <param name="anonymousToken">The anonymous token to save</param>
        private void SaveAnonymousToken(string anonymousToken)
        {
            if (!string.IsNullOrEmpty(anonymousToken))
            {
                PlayerPrefs.SetString("horizOn_AnonymousToken", anonymousToken);
                PlayerPrefs.Save();
                HorizonApp.Log.Info("Anonymous token saved to cache");
            }
        }

        /// <summary>
        /// Get cached anonymous token from PlayerPrefs.
        /// </summary>
        /// <returns>The cached anonymous token, or null if not found or invalid</returns>
        private string GetCachedAnonymousToken()
        {
            if (PlayerPrefs.HasKey("horizOn_AnonymousToken"))
            {
                string token = PlayerPrefs.GetString("horizOn_AnonymousToken");

                // Validate token length (API requires max 32 chars)
                if (!string.IsNullOrEmpty(token) && token.Length <= 32)
                {
                    return token;
                }

                // Invalid token format (likely old 36-char GUID with dashes), clear it
                HorizonApp.Log.Warning("Cached anonymous token has invalid format (too long). Clearing cache.");
                ClearAnonymousToken();
            }
            return null;
        }

        /// <summary>
        /// Clear cached anonymous token from PlayerPrefs.
        /// </summary>
        public void ClearAnonymousToken()
        {
            PlayerPrefs.DeleteKey("horizOn_AnonymousToken");
            PlayerPrefs.Save();
            HorizonApp.Log.Info("Anonymous token cleared from cache");
        }

        /// <summary>
        /// Check if there is a cached anonymous token available.
        /// </summary>
        /// <returns>True if an anonymous token is cached, false otherwise</returns>
        public bool HasCachedAnonymousToken()
        {
            return PlayerPrefs.HasKey("horizOn_AnonymousToken") &&
                   !string.IsNullOrEmpty(PlayerPrefs.GetString("horizOn_AnonymousToken"));
        }
    }
}
