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
            
            // Log system information at startup
            Logger.LogSystemInfo();
            Logger.LogMemoryUsage();
            
            // Load all settings from registry
            LoadSettingsFromRegistry();
            
            // Set up event handlers for saving settings
            textBoxApiKey.Leave += textBoxApiKey_Leave;
            textBoxSearch.Leave += textBoxSearch_Leave;
            trackBarDuration.ValueChanged += trackBarDuration_ValueChanged;
            radioButton1080p.CheckedChanged += radioButtonResolution_CheckedChanged;
            radioButton4k.CheckedChanged += radioButtonResolution_CheckedChanged;
            radioButtonHorizontal.CheckedChanged += radioButtonAspectRatio_CheckedChanged;
            radioButtonVertical.CheckedChanged += radioButtonAspectRatio_CheckedChanged;
            
            // Set up form closing event to save window position
            this.FormClosing += Form1_FormClosing;
            
            Logger.LogPipelineStep("Application Initialization", "Form loaded and settings loaded from registry");
        }

        private const string PEXELS_API_URL = "https://api.pexels.com/videos/search";
        private const string REGISTRY_PATH = @"Software\\BackgroundVideoWinForms";
        private const string REGISTRY_APIKEY = "PexelsApiKey";

        private async void buttonStart_Click(object sender, EventArgs e)
        {
            var pipelineStopwatch = Stopwatch.StartNew();
            Logger.LogPipelineStep("Pipeline Start", "User initiated video generation");
            Logger.LogMemoryUsage();
            
            // Save current settings before processing
            SaveSettingsToRegistry();
            
            string searchTerm = textBoxSearch.Text.Trim();
            int duration = trackBarDuration.Value * 60; // minutes to seconds
            string resolution = GetResolutionString();
            string apiKey = textBoxApiKey.Text.Trim();

            Logger.LogInfo($"Pipeline Parameters - Search: '{searchTerm}', Duration: {duration}s, Resolution: {resolution}");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Logger.LogError("No API key entered");
                MessageBox.Show("Please enter your Pexels API key.");
                return;
            }
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                Logger.LogError("No search term entered");
                MessageBox.Show("Please enter a search term.");
                return;
            }

            progressBar.Style = ProgressBarStyle.Marquee;
            labelStatus.Text = "Searching Pexels...";
            buttonStart.Enabled = false;

            try
            {
                // 1. Query Pexels API for video clips matching searchTerm
                var searchStopwatch = Stopwatch.StartNew();
                Logger.LogPipelineStep("API Search", $"Searching for '{searchTerm}' with duration {duration}s and resolution {resolution}");
                Logger.LogApiCall("Pexels Search", $"term={searchTerm}, duration={duration}s", true);
                
                var clips = await pexelsService.SearchVideosAsync(searchTerm, apiKey, duration);
                searchStopwatch.Stop();
                Logger.LogPerformance("Pexels API Search", searchStopwatch.Elapsed, $"Found {clips?.Count ?? 0} clips");
                
                if (clips == null || clips.Count == 0)
                {
                    Logger.LogWarning("No videos found for the search term");
                    MessageBox.Show("No videos found for the search term.");
                    progressBar.Style = ProgressBarStyle.Blocks;
                    labelStatus.Text = "No results.";
                    buttonStart.Enabled = true;
                    return;
                }

                // 2. Download and normalize enough clips to cover 'duration' seconds
                Logger.LogPipelineStep("Download and Normalize", $"Processing {clips.Count} clips for {duration}s duration");
                labelStatus.Text = "Downloading and normalizing video clips...";
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 0;
                
                var downloadNormalizeStopwatch = Stopwatch.StartNew();
                var downloadedFiles = await DownloadAndNormalizeClipsAsync(clips, duration, resolution);
                downloadNormalizeStopwatch.Stop();
                Logger.LogPerformance("Download and Normalize", downloadNormalizeStopwatch.Elapsed, $"Processed {downloadedFiles.Count} files");
                
                if (downloadedFiles.Count == 0)
                {
                    Logger.LogError("Failed to download video clips");
                    MessageBox.Show("Failed to download video clips.");
                    progressBar.Style = ProgressBarStyle.Blocks;
                    labelStatus.Text = "Download failed.";
                    buttonStart.Enabled = true;
                    return;
                }

                // 3. Concatenate clips using FFmpeg (no audio, selected resolution)
                Logger.LogPipelineStep("Video Concatenation", $"Concatenating {downloadedFiles.Count} files");
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
                Logger.LogPerformance("Video Concatenation", concatStopwatch.Elapsed, $"Output: {Path.GetFileName(outputFile)}");
                
                // Log final file information
                if (File.Exists(outputFile))
                {
                    var fileInfo = new FileInfo(outputFile);
                    Logger.LogFileOperation("Final Video Created", outputFile, fileInfo.Length);
                }

                // Comprehensive cleanup of all temp files
                Logger.LogPipelineStep("Cleanup", "Cleaning up all temporary files");
                
                // Clean up downloaded and normalized files
                foreach (var file in downloadedFiles)
                {
                    try 
                    { 
                        if (File.Exists(file)) 
                        {
                            File.Delete(file);
                            Logger.LogFileOperation("Deleted Temp File", file);
                        }
                    } 
                    catch (Exception ex) 
                    { 
                        Logger.LogWarning($"Failed to delete temp file {file}: {ex.Message}");
                    }
                }
                
                // Clean up the entire temp directory
                string tempDir = Path.Combine(Path.GetTempPath(), "pexels_bgvid");
                try 
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                        Logger.LogInfo($"Deleted temp directory: {tempDir}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to delete temp directory {tempDir}: {ex.Message}");
                }
                
                // Clean up any remaining concat list files
                try
                {
                    var concatFiles = Directory.GetFiles(Path.GetTempPath(), "pexels_concat_*.txt");
                    foreach (var concatFile in concatFiles)
                    {
                        try
                        {
                            File.Delete(concatFile);
                            Logger.LogFileOperation("Deleted Concat File", concatFile);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Failed to delete concat file {concatFile}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to clean up concat files: {ex.Message}");
                }

                progressBar.Style = ProgressBarStyle.Blocks;
                progressBar.Value = progressBar.Maximum;
                labelStatus.Text = $"Done! Saved to {outputFile}";
                buttonStart.Enabled = true;

                pipelineStopwatch.Stop();
                Logger.LogPerformance("Complete Pipeline", pipelineStopwatch.Elapsed, $"Output: {Path.GetFileName(outputFile)}");
                Logger.LogPipelineStep("Pipeline Success", $"Video generation completed successfully. Output: {outputFile}");
                Logger.LogMemoryUsage();

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
                Logger.LogException(ex, "Video Generation Pipeline");
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
                labelDuration.Text = $"Duration: {trackBarDuration.Value} minute{(trackBarDuration.Value == 1 ? "" : "s")}";
                
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
                
                // Load aspect ratio
                string savedAspectRatio = RegistryHelper.LoadAspectRatio();
                if (savedAspectRatio == "Vertical")
                {
                    radioButtonVertical.Checked = true;
                }
                else
                {
                    radioButtonHorizontal.Checked = true;
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
                string aspectRatio = radioButtonVertical.Checked ? "Vertical" : "Horizontal";
                
                RegistryHelper.SaveAllSettings(apiKey, searchTerm, duration, resolution, aspectRatio);
                
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

        private void trackBarDuration_Scroll(object sender, EventArgs e)
        {
            labelDuration.Text = $"Duration: {trackBarDuration.Value} minute{(trackBarDuration.Value == 1 ? "" : "s")}";
        }

        private void radioButtonResolution_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is RadioButton radioButton && radioButton.Checked)
            {
                string resolution = radioButton == radioButton4k ? "4K" : "1080p";
                RegistryHelper.SaveResolution(resolution);
            }
        }

        private void radioButtonAspectRatio_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is RadioButton radioButton && radioButton.Checked)
            {
                string aspectRatio = radioButton == radioButtonVertical ? "Vertical" : "Horizontal";
                RegistryHelper.SaveAspectRatio(aspectRatio);
            }
        }

        private string GetResolutionString()
        {
            bool is4K = radioButton4k.Checked;
            bool isVertical = radioButtonVertical.Checked;
            
            if (is4K)
            {
                return isVertical ? "2160:3840" : "3840:2160";
            }
            else
            {
                return isVertical ? "1080:1920" : "1920:1080";
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettingsToRegistry();
        }

        private async Task<List<string>> DownloadAndNormalizeClipsAsync(List<PexelsVideoClip> clips, int totalDuration, string resolution)
        {
            Logger.LogPipelineStep("Download and Normalize Start", $"Processing {clips.Count} clips for {totalDuration}s duration");
            Logger.LogMemoryUsage();
            
            var downloadedFiles = new List<string>();
            string tempDir = Path.Combine(Path.GetTempPath(), "pexels_bgvid");
            Directory.CreateDirectory(tempDir);
            Logger.LogInfo($"Created temp directory: {tempDir}");
            
            int targetWidth = 1920, targetHeight = 1080;
            if (radioButton4k.Checked) 
            { 
                targetWidth = 3840; 
                targetHeight = 2160; 
            }
            if (radioButtonVertical.Checked)
            {
                // Swap width and height for vertical orientation
                int temp = targetWidth;
                targetWidth = targetHeight;
                targetHeight = temp;
            }
            
            Logger.LogInfo($"Target resolution: {targetWidth}x{targetHeight}");

            // Improved clip selection with randomization and better duration management
            var selectedClips = new List<PexelsVideoClip>();
            int accumulatedDuration = 0;
            int maxClips = Math.Max(30, totalDuration / 10); // Dynamic max clips based on target duration
            
            Logger.LogInfo($"Clip selection parameters - Max clips: {maxClips}, Target duration: {totalDuration}s");
            
            // Shuffle clips for randomization
            var shuffledClips = clips.OrderBy(x => Random.Shared.Next()).ToList();
            Logger.LogInfo($"Randomized {clips.Count} clips for selection");
            
            foreach (var clip in shuffledClips)
            {
                // Skip clips that are too long (allow longer clips for longer targets)
                int maxClipDuration = Math.Max(60, totalDuration / 4); // Allow up to 1/4 of target duration
                if (clip.Duration > maxClipDuration)
                {
                    Logger.LogDebug($"Skipping clip with duration {clip.Duration}s (too long, max: {maxClipDuration}s)");
                    continue;
                }
                
                // Stop if we have enough duration or too many clips
                if (accumulatedDuration >= totalDuration || selectedClips.Count >= maxClips)
                    break;
                    
                // Allow exceeding target by up to 30% for better coverage
                if (accumulatedDuration + clip.Duration <= totalDuration * 1.3)
                {
                    accumulatedDuration += clip.Duration;
                    selectedClips.Add(clip);
                    Logger.LogDebug($"Selected clip: {clip.Duration}s (total: {accumulatedDuration}s)");
                }
            }
            
            Logger.LogInfo($"Selected {selectedClips.Count} clips with total duration {accumulatedDuration}s (target: {totalDuration}s)");
            
            if (selectedClips.Count == 0)
            {
                Logger.LogWarning("No suitable clips found");
                return downloadedFiles;
            }
            
            int clipsToProcess = selectedClips.Count;
            Logger.LogInfo($"Starting download phase for {clipsToProcess} clips");

            // Download phase
            var downloadStopwatch = new System.Diagnostics.Stopwatch();
            downloadStopwatch.Start();
            Logger.LogPipelineStep("Download Phase", $"Starting download of {clipsToProcess} clips");
            
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
                
                Logger.LogProgress("Download", i + 1, clipsToProcess, $"Clip {i + 1}: {clip.Duration}s");
                Logger.LogDebug($"Starting download {i + 1} of {clipsToProcess}: {clip.Url} (duration: {clip.Duration}s)");
                await videoDownloader.DownloadAsync(clip, fileName);
                sw.Stop();
                downloadTimes.Add(sw.Elapsed.TotalSeconds);
                Logger.LogPerformance("Individual Download", sw.Elapsed, $"Clip {i + 1}: {Path.GetFileName(fileName)}");
                
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
                
                // Log file information after download
                if (File.Exists(fileName))
                {
                    var fileInfo = new FileInfo(fileName);
                    Logger.LogFileOperation("Downloaded", fileName, fileInfo.Length);
                }
            }
            downloadStopwatch.Stop();
            Logger.LogPerformance("Download Phase Complete", downloadStopwatch.Elapsed, $"Downloaded {clipsToProcess} clips");

            // Optimized normalization phase with hardware acceleration and better progress reporting
            var normStopwatch = new System.Diagnostics.Stopwatch();
            normStopwatch.Start();
            Logger.LogPipelineStep("Normalization Phase", $"Starting normalization of {clipsToProcess} clips");
            
            // Update UI on main thread
            progressBar.Invoke((System.Action)(() => {
                progressBar.Maximum = clipsToProcess;
                progressBar.Value = 0;
            }));
            
            int maxParallel = Environment.ProcessorCount; // Use all available CPU cores
            Logger.LogInfo($"Using {maxParallel} parallel processes for normalization");
            
            // Update initial status on main thread
            labelStatus.Invoke((System.Action)(() => {
                labelStatus.Text = $"Normalizing 0 of {clipsToProcess} clips (using {maxParallel} parallel processes)...";
            }));
            
            // Run dimension probing on background thread
            await Task.Run(async () => {
                Logger.LogInfo($"Normalization phase: Probing dimensions for {clipsToProcess} clips on background thread");
                
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
                        Logger.LogInfo($"Normalization phase: Clip {i + 1} needs normalization ({w}x{h} -> {targetWidth}x{targetHeight})");
                    }
                    else
                    {
                        Logger.LogDebug($"Normalization phase: Clip {i + 1} already correct size ({w}x{h})");
                    }
                }
                
                // Count how many actually need normalization
                int clipsToNormalize = needsNormalization.Count(x => x);
                Logger.LogInfo($"Normalization phase: {clipsToNormalize} of {clipsToProcess} clips need normalization");
                
                if (clipsToNormalize > 0)
                {
                    // Use batch normalization for better performance on background thread
                    var normalizeInputs = inputPaths.Where((_, i) => needsNormalization[i]).ToArray();
                    var normalizeOutputs = outputPaths.Where((_, i) => needsNormalization[i]).ToArray();
                    
                    Logger.LogProgress("Normalization", 0, clipsToNormalize, "Starting batch normalization");
                    
                    await videoNormalizer.NormalizeBatchAsync(normalizeInputs, normalizeOutputs, targetWidth, targetHeight, maxParallel, 
                        (index, progress) => {
                            // Find the actual clip index
                            int actualIndex = Array.FindIndex(needsNormalization, x => x) + index;
                            // Update UI on main thread
                            labelStatus.Invoke((System.Action)(() => {
                                labelStatus.Text = $"Normalizing {actualIndex + 1} of {clipsToProcess}: {progress}";
                            }));
                            Logger.LogProgress("Normalization", index + 1, clipsToNormalize, progress);
                        });
                    
                    // Update downloadedFiles array with normalized paths
                    for (int i = 0; i < clipsToProcess; i++)
                    {
                        if (needsNormalization[i])
                        {
                            downloadedFiles[i] = outputPaths[i];
                            // Clean up original file
                            try { 
                                File.Delete(inputPaths[i]); 
                                Logger.LogFileOperation("Deleted Original", inputPaths[i]);
                            } catch (Exception ex) { 
                                Logger.LogWarning($"Failed to delete original file {inputPaths[i]}: {ex.Message}");
                            }
                        }
                    }
                }
            });
            
            normStopwatch.Stop();
            Logger.LogPerformance("Normalization Phase Complete", normStopwatch.Elapsed, $"Processed {clipsToProcess} clips");
            
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