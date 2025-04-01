using DanTheMan827.OnDeviceADB;
using MBF_Launcher.Services;
using System.Diagnostics;

namespace MBF_Launcher
{
    public partial class MainPage : ContentPage
    {
        /// <summary>
        /// The ADB flow object
        /// </summary>
        private static readonly AdbFlow Flow = new AdbFlow();

        private readonly BridgeService Bridge = BridgeService.Instance;

        /// <summary>
        /// Number of times the fish has been tapped
        /// </summary>
        private int fishTaps = 0;

        /// <summary>
        /// If the browser has been launched in the current instance
        /// </summary>
        private bool launchedMbf = false;

        private AdbWrapper.AdbDevice[] _devices = [];
        public AdbWrapper.AdbDevice[] Devices
        {
            get => _devices;
            private set
            {
                _devices = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Constructor for the MainPage
        /// </summary>
        public MainPage()
        {
            InitializeComponent();

            BindingContext = this;
            Flow.OnMessage += this.Flow_OnMessage;
            Bridge.BridgeExited += this.BridgeExited;
        }

        /// <summary>
        /// Removes the event handler when the page is destroyed
        /// </summary>
        ~MainPage()
        {
            //Flow.OnMessage -= this.Flow_OnMessage;
            //Bridge.BridgeExited -= this.BridgeExited;
        }

        /// <summary>
        /// Launches the bridge process
        /// </summary>
        /// <returns></returns>
        public async Task LaunchMbf()
        {
            try
            {
                if (!Bridge.IsRunning)
                {
                    await Bridge.Start(new BridgeService.BridgeStartInfo()
                    {
                        BinaryPath = Path.Combine(SharedData.NativeLibraryDir, "libMbfBridge.so"),
                        AppUrl = AppConfig.Instance.AppUrl,
                        AdbPort = AdbServer.AdbPort
                    });
                }
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() => _ = DisplayAlert(AppResources.ErrorStartingBridge, ex.Message, AppResources.AlertDismiss));
                return;
            }

            var startInfo = BridgeService.Instance.StartupInfo!;
            var address = startInfo.BrowserUrl.ToString();

            if (mbfDevMode.IsChecked)
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

            MainThread.BeginInvokeOnMainThread(() => _ = Navigation.PushAsync(new BrowserPage(address)));
        }

        /// <summary>
        /// Updates the package picker with the list of installed packages
        /// </summary>
        /// <returns></returns>
        private async Task UpdatePackages()
        {
            var output = await AdbWrapper.RunShellCommand(Flow.Devices.First().Name, "pm list packages");

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
                await MainThread.InvokeOnMainThreadAsync(() => packagePicker.ItemsSource = packages);
                if (!packages.Contains(AppConfig.Instance.SelectedGame))
                {
                    AppConfig.Instance.SelectedGame = "com.beatgames.beatsaber";
                }

                await MainThread.InvokeOnMainThreadAsync(() => packagePicker.SelectedIndex = packages.IndexOf(AppConfig.Instance.SelectedGame));
            }
        }

        private enum Layouts
        {
            None,
            Status,
            WiFi,
            WiFiEnabling,
            Authorization,
            Pairing,
            Connect,
            Connected
        }

        /// <summary>
        /// Only shows the given layout and hides the others
        /// </summary>
        /// <param name="layout"></param>
        private async Task ShowOneLayout(Layouts layout)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                statusLayout.IsVisible = statusLayout.IsEnabled = layout == Layouts.Status;
                wifiLayout.IsVisible = wifiLayout.IsEnabled = layout == Layouts.WiFi;
                wifiEnablingLayout.IsVisible = wifiEnablingLayout.IsEnabled = layout == Layouts.WiFiEnabling;
                authorizationLayout.IsVisible = authorizationLayout.IsEnabled = layout == Layouts.Authorization;
                pairingLayout.IsVisible = pairingLayout.IsEnabled = layout == Layouts.Pairing;
                connectLayout.IsVisible = connectLayout.IsEnabled = layout == Layouts.Connect;
                connectedLayout.IsVisible = connectedLayout.IsEnabled = layout == Layouts.Connected;
            });
        }

        /// <summary>
        /// Event handler for the flow messages.
        /// 
        /// This is where the UI is updated based on the messages received from the AdbFlow object.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="rawMessage"></param>
        private void Flow_OnMessage(AdbFlow sender, AdbFlow.FlowMessage rawMessage) => Task.Run(async () =>
        {
            switch (rawMessage)
            {
                case AdbFlow.StatusMessage message:
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        statusLabel.IsVisible = message.Payload != null && message.Payload != "";
                        statusLabel.Text = message.Payload;
                    });
                    break;

                case AdbFlow.AdbPortScanAttempt message:
                    switch (Flow.State)
                    {
                        case AdbFlow.AdbFlowState.EnablingWirelessDebugging:
                            await ShowOneLayout(message.Payload > 0 ? Layouts.WiFiEnabling : Layouts.Status);
                            break;

                        case AdbFlow.AdbFlowState.WaitingForWirelessDebugging:
                            break;
                    }
                    break;

                case AdbFlow.StateChange message:
                    switch (message.Payload)
                    {

                        case AdbFlow.AdbFlowState.Stopped: // The flow has stopped
                        case AdbFlow.AdbFlowState.Disconnected: // The device has disconnected unexpectedly
                            // Restart the flow after popping the navigation back to the root
                            MainThread.BeginInvokeOnMainThread(() => _ = Navigation.PopToRootAsync());
                            await ShowOneLayout(Layouts.Status);
                            _ = Flow.StartFlow();
                            break;

                        case AdbFlow.AdbFlowState.Initializing:
                        case AdbFlow.AdbFlowState.Connecting:
                        case AdbFlow.AdbFlowState.WirelessDebugPairing:
                            // Just show the status layout
                            await ShowOneLayout(Layouts.Status);
                            break;

                        case AdbFlow.AdbFlowState.WaitingForAuthorization:
                            // We're waiting for the user to authorize the connection
                            await ShowOneLayout(Layouts.Authorization);
                            break;

                        case AdbFlow.AdbFlowState.WaitingForWirelessDebugging:
                            // We're waiting for the user to enable wireless debugging
                            await MainThread.InvokeOnMainThreadAsync(() => statusLabel.IsVisible = false);
                            await ShowOneLayout(Layouts.WiFi);
                            break;

                        case AdbFlow.AdbFlowState.EnablingWirelessDebugging:
                            // We're waiting for the user to accept the wireless debugging prompt
                            await ShowOneLayout(Layouts.Status);
                            break;

                        case AdbFlow.AdbFlowState.WaitingForPairingInfo:
                            // We're waiting for the user to enter the pairing info
                            await ShowOneLayout(Layouts.Pairing);
                            break;

                        case AdbFlow.AdbFlowState.WaitingForDebugPort:
                            await ShowOneLayout(Layouts.Connect);
                            break;

                        case AdbFlow.AdbFlowState.Connected:
                            // We're connected to the device
                            await ShowOneLayout(Layouts.Connected);
                            await UpdatePackages();

                            if (launchedMbf == false)
                            {
                                launchedMbf = true;
                                await LaunchMbf();
                            }

                            break;
                    }
                    break;

                case AdbFlow.PairingPortChange message:
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        portEntry.IsEnabled = !message.Payload.HasValue;
                        portEntry.Text = message.Payload?.ToString() ?? "";
                    });
                    break;

                case AdbFlow.DevicesChanged message:
                    MainThread.BeginInvokeOnMainThread(() => devicesLabel.Text = String.Join("\n", message.Payload.Select(d => d.Name)));
                    break;

                case AdbFlow.PairingError message:
                    MainThread.BeginInvokeOnMainThread(() => _ = DisplayAlert(AppResources.Error, message.Payload.Message, AppResources.AlertDismiss));
                    break;

                case AdbFlow.PermissionsError message:
                    MainThread.BeginInvokeOnMainThread(() => _ = DisplayAlert(AppResources.Error, message.Payload.Message, AppResources.AlertDismiss));
                    break;

                case AdbFlow.ConnectionError message:
                    MainThread.BeginInvokeOnMainThread(() => _ = DisplayAlert(AppResources.Error, message.Payload.Message, AppResources.AlertDismiss));
                    break;

                case AdbFlow.ErrorMessage message:
                    MainThread.BeginInvokeOnMainThread(() => _ = DisplayAlert(AppResources.Error, message.Payload.Message, AppResources.AlertDismiss));
                    break;

                case AdbFlow.FlowMessage message:
                    Debug.Assert(false, "Unknown message type was received", message.MessageType.ToString());
                    break;
            }
        });

        #region Event Handlers
        /// <summary>
        /// Called when the bridge process has exited.  This is probably due to an error.
        /// </summary>
        /// <param name="process"></param>
        /// <param name="e"></param>
        private void BridgeExited(Process process, EventArgs e) => _ = Task.Run(async () =>
        {
            await DisplayAlert(AppResources.BridgeProcessTerminated, process.StandardError.ReadToEnd(), AppResources.AlertDismiss);
            await Helpers.RestartApp();
        });

        /// <summary>
        /// Called when the page is loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ContentPage_Loaded(object sender, EventArgs e) => Flow.SendState();

        /// <summary>
        /// Called when the restart adb button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void restartAdbButton_Clicked(object sender, EventArgs e) => _ = Flow.StopFlow();

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
                Flow.ProvideWirelessDebugPairingInfo(pairingCodeEntry.Text.Trim(), port);
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
                Flow.ProvideWirelessDebugPort(port);
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
        private void TcpIpMode_Clicked(object sender, EventArgs e) => _ = Flow.TcpIpMode();

        /// <summary>
        /// Exits the app
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void exitApp_Clicked(object sender, EventArgs e) => Application.Current?.Quit();

        /// <summary>
        /// Enables wireless debugging
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void cycleWifiDebugging_Clicked(object sender, EventArgs e) => _ = AdbWrapper.EnableAdbWiFiAsync(true);

        /// <summary>
        /// Launches the bridge and opens the browser
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void openBrowserButton_Clicked(object sender, EventArgs e) => _ = LaunchMbf();

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
