using Android.App;
using Android.Content.PM;

namespace MBF_Launcher
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ScreenOrientation = ScreenOrientation.Landscape, LaunchMode = LaunchMode.SingleInstance, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
    public class MainActivity : MauiAppCompatActivity
    {
    }
}
