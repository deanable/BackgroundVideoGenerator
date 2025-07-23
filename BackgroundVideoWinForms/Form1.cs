using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;

namespace BackgroundVideoWinForms
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            labelStatus.Text = "";
            // Load API key from registry
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_PATH))
                {
                    if (key != null)
                    {
                        var value = key.GetValue(REGISTRY_APIKEY) as string;
                        if (!string.IsNullOrEmpty(value))
                            textBoxApiKey.Text = value;
                    }
                }
            }
            catch { }
            textBoxApiKey.Leave += textBoxApiKey_Leave;
        }

        private const string PEXELS_API_URL = "https://api.pexels.com/videos/search";
        private const string REGISTRY_PATH = @"Software\\BackgroundVideoWinForms";
        private const string REGISTRY_APIKEY = "PexelsApiKey";

        private async void buttonStart_Click(object sender, EventArgs e)
        {
            string searchTerm = textBoxSearch.Text.Trim();
            int duration = trackBarDuration.Value * 60; // minutes to seconds
            string resolution = radioButton1080p.Checked ? "1920:1080" :
                                radioButton720p.Checked ? "1280:720" : "854:480";
            string apiKey = textBoxApiKey.Text.Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("Please enter your Pexels API key.");
                return;
            }

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                MessageBox.Show("Please enter a search term.");
                return;
            }

            progressBar.Style = ProgressBarStyle.Marquee;
            labelStatus.Text = "Searching Pexels...";
            buttonStart.Enabled = false;

            // 1. Query Pexels API for video clips matching searchTerm
            var clips = await SearchPexelsVideosAsync(searchTerm, apiKey);
            if (clips == null || clips.Count == 0)
            {
                MessageBox.Show("No videos found for the search term.");
                progressBar.Style = ProgressBarStyle.Blocks;
                labelStatus.Text = "No results.";
                buttonStart.Enabled = true;
                return;
            }

            // 2. Download enough clips to cover 'duration' seconds
            labelStatus.Text = "Downloading video clips...";
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 0;
            var downloadedFiles = await DownloadClipsAsync(clips, duration);
            if (downloadedFiles.Count == 0)
            {
                MessageBox.Show("Failed to download video clips.");
                progressBar.Style = ProgressBarStyle.Blocks;
                labelStatus.Text = "Download failed.";
                buttonStart.Enabled = true;
                return;
            }

            // 3. Concatenate clips using FFmpeg (no audio, selected resolution)
            labelStatus.Text = "Rendering final video...";
            progressBar.Style = ProgressBarStyle.Marquee;
            string safeSearchTerm = string.Join("_", searchTerm.Split(Path.GetInvalidFileNameChars()));
            string outputFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{safeSearchTerm}_{DateTime.Now:yyyyMMddHHmmss}.mp4");
            await Task.Run(() => ConcatenateVideos(downloadedFiles, outputFile, resolution));

            // Cleanup temp files
            foreach (var file in downloadedFiles)
            {
                try { File.Delete(file); } catch { }
            }
            try { Directory.Delete(Path.GetDirectoryName(downloadedFiles[0]), true); } catch { }

            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.Value = progressBar.Maximum;
            labelStatus.Text = $"Done! Saved to {outputFile}";
            buttonStart.Enabled = true;
        }

        private void textBoxApiKey_Leave(object sender, EventArgs e)
        {
            // Save API key to registry when the user leaves the textbox
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(REGISTRY_PATH))
                {
                    key.SetValue(REGISTRY_APIKEY, textBoxApiKey.Text.Trim());
                }
            }
            catch { }
        }

        // Helper: Search Pexels API for videos
        private async Task<List<PexelsVideoClip>> SearchPexelsVideosAsync(string searchTerm, string apiKey)
        {
            var result = new List<PexelsVideoClip>();
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", apiKey);
                    string url = $"{PEXELS_API_URL}?query={Uri.EscapeDataString(searchTerm)}&per_page=40";
                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                        return result;
                    var json = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        var videos = doc.RootElement.GetProperty("videos");
                        foreach (var video in videos.EnumerateArray())
                        {
                            int duration = video.GetProperty("duration").GetInt32();
                            string bestUrl = null;
                            int bestWidth = 0;
                            foreach (var file in video.GetProperty("video_files").EnumerateArray())
                            {
                                int width = file.GetProperty("width").GetInt32();
                                string link = file.GetProperty("link").GetString();
                                // Prefer highest resolution available
                                if (width > bestWidth)
                                {
                                    bestWidth = width;
                                    bestUrl = link;
                                }
                            }
                            if (!string.IsNullOrEmpty(bestUrl))
                                result.Add(new PexelsVideoClip { Url = bestUrl, Duration = duration });
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors, return empty list
            }
            return result;
        }

        // Helper: Download enough clips to cover the requested duration
        private async Task<List<string>> DownloadClipsAsync(List<PexelsVideoClip> clips, int totalDuration)
        {
            var downloadedFiles = new List<string>();
            int accumulated = 0;
            int count = 0;
            string tempDir = Path.Combine(Path.GetTempPath(), "pexels_bgvid");
            Directory.CreateDirectory(tempDir);
            progressBar.Invoke((Action)(() => {
                progressBar.Maximum = Math.Min(clips.Count, 20); // Cap at 20 clips for sanity
                progressBar.Value = 0;
            }));
            foreach (var clip in clips)
            {
                if (accumulated >= totalDuration || count >= 20)
                    break;
                string fileName = Path.Combine(tempDir, $"clip_{count}.mp4");
                try
                {
                    using (var client = new HttpClient())
                    using (var response = await client.GetAsync(clip.Url))
                    using (var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                    downloadedFiles.Add(fileName);
                    accumulated += clip.Duration;
                }
                catch
                {
                    // Ignore failed downloads
                }
                count++;
                progressBar.Invoke((Action)(() => {
                    progressBar.Value = Math.Min(count, progressBar.Maximum);
                }));
            }
            return downloadedFiles;
        }

        // Helper: Concatenate videos using FFmpeg (no audio, selected resolution)
        private void ConcatenateVideos(List<string> videoFiles, string outputFile, string resolution)
        {
            if (videoFiles == null || videoFiles.Count == 0)
                return;
            string tempListFile = Path.Combine(Path.GetTempPath(), $"pexels_concat_{Guid.NewGuid()}.txt");
            using (var sw = new StreamWriter(tempListFile))
            {
                foreach (var file in videoFiles)
                {
                    sw.WriteLine($"file '{file.Replace("'", "'\\''")}'");
                }
            }
            string ffmpegArgs = $"-y -f concat -safe 0 -i \"{tempListFile}\" -vf scale={resolution} -c:v libx264 -preset fast -crf 23 -an \"{outputFile}\"";
            RunFfmpeg(ffmpegArgs);
            try { File.Delete(tempListFile); } catch { }
        }

        // Helper class for Pexels video clip info
        private class PexelsVideoClip
        {
            public string Url { get; set; }
            public int Duration { get; set; } // in seconds
        }

        private void RunFfmpeg(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = new Process { StartInfo = psi })
            {
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        // Parse ffmpeg progress
                        var match = Regex.Match(e.Data, @"time=([0-9:.]+)");
                        if (match.Success)
                        {
                            this.BeginInvoke((Action)(() =>
                            {
                                labelStatus.Text = $"Rendering... {match.Groups[1].Value}";
                            }));
                        }
                    }
                };
                process.Start();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }
        }
    }
} 