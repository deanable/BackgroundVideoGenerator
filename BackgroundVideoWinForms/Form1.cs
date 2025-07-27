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
        
        // FFmpeg progress tracking
        private long totalFrames = 0;
        private long currentFrame = 0;
        private double totalDuration = 0;
        private double currentTime = 0;
        private int targetFrameRate = 30; // Default frame rate for progress calculation

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
            if (isProcessing)
            {
                MessageBox.Show("Processing is already in progress. Please wait or cancel the current operation.");
                return;
            }

            var pipelineStopwatch = Stopwatch.StartNew();
            Logger.LogPipelineStep("Pipeline Start", "User initiated video generation");
            Logger.LogMemoryUsage();
            
            // Initialize cancellation token
            cancellationTokenSource = new CancellationTokenSource();
            isProcessing = true;
            
            // Reset progress tracking variables
            totalFrames = 0;
            currentFrame = 0;
            totalDuration = 0;
            currentTime = 0;
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

            try
            {
                // 1. Query Pexels API for video clips matching searchTerm
                var searchStopwatch = Stopwatch.StartNew();
                Logger.LogPipelineStep("API Search", $"Searching for '{searchTerm}' with duration {duration}s and resolution {resolution}");
                Logger.LogApiCall("Pexels Search", $"term={searchTerm}, duration={duration}s", true);
                
                bool isVertical = radioButtonVertical.Checked;
                var clips = await pexelsService.SearchVideosAsync(searchTerm, apiKey, duration, isVertical);
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
                var downloadedFiles = await DownloadAndNormalizeClipsAsync(clips, duration, resolution, cancellationTokenSource.Token);
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
                string outputFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{safeSearchTerm}_{System.DateTime.Now:yyyyMMddHHmmss}.mp4");
                
                // Calculate total duration and detect frame rate for progress tracking
                double totalVideoDuration = 0;
                int detectedFrameRate = targetFrameRate;
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
                    targetFrameRate = detectedFrameRate / frameRateCount;
                    Logger.LogInfo($"Detected average frame rate: {targetFrameRate}fps from {frameRateCount} videos");
                }
                
                SetTotalDuration(totalVideoDuration);
                Logger.LogInfo($"Total video duration calculated: {totalVideoDuration:F2}s for progress tracking");
                
                // If total duration is still 0, use the target duration as fallback
                if (totalVideoDuration <= 0)
                {
                    Logger.LogWarning("Total video duration is 0, using target duration as fallback for progress tracking");
                    SetTotalDuration(duration); // Use the target duration from user input
                }
                
                await Task.Run(() => videoConcatenator.Concatenate(downloadedFiles, outputFile, resolution, (progress) => {
                    // Check for cancellation
                    if (cancellationTokenSource?.Token.IsCancellationRequested == true)
                    {
                        throw new OperationCanceledException("Video concatenation was cancelled");
                    }
                    
                    // Parse FFmpeg progress output
                    ParseFFmpegProgress(progress);
                }, cancellationTokenSource?.Token));
                
                // Set progress to 100% when concatenation is actually complete
                this.BeginInvoke((Action)(() =>
                {
                    progressBar.Value = progressBar.Maximum;
                    labelStatus.Text = "Encoding: Complete!";
                    Logger.LogInfo("Video concatenation completed - progress bar set to 100%");
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

        private void ResetUI()
        {
            isProcessing = false;
            buttonStart.Enabled = true;
            buttonCancel.Enabled = false;
            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.Value = 0;
            
            // Reset progress tracking
            totalFrames = 0;
            currentFrame = 0;
            totalDuration = 0;
            currentTime = 0;
            
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

        private void ParseFFmpegProgress(string line)
        {
            try
            {
                // Only process lines that contain progress information
                if (!line.Contains("frame=") || !line.Contains("time="))
                    return;
                
                // Debug logging for progress parsing
                Logger.LogDebug($"Parsing FFmpeg progress: totalDuration={totalDuration:F2}s, totalFrames={totalFrames}, currentFrame={currentFrame}");
                
                // Parse FFmpeg progress line: frame=114037 fps=736 q=29.0 size=88576KiB time=01:03:21.16 bitrate=190.9kbits/s dup=529620 drop=0 speed=24.5x elapsed=0:02:34.83
                var frameMatch = System.Text.RegularExpressions.Regex.Match(line, @"frame=(\d+)");
                // Improved time regex to handle various time formats including MM:SS.SS
                var timeMatch = System.Text.RegularExpressions.Regex.Match(line, @"time=(\d+):(\d+):(\d+\.?\d*)");
                var speedMatch = System.Text.RegularExpressions.Regex.Match(line, @"speed=(\d+\.?\d*)x");
                
                if (frameMatch.Success && timeMatch.Success)
                {
                    currentFrame = long.Parse(frameMatch.Groups[1].Value);
                    int hours = int.Parse(timeMatch.Groups[1].Value);
                    int minutes = int.Parse(timeMatch.Groups[2].Value);
                    
                    // More robust seconds parsing to handle decimal seconds properly
                    string secondsStr = timeMatch.Groups[3].Value;
                    if (!double.TryParse(secondsStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds))
                    {
                        Logger.LogDebug($"Failed to parse seconds from '{secondsStr}' in time string");
                        return;
                    }
                    
                    currentTime = hours * 3600 + minutes * 60 + seconds;
                    
                    // Prioritize frame-based progress calculation for more accuracy
                    if (totalFrames > 0 && currentFrame > 0)
                    {
                        // Validate that current frame doesn't exceed total frames
                        if (currentFrame > totalFrames)
                        {
                            Logger.LogWarning($"Current frame ({currentFrame:N0}) exceeds total frames ({totalFrames:N0}) - using time-based progress instead");
                            // Fall back to time-based progress
                            double rawProgressPercent = (currentTime / totalDuration) * 100.0;
                            double progressPercent = Math.Min(95.0, rawProgressPercent);
                            int progressValue = (int)Math.Round(progressPercent);
                            
                            this.BeginInvoke((Action)(() =>
                            {
                                progressBar.Value = Math.Min(progressValue, progressBar.Maximum);
                                string speed = speedMatch.Success ? $" ({speedMatch.Groups[1].Value}x)" : "";
                                string timeDisplay = $"{hours:D2}:{minutes:D2}:{seconds:F1}";
                                
                                // Show time-based progress with warning
                                if (rawProgressPercent >= 95.0)
                                {
                                    labelStatus.Text = $"Encoding: Finalizing... {timeDisplay} (frame count error){speed}";
                                }
                                else
                                {
                                    labelStatus.Text = $"Encoding: {progressPercent:F1}% - {timeDisplay} (frame count error){speed}";
                                }
                                
                                Logger.LogDebug($"Time-based progress (fallback): {progressPercent:F1}% at {timeDisplay}");
                            }));
                        }
                        else
                        {
                            // Use frame-based progress for most accurate tracking
                            double frameProgressPercent = Math.Min(95.0, (double)currentFrame / totalFrames * 100.0);
                            int progressValue = (int)Math.Round(frameProgressPercent);
                            
                            this.BeginInvoke((Action)(() =>
                            {
                                progressBar.Value = Math.Min(progressValue, progressBar.Maximum);
                                string speed = speedMatch.Success ? $" ({speedMatch.Groups[1].Value}x)" : "";
                                string timeDisplay = $"{hours:D2}:{minutes:D2}:{seconds:F1}";
                                
                                // Show frame-based progress with frame count
                                if (frameProgressPercent >= 95.0)
                                {
                                    labelStatus.Text = $"Encoding: Finalizing... {timeDisplay} ({currentFrame:N0}/{totalFrames:N0} frames){speed}";
                                }
                                else
                                {
                                    labelStatus.Text = $"Encoding: {frameProgressPercent:F1}% - {timeDisplay} ({currentFrame:N0}/{totalFrames:N0} frames){speed}";
                                }
                                
                                // Log frame-based progress updates
                                Logger.LogDebug($"Frame-based progress: {frameProgressPercent:F1}% ({currentFrame:N0}/{totalFrames:N0} frames) at {timeDisplay}");
                            }));
                        }
                    }
                    else if (totalDuration > 0)
                    {
                        // Fallback to time-based progress if frame count is not available
                        double rawProgressPercent = (currentTime / totalDuration) * 100.0;
                        double progressPercent = Math.Min(95.0, rawProgressPercent);
                        int progressValue = (int)Math.Round(progressPercent);
                        
                        this.BeginInvoke((Action)(() =>
                        {
                            progressBar.Value = Math.Min(progressValue, progressBar.Maximum);
                            string speed = speedMatch.Success ? $" ({speedMatch.Groups[1].Value}x)" : "";
                            string timeDisplay = $"{hours:D2}:{minutes:D2}:{seconds:F1}";
                            
                            // Show time-based progress
                            if (rawProgressPercent >= 95.0)
                            {
                                labelStatus.Text = $"Encoding: Finalizing... {timeDisplay}{speed}";
                            }
                            else
                            {
                                labelStatus.Text = $"Encoding: {progressPercent:F1}% - {timeDisplay}{speed}";
                            }
                            
                            // Log time-based progress updates
                            Logger.LogDebug($"Time-based progress: {progressPercent:F1}% (raw: {rawProgressPercent:F1}%) at {timeDisplay}");
                        }));
                    }
                    else
                    {
                        // Basic progress display without percentage - but still update progress bar based on time
                        this.BeginInvoke((Action)(() =>
                        {
                            string speed = speedMatch.Success ? $" ({speedMatch.Groups[1].Value}x)" : "";
                            string timeDisplay = $"{hours:D2}:{minutes:D2}:{seconds:F1}";
                            labelStatus.Text = $"Encoding: {timeDisplay}{speed}";
                            
                            // Update progress bar based on elapsed time even without total duration
                            // Use a more conservative estimate to avoid jumping to 100% too early
                            double estimatedProgress = Math.Min(90.0, (currentTime / 600.0) * 100.0); // Assume 10 minutes max, cap at 90%
                            int progressValue = (int)Math.Round(estimatedProgress);
                            progressBar.Value = Math.Min(progressValue, progressBar.Maximum);
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"Error parsing FFmpeg progress: {ex.Message}");
            }
        }

        private void SetTotalDuration(double duration)
        {
            totalDuration = duration;
            
            // Use output frame rate (30fps) for total frame calculation, not input frame rate
            // FFmpeg encodes output at 30fps regardless of input frame rates
            int outputFrameRate = 30;
            totalFrames = (long)(duration * outputFrameRate);
            
            Logger.LogInfo($"Set total duration for progress tracking: {duration:F2}s");
            Logger.LogInfo($"Calculated total frames: {totalFrames:N0} (duration: {duration:F2}s × {outputFrameRate}fps output)");
            Logger.LogInfo($"Note: Using output frame rate ({outputFrameRate}fps) for frame calculation, not input average ({targetFrameRate}fps)");
            
            // Validate frame calculation
            if (totalFrames <= 0)
            {
                Logger.LogWarning($"Invalid total frames calculated: {totalFrames} - using fallback calculation");
                // Fallback: use target duration from UI
                int targetDurationMinutes = trackBarDuration.Value;
                double targetDurationSeconds = targetDurationMinutes * 60.0;
                totalFrames = (long)(targetDurationSeconds * outputFrameRate);
                Logger.LogInfo($"Fallback total frames: {totalFrames:N0} (target: {targetDurationMinutes}min × 60s × {outputFrameRate}fps)");
            }
            
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
            
            // Reset current frame counter to prevent accumulation issues
            currentFrame = 0;
            Logger.LogDebug($"Reset current frame counter to 0");
        }

        private double GetVideoDuration(string filePath)
        {
            try
            {
                string ffprobePath = @"C:\Program Files (x86)\ffmpeg-2025-07-23-git-829680f96a-full_build\bin\ffprobe.exe";
                
                if (!File.Exists(ffprobePath))
                {
                    Logger.LogError($"ffprobe not found at {ffprobePath}");
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
                string ffprobePath = @"C:\Program Files (x86)\ffmpeg-2025-07-23-git-829680f96a-full_build\bin\ffprobe.exe";
                
                if (!File.Exists(ffprobePath))
                {
                    Logger.LogError($"ffprobe not found at {ffprobePath}");
                    return targetFrameRate; // Return default frame rate
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
                        return targetFrameRate;
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
            
            return targetFrameRate; // Return default frame rate if detection fails
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
                    
                // More precise duration control - allow exceeding by only 15% instead of 30%
                // This will result in videos closer to the target duration
                if (accumulatedDuration + clip.Duration <= totalDuration * 1.15)
                {
                    accumulatedDuration += clip.Duration;
                    selectedClips.Add(clip);
                    Logger.LogDebug($"Selected clip: {clip.Duration}s, {clip.Width}x{clip.Height} ({(isClipVertical ? "Vertical" : "Horizontal")}) (total: {accumulatedDuration}s)");
                }
                else
                {
                    Logger.LogDebug($"Skipping clip {clip.Duration}s - would exceed target duration limit (current: {accumulatedDuration}s, limit: {totalDuration * 1.15:F1}s)");
                }
            }
            
            Logger.LogInfo($"Selected {selectedClips.Count} clips with total duration {accumulatedDuration}s (target: {totalDuration}s)");
            
            // If we have too few clips or duration is too short, try to add more clips
            if (selectedClips.Count < 3 || accumulatedDuration < totalDuration * 0.8)
            {
                Logger.LogInfo($"Duration too short ({accumulatedDuration}s) or too few clips ({selectedClips.Count}), attempting to add more clips");
                
                // Try to add shorter clips to get closer to target duration
                foreach (var clip in shuffledClips)
                {
                    // Skip if we already have this clip
                    if (selectedClips.Any(c => c.Url == clip.Url))
                        continue;
                        
                    // Skip clips that are too long
                    int maxClipDuration = Math.Max(30, totalDuration / 6); // Allow shorter clips
                    if (clip.Duration > maxClipDuration)
                        continue;
                        
                    // Verify aspect ratio compatibility
                    bool isClipVertical = clip.Height > clip.Width;
                    bool isTargetVertical = radioButtonVertical.Checked;
                    if (isClipVertical != isTargetVertical)
                        continue;
                        
                    // Add clip if it gets us closer to target without exceeding too much
                    if (accumulatedDuration + clip.Duration <= totalDuration * 1.2)
                    {
                        accumulatedDuration += clip.Duration;
                        selectedClips.Add(clip);
                        Logger.LogDebug($"Added additional clip: {clip.Duration}s (total: {accumulatedDuration}s)");
                        
                        // Stop if we have enough duration
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
            for (int i = 0; i < clipsToProcess; i++)
            {
                // Check for cancellation
                cancellationToken.ThrowIfCancellationRequested();
                
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
                
                // Check for cancellation
                cancellationToken.ThrowIfCancellationRequested();
                
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
                    // Check for cancellation
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Use batch normalization for better performance on background thread
                    var normalizeInputs = inputPaths.Where((_, i) => needsNormalization[i]).ToArray();
                    var normalizeOutputs = outputPaths.Where((_, i) => needsNormalization[i]).ToArray();
                    
                    Logger.LogProgress("Normalization", 0, clipsToNormalize, "Starting batch normalization");
                    
                    await videoNormalizer.NormalizeBatchAsync(normalizeInputs, normalizeOutputs, targetWidth, targetHeight, maxParallel, 
                        (index, progress) => {
                            // Check for cancellation
                            cancellationToken.ThrowIfCancellationRequested();
                            
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

        /// <summary>
        /// Performs additional cleanup for any remaining files and handles
        /// </summary>
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