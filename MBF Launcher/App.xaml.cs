using DanTheMan827.OnDeviceADB;
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
    }
}
