using Microsoft.Win32;

namespace BackgroundVideoWinForms
{
    public static class RegistryHelper
    {
        private const string REGISTRY_PATH = @"Software\\BackgroundVideoWinForms";
        private const string REGISTRY_APIKEY = "PexelsApiKey";

        public static void SaveApiKey(string apiKey)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(REGISTRY_PATH))
                {
                    key.SetValue(REGISTRY_APIKEY, apiKey);
                }
            }
            catch { }
        }

        public static string LoadApiKey()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REGISTRY_PATH))
                {
                    if (key != null)
                    {
                        var value = key.GetValue(REGISTRY_APIKEY) as string;
                        if (!string.IsNullOrEmpty(value))
                            return value;
                    }
                }
            }
            catch { }
            return string.Empty;
        }
    }
} 