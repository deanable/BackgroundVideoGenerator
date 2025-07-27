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

        public async Task<List<PexelsVideoClip>> SearchVideosAsync(string searchTerm, string apiKey, int targetDurationSeconds = 60, bool isVertical = false, int maxPages = 3)
        {
            Logger.LogApiCall("Pexels Search", $"term={searchTerm}, targetDuration={targetDurationSeconds}s, aspectRatio={(isVertical ? "Vertical" : "Horizontal")}", true);
            var result = new List<PexelsVideoClip>();
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", apiKey);
                    
                    // Search multiple pages to find enough videos with correct aspect ratio
                    for (int page = 1; page <= maxPages; page++)
                    {
                        string url = $"{PEXELS_API_URL}?query={Uri.EscapeDataString(searchTerm)}&per_page=40&page={page}";
                        Logger.LogDebug($"GET {url} (page {page}/{maxPages})");
                        var response = await client.GetAsync(url);
                        if (!response.IsSuccessStatusCode)
                        {
                            Logger.LogError($"API call failed with status {response.StatusCode} on page {page}");
                            continue;
                        }
                        var json = await response.Content.ReadAsStringAsync();
                        using (JsonDocument doc = JsonDocument.Parse(json))
                        {
                            var videos = doc.RootElement.GetProperty("videos");
                            int count = 0;
                            foreach (var video in videos.EnumerateArray())
                        {
                            int duration = video.GetProperty("duration").GetInt32();
                            
                            // Allow longer videos when target duration is higher
                            int maxDuration = Math.Max(60, targetDurationSeconds / 2); // Allow up to half the target duration
                            if (duration < 3 || duration > maxDuration)
                            {
                                Logger.LogDebug($"Skipping video with duration {duration}s (outside acceptable range 3-{maxDuration}s)");
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
                                // Check aspect ratio compatibility
                                bool isVideoVertical = bestHeight > bestWidth;
                                bool aspectRatioMatches = (isVertical == isVideoVertical);
                                
                                if (aspectRatioMatches)
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
                                    Logger.LogDebug($"Added video: {duration}s, {bestWidth}x{bestHeight} ({(isVideoVertical ? "Vertical" : "Horizontal")}), {bestFileSize / 1024 / 1024:F1}MB");
                                }
                                else
                                {
                                    Logger.LogDebug($"Skipped video: {duration}s, {bestWidth}x{bestHeight} ({(isVideoVertical ? "Vertical" : "Horizontal")}) - doesn't match requested aspect ratio {(isVertical ? "Vertical" : "Horizontal")}");
                                }
                            }
                        }
                        
                        // Stop searching if we have enough videos (at least 10 with correct aspect ratio)
                        if (result.Count >= 10)
                        {
                            Logger.LogInfo($"Found sufficient videos ({result.Count}) with correct aspect ratio, stopping search");
                            break;
                        }
                    } // End of JsonDocument using block
                    } // End of for loop
                    
                    Logger.LogInfo($"Found {result.Count} suitable video results with {(isVertical ? "Vertical" : "Horizontal")} aspect ratio");
                    
                    if (result.Count < 5)
                    {
                        Logger.LogWarning($"Only found {result.Count} videos with {(isVertical ? "Vertical" : "Horizontal")} aspect ratio. Consider trying a different search term or aspect ratio.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "PexelsService.SearchVideosAsync");
            }
            Logger.LogApiCall("Pexels Search", $"term={searchTerm}", result.Count > 0);
            return result;
        }
    }
} 