using DanTheMan827.OnDeviceADB;

internal static partial class Helpers
{
    public static async Task OpenBrowser(string url, string? device = null)
    {
        var browserResult = await AdbWrapper.RunShellCommand(device, "am", "start", "-a", "android.intent.action.VIEW", "-d", url);

        if (browserResult.ExitCode != 0)
        {
            throw new Exception(browserResult.Output);
        }
    }
}
