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
            
            // Load all settings from registry
            LoadSettingsFromRegistry();
            
            // Set up event handlers for saving settings
            textBoxApiKey.Leave += textBoxApiKey_Leave;
            textBoxSearch.Leave += textBoxSearch_Leave;
            trackBarDuration.ValueChanged += trackBarDuration_ValueChanged;
            radioButton1080p.CheckedChanged += radioButtonResolution_CheckedChanged;
            radioButton4k.CheckedChanged += radioButtonResolution_CheckedChanged;
            
            // Set up form closing event to save window position
            this.FormClosing += Form1_FormClosing;
            
            Logger.Log("Application started");
        }

        private const string PEXELS_API_URL = "https://api.pexels.com/videos/search";
        private const string REGISTRY_PATH = @"Software\\BackgroundVideoWinForms";
        private const string REGISTRY_APIKEY = "PexelsApiKey";

        private async void buttonStart_Click(object sender, EventArgs e)
        {
            Logger.Log("ButtonStart_Click: User started video generation");
            
            // Save current settings before processing
            SaveSettingsToRegistry();
            
            string searchTerm = textBoxSearch.Text.Trim();
            int duration = trackBarDuration.Value * 60; // minutes to seconds
            string resolution = radioButton4k.Checked ? "3840:2160" : "1920:1080";
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
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "ButtonStart_Click pipeline");
                MessageBox.Show($"An error occurred. See log:\n{Logger.LogFilePath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadSettingsFromRegistry()
        {
            try
            {
                // Load API key
                textBoxApiKey.Text = RegistryHelper.LoadApiKey();
                
                // Load search term
                textBoxSearch.Text = RegistryHelper.LoadSearchTerm();
                
                // Load duration
                int savedDuration = RegistryHelper.LoadDuration();
                trackBarDuration.Value = Math.Max(trackBarDuration.Minimum, Math.Min(trackBarDuration.Maximum, savedDuration));
                
                // Load resolution
                string savedResolution = RegistryHelper.LoadResolution();
                if (savedResolution == "4K")
                {
                    radioButton4k.Checked = true;
                }
                else
                {
                    radioButton1080p.Checked = true;
                }
                
                // Load window position and size
                var (x, y, width, height) = RegistryHelper.LoadWindowPosition();
                if (x >= 0 && y >= 0)
                {
                    this.StartPosition = FormStartPosition.Manual;
                    this.Location = new System.Drawing.Point(x, y);
                }
                if (width > 0 && height > 0)
                {
                    this.Size = new System.Drawing.Size(width, height);
                }
                
                Logger.Log("Settings loaded from registry successfully");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "LoadSettingsFromRegistry failed");
            }
        }

        private void SaveSettingsToRegistry()
        {
            try
            {
                // Save all current settings
                string apiKey = textBoxApiKey.Text.Trim();
                string searchTerm = textBoxSearch.Text.Trim();
                int duration = trackBarDuration.Value;
                string resolution = radioButton4k.Checked ? "4K" : "1080p";
                
                RegistryHelper.SaveAllSettings(apiKey, searchTerm, duration, resolution);
                
                // Save window position and size
                RegistryHelper.SaveWindowPosition(this.Location.X, this.Location.Y, this.Width, this.Height);
                
                Logger.Log("Settings saved to registry successfully");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SaveSettingsToRegistry failed");
            }
        }

        private void textBoxApiKey_Leave(object sender, EventArgs e)
        {
            RegistryHelper.SaveApiKey(textBoxApiKey.Text.Trim());
        }

        private void textBoxSearch_Leave(object sender, EventArgs e)
        {
            RegistryHelper.SaveSearchTerm(textBoxSearch.Text.Trim());
        }

        private void trackBarDuration_ValueChanged(object sender, EventArgs e)
        {
            RegistryHelper.SaveDuration(trackBarDuration.Value);
        }

        private void radioButtonResolution_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is RadioButton radioButton && radioButton.Checked)
            {
                string resolution = radioButton == radioButton4k ? "4K" : "1080p";
                RegistryHelper.SaveResolution(resolution);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettingsToRegistry();
        }

        private async Task<List<string>> DownloadAndNormalizeClipsAsync(List<PexelsVideoClip> clips, int totalDuration, string resolution)
        {
            var downloadedFiles = new List<string>();
            string tempDir = Path.Combine(Path.GetTempPath(), "pexels_bgvid");
            Directory.CreateDirectory(tempDir);
            int targetWidth = 1920, targetHeight = 1080;
            if (radioButton4k.Checked) { targetWidth = 3840; targetHeight = 2160; }

            // Improved clip selection with better duration management
            var selectedClips = new List<PexelsVideoClip>();
            int accumulatedDuration = 0;
            int maxClips = 15; // Increased from 10 to allow more clips
            
            foreach (var clip in clips)
            {
                // Skip clips that are too long (more than 60 seconds instead of 30)
                if (clip.Duration > 60)
                {
                    Logger.Log($"Skipping clip with duration {clip.Duration}s (too long)");
                    continue;
                }
                
                // Stop if we have enough duration or too many clips
                if (accumulatedDuration >= totalDuration || selectedClips.Count >= maxClips)
                    break;
                    
                // Allow exceeding target by up to 20% instead of 10%
                if (accumulatedDuration + clip.Duration <= totalDuration * 1.2)
                {
                    accumulatedDuration += clip.Duration;
                    selectedClips.Add(clip);
                    Logger.Log($"Selected clip: {clip.Duration}s (total: {accumulatedDuration}s)");
                }
            }
            
            Logger.Log($"Selected {selectedClips.Count} clips with total duration {accumulatedDuration}s (target: {totalDuration}s)");
            
            if (selectedClips.Count == 0)
            {
                Logger.Log("No suitable clips found");
                return downloadedFiles;
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
                labelStatus.Text = $"Downloading 0 of {clipsToProcess} clips...";
            }));
            var downloadTimes = new List<double>();
            for (int i = 0; i < clipsToProcess; i++)
            {
                var clip = selectedClips[i];
                string fileName = Path.Combine(tempDir, $"clip_{i}.mp4");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                
                // Update status to show current download
                labelStatus.Invoke((System.Action)(() => {
                    labelStatus.Text = $"Downloading {i + 1} of {clipsToProcess}: {Path.GetFileName(fileName)} ({clip.Duration}s clip)";
                }));
                
                Logger.Log($"Download phase: Starting download {i + 1} of {clipsToProcess}: {clip.Url} (duration: {clip.Duration}s)");
                await videoDownloader.DownloadAsync(clip, fileName);
                sw.Stop();
                downloadTimes.Add(sw.Elapsed.TotalSeconds);
                Logger.Log($"Download phase: Finished download {i + 1} of {clipsToProcess}: {fileName} in {sw.Elapsed.TotalSeconds:F1}s");
                
                // Update progress bar
                progressBar.Invoke((System.Action)(() => {
                    progressBar.Value = i + 1;
                }));
                
                // Calculate and display progress with time estimates
                double avg = downloadTimes.Count > 0 ? downloadTimes.Average() : 0;
                double est = avg * (clipsToProcess - (i + 1));
                double percentComplete = (double)(i + 1) / clipsToProcess * 100;
                
                labelStatus.Invoke((System.Action)(() => {
                    if (i + 1 < clipsToProcess)
                    {
                        labelStatus.Text = $"Downloading {i + 1} of {clipsToProcess} clips ({percentComplete:F0}% complete) - Est. {est:F0}s remaining";
                    }
                    else
                    {
                        labelStatus.Text = $"Download complete! Downloaded {clipsToProcess} clips in {downloadStopwatch.Elapsed.TotalSeconds:F1}s";
                    }
                }));
                
                downloadedFiles.Add(fileName);
            }
            downloadStopwatch.Stop();
            Logger.Log($"Download phase: All downloads complete in {downloadStopwatch.Elapsed.TotalSeconds:F1}s");

            // Optimized normalization phase with hardware acceleration and better progress reporting
            var normStopwatch = new System.Diagnostics.Stopwatch();
            normStopwatch.Start();
            
            // Update UI on main thread
            progressBar.Invoke((System.Action)(() => {
                progressBar.Maximum = clipsToProcess;
                progressBar.Value = 0;
            }));
            
            int maxParallel = Environment.ProcessorCount; // Use all available CPU cores
            
            // Update initial status on main thread
            labelStatus.Invoke((System.Action)(() => {
                labelStatus.Text = $"Normalizing 0 of {clipsToProcess} clips (using {maxParallel} parallel processes)...";
            }));
            
            // Run dimension probing on background thread
            await Task.Run(async () => {
                Logger.Log($"Normalization phase: Probing dimensions for {clipsToProcess} clips on background thread");
                
                // Prepare input and output paths for batch processing
                var inputPaths = new string[clipsToProcess];
                var outputPaths = new string[clipsToProcess];
                var needsNormalization = new bool[clipsToProcess];
                
                // First pass: probe dimensions and determine which files need normalization
                for (int i = 0; i < clipsToProcess; i++)
                {
                    string fileName = downloadedFiles[i];
                    (int w, int h) = videoNormalizer.ProbeDimensions(fileName);
                    needsNormalization[i] = !(w > 0 && h > 0 && w * targetHeight == h * targetWidth);
                    
                    inputPaths[i] = fileName;
                    outputPaths[i] = needsNormalization[i] ? Path.Combine(tempDir, $"clip_{i}_norm.mp4") : fileName;
                    
                    if (needsNormalization[i])
                    {
                        Logger.Log($"Normalization phase: Clip {i + 1} needs normalization ({w}x{h} -> {targetWidth}x{targetHeight})");
                    }
                    else
                    {
                        Logger.Log($"Normalization phase: Clip {i + 1} already correct size ({w}x{h})");
                    }
                }
                
                // Count how many actually need normalization
                int clipsToNormalize = needsNormalization.Count(x => x);
                Logger.Log($"Normalization phase: {clipsToNormalize} of {clipsToProcess} clips need normalization");
                
                if (clipsToNormalize > 0)
                {
                    // Use batch normalization for better performance on background thread
                    var normalizeInputs = inputPaths.Where((_, i) => needsNormalization[i]).ToArray();
                    var normalizeOutputs = outputPaths.Where((_, i) => needsNormalization[i]).ToArray();
                    
                    await videoNormalizer.NormalizeBatchAsync(normalizeInputs, normalizeOutputs, targetWidth, targetHeight, maxParallel, 
                        (index, progress) => {
                            // Find the actual clip index
                            int actualIndex = Array.FindIndex(needsNormalization, x => x) + index;
                            // Update UI on main thread
                            labelStatus.Invoke((System.Action)(() => {
                                labelStatus.Text = $"Normalizing {actualIndex + 1} of {clipsToProcess}: {progress}";
                            }));
                        });
                    
                    // Update downloadedFiles array with normalized paths
                    for (int i = 0; i < clipsToProcess; i++)
                    {
                        if (needsNormalization[i])
                        {
                            downloadedFiles[i] = outputPaths[i];
                            // Clean up original file
                            try { File.Delete(inputPaths[i]); } catch { }
                        }
                    }
                }
            });
            
            normStopwatch.Stop();
            Logger.Log($"Normalization phase: All normalizations complete in {normStopwatch.Elapsed.TotalSeconds:F1}s");
            
            // Update UI on main thread
            labelStatus.Invoke((System.Action)(() => {
                labelStatus.Text = $"Normalization complete! Processed {clipsToProcess} clips in {normStopwatch.Elapsed.TotalSeconds:F1}s";
            }));
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