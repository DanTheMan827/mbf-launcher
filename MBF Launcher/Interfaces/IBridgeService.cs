using mbf_launcher.Models.Bridge;
using MBF_Launcher.Services;
using System.Diagnostics;

namespace MBF_Launcher.Interfaces
{
    public interface IBridgeService
    {
        public delegate void BridgeExitedEventHandler(Process process, EventArgs e);
        public delegate void BridgeMessageEventHandler(IBridgeService bridgeService, Message message);

        /// <summary>
        /// Information required to start the bridge process.
        /// </summary>
        public interface IBridgeStartInfo
        {
            int Port { get; }
            int AdbPort { get; }
            string BinaryPath { get; }
        }

        /// <summary>
        /// Raised when the bridge process exits.
        /// </summary>
        event BridgeExitedEventHandler? BridgeExited;

        /// <summary>
        /// Raised when the bridge sends a message.
        /// </summary>
        event BridgeMessageEventHandler? BridgeMessage;

        /// <summary>
        /// Whether the bridge process is running.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Information about the bridge process.
        /// </summary>
        StartupInfo? StartupInfo { get; }

        /// <summary>
        /// Starts the bridge process.
        /// </summary>
        /// <param name="startInfo"></param>
        /// <returns></returns>
        Task<StartupInfo> Start(BridgeService.BridgeStartInfo? startInfo = null);

        /// <summary>
        /// Stops the bridge process.
        /// </summary>
        void Stop();
    }
}
