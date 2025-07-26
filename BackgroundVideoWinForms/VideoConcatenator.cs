using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System;
using System.Linq;

namespace BackgroundVideoWinForms
{
    public class VideoConcatenator
    {
        public void Concatenate(List<string> inputFiles, string outputFile, string resolution, Action<string> progressCallback)
        {
            Logger.Log($"VideoConcatenator: Starting concatenation to {outputFile} with resolution {resolution}");
            
            // Validate input files
            var validFiles = new List<string>();
            double totalDuration = 0;
            
            Logger.Log($"VideoConcatenator: Starting duration check for {inputFiles.Count} files");
            
            foreach (var file in inputFiles)
            {
                Logger.Log($"VideoConcatenator: Checking file: {file}");
                
                if (File.Exists(file) && new FileInfo(file).Length > 0)
                {
                    Logger.Log($"VideoConcatenator: File exists and has content, checking duration...");
                    double duration = GetDuration(file);
                    Logger.Log($"VideoConcatenator: File {Path.GetFileName(file)} duration: {duration}s");
                    
                    if (duration > 0 && duration <= 120) // Allow files up to 2 minutes instead of 60 seconds
                    {
                        validFiles.Add(file);
                        totalDuration += duration;
                        Logger.Log($"VideoConcatenator: Added valid file: {Path.GetFileName(file)} (duration: {duration}s, total: {totalDuration}s)");
                    }
                    else
                    {
                        Logger.Log($"VideoConcatenator: Skipping file with invalid duration {duration}s: {file}");
                    }
                }
                else
                {
                    Logger.Log($"VideoConcatenator: Skipping invalid or empty file: {file}");
                }
            }
            
            Logger.Log($"VideoConcatenator: Duration check complete - {validFiles.Count} valid files out of {inputFiles.Count} total files");
            Logger.Log($"VideoConcatenator: Total duration of valid files: {totalDuration}s");
            
            if (validFiles.Count == 0)
            {
                Logger.Log($"VideoConcatenator: No valid input files for concatenation.");
                progressCallback?.Invoke("Error: No valid input files for concatenation.");
                return;
            }
            
            Logger.Log($"VideoConcatenator: Total duration of {validFiles.Count} files: {totalDuration:F1}s");
            
            // Create a more robust concatenation command
            string tempListFile = Path.Combine(Path.GetTempPath(), $"pexels_concat_{Guid.NewGuid()}.txt");
            using (var sw = new StreamWriter(tempListFile))
            {
                foreach (var file in validFiles)
                {
                    sw.WriteLine($"file '{file.Replace("'", "'\\''")}'");
                }
            }
            
            // Improved FFmpeg command with better settings to prevent duplicate frames
            string ffmpegArgs = $"-y -f concat -safe 0 -i \"{tempListFile}\" -vf scale={resolution}:force_original_aspect_ratio=decrease,pad={resolution}:(ow-iw)/2:(oh-ih)/2 -c:v libx264 -preset fast -crf 23 -an -r 30 -vsync 1 -max_muxing_queue_size 1024 \"{outputFile}\"";
            Logger.Log($"VideoConcatenator: ffmpeg {ffmpegArgs}");
            
            try
            {
                string ffmpegPath = @"C:\Program Files (x86)\ffmpeg-2025-07-23-git-829680f96a-full_build\bin\ffmpeg.exe";
                
                if (!File.Exists(ffmpegPath))
                {
                    Logger.Log($"VideoConcatenator: ffmpeg not found at {ffmpegPath}");
                    progressCallback?.Invoke($"Error: FFmpeg not found at {ffmpegPath}");
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
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            Logger.Log($"FFmpeg: {e.Data}");
                            if (progressCallback != null)
                            {
                                var line = e.Data;
                                var idx = line.IndexOf("time=");
                                if (idx >= 0)
                                {
                                    var timePart = line.Substring(idx + 5);
                                    var spaceIdx = timePart.IndexOf(' ');
                                    if (spaceIdx > 0)
                                        timePart = timePart.Substring(0, spaceIdx);
                                    
                                    // Calculate progress percentage with bounds checking
                                    if (TimeSpan.TryParse(timePart, out var currentTime) && totalDuration > 0)
                                    {
                                        double progressPercent = (currentTime.TotalSeconds / totalDuration) * 100;
                                        // Cap progress at 100% to prevent impossible values
                                        progressPercent = Math.Min(progressPercent, 100.0);
                                        progressCallback($"Encoding: {progressPercent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}% ({timePart})");
                                    }
                                    else
                                    {
                                        progressCallback($"Encoding: {timePart}");
                                    }
                                }
                            }
                        }
                    };
                    
                    process.BeginErrorReadLine();
                    
                    // Add timeout to prevent infinite processing
                    if (!process.WaitForExit(300000)) // 5 minute timeout to prevent runaway processes
                    {
                        Logger.Log("VideoConcatenator: Process timeout - killing FFmpeg");
                        try { process.Kill(); } catch { }
                        progressCallback?.Invoke("Error: Process timeout");
                        return;
                    }
                    
                    var endTime = DateTime.Now;
                    Logger.Log($"VideoConcatenator: Process completed in {(endTime - startTime).TotalSeconds:F1}s");
                }
                
                if (File.Exists(outputFile))
                {
                    var fileInfo = new FileInfo(outputFile);
                    Logger.Log($"VideoConcatenator: Output file {outputFile} ({fileInfo.Length} bytes)");
                    progressCallback?.Invoke($"Complete: {fileInfo.Length / 1024 / 1024:F1}MB");
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
                string ffprobePath = @"C:\Program Files (x86)\ffmpeg-2025-07-23-git-829680f96a-full_build\bin\ffprobe.exe";
                
                if (!File.Exists(ffprobePath))
                {
                    Logger.Log($"GetDuration: ffprobe not found at {ffprobePath}");
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
    }
} 