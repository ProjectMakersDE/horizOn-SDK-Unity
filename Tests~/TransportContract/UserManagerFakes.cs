using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Enums;

namespace UnityEngine
{
    public static class PlayerPrefs
    {
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>();

        public static bool HasKey(string key) => Values.ContainsKey(key);
        public static string GetString(string key) => Values.TryGetValue(key, out string value) ? value : string.Empty;
        public static void SetString(string key, string value) => Values[key] = value;
        public static void DeleteKey(string key) => Values.Remove(key);
        public static void DeleteAll() => Values.Clear();
        public static void Save() { }
    }

    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };

        public static string ToJson<T>(T value, bool prettyPrint = false)
            => JsonSerializer.Serialize(value, Options);
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    }

    public static class Debug
    {
        public static void LogError(string message) { }
    }
}

namespace PM.horizOn.Cloud.Base
{
    public abstract class BaseManager<T> where T : class
    {
        public static T Instance { get; private set; }

        public virtual bool Init()
        {
            Instance = (T)(object)this;
            OnInit();
            return true;
        }

        protected virtual void OnInit() { }
    }
}

namespace PM.horizOn.Cloud.Core
{
    public static class HorizonApp
    {
        public static TestLog Log { get; } = new TestLog();
        public static TestEvents Events { get; } = new TestEvents();
        public static PM.horizOn.Cloud.Service.NetworkService Network { get; set; }
    }

    public sealed class TestLog
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }

    public sealed class TestEvents
    {
        private readonly List<EventKeys> _published = new List<EventKeys>();

        public void Publish<T>(EventKeys key, T data) => _published.Add(key);
        public bool WasPublished(EventKeys key) => _published.Contains(key);
        public void Clear() => _published.Clear();
    }
}

namespace PM.horizOn.Cloud.Manager
{
    public static class HorizonAppleSignInBridge
    {
        public sealed class AppleAuthResult
        {
            public bool Success { get; set; }
            public string IdentityToken { get; set; }
            public string ErrorCode { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }

        public static Task<AppleAuthResult> RequestSignIn() => Task.FromResult<AppleAuthResult>(null);
    }
}

namespace PM.horizOn.Cloud.Service
{
    public sealed class NetworkService
    {
        public static NetworkService Instance { get; private set; }
        public Func<string, object, object> Respond { get; set; }
        public Func<string, object, Task<object>> RespondAsync { get; set; }
        public string SessionToken { get; private set; }

        public NetworkService() => Instance = this;

        public async Task<NetworkResponse<T>> PostAsync<T>(string endpoint, object requestData) where T : class
            => (NetworkResponse<T>)(RespondAsync == null
                ? Respond(endpoint, requestData)
                : await RespondAsync(endpoint, requestData));

        public void SetSessionToken(string token) => SessionToken = token;
        public void ClearSessionToken() => SessionToken = null;
    }

    public sealed class NetworkResponse<T> where T : class
    {
        public bool IsSuccess { get; private set; }
        public T Data { get; private set; }
        public string Error { get; private set; }
        public long StatusCode { get; private set; }

        public static NetworkResponse<T> Success(T data, long statusCode = 200)
            => new NetworkResponse<T> { IsSuccess = true, Data = data, StatusCode = statusCode };

        public static NetworkResponse<T> Failure(string error, long statusCode)
            => new NetworkResponse<T> { IsSuccess = false, Error = error, StatusCode = statusCode };
    }
}
