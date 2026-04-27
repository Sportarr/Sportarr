using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;

namespace Sportarr.Api.Helpers;

public static class PluginDownloadHelper
{
    public static async Task<string?> GetPluginDownloadUrlAsync(string pluginType, ILogger logger)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", $"Sportarr/{Sportarr.Api.Version.GetFullVersion()}");
            httpClient.Timeout = TimeSpan.FromSeconds(15);

            var response = await httpClient.GetAsync("https://api.github.com/repos/Sportarr/Sportarr/releases/latest");
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to fetch latest release from GitHub: {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<JsonElement>(json);

            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            var assetPrefix = $"sportarr-{pluginType}-plugin_";
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.StartsWith(assetPrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var browserDownloadUrl = asset.GetProperty("browser_download_url").GetString();
                    logger.LogInformation("Found {PluginType} plugin asset: {Name} -> {Url}", pluginType, name, browserDownloadUrl);
                    return browserDownloadUrl;
                }
            }

            logger.LogWarning("No {PluginType} plugin asset found in latest release", pluginType);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get {PluginType} plugin download URL from GitHub", pluginType);
            return null;
        }
    }
}
