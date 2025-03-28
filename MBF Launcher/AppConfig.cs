namespace MBF_Launcher
{
    internal class AppConfig
    {
        public static AppConfig Instance = new AppConfig();
        public string AppUrl = "https://dantheman827.github.io/ModsBeforeFriday/";
        public string SelectedGame = "com.beatgames.beatsaber";

        private AppConfig() { }
    }
}
