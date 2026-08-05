using DanTheMan827.OnDeviceADB;
using MBF_Launcher.Services;
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
            Page? page = null;
            try
            {
                var result = await UpdateService.CheckForUpdateAsync();
                if (result is not { } update)
                    return;

                page = this.Windows.FirstOrDefault()?.Page;
                if (page == null)
                    return;

                bool install = await MainThread.InvokeOnMainThreadAsync(
                    () => page.DisplayAlert(
                        "Update Available",
                        $"Version {update.Release.TagName.TrimStart('v')} is available. Install it now?",
                        "Install",
                        "Later"));

                if (!install)
                    return;

                string apkPath;
                try
                {
                    apkPath = await UpdateService.DownloadApkAsync(update.Apk.DownloadUrl);
                }
                catch (Exception ex)
                {
                    await MainThread.InvokeOnMainThreadAsync(
                        () => page.DisplayAlert(
                            "Download Failed",
                            $"Could not download the update: {ex.Message}",
                            "OK"));
                    return;
                }

                await InstallApkAsync(apkPath, page);
            }
            catch
            {
                // Silently ignore update check failures.
            }
        }

        private static async Task InstallApkAsync(string localPath, Page page)
        {
#if ANDROID
            var pm = Platform.AppContext.PackageManager;
            if (pm == null)
                return;

            if (!pm.CanRequestPackageInstalls())
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

            var file = new Java.IO.File(localPath);
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
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
        internal static bool IsPrimaryUser
        {
            get
            {
                return true; // Disabled for now, as MBF itself doesn't yet support running in the app context.
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
}

