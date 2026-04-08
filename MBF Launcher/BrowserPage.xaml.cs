using Android.Views;
using Android.Webkit;
using DanTheMan827.OnDeviceADB;
using MBF_Launcher.WebView;

namespace MBF_Launcher;

public partial class BrowserPage : ContentPage
{
    private int _adbPort;

    /// <summary>Cached content of bridge.js, read once from app-package assets.</summary>
    private static string? _bridgeScript;

    public BrowserPage()
    {
        InitializeComponent();
        webView.HandlerChanged += this.WebView_HandlerChanged;
    }

    private void WebView_HandlerChanged(object? sender, EventArgs e)
    {
        if (webView.Handler?.PlatformView is Android.Webkit.WebView browser && browser.Settings is Android.Webkit.WebSettings settings)
        {
            settings.UserAgentString = "MbfLauncher/1.0"; // Set user agent
            settings.JavaScriptEnabled = true;
            settings.AllowContentAccess = true;
            settings.CacheMode = CacheModes.Default;
            settings.SetSupportZoom(false);
            settings.MediaPlaybackRequiresUserGesture = false;
            settings.DomStorageEnabled = true;
            settings.LoadWithOverviewMode = true;
            settings.UseWideViewPort = true;
            settings.AllowFileAccess = true;
            settings.MixedContentMode = MixedContentHandling.AlwaysAllow;
            settings.JavaScriptCanOpenWindowsAutomatically = false;
            browser.Focusable = true;
            browser.OverScrollMode = OverScrollMode.Never;

            // Register the native ADB bridge so that bridge.js can wrap it
            // as window.__mbfBridge.  The object is available to all JavaScript
            // on the page from the moment the WebView processes any script.
            browser.AddJavascriptInterface(
                new MbfBridgeJavascriptInterface(webView, _adbPort),
                "__mbfBridgeNative");
        }
    }

    /// <summary>
    /// Opens <paramref name="url"/> directly in the WebView and registers the
    /// ADB bridge JavaScript interface.
    /// </summary>
    /// <param name="url">URL to load (http/https or file:///android_asset/…).</param>
    /// <param name="adbPort">Port the on-device ADB server is listening on.</param>
    public BrowserPage(string url, int adbPort) : this()
    {
        _adbPort = adbPort;
        webView.Navigated += this.WebView_Navigated;
        webView.Source = new UrlWebViewSource { Url = url };
    }

    /// <summary>
    /// Injects bridge.js after every successful navigation so that
    /// <c>window.__mbfBridge</c> is available even on pages that do not bundle
    /// it themselves.  The script is idempotent and safe to run multiple times.
    /// </summary>
    private async void WebView_Navigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result == WebNavigationResult.Success)
        {
            await InjectBridgeScript();
        }
    }

    private async Task InjectBridgeScript()
    {
        try
        {
            if (_bridgeScript == null)
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("bridge.js");
                using var reader = new StreamReader(stream);
                _bridgeScript = await reader.ReadToEndAsync();
            }

            await webView.EvaluateJavaScriptAsync(_bridgeScript);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BrowserPage] Failed to inject bridge.js: {ex.Message}");
        }
    }
}
