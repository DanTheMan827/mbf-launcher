using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MBF_Launcher.Services
{
    internal static class UpdateService
    {
        private const string Owner = "DanTheMan827";
        private const string Repo  = "mbf-launcher";
        private static readonly string ReleasesUrl =
            $"https://api.github.com/repos/{Owner}/{Repo}/releases";

        internal sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = "";

            [JsonPropertyName("prerelease")]
            public bool Prerelease { get; set; }

            [JsonPropertyName("assets")]
            public List<GitHubAsset> Assets { get; set; } = new();
        }

        internal sealed class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = "";

            [JsonPropertyName("browser_download_url")]
            public string DownloadUrl { get; set; } = "";
        }

        // ── Version helpers ───────────────────────────────────────────────────

        /// <summary>Returns the stable portion of a semver string (before any '-').</summary>
        private static string StableVersion(string version)
        {
            var idx = version.IndexOf('-');
            return idx >= 0 ? version[..idx] : version;
        }

        /// <summary>Compares two dotted numeric version strings element-by-element.</summary>
        private static int CompareVersionParts(string a, string b)
        {
            var ap = a.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
            var bp = b.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
            for (int i = 0; i < Math.Max(ap.Length, bp.Length); i++)
            {
                var av = i < ap.Length ? ap[i] : 0;
                var bv = i < bp.Length ? bp[i] : 0;
                if (av != bv) return av.CompareTo(bv);
            }
            return 0;
        }

        /// <summary>
        /// Full semver comparison.  Stable releases rank higher than pre-releases
        /// with the same base version; pre-release strings are compared
        /// lexicographically.
        /// </summary>
        private static int CompareVersions(string a, string b)
        {
            var cmp = CompareVersionParts(StableVersion(a), StableVersion(b));
            if (cmp != 0) return cmp;

            bool aPre = a.Contains('-');
            bool bPre = b.Contains('-');
            if (!aPre && bPre)  return  1;  // stable > pre-release
            if (aPre  && !bPre) return -1;  // pre-release < stable
            if (!aPre)          return  0;  // both stable, same version

            // Both pre-release: compare the suffix lexicographically
            var aSuffix = a[(a.IndexOf('-') + 1)..];
            var bSuffix = b[(b.IndexOf('-') + 1)..];
            return string.Compare(aSuffix, bSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNewer(string candidate, string current)
            => CompareVersions(candidate, current) > 0;

        /// <summary>
        /// Returns true when the <em>stable</em> part of <paramref name="candidate"/>
        /// is ≥ the stable part of <paramref name="current"/>.
        /// </summary>
        private static bool StablePartIsNewerOrEqual(string candidate, string current)
            => CompareVersionParts(StableVersion(candidate), StableVersion(current)) >= 0;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Checks GitHub releases for a newer version according to the update policy:
        /// <list type="bullet">
        ///   <item>Pre-release build: accept a stable release at the same or newer
        ///     base version, otherwise accept a newer pre-release.</item>
        ///   <item>Stable build: accept only a newer stable release.</item>
        /// </list>
        /// Returns null when no qualifying update is found or when the current
        /// version is the default dev build ("1.0").
        /// </summary>
        public static async Task<(GitHubRelease Release, GitHubAsset Apk)?> CheckForUpdateAsync()
        {
            try
            {
                var current = AppInfo.VersionString;

                // Skip update checks for default / local dev builds.
                if (current == "1.0")
                    return null;

                bool currentIsPreRelease = current.Contains('-');

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MBF-Launcher-Updater/1.0");

                var releases = await client.GetFromJsonAsync<List<GitHubRelease>>(ReleasesUrl);
                if (releases == null || releases.Count == 0)
                    return null;

                GitHubRelease? best = null;

                foreach (var release in releases)
                {
                    var tagVer = release.TagName.TrimStart('v');

                    if (currentIsPreRelease)
                    {
                        // A stable release at the same or newer base version beats everything.
                        if (!release.Prerelease && StablePartIsNewerOrEqual(tagVer, current))
                        {
                            if (best == null || best.Prerelease || IsNewer(tagVer, best.TagName.TrimStart('v')))
                                best = release;
                        }
                        // A newer pre-release is acceptable only when no stable candidate exists.
                        else if (release.Prerelease && IsNewer(tagVer, current))
                        {
                            if (best == null || (best.Prerelease && IsNewer(tagVer, best.TagName.TrimStart('v'))))
                                best = release;
                        }
                    }
                    else
                    {
                        // Stable build: only consider newer stable releases.
                        if (!release.Prerelease && IsNewer(tagVer, current))
                        {
                            if (best == null || IsNewer(tagVer, best.TagName.TrimStart('v')))
                                best = release;
                        }
                    }
                }

                if (best == null)
                    return null;

                var apk = best.Assets.FirstOrDefault(
                    a => a.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));

                return apk != null ? (best, apk) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Downloads the APK from <paramref name="url"/> into the app's cache
        /// directory and returns the local file path.
        /// </summary>
        public static async Task<string> DownloadApkAsync(string url, IProgress<double>? progress = null)
        {
            var localPath = Path.Combine(FileSystem.CacheDirectory, "update.apk");

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MBF-Launcher-Updater/1.0");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var file   = File.Create(localPath);

            var buffer = new byte[65536];
            long read  = 0;
            int  bytes;
            while ((bytes = await stream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, bytes));
                read += bytes;
                if (total is > 0)
                    progress?.Report((double)read / total.Value);
            }

            return localPath;
        }
    }
}
