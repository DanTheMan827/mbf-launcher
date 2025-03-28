namespace MBF_Launcher.Services
{
    public partial class BridgeService
    {
        public class BridgeStartInfo
        {
            public int Port { get; set; } = 0;
            public int AdbPort { get; set; } = 5037;
            public string BinaryPath { get; set; } = Path.Combine(SharedData.NativeLibraryDir, "libMbfBridge.so");
            public string AppUrl { get; set; } = "https://mbf.bsquest.xyz";
        }
    }
}
