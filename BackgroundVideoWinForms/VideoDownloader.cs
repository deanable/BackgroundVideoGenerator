using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace BackgroundVideoWinForms
{
    public class VideoDownloader
    {
        public async Task<string> DownloadAsync(PexelsVideoClip clip, string targetPath)
        {
            using (var client = new HttpClient())
            using (var response = await client.GetAsync(clip.Url))
            using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }
            return targetPath;
        }
    }
} 