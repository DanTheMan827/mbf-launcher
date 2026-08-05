using Android.Views;
using Android.Webkit;
using MBF_Launcher.WebView;

namespace MBF_Launcher;

public partial class BrowserPage : ContentPage
{
    private IAdbSocketFactory? _socketFactory;

    /// <summary>Cached content of bridge.js, read once from app-package assets.</summary>
    private static Lazy<string> _bridgeScript = new(() =>
    {
        var assetManager = Android.App.Application.Context.Assets!;
        using var stream = assetManager.Open("bridge.js");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public BrowserPage()
    {
        InitializeComponent();
        webView.HandlerChanged += this.WebView_HandlerChanged;
    }

    private void WebView_HandlerChanged(object? sender, EventArgs e)
    {
        if (webView.Handler?.PlatformView is Android.Webkit.WebView browser && browser.Settings is Android.Webkit.WebSettings settings)
        {
            settings.UserAgentString = $"MbfLauncher/1.0"; // Set user agent
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
            // as window.__mbfBridge.  The factory determines whether the bridge
            // proxies a real ADB TCP connection or uses the virtual ADB server.
            if (_socketFactory is not null)
            {
                browser.AddJavascriptInterface(
                    new MbfBridgeJavascriptInterface(browser, _socketFactory),
                    "__mbfBridgeNative");


            }
        }
    }

    /// <summary>
    /// Opens <paramref name="url"/> directly in the WebView and registers the
    /// ADB bridge JavaScript interface backed by <paramref name="socketFactory"/>.
    /// </summary>
    /// <param name="url">URL to load (http/https or file:///android_asset/…).</param>
    /// <param name="socketFactory">
    /// Controls which ADB socket implementation is used:
    /// <list type="bullet">
    ///   <item><see cref="TcpAdbSocketFactory"/> – proxies the real ADB TCP server.</item>
    ///   <item><see cref="VirtualAdbServer"/>    – uses the in-process simulated server.</item>
    /// </list>
    /// </param>
    public BrowserPage(string url, IAdbSocketFactory socketFactory) : this()
    {
        _socketFactory = socketFactory;
        webView.Navigated += this.WebView_Navigated;
        webView.Source = new UrlWebViewSource { Url = url };

    }

    /// <summary>
    /// Injects bridge.js after every successful navigation.
    /// The script is idempotent and safe to run multiple times.
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
            await webView.EvaluateJavaScriptAsync(_bridgeScript.Value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BrowserPage] Failed to inject bridge.js: {ex.Message}");
        }
    }
}
