namespace MBF_Launcher.WebView
{
    /// <summary>
    /// Abstraction over a single, open, bidirectional ADB socket connection.
    /// Implementations include <see cref="TcpAdbSocket"/> (real ADB server via TCP)
    /// and any virtual/simulated socket.
    /// </summary>
    internal interface IAdbSocket : IDisposable
    {
        /// <summary>The bidirectional byte stream for this connection.</summary>
        Stream Stream { get; }

        /// <summary>Closes the underlying transport immediately.</summary>
        void Close();
    }
}
