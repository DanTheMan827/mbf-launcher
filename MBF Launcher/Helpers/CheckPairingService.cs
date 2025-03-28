using Tmds.MDns;

internal static partial class Helpers
{
    /// <summary>
    /// Checks if the service is on the same network as the device
    /// </summary>
    /// <param name="service"></param>
    /// <returns></returns>
    public static bool checkPairingService(ServiceAnnouncement service)
    {
        var localAddresses = Helpers.GetLocalIPAddresses();
        foreach (var ip in service.Addresses)
        {
            if (localAddresses.Contains(ip.ToString()))
            {
                return true;
            }
        }

        return false;
    }
}
