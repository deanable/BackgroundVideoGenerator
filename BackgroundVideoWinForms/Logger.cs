using System;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace BackgroundVideoWinForms
{
    public static class Logger
    {
        private static readonly string logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string logFilePath;
        private static readonly object lockObject = new object();
        private static readonly Stopwatch sessionStopwatch = Stopwatch.StartNew();

        static Logger()
        {
            // Create logs directory if it doesn't exist
            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }

            // Create timestamped log file for this session
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            logFilePath = Path.Combine(logsDirectory, $"log-{timestamp}.log");
            
            // Log session start
            Log("=== SESSION START ===");
            Log($"Application: BackgroundVideoWinForms");
            Log($"Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            Log($"OS: {Environment.OSVersion}");
            Log($"Framework: {Environment.Version}");
            Log($"Working Directory: {Environment.CurrentDirectory}");
            Log($"Log File: {logFilePath}");
            Log($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            Log("===================");
        }

        public static string LogFilePath => logFilePath;

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            WriteLog(message, level);
        }

        public static void LogInfo(string message)
        {
            WriteLog(message, LogLevel.Info);
        }

        public static void LogWarning(string message)
        {
            WriteLog(message, LogLevel.Warning);
        }

        public static void LogError(string message)
        {
            WriteLog(message, LogLevel.Error);
        }

        public static void LogDebug(string message)
        {
            WriteLog(message, LogLevel.Debug);
        }

        public static void LogException(Exception ex, string context = null)
        {
            string message = $"EXCEPTION{(context != null ? $" ({context})" : "")}: {ex.Message}";
            if (ex.InnerException != null)
            {
                message += $"\nInner Exception: {ex.InnerException.Message}";
            }
            message += $"\nStack Trace: {ex.StackTrace}";
            WriteLog(message, LogLevel.Error);
        }

        public static void LogPipelineStep(string step, string details = null)
        {
            string message = $"PIPELINE STEP: {step}";
            if (!string.IsNullOrEmpty(details))
            {
                message += $" - {details}";
            }
            WriteLog(message, LogLevel.Info);
        }

        public static void LogPerformance(string operation, TimeSpan duration, string details = null)
        {
            string message = $"PERFORMANCE: {operation} completed in {duration.TotalMilliseconds:F0}ms";
            if (!string.IsNullOrEmpty(details))
            {
                message += $" - {details}";
            }
            WriteLog(message, LogLevel.Info);
        }

        public static void LogFileOperation(string operation, string filePath, long fileSize = 0)
        {
            string message = $"FILE OPERATION: {operation} - {Path.GetFileName(filePath)}";
            if (fileSize > 0)
            {
                message += $" ({FormatFileSize(fileSize)})";
            }
            WriteLog(message, LogLevel.Debug);
        }

        public static void LogApiCall(string endpoint, string parameters = null, bool success = true)
        {
            string message = $"API CALL: {endpoint} - {(success ? "SUCCESS" : "FAILED")}";
            if (!string.IsNullOrEmpty(parameters))
            {
                message += $" - {parameters}";
            }
            WriteLog(message, LogLevel.Info);
        }

        public static void LogFfmpegCommand(string command, string inputFile = null, string outputFile = null)
        {
            string message = $"FFMPEG: {command}";
            if (!string.IsNullOrEmpty(inputFile))
            {
                message += $" | Input: {Path.GetFileName(inputFile)}";
            }
            if (!string.IsNullOrEmpty(outputFile))
            {
                message += $" | Output: {Path.GetFileName(outputFile)}";
            }
            WriteLog(message, LogLevel.Debug);
        }

        public static void LogProgress(string operation, int current, int total, string details = null)
        {
            double percentage = total > 0 ? (double)current / total * 100 : 0;
            string message = $"PROGRESS: {operation} - {current}/{total} ({percentage:F1}%)";
            if (!string.IsNullOrEmpty(details))
            {
                message += $" - {details}";
            }
            WriteLog(message, LogLevel.Info);
        }

        public static void LogMemoryUsage()
        {
            var process = Process.GetCurrentProcess();
            long memoryMB = process.WorkingSet64 / 1024 / 1024;
            WriteLog($"MEMORY USAGE: {memoryMB} MB", LogLevel.Debug);
        }

        public static void LogSystemInfo()
        {
            WriteLog("=== SYSTEM INFO ===", LogLevel.Info);
            WriteLog($"Processor Count: {Environment.ProcessorCount}", LogLevel.Info);
            WriteLog($"Available Memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB", LogLevel.Info);
            WriteLog($"Is 64-bit Process: {Environment.Is64BitProcess}", LogLevel.Info);
            WriteLog($"Is 64-bit OS: {Environment.Is64BitOperatingSystem}", LogLevel.Info);
            WriteLog("==================", LogLevel.Info);
        }

        private static void WriteLog(string message, LogLevel level)
        {
            try
            {
                lock (lockObject)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string sessionTime = sessionStopwatch.Elapsed.ToString(@"hh\:mm\:ss\.fff");
                    string levelStr = level.ToString().ToUpper().PadRight(7);
                    string logEntry = $"[{timestamp}] [{sessionTime}] [{levelStr}] {message}";
                    
                    File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
                    
                    // Also write to debug output for development
                    System.Diagnostics.Debug.WriteLine(logEntry);
                }
            }
            catch (Exception ex)
            {
                // Fallback to console if file logging fails
                Console.WriteLine($"[LOGGER ERROR] Failed to write log: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[LOGGER ERROR] Failed to write log: {ex.Message}");
            }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }
    }
} 