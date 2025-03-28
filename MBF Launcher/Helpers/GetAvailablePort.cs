using System.Net;
using System.Net.Sockets;

internal static partial class Helpers
{
    /// <summary>
    /// checks for used ports and retrieves the first free port
    /// </summary>
    /// <returns>the free port or 0 if it did not find a free port</returns>
    public static UInt16 GetAvailablePort(UInt16 startingPort = 25036, UInt16 maxPort = UInt16.MaxValue)
    {
        int port = startingPort - 1;

        while (++port <= maxPort)
        {
            try
            {
                using (var listener = new TcpListener(IPAddress.Loopback, port))
                {
                    listener.Start();
                    return (ushort)port;
                }
            }
            catch (Exception) { }
        }

        throw new Exception("Free port could not be found");
    }
}
