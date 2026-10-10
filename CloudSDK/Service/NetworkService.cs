using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Helper;
using PM.horizOn.Cloud.Objects.Network.Responses;
using PM.horizOn.Cloud.Transport;

namespace PM.horizOn.Cloud.Service
{
    /// <summary>
    /// Network service for making HTTP requests to horizOn API.
    /// Handles request creation, retry logic, rate limiting, and error handling.
    /// </summary>
    public class NetworkService : BaseService<NetworkService>, IService
    {
        private HorizonConfig _config;
        private string _activeHost;
        private string _sessionToken;

        /// <summary>
        /// Set the active host URL to use for API requests.
        /// </summary>
        public void SetActiveHost(string host)
        {
            _activeHost = host;
            LogService.Instance.Info($"Active host set to: {host}");
        }

        /// <summary>
        /// Get the active host URL.
        /// </summary>
        public string GetActiveHost()
        {
            return _activeHost;
        }

        /// <summary>
        /// Set the session token for authenticated requests.
        /// </summary>
        public void SetSessionToken(string token)
        {
            _sessionToken = token;
            LogService.Instance.Info("Session token updated");
        }

        /// <summary>
        /// Get the current session token.
        /// </summary>
        public string GetSessionToken()
        {
            return _sessionToken;
        }

        /// <summary>
        /// Clear the session token (logout).
        /// </summary>
        public void ClearSessionToken()
        {
            _sessionToken = null;
            LogService.Instance.Info("Session token cleared");
        }

        /// <summary>
        /// Initialize the network service with configuration.
        /// </summary>
        public void Initialize(HorizonConfig config)
        {
            _config = config;

            if (config == null || !config.IsValid()) 
                LogService.Instance.Error("NetworkService initialized with invalid configuration");
        }

        /// <summary>
        /// Send a GET request to the API.
        /// </summary>
        /// <typeparam name="TResponse">The response type</typeparam>
        /// <param name="endpoint">The API endpoint (e.g., "/api/v1/app/news")</param>
        /// <param name="useSessionToken">Whether to include session token in headers</param>
        /// <returns>The deserialized response</returns>
        public async Task<NetworkResponse<TResponse>> GetAsync<TResponse>(string endpoint, bool useSessionToken = false) where TResponse : class
        {
            return await SendRequestAsync<TResponse>(endpoint, "GET", null, useSessionToken, null);
        }

        /// <summary>
        /// Send a POST request to the API.
        /// </summary>
        /// <typeparam name="TResponse">The response type</typeparam>
        /// <param name="endpoint">The API endpoint</param>
        /// <param name="requestData">The request data to serialize as JSON</param>
        /// <param name="useSessionToken">Whether to include session token in headers</param>
        /// <param name="headers">Optional extra request headers, for example <c>If-Match</c></param>
        /// <returns>The deserialized response</returns>
        public async Task<NetworkResponse<TResponse>> PostAsync<TResponse>(string endpoint, object requestData = null, bool useSessionToken = false, IReadOnlyDictionary<string, string> headers = null) where TResponse : class
        {
            return await SendRequestAsync<TResponse>(endpoint, "POST", requestData, useSessionToken, headers);
        }

        /// <summary>
        /// Send a PUT request to the API.
        /// Same headers, JSON serialization, retries and error handling as <see cref="PostAsync{TResponse}"/>.
        /// </summary>
        /// <typeparam name="TResponse">The response type</typeparam>
        /// <param name="endpoint">The API endpoint</param>
        /// <param name="requestData">The request data to serialize as JSON</param>
        /// <param name="useSessionToken">Whether to include session token in headers</param>
        /// <returns>The deserialized response</returns>
        public async Task<NetworkResponse<TResponse>> PutAsync<TResponse>(string endpoint, object requestData = null, bool useSessionToken = false) where TResponse : class
        {
            return await SendRequestAsync<TResponse>(endpoint, "PUT", requestData, useSessionToken, null);
        }

        /// <summary>
        /// Send a DELETE request to the API.
        /// </summary>
        /// <typeparam name="TResponse">The response type</typeparam>
        /// <param name="endpoint">The API endpoint</param>
        /// <param name="useSessionToken">Whether to include session token in headers</param>
        /// <returns>The deserialized response</returns>
        public async Task<NetworkResponse<TResponse>> DeleteAsync<TResponse>(string endpoint, bool useSessionToken = false) where TResponse : class
        {
            return await SendRequestAsync<TResponse>(endpoint, "DELETE", null, useSessionToken, null);
        }

        /// <summary>
        /// Send a POST request with raw binary data.
        /// </summary>
        /// <typeparam name="TResponse">The response type</typeparam>
        /// <param name="endpoint">The API endpoint</param>
        /// <param name="binaryData">The raw binary data to send</param>
        /// <param name="useSessionToken">Whether to include session token in headers</param>
        /// <param name="headers">Optional extra request headers, for example <c>If-Match</c></param>
        /// <returns>The deserialized response</returns>
        public async Task<NetworkResponse<TResponse>> PostBinaryAsync<TResponse>(string endpoint, byte[] binaryData, bool useSessionToken = false, IReadOnlyDictionary<string, string> headers = null) where TResponse : class
        {
            return await SendBinaryRequestAsync<TResponse>(endpoint, "POST", binaryData, useSessionToken, headers);
        }

        /// <summary>
        /// Send a GET request expecting raw binary response.
        /// </summary>
        /// <param name="endpoint">The API endpoint</param>
        /// <param name="useSessionToken">Whether to include session token in headers</param>
        /// <returns>The raw binary response or null if not found</returns>
        public async Task<BinaryNetworkResponse> GetBinaryAsync(string endpoint, bool useSessionToken = false)
        {
            return await SendBinaryResponseRequestAsync(endpoint, "GET", null, useSessionToken, null);
        }

        /// <summary>
        /// Send a JSON POST request expecting a raw binary response.
        /// </summary>
        /// <param name="endpoint">The API endpoint</param>
        /// <param name="requestData">The JSON request body</param>
        /// <param name="useSessionToken">Whether to include session token in headers</param>
        /// <param name="headers">Optional extra request headers</param>
        /// <returns>The raw binary response or not found for HTTP 204</returns>
        public async Task<BinaryNetworkResponse> PostForBinaryAsync(string endpoint, object requestData, bool useSessionToken = false, IReadOnlyDictionary<string, string> headers = null)
        {
            return await SendBinaryResponseRequestAsync(endpoint, "POST", requestData, useSessionToken, headers);
        }

        /// <summary>
        /// Internal method to send HTTP requests with retry logic.
        /// </summary>
        private async Task<NetworkResponse<TResponse>> SendRequestAsync<TResponse>(
            string endpoint,
            string method,
            object requestData,
            bool useSessionToken,
            IReadOnlyDictionary<string, string> headers) where TResponse : class
        {
            if (string.IsNullOrEmpty(_activeHost))
            {
                return NetworkResponse<TResponse>.Failure("No active host. Call HorizonServer.Connect() first.");
            }

            if (_config == null)
            {
                return NetworkResponse<TResponse>.Failure("NetworkService not initialized. Call Initialize() first.");
            }

            string url = $"{_activeHost}{endpoint}";
            int attemptCount = 0;
            int maxAttempts = _config.MaxRetryAttempts + 1; // Initial attempt + retries

            while (attemptCount < maxAttempts)
            {
                attemptCount++;

                EventService.Instance?.Publish(EventKeys.NetworkRequestStarted, new NetworkRequestData
                {
                    Url = url,
                    Method = method,
                    Attempt = attemptCount
                });

                using (UnityWebRequest request = CreateRequest(url, method, requestData, useSessionToken))
                {
                    ApplyHeaders(request, headers);

                    // Send request
                    var operation = request.SendWebRequest();

                    // Wait for completion
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    // Check for network errors
                    if (request.result == UnityWebRequest.Result.ConnectionError ||
                        request.result == UnityWebRequest.Result.ProtocolError)
                    {
                        long responseCode = request.responseCode;

                        // Rate limiting
                        if (responseCode == 429)
                        {
                            string retryAfter = request.GetResponseHeader("Retry-After");
                            float retryDelay = float.TryParse(retryAfter, out float delay) ? delay : _config.RetryDelaySeconds;
                            string rateLimitCode = ParseErrorCode(request);

                            EventService.Instance?.Publish(EventKeys.NetworkRateLimited, new RateLimitData
                            {
                                RetryAfter = retryDelay
                            });

                            // Feature limits with a code (for example RUN_RATE_LIMITED) can last an hour:
                            // they are reported right away instead of being retried.
                            if (attemptCount < maxAttempts && !IsNonRetryableRateLimitCode(rateLimitCode))
                            {
                                LogService.Instance.Warning($"Rate limited. Retrying after {retryDelay} seconds...");
                                await Task.Delay((int)(retryDelay * 1000));
                                continue;
                            }

                            // Still rate limited after the last attempt (or not retryable): fail with a clear
                            // message and keep the 429 status and the server code.
                            string rateLimitError = BuildRateLimitMessage(retryDelay);
                            EventService.Instance?.Publish(EventKeys.NetworkRequestFailed, new NetworkErrorData
                            {
                                Url = url,
                                Method = method,
                                StatusCode = responseCode,
                                Error = rateLimitError
                            });
                            LogService.Instance.Error($"Request failed: {method} {url} - {rateLimitError}");
                            return NetworkResponse<TResponse>.Failure(rateLimitError, responseCode, rateLimitCode);
                        }

                        // Server errors (5xx) or timeout - retry
                        if (responseCode >= 500 || request.result == UnityWebRequest.Result.ConnectionError)
                        {
                            if (attemptCount < maxAttempts)
                            {
                                EventService.Instance?.Publish(EventKeys.NetworkRetryAttempt, new NetworkRetryData
                                {
                                    Attempt = attemptCount,
                                    MaxAttempts = maxAttempts,
                                    Error = request.error
                                });

                                LogService.Instance.Warning($"Request failed (attempt {attemptCount}/{maxAttempts}): {request.error}. Retrying...");
                                await Task.Delay((int)(_config.RetryDelaySeconds * 1000));
                                continue;
                            }
                        }

                        // Client errors (4xx) or final retry - return error
                        string errorMessage = ParseErrorMessage(request);

                        EventService.Instance?.Publish(EventKeys.NetworkRequestFailed, new NetworkErrorData
                        {
                            Url = url,
                            Method = method,
                            StatusCode = responseCode,
                            Error = errorMessage
                        });

                        LogService.Instance.Error($"Request failed: {method} {url} - {errorMessage}");
                        return NetworkResponse<TResponse>.Failure(errorMessage, responseCode, ParseErrorCode(request), ReadResponseHeaders(request));
                    }

                    // Success
                    string responseText = request.downloadHandler.text;

                    EventService.Instance?.Publish(EventKeys.NetworkRequestSuccess, new NetworkSuccessData
                    {
                        Url = url,
                        Method = method,
                        StatusCode = request.responseCode
                    });

                    // Deserialize response
                    try
                    {
                        // Debug log the raw response
                        LogService.Instance.Info($"Raw response from {endpoint}: {responseText}");

                        TResponse data;

                        // Special handling for MessageResponse with plain text responses
                        if (typeof(TResponse) == typeof(MessageResponse))
                        {
                            // If response is plain text (not JSON), wrap it in a MessageResponse
                            if (!responseText.TrimStart().StartsWith("{"))
                            {
                                var messageResponse = new MessageResponse
                                {
                                    success = true,
                                    message = responseText
                                };
                                data = messageResponse as TResponse;
                            }
                            else
                            {
                                data = JsonUtility.FromJson<TResponse>(responseText);
                            }
                        }
                        // Special handling for GetAllRemoteConfigResponse which has a dictionary
                        else if (typeof(TResponse) == typeof(GetAllRemoteConfigResponse))
                        {
                            data = GetAllRemoteConfigResponse.ParseFromJson(responseText) as TResponse;
                        }
                        // Special handling for LocalizationAllResponse which has a dictionary
                        else if (typeof(TResponse) == typeof(LocalizationAllResponse))
                        {
                            data = LocalizationAllResponse.ParseFromJson(responseText) as TResponse;
                        }
                        // Special handling for array responses (JsonUtility can't deserialize arrays directly)
                        else if (typeof(TResponse).IsArray)
                        {
                            // Wrap the array in an object for JsonUtility
                            string wrappedJson = "{\"items\":" + responseText + "}";
                            var elementType = typeof(TResponse).GetElementType();
                            var wrapperType = typeof(ArrayWrapper<>).MakeGenericType(elementType);
                            var wrapper = JsonUtility.FromJson(wrappedJson, wrapperType);
                            var itemsProperty = wrapperType.GetField("items");
                            data = (TResponse)itemsProperty.GetValue(wrapper);
                        }
                        else
                        {
                            data = JsonUtility.FromJson<TResponse>(responseText);
                        }

                        return NetworkResponse<TResponse>.Success(data, request.responseCode, ReadResponseHeaders(request));
                    }
                    catch (Exception e)
                    {
                        LogService.Instance.Error($"Failed to deserialize response: {e.Message}");
                        LogService.Instance.Error($"Response text was: {responseText}");
                        return NetworkResponse<TResponse>.Failure($"Deserialization failed: {e.Message}", request.responseCode);
                    }
                }
            }

            // Max retries exceeded
            return NetworkResponse<TResponse>.Failure($"Max retry attempts ({maxAttempts}) exceeded");
        }

        /// <summary>
        /// Internal method to send binary POST requests with retry logic.
        /// </summary>
        private async Task<NetworkResponse<TResponse>> SendBinaryRequestAsync<TResponse>(
            string endpoint,
            string method,
            byte[] binaryData,
            bool useSessionToken,
            IReadOnlyDictionary<string, string> headers) where TResponse : class
        {
            if (string.IsNullOrEmpty(_activeHost))
            {
                return NetworkResponse<TResponse>.Failure("No active host. Call HorizonServer.Connect() first.");
            }

            if (_config == null)
            {
                return NetworkResponse<TResponse>.Failure("NetworkService not initialized. Call Initialize() first.");
            }

            string url = $"{_activeHost}{endpoint}";
            int attemptCount = 0;
            int maxAttempts = _config.MaxRetryAttempts + 1;

            while (attemptCount < maxAttempts)
            {
                attemptCount++;

                EventService.Instance?.Publish(EventKeys.NetworkRequestStarted, new NetworkRequestData
                {
                    Url = url,
                    Method = method,
                    Attempt = attemptCount
                });

                using (UnityWebRequest request = CreateBinaryPostRequest(url, binaryData, useSessionToken))
                {
                    ApplyHeaders(request, headers);
                    var operation = request.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result == UnityWebRequest.Result.ConnectionError ||
                        request.result == UnityWebRequest.Result.ProtocolError)
                    {
                        long responseCode = request.responseCode;

                        if (responseCode == 429)
                        {
                            string retryAfter = request.GetResponseHeader("Retry-After");
                            float retryDelay = float.TryParse(retryAfter, out float delay) ? delay : _config.RetryDelaySeconds;

                            EventService.Instance?.Publish(EventKeys.NetworkRateLimited, new RateLimitData
                            {
                                RetryAfter = retryDelay
                            });

                            if (attemptCount < maxAttempts)
                            {
                                LogService.Instance.Warning($"Rate limited. Retrying after {retryDelay} seconds...");
                                await Task.Delay((int)(retryDelay * 1000));
                                continue;
                            }

                            // Still rate limited after the last attempt: fail with a clear message and keep the 429 status.
                            string rateLimitError = BuildRateLimitMessage(retryDelay);
                            EventService.Instance?.Publish(EventKeys.NetworkRequestFailed, new NetworkErrorData
                            {
                                Url = url,
                                Method = method,
                                StatusCode = responseCode,
                                Error = rateLimitError
                            });
                            LogService.Instance.Error($"Request failed: {method} {url} - {rateLimitError}");
                            return NetworkResponse<TResponse>.Failure(rateLimitError, responseCode);
                        }

                        if (responseCode >= 500 || request.result == UnityWebRequest.Result.ConnectionError)
                        {
                            if (attemptCount < maxAttempts)
                            {
                                EventService.Instance?.Publish(EventKeys.NetworkRetryAttempt, new NetworkRetryData
                                {
                                    Attempt = attemptCount,
                                    MaxAttempts = maxAttempts,
                                    Error = request.error
                                });

                                LogService.Instance.Warning($"Request failed (attempt {attemptCount}/{maxAttempts}): {request.error}. Retrying...");
                                await Task.Delay((int)(_config.RetryDelaySeconds * 1000));
                                continue;
                            }
                        }

                        string errorMessage = ParseErrorMessage(request);

                        EventService.Instance?.Publish(EventKeys.NetworkRequestFailed, new NetworkErrorData
                        {
                            Url = url,
                            Method = method,
                            StatusCode = responseCode,
                            Error = errorMessage
                        });

                        LogService.Instance.Error($"Request failed: {method} {url} - {errorMessage}");
                        return NetworkResponse<TResponse>.Failure(errorMessage, responseCode, ParseErrorCode(request), ReadResponseHeaders(request));
                    }

                    string responseText = request.downloadHandler.text;

                    EventService.Instance?.Publish(EventKeys.NetworkRequestSuccess, new NetworkSuccessData
                    {
                        Url = url,
                        Method = method,
                        StatusCode = request.responseCode
                    });

                    try
                    {
                        LogService.Instance.Info($"Raw response from {endpoint}: {responseText}");
                        TResponse data = JsonUtility.FromJson<TResponse>(responseText);
                        return NetworkResponse<TResponse>.Success(data, request.responseCode, ReadResponseHeaders(request));
                    }
                    catch (Exception e)
                    {
                        LogService.Instance.Error($"Failed to deserialize response: {e.Message}");
                        return NetworkResponse<TResponse>.Failure($"Deserialization failed: {e.Message}", request.responseCode);
                    }
                }
            }

            return NetworkResponse<TResponse>.Failure($"Max retry attempts ({maxAttempts}) exceeded");
        }

        /// <summary>
        /// Internal method to send requests expecting a binary response with retry logic.
        /// </summary>
        private async Task<BinaryNetworkResponse> SendBinaryResponseRequestAsync(
            string endpoint, string method, object requestData, bool useSessionToken,
            IReadOnlyDictionary<string, string> headers)
        {
            if (string.IsNullOrEmpty(_activeHost))
            {
                return BinaryNetworkResponse.Failure("No active host. Call HorizonServer.Connect() first.");
            }

            if (_config == null)
            {
                return BinaryNetworkResponse.Failure("NetworkService not initialized. Call Initialize() first.");
            }

            string url = $"{_activeHost}{endpoint}";
            int attemptCount = 0;
            int maxAttempts = _config.MaxRetryAttempts + 1;

            while (attemptCount < maxAttempts)
            {
                attemptCount++;

                EventService.Instance?.Publish(EventKeys.NetworkRequestStarted, new NetworkRequestData
                {
                    Url = url,
                    Method = method,
                    Attempt = attemptCount
                });

                using (UnityWebRequest request = CreateRequest(url, method, requestData, useSessionToken))
                {
                    request.SetRequestHeader("Accept", "application/octet-stream");
                    ApplyHeaders(request, headers);
                    var operation = request.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    // Handle 204 No Content (not found)
                    if (request.responseCode == 204)
                    {
                        EventService.Instance?.Publish(EventKeys.NetworkRequestSuccess, new NetworkSuccessData
                        {
                            Url = url,
                            Method = method,
                            StatusCode = 204
                        });
                        return BinaryNetworkResponse.NotFound(ReadResponseHeaders(request));
                    }

                    if (request.result == UnityWebRequest.Result.ConnectionError ||
                        request.result == UnityWebRequest.Result.ProtocolError)
                    {
                        long responseCode = request.responseCode;

                        if (responseCode == 429)
                        {
                            string retryAfter = request.GetResponseHeader("Retry-After");
                            float retryDelay = float.TryParse(retryAfter, out float delay) ? delay : _config.RetryDelaySeconds;

                            EventService.Instance?.Publish(EventKeys.NetworkRateLimited, new RateLimitData
                            {
                                RetryAfter = retryDelay
                            });

                            if (attemptCount < maxAttempts)
                            {
                                LogService.Instance.Warning($"Rate limited. Retrying after {retryDelay} seconds...");
                                await Task.Delay((int)(retryDelay * 1000));
                                continue;
                            }

                            // Still rate limited after the last attempt: fail with a clear message and keep the 429 status.
                            string rateLimitError = BuildRateLimitMessage(retryDelay);
                            EventService.Instance?.Publish(EventKeys.NetworkRequestFailed, new NetworkErrorData
                            {
                                Url = url,
                                Method = method,
                                StatusCode = responseCode,
                                Error = rateLimitError
                            });
                            LogService.Instance.Error($"Request failed: {method} {url} - {rateLimitError}");
                            return BinaryNetworkResponse.Failure(rateLimitError, responseCode);
                        }

                        if (responseCode >= 500 || request.result == UnityWebRequest.Result.ConnectionError)
                        {
                            if (attemptCount < maxAttempts)
                            {
                                EventService.Instance?.Publish(EventKeys.NetworkRetryAttempt, new NetworkRetryData
                                {
                                    Attempt = attemptCount,
                                    MaxAttempts = maxAttempts,
                                    Error = request.error
                                });

                                LogService.Instance.Warning($"Request failed (attempt {attemptCount}/{maxAttempts}): {request.error}. Retrying...");
                                await Task.Delay((int)(_config.RetryDelaySeconds * 1000));
                                continue;
                            }
                        }

                        string errorMessage = ParseErrorMessage(request);

                        EventService.Instance?.Publish(EventKeys.NetworkRequestFailed, new NetworkErrorData
                        {
                            Url = url,
                            Method = method,
                            StatusCode = responseCode,
                            Error = errorMessage
                        });

                        LogService.Instance.Error($"Request failed: {method} {url} - {errorMessage}");
                        return BinaryNetworkResponse.Failure(errorMessage, responseCode, ReadResponseHeaders(request));
                    }

                    byte[] responseData = request.downloadHandler.data;

                    EventService.Instance?.Publish(EventKeys.NetworkRequestSuccess, new NetworkSuccessData
                    {
                        Url = url,
                        Method = method,
                        StatusCode = request.responseCode
                    });

                    LogService.Instance.Info($"Binary response from {endpoint}: {responseData?.Length ?? 0} bytes");
                    return BinaryNetworkResponse.Success(responseData, request.responseCode, ReadResponseHeaders(request));
                }
            }

            return BinaryNetworkResponse.Failure($"Max retry attempts ({maxAttempts}) exceeded");
        }

        /// <summary>
        /// Create a UnityWebRequest for binary POST with octet-stream content type.
        /// </summary>
        private UnityWebRequest CreateBinaryPostRequest(string url, byte[] binaryData, bool useSessionToken)
        {
            UnityWebRequest request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(binaryData ?? new byte[0]);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/octet-stream");

            request.timeout = _config.ConnectionTimeoutSeconds;

            string apiKey = _config.ApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                LogService.Instance.Error("API Key is empty or null! Check your HorizonConfig configuration.");
            }
            else
            {
                LogService.Instance.Info($"Using API Key (length: {apiKey.Length}, starts with: {apiKey.Substring(0, Math.Min(10, apiKey.Length))}...)");
            }
            request.SetRequestHeader("X-API-Key", apiKey);

            if (useSessionToken && !string.IsNullOrEmpty(_sessionToken))
            {
                request.SetRequestHeader("Authorization", $"Bearer {_sessionToken}");
                LogService.Instance.Info("Authorization header added (session token)");
            }

            return request;
        }

        /// <summary>
        /// Create a UnityWebRequest with proper headers and body.
        /// </summary>
        private UnityWebRequest CreateRequest(string url, string method, object requestData, bool useSessionToken)
        {
            UnityWebRequest request;

            if (method == "GET")
            {
                request = UnityWebRequest.Get(url);
            }
            else if (method == "POST" || method == "PUT")
            {
                // Use ToJsonExcludeEmpty to avoid sending empty strings that fail API validation
                string jsonData = requestData != null ? JsonHelper.ToJsonExcludeEmpty(requestData) : "{}";
                LogService.Instance.Info($"Request JSON: {jsonData}");
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
                request = new UnityWebRequest(url, method);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
            }
            else if (method == "DELETE")
            {
                request = UnityWebRequest.Delete(url);
                request.downloadHandler = new DownloadHandlerBuffer();
            }
            else
            {
                throw new ArgumentException($"Unsupported HTTP method: {method}");
            }

            // Set timeout
            request.timeout = _config.ConnectionTimeoutSeconds;

            // Set API key header
            string apiKey = _config.ApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                LogService.Instance.Error("API Key is empty or null! Check your HorizonConfig configuration.");
            }
            else
            {
                LogService.Instance.Info($"Using API Key (length: {apiKey.Length}, starts with: {apiKey.Substring(0, Math.Min(10, apiKey.Length))}...)");
            }
            foreach (var header in HorizonRequestHeaders.Create(apiKey, _sessionToken, useSessionToken))
            {
                request.SetRequestHeader(header.Key, header.Value);
            }
            if (useSessionToken && !string.IsNullOrEmpty(_sessionToken))
            {
                LogService.Instance.Info("Authorization header added (session token)");
            }

            return request;
        }

        /// <summary>
        /// Set extra per-request headers (for example <c>If-Match</c>) after the default ones.
        /// </summary>
        private static void ApplyHeaders(UnityWebRequest request, IReadOnlyDictionary<string, string> headers)
        {
            if (headers == null)
            {
                return;
            }
            foreach (var header in headers)
            {
                request.SetRequestHeader(header.Key, header.Value);
            }
        }

        /// <summary>
        /// Copy the response headers into a case-insensitive dictionary. WebGL reports header names
        /// in lower case, and only headers the server exposes through CORS
        /// (<c>Access-Control-Expose-Headers</c>) are visible there.
        /// </summary>
        private static IReadOnlyDictionary<string, string> ReadResponseHeaders(UnityWebRequest request)
        {
            return ResponseHeaders.From(request.GetResponseHeaders());
        }

        /// <summary>
        /// Error message for a request that is still rate limited (HTTP 429) after the last retry.
        /// </summary>
        /// <param name="retryAfterSeconds">Seconds from the Retry-After header (0 if unknown)</param>
        /// <returns>Human-readable error message</returns>
        internal static string BuildRateLimitMessage(float retryAfterSeconds)
        {
            if (retryAfterSeconds > 0f)
            {
                return $"Rate limit exceeded (HTTP 429). Try again in {(int)Math.Ceiling(retryAfterSeconds)} seconds.";
            }
            return "Rate limit exceeded (HTTP 429). Try again later.";
        }

        /// <summary>
        /// True for a 429 server code that must not be retried automatically because the wait can
        /// be long: the validated actions run limits <c>RUN_RATE_LIMITED</c> and
        /// <c>RUN_CAPACITY_REACHED</c> (up to an hour). A 429 without a code (the account request
        /// limit) keeps the Retry-After based retries.
        /// </summary>
        /// <param name="errorCode">The <c>code</c> of the 429 body, or null</param>
        internal static bool IsNonRetryableRateLimitCode(string errorCode)
        {
            return errorCode == ValidatedActionsErrorCodes.RunRateLimited ||
                   errorCode == ValidatedActionsErrorCodes.RunCapacityReached;
        }

        /// <summary>
        /// Parse error message from request.
        /// </summary>
        private string ParseErrorMessage(UnityWebRequest request)
        {
            try
            {
                if (!string.IsNullOrEmpty(request.downloadHandler?.text))
                {
                    // Try to parse error from JSON response
                    var errorResponse = JsonUtility.FromJson<ErrorResponse>(request.downloadHandler.text);
                    if (errorResponse != null && !string.IsNullOrEmpty(errorResponse.message))
                    {
                        return errorResponse.message;
                    }
                }
            }
            catch
            {
                // Ignore JSON parse errors
            }

            // Fallback to Unity error message
            return !string.IsNullOrEmpty(request.error) ? request.error : $"HTTP {request.responseCode}";
        }

        /// <summary>
        /// Parse the stable error <c>code</c> from a JSON error body
        /// (for example <c>{"code": "COSMETIC_LOCKED", ...}</c>).
        /// </summary>
        /// <returns>The server code, or null when the body has none</returns>
        private string ParseErrorCode(UnityWebRequest request)
        {
            try
            {
                string text = request.downloadHandler?.text;
                if (!string.IsNullOrEmpty(text) && text.TrimStart().StartsWith("{"))
                {
                    var errorResponse = JsonUtility.FromJson<ErrorResponse>(text);
                    if (errorResponse != null && !string.IsNullOrEmpty(errorResponse.code))
                    {
                        return errorResponse.code;
                    }
                }
            }
            catch
            {
                // Ignore JSON parse errors
            }

            return null;
        }
    }

    /// <summary>
    /// Generic network response wrapper.
    /// </summary>
    public class NetworkResponse<T> where T : class
    {
        public bool IsSuccess { get; private set; }
        public T Data { get; private set; }
        public string Error { get; private set; }
        public long StatusCode { get; private set; }

        /// <summary>
        /// Stable error code from the JSON error body (for example <c>COSMETIC_LOCKED</c>),
        /// or null when the server sent none. Only set on failures.
        /// </summary>
        public string ErrorCode { get; private set; }

        /// <summary>
        /// Response headers, case-insensitive. Empty when the request never reached the server.
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers { get; private set; } = ResponseHeaders.Empty;

        /// <summary>
        /// Value of a response header (name is case-insensitive), or null when it is missing.
        /// </summary>
        public string GetHeader(string name) => ResponseHeaders.Get(Headers, name);

        public static NetworkResponse<T> Success(T data, long statusCode = 200, IReadOnlyDictionary<string, string> headers = null)
        {
            return new NetworkResponse<T>
            {
                IsSuccess = true,
                Data = data,
                StatusCode = statusCode,
                Headers = headers ?? ResponseHeaders.Empty
            };
        }

        public static NetworkResponse<T> Failure(string error, long statusCode = 0, string errorCode = null, IReadOnlyDictionary<string, string> headers = null)
        {
            return new NetworkResponse<T>
            {
                IsSuccess = false,
                Error = error,
                StatusCode = statusCode,
                ErrorCode = errorCode,
                Headers = headers ?? ResponseHeaders.Empty
            };
        }
    }

    /// <summary>
    /// Binary network response wrapper for raw byte data.
    /// </summary>
    public class BinaryNetworkResponse
    {
        public bool IsSuccess { get; private set; }
        public bool Found { get; private set; }
        public byte[] Data { get; private set; }
        public string Error { get; private set; }
        public long StatusCode { get; private set; }

        /// <summary>
        /// Response headers, case-insensitive. Empty when the request never reached the server.
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers { get; private set; } = ResponseHeaders.Empty;

        /// <summary>
        /// Value of a response header (name is case-insensitive), or null when it is missing.
        /// </summary>
        public string GetHeader(string name) => ResponseHeaders.Get(Headers, name);

        public static BinaryNetworkResponse Success(byte[] data, long statusCode = 200, IReadOnlyDictionary<string, string> headers = null)
        {
            return new BinaryNetworkResponse
            {
                IsSuccess = true,
                Found = true,
                Data = data,
                StatusCode = statusCode,
                Headers = headers ?? ResponseHeaders.Empty
            };
        }

        public static BinaryNetworkResponse NotFound(IReadOnlyDictionary<string, string> headers = null)
        {
            return new BinaryNetworkResponse
            {
                IsSuccess = true,
                Found = false,
                Data = null,
                StatusCode = 204,
                Headers = headers ?? ResponseHeaders.Empty
            };
        }

        public static BinaryNetworkResponse Failure(string error, long statusCode = 0, IReadOnlyDictionary<string, string> headers = null)
        {
            return new BinaryNetworkResponse
            {
                IsSuccess = false,
                Found = false,
                Error = error,
                StatusCode = statusCode,
                Headers = headers ?? ResponseHeaders.Empty
            };
        }
    }

    /// <summary>
    /// Case-insensitive response header helpers shared by both response wrappers.
    /// </summary>
    internal static class ResponseHeaders
    {
        internal static readonly IReadOnlyDictionary<string, string> Empty =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static IReadOnlyDictionary<string, string> From(Dictionary<string, string> raw)
        {
            if (raw == null || raw.Count == 0)
            {
                return Empty;
            }
            // Indexer instead of the copy constructor: names that differ only in case must not throw.
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in raw)
            {
                headers[header.Key] = header.Value;
            }
            return headers;
        }

        internal static string Get(IReadOnlyDictionary<string, string> headers, string name)
        {
            if (headers == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            return headers.TryGetValue(name, out string value) ? value : null;
        }
    }

    /// <summary>
    /// Standard error response from API.
    /// </summary>
    [Serializable]
    public class ErrorResponse
    {
        public string message;
        public string code;
    }

    /// <summary>
    /// Network request event data.
    /// </summary>
    public class NetworkRequestData
    {
        public string Url;
        public string Method;
        public int Attempt;
    }

    /// <summary>
    /// Network error event data.
    /// </summary>
    public class NetworkErrorData
    {
        public string Url;
        public string Method;
        public long StatusCode;
        public string Error;
    }

    /// <summary>
    /// Network success event data.
    /// </summary>
    public class NetworkSuccessData
    {
        public string Url;
        public string Method;
        public long StatusCode;
    }

    /// <summary>
    /// Network retry event data.
    /// </summary>
    public class NetworkRetryData
    {
        public int Attempt;
        public int MaxAttempts;
        public string Error;
    }

    /// <summary>
    /// Rate limit event data.
    /// </summary>
    public class RateLimitData
    {
        public float RetryAfter;
    }

    /// <summary>
    /// Generic wrapper for array deserialization.
    /// Unity's JsonUtility cannot deserialize arrays directly, so we wrap them in an object.
    /// </summary>
    [Serializable]
    public class ArrayWrapper<T>
    {
        public T[] items;
    }
}
