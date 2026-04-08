using System.Net.Sockets;

namespace MBF_Launcher.WebView
{
    /// <summary>
    /// <see cref="IAdbSocketFactory"/> that opens TCP connections to a real ADB
    /// server (e.g. the on-device <c>adbd</c> listening on <paramref name="port"/>).
    /// This preserves the original ADB TCP proxy behaviour of
    /// <see cref="MbfBridgeJavascriptInterface"/>.
    /// </summary>
    internal sealed class TcpAdbSocketFactory : IAdbSocketFactory
    {
        private readonly string _host;
        private readonly int _port;

        /// <param name="host">Hostname or IP address of the ADB server (usually <c>127.0.0.1</c>).</param>
        /// <param name="port">TCP port the ADB server listens on.</param>
        public TcpAdbSocketFactory(string host, int port)
        {
            _host = host;
            _port = port;
        }

        /// <inheritdoc/>
        public async Task<IAdbSocket> ConnectAsync()
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(_host, _port);
            return new TcpAdbSocket(tcp);
        }
    }
}
