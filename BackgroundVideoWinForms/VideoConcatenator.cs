using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System;
using System.Linq;
using System.Threading;

namespace BackgroundVideoWinForms
{
    public class VideoConcatenator
    {
        public bool ConcatenateVideos(List<string> inputFiles, string outputFile, int targetWidth, int targetHeight, Action<string> progressCallback = null)
        {
            if (inputFiles == null || inputFiles.Count == 0)
            {
                Logger.LogError("VideoConcatenator: No input files provided");
                return false;
            }

            Logger.LogPipelineStep("Video Concatenation", $"Concatenating {inputFiles.Count} videos to {Path.GetFileName(outputFile)}");
            
            // Step 1: Validate all input files and get their durations
            var validFiles = new List<string>();
            double totalDuration = 0;
            
            Logger.LogInfo($"VideoConcatenator: Validating {inputFiles.Count} input files...");
            
            foreach (var file in inputFiles)
            {
                if (!File.Exists(file))
                {
                    Logger.LogWarning($"VideoConcatenator: File not found: {file}");
                    continue;
                }

                var duration = GetVideoDuration(file);
                if (duration > 0 && duration <= 120) // Allow files up to 2 minutes instead of 60 seconds
                {
                    validFiles.Add(file);
                    totalDuration += duration;
                    Logger.LogInfo($"VideoConcatenator: Valid file - {Path.GetFileName(file)}: {duration:F1}s (total: {totalDuration:F1}s)");
                }
                else
                {
                    Logger.LogWarning($"VideoConcatenator: Skipping file with invalid duration {duration}s: {Path.GetFileName(file)}");
                }
            }

            if (validFiles.Count == 0)
            {
                Logger.LogError("VideoConcatenator: No valid input files found");
                return false;
            }

            Logger.LogInfo($"VideoConcatenator: Validated {validFiles.Count} files with total duration {totalDuration:F1}s");

            // Step 2: Pre-normalize all clips to identical format to prevent concatenation issues
            var normalizedFiles = new List<string>();
            var tempDir = Path.Combine(Path.GetTempPath(), $"pexels_concat_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            
            Logger.LogInfo($"VideoConcatenator: Pre-normalizing {validFiles.Count} clips to identical format...");
            
            for (int i = 0; i < validFiles.Count; i++)
            {
                var inputFile = validFiles[i];
                var normalizedFile = Path.Combine(tempDir, $"normalized_{i:D3}.mp4");
                
                progressCallback?.Invoke($"Pre-normalizing clip {i + 1}/{validFiles.Count}...");
                
                // Normalize to exact format: 1920x1080, 30fps, yuv420p, libx264
                var normalizeArgs = $"-y -i \"{inputFile}\" -vf scale=w={targetWidth}:h={targetHeight}:force_original_aspect_ratio=decrease,pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2,fps=30 -c:v libx264 -preset fast -crf 23 -r 30 -pix_fmt yuv420p -an -avoid_negative_ts make_zero \"{normalizedFile}\"";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = normalizeArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();
                
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && File.Exists(normalizedFile))
                {
                    var normalizedDuration = GetVideoDuration(normalizedFile);
                    if (normalizedDuration > 0)
                    {
                        normalizedFiles.Add(normalizedFile);
                        Logger.LogInfo($"VideoConcatenator: Normalized clip {i + 1}: {normalizedDuration:F1}s");
                    }
                    else
                    {
                        Logger.LogWarning($"VideoConcatenator: Normalized clip {i + 1} has zero duration, skipping");
                        if (File.Exists(normalizedFile)) File.Delete(normalizedFile);
                    }
                }
                else
                {
                    Logger.LogError($"VideoConcatenator: Failed to normalize clip {i + 1}: {error}");
                    if (File.Exists(normalizedFile)) File.Delete(normalizedFile);
                }
            }

            if (normalizedFiles.Count == 0)
            {
                Logger.LogError("VideoConcatenator: No clips successfully normalized");
                CleanupTempDirectory(tempDir);
                return false;
            }

            Logger.LogInfo($"VideoConcatenator: Successfully normalized {normalizedFiles.Count} clips");

            // Step 3: Create concat list file with normalized clips
            var tempListFile = Path.Combine(tempDir, "concat_list.txt");
            using (var writer = new StreamWriter(tempListFile))
            {
                foreach (var file in normalizedFiles)
                {
                    writer.WriteLine($"file '{file}'");
                }
            }

            Logger.LogInfo($"VideoConcatenator: Created concat list with {normalizedFiles.Count} files");

            // Step 4: Concatenate using stream copy for maximum compatibility
            var resolution = $"{targetWidth}:{targetHeight}";
            var startTime = DateTime.Now;
            
            Logger.LogInfo($"VideoConcatenator: Starting concatenation at {startTime:HH:mm:ss.fff}");
            Logger.LogInfo($"VideoConcatenator: Expected total duration: {totalDuration:F1}s");
            
            // Use a simpler, more reliable concatenation approach
            // Since all files are now normalized to identical format, we can use stream copy
            string ffmpegArgs = $"-y -f concat -safe 0 -i \"{tempListFile}\" -c copy -avoid_negative_ts make_zero \"{outputFile}\"";
            
            Logger.LogInfo($"VideoConcatenator: FFmpeg command: ffmpeg {ffmpegArgs}");
            
            var concatPsi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var concatProcess = new Process { StartInfo = concatPsi };
            concatProcess.Start();
            
            // Monitor the process with timeout
            int timeoutMinutes = CalculateTimeoutMinutes(totalDuration, normalizedFiles.Count);
            bool completed = concatProcess.WaitForExit(timeoutMinutes * 60 * 1000);
            
            if (!completed)
            {
                Logger.LogWarning($"VideoConcatenator: Process timeout after {timeoutMinutes} minutes - killing FFmpeg");
                try
                {
                    concatProcess.Kill();
                    Logger.LogInfo("FFmpeg process killed due to timeout");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"VideoConcatenator: Error killing process: {ex.Message}");
                }
                CleanupTempDirectory(tempDir);
                return false;
            }

            var endTime = DateTime.Now;
            var actualDuration = endTime - startTime;
            Logger.Log($"VideoConcatenator: Process completed at {endTime:HH:mm:ss.fff}");
            Logger.Log($"VideoConcatenator: Actual processing time: {actualDuration:mm\\:ss\\.ff} ({actualDuration.TotalSeconds:F1} seconds)");
            Logger.Log($"VideoConcatenator: Expected vs Actual - Expected: {totalDuration:F1}s, Actual: {actualDuration.TotalSeconds:F1}s");
            
            // Check if process exited normally
            if (concatProcess.ExitCode != 0)
            {
                Logger.LogError($"VideoConcatenator: FFmpeg process failed with exit code {concatProcess.ExitCode}");
                CleanupTempDirectory(tempDir);
                return false;
            }

            // Step 5: Validate output file
            if (!File.Exists(outputFile))
            {
                Logger.LogError($"VideoConcatenator: Output file not created: {outputFile}");
                CleanupTempDirectory(tempDir);
                return false;
            }

            var fileInfo = new FileInfo(outputFile);
            Logger.LogInfo($"VideoConcatenator: Output file {outputFile} ({fileInfo.Length} bytes)");

            // Get actual duration of output file
            var outputDuration = GetVideoDuration(outputFile);
            Logger.LogInfo($"VideoConcatenator: Output file validated - Duration: {outputDuration:F1}s, Size: {fileInfo.Length / (1024.0 * 1024.0):F1}MB");

            // Validate that output duration is reasonable (within 10% of expected)
            var durationDifference = Math.Abs(outputDuration - totalDuration);
            var durationPercentage = (durationDifference / totalDuration) * 100;
            
            if (durationPercentage > 10)
            {
                Logger.LogWarning($"VideoConcatenator: Output duration differs significantly from expected: {outputDuration:F1}s vs {totalDuration:F1}s ({durationPercentage:F1}% difference)");
            }
            else
            {
                Logger.LogInfo($"VideoConcatenator: Output duration is within acceptable range: {outputDuration:F1}s vs {totalDuration:F1}s ({durationPercentage:F1}% difference)");
            }

            // Cleanup
            CleanupTempDirectory(tempDir);
            
            var totalTime = DateTime.Now - startTime;
            Logger.LogInfo($"VideoConcatenator: PERFORMANCE: Video Concatenation completed in {totalTime.TotalMilliseconds:F0}ms - Output: {Path.GetFileName(outputFile)}");
            
            return true;
        }

        private int CalculateTimeoutMinutes(double totalDuration, int fileCount)
        {
            // Base timeout: 10 minutes (increased from 5)
            int baseTimeout = 10;
            
            // Add time based on video duration (2 minutes per 1 minute of video - more generous)
            int durationTimeout = (int)Math.Ceiling(totalDuration / 60.0) * 2;
            
            // Add time based on number of files (1 minute per file - more generous)
            int fileCountTimeout = fileCount;
            
            // Add buffer for system load
            int bufferTimeout = 5;
            
            int totalTimeout = baseTimeout + durationTimeout + fileCountTimeout + bufferTimeout;
            
            // Cap at reasonable maximum (60 minutes instead of 30)
            return Math.Min(totalTimeout, 60);
        }

        private double GetVideoDuration(string filePath)
        {
            try
            {
                Logger.Log($"GetVideoDuration: Attempting to get duration for {filePath}");
                
                // Check if file exists
                if (!File.Exists(filePath))
                {
                    Logger.Log($"GetVideoDuration: File does not exist: {filePath}");
                    return 0;
                }
                
                var fileInfo = new FileInfo(filePath);
                Logger.Log($"GetVideoDuration: File exists, size: {fileInfo.Length} bytes");
                
                // Test ffprobe availability first
                Logger.Log("GetVideoDuration: Testing ffprobe availability...");
                string ffprobePath = FFmpegPathManager.FFprobePath;
                
                if (string.IsNullOrEmpty(ffprobePath))
                {
                    Logger.Log("GetVideoDuration: ffprobe not found. Please configure FFmpeg paths in settings.");
                    return 0;
                }
                
                var testPsi = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                try
                {
                    using (var testProcess = Process.Start(testPsi))
                    {
                        string testOutput = testProcess.StandardOutput.ReadToEnd();
                        testProcess.WaitForExit(3000);
                        Logger.Log($"GetVideoDuration: ffprobe test - exit code: {testProcess.ExitCode}, output length: {testOutput?.Length ?? 0}");
                        if (testProcess.ExitCode != 0)
                        {
                            Logger.Log($"GetVideoDuration: ffprobe not available or failed test");
                            return 0;
                        }
                    }
                }
                catch (Exception testEx)
                {
                    Logger.LogException(testEx, "GetVideoDuration: ffprobe test failed");
                    return 0;
                }
                
                var psi = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                Logger.Log($"GetVideoDuration: Running ffprobe with args: {psi.Arguments}");
                
                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        Logger.Log("GetVideoDuration: Failed to start ffprobe process");
                        return 0;
                    }
                    
                    // Read all output and error
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    
                    Logger.Log($"GetVideoDuration: Waiting for ffprobe to exit...");
                    bool exited = process.WaitForExit(10000); // 10 second timeout
                    
                    Logger.Log($"GetVideoDuration: ffprobe process - exited: {exited}, exit code: {process.ExitCode}");
                    Logger.Log($"GetVideoDuration: ffprobe output: '{output?.Trim()}'");
                    Logger.Log($"GetVideoDuration: ffprobe error: '{error?.Trim()}'");
                    
                    if (!exited)
                    {
                        Logger.Log("GetVideoDuration: ffprobe process timed out");
                        try { process.Kill(); } catch { }
                        return 0;
                    }
                    
                    if (process.ExitCode != 0)
                    {
                        Logger.Log($"GetVideoDuration: ffprobe failed with exit code {process.ExitCode}");
                        return 0;
                    }
                    
                    if (string.IsNullOrWhiteSpace(output))
                    {
                        Logger.Log("GetVideoDuration: ffprobe returned empty output");
                        return 0;
                    }
                    
                    string trimmedOutput = output.Trim();
                    Logger.Log($"GetVideoDuration: Attempting to parse duration from: '{trimmedOutput}'");
                    
                    if (double.TryParse(trimmedOutput, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double duration))
                    {
                        Logger.Log($"GetVideoDuration: Successfully parsed duration: {duration}s");
                        return duration;
                    }
                    else
                    {
                        Logger.Log($"GetVideoDuration: Failed to parse duration from output: '{trimmedOutput}'");
                        // Try alternative parsing with invariant culture
                        if (trimmedOutput.Contains("duration="))
                        {
                            var parts = trimmedOutput.Split('=');
                            if (parts.Length > 1 && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double altDuration))
                            {
                                Logger.Log($"GetVideoDuration: Alternative parsing successful: {altDuration}s");
                                return altDuration;
                            }
                        }
                        // Try parsing with different number formats
                        if (double.TryParse(trimmedOutput.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double fallbackDuration))
                        {
                            Logger.Log($"GetVideoDuration: Fallback parsing successful: {fallbackDuration}s");
                            return fallbackDuration;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"GetVideoDuration: Error getting duration for {filePath}");
            }
            return 0;
        }

        private void CleanupTempDirectory(string tempDir)
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                    Logger.Log($"VideoConcatenator: Cleaned up temporary directory: {tempDir}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"VideoConcatenator: Error cleaning up temporary directory {tempDir}: {ex.Message}");
            }
        }
    }
} 