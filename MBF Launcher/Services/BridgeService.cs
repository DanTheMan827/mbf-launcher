using mbf_launcher.Models.Bridge;
using MBF_Launcher.Interfaces;
using System.Diagnostics;
using System.Text.Json;

namespace MBF_Launcher.Services
{

    public partial class BridgeService : IBridgeService
    {
        /// <summary>
        /// A lazy backing field for the singleton instance of the bridge service.
        /// </summary>
        private static Lazy<BridgeService> _instance = new(() => new BridgeService());

        /// <summary>
        /// Singleton instance of the bridge service.
        /// </summary>
        public static BridgeService Instance => _instance.Value;

        /// <summary>
        /// Raised when the bridge process exits.
        /// </summary>
        public event IBridgeService.BridgeExitedEventHandler? BridgeExited;

        /// <summary>
        /// Raised when the bridge sends a message.
        /// </summary>
        public event IBridgeService.BridgeMessageEventHandler? BridgeMessage;

        /// <summary>
        /// Whether the bridge process is running.
        /// </summary>
        public bool IsRunning => _bridgeProcess != null && !_bridgeProcess.HasExited;

        /// <summary>
        /// Information about the bridge process.
        /// </summary>
        public StartupInfo? StartupInfo { get; private set; }

        /// <summary>
        /// The bridge process.
        /// </summary>
        private Process? _bridgeProcess;

        /// <summary>
        /// A private constructor to prevent instantiation of the bridge service outside of the singleton instance.
        /// </summary>
        private BridgeService() { }

        /// <summary>
        /// Monitors the standard output of the bridge process for messages.
        /// </summary>
        private void MonitorBridgeMessages()
        {
            do
            {
                // Read a line from the standard output of the bridge process.
                var line = _bridgeProcess?.StandardOutput.ReadLine();

                // If the line is null or empty, continue to the next iteration.
                if (line == null || line == "")
                {
                    continue;
                }

                // Setup a variable to store the deserialized message.
                Message? message = null;

                // Attempt to deserialize the message.
                try
                {
                    message = JsonSerializer.Deserialize<Message>(line);
                }
                catch (Exception) { }

                // If the message was deserialized successfully, handle it.
                if (message != null)
                {
                    // Switch on the message type to determine the type of message.
                    switch (message.MessageType)
                    {
                        // If the message type is "StartupInfo", invoke the BridgeMessage event with the StartupInfo payload.
                        case "StartupInfo":
                            BridgeMessage?.Invoke(this, Message.FromJson<StartupInfo>(line)!);
                            break;

                        // If the message type is "StandardMessage" or "ErrorMessage", invoke the BridgeMessage event with the message payload.
                        case "StandardMessage":
                        case "ErrorMessage":
                            BridgeMessage?.Invoke(this, Message.FromJson<MessagePayload>(line)!);
                            break;

                        // If the message type is unknown, invoke the BridgeMessage event with the message.
                        default:
                            BridgeMessage?.Invoke(this, new UnknownJsonMessage()
                            {
                                Payload = new JsonPayload()
                                {
                                    message = line
                                }
                            });
                            break;
                    }
                }
                else
                {
                    BridgeMessage?.Invoke(this, new UnknownMessage()
                    {
                        Payload = new MessagePayload()
                        {
                            message = line
                        }
                    });
                }
            } while (IsRunning);
        }

        /// <summary>
        /// Fires the BridgeExited event when the bridge process exits.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void bridgeProcess_Exited(object? sender, EventArgs e)
        {
            BridgeExited?.Invoke(_bridgeProcess!, e);
        }

        public async Task<StartupInfo> Start(BridgeStartInfo? startInfo = null)
        {
            // If no start info was provided, use the default start info.
            if (startInfo == null)
            {
                startInfo = new BridgeStartInfo();
            }

            // If the bridge process is not running, start it.
            if (_bridgeProcess == null || _bridgeProcess.HasExited)
            {
                // Create a task completion source to wait for the StartupInfo message.
                var tcs = new TaskCompletionSource<StartupInfo>();

                // Dispose of any previous bridge process that may have exited.
                _bridgeProcess?.Dispose();

                // Start the bridge process.
                _bridgeProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = startInfo.BinaryPath,
                    ArgumentList = {
                        "--url",
                        startInfo.AppUrl,
                        "--proxy",
                        "--port",
                        startInfo.Port.ToString(),
                        "--adb-port",
                        startInfo.AdbPort.ToString(),
                        "--output-json"
                    },
                    RedirectStandardOutput = true,
                });

                // If the bridge process failed to start, throw an exception.
                if (_bridgeProcess == null)
                {
                    throw new Exception("Failed to start bridge.");
                }

                // Subscribe to the Exited event to handle the bridge process exiting.
                _bridgeProcess.Exited += this.bridgeProcess_Exited;

                // Create a callback to handle the StartupInfo message.
                var callback = new IBridgeService.BridgeMessageEventHandler((sender, message) =>
                {
                    // If the message is a StartupInfo message, set the StartupInfo and complete the task.
                    if (message is Message<StartupInfo> startupInfo)
                    {
                        StartupInfo = startupInfo.Payload;
                        tcs.SetResult(startupInfo.Payload);
                    }
                });

                // Subscribe to the BridgeMessage event to handle the StartupInfo message.
                BridgeMessage += callback;

                // Start monitoring the standard output of the bridge process for messages.
                new Thread(MonitorBridgeMessages).Start();

                // Wait for the StartupInfo message.
                await tcs.Task;

                // Unsubscribe from the BridgeMessage event.
                BridgeMessage -= callback;

                // Return the StartupInfo.
                return await tcs.Task;
            }

            // If the bridge process is already running, throw an exception.
            throw new Exception("Bridge is already running.");
        }

        public void Stop()
        {
            // If the bridge process is running, kill it.
            if (IsRunning)
            {
                _bridgeProcess!.Kill();
            }

            // Dispose of the bridge process.
            _bridgeProcess?.Dispose();

            // Set the bridge process to null.
            _bridgeProcess = null;
        }
    }
}
