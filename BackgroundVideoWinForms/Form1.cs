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
using System.Linq; // Added for Average()

namespace BackgroundVideoWinForms
{
    public partial class Form1 : Form
    {
        private PexelsService pexelsService = new PexelsService();
        private VideoDownloader videoDownloader = new VideoDownloader();
        private VideoNormalizer videoNormalizer = new VideoNormalizer();
        private VideoConcatenator videoConcatenator = new VideoConcatenator();

        public Form1()
        {
            InitializeComponent();
            labelStatus.Text = "";
            // Load API key from registry
            textBoxApiKey.Text = RegistryHelper.LoadApiKey();
            textBoxApiKey.Leave += textBoxApiKey_Leave;
            Logger.Log("Application started");
        }

        private const string PEXELS_API_URL = "https://api.pexels.com/videos/search";
        private const string REGISTRY_PATH = @"Software\\BackgroundVideoWinForms";
        private const string REGISTRY_APIKEY = "PexelsApiKey";

        private async void buttonStart_Click(object sender, EventArgs e)
        {
            Logger.Log("ButtonStart_Click: User started video generation");
            string searchTerm = textBoxSearch.Text.Trim();
            int duration = trackBarDuration.Value * 60; // minutes to seconds
            string resolution = radioButton1080p.Checked ? "1920:1080" :
                                radioButton720p.Checked ? "1280:720" : "854:480";
            string apiKey = textBoxApiKey.Text.Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Logger.Log("ButtonStart_Click: No API key entered");
                MessageBox.Show("Please enter your Pexels API key.");
                return;
            }
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                Logger.Log("ButtonStart_Click: No search term entered");
                MessageBox.Show("Please enter a search term.");
                return;
            }

            progressBar.Style = ProgressBarStyle.Marquee;
            labelStatus.Text = "Searching Pexels...";
            buttonStart.Enabled = false;

            try
            {
                // 1. Query Pexels API for video clips matching searchTerm
                Logger.Log($"ButtonStart_Click: Searching for '{searchTerm}' with duration {duration}s and resolution {resolution}");
                var clips = await pexelsService.SearchVideosAsync(searchTerm, apiKey);
                if (clips == null || clips.Count == 0)
                {
                    Logger.Log("ButtonStart_Click: No videos found for the search term");
                    MessageBox.Show("No videos found for the search term.");
                    progressBar.Style = ProgressBarStyle.Blocks;
                    labelStatus.Text = "No results.";
                    buttonStart.Enabled = true;
                    return;
                }

                // 2. Download and normalize enough clips to cover 'duration' seconds
                Logger.Log($"ButtonStart_Click: Downloading and normalizing {clips.Count} clips");
                labelStatus.Text = "Downloading and normalizing video clips...";
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 0;
                var downloadedFiles = await DownloadAndNormalizeClipsAsync(clips, duration, resolution);
                if (downloadedFiles.Count == 0)
                {
                    Logger.Log("ButtonStart_Click: Failed to download video clips");
                    MessageBox.Show("Failed to download video clips.");
                    progressBar.Style = ProgressBarStyle.Blocks;
                    labelStatus.Text = "Download failed.";
                    buttonStart.Enabled = true;
                    return;
                }

                // 3. Concatenate clips using FFmpeg (no audio, selected resolution)
                Logger.Log($"ButtonStart_Click: Concatenating {downloadedFiles.Count} files");
                labelStatus.Text = "Rendering final video (concatenating)...";
                progressBar.Style = ProgressBarStyle.Marquee;
                var concatStopwatch = new System.Diagnostics.Stopwatch();
                concatStopwatch.Start();
                string safeSearchTerm = string.Join("_", searchTerm.Split(Path.GetInvalidFileNameChars()));
                string outputFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{safeSearchTerm}_{System.DateTime.Now:yyyyMMddHHmmss}.mp4");
                await Task.Run(() => videoConcatenator.Concatenate(downloadedFiles, outputFile, resolution, (progress) => {
                    labelStatus.Invoke((System.Action)(() => {
                        labelStatus.Text = $"Encoding: {progress}";
                    }));
                }));
                concatStopwatch.Stop();
                Logger.Log($"Concatenation phase: Completed in {concatStopwatch.Elapsed.TotalSeconds:F1}s");

                // Cleanup temp files
                Logger.Log("ButtonStart_Click: Cleaning up temp files");
                foreach (var file in downloadedFiles)
                {
                    try { File.Delete(file); } catch { }
                }
                try { Directory.Delete(Path.GetDirectoryName(downloadedFiles[0]), true); } catch { }

                progressBar.Style = ProgressBarStyle.Blocks;
                progressBar.Value = progressBar.Maximum;
                labelStatus.Text = $"Done! Saved to {outputFile}";
                buttonStart.Enabled = true;

                // Open the folder containing the output file (robust for .NET Core)
                try
                {
                    if (File.Exists(outputFile))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{outputFile}\"",
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    else
                    {
                        // fallback: open Desktop
                        var psi = new ProcessStartInfo
                        {
                            FileName = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                }
                catch (Exception ex) { Logger.LogException(ex, "Open output folder"); }

                MessageBox.Show($"Pipeline log saved to:\n{Logger.LogFilePath}", "Debug Log", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "ButtonStart_Click pipeline");
                MessageBox.Show($"An error occurred. See log:\n{Logger.LogFilePath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void textBoxApiKey_Leave(object sender, EventArgs e)
        {
            RegistryHelper.SaveApiKey(textBoxApiKey.Text.Trim());
        }

        private async Task<List<string>> DownloadAndNormalizeClipsAsync(List<PexelsVideoClip> clips, int totalDuration, string resolution)
        {
            var downloadedFiles = new List<string>();
            string tempDir = Path.Combine(Path.GetTempPath(), "pexels_bgvid");
            Directory.CreateDirectory(tempDir);
            int targetWidth = 1280, targetHeight = 720;
            if (radioButton1080p.Checked) { targetWidth = 1920; targetHeight = 1080; }
            else if (radioButton480p.Checked) { targetWidth = 854; targetHeight = 480; }

            var selectedClips = new List<PexelsVideoClip>();
            int accumulatedDuration = 0;
            foreach (var clip in clips)
            {
                if (accumulatedDuration >= totalDuration || selectedClips.Count >= 20)
                    break;
                accumulatedDuration += clip.Duration;
                selectedClips.Add(clip);
            }
            int clipsToProcess = selectedClips.Count;

            // Download phase
            var downloadStopwatch = new System.Diagnostics.Stopwatch();
            downloadStopwatch.Start();
            progressBar.Invoke((System.Action)(() => {
                progressBar.Maximum = clipsToProcess;
                progressBar.Value = 0;
            }));
            labelStatus.Invoke((System.Action)(() => {
                labelStatus.Text = $"Downloading 1 of {clipsToProcess}...";
            }));
            var downloadTimes = new List<double>();
            for (int i = 0; i < clipsToProcess; i++)
            {
                var clip = selectedClips[i];
                string fileName = Path.Combine(tempDir, $"clip_{i}.mp4");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                labelStatus.Invoke((System.Action)(() => {
                    labelStatus.Text = $"Downloading {i + 1} of {clipsToProcess}: {Path.GetFileName(fileName)}";
                }));
                Logger.Log($"Download phase: Starting download {i + 1} of {clipsToProcess}: {clip.Url}");
                await videoDownloader.DownloadAsync(clip, fileName);
                sw.Stop();
                downloadTimes.Add(sw.Elapsed.TotalSeconds);
                Logger.Log($"Download phase: Finished download {i + 1} of {clipsToProcess}: {fileName} in {sw.Elapsed.TotalSeconds:F1}s");
                progressBar.Invoke((System.Action)(() => {
                    progressBar.Value = i + 1;
                }));
                double avg = downloadTimes.Count > 0 ? downloadTimes.Average() : 0;
                double est = avg * (clipsToProcess - (i + 1));
                labelStatus.Invoke((System.Action)(() => {
                    labelStatus.Text = $"Downloading {i + 1} of {clipsToProcess}: {Path.GetFileName(fileName)} (Est. {est:F0}s left)";
                }));
                downloadedFiles.Add(fileName);
            }
            downloadStopwatch.Stop();
            Logger.Log($"Download phase: All downloads complete in {downloadStopwatch.Elapsed.TotalSeconds:F1}s");

            // Normalization phase
            var normStopwatch = new System.Diagnostics.Stopwatch();
            normStopwatch.Start();
            progressBar.Invoke((System.Action)(() => {
                progressBar.Maximum = clipsToProcess;
                progressBar.Value = 0;
            }));
            var normTimes = new List<double>();
            for (int i = 0; i < clipsToProcess; i++)
            {
                string fileName = downloadedFiles[i];
                var sw = System.Diagnostics.Stopwatch.StartNew();
                labelStatus.Invoke((System.Action)(() => {
                    labelStatus.Text = $"Normalizing {i + 1} of {clipsToProcess}: {Path.GetFileName(fileName)}";
                }));
                Logger.Log($"Normalization phase: Starting normalization {i + 1} of {clipsToProcess}: {fileName}");
                (int w, int h) = videoNormalizer.ProbeDimensions(fileName);
                bool needsNormalization = !(w > 0 && h > 0 && w * targetHeight == h * targetWidth);
                if (needsNormalization)
                {
                    string normFile = Path.Combine(tempDir, $"clip_{i}_norm.mp4");
                    videoNormalizer.Normalize(fileName, normFile, targetWidth, targetHeight);
                    try { File.Delete(fileName); } catch { }
                    fileName = normFile;
                }
                sw.Stop();
                normTimes.Add(sw.Elapsed.TotalSeconds);
                Logger.Log($"Normalization phase: Finished normalization {i + 1} of {clipsToProcess}: {fileName} in {sw.Elapsed.TotalSeconds:F1}s");
                progressBar.Invoke((System.Action)(() => {
                    progressBar.Value = i + 1;
                }));
                double avg = normTimes.Count > 0 ? normTimes.Average() : 0;
                double est = avg * (clipsToProcess - (i + 1));
                labelStatus.Invoke((System.Action)(() => {
                    labelStatus.Text = $"Normalizing {i + 1} of {clipsToProcess}: {Path.GetFileName(fileName)} (Est. {est:F0}s left)";
                }));
                downloadedFiles[i] = fileName;
            }
            normStopwatch.Stop();
            Logger.Log($"Normalization phase: All normalizations complete in {normStopwatch.Elapsed.TotalSeconds:F1}s");
            labelStatus.Invoke((System.Action)(() => {
                labelStatus.Text = "All clips downloaded and normalized.";
            }));
            return downloadedFiles;
        }

        // Helper: Probe video dimensions using ffprobe (returns width, height)
        private (int, int) ProbeVideoDimensions(string filePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadLine();
                    process.WaitForExit(2000);
                    if (!string.IsNullOrEmpty(output))
                    {
                        var parts = output.Split('x');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                            return (w, h);
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex, $"ProbeVideoDimensions: Error probing dimensions for {filePath}"); }
            return (0, 0);
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