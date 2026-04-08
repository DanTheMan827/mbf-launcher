using System.Net.Sockets;

namespace MBF_Launcher.WebView
{
    /// <summary>
    /// <see cref="IAdbSocket"/> implementation backed by a <see cref="TcpClient"/>
    /// connected to a real ADB server.
    /// </summary>
    internal sealed class TcpAdbSocket : IAdbSocket
    {
        private readonly TcpClient _tcp;

        /// <inheritdoc/>
        public Stream Stream { get; }

        public TcpAdbSocket(TcpClient tcp)
        {
            _tcp = tcp;
            Stream = tcp.GetStream();
        }

        /// <inheritdoc/>
        public void Close() => _tcp.Close();

        /// <inheritdoc/>
        public void Dispose() => _tcp.Dispose();
    }
}
