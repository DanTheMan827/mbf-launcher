using DanTheMan827.OnDeviceADB;
using Tmds.MDns;

namespace MBF_Launcher
{
    internal class AdbFlow
    {
        /// <summary>
        /// Contains the possible states for the flow.
        /// </summary>
        public enum AdbFlowState
        {
            /// <summary>
            /// The flow is ready to be started with <see cref="StartFlow" />.
            /// 
            /// The flow is also set to this state when <see cref="StopFlow" /> is called.
            /// </summary>
            Stopped,

            /// <summary>
            /// 1. The flow is initializing.
            /// 
            /// An ideal state for the window would be to show a loading spinner with status text.
            /// </summary>
            Initializing,

            /// <summary>
            /// 2. The flow is waiting for the user to authorize the connection.
            /// 
            /// An ideal state for the window would be to show a message stating that the user needs to authorize the connection with a button to retry by restarting the adb server.
            /// </summary>
            WaitingForAuthorization,

            /// <summary>
            /// 3. The flow is waiting for wireless debugging to be enabled.
            /// 
            /// An ideal state for the window would be to show a message stating that the user needs to enable wireless debugging on the device with a button to open the developer settings.
            /// </summary>
            WaitingForWirelessDebugging,

            /// <summary>
            /// The flow is waiting for the user to provide the wireless debug pairing information.
            /// 
            /// An ideal state for the window would be to show a message stating that the user needs to provide the pairing information with a text box to enter the pairing code and port if neccesary and a button to submit the information.
            /// 
            /// 
            /// </summary>
            WaitingForPairingInfo,

            /// <summary>
            /// The flow is attempting to pair with wireless debug.
            /// 
            /// An ideal state for the window would be to show a loading spinner with status text.
            /// </summary>
            WirelessDebugPairing,

            /// <summary>
            /// The flow is waiting for the wireless debug port.
            /// 
            /// An ideal state for the window would be to show a message stating that the user needs to provide the wireless debug port with a text box to enter the port and a button to submit the information.
            /// </summary>
            WaitingForDebugPort,

            /// <summary>
            /// The flow is attempting to connect to the device.
            /// </summary>
            Connecting,

            /// <summary>
            /// The flow is connected to the device
            /// </summary>
            Connected,

            /// <summary>
            /// The device has unexpectedly disconnected.
            /// </summary>
            Disconnected
        }

        public static class FlowStrings
        {
            public static readonly string StartingAdb = AppResources.StartingAdb;
            public static readonly string GettingDevices = AppResources.GettingDevices;
            public static readonly string DisconnectingDevices = AppResources.DisconnectingDevices;
            public static readonly string EnablingWirelessDebugging = AppResources.EnablingWirelessDebugging;
            public static readonly string CheckingDevice = AppResources.CheckingDevice;
            public static readonly string PairingWithDevice = AppResources.PairingWithDevice;
            public static readonly string ScanningForWirelessDebugPort = AppResources.ScanningForWirelessDebugPort;
            public static readonly string ConnectingOnPort = AppResources.ConnectingOnPort;
            public static readonly string GrantingPermissions = AppResources.GrantingPermissions;
            public static readonly string NoAuthorizedDevicesFound = AppResources.NoAuthorizedDevicesFound;
            public static readonly string StoppingAdb = AppResources.StoppingAdb;
            public static readonly string SettingTcpIpMode = AppResources.SettingTcpIpMode;
        }

        /// <summary>
        /// Base class for messages that are sent from the flow.
        /// </summary>
        public class FlowMessage
        {
            public Type MessageType { get; protected set; }
        }

        /// <summary>
        /// Generic message that is sent from the flow.
        /// </summary>
        /// <typeparam name="M"></typeparam>
        /// <typeparam name="P"></typeparam>
        public class FlowMessage<M, P> : FlowMessage
        {
            public P Payload { get; private set; }
            protected FlowMessage(P payload)
            {
                MessageType = typeof(M);
                Payload = payload;
            }
        }

        /// <summary>
        /// Message that is sent when the flow status changes.
        /// </summary>
        public class StatusMessage : FlowMessage<StatusMessage, string?>
        {
            public StatusMessage(string? payload) : base(payload) { }
        }

        /// <summary>
        /// A base class for messages that are sent when the flow encounters an error.
        /// </summary>
        public class ErrorMessage : FlowMessage<ErrorMessage, Exception>
        {
            public ErrorMessage(Exception payload) : base(payload) { }
        }

        /// <summary>
        /// Message that is sent when the flow state changes.
        /// </summary>
        public class StateChange : FlowMessage<StateChange, AdbFlowState>
        {
            public StateChange(AdbFlowState payload) : base(payload) { }
        }

        /// <summary>
        /// Message that is sent when the detected wireless debug pairing port changes.
        /// </summary>
        public class PairingPortChange : FlowMessage<PairingPortChange, ushort?>
        {
            public PairingPortChange(ushort? payload) : base(payload) { }
        }

        /// <summary>
        /// Message that is sent when the flow encounters a wireless debug pairing error.
        /// </summary>
        public class PairingError : ErrorMessage
        {
            public PairingError(Exception payload) : base(payload) { }
        }

        /// <summary>
        /// Message that is sent when the flow encounters an error while attempting to grant permissions.
        /// </summary>
        public class PermissionsError : ErrorMessage
        {
            public PermissionsError(Exception payload) : base(payload) { }
        }

        /// <summary>
        /// Message that is sent when the flow encounters an error while attempting to connect to a device.
        /// </summary>
        public class ConnectionError : ErrorMessage
        {
            public ConnectionError(Exception payload) : base(payload) { }
        }

        /// <summary>
        /// Message that is sent when the devices connected to the ADB server change.
        /// </summary>
        public class DevicesChanged : FlowMessage<DevicesChanged, AdbWrapper.AdbDevice[]>
        {
            public DevicesChanged(AdbWrapper.AdbDevice[] payload) : base(payload) { }
        }

        /// <summary>
        /// Delegate for handling flow messages.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="message"></param>
        public delegate void MessageHandler(AdbFlow sender, FlowMessage message);

        /// <summary>
        /// Event that is fired when a message is sent from the flow.
        /// </summary>
        public event MessageHandler OnMessage;

        /// <summary>
        /// A lock object for updating the last status message that was sent from the flow.
        /// </summary>
        private object _statusLock = new object();

        /// <summary>
        /// The backing field for the last status message that was sent from the flow.
        /// </summary>
        private string? _lastStatus = null;

        /// <summary>
        /// The last status message that was sent from the flow.
        /// </summary>
        public string? LastStatus
        {
            get
            {
                lock (_statusLock)
                {
                    return _lastStatus;
                }
            }
            set
            {
                lock (_statusLock)
                {
                    _lastStatus = value;
                }

                SendMessage(new StatusMessage(value));
            }
        }

        /// <summary>
        /// The backing field for the current state of the flow.
        /// </summary>
        private AdbFlowState _state = AdbFlowState.Stopped;

        /// <summary>
        /// The current state of the flow.
        /// </summary>
        public AdbFlowState State
        {
            get => _state;
            set
            {
                _state = value;
                SendMessage(new StateChange(value));
            }
        }

        /// <summary>
        /// The backing field for the list of devices connected to the ADB server.
        /// </summary>
        private AdbWrapper.AdbDevice[] _devices = [];

        /// <summary>
        /// List of devices connected to the ADB server
        /// </summary>
        public AdbWrapper.AdbDevice[] Devices
        {
            get => _devices;
            private set
            {
                _devices = value;
                AuthorizedDevices = _devices.Where(device => device.Authorized).ToArray();
                UnauthorizedDevices = _devices.Where(device => !device.Authorized).ToArray();
                SendMessage(new DevicesChanged(_devices));
            }
        }

        /// <summary>
        /// List of devices connected to the ADB server that are authorized
        /// </summary>
        public AdbWrapper.AdbDevice[] AuthorizedDevices { get; private set; }

        /// <summary>
        /// List of devices connected to the ADB server that are unauthorized
        /// </summary>
        public AdbWrapper.AdbDevice[] UnauthorizedDevices { get; private set; }

        public bool IsFlowRunning { get; private set; }

        /// <summary>
        /// The service browser that is used to detect the wireless debug pairing port.
        /// </summary>
        private ServiceBrowser ServiceBrowser;

        /// <summary>
        /// If the connection monitor should expect the monitored process to end.
        /// </summary>
        private bool _expectedDisconnect = false;

        /// <summary>
        /// The initializer for the flow object.
        /// </summary>
        public AdbFlow()
        {
            // Initialize the service browser
            ServiceBrowser = new ServiceBrowser();
            ServiceBrowser.ServiceAdded += this.ServiceBrowser_ServiceAdded;
            ServiceBrowser.ServiceRemoved += this.ServiceBrowser_ServiceRemoved;

            // Starts the service browser
            ServiceBrowser.StartBrowse("_adb-tls-pairing._tcp");
        }

        /// <summary>
        /// Fired when the wireless debug pairing service is removed.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ServiceBrowser_ServiceRemoved(object? sender, ServiceAnnouncementEventArgs e)
        {
            if (Helpers.checkPairingService(e.Announcement))
            {
                // Send a message that the pairing port has been removed
                OnMessage?.Invoke(this, new PairingPortChange(null));
            }
        }

        /// <summary>
        /// Fired when the wireless debug pairing service is added.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ServiceBrowser_ServiceAdded(object? sender, ServiceAnnouncementEventArgs e)
        {
            if (Helpers.checkPairingService(e.Announcement))
            {
                // Send a message that the pairing port has been removed
                OnMessage?.Invoke(this, new PairingPortChange(e.Announcement.Port));
            }
        }

        private async void SendMessage(FlowMessage message)
        {
            await Task.Run(() => OnMessage?.Invoke(this, message));
        }

        /// <summary>
        /// Updates the list of devices connected to the ADB server.
        /// </summary>
        /// <returns></returns>
        private async Task UpdateDevices(string? message = null)
        {
            if (message == null)
            {
                message = FlowStrings.GettingDevices;
            }

            LastStatus = message;
            Devices = await AdbWrapper.GetDevicesAsync();
        }

        /// <summary>
        /// Starts the flow.
        /// </summary>
        public async Task StartFlow()
        {
            if (IsFlowRunning)
            {
                throw new Exception("Flow is already running");
            }

            IsFlowRunning = true;
            State = AdbFlowState.Initializing;

            if (!AdbWrapper.IsServerRunning)
            {
                LastStatus = FlowStrings.StartingAdb;
                await AdbWrapper.DisconnectAsync();
            }

            await UpdateDevices();

            if (AuthorizedDevices.Length == 0 && UnauthorizedDevices.Length == 0 && Helpers.HasPermission())
            {
                try
                {
                    await EnableWifiDebug();
                }
                catch (Exception ex) { }
            }

            while (AuthorizedDevices.Length == 0 && UnauthorizedDevices.Length > 0)
            {
                if (State != AdbFlowState.WaitingForAuthorization)
                {
                    LastStatus = null;
                    State = AdbFlowState.WaitingForAuthorization;
                }

                await UpdateDevices();
            }

            if (Devices.Length == 0)
            {
                LastStatus = null;
                State = AdbFlowState.WaitingForWirelessDebugging;
                return;
            }

            await DeviceConnected();

            IsFlowRunning = false;
        }

        /// <summary>
        /// Enables wireless debugging and connects to the device
        /// </summary>
        /// <returns></returns>
        private async Task EnableWifiDebug()
        {
            LastStatus = FlowStrings.EnablingWirelessDebugging;

            AdbWrapper.AdbWifiState = AdbWifiState.Enabled;
            UInt16 port = 0;

            while (port == 0)
            {
                LastStatus = FlowStrings.ScanningForWirelessDebugPort;
                port = await Helpers.GetAdbPort();

                if (port > 0)
                {
                    break;
                }
                else
                {
                    Thread.Sleep(500);
                }
            }

            State = AdbFlowState.Connecting;
            LastStatus = string.Format(FlowStrings.ConnectingOnPort, port);
            var device = await AdbWrapper.ConnectAsync("127.0.0.1", port);

            await UpdateDevices();
        }

        /// <summary>
        /// Provides the wireless debug pairing information to the flow.
        /// 
        /// This can only be called when the flow is in the WaitingForPairingInfo state.
        /// </summary>
        /// <param name="pairingCode"></param>
        /// <param name="port"></param>
        public async void ProvideWirelessDebugPairingInfo(string pairingCode, int pairingPort)
        {
            try
            {
                State = AdbFlowState.WirelessDebugPairing;
                LastStatus = FlowStrings.PairingWithDevice;

                var pairResult = await AdbWrapper.RunAdbCommandAsync("pair", $"127.0.0.1:{pairingPort}", pairingCode.ToString());

                if (pairResult.Output.StartsWith("Failed:"))
                {
                    throw new Exception(pairResult.Output.Substring(8));
                }
                else
                {
                    await UpdateDevices(FlowStrings.CheckingDevice);

                    if (AuthorizedDevices.Length == 0)
                    {
                        LastStatus = FlowStrings.ScanningForWirelessDebugPort;
                        var port = await Helpers.GetAdbPort();

                        if (port > 0)
                        {
                            State = AdbFlowState.Connecting;
                            LastStatus = string.Format(FlowStrings.ConnectingOnPort, port);

                            var device = await AdbWrapper.ConnectAsync("127.0.0.1", port);

                            await UpdateDevices();

                            if (Devices.Length == 0)
                            {
                                await AdbWrapper.DisconnectAsync();
                                LastStatus = null;
                                State = AdbFlowState.WaitingForDebugPort;
                            }
                            else
                            {
                                _ = DeviceConnected();
                            }
                        }
                        else
                        {
                            LastStatus = null;
                            State = AdbFlowState.WaitingForDebugPort;
                        }
                    }
                    else
                    {
                        _ = DeviceConnected();
                    }

                }
            }
            catch (Exception ex)
            {
                State = AdbFlowState.WaitingForPairingInfo;
                SendMessage(new PairingError(ex));
            }
        }

        public async void ProvideWirelessDebugPort(int port)
        {
            try
            {
                State = AdbFlowState.Connecting;

                var connectResult = await AdbWrapper.ConnectAsync("127.0.0.1", port);

                //LastStatus = FlowStrings.SettingTcpIpMode;
                //await AdbWrapper.TcpIpMode(5555);

                //LastStatus = FlowStrings.DisconnectingDevices;
                //await AdbWrapper.DisconnectAsync();

                await UpdateDevices();

                if (AuthorizedDevices.Length > 0)
                {
                    await DeviceConnected();

                    return;
                }

                throw new Exception(FlowStrings.NoAuthorizedDevicesFound);
            }
            catch (Exception ex)
            {
                SendMessage(new ConnectionError(ex));
            }
        }

        /// <summary>
        /// Run after a valid device has been detected
        /// </summary>
        /// <returns></returns>
        private async Task DeviceConnected()
        {
            if (!Helpers.HasPermission())
            {
                LastStatus = FlowStrings.GrantingPermissions;
                try
                {
                    var package = Android.App.Application.Context.PackageName;
                    await AdbWrapper.RunAdbCommandAsync("shell", $"sh -c '{AdbWrapper.GrantPermissionsCommand}; {SharedData.AppRestartCommand}' > /dev/null 2>&1 < /dev/null &");
                }
                catch (Exception ex)
                {
                    _ = Task.Run(() => OnMessage?.Invoke(this, new PermissionsError(ex)));
                }
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await AdbWrapper.RunShellCommand(Devices.First().Name, "tail -f /dev/null");
                }
                catch (Exception) { }

                if (!_expectedDisconnect)
                {
                    State = AdbFlowState.Disconnected;
                    _expectedDisconnect = false;
                }
            });

            LastStatus = null;
            State = AdbFlowState.Connected;
        }

        /// <summary>
        /// Switches the ADB server to TCP/IP mode and restarts the flow.
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        public async Task TcpIpMode(int port = 5555)
        {
            _expectedDisconnect = true;
            LastStatus = FlowStrings.SettingTcpIpMode;
            await AdbWrapper.TcpIpMode(port);

            LastStatus = FlowStrings.DisconnectingDevices;
            await AdbWrapper.DisconnectAsync();

            LastStatus = FlowStrings.StoppingAdb;
            await AdbWrapper.KillServerAsync();

            Devices = [];
            LastStatus = null;

            await Task.Delay(2000);

            _ = StartFlow();
        }

        /// <summary>
        /// Stops the ADB server and resets the flow to the initial state.
        /// </summary>
        /// <returns></returns>
        public async Task StopFlow()
        {
            _expectedDisconnect = true;

            LastStatus = FlowStrings.DisconnectingDevices;
            await AdbWrapper.DisconnectAsync();

            LastStatus = FlowStrings.StoppingAdb;
            await AdbWrapper.KillServerAsync();

            Devices = [];
            LastStatus = null;
            State = AdbFlowState.Stopped;
        }

        /// <summary>
        /// Sends the current state and status to the message handler.
        /// </summary>
        public void SendState()
        {
            SendMessage(new StateChange(State));
            SendMessage(new StatusMessage(LastStatus));
            SendMessage(new DevicesChanged(Devices));
        }
    }
}
