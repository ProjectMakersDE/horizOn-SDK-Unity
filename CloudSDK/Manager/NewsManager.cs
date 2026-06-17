using System.Collections.Generic;
using System.Threading.Tasks;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Objects.Network.Responses;

namespace PM.horizOn.Cloud.Manager
{
    /// <summary>
    /// Manager for loading and caching news entries.
    /// </summary>
    public class NewsManager : BaseManager<NewsManager>
    {
        private List<UserNewsResponse> _newsCache = new List<UserNewsResponse>();
        private float _lastFetchTime = 0f;
        private const float CACHE_DURATION_SECONDS = 300f; // 5 minutes

        /// <summary>
        /// Load news entries from the server.
        /// </summary>
        /// <param name="limit">Number of entries to load (0-100, default 20)</param>
        /// <param name="languageCode">Optional language code filter (e.g., "en", "de")</param>
        /// <param name="useCache">Whether to use cached news if available</param>
        /// <returns>List of news entries, or null if failed</returns>
        public async Task<List<UserNewsResponse>> LoadNews(int limit = 20, string languageCode = null, bool useCache = true)
        {
            // Check cache
            if (useCache && _newsCache.Count > 0 && !IsCacheExpired())
            {
                HorizonApp.Events.Publish(EventKeys.CacheHit, "News");
                return new List<UserNewsResponse>(_newsCache);
            }

            if (limit > 100)
            {
                HorizonApp.Log.Warning("Limit capped at 100 entries");
                limit = 100;
            }

            // Build endpoint with parameters
            string endpoint = $"/api/v1/app/news?limit={limit}";
            if (!string.IsNullOrEmpty(languageCode))
            {
                endpoint += $"&languageCode={languageCode}";
            }

            var response = await HorizonApp.Network.GetAsync<UserNewsResponse[]>(
                endpoint,
                useSessionToken: false
            );

            if (response.IsSuccess && response.Data != null)
            {
                // Update cache
                _newsCache.Clear();
                _newsCache.AddRange(response.Data);
                _lastFetchTime = UnityEngine.Time.time;

                HorizonApp.Log.Info($"Loaded {_newsCache.Count} news entries");
                HorizonApp.Events.Publish(EventKeys.NewsDataLoaded, _newsCache);

                return new List<UserNewsResponse>(_newsCache);
            }
            else
            {
                HorizonApp.Log.Error($"Failed to load news: {response.Error}");
                return null;
            }
        }

        /// <summary>
        /// Get a specific news entry by ID.
        /// </summary>
        public UserNewsResponse GetNewsById(string id)
        {
            foreach (var entry in _newsCache)
            {
                if (entry.id == id)
                {
                    return entry;
                }
            }
            return null;
        }

        /// <summary>
        /// Clear the news cache.
        /// </summary>
        public void ClearCache()
        {
            _newsCache.Clear();
            _lastFetchTime = 0f;
            HorizonApp.Events.Publish(EventKeys.CacheCleared, "News");
            HorizonApp.Log.Info("News cache cleared");
        }

        /// <summary>
        /// Check if the cache has expired.
        /// </summary>
        private bool IsCacheExpired()
        {
            return (UnityEngine.Time.time - _lastFetchTime) > CACHE_DURATION_SECONDS;
        }
    }
}
