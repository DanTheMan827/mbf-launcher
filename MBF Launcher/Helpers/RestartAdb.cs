using DanTheMan827.OnDeviceADB;

internal static partial class Helpers
{
    /// <summary>
    /// Restarts the adb server
    /// </summary>
    /// <returns></returns>
    public static async Task RestartAdb()
    {
        await AdbWrapper.DisconnectAsync();
        await AdbWrapper.KillServerAsync();
        await AdbWrapper.StartServerAsync();
    }
}
