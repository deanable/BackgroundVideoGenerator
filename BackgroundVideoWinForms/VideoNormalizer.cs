using System.Diagnostics;

namespace BackgroundVideoWinForms
{
    public class VideoNormalizer
    {
        public (int, int) ProbeDimensions(string filePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadLine();
                    process.WaitForExit(2000);
                    if (!string.IsNullOrEmpty(output))
                    {
                        var parts = output.Split('x');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                            return (w, h);
                    }
                }
            }
            catch { }
            return (0, 0);
        }

        public void Normalize(string inputPath, string outputPath, int targetWidth, int targetHeight)
        {
            string ffmpegArgs = $"-y -i \"{inputPath}\" -vf scale=w={targetWidth}:h={targetHeight}:force_original_aspect_ratio=decrease,pad={targetWidth}:{targetHeight}:(ow-iw)/2:(oh-ih)/2 -c:v libx264 -crf 23 -preset fast -an \"{outputPath}\"";
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
        }
    }
} 