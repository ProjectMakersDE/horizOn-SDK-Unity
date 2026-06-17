using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
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
    /// Manager for crash reporting and error tracking.
    /// Provides automatic crash capture, breadcrumb trails, and manual exception recording.
    /// Uses a token bucket rate limiter to prevent report flooding.
    /// </summary>
    public class CrashManager : BaseManager<CrashManager>
    {
        // ===== CONSTANTS =====

        private const int MaxBreadcrumbs = 50;
        private const int MaxCustomKeys = 10;
        private const int TokensPerMinute = 5;
        private const int MaxTokensPerSession = 20;
        private const string SdkVersion = "1.0.0";

        // ===== STATE =====

        private bool _isCapturing;
        private string _sessionId;
        private string _userIdOverride;

        // Breadcrumb ring buffer
        private readonly BreadcrumbData[] _breadcrumbs = new BreadcrumbData[MaxBreadcrumbs];
        private int _breadcrumbHead;
        private int _breadcrumbCount;

        // Custom keys
        private readonly Dictionary<string, string> _customKeys = new Dictionary<string, string>();

        // Rate limiter (token bucket)
        private float _tokens;
        private float _lastRefillTime;
        private int _sessionReportCount;

        // Cached device info
        private string _cachedPlatform;
        private string _cachedOs;
        private string _cachedDeviceModel;
        private int _cachedDeviceMemoryMb;

        // ===== INITIALIZATION =====

        protected override void OnInit()
        {
            base.OnInit();
            CacheDeviceInfo();
            _tokens = TokensPerMinute;
            _lastRefillTime = Time.realtimeSinceStartup;
        }

        // ===== PUBLIC API =====

        /// <summary>
        /// Start automatic crash capture.
        /// Hooks into Unity's log callback and AppDomain unhandled exceptions.
        /// Registers a crash session with the backend.
        /// </summary>
        public void StartCapture()
        {
            if (_isCapturing)
            {
                HorizonApp.Log.Warning("CrashManager: Capture already started");
                return;
            }

            _sessionId = Guid.NewGuid().ToString("N");
            _sessionReportCount = 0;
            _isCapturing = true;

            Application.logMessageReceived += OnUnityLogMessage;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            HorizonApp.Log.Info($"CrashManager: Capture started (session: {_sessionId})");

            // Register session ping in background
            _ = RegisterSessionPing();
        }

        /// <summary>
        /// Stop automatic crash capture and unhook callbacks.
        /// </summary>
        public void StopCapture()
        {
            if (!_isCapturing)
                return;

            Application.logMessageReceived -= OnUnityLogMessage;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

            _isCapturing = false;
            HorizonApp.Log.Info("CrashManager: Capture stopped");
        }

        /// <summary>
        /// Manually record a non-fatal exception.
        /// </summary>
        /// <param name="exception">The exception to report</param>
        /// <param name="extraKeys">Optional additional key-value pairs for this report</param>
        public void RecordException(Exception exception, Dictionary<string, string> extraKeys = null)
        {
            if (exception == null)
            {
                HorizonApp.Log.Warning("CrashManager: Cannot record null exception");
                return;
            }

            _ = SubmitReport(
                CrashType.NON_FATAL,
                exception.Message,
                exception.StackTrace ?? "",
                extraKeys
            );
        }

        /// <summary>
        /// Record a breadcrumb for crash context.
        /// Breadcrumbs are included in subsequent crash reports.
        /// </summary>
        /// <param name="type">Breadcrumb category (e.g., "navigation", "user", "network")</param>
        /// <param name="message">Descriptive message</param>
        public void RecordBreadcrumb(string type, string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            AddBreadcrumb(type ?? "custom", message);
            HorizonApp.Events.Publish(EventKeys.BreadcrumbRecorded, message);
        }

        /// <summary>
        /// Shorthand for recording a "log" breadcrumb.
        /// </summary>
        /// <param name="message">Log message to record as breadcrumb</param>
        public void Log(string message)
        {
            RecordBreadcrumb("log", message);
        }

        /// <summary>
        /// Set a persistent custom key-value pair included in all crash reports.
        /// Maximum of 10 custom keys allowed.
        /// </summary>
        /// <param name="key">Key name</param>
        /// <param name="value">Key value</param>
        public void SetCustomKey(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (_customKeys.Count >= MaxCustomKeys && !_customKeys.ContainsKey(key))
            {
                HorizonApp.Log.Warning($"CrashManager: Maximum custom keys ({MaxCustomKeys}) reached, ignoring key '{key}'");
                return;
            }

            _customKeys[key] = value ?? "";
        }

        /// <summary>
        /// Override the user ID used in crash reports.
        /// If not set, falls back to UserManager's current user ID.
        /// </summary>
        /// <param name="userId">The user ID to use</param>
        public void SetUserId(string userId)
        {
            _userIdOverride = userId;
        }

        // ===== CALLBACKS =====

        /// <summary>
        /// Handler for Application.logMessageReceived.
        /// Captures Unity errors and exceptions as crash reports.
        /// </summary>
        private void OnUnityLogMessage(string condition, string stackTrace, UnityEngine.LogType type)
        {
            if (!_isCapturing)
                return;

            switch (type)
            {
                case UnityEngine.LogType.Exception:
                    _ = SubmitReport(CrashType.CRASH, condition, stackTrace, null);
                    break;
                case UnityEngine.LogType.Error:
                    _ = SubmitReport(CrashType.NON_FATAL, condition, stackTrace, null);
                    break;
            }
        }

        /// <summary>
        /// Handler for AppDomain.CurrentDomain.UnhandledException.
        /// Captures unhandled exceptions as crash reports.
        /// </summary>
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            if (!_isCapturing)
                return;

            var exception = args.ExceptionObject as Exception;
            if (exception != null)
            {
                _ = SubmitReport(
                    CrashType.CRASH,
                    exception.Message,
                    exception.StackTrace ?? "",
                    null
                );
            }
        }

        // ===== SUBMISSION =====

        /// <summary>
        /// Build and submit a crash report to the backend.
        /// Checks rate limits before sending.
        /// </summary>
        private async Task SubmitReport(CrashType type, string message, string stackTrace, Dictionary<string, string> extraKeys)
        {
            // Rate limiting
            RefillTokens();

            if (_tokens < 1f)
            {
                HorizonApp.Log.Warning("CrashManager: Rate limit exceeded, dropping crash report");
                return;
            }

            if (_sessionReportCount >= MaxTokensPerSession)
            {
                HorizonApp.Log.Warning("CrashManager: Session report limit exceeded, dropping crash report");
                return;
            }

            _tokens -= 1f;
            _sessionReportCount++;

            // Resolve user ID
            string userId = _userIdOverride;
            if (string.IsNullOrEmpty(userId))
            {
                userId = UserManager.Instance?.CurrentUser?.UserId ?? "";
            }

            // Build request
            var request = new CreateCrashReportRequest
            {
                type = type.ToString(),
                message = message ?? "",
                stackTrace = stackTrace ?? "",
                fingerprint = GenerateFingerprint(stackTrace),
                appVersion = Application.version,
                sdkVersion = SdkVersion,
                platform = _cachedPlatform,
                os = _cachedOs,
                deviceModel = _cachedDeviceModel,
                deviceMemoryMb = _cachedDeviceMemoryMb,
                sessionId = _sessionId ?? "",
                userId = userId
            };

            // Copy breadcrumbs
            request.breadcrumbs = GetBreadcrumbEntries();

            // Merge custom keys
            foreach (var kvp in _customKeys)
            {
                request.customKeys[kvp.Key] = kvp.Value;
            }

            if (extraKeys != null)
            {
                foreach (var kvp in extraKeys)
                {
                    request.customKeys[kvp.Key] = kvp.Value;
                }
            }

            // Build JSON manually (JsonUtility cannot handle Dictionary or List<T> properly)
            string json = BuildRequestJson(request);

            try
            {
                var response = await PostRawJsonAsync<CreateCrashReportResponse>(
                    "/api/v1/app/crash-reports/create",
                    json
                );

                if (response.IsSuccess && response.Data != null && !string.IsNullOrEmpty(response.Data.id))
                {
                    HorizonApp.Log.Info($"CrashManager: Report submitted (id: {response.Data.id}, group: {response.Data.groupId})");
                    HorizonApp.Events.Publish(EventKeys.CrashReported, response.Data);
                }
                else
                {
                    string errorMsg = response.Error ?? "Unknown error";

                    if (response.StatusCode == 403)
                    {
                        errorMsg = "Crash reporting is not available for FREE accounts";
                    }
                    else if (response.StatusCode == 429)
                    {
                        errorMsg = "Server rate limit exceeded. Please try again later.";
                    }

                    HorizonApp.Log.Warning($"CrashManager: Report submission failed: {errorMsg}");
                    HorizonApp.Events.Publish(EventKeys.CrashReportFailed, errorMsg);
                }
            }
            catch (Exception e)
            {
                HorizonApp.Log.Error($"CrashManager: Report submission error: {e.Message}");
                HorizonApp.Events.Publish(EventKeys.CrashReportFailed, e.Message);
            }
        }

        // ===== JSON CONSTRUCTION =====

        /// <summary>
        /// Manually build JSON for the crash report request.
        /// Unity's JsonUtility cannot serialize Dictionary or generic Lists reliably.
        /// </summary>
        private string BuildRequestJson(CreateCrashReportRequest request)
        {
            var sb = new StringBuilder();
            sb.Append("{");

            sb.Append("\"type\":\"").Append(EscapeJson(request.type)).Append("\"");
            sb.Append(",\"message\":\"").Append(EscapeJson(request.message)).Append("\"");
            sb.Append(",\"stackTrace\":\"").Append(EscapeJson(request.stackTrace)).Append("\"");
            sb.Append(",\"fingerprint\":\"").Append(EscapeJson(request.fingerprint)).Append("\"");
            sb.Append(",\"appVersion\":\"").Append(EscapeJson(request.appVersion)).Append("\"");
            sb.Append(",\"sdkVersion\":\"").Append(EscapeJson(request.sdkVersion)).Append("\"");
            sb.Append(",\"platform\":\"").Append(EscapeJson(request.platform)).Append("\"");
            sb.Append(",\"os\":\"").Append(EscapeJson(request.os)).Append("\"");
            sb.Append(",\"deviceModel\":\"").Append(EscapeJson(request.deviceModel)).Append("\"");
            sb.Append(",\"deviceMemoryMb\":").Append(request.deviceMemoryMb);
            sb.Append(",\"sessionId\":\"").Append(EscapeJson(request.sessionId)).Append("\"");
            sb.Append(",\"userId\":\"").Append(EscapeJson(request.userId)).Append("\"");

            // Breadcrumbs array
            sb.Append(",\"breadcrumbs\":[");
            for (int i = 0; i < request.breadcrumbs.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var bc = request.breadcrumbs[i];
                sb.Append("{\"timestamp\":\"").Append(EscapeJson(bc.timestamp)).Append("\"");
                sb.Append(",\"type\":\"").Append(EscapeJson(bc.type)).Append("\"");
                sb.Append(",\"message\":\"").Append(EscapeJson(bc.message)).Append("\"}");
            }
            sb.Append("]");

            // Custom keys object
            sb.Append(",\"customKeys\":{");
            bool first = true;
            foreach (var kvp in request.customKeys)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\"").Append(EscapeJson(kvp.Key)).Append("\":\"").Append(EscapeJson(kvp.Value)).Append("\"");
            }
            sb.Append("}");

            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Escape special characters for JSON string values.
        /// </summary>
        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str))
                return "";

            var sb = new StringBuilder(str.Length);
            foreach (char c in str)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        // ===== NETWORK =====

        /// <summary>
        /// Send a POST request with pre-built JSON body.
        /// Used instead of PostAsync because JsonUtility cannot handle Dictionary/List in the request.
        /// </summary>
        private async Task<NetworkResponse<TResponse>> PostRawJsonAsync<TResponse>(string endpoint, string json) where TResponse : class
        {
            string activeHost = HorizonApp.Network.GetActiveHost();
            if (string.IsNullOrEmpty(activeHost))
            {
                return NetworkResponse<TResponse>.Failure("No active host. Call HorizonServer.Connect() first.");
            }

            string url = $"{activeHost}{endpoint}";

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                // Set API key from config
                var config = HorizonConfig.Load();
                if (config != null)
                {
                    request.SetRequestHeader("X-API-Key", config.ApiKey);
                    request.timeout = config.ConnectionTimeoutSeconds;
                }

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError)
                {
                    string errorMessage = request.downloadHandler?.text ?? request.error ?? $"HTTP {request.responseCode}";
                    return NetworkResponse<TResponse>.Failure(errorMessage, request.responseCode);
                }

                string responseText = request.downloadHandler.text;
                try
                {
                    TResponse data = JsonUtility.FromJson<TResponse>(responseText);
                    return NetworkResponse<TResponse>.Success(data, request.responseCode);
                }
                catch (Exception e)
                {
                    return NetworkResponse<TResponse>.Failure($"Deserialization failed: {e.Message}", request.responseCode);
                }
            }
        }

        // ===== FINGERPRINT =====

        /// <summary>
        /// Generate a SHA-256 fingerprint from the stack trace.
        /// Normalizes frames by stripping engine internals, line numbers, and memory addresses.
        /// Uses the top 5 game-code frames for grouping.
        /// </summary>
        private string GenerateFingerprint(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
                return "";

            var lines = stackTrace.Split('\n');
            var gameFrames = new List<string>();

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                string className = ExtractClassName(trimmed);

                // Skip engine internals
                if (!string.IsNullOrEmpty(className) &&
                    (className.StartsWith("UnityEngine.") ||
                     className.StartsWith("System.") ||
                     className.StartsWith("Mono.") ||
                     className.StartsWith("Unity.")))
                {
                    continue;
                }

                string normalized = NormalizeFrame(trimmed);
                if (!string.IsNullOrEmpty(normalized))
                {
                    gameFrames.Add(normalized);
                }

                if (gameFrames.Count >= 5)
                    break;
            }

            if (gameFrames.Count == 0)
                return "";

            string combined = string.Join("|", gameFrames);

            using (var sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
                var sb = new StringBuilder(hashBytes.Length * 2);
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Normalize a stack frame by stripping line numbers, memory addresses, and lambda identifiers.
        /// </summary>
        private string NormalizeFrame(string frame)
        {
            if (string.IsNullOrEmpty(frame))
                return "";

            // Strip " (at ...)" file references
            string normalized = Regex.Replace(frame, @"\s*\(at\s+.*?\)", "");

            // Strip " [0x...]" memory addresses
            normalized = Regex.Replace(normalized, @"\s*\[0x[0-9a-fA-F]+\]", "");

            // Strip ":line N" line numbers
            normalized = Regex.Replace(normalized, @":line\s+\d+", "");

            // Strip " <...>" lambda/generic markers
            normalized = Regex.Replace(normalized, @"\s*<[^>]*>", "");

            // Strip " in ..." file paths
            normalized = Regex.Replace(normalized, @"\s+in\s+.*$", "");

            return normalized.Trim();
        }

        /// <summary>
        /// Extract the namespace.class name from a stack frame line.
        /// </summary>
        private string ExtractClassName(string frame)
        {
            if (string.IsNullOrEmpty(frame))
                return "";

            // Match patterns like "Namespace.ClassName.MethodName (" or "Namespace.ClassName:MethodName ("
            var match = Regex.Match(frame, @"^(?:at\s+)?([A-Za-z_][\w.]*)\.[A-Za-z_]\w*\s*[\(:]");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            // Fallback: try to find dotted identifier before a method separator
            match = Regex.Match(frame, @"([A-Za-z_][\w.]+)\.[A-Za-z_]\w*");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return "";
        }

        // ===== BREADCRUMBS =====

        /// <summary>
        /// Add a breadcrumb to the ring buffer.
        /// Oldest breadcrumbs are overwritten when the buffer is full.
        /// </summary>
        private void AddBreadcrumb(string type, string message)
        {
            var breadcrumb = new BreadcrumbData
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                type = type,
                message = message
            };

            _breadcrumbs[_breadcrumbHead] = breadcrumb;
            _breadcrumbHead = (_breadcrumbHead + 1) % MaxBreadcrumbs;
            if (_breadcrumbCount < MaxBreadcrumbs)
                _breadcrumbCount++;
        }

        /// <summary>
        /// Get all breadcrumbs as a list of BreadcrumbEntry objects, in chronological order.
        /// </summary>
        private List<CreateCrashReportRequest.BreadcrumbEntry> GetBreadcrumbEntries()
        {
            var entries = new List<CreateCrashReportRequest.BreadcrumbEntry>(_breadcrumbCount);

            if (_breadcrumbCount == 0)
                return entries;

            // Calculate start index for chronological order
            int start = _breadcrumbCount < MaxBreadcrumbs ? 0 : _breadcrumbHead;

            for (int i = 0; i < _breadcrumbCount; i++)
            {
                int index = (start + i) % MaxBreadcrumbs;
                var bc = _breadcrumbs[index];
                if (bc != null)
                {
                    entries.Add(new CreateCrashReportRequest.BreadcrumbEntry
                    {
                        timestamp = bc.timestamp,
                        type = bc.type,
                        message = bc.message
                    });
                }
            }

            return entries;
        }

        // ===== RATE LIMITER =====

        /// <summary>
        /// Refill rate limiter tokens based on elapsed time.
        /// </summary>
        private void RefillTokens()
        {
            float now = Time.realtimeSinceStartup;
            float elapsed = now - _lastRefillTime;

            if (elapsed > 0f)
            {
                float refill = elapsed * (TokensPerMinute / 60f);
                _tokens = Mathf.Min(_tokens + refill, TokensPerMinute);
                _lastRefillTime = now;
            }
        }

        // ===== DEVICE INFO =====

        /// <summary>
        /// Cache device information from SystemInfo to avoid repeated lookups.
        /// </summary>
        private void CacheDeviceInfo()
        {
            _cachedPlatform = Application.platform.ToString();
            _cachedOs = SystemInfo.operatingSystem;
            _cachedDeviceModel = SystemInfo.deviceModel;
            _cachedDeviceMemoryMb = SystemInfo.systemMemorySize;
        }

        // ===== SESSION =====

        /// <summary>
        /// Register the crash session with the backend.
        /// </summary>
        private async Task RegisterSessionPing()
        {
            string userId = _userIdOverride;
            if (string.IsNullOrEmpty(userId))
            {
                userId = UserManager.Instance?.CurrentUser?.UserId ?? "";
            }

            var request = new CreateCrashSessionRequest
            {
                sessionId = _sessionId,
                appVersion = Application.version,
                platform = _cachedPlatform,
                userId = userId
            };

            var response = await HorizonApp.Network.PostAsync<CreateCrashReportResponse>(
                "/api/v1/app/crash-reports/session",
                request,
                useSessionToken: false
            );

            if (response.IsSuccess)
            {
                HorizonApp.Log.Info($"CrashManager: Session registered ({_sessionId})");
                HorizonApp.Events.Publish(EventKeys.CrashSessionRegistered, _sessionId);
            }
            else
            {
                HorizonApp.Log.Warning($"CrashManager: Session registration failed: {response.Error}");
            }
        }

        // ===== CLEANUP =====

        /// <summary>
        /// Unity lifecycle: cleanup on destroy.
        /// </summary>
        protected virtual void OnDestroy()
        {
            StopCapture();
        }
    }
}
