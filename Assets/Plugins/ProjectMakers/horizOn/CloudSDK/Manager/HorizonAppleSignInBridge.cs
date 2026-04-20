using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using PM.horizOn.Cloud.Core;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Hidden MonoBehaviour that bridges between C# and the native Apple Sign-In flow.
    /// On iOS it forwards to the AuthenticationServices Objective-C++ plugin under
    /// Plugins/iOS/HorizonAppleSignIn.mm. On other platforms it falls back to a
    /// system-browser OAuth redirect using the customer-configured Services ID.
    ///
    /// The MonoBehaviour self-registers (DontDestroyOnLoad) on first use - no manual
    /// wiring required by the consumer.
    /// </summary>
    public class HorizonAppleSignInBridge : MonoBehaviour
    {
        private const string GameObjectName = "HorizonAppleSignInBridge";

        // Apple's standard authorization endpoint. The customer's Services ID is provided as
        // client_id; the redirect_uri must be configured both on Apple's side and inside the
        // game (deep link / localhost listener).
        private const string AppleAuthorizeEndpoint = "https://appleid.apple.com/auth/authorize";

        private static HorizonAppleSignInBridge _instance;
        private static TaskCompletionSource<AppleAuthResult> _pending;
        private static string _pendingNonce;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void _HorizonAppleSignIn_Present(string nonce);
#endif

        /// <summary>
        /// Result of a single Apple Sign-In attempt. Mirrors what the iOS native bridge sends
        /// back via UnitySendMessage and what the web-fallback OAuth redirect parses out.
        /// </summary>
        public class AppleAuthResult
        {
            public bool Success;
            public string IdentityToken;
            public string FirstName;
            public string LastName;
            public string ErrorCode;
            public string Nonce;
        }

        // JSON DTO used by the ObjC bridge. Field names must stay lowercase to match the
        // payload built in HorizonAppleSignIn.mm.
        [Serializable]
        private class NativePayload
        {
            public string identityToken;
            public string firstName;
            public string lastName;
            public string error;
        }

        /// <summary>
        /// Request a fresh Apple Sign-In flow. Returns the identity token (and optional
        /// first-login profile fields) or an error code. Single-flight: if a previous
        /// request is still in flight, the new caller receives the same Task.
        /// </summary>
        public static Task<AppleAuthResult> RequestSignIn()
        {
            EnsureInstance();

            if (_pending != null && !_pending.Task.IsCompleted)
            {
                return _pending.Task;
            }

            _pending = new TaskCompletionSource<AppleAuthResult>();
            _pendingNonce = GenerateNonce();

#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                // Pass the SHA-256 hash of the nonce to Apple. The raw nonce is sent
                // alongside the identity token to the backend for verification.
                string hashedNonce = Sha256Hex(_pendingNonce);
                _HorizonAppleSignIn_Present(hashedNonce);
            }
            catch (Exception ex)
            {
                CompletePending(new AppleAuthResult { Success = false, ErrorCode = "INVALID_APPLE_TOKEN" });
                HorizonApp.Log.Error($"Failed to invoke native Apple Sign-In: {ex.Message}");
            }
#elif UNITY_ANDROID || UNITY_STANDALONE || UNITY_WEBGL || UNITY_EDITOR
            _instance.StartWebFallback(_pendingNonce);
#else
            HorizonApp.Log.Error("Apple Sign-In is not supported on this platform");
            CompletePending(new AppleAuthResult { Success = false, ErrorCode = "APPLE_NOT_CONFIGURED" });
#endif

            return _pending.Task;
        }

        private static void EnsureInstance()
        {
            if (_instance != null)
            {
                return;
            }

            var existing = GameObject.Find(GameObjectName);
            if (existing != null)
            {
                _instance = existing.GetComponent<HorizonAppleSignInBridge>();
                if (_instance != null)
                {
                    return;
                }
            }

            var go = new GameObject(GameObjectName);
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<HorizonAppleSignInBridge>();
        }

        // Called via UnitySendMessage from HorizonAppleSignIn.mm with a JSON payload
        // {identityToken, firstName, lastName, error}.
        public void OnAppleSignInResult(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                CompletePending(new AppleAuthResult { Success = false, ErrorCode = "INVALID_APPLE_TOKEN" });
                return;
            }

            try
            {
                var payload = JsonUtility.FromJson<NativePayload>(json);
                if (payload == null)
                {
                    CompletePending(new AppleAuthResult { Success = false, ErrorCode = "INVALID_APPLE_TOKEN" });
                    return;
                }

                if (!string.IsNullOrEmpty(payload.error))
                {
                    CompletePending(new AppleAuthResult
                    {
                        Success = false,
                        ErrorCode = MapNativeError(payload.error)
                    });
                    return;
                }

                if (string.IsNullOrEmpty(payload.identityToken))
                {
                    CompletePending(new AppleAuthResult { Success = false, ErrorCode = "INVALID_APPLE_TOKEN" });
                    return;
                }

                CompletePending(new AppleAuthResult
                {
                    Success = true,
                    IdentityToken = payload.identityToken,
                    FirstName = payload.firstName,
                    LastName = payload.lastName,
                    Nonce = _pendingNonce
                });
            }
            catch (Exception ex)
            {
                HorizonApp.Log.Error($"Failed to parse Apple Sign-In native payload: {ex.Message}");
                CompletePending(new AppleAuthResult { Success = false, ErrorCode = "INVALID_APPLE_TOKEN" });
            }
        }

        private static void CompletePending(AppleAuthResult result)
        {
            if (_pending == null || _pending.Task.IsCompleted)
            {
                return;
            }

            _pending.TrySetResult(result);
        }

        private static string MapNativeError(string nativeError)
        {
            // ASAuthorizationError codes are forwarded as strings; collapse anything that isn't
            // a documented horizOn error to the generic INVALID_APPLE_TOKEN bucket - the only
            // exception is user cancellation, which we still surface as INVALID_APPLE_TOKEN
            // (no dedicated horizOn code for "user cancelled").
            switch (nativeError)
            {
                case "APPLE_NOT_CONFIGURED":
                case "INVALID_APPLE_TOKEN":
                case "NETWORK_ERROR":
                    return nativeError;
                default:
                    return "INVALID_APPLE_TOKEN";
            }
        }

        // ===== Web fallback (Android / Standalone / macOS / WebGL / Editor) =====

        private void StartWebFallback(string nonce)
        {
            string servicesId = HorizonConfig.Load()?.AppleServicesId;
            if (string.IsNullOrEmpty(servicesId))
            {
                HorizonApp.Log.Error("Apple Sign-In web fallback requires AppleServicesId in HorizonConfig");
                CompletePending(new AppleAuthResult { Success = false, ErrorCode = "APPLE_NOT_CONFIGURED" });
                return;
            }

            string redirectUri = HorizonConfig.Load()?.AppleRedirectUri;
            if (string.IsNullOrEmpty(redirectUri))
            {
                HorizonApp.Log.Error("Apple Sign-In web fallback requires AppleRedirectUri in HorizonConfig");
                CompletePending(new AppleAuthResult { Success = false, ErrorCode = "APPLE_NOT_CONFIGURED" });
                return;
            }

            // Apple requires response_mode=form_post when scope is non-empty. The customer's
            // redirect handler (Android deep-link, Standalone localhost listener, WebGL JS
            // bridge) is responsible for capturing the id_token and forwarding it back into
            // the SDK by calling DeliverWebToken below.
            var query = new StringBuilder();
            query.Append("response_type=code%20id_token");
            query.Append("&response_mode=form_post");
            query.Append("&client_id=").Append(WebUtility.UrlEncode(servicesId));
            query.Append("&redirect_uri=").Append(WebUtility.UrlEncode(redirectUri));
            query.Append("&scope=name%20email");
            query.Append("&nonce=").Append(WebUtility.UrlEncode(Sha256Hex(nonce)));
            query.Append("&state=").Append(WebUtility.UrlEncode(nonce));

            string url = AppleAuthorizeEndpoint + "?" + query.ToString();

            HorizonApp.Log.Info($"Opening Apple authorize URL in system browser: {url}");
            Application.OpenURL(url);

            // The customer-side redirect handler must call HorizonAppleSignInBridge.DeliverWebToken
            // with the harvested id_token. We don't poll here - the Task stays pending until
            // either DeliverWebToken or DeliverWebError is invoked.
        }

        /// <summary>
        /// Customer-side hook for the web fallback flow. The customer's deep-link / localhost
        /// listener should call this with the id_token harvested from Apple's redirect.
        /// </summary>
        public static void DeliverWebToken(string identityToken, string firstName = null, string lastName = null)
        {
            CompletePending(new AppleAuthResult
            {
                Success = !string.IsNullOrEmpty(identityToken),
                IdentityToken = identityToken,
                FirstName = firstName,
                LastName = lastName,
                ErrorCode = string.IsNullOrEmpty(identityToken) ? "INVALID_APPLE_TOKEN" : null,
                Nonce = _pendingNonce
            });
        }

        /// <summary>
        /// Customer-side hook for the web fallback flow. Call this with one of the documented
        /// error codes if the redirect handler determines that the OAuth flow failed.
        /// </summary>
        public static void DeliverWebError(string errorCode)
        {
            CompletePending(new AppleAuthResult { Success = false, ErrorCode = errorCode ?? "INVALID_APPLE_TOKEN" });
        }

        // ===== Helpers =====

        private static string GenerateNonce()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        private static string Sha256Hex(string input)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
