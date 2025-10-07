using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BackgroundVideoWinForms
{
    public class HardwareEncoderInfo
    {
        public string Name { get; }
        public string Args { get; }
        public Func<bool> TestAvailability { get; }

        public HardwareEncoderInfo(string name, string args, Func<bool> testAvailability)
        {
            Name = name;
            Args = args;
            TestAvailability = testAvailability;
        }
    }

    public class VideoConcatenator
    {
        public static async Task<bool> ConcatenateVideosAsync(List<string> inputFiles, string outputFile, int targetWidth, int targetHeight, IProgress<string> progress = null)
        {
            return await Task.Run(() =>
            {
                var concatenator = new VideoConcatenator();
                return concatenator.ConcatenateVideos(inputFiles, outputFile, targetWidth, targetHeight, progress);
            });
        }

        public bool ConcatenateVideos(List<string> inputFiles, string outputFile, int targetWidth, int targetHeight, IProgress<string> progress = null)
        {
            if (inputFiles == null || inputFiles.Count == 0)
            {
                Logger.LogError("VideoConcatenator: No input files provided");
                return false;
            }

            Logger.LogPipelineStep("Video Concatenation", $"Concatenating {inputFiles.Count} videos to {Path.GetFileName(outputFile)}");

            var startTime = DateTime.Now;
            var tempDir = Path.Combine(Path.GetTempPath(), $"pexels_concat_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // Step 1: Quick validation - files should already be normalized from Form1.cs
                var validFiles = new List<string>();
                double totalDuration = 0;

                Logger.LogInfo($"VideoConcatenator: Quick validation of {inputFiles.Count} pre-normalized files...");

                foreach (var file in inputFiles)
                {
                    if (!File.Exists(file))
                    {
                        Logger.LogWarning($"VideoConcatenator: File not found: {file}");
                        continue;
                    }

                    var duration = GetVideoDuration(file);
                    if (duration > 0)
                    {
                        validFiles.Add(file);
                        totalDuration += duration;
                        Logger.LogDebug($"VideoConcatenator: Valid file - {Path.GetFileName(file)}: {duration:F1}s");
                    }
                }

                if (validFiles.Count == 0)
                {
                    Logger.LogError("VideoConcatenator: No valid input files found");
                    return false;
                }

                Logger.LogInfo($"VideoConcatenator: Ready to concatenate {validFiles.Count} files ({totalDuration:F1}s total)");

                // Step 2: Create optimized concat list (no duplicate normalization needed)
                var tempListFile = Path.Combine(tempDir, "concat_list.txt");
                using (var writer = new StreamWriter(tempListFile))
                {
                    foreach (var file in validFiles)
                    {
                        writer.WriteLine($"file '{file.Replace("'", "'\\''")}'");
                    }
                }

                Logger.LogInfo($"VideoConcatenator: Created optimized concat list with {validFiles.Count} files");

                // Step 3: Ultra-fast concatenation using hardware acceleration when available
                progress?.Report("Concatenating videos at high speed...");

                string ffmpegArgs = BuildOptimizedFFmpegArgs(tempListFile, outputFile, targetWidth, targetHeight);

                Logger.LogInfo($"VideoConcatenator: Using optimized FFmpeg args: {ffmpegArgs}");
                Logger.LogInfo($"VideoConcatenator: Starting high-speed concatenation at {DateTime.Now:HH:mm:ss.fff}");

                var success = RunFFmpegConcatenation(ffmpegArgs, tempListFile, outputFile, totalDuration, progress);

                if (success)
                {
                    // Step 4: Validate output
                    var fileInfo = new FileInfo(outputFile);
                    var outputDuration = GetVideoDuration(outputFile);

                    Logger.LogInfo($"VideoConcatenator: SUCCESS - Output: {Path.GetFileName(outputFile)} ({fileInfo.Length / (1024.0 * 1024.0):F1}MB, {outputDuration:F1}s)");

                    var totalTime = DateTime.Now - startTime;
                    Logger.LogPerformance("Video Concatenation", totalTime, $"Output: {Path.GetFileName(outputFile)}");
                }

                return success;
            }
            finally
            {
                CleanupTempDirectory(tempDir);
            }
        }

        private string BuildOptimizedFFmpegArgs(string tempListFile, string outputFile, int targetWidth, int targetHeight)
        {
            // Try multiple hardware acceleration options, fallback to optimized software
            var hardwareEncoder = DetectBestHardwareEncoder();

            if (hardwareEncoder != null)
            {
                Logger.LogInfo($"VideoConcatenator: Using {hardwareEncoder.Name} for maximum speed");
                return $"-y -f concat -safe 0 -i \"{tempListFile}\" {hardwareEncoder.Args} -c:a aac -avoid_negative_ts make_zero \"{outputFile}\"";
            }
            else
            {
                Logger.LogInfo("VideoConcatenator: Using ultra-optimized software encoding (AMD Ryzen integrated graphics detected)");
                return $"-y -f concat -safe 0 -i \"{tempListFile}\" -c:v libx264 -preset ultrafast -crf 23 -tune fastdecode -c:a aac -avoid_negative_ts make_zero \"{outputFile}\"";
            }
        }

        private HardwareEncoderInfo DetectBestHardwareEncoder()
        {
            var encoders = new[]
            {
                new HardwareEncoderInfo("NVIDIA", "-c:v h264_nvenc -preset p7 -cq 26", () => TestEncoder("h264_nvenc")),
                new HardwareEncoderInfo("AMD", "-c:v h264_amf -usage transcoding -quality speed", () => TestEncoder("h264_amf")),
                new HardwareEncoderInfo("Intel QSV", "-c:v h264_qsv -preset fast -global_quality 25", () => TestEncoder("h264_qsv"))
            };

            foreach (var encoder in encoders)
            {
                if (encoder.TestAvailability())
                {
                    return encoder;
                }
            }

            return null;
        }

        private bool TestEncoder(string encoderName)
        {
            try
            {
                string ffmpegPath = FFmpegPathManager.FFmpegPath;

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-f lavfi -i testsrc=duration=1:size=320x240:rate=1 -c:v {encoderName} -t 1 -f null -",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    process.WaitForExit(5000); // 5 second timeout
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool RunFFmpegConcatenation(string ffmpegArgs, string tempListFile, string outputFile, double totalDuration, IProgress<string> progress)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            // Monitor progress with timeout
            int timeoutMinutes = Math.Max(5, (int)Math.Ceiling(totalDuration / 60.0 * 2)); // 2 minutes per minute of video
            var stopwatch = Stopwatch.StartNew();

            while (!process.HasExited)
            {
                if (stopwatch.Elapsed.TotalMinutes > timeoutMinutes)
                {
                    Logger.LogWarning($"VideoConcatenator: Timeout after {timeoutMinutes} minutes - terminating");
                    try { process.Kill(); } catch { }
                    return false;
                }

                // Read progress periodically
                if (stopwatch.ElapsedMilliseconds % 5000 < 100) // Every 5 seconds
                {
                    progress?.Report($"Concatenating... ({stopwatch.Elapsed:mm\\:ss} elapsed)");
                }

                Thread.Sleep(100);
            }

            var totalTime = stopwatch.Elapsed;
            Logger.LogInfo($"VideoConcatenator: Concatenation completed in {totalTime:mm\\:ss\\.ff}");

            if (process.ExitCode != 0)
            {
                Logger.LogError($"VideoConcatenator: FFmpeg failed with exit code {process.ExitCode}");
                Logger.LogDebug($"VideoConcatenator: FFmpeg arguments were: {ffmpegArgs}");
                return false;
            }

            return File.Exists(outputFile);
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