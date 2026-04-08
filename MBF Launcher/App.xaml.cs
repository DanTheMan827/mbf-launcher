using DanTheMan827.OnDeviceADB;
using MBF_Launcher.Services;
using MBF_Launcher.WebView;
using Window = Microsoft.Maui.Controls.Window;

namespace MBF_Launcher
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // Assigns ADB server port to a random available port
            AdbServer.AdbPort = Helpers.GetAvailablePort();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // Non-primary Android users cannot use the real ADB stack; route them
            // directly to the browser backed by the in-process virtual ADB server.
            if (!IsPrimaryUser())
            {
                return new Window(new NavigationPage(
                    new BrowserPage(AppConfig.AppUrl, new VirtualAdbServer())));
            }

            return new Window(new NavigationPage(new MainPage()));
        }

        protected override void OnStart()
        {
            base.OnStart();
            _ = CheckForUpdatesAsync();
        }

        // ── Update mechanism ──────────────────────────────────────────────────

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var result = await UpdateService.CheckForUpdateAsync();
                if (result is not { } update)
                    return;

                var page = this.Windows.FirstOrDefault()?.Page;
                if (page == null)
                    return;

                bool install = await MainThread.InvokeOnMainThreadAsync(
                    () => page.DisplayAlert(
                        "Update Available",
                        $"Version {update.Release.TagName} is available. Install it now?",
                        "Install",
                        "Later"));

                if (!install)
                    return;

                var apkPath = await UpdateService.DownloadApkAsync(update.Apk.DownloadUrl);
                await InstallApkAsync(apkPath, page);
            }
            catch
            {
                // Silently ignore update check / download failures.
            }
        }

        private static async Task InstallApkAsync(string localPath, Page page)
        {
#if ANDROID
            var pm = Platform.AppContext.PackageManager;
            if (pm != null && !pm.CanRequestPackageInstalls())
            {
                await MainThread.InvokeOnMainThreadAsync(
                    () => page.DisplayAlert(
                        "Permission Required",
                        "Please enable 'Install unknown apps' for MBF Launcher in Settings, then try again.",
                        "OK"));

                var settingsIntent = new Android.Content.Intent(
                    Android.Provider.Settings.ActionManageUnknownAppSources,
                    Android.Net.Uri.Parse("package:" + Platform.AppContext.PackageName))
                    .AddFlags(Android.Content.ActivityFlags.NewTask);
                Platform.AppContext.StartActivity(settingsIntent);
                return;
            }

            var file   = new Java.IO.File(localPath);
            var uri    = AndroidX.Core.Content.FileProvider.GetUriForFile(
                             Platform.AppContext,
                             Platform.AppContext.PackageName + ".fileprovider",
                             file);
            var intent = new Android.Content.Intent(Android.Content.Intent.ActionInstallPackage)
                .SetData(uri)
                .AddFlags(Android.Content.ActivityFlags.NewTask |
                          Android.Content.ActivityFlags.GrantReadUriPermission);
            Platform.AppContext.StartActivity(intent);
#else
            await Task.CompletedTask;
#endif
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns <c>true</c> when the app is running as the primary Android user
        /// (user ID 0).  Secondary users lack access to the real ADB stack.
        /// </summary>
        private static bool IsPrimaryUser()
        {
#if ANDROID
            // Android assigns UIDs in the range [userId * 100000, (userId+1) * 100000).
            // The primary user is always userId == 0.
            return Android.OS.Process.MyUid() / 100000 == 0;
#else
            return true;
#endif
        }
    }
}

