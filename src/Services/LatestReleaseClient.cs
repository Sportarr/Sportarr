using System.Runtime.InteropServices;
using System.Text.Json;

namespace Sportarr.Api.Services;

/// <summary>
/// One file of a release. Sha256 is the hex digest that GitHub reports, or
/// empty when GitHub does not report one.
/// </summary>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size = 0, string Sha256 = "");

/// <summary>
/// The latest Sportarr release on GitHub. Version has no leading "v".
/// </summary>
public sealed record LatestRelease(string Version, string PageUrl, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>
/// Reads the latest Sportarr release from GitHub. The health check and the
/// update script use the same request and the same version rule.
/// </summary>
public static class LatestReleaseClient
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/Sportarr/Sportarr/releases/latest";

    // TEST ONLY. The end-to-end update test sets this environment variable
    // to point the check at a local server instead of GitHub. Only the
    // process environment sets it. No setting, API or page can change it.
    public const string ApiBaseOverrideVariable = "SPORTARR_UPDATE_API_BASE";

    /// <summary>
    /// A package must come over HTTPS. Only the test server, which the
    /// variable above names, may use plain HTTP.
    /// </summary>
    public static bool DownloadUrlAllowed(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiBaseOverrideVariable));

    public static string ResolveLatestReleaseUrl()
    {
        var overrideBase = Environment.GetEnvironmentVariable(ApiBaseOverrideVariable);
        return string.IsNullOrWhiteSpace(overrideBase)
            ? LatestReleaseApiUrl
            : overrideBase.TrimEnd('/') + "/repos/Sportarr/Sportarr/releases/latest";
    }
    public const string HttpClientName = "TrashGuides"; // This client already sends a User-Agent that GitHub accepts.

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Returns null when GitHub cannot be reached or answers with an error.
    /// A GitHub outage is not a Sportarr problem, so the caller skips the
    /// check and tries again on the next pass.
    /// </summary>
    public static async Task<LatestRelease?> FetchAsync(
        IHttpClientFactory httpClientFactory, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(RequestTimeout);
            using var response = await httpClient.GetAsync(ResolveLatestReleaseUrl(), cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Update check skipped. GitHub answered {Status}", (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            return Parse(json);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Update check skipped. GitHub could not be reached");
            return null;
        }
    }

    internal static LatestRelease? Parse(string json)
    {
        var release = JsonSerializer.Deserialize<JsonElement>(json);
        if (release.ValueKind != JsonValueKind.Object)
            return null;

        var tagName = release.TryGetProperty("tag_name", out var tagProp) && tagProp.ValueKind == JsonValueKind.String
            ? tagProp.GetString()
            : null;
        var version = tagName?.TrimStart('v', 'V');
        if (string.IsNullOrEmpty(version))
            return null;

        var pageUrl = release.TryGetProperty("html_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
            ? urlProp.GetString() ?? ""
            : "";

        var assets = new List<ReleaseAsset>();
        if (release.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsProp.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                var size = asset.TryGetProperty("size", out var z) && z.ValueKind == JsonValueKind.Number ? z.GetInt64() : 0;
                var digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
                var sha256 = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : "";
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                    assets.Add(new ReleaseAsset(name, url, size, sha256));
            }
        }

        return new LatestRelease(version, pageUrl, assets);
    }

    /// <summary>
    /// True when the release is newer than the running version. Only the
    /// first three parts count, as in the health check.
    /// </summary>
    public static bool IsNewer(string latestVersion, string currentVersion)
    {
        return System.Version.TryParse(NormalizeToThreePart(latestVersion), out var latest) &&
               System.Version.TryParse(NormalizeToThreePart(currentVersion), out var current) &&
               latest > current;
    }

    /// <summary>
    /// A tag such as "4.2" or "4.1.12.1122" does not always have three
    /// parts. System.Version compares missing parts as -1, so pad or cut to
    /// three.
    /// </summary>
    public static string NormalizeToThreePart(string versionText)
    {
        var parts = versionText.Split('.');
        return parts.Length >= 3
            ? string.Join('.', parts[..3])
            : string.Join('.', parts.Concat(Enumerable.Repeat("0", 3 - parts.Length)));
    }

    /// <summary>
    /// Finds the release archive for this computer. Release assets are named
    /// Sportarr-{os}-{arch}-{version} with .zip on Windows and .tar.gz
    /// elsewhere. Returns an empty string when no asset matches.
    /// </summary>
    public static string SelectDownloadUrl(IEnumerable<ReleaseAsset> assets, string? os, string? arch) =>
        SelectAsset(assets, os, arch)?.DownloadUrl ?? "";

    public static ReleaseAsset? SelectAsset(IEnumerable<ReleaseAsset> assets, string? os, string? arch)
    {
        if (string.IsNullOrEmpty(os) || string.IsNullOrEmpty(arch))
            return null;

        var prefix = $"Sportarr-{os}-{arch}-";
        var extension = os == "win" ? ".zip" : ".tar.gz";
        return assets.FirstOrDefault(a =>
            a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    public static string? CurrentOs()
    {
        if (OperatingSystem.IsWindows()) return "win";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "osx";
        return null;
    }

    public static string? CurrentArch() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        _ => null
    };
}
