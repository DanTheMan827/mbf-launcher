using DanTheMan827.OnDeviceADB;
using MBF_Launcher.Services;
using System.Diagnostics;
using Tmds.MDns;

namespace MBF_Launcher
{
    public partial class MainPage : ContentPage
    {
        #region Variables
        /// <summary>
        /// If ADB has been initialized
        /// </summary>
        internal bool initialized = false;

        /// <summary>
        /// Service browser instance for discovering ADB pairing services
        /// </summary>
        ServiceBrowser serviceBrowser = new ServiceBrowser();

        AdbWrapper.AdbDevice[] _devices = [];
        /// <summary>
        /// List of devices connected to the ADB server
        /// </summary>
        AdbWrapper.AdbDevice[] devices
        {
            get => _devices;
            set
            {
                _devices = value;
                authorizedDevices = _devices.Where(device => device.Authorized).ToArray();
                unauthorizedDevices = _devices.Where(device => !device.Authorized).ToArray();

                OnPropertyChanged(nameof(devices));
                OnPropertyChanged(nameof(authorizedDevices));
                OnPropertyChanged(nameof(unauthorizedDevices));
            }
        }

        /// <summary>
        /// List of devices connected to the ADB server that are authorized
        /// </summary>
        AdbWrapper.AdbDevice[] authorizedDevices { get; set; }

        /// <summary>
        /// List of devices connected to the ADB server that are unauthorized
        /// </summary>
        AdbWrapper.AdbDevice[] unauthorizedDevices { get; set; }

        /// <summary>
        /// Number of times the fish has been tapped
        /// </summary>
        int fishTaps = 0;
        #endregion

        /// <summary>
        /// Constructor for the MainPage
        /// </summary>
        /// <param name="bridge"></param>
        public MainPage()
        {
            InitializeComponent();

            // Configures the service browser to look for ADB pairing services
            serviceBrowser.ServiceAdded += this.ServiceBrowser_ServiceAdded;
            serviceBrowser.ServiceRemoved += this.ServiceBrowser_ServiceRemoved;

            // Starts the service browser
            serviceBrowser.StartBrowse("_adb-tls-pairing._tcp");
        }

        #region Helpers
        /// <summary>
        /// Enables wireless debugging and connects to the device
        /// </summary>
        /// <returns></returns>
        private async Task EnableWifiDebug()
        {
            AdbWrapper.AdbWifiState = AdbWifiState.Enabled;
            UInt16 port = 0;

            while (port == 0)
            {
                port = await Helpers.GetAdbPort();

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
            statusLabel.Text = string.Format(AppResources.ConnectingOnPort, port);
            var device = await AdbWrapper.ConnectAsync("127.0.0.1", port);

            statusLabel.Text = AppResources.GettingDevices;
            devices = await AdbWrapper.GetDevicesAsync();
        }

        /// <summary>
        /// Launches the bridge process
        /// </summary>
        /// <returns></returns>
        public async Task LaunchMbf()
        {
            try
            {
                if (!BridgeService.Instance.IsRunning)
                {
                    await BridgeService.Instance.Start(new Services.BridgeService.BridgeStartInfo()
                    {
                        BinaryPath = Path.Combine(SharedData.NativeLibraryDir, "libMbfBridge.so"),
                        AppUrl = AppConfig.Instance.AppUrl,
                        AdbPort = AdbServer.AdbPort
                    });
                    BridgeService.Instance.BridgeExited += this.BridgeExited;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert(AppResources.ErrorStartingBridge, ex.Message, AppResources.AlertDismiss);
                return;
            }

            var startInfo = BridgeService.Instance.StartupInfo!;
            var address = startInfo.BrowserUrl.ToString();

            if (developerLayout.IsVisible)
            {
                address += "&dev=true";
            }

            if (packagePicker.SelectedIndex != -1)
            {
                var package = packagePicker.SelectedItem as String;
                if (package != null)
                {
                    address += $"&game_id={package}";
                }
            }

            await Navigation.PushAsync(new BrowserPage(address));
        }
        #endregion

        #region Page State
        /// <summary>
        /// The various states the page can be in
        /// </summary>
        enum PageState
        {
            /// <summary>
            /// The page is initializing
            /// </summary>
            Initializing,

            /// <summary>
            /// Device is waiting for ADB authorization
            /// </summary>
            Authorization,

            /// <summary>
            /// Waiting for wireless debugging to be enabled
            /// </summary>
            WirelessDebugging,

            /// <summary>
            /// In wireless debug pairing
            /// </summary>
            Pairing,

            /// <summary>
            /// Prompting for wireless debug port
            /// </summary>
            ConnectDevice
        }

        /// <summary>
        /// Controls when certain elements are visible on the page
        /// </summary>
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

        #region Methods
        /// <summary>
        /// Updates the package picker with the list of installed packages
        /// </summary>
        /// <returns></returns>
        private async Task UpdatePackages()
        {
            var output = await AdbWrapper.RunShellCommand(devices[0].Name, "pm list packages");

            if (output.ExitCode == 0)
            {
                var packages = output.Output
                    .Split("\n")
                    .Where(l => l.StartsWith("package:"))
                    .Select(l => l.Substring(8))
                    .Where(l =>
                        !l.StartsWith("com.android.") &&
                        !l.StartsWith("com.oculus.") &&
                        !l.StartsWith("com.meta.") &&
                        !l.StartsWith("com.facebook.") &&
                        !l.StartsWith("com.environment.") &&
                        !l.StartsWith("android.") &&
                        l != "android" &&
                        l != "horizonos.platform" &&
                        l != "oculus.platform" &&
                        l != "com.qualcomm.timeservice"
                     )
                    .Order()
                    .ToList();
                packagePicker.ItemsSource = packages;
                if (!packages.Contains(AppConfig.Instance.SelectedGame))
                {
                    AppConfig.Instance.SelectedGame = "com.beatgames.beatsaber";
                }

                packagePicker.SelectedIndex = packages.IndexOf(AppConfig.Instance.SelectedGame);
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
                statusLabel.Text = AppResources.StartingAdb;
                await AdbWrapper.DisconnectAsync();
            }

            statusLabel.Text = AppResources.GettingDevices;
            devices = await AdbWrapper.GetDevicesAsync();

            if (authorizedDevices.Length == 0 && unauthorizedDevices.Length == 0 && Helpers.HasPermission())
            {
                try
                {
                    statusLabel.Text = AppResources.EnablingWirelessDebugging;
                    await EnableWifiDebug();
                }
                catch (Exception ex) { }
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
            if (!Helpers.HasPermission())
            {
                statusLabel.Text = AppResources.GrantingPermissions;
                try
                {
                    var package = Android.App.Application.Context.PackageName;
                    await AdbWrapper.RunAdbCommandAsync("shell", $"sh -c '{AdbWrapper.GrantPermissionsCommand}; {SharedData.AppRestartCommand}' > /dev/null 2>&1 < /dev/null &");
                }
                catch (Exception ex)
                {
                    await DisplayAlert(AppResources.Error, ex.Message, AppResources.AlertDismiss);
                }
            }

            statusLabel.Text = "Devices:\n";
            statusLabel.Text += String.Join("\n", devices.Select(device => $"  {device.Name}"));
            statusLabel.Text += "\n\nLeave this app running to use ModsBeforeFriday";

            openBrowserButton.IsVisible = true;

            initialized = true;

            await UpdatePackages();
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
                        var port = await Helpers.GetAdbPort();
                        if (port > 0)
                        {
                            State = PageState.Initializing;
                            statusLabel.Text = string.Format(AppResources.ConnectingOnPort, port);
                            var device = await AdbWrapper.ConnectAsync("127.0.0.1", port);

                            statusLabel.Text = AppResources.GettingDevices;
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
                await DisplayAlert("Error", "Unable to pair with the provided information.", AppResources.AlertDismiss);
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

                statusLabel.Text = AppResources.GettingDevices;
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
                await DisplayAlert(AppResources.Error, ex.Message, AppResources.AlertDismiss);
            }
            finally
            {
                connectLayout.IsEnabled = true;
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Called when the pairing service is removed.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ServiceBrowser_ServiceRemoved(object? sender, ServiceAnnouncementEventArgs e)
        {
            if (Helpers.checkPairingService(e.Announcement))
            {
                portEntry.Text = "";
                portEntry.IsEnabled = true;
            }
        }

        /// <summary>
        /// Called when the pairing service is added.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ServiceBrowser_ServiceAdded(object? sender, ServiceAnnouncementEventArgs e)
        {
            if (Helpers.checkPairingService(e.Announcement))
            {
                portEntry.Text = e.Announcement.Port.ToString();
                portEntry.IsEnabled = false;

            }
        }

        /// <summary>
        /// Called when the bridge process has exited.  This is probably due to an error.
        /// </summary>
        /// <param name="process"></param>
        /// <param name="e"></param>
        private void BridgeExited(Process process, EventArgs e)
        {
            Task.Run(async () =>
            {
                await DisplayAlert(AppResources.BridgeProcessTerminated, process.StandardError.ReadToEnd(), AppResources.AlertDismiss);
                await AdbWrapper.RunAdbCommandAsync("shell", $"sh -c '{SharedData.AppRestartCommand}' > /dev/null 2>&1 < /dev/null &");
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

            if (!initialized)
            {
                _ = InitializeAdb();
            }
        }

        /// <summary>
        /// Called when the restart adb button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void restartAdbButton_Clicked(object sender, EventArgs e) => _ = Helpers.RestartAdb();

        /// <summary>
        /// Called when the launch settings button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void launchSettingsButton_Clicked(object sender, EventArgs e) => Helpers.OpenSettings(false);

        /// <summary>
        /// Called when the launch settings button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void launchDeveloperSettingsButton_Clicked(object sender, EventArgs e) => Helpers.OpenSettings(true);

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
                DisplayAlert(AppResources.Error, "Please check the format of the port and pairing code, only numbers should be entered.", AppResources.AlertDismiss);
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
                DisplayAlert(AppResources.Error, "Please check the format of the port, only numbers should be entered.", AppResources.AlertDismiss);
            }
        }

        /// <summary>
        /// Switches the device to TCP/IP mode
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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

        /// <summary>
        /// Exits the app
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void exitApp_Clicked(object sender, EventArgs e)
        {
            Application.Current?.Quit();
        }

        /// <summary>
        /// Enables wireless debugging
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void cycleWifiDebugging_Clicked(object sender, EventArgs e)
        {
            _ = AdbWrapper.EnableAdbWiFiAsync(true);
        }

        /// <summary>
        /// Launches the bridge and opens the browser
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void openBrowserButton_Clicked(object sender, EventArgs e)
        {
            _ = LaunchMbf();
        }

        /// <summary>
        /// Called when the fish is tapped
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Fish_Tapped(object sender, TappedEventArgs e)
        {
            if (fishTaps < 5)
            {
                fishTaps++;
            }

            if (fishTaps >= 5)
            {
                developerLayout.IsVisible = true;
                return;
            }
        }
        #endregion
    }

}
