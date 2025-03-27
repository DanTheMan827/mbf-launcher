namespace MBF_Launcher;

public partial class BrowserPage : ContentPage
{
    public BrowserPage()
    {
        InitializeComponent();
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
