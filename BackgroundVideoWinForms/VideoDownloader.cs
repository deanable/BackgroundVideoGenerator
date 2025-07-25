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
            Logger.Log($"VideoDownloader: Downloading {clip.Url} to {targetPath}");
            try
            {
                using (var client = new HttpClient())
                using (var response = await client.GetAsync(clip.Url))
                using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }
                Logger.Log($"VideoDownloader: Downloaded {targetPath} ({new FileInfo(targetPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"VideoDownloader.DownloadAsync {clip.Url}");
            }
            return targetPath;
        }
    }
} 