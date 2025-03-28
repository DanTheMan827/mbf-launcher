using System.Text.Json.Serialization;

namespace mbf_launcher.Models.Bridge
{
    public class Args
    {
        /// <summary>
        /// The port number to use.
        /// </summary>
        [JsonPropertyName("port")]
        public long Port { get; set; }

        /// <summary>
        /// Automatically exit the bridge after 10 seconds of inactivity.
        /// </summary>
        [JsonPropertyName("auto_close")]
        public bool AutoClose { get; set; }

        /// <summary>
        /// Specify a custom URL for the MBF app.
        /// </summary>
        [JsonPropertyName("url")]
        public Uri Url { get; set; }

        /// <summary>
        /// Proxy requests through the internal server to avoid mixed content errors.
        /// </summary>
        [JsonPropertyName("proxy")]
        public bool Proxy { get; set; }

        /// <summary>
        /// Allocate a console window to display logs.
        /// </summary>
        [JsonPropertyName("console")]
        public bool Console { get; set; }

        /// <summary>
        /// Start the server without automatically opening the browser.
        /// </summary>
        [JsonPropertyName("no_browser")]
        public bool NoBrowser { get; set; }

        /// <summary>
        /// The port that the adb server is running on.
        /// </summary>
        [JsonPropertyName("adb_port")]
        public long AdbPort { get; set; }

        /// <summary>
        /// Output the console messages as JSON.
        /// </summary>
        [JsonPropertyName("output_json")]
        public bool OutputJson { get; set; }

        /// <summary>
        /// Enable MBF development mode.
        /// </summary>
        [JsonPropertyName("dev_mode")]
        public bool DevMode { get; set; }

        /// <summary>
        /// Specify a custom game ID for the MBF app.
        /// </summary>
        [JsonPropertyName("game_id")]
        public string GameId { get; set; }

        /// <summary>
        /// Ignore the package ID check during qmod installation.
        /// </summary>
        [JsonPropertyName("ignore_package_id")]
        public bool IgnorePackageId { get; set; }

        /// <summary>
        /// Additional HTTP origins to allow for CORS
        /// </summary>
        [JsonPropertyName("additional_origins")]
        public object[] AdditionalOrigins { get; set; }

        /// <summary>
        /// The IP address to bind the server to
        /// </summary>
        [JsonPropertyName("bind_ip")]
        public string BindIp { get; set; }

        /// <summary>
        /// Print help.
        /// </summary>
        [JsonPropertyName("help")]
        public bool Help { get; set; }
    }
}
