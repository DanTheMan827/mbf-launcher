using MBF_Launcher;
using System.Diagnostics;

internal static partial class Helpers
{
    public static async Task<UInt16> GetAdbPort()
    {
        var process = Process.Start(new ProcessStartInfo(Path.Combine(SharedData.NativeLibraryDir, "libAdbFinder.so"))
        {
            RedirectStandardOutput = true
        });
        await process.WaitForExitAsync();
        return UInt16.Parse(process.StandardOutput.ReadToEnd().Trim().Split("\n").Where(l => l.Trim() != "").FirstOrDefault("0"));
    }
}
