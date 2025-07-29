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
        public void Concatenate(List<string> inputFiles, string outputFile, string resolution, Action<string> progressCallback, CancellationToken? cancellationToken = null)
        {
            Logger.LogPipelineStep("Video Concatenation", $"Starting concatenation to {Path.GetFileName(outputFile)} with resolution {resolution}");
            
            // Validate input files
            var validFiles = new List<string>();
            double totalDuration = 0;
            
            Logger.LogInfo($"Starting duration check for {inputFiles.Count} files");
            
            foreach (var file in inputFiles)
            {
                Logger.LogDebug($"Checking file: {Path.GetFileName(file)}");
                
                if (File.Exists(file) && new FileInfo(file).Length > 0)
                {
                    Logger.LogDebug($"File exists and has content, checking duration...");
                    double duration = GetDuration(file);
                    Logger.LogDebug($"File {Path.GetFileName(file)} duration: {duration}s");
                    
                    if (duration > 0 && duration <= 120) // Allow files up to 2 minutes instead of 60 seconds
                    {
                        validFiles.Add(file);
                        totalDuration += duration;
                        Logger.LogDebug($"Added valid file: {Path.GetFileName(file)} (duration: {duration}s, total: {totalDuration}s)");
                    }
                    else
                    {
                        Logger.LogWarning($"Skipping file with invalid duration {duration}s: {Path.GetFileName(file)}");
                    }
                }
                else
                {
                    Logger.LogWarning($"Skipping invalid or empty file: {Path.GetFileName(file)}");
                }
            }
            
            Logger.LogInfo($"Duration check complete - {validFiles.Count} valid files out of {inputFiles.Count} total files");
            Logger.LogInfo($"Total duration of valid files: {totalDuration}s");
            
            if (validFiles.Count == 0)
            {
                Logger.LogError("No valid input files for concatenation.");
                progressCallback?.Invoke("Error: No valid input files for concatenation.");
                return;
            }
            
            Logger.LogInfo($"Total duration of {validFiles.Count} files: {totalDuration:F1}s");
            
            // Additional validation to ensure we have the expected files
            if (totalDuration < 60) // If total duration is less than 1 minute, something is wrong
            {
                Logger.LogWarning($"Total duration ({totalDuration:F1}s) is suspiciously low for {validFiles.Count} files");
                Logger.LogWarning("This may indicate that some files were not properly included in concatenation");
            }
            
            // Create a more robust concatenation command
            string tempListFile = Path.Combine(Path.GetTempPath(), $"pexels_concat_{Guid.NewGuid()}.txt");
            using (var sw = new StreamWriter(tempListFile))
            {
                foreach (var file in validFiles)
                {
                    // Use absolute paths and escape properly for FFmpeg
                    string absolutePath = Path.GetFullPath(file);
                    string escapedPath = absolutePath.Replace("\\", "/").Replace("'", "'\\''");
                    sw.WriteLine($"file '{escapedPath}'");
                    Logger.LogDebug($"Added to concat list: {escapedPath}");
                }
            }
            
            // Log the concatenation list for debugging
            Logger.LogDebug($"Concatenation list created with {validFiles.Count} files:");
            foreach (var file in validFiles)
            {
                Logger.LogDebug($"  - {Path.GetFileName(file)} ({GetDuration(file):F2}s)");
            }
            
            // Improved FFmpeg command with better settings for playback compatibility
            // Added -avoid_negative_ts make_zero to handle timestamp issues
            // Use more efficient settings when all clips have consistent format
            string ffmpegArgs = $"-y -f concat -safe 0 -i \"{tempListFile}\" -vf scale={resolution}:force_original_aspect_ratio=decrease,pad={resolution}:(ow-iw)/2:(oh-ih)/2 -c:v libx264 -preset fast -crf 23 -an -r 30 -pix_fmt yuv420p -movflags +faststart -max_muxing_queue_size 1024 -avoid_negative_ts make_zero \"{outputFile}\"";
            
            // Check if all files have the same format for potential optimization
            bool allSameFormat = CheckConsistentFormat(validFiles);
            if (allSameFormat)
            {
                Logger.LogInfo("All input files have consistent format - using optimized concatenation settings");
                // Use more conservative settings for consistent format
                ffmpegArgs = $"-y -f concat -safe 0 -i \"{tempListFile}\" -c:v libx264 -preset fast -crf 23 -an -r 30 -pix_fmt yuv420p -movflags +faststart -avoid_negative_ts make_zero \"{outputFile}\"";
            }
            else
            {
                Logger.LogInfo("Input files have mixed formats - using standard concatenation with scaling");
            }
            
            Logger.LogFfmpegCommand(ffmpegArgs, tempListFile, outputFile);
            
            try
            {
                string ffmpegPath = FFmpegPathManager.FFmpegPath;
                
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    Logger.LogError("FFmpeg not found. Please configure FFmpeg paths in settings.");
                    progressCallback?.Invoke("Error: FFmpeg not found. Please configure FFmpeg paths in settings.");
                    return;
                }
                
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = ffmpegArgs,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using (var process = Process.Start(psi))
                {
                    var startTime = DateTime.Now;
                    Logger.LogInfo($"Starting FFmpeg concatenation at {startTime:HH:mm:ss.fff}");
                    Logger.LogInfo($"Expected total duration: {totalDuration:F1} seconds");
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            // Check for cancellation
                            if (cancellationToken?.IsCancellationRequested == true)
                            {
                                try
                                {
                                    if (!process.HasExited)
                                    {
                                        process.Kill();
                                        Logger.LogInfo("FFmpeg process killed due to cancellation");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogWarning($"Error killing FFmpeg process: {ex.Message}");
                                }
                                progressCallback?.Invoke("Cancelled");
                                return;
                            }
                            
                            Logger.Log($"FFmpeg: {e.Data}");
                            progressCallback?.Invoke(e.Data);
                        }
                    };
                    
                    process.BeginErrorReadLine();
                    
                    // Calculate dynamic timeout based on video size and duration
                    int timeoutMinutes = CalculateTimeoutMinutes(totalDuration, inputFiles.Count);
                    Logger.Log($"VideoConcatenator: Using {timeoutMinutes} minute timeout for {totalDuration:F1}s video with {inputFiles.Count} files");
                    
                    // Wait for process completion with dynamic timeout and cancellation support
                    bool processCompleted = false;
                    DateTime timeoutDeadline = DateTime.Now.AddMinutes(timeoutMinutes);
                    
                    while (!processCompleted && !process.HasExited)
                    {
                        // Check for cancellation
                        if (cancellationToken?.IsCancellationRequested == true)
                        {
                            try
                            {
                                if (!process.HasExited)
                                {
                                    process.Kill();
                                    Logger.LogInfo("FFmpeg process killed due to cancellation");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning($"Error killing FFmpeg process: {ex.Message}");
                            }
                            progressCallback?.Invoke("Cancelled");
                            return;
                        }
                        
                        // Check for timeout
                        if (DateTime.Now > timeoutDeadline)
                        {
                            Logger.LogWarning($"VideoConcatenator: Process timeout after {timeoutMinutes} minutes - killing FFmpeg");
                            try 
                            { 
                                process.Kill(); 
                                Logger.LogInfo("FFmpeg process killed due to timeout");
                            } 
                            catch (Exception ex)
                            {
                                Logger.LogWarning($"Error killing FFmpeg process on timeout: {ex.Message}");
                            }
                            progressCallback?.Invoke("Error: Process timeout");
                            return;
                        }
                        
                        // Wait a short time before checking again
                        processCompleted = process.WaitForExit(2000); // 2 second timeout for more responsive cancellation
                    }
                    
                    var endTime = DateTime.Now;
                    var actualDuration = endTime - startTime;
                    Logger.Log($"VideoConcatenator: Process completed at {endTime:HH:mm:ss.fff}");
                    Logger.Log($"VideoConcatenator: Actual processing time: {actualDuration:mm\\:ss\\.ff} ({actualDuration.TotalSeconds:F1} seconds)");
                    Logger.Log($"VideoConcatenator: Expected vs Actual - Expected: {totalDuration:F1}s, Actual: {actualDuration.TotalSeconds:F1}s");
                    
                    // Check if process exited normally
                    if (process.ExitCode != 0)
                    {
                        Logger.LogError($"VideoConcatenator: FFmpeg process failed with exit code {process.ExitCode}");
                        progressCallback?.Invoke($"Error: FFmpeg failed with exit code {process.ExitCode}");
                        return;
                    }
                }
                
                if (File.Exists(outputFile))
                {
                    var fileInfo = new FileInfo(outputFile);
                    Logger.Log($"VideoConcatenator: Output file {outputFile} ({fileInfo.Length} bytes)");
                    
                    // Validate the output file has reasonable size and duration
                    if (fileInfo.Length < 1024 * 1024) // Less than 1MB
                    {
                        Logger.LogError($"VideoConcatenator: Output file is too small ({fileInfo.Length} bytes) - likely corrupted");
                        progressCallback?.Invoke("Error: Output file is too small - likely corrupted");
                        return;
                    }
                    
                    // Check if the file has valid duration using ffprobe
                    double outputDuration = GetDuration(outputFile);
                    if (outputDuration <= 0)
                    {
                        Logger.LogError($"VideoConcatenator: Output file has invalid duration ({outputDuration}s) - likely corrupted");
                        progressCallback?.Invoke("Error: Output file has invalid duration - likely corrupted");
                        return;
                    }
                    
                    Logger.LogInfo($"VideoConcatenator: Output file validated - Duration: {outputDuration:F1}s, Size: {fileInfo.Length / 1024 / 1024:F1}MB");
                    progressCallback?.Invoke($"Complete: {fileInfo.Length / 1024 / 1024:F1}MB ({outputDuration:F1}s)");
                }
                else
                {
                    Logger.Log("VideoConcatenator: Output file was not created");
                    progressCallback?.Invoke("Error: Output file not created");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"VideoConcatenator.Concatenate {outputFile}");
                progressCallback?.Invoke($"Error: {ex.Message}");
            }
            finally
            {
                try { File.Delete(tempListFile); } catch { }
            }
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

        private double GetDuration(string filePath)
        {
            try
            {
                Logger.Log($"GetDuration: Attempting to get duration for {filePath}");
                
                // Check if file exists
                if (!File.Exists(filePath))
                {
                    Logger.Log($"GetDuration: File does not exist: {filePath}");
                    return 0;
                }
                
                var fileInfo = new FileInfo(filePath);
                Logger.Log($"GetDuration: File exists, size: {fileInfo.Length} bytes");
                
                // Test ffprobe availability first
                Logger.Log("GetDuration: Testing ffprobe availability...");
                string ffprobePath = FFmpegPathManager.FFprobePath;
                
                if (string.IsNullOrEmpty(ffprobePath))
                {
                    Logger.Log("GetDuration: ffprobe not found. Please configure FFmpeg paths in settings.");
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
                        Logger.Log($"GetDuration: ffprobe test - exit code: {testProcess.ExitCode}, output length: {testOutput?.Length ?? 0}");
                        if (testProcess.ExitCode != 0)
                        {
                            Logger.Log($"GetDuration: ffprobe not available or failed test");
                            return 0;
                        }
                    }
                }
                catch (Exception testEx)
                {
                    Logger.LogException(testEx, "GetDuration: ffprobe test failed");
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
                
                Logger.Log($"GetDuration: Running ffprobe with args: {psi.Arguments}");
                
                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        Logger.Log("GetDuration: Failed to start ffprobe process");
                        return 0;
                    }
                    
                    // Read all output and error
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    
                    Logger.Log($"GetDuration: Waiting for ffprobe to exit...");
                    bool exited = process.WaitForExit(10000); // 10 second timeout
                    
                    Logger.Log($"GetDuration: ffprobe process - exited: {exited}, exit code: {process.ExitCode}");
                    Logger.Log($"GetDuration: ffprobe output: '{output?.Trim()}'");
                    Logger.Log($"GetDuration: ffprobe error: '{error?.Trim()}'");
                    
                    if (!exited)
                    {
                        Logger.Log("GetDuration: ffprobe process timed out");
                        try { process.Kill(); } catch { }
                        return 0;
                    }
                    
                    if (process.ExitCode != 0)
                    {
                        Logger.Log($"GetDuration: ffprobe failed with exit code {process.ExitCode}");
                        return 0;
                    }
                    
                    if (string.IsNullOrWhiteSpace(output))
                    {
                        Logger.Log("GetDuration: ffprobe returned empty output");
                        return 0;
                    }
                    
                    string trimmedOutput = output.Trim();
                    Logger.Log($"GetDuration: Attempting to parse duration from: '{trimmedOutput}'");
                    
                    if (double.TryParse(trimmedOutput, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double duration))
                    {
                        Logger.Log($"GetDuration: Successfully parsed duration: {duration}s");
                        return duration;
                    }
                    else
                    {
                        Logger.Log($"GetDuration: Failed to parse duration from output: '{trimmedOutput}'");
                        // Try alternative parsing with invariant culture
                        if (trimmedOutput.Contains("duration="))
                        {
                            var parts = trimmedOutput.Split('=');
                            if (parts.Length > 1 && double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double altDuration))
                            {
                                Logger.Log($"GetDuration: Alternative parsing successful: {altDuration}s");
                                return altDuration;
                            }
                        }
                        // Try parsing with different number formats
                        if (double.TryParse(trimmedOutput.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double fallbackDuration))
                        {
                            Logger.Log($"GetDuration: Fallback parsing successful: {fallbackDuration}s");
                            return fallbackDuration;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"GetDuration: Error getting duration for {filePath}");
            }
            return 0;
        }

        private bool CheckConsistentFormat(List<string> files)
        {
            if (files.Count == 0) return false;

            string firstFormat = null;
            foreach (var file in files)
            {
                double duration = GetDuration(file);
                if (duration <= 0)
                {
                    Logger.LogWarning($"Skipping file with invalid duration for format check: {Path.GetFileName(file)}");
                    continue;
                }

                string currentFormat = GetFormat(file);
                if (string.IsNullOrEmpty(currentFormat))
                {
                    Logger.LogWarning($"Could not determine format for file: {Path.GetFileName(file)}");
                    continue;
                }

                if (firstFormat == null)
                {
                    firstFormat = currentFormat;
                }
                else if (firstFormat != currentFormat)
                {
                    Logger.LogWarning($"File {Path.GetFileName(file)} has a different format than the first file. Expected: {firstFormat}, Found: {currentFormat}");
                    return false;
                }
            }
            return true;
        }

        private string GetFormat(string filePath)
        {
            try
            {
                Logger.Log($"GetFormat: Attempting to get format for {filePath}");
                
                // Check if file exists
                if (!File.Exists(filePath))
                {
                    Logger.Log($"GetFormat: File does not exist: {filePath}");
                    return null;
                }
                
                var fileInfo = new FileInfo(filePath);
                Logger.Log($"GetFormat: File exists, size: {fileInfo.Length} bytes");
                
                // Test ffprobe availability first
                Logger.Log("GetFormat: Testing ffprobe availability...");
                string ffprobePath = FFmpegPathManager.FFprobePath;
                
                if (string.IsNullOrEmpty(ffprobePath))
                {
                    Logger.Log("GetFormat: ffprobe not found. Please configure FFmpeg paths in settings.");
                    return null;
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
                        Logger.Log($"GetFormat: ffprobe test - exit code: {testProcess.ExitCode}, output length: {testOutput?.Length ?? 0}");
                        if (testProcess.ExitCode != 0)
                        {
                            Logger.Log($"GetFormat: ffprobe not available or failed test");
                            return null;
                        }
                    }
                }
                catch (Exception testEx)
                {
                    Logger.LogException(testEx, "GetFormat: ffprobe test failed");
                    return null;
                }
                
                var psi = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -show_entries format=format_name -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                Logger.Log($"GetFormat: Running ffprobe with args: {psi.Arguments}");
                
                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        Logger.Log("GetFormat: Failed to start ffprobe process");
                        return null;
                    }
                    
                    // Read all output and error
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    
                    Logger.Log($"GetFormat: Waiting for ffprobe to exit...");
                    bool exited = process.WaitForExit(10000); // 10 second timeout
                    
                    Logger.Log($"GetFormat: ffprobe process - exited: {exited}, exit code: {process.ExitCode}");
                    Logger.Log($"GetFormat: ffprobe output: '{output?.Trim()}'");
                    Logger.Log($"GetFormat: ffprobe error: '{error?.Trim()}'");
                    
                    if (!exited)
                    {
                        Logger.Log("GetFormat: ffprobe process timed out");
                        try { process.Kill(); } catch { }
                        return null;
                    }
                    
                    if (process.ExitCode != 0)
                    {
                        Logger.Log($"GetFormat: ffprobe failed with exit code {process.ExitCode}");
                        return null;
                    }
                    
                    if (string.IsNullOrWhiteSpace(output))
                    {
                        Logger.Log("GetFormat: ffprobe returned empty output");
                        return null;
                    }
                    
                    string trimmedOutput = output.Trim();
                    Logger.Log($"GetFormat: Attempting to parse format from: '{trimmedOutput}'");
                    
                    if (trimmedOutput.Contains("format_name="))
                    {
                        var parts = trimmedOutput.Split('=');
                        if (parts.Length > 1)
                        {
                            string format = parts[1].Trim();
                            Logger.Log($"GetFormat: Successfully parsed format: {format}");
                            return format;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"GetFormat: Error getting format for {filePath}");
            }
            return null;
        }
    }
} 