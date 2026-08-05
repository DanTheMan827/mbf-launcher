namespace MBF_Launcher.WebView
{
    /// <summary>
    /// <see cref="IAdbSocket"/> backed by an in-memory <see cref="DuplexStream"/>
    /// created by <see cref="VirtualAdbServer"/>.
    /// </summary>
    internal sealed class VirtualAdbSocket : IAdbSocket
    {
        /// <inheritdoc/>
        public Stream Stream { get; }

        public VirtualAdbSocket(Stream stream) => Stream = stream;

        /// <inheritdoc/>
        public void Close() => Stream.Dispose();

        /// <inheritdoc/>
        public void Dispose() => Stream.Dispose();
    }
}
