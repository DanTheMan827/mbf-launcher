using DanTheMan827.OnDeviceADB;

internal static partial class Helpers
{
    /// <summary>
    /// Uses ADB to kill the app and restart it by using a detached shell command with the app restart command.
    /// </summary>
    /// <returns></returns>
    public static async Task RestartApp()
    {
        await AdbWrapper.RunAdbCommandAsync("shell", $"sh -c '{DanTheMan827.OnDeviceADB.SharedData.AppRestartCommand}' > /dev/null 2>&1 < /dev/null &");
    }
}