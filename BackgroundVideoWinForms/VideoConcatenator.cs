using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace BackgroundVideoWinForms
{
    public class VideoConcatenator
    {
        public void Concatenate(List<string> inputFiles, string outputFile, string resolution, System.Action<string> progressCallback)
        {
            string tempListFile = Path.Combine(Path.GetTempPath(), $"pexels_concat_{System.Guid.NewGuid()}.txt");
            using (var sw = new StreamWriter(tempListFile))
            {
                foreach (var file in inputFiles)
                {
                    sw.WriteLine($"file '{file.Replace("'", "'\\''")}'");
                }
            }
            string ffmpegArgs = $"-y -f concat -safe 0 -i \"{tempListFile}\" -vf scale={resolution} -c:v libx264 -preset fast -crf 23 -an \"{outputFile}\"";
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
                            // Extract time=00:00:12.34
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
    }
} 