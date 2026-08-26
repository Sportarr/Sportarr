using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Services;
using System.Net.Http;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class SystemUpdatesEndpoint
{
    public static IEndpointRouteBuilder MapSystemUpdatesEndpoint(this IEndpointRouteBuilder app)
    {
        // API: System Updates - Check for new versions from GitHub
        app.MapGet("/api/system/updates", async (ConfigService configService, IHttpClientFactory httpClientFactory, ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("[UPDATES] Checking for updates from GitHub");

                var currentVersion = Sportarr.Api.Version.GetFullVersion();

                // Update channel: "main" sees stable releases only;
                // "develop" also surfaces prereleases.
                var updateBranch = (await configService.GetConfigAsync()).Branch;
                var includePrereleases = string.Equals(updateBranch, "develop", StringComparison.OrdinalIgnoreCase);

                logger.LogInformation("[UPDATES] Current version: {Version}", currentVersion);

                // Factory-pooled named client (UA + timeout configured at
                // registration) - a fresh HttpClient per update check was
                // the classic socket-exhaustion antipattern.
                var httpClient = httpClientFactory.CreateClient("GitHub");

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

                    // Rolling-tag releases (the "dev" prerelease is re-published
                    // on every dev push) carry no version in the tag, which made
                    // the check report "Latest Version: dev" and a permanent
                    // false update. The real build number is in the asset names
                    // (Sportarr-win-x64-4.0.1024.706-dev.zip) - resolve it from
                    // there, falling back to the release body.
                    if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+(\.\d+)+$"))
                    {
                        string? resolved = null;
                        if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var asset in assets.EnumerateArray())
                            {
                                var assetName = asset.GetProperty("name").GetString() ?? "";
                                var m = System.Text.RegularExpressions.Regex.Match(assetName, @"\d+\.\d+\.\d+(\.\d+)?");
                                if (m.Success) { resolved = m.Value; break; }
                            }
                        }
                        if (resolved == null)
                        {
                            var bodyMatch = System.Text.RegularExpressions.Regex.Match(
                                release.GetProperty("body").GetString() ?? "", @"\d+\.\d+\.\d+(\.\d+)?");
                            if (bodyMatch.Success) resolved = bodyMatch.Value;
                        }
                        if (resolved != null) version = resolved;
                    }
                    var publishedAt = release.GetProperty("published_at").GetString() ?? DateTime.UtcNow.ToString();
                    var body = release.GetProperty("body").GetString() ?? "";
                    var htmlUrl = release.GetProperty("html_url").GetString() ?? "";
                    var isDraft = release.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean();
                    var isPrerelease = release.TryGetProperty("prerelease", out var prereleaseProp) && prereleaseProp.GetBoolean();

                    if (isDraft || (isPrerelease && !includePrereleases))
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
                    // Compare the numbers, not the spelling. "4.1.0" and
                    // "4.1.0.0" are the same build, and a string test called
                    // the running release not installed whenever the two sides
                    // wrote the same version with a different number of parts.
                    var isInstalled = version == currentBase || version == currentVersion;
                    if (!isInstalled &&
                        System.Version.TryParse(version, out var releaseParsed) &&
                        (System.Version.TryParse(currentVersion, out var runningParsed) ||
                         System.Version.TryParse(currentBase, out runningParsed)))
                    {
                        isInstalled = releaseParsed == runningParsed;
                    }

                    releaseList.Add(new
                    {
                        version,
                        releaseDate = publishedAt,
                        branch = isPrerelease ? "develop" : "main",
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

                    // Numeric comparison when both sides parse: "newer" is what
                    // makes an update, not merely "different" - a rolling dev
                    // build equal to the running version is up to date, and a
                    // user running ahead of the last published build must not
                    // be told to "update" backwards.
                    if (System.Version.TryParse(latestVersion, out var latestParsed) &&
                        (System.Version.TryParse(currentVersion, out var currentParsed) ||
                         System.Version.TryParse(currentBase, out currentParsed)))
                    {
                        updateAvailable = latestParsed > currentParsed;
                    }
                    else
                    {
                        updateAvailable = latestVersion != currentBase && latestVersion != currentVersion;
                    }
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
