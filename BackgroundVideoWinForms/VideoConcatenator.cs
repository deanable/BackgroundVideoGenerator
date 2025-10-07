using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BackgroundVideoWinForms
{
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
            
            var tempDir = Path.Combine(Path.GetTempPath(), $"pexels_concat_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var tempListFile = Path.Combine(tempDir, "concat_list.txt");
                using (var writer = new StreamWriter(tempListFile))
                {
                    foreach (var file in inputFiles)
                    {
                        writer.WriteLine($"file '{file.Replace("'", "'\'\''")}'");
                    }
                }

                string ffmpegArgs = $"-y -f concat -safe 0 -i \"{tempListFile}\" -c copy \"{outputFile}\"";

                Logger.LogInfo($"VideoConcatenator: Using ffmpeg command: {ffmpegArgs}");

                var success = RunFFmpegConcatenation(ffmpegArgs, outputFile, progress);

                if (success)
                {
                    Logger.LogInfo($"VideoConcatenator: SUCCESS - Output: {Path.GetFileName(outputFile)}");
                }

                return success;
            }
            finally
            {
                CleanupTempDirectory(tempDir);
            }
        }

        private bool RunFFmpegConcatenation(string ffmpegArgs, string outputFile, IProgress<string> progress)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = psi })
            {
                process.ErrorDataReceived += (sender, e) => 
                {
                    if (e.Data != null)
                    {
                        progress?.Report(e.Data);
                        Logger.LogDebug($"FFMPEG: {e.Data}");
                    }
                };

                process.Start();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Logger.LogError($"VideoConcatenator: FFmpeg failed with exit code {process.ExitCode}");
                    return false;
                }
            }

            return File.Exists(outputFile);
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
                Logger.LogWarning($"VideoConcatenator: Error cleaning up temporary directory {tempDir}: {{ex.Message}}");
            }
        }
    }
}
