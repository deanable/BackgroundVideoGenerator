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

                    string size = "large";
                    if (targetWidth <= 1920) size = "medium";
                    if (targetWidth <= 1280) size = "small";

                    // Search multiple pages to find enough videos with correct aspect ratio
                    for (int page = 1; page <= maxPages; page++)
                    {
                        // Check for cancellation before each API call
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        string url = $"{PEXELS_API_URL}?query={Uri.EscapeDataString(searchTerm)}&per_page=80&page={page}&size={size}&min_width={targetWidth}&min_height={targetHeight}";
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

                                int maxDuration = targetDurationSeconds >= 120 ? targetDurationSeconds : Math.Max(60, targetDurationSeconds * 2);
                                if (duration < 3 || duration > maxDuration)
                                {
                                    continue;
                                }

                                string bestUrl = null;
                                int bestWidth = 0;
                                int bestHeight = 0;
                                int bestFileSize = int.MaxValue;

                                foreach (var file in video.GetProperty("video_files").EnumerateArray())
                                {
                                    int width = file.GetProperty("width").GetInt32();
                                    int height = file.GetProperty("height").GetInt32();
                                    string link = file.GetProperty("link").GetString();
                                    int fileSize = file.TryGetProperty("file_size", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number ? sizeElement.GetInt32() : 0;

                                    bool resolutionMatches = (isVertical && height >= width) || (!isVertical && width >= height);

                                    if (resolutionMatches && fileSize > 0 && fileSize < bestFileSize)
                                    {
                                        bestUrl = link;
                                        bestWidth = width;
                                        bestHeight = height;
                                        bestFileSize = fileSize;
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
                                        FileSize = bestFileSize,
                                        FrameRate = targetFrameRate
                                    });
                                    count++;
                                }
                            }
                        }
                        if (result.Count >= 40)
                        {
                            break;
                        }
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