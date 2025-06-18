namespace MBF_Launcher
{
    internal static class AppConfig
    {
        public static string AppUrl
        {
            get => Preferences.Default.Get(nameof(AppUrl), "https://dantheman827.github.io/ModsBeforeFriday/");
            set => Preferences.Default.Set(nameof(AppUrl), value);
        }
        public static string SelectedGame
        {
            get => Preferences.Default.Get(nameof(SelectedGame), "com.beatgames.beatsaber");
            set => Preferences.Default.Set(nameof(SelectedGame), value);
        }

        public static bool DevMode
        {
            get => Preferences.Default.Get(nameof(DevMode), false);
            set => Preferences.Default.Set(nameof(DevMode), value);
        }

        public static bool ShowDevOptions
        {
            get => Preferences.Default.Get(nameof(ShowDevOptions), false);
            set => Preferences.Default.Set(nameof(ShowDevOptions), value);
        }
    }
}
