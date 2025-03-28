using System.Text.Json.Serialization;

namespace mbf_launcher.Models.Bridge
{
    public class StartupInfo
    {
        [JsonPropertyName("allowed_origins")]
        public Uri[] AllowedOrigins { get; set; }

        [JsonPropertyName("server_url")]
        public Uri ServerUrl { get; set; }

        [JsonPropertyName("browser_url")]
        public Uri BrowserUrl { get; set; }

        [JsonPropertyName("args")]
        public Args Args { get; set; }
    }
}