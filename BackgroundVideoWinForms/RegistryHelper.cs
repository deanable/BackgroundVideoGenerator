using Microsoft.Win32;

namespace BackgroundVideoWinForms
{
    public static class RegistryHelper
    {
        private const string REGISTRY_PATH = @"Software\BackgroundVideoWinForms";
        
        // Registry key names
        private const string REGISTRY_APIKEY = "PexelsApiKey";
        private const string REGISTRY_SEARCH_TERM = "SearchTerm";
        private const string REGISTRY_DURATION = "Duration";
        private const string REGISTRY_RESOLUTION = "Resolution";
        private const string REGISTRY_ASPECT_RATIO = "AspectRatio";
        private const string REGISTRY_WINDOW_WIDTH = "WindowWidth";
        private const string REGISTRY_WINDOW_HEIGHT = "WindowHeight";
        private const string REGISTRY_WINDOW_X = "WindowX";
        private const string REGISTRY_WINDOW_Y = "WindowY";
        private const string REGISTRY_FFMPEG_PATH = "FFmpegPath";
        private const string REGISTRY_FFPROBE_PATH = "FFprobePath";

        // Default values
        private const string DEFAULT_SEARCH_TERM = "city";
        private const int DEFAULT_DURATION = 2;
        private const string DEFAULT_RESOLUTION = "1080p";
        private const string DEFAULT_ASPECT_RATIO = "Horizontal";
        private const int DEFAULT_WINDOW_WIDTH = 600;
        private const int DEFAULT_WINDOW_HEIGHT = 400;

        #region API Key
        public static void SaveApiKey(string apiKey)
        {
            SaveStringValue(REGISTRY_APIKEY, apiKey);
        }

        public static string LoadApiKey()
        {
            return LoadStringValue(REGISTRY_APIKEY, string.Empty);
        }
        #endregion

        #region Search Term
        public static void SaveSearchTerm(string searchTerm)
        {
            SaveStringValue(REGISTRY_SEARCH_TERM, searchTerm);
        }

        public static string LoadSearchTerm()
        {
            return LoadStringValue(REGISTRY_SEARCH_TERM, DEFAULT_SEARCH_TERM);
        }
        #endregion

        #region Duration
        public static void SaveDuration(int duration)
        {
            SaveIntValue(REGISTRY_DURATION, duration);
        }

        public static int LoadDuration()
        {
            return LoadIntValue(REGISTRY_DURATION, DEFAULT_DURATION);
        }
        #endregion

        #region Resolution
        public static void SaveResolution(string resolution)
        {
            SaveStringValue(REGISTRY_RESOLUTION, resolution);
        }

        public static string LoadResolution()
        {
            return LoadStringValue(REGISTRY_RESOLUTION, DEFAULT_RESOLUTION);
        }
        #endregion

        #region Aspect Ratio
        public static void SaveAspectRatio(string aspectRatio)
        {
            SaveStringValue(REGISTRY_ASPECT_RATIO, aspectRatio);
        }

        public static string LoadAspectRatio()
        {
            return LoadStringValue(REGISTRY_ASPECT_RATIO, DEFAULT_ASPECT_RATIO);
        }
        #endregion

        #region Window Position and Size
        public static void SaveWindowPosition(int x, int y, int width, int height)
        {
            SaveIntValue(REGISTRY_WINDOW_X, x);
            SaveIntValue(REGISTRY_WINDOW_Y, y);
            SaveIntValue(REGISTRY_WINDOW_WIDTH, width);
            SaveIntValue(REGISTRY_WINDOW_HEIGHT, height);
        }

        public static (int x, int y, int width, int height) LoadWindowPosition()
        {
            int x = LoadIntValue(REGISTRY_WINDOW_X, -1);
            int y = LoadIntValue(REGISTRY_WINDOW_Y, -1);
            int width = LoadIntValue(REGISTRY_WINDOW_WIDTH, DEFAULT_WINDOW_WIDTH);
            int height = LoadIntValue(REGISTRY_WINDOW_HEIGHT, DEFAULT_WINDOW_HEIGHT);
            return (x, y, width, height);
        }
        #endregion

        #region FFmpeg Paths
        public static void SaveFFmpegPath(string ffmpegPath)
        {
            SaveStringValue(REGISTRY_FFMPEG_PATH, ffmpegPath);
        }

        public static string LoadFFmpegPath()
        {
            return LoadStringValue(REGISTRY_FFMPEG_PATH, string.Empty);
        }

        public static void SaveFFprobePath(string ffprobePath)
        {
            SaveStringValue(REGISTRY_FFPROBE_PATH, ffprobePath);
        }

        public static string LoadFFprobePath()
        {
            return LoadStringValue(REGISTRY_FFPROBE_PATH, string.Empty);
        }
        #endregion

        #region Generic Save/Load Methods
        private static void SaveStringValue(string valueName, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(REGISTRY_PATH))
                {
                    if (key != null)
                    {
                        key.SetValue(valueName, value ?? string.Empty);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogException(ex, $"RegistryHelper.SaveStringValue failed for {valueName}");
            }
        }

        private static void SaveIntValue(string valueName, int value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(REGISTRY_PATH))
                {
                    if (key != null)
                    {
                        key.SetValue(valueName, value);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogException(ex, $"RegistryHelper.SaveIntValue failed for {valueName}");
            }
        }

        private static string LoadStringValue(string valueName, string defaultValue)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_PATH))
                {
                    if (key != null)
                    {
                        var value = key.GetValue(valueName) as string;
                        if (!string.IsNullOrEmpty(value))
                            return value;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogException(ex, $"RegistryHelper.LoadStringValue failed for {valueName}");
            }
            return defaultValue;
        }

        private static int LoadIntValue(string valueName, int defaultValue)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_PATH))
                {
                    if (key != null)
                    {
                        var value = key.GetValue(valueName);
                        if (value != null && int.TryParse(value.ToString(), out int result))
                            return result;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogException(ex, $"RegistryHelper.LoadIntValue failed for {valueName}");
            }
            return defaultValue;
        }
        #endregion

        #region Settings Management
        public static void SaveAllSettings(string apiKey, string searchTerm, int duration, string resolution, string aspectRatio)
        {
            SaveApiKey(apiKey);
            SaveSearchTerm(searchTerm);
            SaveDuration(duration);
            SaveResolution(resolution);
            SaveAspectRatio(aspectRatio);
        }

        public static void ClearAllSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_PATH, true))
                {
                    if (key != null)
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(REGISTRY_PATH);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogException(ex, "RegistryHelper.ClearAllSettings failed");
            }
        }
        #endregion
    }
} 