using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace BackgroundVideoWinForms
{
    public class PexelsVideoClip
    {
        public string Url { get; set; }
        public int Duration { get; set; } // in seconds
        public int Width { get; set; }
        public int Height { get; set; }
        public int FileSize { get; set; } // in bytes
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
                            
                            // Skip videos that are too long or too short
                            if (duration < 3 || duration > 60)
                            {
                                Logger.Log($"Skipping video with duration {duration}s (outside acceptable range 3-60s)");
                                continue;
                            }
                            
                            string bestUrl = null;
                            int bestWidth = 0;
                            int bestHeight = 0;
                            int bestFileSize = 0;
                            
                            foreach (var file in video.GetProperty("video_files").EnumerateArray())
                            {
                                int width = file.GetProperty("width").GetInt32();
                                int height = file.GetProperty("height").GetInt32();
                                string link = file.GetProperty("link").GetString();
                                int fileSize = file.TryGetProperty("file_size", out _) ? file.GetProperty("file_size").GetInt32() : 0;
                                
                                // Accept a wider range of video qualities and sizes
                                if (width >= 1280 && height >= 720 && fileSize < 1000 * 1024 * 1024) // Less than 1GB, 720p minimum
                                {
                                    if (width > bestWidth || (width == bestWidth && fileSize < bestFileSize))
                                    {
                                        bestWidth = width;
                                        bestHeight = height;
                                        bestUrl = link;
                                        bestFileSize = fileSize;
                                    }
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(bestUrl))
                            {
                                result.Add(new PexelsVideoClip 
                                { 
                                    Url = bestUrl, 
                                    Duration = duration,
                                    Width = bestWidth,
                                    Height = bestHeight,
                                    FileSize = bestFileSize
                                });
                                count++;
                                Logger.Log($"Added video: {duration}s, {bestWidth}x{bestHeight}, {bestFileSize / 1024 / 1024:F1}MB");
                            }
                        }
                        Logger.Log($"PexelsService: Found {count} suitable video results");
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