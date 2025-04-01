using Android.Views;
using Android.Webkit;

namespace MBF_Launcher;

public partial class BrowserPage : ContentPage
{
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
        }
    }

    public BrowserPage(string url) : this()
    {
        var source = new HtmlWebViewSource
        {
            Html = @"
                <html>
                <head>
                    <style>
                        body, html {
                            margin: 0;
                            padding: 0;
                            height: 100%;
                            width: 100%;
                            overflow: hidden;
                        }
                        iframe {
                            width: 100%;
                            height: 100%;
                            border: none;
                            zoom: 0.85;
                        }
                    </style>
                </head>
                <body>
                    <iframe src='" + url + @"'></iframe>
                </body>
                </html>"
        };
        webView.Source = source;
    }
}
