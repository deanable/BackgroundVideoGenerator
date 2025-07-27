using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System;
using System.Threading;

namespace BackgroundVideoWinForms
{
    public class VideoDownloader
    {
        public async Task<string> DownloadAsync(PexelsVideoClip clip, string targetPath, CancellationToken cancellationToken = default)
        {
            Logger.LogDebug($"Downloading {clip.Url} to {Path.GetFileName(targetPath)}");
            try
            {
                // Ensure the target directory exists
                string targetDir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                
                // Delete the target file if it already exists to avoid conflicts
                if (File.Exists(targetPath))
                {
                    try
                    {
                        File.Delete(targetPath);
                    }
                    catch (IOException ex)
                    {
                        Logger.LogWarning($"Could not delete existing file {Path.GetFileName(targetPath)}: {ex.Message}");
                        // Try with a different filename
                        string fileName = Path.GetFileNameWithoutExtension(targetPath);
                        string extension = Path.GetExtension(targetPath);
                        string directory = Path.GetDirectoryName(targetPath);
                        targetPath = Path.Combine(directory, $"{fileName}_{DateTime.Now:HHmmss}{extension}");
                        Logger.LogInfo($"Using alternative filename: {Path.GetFileName(targetPath)}");
                    }
                }
                
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10); // 10 minute timeout for large files
                    using (var response = await client.GetAsync(clip.Url, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await response.Content.CopyToAsync(fs, cancellationToken);
                        }
                    }
                }
                
                var fileInfo = new FileInfo(targetPath);
                Logger.LogFileOperation("Downloaded", targetPath, fileInfo.Length);
            }
            catch (OperationCanceledException)
            {
                Logger.LogInfo($"Download cancelled for {Path.GetFileName(targetPath)}");
                // Clean up partial file if it exists
                if (File.Exists(targetPath))
                {
                    try
                    {
                        File.Delete(targetPath);
                        Logger.LogInfo($"Cleaned up partial file after cancellation: {Path.GetFileName(targetPath)}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to clean up partial file after cancellation: {ex.Message}");
                    }
                }
                throw; // Re-throw to propagate cancellation
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"VideoDownloader.DownloadAsync {Path.GetFileName(targetPath)}");
                // Clean up partial file if it exists
                if (File.Exists(targetPath))
                {
                    try
                    {
                        File.Delete(targetPath);
                    }
                    catch { /* Ignore cleanup errors */ }
                }
            }
            return targetPath;
        }
    }
} 