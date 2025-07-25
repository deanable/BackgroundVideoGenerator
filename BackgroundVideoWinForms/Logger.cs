using System;
using System.IO;

namespace BackgroundVideoWinForms
{
    public static class Logger
    {
        private static readonly string logFilePath = Path.Combine(Path.GetTempPath(), $"BackgroundVideoWinForms_{DateTime.Now:yyyyMMddHHmmss}.log");
        public static string LogFilePath => logFilePath;

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        public static void LogException(Exception ex, string context = null)
        {
            try
            {
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR{(context != null ? $" ({context})" : "")}: {ex}\n");
            }
            catch { }
        }
    }
} 