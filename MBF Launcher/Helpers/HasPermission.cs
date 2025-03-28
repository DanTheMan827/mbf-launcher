using DanTheMan827.OnDeviceADB;

internal static partial class Helpers
{
    /// <summary>
    /// Checks if we have our needed permissions by trying to set the Wi-Fi debugging state
    /// </summary>
    /// <returns></returns>
    public static bool HasPermission()
    {
        try
        {
            AdbWrapper.AdbWifiState = AdbWrapper.AdbWifiState;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
