using Android.Views;
using Android.Webkit;
using DanTheMan827.OnDeviceADB;
using MBF_Launcher.WebView;

namespace MBF_Launcher;

public partial class BrowserPage : ContentPage
{
    private IAdbSocketFactory? _socketFactory;

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
            // as window.__mbfBridge.  The factory determines whether the bridge
            // proxies a real ADB TCP connection or uses the virtual ADB server.
            if (_socketFactory is not null)
            {
                browser.AddJavascriptInterface(
                    new MbfBridgeJavascriptInterface(browser, _socketFactory),
                    "__mbfBridgeNative");

                // Eagerly load bridge.js so we can register it as a document-start
                // script, guaranteeing that window.__mbfBridge is defined before
                // any page JavaScript runs (fixes the race where pages could call
                // __mbfBridge before the post-navigation injection had completed).
                try
                {
                    if (_bridgeScript == null)
                    {
                        var assetManager = Android.App.Application.Context.Assets!;
                        using var stream = assetManager.Open("bridge.js");
                        using var reader = new StreamReader(stream);
                        _bridgeScript = reader.ReadToEnd();
                    }

                    if (Android.Webkit.WebViewCompat.IsFeatureSupported(
                            Android.Webkit.WebViewFeature.DocumentStartScript))
                    {
                        Android.Webkit.WebViewCompat.AddDocumentStartJavaScript(
                            browser, _bridgeScript, new System.Collections.Generic.List<string> { "*" });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[BrowserPage] AddDocumentStartJavaScript failed: {ex.Message}");
                }
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
    /// Injects bridge.js after every successful navigation as a fallback for
    /// WebKit versions that do not support <c>AddDocumentStartJavaScript</c>.
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
