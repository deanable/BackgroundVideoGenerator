using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BackgroundVideoWinForms
{
    public class PexelsVideoClip
    {
        public string Url { get; set; }
        public int Duration { get; set; } // in seconds
    }

    public class PexelsService
    {
        private const string PEXELS_API_URL = "https://api.pexels.com/videos/search";

        public async Task<List<PexelsVideoClip>> SearchVideosAsync(string searchTerm, string apiKey)
        {
            Logger.Log($"PexelsService: Searching for '{searchTerm}'");
            var result = new List<PexelsVideoClip>();
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", apiKey);
                    string url = $"{PEXELS_API_URL}?query={Uri.EscapeDataString(searchTerm)}&per_page=40";
                    Logger.Log($"PexelsService: GET {url}");
                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Log($"PexelsService: API call failed with status {response.StatusCode}");
                        return result;
                    }
                    var json = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        var videos = doc.RootElement.GetProperty("videos");
                        int count = 0;
                        foreach (var video in videos.EnumerateArray())
                        {
                            int duration = video.GetProperty("duration").GetInt32();
                            string bestUrl = null;
                            int bestWidth = 0;
                            foreach (var file in video.GetProperty("video_files").EnumerateArray())
                            {
                                int width = file.GetProperty("width").GetInt32();
                                string link = file.GetProperty("link").GetString();
                                if (width > bestWidth)
                                {
                                    bestWidth = width;
                                    bestUrl = link;
                                }
                            }
                            if (!string.IsNullOrEmpty(bestUrl))
                            {
                                result.Add(new PexelsVideoClip { Url = bestUrl, Duration = duration });
                                count++;
                            }
                        }
                        Logger.Log($"PexelsService: Found {count} video results");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "PexelsService.SearchVideosAsync");
            }
            return result;
        }
    }
} 