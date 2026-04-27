using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class SystemUpdatesEndpoint
{
    public static IEndpointRouteBuilder MapSystemUpdatesEndpoint(this IEndpointRouteBuilder app)
    {
        // API: System Updates - Check for new versions from GitHub
        app.MapGet("/api/system/updates", async (ILogger<SystemUpdatesEndpoint> logger) =>
        {
            try
            {
                logger.LogInformation("[UPDATES] Checking for updates from GitHub");

                var currentVersion = Sportarr.Api.Version.GetFullVersion();

                logger.LogInformation("[UPDATES] Current version: {Version}", currentVersion);

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", $"Sportarr/{currentVersion}");
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                HttpResponseMessage response;
                try
                {
                    response = await httpClient.GetAsync("https://api.github.com/repos/Sportarr/Sportarr/releases");
                }
                catch (HttpRequestException ex)
                {
                    logger.LogError(ex, "[UPDATES] HTTP error connecting to GitHub API");
                    return Results.Problem($"Failed to connect to GitHub: {ex.Message}");
                }
                catch (TaskCanceledException ex)
                {
                    logger.LogError(ex, "[UPDATES] Request to GitHub API timed out");
                    return Results.Problem("GitHub API request timed out");
                }

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("[UPDATES] Failed to fetch releases from GitHub: {StatusCode}", response.StatusCode);
                    return Results.Problem("Failed to fetch updates from GitHub");
                }

                var json = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(json))
                {
                    logger.LogWarning("[UPDATES] GitHub returned empty response");
                    return Results.Ok(new
                    {
                        updateAvailable = false,
                        currentVersion,
                        latestVersion = currentVersion,
                        releases = new List<object>()
                    });
                }

                JsonElement releases;
                try
                {
                    releases = JsonSerializer.Deserialize<JsonElement>(json);
                }
                catch (JsonException ex)
                {
                    logger.LogError(ex, "[UPDATES] Failed to parse GitHub response");
                    return Results.Problem("Failed to parse GitHub response");
                }

                if (releases.ValueKind != JsonValueKind.Array)
                {
                    logger.LogWarning("[UPDATES] GitHub response is not an array: {Kind}", releases.ValueKind);
                    if (releases.TryGetProperty("message", out var messageElement))
                    {
                        var errorMessage = messageElement.GetString();
                        logger.LogWarning("[UPDATES] GitHub error: {Message}", errorMessage);
                        return Results.Problem($"GitHub API error: {errorMessage}");
                    }
                    return Results.Ok(new
                    {
                        updateAvailable = false,
                        currentVersion,
                        latestVersion = currentVersion,
                        releases = new List<object>()
                    });
                }

                var releaseList = new List<object>();
                string? latestVersion = null;

                foreach (var release in releases.EnumerateArray())
                {
                    var tagName = release.GetProperty("tag_name").GetString() ?? "";
                    var version = tagName.TrimStart('v');
                    var publishedAt = release.GetProperty("published_at").GetString() ?? DateTime.UtcNow.ToString();
                    var body = release.GetProperty("body").GetString() ?? "";
                    var htmlUrl = release.GetProperty("html_url").GetString() ?? "";
                    var isDraft = release.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean();
                    var isPrerelease = release.TryGetProperty("prerelease", out var prereleaseProp) && prereleaseProp.GetBoolean();

                    if (isDraft || isPrerelease)
                    {
                        continue;
                    }

                    if (latestVersion == null)
                    {
                        latestVersion = version;
                    }

                    var changes = new List<string>();
                    if (!string.IsNullOrEmpty(body))
                    {
                        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                            {
                                continue;
                            }
                            if (trimmed.StartsWith("-") || trimmed.StartsWith("*"))
                            {
                                changes.Add(trimmed.TrimStart('-', '*').Trim());
                            }
                            else if (changes.Count < 10)
                            {
                                changes.Add(trimmed);
                            }
                        }
                    }

                    var currentParts = currentVersion.Split('.');
                    var currentBase = currentParts.Length >= 3 ? $"{currentParts[0]}.{currentParts[1]}.{currentParts[2]}" : currentVersion;
                    var isInstalled = version == currentBase || version == currentVersion;

                    releaseList.Add(new
                    {
                        version,
                        releaseDate = publishedAt,
                        branch = "main",
                        changes = changes.Take(10).ToList(),
                        downloadUrl = htmlUrl,
                        isInstalled,
                        isLatest = version == latestVersion
                    });

                    if (releaseList.Count >= 10)
                    {
                        break;
                    }
                }

                var updateAvailable = false;
                if (latestVersion != null)
                {
                    var currentParts = currentVersion.Split('.');
                    var currentBase = currentParts.Length >= 3 ? $"{currentParts[0]}.{currentParts[1]}.{currentParts[2]}" : currentVersion;

                    updateAvailable = latestVersion != currentBase && latestVersion != currentVersion;
                }

                logger.LogInformation("[UPDATES] Current: {Current}, Latest: {Latest}, Available: {Available}",
                    currentVersion, latestVersion ?? "unknown", updateAvailable);

                return Results.Ok(new
                {
                    updateAvailable,
                    currentVersion,
                    latestVersion = latestVersion ?? currentVersion,
                    releases = releaseList
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[UPDATES] Error checking for updates");
                return Results.Problem("Error checking for updates: " + ex.Message);
            }
        });

        return app;
    }
}
