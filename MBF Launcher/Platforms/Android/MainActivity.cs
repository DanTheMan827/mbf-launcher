using Android.App;
using Android.Content.PM;
using Android.OS;

namespace MBF_Launcher
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ScreenOrientation = ScreenOrientation.Landscape, LaunchMode = LaunchMode.SingleInstance, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true);
        }
    }
}
