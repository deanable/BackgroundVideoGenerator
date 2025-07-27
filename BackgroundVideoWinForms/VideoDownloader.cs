using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Collections.Generic;

namespace BackgroundVideoWinForms
{
    public class VideoDownloader
    {
        private static readonly SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(3, 3); // Limit concurrent downloads
        private static readonly Dictionary<string, SemaphoreSlim> _fileLocks = new Dictionary<string, SemaphoreSlim>();
        private static readonly object _fileLocksLock = new object();

        public async Task<string> DownloadAsync(PexelsVideoClip clip, string targetPath, CancellationToken cancellationToken = default)
        {
            Logger.LogDebug($"Downloading {clip.Url} to {Path.GetFileName(targetPath)}");
            
            // Acquire download semaphore to limit concurrent downloads
            await _downloadSemaphore.WaitAsync(cancellationToken);
            
            // Get or create file-specific lock
            SemaphoreSlim fileLock;
            lock (_fileLocksLock)
            {
                if (!_fileLocks.ContainsKey(targetPath))
                {
                    _fileLocks[targetPath] = new SemaphoreSlim(1, 1);
                }
                fileLock = _fileLocks[targetPath];
            }
            
            // Acquire file-specific lock
            await fileLock.WaitAsync(cancellationToken);
            
            try
            {
                return await DownloadWithRetryAsync(clip, targetPath, cancellationToken);
            }
            finally
            {
                // Release locks
                fileLock.Release();
                _downloadSemaphore.Release();
                
                // Clean up file lock if no longer needed
                lock (_fileLocksLock)
                {
                    if (_fileLocks.ContainsKey(targetPath))
                    {
                        _fileLocks.Remove(targetPath);
                    }
                }
            }
        }

        private async Task<string> DownloadWithRetryAsync(PexelsVideoClip clip, string targetPath, CancellationToken cancellationToken, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await PerformDownloadAsync(clip, targetPath, cancellationToken);
                }
                catch (IOException ex) when (ex.Message.Contains("being used by another process") && attempt < maxRetries)
                {
                    Logger.LogWarning($"File access conflict on attempt {attempt} for {Path.GetFileName(targetPath)}: {ex.Message}");
                    await Task.Delay(1000 * attempt, cancellationToken); // Exponential backoff
                    continue;
                }
                catch (HttpRequestException ex) when (attempt < maxRetries)
                {
                    Logger.LogWarning($"HTTP error on attempt {attempt} for {Path.GetFileName(targetPath)}: {ex.Message}");
                    await Task.Delay(2000 * attempt, cancellationToken); // Exponential backoff
                    continue;
                }
                catch (TaskCanceledException ex) when (attempt < maxRetries)
                {
                    Logger.LogWarning($"Timeout on attempt {attempt} for {Path.GetFileName(targetPath)}: {ex.Message}");
                    await Task.Delay(3000 * attempt, cancellationToken); // Exponential backoff
                    continue;
                }
            }
            
            // If we get here, all retries failed
            throw new Exception($"Failed to download {Path.GetFileName(targetPath)} after {maxRetries} attempts");
        }

        private async Task<string> PerformDownloadAsync(PexelsVideoClip clip, string targetPath, CancellationToken cancellationToken)
        {
            // Ensure the target directory exists
            string targetDir = Path.GetDirectoryName(targetPath);
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            
            // Generate unique filename to avoid conflicts
            string uniquePath = GetUniqueFilePathAsync(targetPath);
            
            // Download to temporary file first, then move to final location
            string tempPath = uniquePath + ".tmp";
            
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(15); // Increased timeout for large files
                    using (var response = await client.GetAsync(clip.Url, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        
                        // Use FileShare.Read to allow other processes to read while we write
                        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            await response.Content.CopyToAsync(fs, cancellationToken);
                        }
                    }
                }
                
                // Verify the downloaded file
                var tempFileInfo = new FileInfo(tempPath);
                if (tempFileInfo.Length == 0)
                {
                    throw new Exception("Downloaded file is empty");
                }
                
                // Move to final location atomically
                if (File.Exists(uniquePath))
                {
                    File.Delete(uniquePath);
                }
                File.Move(tempPath, uniquePath);
                
                var fileInfo = new FileInfo(uniquePath);
                Logger.LogFileOperation("Downloaded", uniquePath, fileInfo.Length);
                
                return uniquePath;
            }
            catch
            {
                // Clean up temp file on any error
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                throw;
            }
        }

        private string GetUniqueFilePathAsync(string originalPath)
        {
            if (!File.Exists(originalPath))
            {
                return originalPath;
            }
            
            // Try to find a unique filename
            string directory = Path.GetDirectoryName(originalPath);
            string fileName = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);
            
            for (int i = 1; i <= 100; i++)
            {
                string uniquePath = Path.Combine(directory, $"{fileName}_{i}{extension}");
                if (!File.Exists(uniquePath))
                {
                    Logger.LogInfo($"Using unique filename: {Path.GetFileName(uniquePath)}");
                    return uniquePath;
                }
            }
            
            // If we can't find a unique name, use timestamp
            string timestampPath = Path.Combine(directory, $"{fileName}_{DateTime.Now:yyyyMMddHHmmss}{extension}");
            Logger.LogInfo($"Using timestamped filename: {Path.GetFileName(timestampPath)}");
            return timestampPath;
        }
    }
} 