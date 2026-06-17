using System;
using UnityEngine;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Manager;

namespace PM.horizOn.Cloud.Examples.Features
{
    /// <summary>
    /// Minimal example: News.
    ///
    /// What it does: connects, then loads the latest news entries and prints each
    /// title and message.
    /// Setup: import the SDK, set your API key via Window > horizOn > Config Importer,
    /// then attach this script to an empty GameObject and press Play.
    /// Expected Debug.Log output: "Loaded <n> news entries" followed by one line per entry.
    ///
    /// Reference: docs/wiki/sdks/features/news.md
    /// </summary>
    public class NewsExample : MonoBehaviour
    {
        private async void Start()
        {
            try
            {
                HorizonApp.Initialize();

                var server = new HorizonServer();
                if (!await server.Connect())
                {
                    Debug.LogError("[NewsExample] Could not connect to horizOn");
                    return;
                }

                // News does not require authentication.
                // Pass a languageCode such as "en" or "de" to filter by language.
                var news = await NewsManager.Instance.LoadNews(limit: 10);
                if (news != null)
                {
                    Debug.Log($"[NewsExample] Loaded {news.Count} news entries");
                    foreach (var entry in news)
                    {
                        Debug.Log($"[NewsExample] {entry.title}: {entry.message}");
                    }
                }
                else
                {
                    Debug.LogWarning("[NewsExample] Could not load news");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[NewsExample] Unexpected error: {e.Message}");
            }
        }
    }
}
