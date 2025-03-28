using Android.Content;

internal static partial class Helpers
{
    /// <summary>
    /// Opens the settings app
    /// </summary>
    /// <param name="developerSettings">Open to the developer settings</param>
    public static void OpenSettings(bool developerSettings)
    {
        var context = Android.App.Application.Context;
        var intent = new Intent();
        // Specify the package and class name for the internal Development Settings activity
        if (developerSettings)
        {
            intent.SetComponent(new ComponentName(
                "com.android.settings",
                "com.android.settings.Settings$DevelopmentSettingsDashboardActivity"));
        }
        else
        {
            intent.SetComponent(new ComponentName(
                "com.android.settings",
                "com.android.settings.Settings"));
        }
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}