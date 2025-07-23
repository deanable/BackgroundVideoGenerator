using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace BackgroundVideoWinForms
{
    public class VideoConcatenator
    {
        public void Concatenate(List<string> inputFiles, string outputFile, string resolution)
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
                process.WaitForExit();
            }
            try { File.Delete(tempListFile); } catch { }
        }
    }
} 