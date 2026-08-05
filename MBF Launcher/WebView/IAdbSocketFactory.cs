namespace MBF_Launcher.WebView
{
    /// <summary>
    /// Factory that produces connected <see cref="IAdbSocket"/> instances.
    /// Inject a different implementation to switch between the real ADB TCP
    /// server and a virtual/simulated one.
    /// </summary>
    public interface IAdbSocketFactory
    {
        /// <summary>
        /// Creates and returns a connected <see cref="IAdbSocket"/>.
        /// Throws if the connection cannot be established.
        /// </summary>
        Task<IAdbSocket> ConnectAsync();
    }
}
