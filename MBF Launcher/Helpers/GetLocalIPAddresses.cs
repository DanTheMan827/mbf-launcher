using MBF_Launcher;

internal static partial class Helpers
{
    /// <summary>
    /// Gets the device's local IP address.
    /// </summary>
    /// <returns>The IP address as a string.</returns>
    public static string[] GetLocalIPAddresses()
    {
        var addresses = new List<string>();

        // Get the currently active network
        var activeNetwork = SharedData.ConnectivityManager.ActiveNetwork;
        if (activeNetwork == null)
        {
            return addresses.ToArray();
        }

        // Retrieve link properties for the active network
        var linkProperties = SharedData.ConnectivityManager.GetLinkProperties(activeNetwork);
        if (linkProperties == null)
        {
            return addresses.ToArray();
        }

        // Iterate over the link addresses to find an IPv4 address
        foreach (var linkAddress in linkProperties.LinkAddresses)
        {
            var address = linkAddress.Address?.HostAddress;
            if (address != null)
            {
                addresses.Add(address);
            }
        }

        return addresses.ToArray();
    }
}
