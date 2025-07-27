using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

namespace BackgroundVideoWinForms
{
    public class PexelsVideoClip
    {
        public string Url { get; set; }
        public int Duration { get; set; } // in seconds
        public int Width { get; set; }
        public int Height { get; set; }
        public int FileSize { get; set; } // in bytes
        public int FrameRate { get; set; } // in fps
    }

    public class PexelsService
    {
        private const string PEXELS_API_URL = "https://api.pexels.com/videos/search";

        public async Task<List<PexelsVideoClip>> SearchVideosAsync(string searchTerm, string apiKey, int targetDurationSeconds = 60, bool isVertical = false, int maxPages = 3, CancellationToken cancellationToken = default, int targetWidth = 1920, int targetHeight = 1080, int targetFrameRate = 30)
        {
            Logger.LogApiCall("Pexels Search", $"term={searchTerm}, targetDuration={targetDurationSeconds}s, aspectRatio={(isVertical ? "Vertical" : "Horizontal")}, targetResolution={targetWidth}x{targetHeight}, targetFrameRate={targetFrameRate}fps", true);
            var result = new List<PexelsVideoClip>();
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", apiKey);
                    
                    // Search multiple pages to find enough videos with correct aspect ratio
                    for (int page = 1; page <= maxPages; page++)
                    {
                        // Check for cancellation before each API call
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        string url = $"{PEXELS_API_URL}?query={Uri.EscapeDataString(searchTerm)}&per_page=40&page={page}";
                        Logger.LogDebug($"GET {url} (page {page}/{maxPages})");
                        var response = await client.GetAsync(url, cancellationToken);
                        if (!response.IsSuccessStatusCode)
                        {
                            Logger.LogError($"API call failed with status {response.StatusCode} on page {page}");
                            continue;
                        }
                        var json = await response.Content.ReadAsStringAsync(cancellationToken);
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
                            int bestFrameRate = 30;
                            
                            foreach (var file in video.GetProperty("video_files").EnumerateArray())
                            {
                                int width = file.GetProperty("width").GetInt32();
                                int height = file.GetProperty("height").GetInt32();
                                string link = file.GetProperty("link").GetString();
                                int fileSize = file.TryGetProperty("file_size", out _) ? file.GetProperty("file_size").GetInt32() : 0;
                                
                                // Extract frame rate from URL or metadata
                                int frameRate = 30; // Default frame rate
                                if (link.Contains("30fps"))
                                    frameRate = 30;
                                else if (link.Contains("25fps"))
                                    frameRate = 25;
                                else if (link.Contains("24fps"))
                                    frameRate = 24;
                                else if (link.Contains("60fps"))
                                    frameRate = 60;
                                else if (link.Contains("50fps"))
                                    frameRate = 50;
                                
                                // Filter by target resolution to minimize normalization work
                                bool resolutionMatches = false;
                                
                                // For 1080p target: accept 1080p and 1440p (but not 4K)
                                if (targetWidth == 1920 && targetHeight == 1080)
                                {
                                    // Accept 1080p (1920x1080) and 1440p (2560x1440) for horizontal
                                    // Accept 1080p (1080x1920) and 1440p (1440x2560) for vertical
                                    if (isVertical)
                                    {
                                        resolutionMatches = (width == 1080 && height == 1920) || (width == 1440 && height == 2560);
                                    }
                                    else
                                    {
                                        resolutionMatches = (width == 1920 && height == 1080) || (width == 2560 && height == 1440);
                                    }
                                }
                                // For 4K target: accept 4K and higher resolutions
                                else if (targetWidth == 3840 && targetHeight == 2160)
                                {
                                    // Accept 4K (3840x2160) and 8K (7680x4320) for horizontal
                                    // Accept 4K (2160x3840) and 8K (4320x7680) for vertical
                                    if (isVertical)
                                    {
                                        resolutionMatches = (width == 2160 && height == 3840) || (width == 4320 && height == 7680);
                                    }
                                    else
                                    {
                                        resolutionMatches = (width == 3840 && height == 2160) || (width == 7680 && height == 4320);
                                    }
                                }
                                // For vertical 1080p target
                                else if (targetWidth == 1080 && targetHeight == 1920)
                                {
                                    resolutionMatches = (width == 1080 && height == 1920) || (width == 1440 && height == 2560);
                                }
                                // For vertical 4K target
                                else if (targetWidth == 2160 && targetHeight == 3840)
                                {
                                    resolutionMatches = (width == 2160 && height == 3840) || (width == 4320 && height == 7680);
                                }
                                
                                // Also accept files that are close to target resolution (within 10% tolerance)
                                if (!resolutionMatches)
                                {
                                    double widthRatio = (double)width / targetWidth;
                                    double heightRatio = (double)height / targetHeight;
                                    bool widthClose = Math.Abs(widthRatio - 1.0) <= 0.1; // Within 10%
                                    bool heightClose = Math.Abs(heightRatio - 1.0) <= 0.1; // Within 10%
                                    resolutionMatches = widthClose && heightClose;
                                }
                                
                                // Filter by frame rate to avoid conversion issues
                                bool frameRateMatches = false;
                                
                                // Accept exact frame rate match
                                if (frameRate == targetFrameRate)
                                {
                                    frameRateMatches = true;
                                }
                                // Accept common compatible frame rates (24fps for 30fps target, 25fps for 30fps target)
                                else if (targetFrameRate == 30 && (frameRate == 24 || frameRate == 25))
                                {
                                    frameRateMatches = true;
                                }
                                // Accept 30fps for 25fps target
                                else if (targetFrameRate == 25 && frameRate == 30)
                                {
                                    frameRateMatches = true;
                                }
                                // Accept 24fps for 25fps target
                                else if (targetFrameRate == 25 && frameRate == 24)
                                {
                                    frameRateMatches = true;
                                }
                                
                                // Accept files that match resolution, frame rate, and size criteria
                                if (resolutionMatches && frameRateMatches && fileSize < 1000 * 1024 * 1024) // Less than 1GB
                                {
                                    if (width > bestWidth || (width == bestWidth && fileSize < bestFileSize))
                                    {
                                        bestWidth = width;
                                        bestHeight = height;
                                        bestUrl = link;
                                        bestFileSize = fileSize;
                                        bestFrameRate = frameRate;
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
                                        FileSize = bestFileSize,
                                        FrameRate = bestFrameRate
                                    });
                                    count++;
                                    Logger.LogDebug($"Added video: {duration}s, {bestWidth}x{bestHeight} ({(isVideoVertical ? "Vertical" : "Horizontal")}), {bestFileSize / 1024 / 1024:F1}MB, {bestFrameRate}fps - matches target {targetWidth}x{targetHeight} {targetFrameRate}fps");
                                }
                                else
                                {
                                    Logger.LogDebug($"Skipped video: {duration}s, {bestWidth}x{bestHeight} ({(isVideoVertical ? "Vertical" : "Horizontal")}) - doesn't match requested aspect ratio {(isVertical ? "Vertical" : "Horizontal")}, resolution {targetWidth}x{targetHeight}, or frame rate {targetFrameRate}fps");
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
                    
                    Logger.LogInfo($"Found {result.Count} suitable video results with {(isVertical ? "Vertical" : "Horizontal")} aspect ratio, {targetWidth}x{targetHeight} resolution, and {targetFrameRate}fps frame rate");
                    
                    if (result.Count < 5)
                    {
                        Logger.LogWarning($"Only found {result.Count} videos with {(isVertical ? "Vertical" : "Horizontal")} aspect ratio, {targetWidth}x{targetHeight} resolution, and {targetFrameRate}fps frame rate. Consider trying a different search term, aspect ratio, resolution, or frame rate.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogInfo("Pexels API search was cancelled by user");
                throw; // Re-throw to propagate cancellation
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