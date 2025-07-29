using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;

namespace BackgroundVideoWinForms
{
    public static class FFmpegPathManager
    {
        private static string _ffmpegPath;
        private static string _ffprobePath;
        private static bool _initialized = false;

        // Common FFmpeg installation paths
        private static readonly string[] CommonFFmpegPaths = {
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg-2025-07-23-git-829680f96a-full_build\bin\ffmpeg.exe",
            @"C:\Program Files (x86)\ffmpeg-2025-07-23-git-829680f96a-full_build\bin\ffmpeg.exe"
        };

        private static readonly string[] CommonFFprobePaths = {
            @"C:\Program Files\ffmpeg\bin\ffprobe.exe",
            @"C:\Program Files (x86)\ffmpeg\bin\ffprobe.exe",
            @"C:\ffmpeg\bin\ffprobe.exe",
            @"C:\Program Files\ffmpeg-2025-07-23-git-829680f96a-full_build\bin\ffprobe.exe",
            @"C:\Program Files (x86)\ffmpeg-2025-07-23-git-829680f96a-full_build\bin\ffprobe.exe"
        };

        public static string FFmpegPath
        {
            get
            {
                Initialize();
                return _ffmpegPath;
            }
        }

        public static string FFprobePath
        {
            get
            {
                Initialize();
                return _ffprobePath;
            }
        }

        public static bool IsFFmpegAvailable => !string.IsNullOrEmpty(FFmpegPath);
        public static bool IsFFprobeAvailable => !string.IsNullOrEmpty(FFprobePath);

        private static void Initialize()
        {
            if (_initialized) return;

            // Try to find FFmpeg in PATH first
            _ffmpegPath = FindExecutableInPath("ffmpeg");
            _ffprobePath = FindExecutableInPath("ffprobe");

            // If not found in PATH, try common installation directories
            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                _ffmpegPath = FindFFmpegInCommonPaths();
            }

            if (string.IsNullOrEmpty(_ffprobePath))
            {
                _ffprobePath = FindFFprobeInCommonPaths();
            }

            // Log the results
            if (!string.IsNullOrEmpty(_ffmpegPath))
            {
                Logger.LogInfo($"FFmpeg found at: {_ffmpegPath}");
            }
            else
            {
                Logger.LogError("FFmpeg not found in PATH or common installation directories");
            }

            if (!string.IsNullOrEmpty(_ffprobePath))
            {
                Logger.LogInfo($"FFprobe found at: {_ffprobePath}");
            }
            else
            {
                Logger.LogError("FFprobe not found in PATH or common installation directories");
            }

            _initialized = true;
        }

        private static string FindExecutableInPath(string executableName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = executableName,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        process.WaitForExit(3000);
                        if (process.ExitCode == 0)
                        {
                            return executableName;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"Failed to find {executableName} in PATH: {ex.Message}");
            }

            return null;
        }

        private static string FindFFmpegInCommonPaths()
        {
            foreach (var path in CommonFFmpegPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }

        private static string FindFFprobeInCommonPaths()
        {
            foreach (var path in CommonFFprobePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }

        public static void SetCustomFFmpegPath(string ffmpegPath, string ffprobePath = null)
        {
            if (File.Exists(ffmpegPath))
            {
                _ffmpegPath = ffmpegPath;
                Logger.LogInfo($"Custom FFmpeg path set: {ffmpegPath}");
            }
            else
            {
                Logger.LogError($"Custom FFmpeg path does not exist: {ffmpegPath}");
            }

            if (!string.IsNullOrEmpty(ffprobePath))
            {
                if (File.Exists(ffprobePath))
                {
                    _ffprobePath = ffprobePath;
                    Logger.LogInfo($"Custom FFprobe path set: {ffprobePath}");
                }
                else
                {
                    Logger.LogError($"Custom FFprobe path does not exist: {ffprobePath}");
                }
            }
            else if (!string.IsNullOrEmpty(_ffmpegPath))
            {
                // Try to find ffprobe in the same directory as ffmpeg
                var ffmpegDir = Path.GetDirectoryName(_ffmpegPath);
                var ffprobeInSameDir = Path.Combine(ffmpegDir, "ffprobe.exe");
                if (File.Exists(ffprobeInSameDir))
                {
                    _ffprobePath = ffprobeInSameDir;
                    Logger.LogInfo($"FFprobe found in same directory: {ffprobeInSameDir}");
                }
            }
        }

        public static bool ValidateFFmpegInstallation()
        {
            if (!IsFFmpegAvailable)
            {
                Logger.LogError("FFmpeg is not available");
                return false;
            }

            if (!IsFFprobeAvailable)
            {
                Logger.LogError("FFprobe is not available");
                return false;
            }

            // Test FFmpeg functionality
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit(5000);
                        if (process.ExitCode == 0 && output.Contains("ffmpeg version"))
                        {
                            Logger.LogInfo("FFmpeg installation validated successfully");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "FFmpeg validation failed");
            }

            Logger.LogError("FFmpeg installation validation failed");
            return false;
        }
    }
} 