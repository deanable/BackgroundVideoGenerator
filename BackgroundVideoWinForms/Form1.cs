using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;
using System.Linq; // Added for Average()
using System.Globalization; // Added for NumberStyles

namespace BackgroundVideoWinForms
{
    public partial class Form1 : Form
    {
        private PexelsService pexelsService = new PexelsService();
        private VideoDownloader videoDownloader = new VideoDownloader();
        private VideoNormalizer videoNormalizer = new VideoNormalizer();
        private VideoConcatenator videoConcatenator = new VideoConcatenator();
        
        // Cancellation support
        private CancellationTokenSource cancellationTokenSource;
        private bool isProcessing = false;
        
        // Progress tracking
        private double totalDuration = 0;

        public Form1()
        {
            InitializeComponent();
            labelStatus.Text = "";
            
            // Log system information at startup
            Logger.LogSystemInfo();
            Logger.LogMemoryUsage();
            
            // Initialize FFmpeg paths
            InitializeFFmpegPaths();
            
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

        private void InitializeFFmpegPaths()
        {
            // Load custom FFmpeg paths from registry if set
            string customFFmpegPath = RegistryHelper.LoadFFmpegPath();
            string customFFprobePath = RegistryHelper.LoadFFprobePath();
            
            if (!string.IsNullOrEmpty(customFFmpegPath))
            {
                FFmpegPathManager.SetCustomFFmpegPath(customFFmpegPath, customFFprobePath);
            }
            
            // Validate FFmpeg installation
            if (!FFmpegPathManager.ValidateFFmpegInstallation())
            {
                Logger.LogWarning("FFmpeg installation validation failed. Users may need to configure paths in settings.");
            }
        }

        private const string PEXELS_API_URL = "https://api.pexels.com/videos/search";
        private const string REGISTRY_PATH = @"Software\\BackgroundVideoWinForms";
        private const string REGISTRY_APIKEY = "PexelsApiKey";

        private async void buttonStart_Click(object sender, EventArgs e)
        {
            if (isProcessing)
            {
                MessageBox.Show("Processing is already in progress. Please wait or cancel the current operation.");
                return;
            }

            // Validate FFmpeg installation before starting
            if (!FFmpegPathManager.ValidateFFmpegInstallation())
            {
                var result = MessageBox.Show(
                    "FFmpeg is not properly configured. Would you like to open settings to configure FFmpeg paths?",
                    "FFmpeg Configuration Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                
                if (result == DialogResult.Yes)
                {
                    using (var settingsForm = new SettingsForm())
                    {
                        settingsForm.ShowDialog();
                    }
                }
                return;
            }

            var pipelineStopwatch = Stopwatch.StartNew();
            Logger.LogPipelineStep("Pipeline Start", "User initiated video generation");
            Logger.LogMemoryUsage();
            
            // Initialize cancellation token
            cancellationTokenSource = new CancellationTokenSource();
            isProcessing = true;
            
            // Reset progress tracking variables
            totalDuration = 0;
            Logger.LogDebug("Reset progress tracking variables for new encoding session");
            
            // Update UI
            buttonStart.Enabled = false;
            buttonCancel.Enabled = true;
            
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
                ResetUI();
                return;
            }
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                Logger.LogError("No search term entered");
                MessageBox.Show("Please enter a search term.");
                ResetUI();
                return;
            }

            progressBar.Style = ProgressBarStyle.Marquee;
            labelStatus.Text = "Searching Pexels...";
            buttonStart.Enabled = false;

            List<string> downloadedFiles = null;
            string outputFilePath = string.Empty;

            try
            {
                // 1. Query Pexels API for video clips matching searchTerm
                var searchStopwatch = Stopwatch.StartNew();
                Logger.LogPipelineStep("API Search", $"Searching for '{searchTerm}' with duration {duration}s and resolution {resolution}");
                Logger.LogApiCall("Pexels Search", $"term={searchTerm}, duration={duration}s", true);
                
                bool isVertical = radioButtonVertical.Checked;
                
                // Calculate target resolution for search filtering
                int targetWidth = 1920, targetHeight = 1080;
                if (radioButton4k.Checked) 
                { 
                    targetWidth = 3840; 
                    targetHeight = 2160; 
                }
                if (isVertical)
                {
                    // Swap width and height for vertical orientation
                    int temp = targetWidth;
                    targetWidth = targetHeight;
                    targetHeight = temp;
                }
                
                var clips = await pexelsService.SearchVideosAsync(searchTerm, apiKey, duration, isVertical, 
                    cancellationToken: cancellationTokenSource.Token, 
                    targetWidth: targetWidth, 
                    targetHeight: targetHeight,
                    targetFrameRate: 30);
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
                downloadedFiles = await DownloadAndNormalizeClipsAsync(clips, duration, resolution, cancellationTokenSource.Token);
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
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Maximum = 100;
                progressBar.Value = 0;
                
                var concatStopwatch = new System.Diagnostics.Stopwatch();
                concatStopwatch.Start();
                string safeSearchTerm = string.Join("_", searchTerm.Split(Path.GetInvalidFileNameChars()));
                outputFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{safeSearchTerm}_{System.DateTime.Now:yyyyMMddHHmmss}.mp4");
                
                // Calculate total duration for progress tracking
                double totalVideoDuration = 0;
                int detectedFrameRate = 30; // Default frame rate
                int frameRateCount = 0;
                
                foreach (var file in downloadedFiles)
                {
                    if (File.Exists(file))
                    {
                        double fileDuration = GetVideoDuration(file);
                        int fileFrameRate = GetVideoFrameRate(file);
                        
                        totalVideoDuration += fileDuration;
                        
                        // Calculate average frame rate from all videos
                        if (fileFrameRate > 0)
                        {
                            detectedFrameRate += fileFrameRate;
                            frameRateCount++;
                        }
                        
                        Logger.LogDebug($"File {Path.GetFileName(file)} duration: {fileDuration:F2}s, frame rate: {fileFrameRate}fps");
                    }
                }
                
                // Set average frame rate if we detected any
                if (frameRateCount > 0)
                {
                                    Logger.LogInfo($"Detected average frame rate: {detectedFrameRate / frameRateCount}fps from {frameRateCount} videos");
                }
                
                SetTotalDuration(totalVideoDuration);
                Logger.LogInfo($"Total video duration calculated: {totalVideoDuration:F2}s for progress tracking");
                
                // If total duration is still 0, use the target duration as fallback
                if (totalVideoDuration <= 0)
                {
                    Logger.LogWarning("Total video duration is 0, using target duration as fallback for progress tracking");
                    SetTotalDuration(duration); // Use the target duration from user input
                }
                
                bool concatenationCancelled = false;
                UpdateProgress("High-speed concatenation in progress...", 0);
                var concatenationProgress = new Progress<string>(status => {
                    this.BeginInvoke((Action)(() => {
                        labelStatus.Text = status;
                        Logger.LogDebug($"Concatenation: {status}");
                    }));
                });

                bool concatenationSuccess = await VideoConcatenator.ConcatenateVideosAsync(downloadedFiles, outputFilePath, targetWidth, targetHeight, concatenationProgress);

                // Check if concatenation was cancelled or failed
                if (concatenationCancelled)
                {
                    cancellationTokenSource?.Token.ThrowIfCancellationRequested();
                }

                if (!concatenationSuccess)
                {
                    throw new Exception("Video concatenation failed");
                }

                // Set progress to 100% when concatenation is actually complete
                this.BeginInvoke((Action)(() =>
                {
                    progressBar.Value = progressBar.Maximum;
                    labelStatus.Text = "Encoding: Complete!";
                    Logger.LogInfo("Video concatenation completed - progress bar set to 100%");
                }));
                concatStopwatch.Stop();
                Logger.LogPerformance("Video Concatenation", concatStopwatch.Elapsed, $"Output: {Path.GetFileName(outputFilePath)}");
                
                // Log final file information
                if (File.Exists(outputFilePath))
                {
                    var fileInfo = new FileInfo(outputFilePath);
                    Logger.LogFileOperation("Final Video Created", outputFilePath, fileInfo.Length);
                }

                // Comprehensive cleanup of all temp files
                Logger.LogPipelineStep("Cleanup", "Cleaning up all temporary files");
                
                // Force garbage collection to release file handles
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                // Clean up downloaded and normalized files with improved retry mechanism
                foreach (var file in downloadedFiles)
                {
                    DeleteFileWithRetry(file, "Temp File");
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
                labelStatus.Text = $"Done! Saved to {outputFilePath}";
                buttonStart.Enabled = true;

                pipelineStopwatch.Stop();
                Logger.LogPerformance("Complete Pipeline", pipelineStopwatch.Elapsed, $"Output: {Path.GetFileName(outputFilePath)}");
                Logger.LogPipelineStep("Pipeline Success", $"Video generation completed successfully. Output: {outputFilePath}");
                Logger.LogMemoryUsage();

                // Open the folder containing the output file (robust for .NET Core)
                try
                {
                    if (File.Exists(outputFilePath))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{outputFilePath}\"",
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
            catch (OperationCanceledException)
            {
                Logger.LogInfo("Video generation was cancelled by user");
                
                // Perform cleanup for cancelled operations
                HandleCancellationCleanup();
                
                this.BeginInvoke((Action)(() =>
                {
                    progressBar.Style = ProgressBarStyle.Blocks;
                    progressBar.Value = 0;
                    labelStatus.Text = "Operation cancelled by user";
                    buttonStart.Enabled = true;
                    buttonCancel.Enabled = false;
                }));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Video Generation Pipeline");
                MessageBox.Show($"An error occurred. See log:\n{Logger.LogFilePath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ResetUI();
            }
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            if (isProcessing && cancellationTokenSource != null)
            {
                Logger.LogWarning("User cancelled video generation");
                cancellationTokenSource.Cancel();
                labelStatus.Text = "Cancelling...";
                buttonCancel.Enabled = false;
            }
        }

        private void buttonSettings_Click(object sender, EventArgs e)
        {
            using (var settingsForm = new SettingsForm())
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    Logger.LogInfo("Settings updated successfully");
                }
            }
        }

        private void ResetUI()
        {
            isProcessing = false;
            buttonStart.Enabled = true;
            buttonCancel.Enabled = false;
            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.Value = 0;
            
            // Reset progress tracking
            totalDuration = 0;
            
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }
            
            // Force garbage collection to free memory and file handles
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect(); // Second collection to ensure cleanup
            
            // Additional cleanup for any remaining file handles
            CleanupRemainingFiles();
            
            Logger.LogMemoryUsage();
        }

        private void HandleCancellationCleanup()
        {
            Logger.LogInfo("Performing cancellation cleanup");
            
            // Clean up any temporary files that might have been created
            string tempDir = Path.Combine(Path.GetTempPath(), "pexels_bgvid");
            try 
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                    Logger.LogInfo($"Cleaned up temp directory after cancellation: {tempDir}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to clean up temp directory after cancellation {tempDir}: {ex.Message}");
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
                        Logger.LogFileOperation("Cleaned up Concat File after cancellation", concatFile);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to clean up concat file after cancellation {concatFile}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to clean up concat files after cancellation: {ex.Message}");
            }
        }


        private void SetTotalDuration(double duration)
        {
            totalDuration = duration;
            
            // Use time-based progress calculation instead of frame-based to avoid mismatches
            // FFmpeg output frame rate may vary, so we'll rely on time for accuracy
            Logger.LogInfo($"Set total duration for progress tracking: {duration:F2}s");
            Logger.LogInfo("Using time-based progress calculation for accuracy");
            
            // Validate duration
            if (duration <= 0)
            {
                Logger.LogWarning("Total duration is 0 or negative - progress tracking may not work correctly");
                Logger.LogDebug("This will cause progress bar to use estimated progress instead of accurate percentage");
            }
            else if (duration > 3600) // More than 1 hour
            {
                Logger.LogWarning($"Very long duration detected: {duration:F2}s - this may indicate an issue");
            }
            else
            {
                Logger.LogInfo($"Total duration set successfully: {duration:F2}s - progress bar will show accurate percentage");
            }
            
            Logger.LogDebug("Reset progress tracking");
        }

        private double GetVideoDuration(string filePath)
        {
            try
            {
                string ffprobePath = FFmpegPathManager.FFprobePath;
                
                if (string.IsNullOrEmpty(ffprobePath))
                {
                    Logger.LogError("ffprobe not found. Please configure FFmpeg paths in settings.");
                    return 0;
                }
                
                var psi = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        Logger.LogError("Failed to start ffprobe process");
                        return 0;
                    }
                    
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double duration))
                        {
                            return duration;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "GetVideoDuration");
            }
            
            return 0;
        }

        private int GetVideoFrameRate(string filePath)
        {
            try
            {
                string ffprobePath = FFmpegPathManager.FFprobePath;
                
                if (string.IsNullOrEmpty(ffprobePath))
                {
                    Logger.LogError("ffprobe not found. Please configure FFmpeg paths in settings.");
                    return 30; // Return default frame rate
                }
                
                var psi = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=r_frame_rate -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        Logger.LogError("Failed to start ffprobe process for frame rate detection");
                        return 30;
                    }
                    
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        string frameRateStr = output.Trim();
                        // Parse frame rate in format "30/1" or "30000/1001"
                        if (frameRateStr.Contains("/"))
                        {
                            var parts = frameRateStr.Split('/');
                            if (parts.Length == 2 && 
                                double.TryParse(parts[0], out double numerator) && 
                                double.TryParse(parts[1], out double denominator) && 
                                denominator > 0)
                            {
                                int frameRate = (int)Math.Round(numerator / denominator);
                                Logger.LogDebug($"Detected frame rate: {frameRate}fps from {frameRateStr} for {Path.GetFileName(filePath)}");
                                return frameRate;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "GetVideoFrameRate");
            }
            
            return 30; // Return default frame rate if detection fails
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
                
                // Load window position only (no automatic sizing)
                var (x, y, width, height) = RegistryHelper.LoadWindowPosition();
                if (x >= 0 && y >= 0)
                {
                    this.StartPosition = FormStartPosition.Manual;
                    this.Location = new System.Drawing.Point(x, y);
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
                
                // Save window position only (no automatic sizing)
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

        private async Task<List<string>> DownloadAndNormalizeClipsAsync(List<PexelsVideoClip> clips, int totalDuration, string resolution, CancellationToken cancellationToken)
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
            int maxClips = Math.Max(10, totalDuration / 15); // More conservative max clips based on target duration
            
            Logger.LogInfo($"Clip selection parameters - Max clips: {maxClips}, Target duration: {totalDuration}s");
            
            // Shuffle clips for randomization
            var shuffledClips = clips.OrderBy(x => Random.Shared.Next()).ToList();
            Logger.LogInfo($"Randomized {clips.Count} clips for selection");
            
            // First pass: find clips with exact target resolution and frame rate
            var exactMatchClips = shuffledClips.Where(c => 
                c.Width == targetWidth && 
                c.Height == targetHeight && 
                c.FrameRate == 30).ToList();
            
            Logger.LogInfo($"Found {exactMatchClips.Count} clips with exact format match ({targetWidth}x{targetHeight}, 30fps)");
            
            // Use exact matches first if available
            var clipsToSelect = exactMatchClips.Count >= 3 ? exactMatchClips : shuffledClips;
            
            foreach (var clip in clipsToSelect)
            {
                // Skip clips that are too long (more conservative limit)
                int maxClipDuration = Math.Min(60, totalDuration / 3); // Allow up to 1/3 of target duration, max 60s
                if (clip.Duration > maxClipDuration)
                {
                    Logger.LogDebug($"Skipping clip with duration {clip.Duration}s (too long, max: {maxClipDuration}s)");
                    continue;
                }
                
                // Verify aspect ratio compatibility
                bool isClipVertical = clip.Height > clip.Width;
                bool isTargetVertical = radioButtonVertical.Checked;
                if (isClipVertical != isTargetVertical)
                {
                    Logger.LogDebug($"Skipping clip with wrong aspect ratio: {clip.Width}x{clip.Height} ({(isClipVertical ? "Vertical" : "Horizontal")}) for {(isTargetVertical ? "Vertical" : "Horizontal")} target");
                    continue;
                }
                
                // Stop if we have enough duration or too many clips
                if (accumulatedDuration >= totalDuration || selectedClips.Count >= maxClips)
                    break;
                    
                // More strict duration control - allow exceeding by only 10% instead of 15%
                // This will result in videos much closer to the target duration
                if (accumulatedDuration + clip.Duration <= totalDuration * 1.10)
                {
                    accumulatedDuration += clip.Duration;
                    selectedClips.Add(clip);
                    Logger.LogDebug($"Selected clip: {clip.Duration}s, {clip.Width}x{clip.Height} ({(isClipVertical ? "Vertical" : "Horizontal")}) @ {clip.FrameRate}fps (total: {accumulatedDuration}s)");
                }
                else
                {
                    Logger.LogDebug($"Skipping clip {clip.Duration}s - would exceed target duration limit (current: {accumulatedDuration}s, limit: {totalDuration * 1.10:F1}s)");
                }
            }
            
            Logger.LogInfo($"Selected {selectedClips.Count} clips with total duration {accumulatedDuration}s (target: {totalDuration}s)");
            
            // Log format consistency information
            if (selectedClips.Count > 0)
            {
                var resolutions = selectedClips.Select(c => $"{c.Width}x{c.Height}").Distinct().ToList();
                var frameRates = selectedClips.Select(c => c.FrameRate).Distinct().ToList();
                Logger.LogInfo($"Selected clips have {resolutions.Count} different resolutions: {string.Join(", ", resolutions)}");
                Logger.LogInfo($"Selected clips have {frameRates.Count} different frame rates: {string.Join(", ", frameRates)}");
                
                if (resolutions.Count == 1 && frameRates.Count == 1)
                {
                    Logger.LogInfo("Excellent! All selected clips have consistent format - this will reduce processing errors");
                }
                else
                {
                    Logger.LogWarning("Selected clips have mixed formats - this may cause processing issues");
                }
            }
            
            // Only add more clips if we're significantly under the target (less than 80%)
            if (selectedClips.Count < 2 || accumulatedDuration < totalDuration * 0.8)
            {
                Logger.LogInfo($"Duration too short ({accumulatedDuration}s) or too few clips ({selectedClips.Count}), attempting to add more clips");
                
                // Try to add shorter clips to get closer to target duration
                foreach (var clip in shuffledClips)
                {
                    // Skip if we already have this clip
                    if (selectedClips.Any(c => c.Url == clip.Url))
                        continue;
                        
                    // Skip clips that are too long
                    int maxClipDuration = Math.Min(30, totalDuration / 4); // More conservative limit for additional clips
                    if (clip.Duration > maxClipDuration)
                        continue;
                        
                    // Verify aspect ratio compatibility
                    bool isClipVertical = clip.Height > clip.Width;
                    bool isTargetVertical = radioButtonVertical.Checked;
                    if (isClipVertical != isTargetVertical)
                        continue;
                        
                    // Add clip if it gets us closer to target without exceeding too much (max 15% over target)
                    if (accumulatedDuration + clip.Duration <= totalDuration * 1.15)
                    {
                        accumulatedDuration += clip.Duration;
                        selectedClips.Add(clip);
                        Logger.LogDebug($"Added additional clip: {clip.Duration}s @ {clip.FrameRate}fps (total: {accumulatedDuration}s)");
                        
                        // Stop if we have enough duration (90% of target is sufficient)
                        if (accumulatedDuration >= totalDuration * 0.9)
                            break;
                    }
                }
                
                Logger.LogInfo($"After additional selection: {selectedClips.Count} clips with total duration {accumulatedDuration}s");
            }
            
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
            
            // Memory optimization: Process downloads in smaller batches
            int batchSize = Math.Min(3, clipsToProcess); // Process max 3 downloads at a time
            Logger.LogInfo($"Memory optimization: Processing downloads in batches of {batchSize}");
            
            for (int i = 0; i < clipsToProcess; i += batchSize)
            {
                // Check for cancellation
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogInfo("Download phase cancelled by user");
                    return downloadedFiles;
                }
                
                // Process batch
                int batchEnd = Math.Min(i + batchSize, clipsToProcess);
                var batchTasks = new List<Task<(string fileName, double duration)>>();
                
                for (int j = i; j < batchEnd; j++)
                {
                    var clip = selectedClips[j];
                    string fileName = Path.Combine(tempDir, $"clip_{j}.mp4");
                    
                    Logger.LogProgress("Download", j + 1, clipsToProcess, $"Clip {j + 1}: {clip.Duration}s");
                    Logger.LogDebug($"Starting download {j + 1} of {clipsToProcess}: {clip.Url} (duration: {clip.Duration}s)");
                    
                    var task = DownloadClipWithTimingAsync(clip, fileName, j + 1, clipsToProcess, cancellationToken);
                    batchTasks.Add(task);
                }
                
                // Wait for batch to complete
                var batchResults = await Task.WhenAll(batchTasks);
                
                // Process results
                foreach (var result in batchResults)
                {
                    downloadedFiles.Add(result.fileName);
                    downloadTimes.Add(result.duration);
                }
                
                // Force garbage collection after each batch to free memory
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                Logger.LogDebug($"Completed batch {i / batchSize + 1}, memory usage: {GC.GetTotalMemory(false) / 1024 / 1024}MB");
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
            
            // Check for cancellation before starting normalization
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogInfo("Normalization phase cancelled by user");
                return downloadedFiles;
            }
            
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
                            // Check for cancellation
                            if (cancellationToken.IsCancellationRequested)
                            {
                                Logger.LogInfo("Normalization progress cancelled by user");
                                return;
                            }
                            
                            // Find the actual clip index
                            int actualIndex = Array.FindIndex(needsNormalization, x => x) + index;
                            // Update UI on main thread
                            labelStatus.Invoke((System.Action)(() => {
                                labelStatus.Text = $"Normalizing {actualIndex + 1} of {clipsToProcess}: {progress}";
                            }));
                            Logger.LogProgress("Normalization", index + 1, clipsToNormalize, progress);
                        }, cancellationToken);
                    
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


        /// <summary>
        /// Deletes a file with multiple retry attempts and better error handling
        /// </summary>
        private void DeleteFileWithRetry(string filePath, string fileType)
        {
            if (!File.Exists(filePath))
                return;

            const int maxRetries = 3;
            const int retryDelayMs = 200;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    File.Delete(filePath);
                    Logger.LogFileOperation($"Deleted {fileType}", Path.GetFileName(filePath));
                    return;
                }
                catch (IOException ex)
                {
                    if (attempt == maxRetries)
                    {
                        Logger.LogWarning($"Failed to delete {fileType} {Path.GetFileName(filePath)} after {maxRetries} attempts: {ex.Message}");
                        // Schedule for cleanup later
                        ScheduleFileForCleanup(filePath);
                    }
                    else
                    {
                        Logger.LogDebug($"Attempt {attempt} failed to delete {fileType} {Path.GetFileName(filePath)}: {ex.Message}");
                        Thread.Sleep(retryDelayMs * attempt); // Exponential backoff
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to delete {fileType} {Path.GetFileName(filePath)}: {ex.Message}");
                    return;
                }
            }
        }

        /// <summary>
        /// Schedules a file for cleanup on application exit
        /// </summary>
        private void ScheduleFileForCleanup(string filePath)
        {
            try
            {
                // Create a cleanup marker file
                string cleanupMarker = filePath + ".cleanup";
                File.WriteAllText(cleanupMarker, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Logger.LogDebug($"Scheduled {Path.GetFileName(filePath)} for cleanup on exit");
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"Failed to schedule cleanup for {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        private async Task<(string fileName, double duration)> DownloadClipWithTimingAsync(PexelsVideoClip clip, string fileName, int clipNumber, int totalClips, CancellationToken cancellationToken)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            // Update status to show current download
            labelStatus.Invoke((System.Action)(() => {
                labelStatus.Text = $"Downloading {clipNumber} of {totalClips}: {Path.GetFileName(fileName)} ({clip.Duration}s clip)";
            }));
            
            string downloadedFileName = await videoDownloader.DownloadAsync(clip, fileName, cancellationToken);
            
            sw.Stop();
            Logger.LogPerformance("Individual Download", sw.Elapsed, $"Clip {clipNumber}: {Path.GetFileName(downloadedFileName)}");
            
            // Update progress bar
            progressBar.Invoke((System.Action)(() => {
                progressBar.Value = clipNumber;
            }));
            
            return (downloadedFileName, sw.Elapsed.TotalSeconds);
        }

        private void ParseFFmpegProgress(string line)
        {
            if (line != null)
            {
                this.BeginInvoke((Action)(() =>
                {
                    labelStatus.Text = line;
                }));
            }
        }

        private void UpdateProgress(string message, int value)
        {
            if (progressBar.InvokeRequired)
            {
                progressBar.Invoke(new Action(() => UpdateProgress(message, value)));
            }
            else
            {
                labelStatus.Text = message;
                if (value >= 0)
                {
                    progressBar.Style = ProgressBarStyle.Continuous;
                    progressBar.Value = value;
                }
                else
                {
                    progressBar.Style = ProgressBarStyle.Marquee;
                }
            }
        }

        private void CleanupRemainingFiles()
        {
            try
            {
                // Clean up any scheduled cleanup files
                var cleanupMarkers = Directory.GetFiles(Path.GetTempPath(), "*.cleanup");
                foreach (var marker in cleanupMarkers)
                {
                    try
                    {
                        string originalFile = marker.Replace(".cleanup", "");
                        if (File.Exists(originalFile))
                        {
                            File.Delete(originalFile);
                            Logger.LogDebug($"Cleaned up scheduled file: {Path.GetFileName(originalFile)}");
                        }
                        File.Delete(marker);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDebug($"Failed to clean up scheduled file {marker}: {ex.Message}");
                    }
                }

                // Additional cleanup for temp directory
                string tempDir = Path.Combine(Path.GetTempPath(), "pexels_bgvid");
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        // Try to delete any remaining files in the directory
                        var remainingFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
                        foreach (var file in remainingFiles)
                        {
                            try
                            {
                                File.Delete(file);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogDebug($"Failed to delete remaining file {Path.GetFileName(file)}: {ex.Message}");
                            }
                        }

                        // Try to delete the directory itself
                        Directory.Delete(tempDir, true);
                        Logger.LogDebug("Cleaned up temp directory on exit");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDebug($"Failed to clean up temp directory: {ex.Message}");
                    }
                }

                // Clean up any remaining concat files
                var concatFiles = Directory.GetFiles(Path.GetTempPath(), "pexels_concat_*.txt");
                foreach (var concatFile in concatFiles)
                {
                    try
                    {
                        File.Delete(concatFile);
                        Logger.LogDebug($"Cleaned up concat file: {Path.GetFileName(concatFile)}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDebug($"Failed to clean up concat file {Path.GetFileName(concatFile)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"Error during additional cleanup: {ex.Message}");
            }
        }
    }
}
