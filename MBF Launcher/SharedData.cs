using Android.Net;

namespace MBF_Launcher
{
    internal class SharedData
    {
        private static Lazy<Android.Content.Context> _context = new(() => Android.App.Application.Context ?? throw new Exception("Unable to retrieve application context"));
        private static Lazy<string> _packageID = new(() => Context.PackageName ?? throw new Exception("Unknown package name"));
        private static Lazy<ConnectivityManager> _connectivityManager = new(() => (Context.GetSystemService(Android.Content.Context.ConnectivityService) as ConnectivityManager) ?? throw new Exception("Unable to retrieve connectivity manager"));
        private static Lazy<string> _nativeLibraryDir = new(() => Context.ApplicationInfo?.NativeLibraryDir ?? throw new Exception("Unable to get native library path"));
        private static Lazy<string> _appRestartCommand = new(() => $"am force-stop {SharedData.PackageID}; monkey -p {SharedData.PackageID} -c android.intent.category.LAUNCHER 1");

        public static Android.Content.Context Context => _context.Value;
        public static string PackageID => _packageID.Value;
        public static ConnectivityManager ConnectivityManager => _connectivityManager.Value;
        public static string NativeLibraryDir => _nativeLibraryDir.Value;
        public static string AppRestartCommand => _appRestartCommand.Value;
    }
}
