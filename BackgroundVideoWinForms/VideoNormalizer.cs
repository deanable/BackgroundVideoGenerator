using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BackgroundVideoWinForms
{
    public class VideoNormalizer
    {
        public (int, int) ProbeDimensions(string filePath)
        {
            Logger.Log($"VideoNormalizer: Probing dimensions for {filePath}");
            try
            {
                string ffprobePath = @"C:\Program Files (x86)\ffmpeg-2025-07-23-git-829680f96a-full_build\bin\ffprobe.exe";
                
                if (!File.Exists(ffprobePath))
                {
                    Logger.Log($"VideoNormalizer: ffprobe not found at {ffprobePath}");
                    return (0, 0);
                }
                
                var psi = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Logger.Log($"VideoNormalizer: ffprobe {psi.Arguments}");
                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadLine();
                    process.WaitForExit(2000);
                    if (!string.IsNullOrEmpty(output))
                    {
                        var parts = output.Split('x');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                        {
                            Logger.Log($"VideoNormalizer: Probed {filePath} => {w}x{h}");
                            return (w, h);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"VideoNormalizer.ProbeDimensions {filePath}");
            }
            return (0, 0);
        }

        public void Normalize(string inputPath, string outputPath, int targetWidth, int targetHeight, Action<string> progressCallback = null)
        {
            // Try hardware acceleration first, fallback to software if it fails
            bool useHardwareAccel = CheckHardwareAcceleration();
            
            // Optimized FFmpeg arguments for speed
            string ffmpegArgs;
            if (useHardwareAccel)
            {
                // Use hardware acceleration for faster processing
                ffmpegArgs = $"-y -hwaccel auto -i \"{inputPath}\" -vf scale=w={targetWidth}:h={targetHeight}:force_original_aspect_ratio=decrease,pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2 -c:v h264_nvenc -preset p7 -rc vbr -cq 26 -b:v 5M -maxrate 10M -bufsize 10M -an \"{outputPath}\"";
                Logger.Log($"VideoNormalizer: Using hardware acceleration for {inputPath}");
            }
            else
            {
                // Optimized software encoding settings
                ffmpegArgs = $"-y -i \"{inputPath}\" -vf scale=w={targetWidth}:h={targetHeight}:force_original_aspect_ratio=decrease,pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2 -c:v libx264 -preset ultrafast -crf 28 -tune fastdecode -an \"{outputPath}\"";
                Logger.Log($"VideoNormalizer: Using software encoding for {inputPath}");
            }
            
            Logger.Log($"VideoNormalizer: Normalizing {inputPath} to {outputPath} as {targetWidth}x{targetHeight}");
            Logger.Log($"VideoNormalizer: ffmpeg {ffmpegArgs}");
            
            try
            {
                string ffmpegPath = @"C:\Program Files (x86)\ffmpeg-2025-07-23-git-829680f96a-full_build\bin\ffmpeg.exe";
                
                if (!File.Exists(ffmpegPath))
                {
                    Logger.Log($"VideoNormalizer: ffmpeg not found at {ffmpegPath}");
                    throw new Exception($"FFmpeg not found at {ffmpegPath}");
                }
                
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = ffmpegArgs,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                var startTime = DateTime.Now;
                Logger.Log($"VideoNormalizer: Starting normalization of {Path.GetFileName(inputPath)} at {startTime:HH:mm:ss.fff}");
                
                using (var process = Process.Start(psi))
                {
                    // Monitor progress if callback provided
                    if (progressCallback != null)
                    {
                        var progressThread = new Thread(() => MonitorProgress(process, progressCallback));
                        progressThread.Start();
                    }
                    
                    process.WaitForExit();
                    
                    if (process.ExitCode != 0)
                    {
                        Logger.Log($"VideoNormalizer: FFmpeg failed with exit code {process.ExitCode}");
                        
                        // If hardware acceleration failed, try software encoding as fallback
                        if (useHardwareAccel)
                        {
                            Logger.Log($"VideoNormalizer: Hardware acceleration failed, trying software encoding for {inputPath}");
                            useHardwareAccel = false;
                            ffmpegArgs = $"-y -i \"{inputPath}\" -vf scale=w={targetWidth}:h={targetHeight}:force_original_aspect_ratio=decrease,pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2 -c:v libx264 -preset ultrafast -crf 28 -tune fastdecode -an \"{outputPath}\"";
                            
                            // Try again with software encoding
                            psi.Arguments = ffmpegArgs;
                            using (var fallbackProcess = Process.Start(psi))
                            {
                                if (progressCallback != null)
                                {
                                    var progressThread = new Thread(() => MonitorProgress(fallbackProcess, progressCallback));
                                    progressThread.Start();
                                }
                                
                                fallbackProcess.WaitForExit();
                                
                                if (fallbackProcess.ExitCode != 0)
                                {
                                    Logger.Log($"VideoNormalizer: Software encoding also failed with exit code {fallbackProcess.ExitCode}");
                                    throw new Exception($"FFmpeg normalization failed with exit code {fallbackProcess.ExitCode}");
                                }
                            }
                        }
                        else
                        {
                            throw new Exception($"FFmpeg normalization failed with exit code {process.ExitCode}");
                        }
                    }
                }
                
                var endTime = DateTime.Now;
                var actualDuration = endTime - startTime;
                Logger.Log($"VideoNormalizer: Completed normalization of {Path.GetFileName(inputPath)} at {endTime:HH:mm:ss.fff}");
                Logger.Log($"VideoNormalizer: Normalization time: {actualDuration:mm\\:ss\\.ff} ({actualDuration.TotalSeconds:F1} seconds)");
                
                var fileInfo = new FileInfo(outputPath);
                Logger.Log($"VideoNormalizer: Normalized {outputPath} ({fileInfo.Length} bytes)");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"VideoNormalizer.Normalize {inputPath}");
                throw;
            }
        }

        private bool CheckHardwareAcceleration()
        {
            try
            {
                string ffmpegPath = @"C:\Program Files (x86)\ffmpeg-2025-07-23-git-829680f96a-full_build\bin\ffmpeg.exe";
                
                if (!File.Exists(ffmpegPath))
                {
                    Logger.Log($"VideoNormalizer: ffmpeg not found at {ffmpegPath}");
                    return false;
                }
                
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-hide_banner -encoders | findstr nvenc",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(3000);
                    return output.Contains("h264_nvenc");
                }
            }
            catch
            {
                return false;
            }
        }

        private void MonitorProgress(Process process, Action<string> progressCallback)
        {
            try
            {
                while (!process.HasExited)
                {
                    string line = process.StandardError.ReadLine();
                    if (line != null && line.Contains("time="))
                    {
                        // Extract time information from FFmpeg output
                        var timeMatch = System.Text.RegularExpressions.Regex.Match(line, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                        if (timeMatch.Success)
                        {
                            int hours = int.Parse(timeMatch.Groups[1].Value);
                            int minutes = int.Parse(timeMatch.Groups[2].Value);
                            int seconds = int.Parse(timeMatch.Groups[3].Value);
                            int centiseconds = int.Parse(timeMatch.Groups[4].Value);
                            
                            double totalSeconds = hours * 3600 + minutes * 60 + seconds + centiseconds / 100.0;
                            progressCallback($"Processing: {totalSeconds:F1}s");
                        }
                    }
                    Thread.Sleep(100); // Check every 100ms
                }
            }
            catch
            {
                // Ignore errors in progress monitoring
            }
        }

        // Batch normalization for multiple files with parallel processing
        public async Task NormalizeBatchAsync(string[] inputPaths, string[] outputPaths, int targetWidth, int targetHeight, int maxParallel = 2, Action<int, string> progressCallback = null)
        {
            var semaphore = new SemaphoreSlim(maxParallel);
            var tasks = new List<Task>();
            
            for (int i = 0; i < inputPaths.Length; i++)
            {
                int index = i;
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        progressCallback?.Invoke(index, $"Starting normalization {index + 1} of {inputPaths.Length}");
                        await Task.Run(() => Normalize(inputPaths[index], outputPaths[index], targetWidth, targetHeight, 
                            (progress) => progressCallback?.Invoke(index, progress)));
                        progressCallback?.Invoke(index, $"Completed normalization {index + 1} of {inputPaths.Length}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }
            
            await Task.WhenAll(tasks.ToArray());
        }
        
        // Synchronous version for backward compatibility
        public void NormalizeBatch(string[] inputPaths, string[] outputPaths, int targetWidth, int targetHeight, int maxParallel = 2, Action<int, string> progressCallback = null)
        {
            NormalizeBatchAsync(inputPaths, outputPaths, targetWidth, targetHeight, maxParallel, progressCallback).Wait();
        }
    }
} 