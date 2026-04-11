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
            return await SendRequestAsync<TResponse>(endpoint, "GET", null, useSessionToken);
        }

        /// <summary>
        /// Send a POST request to the API.
        /// </summary>
        /// <typeparam name="TResponse">The response type</typeparam>
        /// <param name="endpoint">The API endpoint</param>
        /// <param name="requestData">The request data to serialize as JSON</param>
        /// <param name="useSessionToken">Whether to include session token in headers</param>
        /// <returns>The deserialized response</returns>
        public async Task<NetworkResponse<TResponse>> PostAsync<TResponse>(string endpoint, object requestData = null, bool useSessionToken = false) where TResponse : class
        {
            return await SendRequestAsync<TResponse>(endpoint, "POST", requestData, useSessionToken);
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
            return await SendRequestAsync<TResponse>(endpoint, "DELETE", null, useSessionToken);
        }

        /// <summary>
        /// Send a POST request with raw binary data.
        /// </summary>
        /// <typeparam name="TResponse">The response type</typeparam>
        /// <param name="endpoint">The API endpoint</param>
        /// <param name="binaryData">The raw binary data to send</param>
        /// <param name="useSessionToken">Whether to include session token in headers</param>
        /// <returns>The deserialized response</returns>
        public async Task<NetworkResponse<TResponse>> PostBinaryAsync<TResponse>(string endpoint, byte[] binaryData, bool useSessionToken = false) where TResponse : class
        {
            return await SendBinaryRequestAsync<TResponse>(endpoint, "POST", binaryData, useSessionToken);
        }

        /// <summary>
        /// Send a GET request expecting raw binary response.
        /// </summary>
        /// <param name="endpoint">The API endpoint</param>
        /// <param name="useSessionToken">Whether to include session token in headers</param>
        /// <returns>The raw binary response or null if not found</returns>
        public async Task<BinaryNetworkResponse> GetBinaryAsync(string endpoint, bool useSessionToken = false)
        {
            return await SendBinaryGetRequestAsync(endpoint, useSessionToken);
        }

        /// <summary>
        /// Internal method to send HTTP requests with retry logic.
        /// </summary>
        private async Task<NetworkResponse<TResponse>> SendRequestAsync<TResponse>(
            string endpoint,
            string method,
            object requestData,
            bool useSessionToken) where TResponse : class
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

                            EventService.Instance?.Publish(EventKeys.NetworkRateLimited, new RateLimitData
                            {
                                RetryAfter = retryDelay
                            });

                            LogService.Instance.Warning($"Rate limited. Retrying after {retryDelay} seconds...");

                            // Wait and retry
                            await Task.Delay((int)(retryDelay * 1000));
                            continue;
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
                        return NetworkResponse<TResponse>.Failure(errorMessage, responseCode);
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

                        return NetworkResponse<TResponse>.Success(data, request.responseCode);
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
            bool useSessionToken) where TResponse : class
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

                            LogService.Instance.Warning($"Rate limited. Retrying after {retryDelay} seconds...");
                            await Task.Delay((int)(retryDelay * 1000));
                            continue;
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
                        return NetworkResponse<TResponse>.Failure(errorMessage, responseCode);
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
                        return NetworkResponse<TResponse>.Success(data, request.responseCode);
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
        /// Internal method to send binary GET requests with retry logic.
        /// </summary>
        private async Task<BinaryNetworkResponse> SendBinaryGetRequestAsync(string endpoint, bool useSessionToken)
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
                    Method = "GET",
                    Attempt = attemptCount
                });

                using (UnityWebRequest request = CreateBinaryGetRequest(url, useSessionToken))
                {
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
                            Method = "GET",
                            StatusCode = 204
                        });
                        return BinaryNetworkResponse.NotFound();
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

                            LogService.Instance.Warning($"Rate limited. Retrying after {retryDelay} seconds...");
                            await Task.Delay((int)(retryDelay * 1000));
                            continue;
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
                            Method = "GET",
                            StatusCode = responseCode,
                            Error = errorMessage
                        });

                        LogService.Instance.Error($"Request failed: GET {url} - {errorMessage}");
                        return BinaryNetworkResponse.Failure(errorMessage, responseCode);
                    }

                    byte[] responseData = request.downloadHandler.data;

                    EventService.Instance?.Publish(EventKeys.NetworkRequestSuccess, new NetworkSuccessData
                    {
                        Url = url,
                        Method = "GET",
                        StatusCode = request.responseCode
                    });

                    LogService.Instance.Info($"Binary response from {endpoint}: {responseData?.Length ?? 0} bytes");
                    return BinaryNetworkResponse.Success(responseData, request.responseCode);
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
        /// Create a UnityWebRequest for binary GET with octet-stream accept header.
        /// </summary>
        private UnityWebRequest CreateBinaryGetRequest(string url, bool useSessionToken)
        {
            UnityWebRequest request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Accept", "application/octet-stream");

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
            else if (method == "POST")
            {
                // Use ToJsonExcludeEmpty to avoid sending empty strings that fail API validation
                string jsonData = requestData != null ? JsonHelper.ToJsonExcludeEmpty(requestData) : "{}";
                LogService.Instance.Info($"Request JSON: {jsonData}");
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
                request = new UnityWebRequest(url, "POST");
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
            request.SetRequestHeader("X-API-Key", apiKey);

            // Set session token if needed
            if (useSessionToken && !string.IsNullOrEmpty(_sessionToken))
            {
                request.SetRequestHeader("Authorization", $"Bearer {_sessionToken}");
                LogService.Instance.Info("Authorization header added (session token)");
            }
            else
            {
                LogService.Instance.Info("No Authorization header (useSessionToken: " + useSessionToken + ")");
            }

            return request;
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

        public static NetworkResponse<T> Success(T data, long statusCode = 200)
        {
            return new NetworkResponse<T>
            {
                IsSuccess = true,
                Data = data,
                StatusCode = statusCode
            };
        }

        public static NetworkResponse<T> Failure(string error, long statusCode = 0)
        {
            return new NetworkResponse<T>
            {
                IsSuccess = false,
                Error = error,
                StatusCode = statusCode
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

        public static BinaryNetworkResponse Success(byte[] data, long statusCode = 200)
        {
            return new BinaryNetworkResponse
            {
                IsSuccess = true,
                Found = true,
                Data = data,
                StatusCode = statusCode
            };
        }

        public static BinaryNetworkResponse NotFound()
        {
            return new BinaryNetworkResponse
            {
                IsSuccess = true,
                Found = false,
                Data = null,
                StatusCode = 204
            };
        }

        public static BinaryNetworkResponse Failure(string error, long statusCode = 0)
        {
            return new BinaryNetworkResponse
            {
                IsSuccess = false,
                Found = false,
                Error = error,
                StatusCode = statusCode
            };
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
