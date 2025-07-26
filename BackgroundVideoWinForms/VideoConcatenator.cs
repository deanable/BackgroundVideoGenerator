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
            
            foreach (var file in inputFiles)
            {
                if (File.Exists(file) && new FileInfo(file).Length > 0)
                {
                    double duration = GetDuration(file);
                    if (duration > 0 && duration <= 120) // Allow files up to 2 minutes instead of 60 seconds
                    {
                        validFiles.Add(file);
                        totalDuration += duration;
                        Logger.Log($"VideoConcatenator: Valid file {file} - duration: {duration}s");
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
            
            // Improved FFmpeg command with better settings
            string ffmpegArgs = $"-y -f concat -safe 0 -i \"{tempListFile}\" -vf scale={resolution}:force_original_aspect_ratio=decrease,pad={resolution}:(ow-iw)/2:(oh-ih)/2 -c:v libx264 -preset fast -crf 23 -an -r 30 -max_muxing_queue_size 1024 \"{outputFile}\"";
            Logger.Log($"VideoConcatenator: ffmpeg {ffmpegArgs}");
            
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
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
                                    
                                    // Calculate progress percentage
                                    if (TimeSpan.TryParse(timePart, out var currentTime) && totalDuration > 0)
                                    {
                                        double progressPercent = (currentTime.TotalSeconds / totalDuration) * 100;
                                        progressCallback($"Encoding: {progressPercent:F1}% ({timePart})");
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
                    if (!process.WaitForExit(600000)) // 10 minute timeout instead of 5 minutes
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
                var psi = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadLine();
                    process.WaitForExit(5000); // Increased timeout
                    if (double.TryParse(output, out double duration))
                        return duration;
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