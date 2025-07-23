using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System;

namespace BackgroundVideoWinForms
{
    public class VideoConcatenator
    {
        public void Concatenate(List<string> inputFiles, string outputFile, string resolution, Action<string> progressCallback)
        {
            // Validate input files
            var validFiles = new List<string>();
            foreach (var file in inputFiles)
            {
                if (File.Exists(file) && new FileInfo(file).Length > 0)
                {
                    validFiles.Add(file);
                }
                else
                {
                    progressCallback?.Invoke($"Warning: Skipping invalid or empty file: {file}");
                }
            }
            if (validFiles.Count == 0)
            {
                progressCallback?.Invoke("Error: No valid input files for concatenation.");
                return;
            }
            // Log concat list and durations
            string debugLog = Path.Combine(Path.GetTempPath(), $"concat_debug_{Guid.NewGuid()}.txt");
            using (var log = new StreamWriter(debugLog))
            {
                foreach (var file in validFiles)
                {
                    log.WriteLine($"{file} - {GetDuration(file)}s");
                }
            }
            string tempListFile = Path.Combine(Path.GetTempPath(), $"pexels_concat_{Guid.NewGuid()}.txt");
            using (var sw = new StreamWriter(tempListFile))
            {
                foreach (var file in validFiles)
                {
                    sw.WriteLine($"file '{file.Replace("'", "'\\''")}'");
                }
            }
            // Add -vsync 2 and -r 30 for robustness
            string ffmpegArgs = $"-y -f concat -safe 0 -i \"{tempListFile}\" -vf scale={resolution} -c:v libx264 -preset fast -crf 23 -an -vsync 2 -r 30 \"{outputFile}\"";
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
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null && progressCallback != null)
                    {
                        var line = e.Data;
                        var idx = line.IndexOf("time=");
                        if (idx >= 0)
                        {
                            var timePart = line.Substring(idx + 5);
                            var spaceIdx = timePart.IndexOf(' ');
                            if (spaceIdx > 0)
                                timePart = timePart.Substring(0, spaceIdx);
                            progressCallback(timePart);
                        }
                    }
                };
                process.BeginErrorReadLine();
                process.WaitForExit();
            }
            try { File.Delete(tempListFile); } catch { }
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
                    process.WaitForExit(2000);
                    if (double.TryParse(output, out double duration))
                        return duration;
                }
            }
            catch { }
            return 0;
        }
    }
} 