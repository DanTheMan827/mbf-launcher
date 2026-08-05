using Android.Content;
using AndroidX.Core.Content;
using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace MBF_Launcher.Services
{
    internal static class UpdateService
    {
        private const string Owner = "DanTheMan827";
        private const string Repo = "mbf-launcher";
        private const int ReleasesPerPage = 100;
        private const string ApkContentType = "application/vnd.android.package-archive";

        private static readonly string ReleasesUrl =
            $"https://api.github.com/repos/{Owner}/{Repo}/releases";

        private static readonly HttpClient Client = CreateClient();

        internal sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("body")]
            public string Body { get; set; } = string.Empty;

            [JsonPropertyName("html_url")]
            public string HtmlUrl { get; set; } = string.Empty;

            [JsonPropertyName("draft")]
            public bool Draft { get; set; }

            [JsonPropertyName("prerelease")]
            public bool Prerelease { get; set; }

            [JsonPropertyName("assets")]
            public List<GitHubAsset> Assets { get; set; } = [];
        }

        internal sealed class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("browser_download_url")]
            public string DownloadUrl { get; set; } = string.Empty;

            [JsonPropertyName("content_type")]
            public string ContentType { get; set; } = string.Empty;

            [JsonPropertyName("digest")]
            public string? Digest { get; set; }
        }

        internal sealed record UpdateInfo(
            GitHubRelease Release,
            GitHubAsset Apk,
            string Version,
            bool IsPrerelease);

        private sealed record ParsedRelease(
            GitHubRelease Release,
            GitHubAsset Apk,
            SemanticVersion Version,
            bool IsPrerelease);

        /// <summary>
        /// Checks every published GitHub release and returns the best update for
        /// the app's current release channel.
        /// </summary>
        public static async Task<UpdateInfo?> CheckForUpdateAsync(
            CancellationToken cancellationToken = default)
        {
            if (!SemanticVersion.TryParse(AppInfo.VersionString, out var currentVersion))
            {
                return null;
            }

            var releases = await GetAllReleasesAsync(cancellationToken).ConfigureAwait(false);
            var parsedReleases = releases
                .Where(release => !release.Draft)
                .Select(release => ParseRelease(release))
                .Where(release => release is not null)
                .Cast<ParsedRelease>()
                .Where(release => release.Version.CompareTo(currentVersion) > 0)
                .ToList();

            ParsedRelease? selected;
            if (currentVersion.IsPrerelease)
            {
                // A prerelease installation should move to the newest stable
                // release whenever one is newer. Only stay on the prerelease
                // channel when no newer stable release exists.
                selected = parsedReleases
                    .Where(release => !release.IsPrerelease)
                    .MaxBy(release => release.Version)
                    ?? parsedReleases
                        .Where(release => release.IsPrerelease)
                        .MaxBy(release => release.Version);
            }
            else
            {
                // Stable installations never opt into prerelease builds.
                selected = parsedReleases
                    .Where(release => !release.IsPrerelease)
                    .MaxBy(release => release.Version);
            }

            return selected is null
                ? null
                : new UpdateInfo(
                    selected.Release,
                    selected.Apk,
                    selected.Version.Original,
                    selected.IsPrerelease);
        }

        /// <summary>
        /// Downloads an update into the app cache directory and validates the
        /// GitHub-provided SHA-256 digest when one is available.
        /// </summary>
        public static async Task<string> DownloadApkAsync(
            UpdateInfo update,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var safeVersion = string.Concat(update.Version.Select(character =>
                char.IsLetterOrDigit(character) || character is '.' or '-' or '_'
                    ? character
                    : '_'));
            var localPath = Path.Combine(
                FileSystem.CacheDirectory,
                $"mbf-launcher-{safeVersion}.apk");

            try
            {
                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                }

                using var response = await Client.GetAsync(
                    update.Apk.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                await using var input = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using (var output = new FileStream(
                    localPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[64 * 1024];
                    long downloadedBytes = 0;
                    int bytesRead;

                    progress?.Report(0);
                    while ((bytesRead = await input.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(
                            buffer.AsMemory(0, bytesRead),
                            cancellationToken).ConfigureAwait(false);

                        downloadedBytes += bytesRead;
                        if (totalBytes is > 0)
                        {
                            progress?.Report((double)downloadedBytes / totalBytes.Value);
                        }
                    }
                }

                await ValidateDigestAsync(
                    localPath,
                    update.Apk.Digest,
                    cancellationToken).ConfigureAwait(false);
                progress?.Report(1);

                return localPath;
            }
            catch
            {
                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                }

                throw;
            }
        }

        /// <summary>
        /// Opens Android's package installer for the downloaded APK.
        /// </summary>
        public static void OpenPackageInstaller(string apkPath)
        {
            if (!File.Exists(apkPath))
            {
                throw new FileNotFoundException("The downloaded update could not be found.", apkPath);
            }

            var context = Android.App.Application.Context
                ?? throw new InvalidOperationException("Android application context is unavailable.");
            var apkFile = new Java.IO.File(apkPath);
            var contentUri = Microsoft.Maui.Storage.FileProvider.GetUriForFile(
                context,
                $"{context.PackageName}.fileprovider",
                apkFile);

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(contentUri, ApkContentType);
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);

            if (intent.ResolveActivity(context.PackageManager) is null)
            {
                throw new InvalidOperationException("No Android package installer is available.");
            }

            context.StartActivity(intent);
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MBF-Launcher-Updater/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return client;
        }

        private static async Task<List<GitHubRelease>> GetAllReleasesAsync(
            CancellationToken cancellationToken)
        {
            var releases = new List<GitHubRelease>();

            for (var page = 1; ; page++)
            {
                var pageUrl = $"{ReleasesUrl}?per_page={ReleasesPerPage}&page={page}";
                using var response = await Client.GetAsync(pageUrl, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var pageItems = await response.Content
                    .ReadFromJsonAsync<List<GitHubRelease>>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false) ?? [];

                releases.AddRange(pageItems);
                if (pageItems.Count < ReleasesPerPage)
                {
                    break;
                }
            }

            return releases;
        }

        private static ParsedRelease? ParseRelease(GitHubRelease release)
        {
            if (!SemanticVersion.TryParse(release.TagName, out var version))
            {
                return null;
            }

            var apk = SelectApkAsset(release.Assets);
            if (apk is null)
            {
                return null;
            }

            // Treat a semantic-version suffix as prerelease even if the GitHub
            // release metadata was accidentally configured as stable.
            var isPrerelease = release.Prerelease || version.IsPrerelease;
            return new ParsedRelease(release, apk, version, isPrerelease);
        }

        private static GitHubAsset? SelectApkAsset(IEnumerable<GitHubAsset> assets)
        {
            var apkAssets = assets
                .Where(asset =>
                    !string.IsNullOrWhiteSpace(asset.DownloadUrl)
                    && (asset.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            asset.ContentType,
                            ApkContentType,
                            StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (apkAssets.Count == 0)
            {
                return null;
            }

            var architectureToken = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "armeabi",
                Architecture.X64 => "x86_64",
                Architecture.X86 => "x86",
                _ => string.Empty
            };

            return apkAssets
                .OrderByDescending(asset => ScoreApkAsset(asset, architectureToken))
                .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        private static int ScoreApkAsset(GitHubAsset asset, string architectureToken)
        {
            var score = 0;
            if (!string.IsNullOrEmpty(architectureToken)
                && asset.Name.Contains(architectureToken, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }

            if (asset.Name.Contains("universal", StringComparison.OrdinalIgnoreCase))
            {
                score += 80;
            }

            if (asset.Name.Contains("signed", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }

            return score;
        }

        private static async Task ValidateDigestAsync(
            string filePath,
            string? expectedDigest,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(expectedDigest)
                || !expectedDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var expected = expectedDigest["sha256:".Length..].Trim();
            await using var stream = File.OpenRead(filePath);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            var actual = Convert.ToHexString(hash);

            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded update failed integrity validation.");
            }
        }

        private sealed class SemanticVersion : IComparable<SemanticVersion>
        {
            private SemanticVersion(
                string original,
                int[] core,
                string[] prerelease)
            {
                Original = original;
                Core = core;
                Prerelease = prerelease;
            }

            public string Original { get; }
            public bool IsPrerelease => Prerelease.Length > 0;
            private int[] Core { get; }
            private string[] Prerelease { get; }

            public static bool TryParse(string? value, out SemanticVersion version)
            {
                version = null!;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                var normalized = value.Trim();
                if (normalized.StartsWith('v') || normalized.StartsWith('V'))
                {
                    normalized = normalized[1..];
                }

                var metadataIndex = normalized.IndexOf('+');
                if (metadataIndex >= 0)
                {
                    normalized = normalized[..metadataIndex];
                }

                var prereleaseIndex = normalized.IndexOf('-');
                var coreText = prereleaseIndex >= 0
                    ? normalized[..prereleaseIndex]
                    : normalized;
                var prereleaseText = prereleaseIndex >= 0
                    ? normalized[(prereleaseIndex + 1)..]
                    : string.Empty;

                var coreParts = coreText.Split('.', StringSplitOptions.None);
                if (coreParts.Length == 0)
                {
                    return false;
                }

                var core = new int[coreParts.Length];
                for (var index = 0; index < coreParts.Length; index++)
                {
                    if (!int.TryParse(
                        coreParts[index],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out core[index])
                        || core[index] < 0)
                    {
                        return false;
                    }
                }

                var prerelease = string.IsNullOrEmpty(prereleaseText)
                    ? []
                    : prereleaseText.Split('.', StringSplitOptions.RemoveEmptyEntries);

                if (!string.IsNullOrEmpty(prereleaseText) && prerelease.Length == 0)
                {
                    return false;
                }

                version = new SemanticVersion(normalized, core, prerelease);
                return true;
            }

            public int CompareTo(SemanticVersion? other)
            {
                if (other is null)
                {
                    return 1;
                }

                var maxCoreLength = Math.Max(Core.Length, other.Core.Length);
                for (var index = 0; index < maxCoreLength; index++)
                {
                    var left = index < Core.Length ? Core[index] : 0;
                    var right = index < other.Core.Length ? other.Core[index] : 0;
                    var result = left.CompareTo(right);
                    if (result != 0)
                    {
                        return result;
                    }
                }

                if (!IsPrerelease && !other.IsPrerelease)
                {
                    return 0;
                }

                if (!IsPrerelease)
                {
                    return 1;
                }

                if (!other.IsPrerelease)
                {
                    return -1;
                }

                var maxPrereleaseLength = Math.Max(Prerelease.Length, other.Prerelease.Length);
                for (var index = 0; index < maxPrereleaseLength; index++)
                {
                    if (index >= Prerelease.Length)
                    {
                        return -1;
                    }

                    if (index >= other.Prerelease.Length)
                    {
                        return 1;
                    }

                    var result = ComparePrereleaseIdentifier(
                        Prerelease[index],
                        other.Prerelease[index]);
                    if (result != 0)
                    {
                        return result;
                    }
                }

                return 0;
            }

            private static int ComparePrereleaseIdentifier(string left, string right)
            {
                var leftNumeric = left.All(char.IsDigit);
                var rightNumeric = right.All(char.IsDigit);

                if (leftNumeric && rightNumeric)
                {
                    return CompareNumericStrings(left, right);
                }

                if (leftNumeric)
                {
                    return -1;
                }

                if (rightNumeric)
                {
                    return 1;
                }

                return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            }

            private static int CompareNumericStrings(string left, string right)
            {
                left = left.TrimStart('0');
                right = right.TrimStart('0');
                left = left.Length == 0 ? "0" : left;
                right = right.Length == 0 ? "0" : right;

                var lengthComparison = left.Length.CompareTo(right.Length);
                return lengthComparison != 0
                    ? lengthComparison
                    : string.Compare(left, right, StringComparison.Ordinal);
            }
        }
    }
}
