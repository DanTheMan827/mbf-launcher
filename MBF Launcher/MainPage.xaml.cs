using Android.Content;
using Android.Net;
using DanTheMan827.OnDeviceADB;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Tmds.MDns;

namespace MBF_Launcher
{

    public partial class MainPage : ContentPage
    {
        internal bool initialized = false;
        internal bool newWindow = true;

        ServiceBrowser serviceBrowser = new ServiceBrowser();

        AdbWrapper.AdbDevice[] devices = [];
        AdbWrapper.AdbDevice[] authorizedDevices => devices.Where(device => device.Authorized).ToArray();
        AdbWrapper.AdbDevice[] unauthorizedDevices => devices.Where(device => !device.Authorized).ToArray();
        int[] adbPorts = [];

        int bridgePort = 25037;
        int fishClicks = 0;

        public static class PageStrings
        {
            public const string DisconnectingDevices = "Disconnecting Devices";
            public const string EnablingWirelessDebugging = "Enabling Wireless Debugging";
            public const string ConnectingOnPort = "Connecting on Port {0}";
            public const string GettingDevices = "Getting Devices";
            public const string SettingTcpIpMode = "Setting TCP IP Mode";
            public const string AlertDismiss = "Okay";
            public const string Error = "Error";
            public const string BridgeTerminated = "Bridge Process Terminated";
            public const string StartingAdb = "Starting ADB";
            public static string BridgeAddress = "http://127.0.0.1:25037/?bridge=";
            public static string PackageID => Android.App.Application.Context?.PackageName ?? throw new Exception("Unknown package name");
            public static string AppRestartCommand => $"am force-stop {PackageID}; monkey -p {PackageID} -c android.intent.category.LAUNCHER 1";
            public const string ErrorStartingBridge = "Error Starting Bridge";
            public const string GrantingPermissions = "Granting Permissions";
        }

        #region Helpers
        /// <summary>
        /// Gets the device's local IP address.
        /// </summary>
        /// <returns>The IP address as a string.</returns>
        public static string[] GetLocalIPAddresses()
        {
            var addresses = new List<string>();

            var context = Android.App.Application.Context;
            // Obtain the ConnectivityManager instance
            var connectivityManager = (ConnectivityManager)context.GetSystemService(Context.ConnectivityService)!;

            // Get the currently active network
            var activeNetwork = connectivityManager.ActiveNetwork;
            if (activeNetwork == null)
            {
                return addresses.ToArray();
            }

            // Retrieve link properties for the active network
            var linkProperties = connectivityManager.GetLinkProperties(activeNetwork);
            if (linkProperties == null)
            {
                return addresses.ToArray();
            }

            // Iterate over the link addresses to find an IPv4 address
            foreach (var linkAddress in linkProperties.LinkAddresses)
            {
                var address = linkAddress.Address?.HostAddress;
                if (address != null)
                {
                    addresses.Add(address);
                }
            }

            return addresses.ToArray();
        }
        /// <summary>
        /// Retrieves the value of an environment variable from the current process.
        /// </summary>
        /// <param name="variable">The name of the environment variable.</param>
        /// <returns>The value of the environment variable specified by <paramref name="variable"/>,
        /// or <see langword="null"/> if the environment variable is not found.</returns>
        private static string? TryGetEnvironmentVariable(string variable)
        {
            try
            {
                return Environment.GetEnvironmentVariable(variable);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// checks for used ports and retrieves the first free port
        /// </summary>
        /// <returns>the free port or 0 if it did not find a free port</returns>
        public static UInt16 GetAvailablePort(UInt16 startingPort = 25036, UInt16 maxPort = UInt16.MaxValue)
        {
            int port = startingPort - 1;

            while (++port < maxPort)
            {
                try
                {
                    using (var listener = new TcpListener(IPAddress.Loopback, port))
                    {
                        listener.Start();
                        return (ushort)port;
                    }
                }
                catch (Exception)
                {

                }
            }

            throw new Exception("Free port could not be found");
        }

        /// <summary>
        /// Checks if we have our needed permissions by trying to set the Wi-Fi debugging state
        /// </summary>
        /// <returns></returns>
        private bool HasPermission()
        {
            try
            {
                AdbWrapper.AdbWifiState = AdbWrapper.AdbWifiState;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Restarts the adb server
        /// </summary>
        /// <returns></returns>
        private static async Task RestartAdb()
        {
            await AdbWrapper.DisconnectAsync();
            await AdbWrapper.KillServerAsync();
            await AdbWrapper.StartServerAsync();
        }

        private async Task<UInt16> GetAdbPort()
        {
            var process = Process.Start(new ProcessStartInfo(Path.Combine(Android.App.Application.Context.ApplicationInfo?.NativeLibraryDir!, "libAdbFinder.so"))
            {
                RedirectStandardOutput = true
            });
            await process.WaitForExitAsync();
            return UInt16.Parse(process.StandardOutput.ReadToEnd().Trim().Split("\n").Where(l => l.Trim() != "").FirstOrDefault("0"));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task EnableWifiDebug()
        {
            AdbWrapper.AdbWifiState = AdbWifiState.Enabled;
            UInt16 port = 0;

            while (port == 0)
            {
                port = await GetAdbPort();

                if (port > 0)
                {
                    break;
                }
                else
                {
                    var label = statusLabel.Text;
                    State = PageState.WirelessDebugging;
                    statusLabel.Text = label;
                    Thread.Sleep(500);
                }
            }

            State = PageState.Initializing;
            statusLabel.Text = String.Format(PageStrings.ConnectingOnPort, port);
            var device = await AdbWrapper.ConnectAsync("127.0.0.1", port);

            statusLabel.Text = PageStrings.GettingDevices;
            devices = await AdbWrapper.GetDevicesAsync();
        }

        /// <summary>
        /// Opens the settings to the developer options
        /// </summary>
        private static void OpenSettings(bool developerSettings)
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

        /// <summary>
        /// Launches the bridge process
        /// </summary>
        /// <returns></returns>
        public async Task LaunchMbf()
        {
            try
            {
                if (MauiProgram.bridgeProcess == null || MauiProgram.bridgeProcess.HasExited)
                {
                    MauiProgram.bridgeProcess = Process.Start(Path.Combine(Android.App.Application.Context.ApplicationInfo?.NativeLibraryDir!, "libMbfBridge.so"), $"--proxy --port {bridgePort} --adb-port {AdbServer.AdbPort}");
                    MauiProgram.bridgeProcess.Exited += this.BridgeProcess_Exited;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert(PageStrings.ErrorStartingBridge, ex.Message, PageStrings.AlertDismiss);
            }

            await Navigation.PushAsync(new BrowserPage(PageStrings.BridgeAddress));
            return;

            try
            {
                var browserResult = await AdbWrapper.RunShellCommand(devices[0].Name, "am", "start", "-a", "android.intent.action.VIEW", "-d", PageStrings.BridgeAddress);

                if (browserResult.ExitCode != 0)
                {
                    throw new Exception(browserResult.Output);
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert(PageStrings.Error, ex.Message, PageStrings.AlertDismiss);
            }

        }
        #endregion

        #region Page State
        enum PageState
        {
            Initializing, Authorization, WirelessDebugging, Pairing, ConnectDevice
        }

        private PageState State
        {
            set
            {
                statusLabel.Text = "";
                statusLabel.IsVisible = false;
                initializingLayout.IsVisible = false;
                initializingLayout.IsEnabled = false;

                authorizationLayout.IsVisible = false;
                authorizationLayout.IsEnabled = false;

                wifiLayout.IsVisible = false;
                wifiLayout.IsEnabled = false;

                pairingLayout.IsVisible = false;
                pairingLayout.IsEnabled = false;
                portEntry.Text = "";
                pairingCodeEntry.Text = "";

                connectLayout.IsVisible = false;
                connectLayout.IsEnabled = false;
                debugPortEntry.Text = "";


                switch (value)
                {
                    case PageState.Initializing:
                        initializingLayout.IsVisible = true;
                        initializingLayout.IsEnabled = true;
                        statusLabel.IsVisible = true;
                        break;

                    case PageState.Authorization:
                        statusLabel.Text = "Please allow the connection request";
                        statusLabel.IsVisible = true;
                        authorizationLayout.IsVisible = true;
                        authorizationLayout.IsEnabled = true;
                        break;

                    case PageState.WirelessDebugging:
                        statusLabel.IsVisible = true;
                        wifiLayout.IsVisible = true;
                        wifiLayout.IsEnabled = true;
                        break;

                    case PageState.Pairing:
                        pairingLayout.IsVisible = true;
                        pairingLayout.IsEnabled = true;
                        break;

                    case PageState.ConnectDevice:
                        connectLayout.IsVisible = true;
                        connectLayout.IsEnabled = true;
                        break;
                }
            }
        }
        #endregion

        public MainPage()
        {
            InitializeComponent();
            var adbPort = GetAvailablePort();

            AdbServer.AdbPort = adbPort;
            bridgePort = GetAvailablePort((ushort)(adbPort + 1));

            PageStrings.BridgeAddress = $"http://127.0.0.1:{bridgePort}/ModsBeforeFriday/?bridge=";

            serviceBrowser.ServiceAdded += this.ServiceBrowser_ServiceAdded;
            ;
            serviceBrowser.ServiceRemoved += this.ServiceBrowser_ServiceRemoved;
            ;

            serviceBrowser.StartBrowse("_adb-tls-pairing._tcp");
        }

        private static bool checkPairingService(ServiceAnnouncement service)
        {
            var localAddresses = GetLocalIPAddresses();
            foreach (var ip in service.Addresses)
            {
                if (localAddresses.Contains(ip.ToString()))
                {
                    return true;
                }
            }

            return false;
        }

        private void ServiceBrowser_ServiceRemoved(object? sender, ServiceAnnouncementEventArgs e)
        {
            if (checkPairingService(e.Announcement))
            {

                portEntry.Text = "";
                portLabel.IsVisible = true;
                portEntry.IsVisible = true;
            }
        }

        private void ServiceBrowser_ServiceAdded(object? sender, ServiceAnnouncementEventArgs e)
        {
            if (checkPairingService(e.Announcement))
            {
                portEntry.Text = e.Announcement.Port.ToString();
                portLabel.IsVisible = false;
                portEntry.IsVisible = false;

            }
        }

        /// <summary>
        /// Initializes ADB and sets the state for the page.
        /// </summary>
        /// <returns></returns>
        private async Task InitializeAdb()
        {
            if (!AdbWrapper.IsServerRunning)
            {
                statusLabel.Text = PageStrings.StartingAdb;
                await AdbWrapper.DisconnectAsync();
            }

            statusLabel.Text = PageStrings.GettingDevices;
            devices = await AdbWrapper.GetDevicesAsync();

            if (authorizedDevices.Length == 0 && unauthorizedDevices.Length == 0 && HasPermission())
            {
                try
                {
                    statusLabel.Text = PageStrings.EnablingWirelessDebugging;
                    await EnableWifiDebug();
                }
                catch (Exception ex)
                {
                }
                State = PageState.Initializing;
            }

            while (authorizedDevices.Length == 0 && unauthorizedDevices.Length > 0)
            {
                State = PageState.Authorization;
                devices = await AdbWrapper.GetDevicesAsync();
            }

            if (devices.Length == 0)
            {
                State = PageState.Pairing;
                return;
            }

            await DeviceConnected();
        }

        /// <summary>
        /// Run after a valid device has been detected
        /// </summary>
        /// <returns></returns>
        private async Task DeviceConnected()
        {
            State = PageState.Initializing;
            if (!HasPermission())
            {
                statusLabel.Text = PageStrings.GrantingPermissions;
                try
                {
                    var package = Android.App.Application.Context.PackageName;
                    await AdbWrapper.RunAdbCommandAsync("shell", $"sh -c '{AdbWrapper.GrantPermissionsCommand}; {PageStrings.AppRestartCommand}' > /dev/null 2>&1 < /dev/null &");
                }
                catch (Exception ex)
                {
                    await DisplayAlert(PageStrings.Error, ex.Message, PageStrings.AlertDismiss);
                }
            }

            statusLabel.Text = "Devices:\n";
            statusLabel.Text += String.Join("\n", devices.Select(device => $"  {device.Name}"));
            statusLabel.Text += "\n\nLeave this app running to use ModsBeforeFriday";

            openBrowserButton.IsVisible = true;

            initialized = true;

            await LaunchMbf();
        }

        /// <summary>
        /// Run after the pair button is clicked and the ports are validated
        /// </summary>
        /// <param name="pairingPort"></param>
        /// <param name="pairingCode"></param>
        /// <returns></returns>
        private async Task PairDevice(UInt16 pairingPort, int pairingCode)
        {
            pairingLayout.IsEnabled = false;

            try
            {
                var pairResult = await AdbWrapper.RunAdbCommandAsync("pair", $"127.0.0.1:{pairingPort}", pairingCode.ToString());
                if (pairResult.Output.StartsWith("Failed:"))
                {
                    throw new Exception(pairResult.Output.Substring(8));
                }
                else
                {
                    devices = await AdbWrapper.GetDevicesAsync();

                    if (authorizedDevices.Length == 0)
                    {
                        var port = await GetAdbPort();
                        if (port > 0)
                        {
                            State = PageState.Initializing;
                            statusLabel.Text = String.Format(PageStrings.ConnectingOnPort, port);
                            var device = await AdbWrapper.ConnectAsync("127.0.0.1", port);

                            statusLabel.Text = PageStrings.GettingDevices;
                            devices = await AdbWrapper.GetDevicesAsync();

                            if (devices.Length == 0)
                            {
                                await AdbWrapper.DisconnectAsync();
                                State = PageState.ConnectDevice;
                            }
                            else
                            {
                                _ = DeviceConnected();
                            }
                        }
                        else
                        {
                            State = PageState.ConnectDevice;
                        }
                    }
                    else
                    {
                        _ = DeviceConnected();
                    }

                }
            }
            catch (Exception)
            {
                await DisplayAlert("Error", "Unable to pair with the provided information.", PageStrings.AlertDismiss);
            }
            finally
            {
                pairingLayout.IsEnabled = true;
            }
        }

        /// <summary>
        /// Run after the connect button is clicked and the ports are validated
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        private async Task ConnectDevice(int port)
        {
            connectLayout.IsEnabled = false;

            try
            {
                var connectResult = await AdbWrapper.ConnectAsync("127.0.0.1", port);
                State = PageState.Initializing;
                //statusLabel.Text = "Setting TCP IP Mode";
                //await AdbWrapper.TcpIpMode(5555);

                //statusLabel.Text = "Disconnecting Devices";
                //await AdbWrapper.DisconnectAsync();

                statusLabel.Text = PageStrings.GettingDevices;
                devices = await AdbWrapper.GetDevicesAsync();



                if (authorizedDevices.Length > 0)
                {
                    State = PageState.Initializing;
                    await DeviceConnected();

                    return;
                }

                await InitializeAdb();
            }
            catch (Exception ex)
            {
                await DisplayAlert(PageStrings.Error, ex.Message, PageStrings.AlertDismiss);
            }
            finally
            {
                connectLayout.IsEnabled = true;
            }
        }

        #region Event Handlers
        /// <summary>
        /// Called when the bridge process has exited.  This is probably due to an error.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BridgeProcess_Exited(object? sender, EventArgs e)
        {
            Task.Run(async () =>
            {
                await DisplayAlert(PageStrings.BridgeTerminated, MauiProgram.bridgeProcess?.StandardError.ReadToEnd(), PageStrings.AlertDismiss);
                await AdbWrapper.RunAdbCommandAsync("shell", $"sh -c '{PageStrings.AppRestartCommand}' > /dev/null 2>&1 < /dev/null &");
            });
        }

        /// <summary>
        /// Called when the page is loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ContentPage_Loaded(object sender, EventArgs e)
        {
            State = PageState.Initializing;

            if (newWindow && initialized)
            {
                _ = LaunchMbf();
            }

            if (!initialized)
            {
                _ = InitializeAdb();
            }

            newWindow = false;
        }

        /// <summary>
        /// Called when the restart adb button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void restartAdbButton_Clicked(object sender, EventArgs e) => _ = RestartAdb();

        /// <summary>
        /// Called when the launch settings button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void launchSettingsButton_Clicked(object sender, EventArgs e) => OpenSettings(false);

        /// <summary>
        /// Called when the launch settings button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void launchDeveloperSettingsButton_Clicked(object sender, EventArgs e) => OpenSettings(true);

        /// <summary>
        /// Called when the pair button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void pairingConfirmButton_Clicked(object sender, EventArgs e)
        {
            pairingConfirmButton.Focus();
            UInt16 port;
            int pairingCode;
            if (UInt16.TryParse(portEntry.Text, out port) && int.TryParse(pairingCodeEntry.Text, out pairingCode))
            {
                _ = PairDevice(port, pairingCode);
            }
            else
            {
                DisplayAlert(PageStrings.Error, "Please check the format of the port and pairing code, only numbers should be entered.", PageStrings.AlertDismiss);
            }
        }

        /// <summary>
        /// Called when the connect button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void postPairingConnectButton_Clicked(object sender, EventArgs e)
        {
            UInt16 port;
            if (UInt16.TryParse(debugPortEntry.Text, out port))
            {
                _ = ConnectDevice(port);
            }
            else
            {
                DisplayAlert(PageStrings.Error, "Please check the format of the port, only numbers should be entered.", PageStrings.AlertDismiss);
            }
        }

        private void Fish_Clicked(object sender, EventArgs e)
        {
            if (fishClicks < 5)
            {
                fishClicks++;
            }

            if (fishClicks >= 5)
            {
                developerLayout.IsVisible = true;
                return;
            }
        }
        #endregion

        private void TcpIpMode_Clicked(object sender, EventArgs e)
        {
            _ = Task.Run(async () =>
            {
                await AdbWrapper.TcpIpMode(5555);
                await AdbWrapper.DisconnectAsync();
                await Task.Delay(3000);
                await AdbWrapper.ConnectAsync("127.0.0.1");
            });
        }

        private void exitApp_Clicked(object sender, EventArgs e)
        {
            Application.Current?.Quit();
        }

        private void cycleWifiDebugging_Clicked(object sender, EventArgs e)
        {
            _ = AdbWrapper.EnableAdbWiFiAsync(true);
        }

        private void openBrowserButton_Clicked(object sender, EventArgs e)
        {
            _ = LaunchMbf();
        }
    }

}
