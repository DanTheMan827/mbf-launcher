using DanTheMan827.OnDeviceADB;
using MBF_Launcher.Services;
using MBF_Launcher.WebView;
using System.Diagnostics;

namespace MBF_Launcher
{
    public partial class MainPage : ContentPage
    {
        /// <summary>
        /// The ADB flow object
        /// </summary>
        private static readonly AdbFlow Flow = new AdbFlow();

        /// <summary>
        /// Number of times the fish has been tapped
        /// </summary>
        private int fishTaps = 0;

        /// <summary>
        /// If the browser has been launched in the current instance
        /// </summary>
        private bool launchedMbf = false;

        private bool updateCheckStarted;
        private bool updateCheckInProgress;

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

            versionLabel.Text = $"Version {AppInfo.VersionString}";
            BindingContext = this;
            Flow.OnMessage += this.Flow_OnMessage;
        }

        /// <summary>
        /// Opens the browser page with the configured app URL and the C# ADB bridge.
        /// </summary>
        /// <returns></returns>
        public async Task LaunchMbf()
        {
            var address = AppConfig.AppUrl;

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

            var status = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
            if (!App.IsPrimaryUser && status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.StorageWrite>();
                if (status != PermissionStatus.Granted)
                {
                    await MainThread.InvokeOnMainThreadAsync(() => DisplayAlert(AppResources.Error, "Storage permission is required to launch the browser with the ADB bridge enabled.", AppResources.AlertDismiss));
                    return;
                }
            }

            MainThread.BeginInvokeOnMainThread(() => _ = Navigation.PushAsync(
                new BrowserPage(address, App.IsPrimaryUser
                    ? new TcpAdbSocketFactory("127.0.0.1", AdbServer.AdbPort)
                    : new VirtualAdbServer())));
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
                if (!packages.Contains(AppConfig.SelectedGame))
                {
                    AppConfig.SelectedGame = "com.beatgames.beatsaber";
                }

                await MainThread.InvokeOnMainThreadAsync(() => packagePicker.SelectedIndex = packages.IndexOf(AppConfig.SelectedGame));
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
                developerLayout.IsVisible = layout == Layouts.Connected && AppConfig.ShowDevOptions;
                exitLayout.IsVisible = exitLayout.IsEnabled = layout == Layouts.Connected;
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

        private async Task CheckForUpdatesAsync(bool userInitiated)
        {
            if (updateCheckInProgress || AppInfo.Version == new Version(0,0,-1))
            {
                return;
            }

            updateCheckInProgress = true;
            var downloadStarted = false;
            checkForUpdatesButton.IsEnabled = false;
            checkForUpdatesButton.Text = "Checking…";

            try
            {
                var update = await UpdateService.CheckForUpdateAsync();
                if (update is null)
                {
                    if (userInitiated)
                    {
                        await DisplayAlert(
                            "You're up to date",
                            $"MBF Launcher {AppInfo.VersionString} is the newest version available for this release channel.",
                            "OK");
                    }

                    return;
                }

                var releaseChannel = update.IsPrerelease ? "prerelease" : "stable release";
                var accepted = await DisplayAlert(
                    "Update available",
                    $"MBF Launcher {update.Version} is available as a {releaseChannel}. " +
                    $"You are currently using {AppInfo.VersionString}.\n\n" +
                    "Download the APK and open the Android installer?",
                    "Download update",
                    "Not now");

                if (!accepted)
                {
                    return;
                }

                downloadStarted = true;
                updateProgressContainer.IsVisible = true;
                updateProgressBar.Progress = 0;
                updatePercentLabel.Text = "0%";
                updateStatusLabel.Text = $"Downloading MBF Launcher {update.Version}…";

                var progress = new Progress<double>(value =>
                {
                    var boundedValue = Math.Clamp(value, 0, 1);
                    updateProgressBar.Progress = boundedValue;
                    updatePercentLabel.Text = $"{boundedValue:P0}";
                });

                var apkPath = await UpdateService.DownloadApkAsync(update, progress);
                updateProgressBar.Progress = 1;
                updatePercentLabel.Text = "100%";
                updateStatusLabel.Text = "Opening the Android installer…";
                UpdateService.OpenPackageInstaller(apkPath);
            }
            catch (Exception ex)
            {
                updateProgressContainer.IsVisible = false;
                if (userInitiated || downloadStarted)
                {
                    await DisplayAlert(
                        "Update failed",
                        $"MBF Launcher could not complete the update. {ex.Message}",
                        AppResources.AlertDismiss);
                }
            }
            finally
            {
                updateCheckInProgress = false;
                checkForUpdatesButton.IsEnabled = true;
                checkForUpdatesButton.Text = "Check for updates";
            }
        }

        #region Event Handlers
        /// <summary>
        /// Called when the page is loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ContentPage_Loaded(object sender, EventArgs e)
        {
            if (!updateCheckStarted)
            {
                updateCheckStarted = true;
                _ = CheckForUpdatesAsync(userInitiated: false);
            }

            if (App.IsPrimaryUser)
            {
                Flow.SendState();
            }
            else
            {
                // Non-primary users can't use the real ADB stack; skip the setup
                // flow and show the connected layout directly so they can still
                // configure the game and launch MBF with the virtual ADB server.
                _ = ShowOneLayout(Layouts.Connected);
            }
        }

        private async void checkForUpdatesButton_Clicked(object sender, EventArgs e)
            => await CheckForUpdatesAsync(userInitiated: true);

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

            if (UInt16.TryParse(portEntry.Text, out var port) && int.TryParse(pairingCodeEntry.Text, out var pairingCode))
            {
                Flow.ProvideWirelessDebugPairingInfo(pairingCodeEntry.Text.Trim(), port);
            }
            else
            {
                DisplayAlert(AppResources.Error, "Please check the format of the port and pairing code, only numbers should be entered.", AppResources.AlertDismiss);
            }
        }
        private void pairingCodeEntry_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(pairingCodeEntry.Text, out var pairingCode) && pairingCodeEntry.Text.Trim().Length == 6 && UInt16.TryParse(portEntry.Text, out var port))
            {
                pairingConfirmButton_Clicked(sender, e);
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
            fishTaps++;

            if (fishTaps >= 5)
            {
                fishTaps = 0;

                // Toggle the developer layout visibility
                AppConfig.ShowDevOptions = !developerLayout.IsVisible;

                // Update the visibility of the developer layout and exit layout
                developerLayout.IsVisible = !developerLayout.IsVisible;

                if (developerLayout.IsVisible)
                {
                    exitLayout.IsVisible = developerLayout.IsVisible;
                }

                return;
            }
        }
        #endregion

        private void packagePicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (packagePicker.SelectedIndex != -1)
            {
                var package = packagePicker.SelectedItem as String;
                if (package != null)
                {
                    AppConfig.SelectedGame = packagePicker.SelectedItem as string ?? "com.beatgames.beatsaber";
                }
            }
        }

        private void mbfDevMode_CheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            AppConfig.DevMode = mbfDevMode.IsChecked;
        }
    }
}
