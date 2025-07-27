using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System;

namespace BackgroundVideoWinForms
{
    public class VideoDownloader
    {
        public async Task<string> DownloadAsync(PexelsVideoClip clip, string targetPath)
        {
            Logger.LogDebug($"Downloading {clip.Url} to {Path.GetFileName(targetPath)}");
            try
            {
                using (var client = new HttpClient())
                using (var response = await client.GetAsync(clip.Url))
                using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }
                
                var fileInfo = new FileInfo(targetPath);
                Logger.LogFileOperation("Downloaded", targetPath, fileInfo.Length);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"VideoDownloader.DownloadAsync {Path.GetFileName(targetPath)}");
            }
            return targetPath;
        }
    }
} 