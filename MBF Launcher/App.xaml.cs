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

