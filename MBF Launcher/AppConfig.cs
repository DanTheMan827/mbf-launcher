namespace MBF_Launcher
{
    internal static class AppConfig
    {
        public static string AppUrl
        {
            get => Preferences.Default.Get("AppUrl", "https://dantheman827.github.io/ModsBeforeFriday/");
            set => Preferences.Default.Set("AppUrl", value);
        }
        public static string SelectedGame
        {
            get => Preferences.Default.Get("SelectedGame", "com.beatgames.beatsaber");
            set => Preferences.Default.Set("SelectedGame", value);
        }

        public static bool DevMode
        {
            get => Preferences.Default.Get("DevMode", false);
            set => Preferences.Default.Set("DevMode", value);
        }
    }
}
