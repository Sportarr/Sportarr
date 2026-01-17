using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for managing media server connections (Plex, Jellyfin, Emby) and triggering library updates.
/// Similar to Sonarr/Radarr's Connections feature for media server notifications.
/// </summary>
public class MediaServerService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MediaServerService> _logger;

    public MediaServerService(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<MediaServerService> logger)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Test a media server connection and retrieve server information
    /// </summary>
    public async Task<MediaServerTestResult> TestConnectionAsync(MediaServerConnection connection)
    {
        return connection.Type.ToLowerInvariant() switch
        {
            "plex" => await TestPlexConnectionAsync(connection),
            "jellyfin" => await TestJellyfinConnectionAsync(connection),
            "emby" => await TestEmbyConnectionAsync(connection),
            _ => new MediaServerTestResult
            {
                Success = false,
                Message = $"Unknown media server type: {connection.Type}"
            }
        };
    }

    /// <summary>
    /// Get available libraries from a media server
    /// </summary>
    public async Task<List<MediaServerLibrary>> GetLibrariesAsync(MediaServerConnection connection)
    {
        return connection.Type.ToLowerInvariant() switch
        {
            "plex" => await GetPlexLibrariesAsync(connection),
            "jellyfin" => await GetJellyfinLibrariesAsync(connection),
            "emby" => await GetEmbyLibrariesAsync(connection),
            _ => new List<MediaServerLibrary>()
        };
    }

    /// <summary>
    /// Trigger a library refresh on the media server.
    /// If path is provided and UsePartialScan is enabled, only that path will be scanned.
    /// </summary>
    public async Task<bool> RefreshLibraryAsync(MediaServerConnection connection, string? path = null)
    {
        if (!connection.Enabled || !connection.UpdateLibrary)
        {
            _logger.LogDebug("[MediaServer] Skipping refresh for {Name} - disabled or UpdateLibrary=false", connection.Name);
            return true;
        }

        return connection.Type.ToLowerInvariant() switch
        {
            "plex" => await RefreshPlexLibraryAsync(connection, path),
            "jellyfin" => await RefreshJellyfinLibraryAsync(connection, path),
            "emby" => await RefreshEmbyLibraryAsync(connection, path),
            _ => false
        };
    }

    /// <summary>
    /// Notify all enabled media server connections about an imported file.
    /// Called after successful file import.
    /// </summary>
    public async Task NotifyImportAsync(int eventId, string filePath)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        var connections = await db.MediaServerConnections
            .Where(c => c.Enabled && c.UpdateLibrary)
            .ToListAsync();

        if (!connections.Any())
        {
            _logger.LogDebug("[MediaServer] No enabled media server connections configured");
            return;
        }

        _logger.LogInformation("[MediaServer] Notifying {Count} media server(s) about imported file: {Path}",
            connections.Count, filePath);

        foreach (var connection in connections)
        {
            try
            {
                // Apply path mapping if configured
                var serverPath = MapPath(filePath, connection);

                var success = await RefreshLibraryAsync(connection, serverPath);

                // Update connection health status
                connection.LastTested = DateTime.UtcNow;
                connection.IsHealthy = success;
                connection.LastError = success ? null : "Failed to trigger library refresh";
                connection.Modified = DateTime.UtcNow;

                if (success)
                {
                    _logger.LogInformation("[MediaServer] Successfully notified {Name} ({Type}) for path: {Path}",
                        connection.Name, connection.Type, serverPath);
                }
                else
                {
                    _logger.LogWarning("[MediaServer] Failed to notify {Name} ({Type}) for path: {Path}",
                        connection.Name, connection.Type, serverPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MediaServer] Error notifying {Name} ({Type}): {Error}",
                    connection.Name, connection.Type, ex.Message);

                connection.LastTested = DateTime.UtcNow;
                connection.IsHealthy = false;
                connection.LastError = ex.Message;
                connection.Modified = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Map a local path to the media server's path using PathMapFrom/PathMapTo settings.
    /// Returns the original path if no mapping is configured.
    /// </summary>
    private string MapPath(string localPath, MediaServerConnection connection)
    {
        if (string.IsNullOrEmpty(connection.PathMapFrom) || string.IsNullOrEmpty(connection.PathMapTo))
        {
            return localPath;
        }

        var fromPath = connection.PathMapFrom.TrimEnd('/', '\\');
        var toPath = connection.PathMapTo.TrimEnd('/', '\\');

        // Normalize path separators for comparison
        var normalizedLocal = localPath.Replace('\\', '/');
        var normalizedFrom = fromPath.Replace('\\', '/');

        if (normalizedLocal.StartsWith(normalizedFrom, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = normalizedLocal.Substring(normalizedFrom.Length);
            var mappedPath = toPath + relativePath;

            _logger.LogDebug("[MediaServer] Mapped path: {Local} -> {Server}", localPath, mappedPath);
            return mappedPath;
        }

        return localPath;
    }

    #region Plex

    private async Task<MediaServerTestResult> TestPlexConnectionAsync(MediaServerConnection connection)
    {
        var result = new MediaServerTestResult();

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = BuildPlexUrl(connection, "/");

            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var doc = XDocument.Parse(content);
                var mediaContainer = doc.Root;

                result.Success = true;
                result.ServerName = mediaContainer?.Attribute("friendlyName")?.Value ?? "Plex Server";
                result.ServerVersion = mediaContainer?.Attribute("version")?.Value;
                result.Message = $"Connected to {result.ServerName}";

                // Get libraries
                result.Libraries = await GetPlexLibrariesAsync(connection);
            }
            else
            {
                result.Success = false;
                result.Message = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Authentication failed - check your Plex token"
                    : $"Connection failed: {response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Connection error: {ex.Message}";
            _logger.LogError(ex, "[MediaServer] Plex connection test failed");
        }

        return result;
    }

    private async Task<List<MediaServerLibrary>> GetPlexLibrariesAsync(MediaServerConnection connection)
    {
        var libraries = new List<MediaServerLibrary>();

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = BuildPlexUrl(connection, "/library/sections");

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[MediaServer] Failed to get Plex libraries: {Status}", response.StatusCode);
                return libraries;
            }

            var content = await response.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(content);

            foreach (var directory in doc.Descendants("Directory"))
            {
                libraries.Add(new MediaServerLibrary
                {
                    Id = directory.Attribute("key")?.Value ?? "",
                    Name = directory.Attribute("title")?.Value ?? "",
                    Type = directory.Attribute("type")?.Value ?? "",
                    Path = directory.Element("Location")?.Attribute("path")?.Value
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MediaServer] Error getting Plex libraries");
        }

        return libraries;
    }

    private async Task<bool> RefreshPlexLibraryAsync(MediaServerConnection connection, string? path)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();

            // If a specific library section is configured, use it
            // Otherwise, refresh all sections
            var sectionId = connection.LibrarySectionId;

            if (string.IsNullOrEmpty(sectionId))
            {
                // Try to find a matching section based on path
                var libraries = await GetPlexLibrariesAsync(connection);
                if (!string.IsNullOrEmpty(path))
                {
                    var matchingLib = libraries.FirstOrDefault(l =>
                        !string.IsNullOrEmpty(l.Path) &&
                        path.StartsWith(l.Path, StringComparison.OrdinalIgnoreCase));

                    if (matchingLib != null)
                    {
                        sectionId = matchingLib.Id;
                        _logger.LogDebug("[MediaServer] Auto-detected Plex library section {Id} ({Name}) for path {Path}",
                            sectionId, matchingLib.Name, path);
                    }
                }

                // If still no section found, refresh all
                if (string.IsNullOrEmpty(sectionId))
                {
                    _logger.LogDebug("[MediaServer] No specific library section configured, refreshing all Plex libraries");
                    foreach (var lib in libraries.Where(l => l.Type == "show" || l.Type == "movie"))
                    {
                        await RefreshPlexSectionAsync(client, connection, lib.Id, null);
                    }
                    return true;
                }
            }

            return await RefreshPlexSectionAsync(client, connection, sectionId, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MediaServer] Error refreshing Plex library");
            return false;
        }
    }

    private async Task<bool> RefreshPlexSectionAsync(HttpClient client, MediaServerConnection connection, string sectionId, string? path)
    {
        string url;

        if (!string.IsNullOrEmpty(path) && connection.UsePartialScan)
        {
            // Partial scan - only scan the specific path (faster)
            var encodedPath = HttpUtility.UrlEncode(path);
            url = BuildPlexUrl(connection, $"/library/sections/{sectionId}/refresh?path={encodedPath}");
            _logger.LogInformation("[MediaServer] Triggering Plex partial scan for section {Section} path: {Path}", sectionId, path);
        }
        else
        {
            // Full section scan
            url = BuildPlexUrl(connection, $"/library/sections/{sectionId}/refresh");
            _logger.LogInformation("[MediaServer] Triggering Plex full scan for section {Section}", sectionId);
        }

        var response = await client.GetAsync(url);
        return response.IsSuccessStatusCode;
    }

    private string BuildPlexUrl(MediaServerConnection connection, string endpoint)
    {
        var baseUrl = connection.Url.TrimEnd('/');
        var separator = endpoint.Contains('?') ? '&' : '?';
        return $"{baseUrl}{endpoint}{separator}X-Plex-Token={connection.ApiKey}";
    }

    #endregion

    #region Jellyfin

    private async Task<MediaServerTestResult> TestJellyfinConnectionAsync(MediaServerConnection connection)
    {
        var result = new MediaServerTestResult();

        try
        {
            var client = CreateJellyfinClient(connection);
            var url = $"{connection.Url.TrimEnd('/')}/System/Info";

            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var info = JsonSerializer.Deserialize<JsonElement>(content);

                result.Success = true;
                result.ServerName = info.TryGetProperty("ServerName", out var name) ? name.GetString() : "Jellyfin Server";
                result.ServerVersion = info.TryGetProperty("Version", out var version) ? version.GetString() : null;
                result.Message = $"Connected to {result.ServerName}";

                // Get libraries
                result.Libraries = await GetJellyfinLibrariesAsync(connection);
            }
            else
            {
                result.Success = false;
                result.Message = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Authentication failed - check your API key"
                    : $"Connection failed: {response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Connection error: {ex.Message}";
            _logger.LogError(ex, "[MediaServer] Jellyfin connection test failed");
        }

        return result;
    }

    private async Task<List<MediaServerLibrary>> GetJellyfinLibrariesAsync(MediaServerConnection connection)
    {
        var libraries = new List<MediaServerLibrary>();

        try
        {
            var client = CreateJellyfinClient(connection);
            var url = $"{connection.Url.TrimEnd('/')}/Library/VirtualFolders";

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[MediaServer] Failed to get Jellyfin libraries: {Status}", response.StatusCode);
                return libraries;
            }

            var content = await response.Content.ReadAsStringAsync();
            var folders = JsonSerializer.Deserialize<JsonElement>(content);

            if (folders.ValueKind == JsonValueKind.Array)
            {
                foreach (var folder in folders.EnumerateArray())
                {
                    var id = folder.TryGetProperty("ItemId", out var itemId) ? itemId.GetString() : "";
                    var name = folder.TryGetProperty("Name", out var nameEl) ? nameEl.GetString() : "";
                    var collectionType = folder.TryGetProperty("CollectionType", out var type) ? type.GetString() : "";

                    // Get first path from Locations array
                    string? path = null;
                    if (folder.TryGetProperty("Locations", out var locations) && locations.ValueKind == JsonValueKind.Array)
                    {
                        var firstLoc = locations.EnumerateArray().FirstOrDefault();
                        if (firstLoc.ValueKind == JsonValueKind.String)
                        {
                            path = firstLoc.GetString();
                        }
                    }

                    libraries.Add(new MediaServerLibrary
                    {
                        Id = id ?? "",
                        Name = name ?? "",
                        Type = collectionType ?? "",
                        Path = path
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MediaServer] Error getting Jellyfin libraries");
        }

        return libraries;
    }

    private async Task<bool> RefreshJellyfinLibraryAsync(MediaServerConnection connection, string? path)
    {
        try
        {
            var client = CreateJellyfinClient(connection);
            var baseUrl = connection.Url.TrimEnd('/');

            if (!string.IsNullOrEmpty(path) && connection.UsePartialScan)
            {
                // Partial scan - notify about specific path change
                var payload = new
                {
                    Updates = new[]
                    {
                        new { Path = path, UpdateType = "Created" }
                    }
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                var url = $"{baseUrl}/Library/Media/Updated";
                _logger.LogInformation("[MediaServer] Triggering Jellyfin partial scan for path: {Path}", path);

                var response = await client.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            else
            {
                // Full library refresh
                var url = $"{baseUrl}/Library/Refresh";
                _logger.LogInformation("[MediaServer] Triggering Jellyfin full library refresh");

                var response = await client.PostAsync(url, null);
                return response.IsSuccessStatusCode;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MediaServer] Error refreshing Jellyfin library");
            return false;
        }
    }

    private HttpClient CreateJellyfinClient(MediaServerConnection connection)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-MediaBrowser-Token", connection.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    #endregion

    #region Emby

    private async Task<MediaServerTestResult> TestEmbyConnectionAsync(MediaServerConnection connection)
    {
        var result = new MediaServerTestResult();

        try
        {
            var client = CreateEmbyClient(connection);
            var url = $"{connection.Url.TrimEnd('/')}/emby/System/Info";

            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var info = JsonSerializer.Deserialize<JsonElement>(content);

                result.Success = true;
                result.ServerName = info.TryGetProperty("ServerName", out var name) ? name.GetString() : "Emby Server";
                result.ServerVersion = info.TryGetProperty("Version", out var version) ? version.GetString() : null;
                result.Message = $"Connected to {result.ServerName}";

                // Get libraries
                result.Libraries = await GetEmbyLibrariesAsync(connection);
            }
            else
            {
                result.Success = false;
                result.Message = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Authentication failed - check your API key"
                    : $"Connection failed: {response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Connection error: {ex.Message}";
            _logger.LogError(ex, "[MediaServer] Emby connection test failed");
        }

        return result;
    }

    private async Task<List<MediaServerLibrary>> GetEmbyLibrariesAsync(MediaServerConnection connection)
    {
        var libraries = new List<MediaServerLibrary>();

        try
        {
            var client = CreateEmbyClient(connection);
            var url = $"{connection.Url.TrimEnd('/')}/emby/Library/VirtualFolders";

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[MediaServer] Failed to get Emby libraries: {Status}", response.StatusCode);
                return libraries;
            }

            var content = await response.Content.ReadAsStringAsync();
            var folders = JsonSerializer.Deserialize<JsonElement>(content);

            if (folders.ValueKind == JsonValueKind.Array)
            {
                foreach (var folder in folders.EnumerateArray())
                {
                    var id = folder.TryGetProperty("ItemId", out var itemId) ? itemId.GetString() : "";
                    var name = folder.TryGetProperty("Name", out var nameEl) ? nameEl.GetString() : "";
                    var collectionType = folder.TryGetProperty("CollectionType", out var type) ? type.GetString() : "";

                    // Get first path from Locations array
                    string? path = null;
                    if (folder.TryGetProperty("Locations", out var locations) && locations.ValueKind == JsonValueKind.Array)
                    {
                        var firstLoc = locations.EnumerateArray().FirstOrDefault();
                        if (firstLoc.ValueKind == JsonValueKind.String)
                        {
                            path = firstLoc.GetString();
                        }
                    }

                    libraries.Add(new MediaServerLibrary
                    {
                        Id = id ?? "",
                        Name = name ?? "",
                        Type = collectionType ?? "",
                        Path = path
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MediaServer] Error getting Emby libraries");
        }

        return libraries;
    }

    private async Task<bool> RefreshEmbyLibraryAsync(MediaServerConnection connection, string? path)
    {
        try
        {
            var client = CreateEmbyClient(connection);
            var baseUrl = connection.Url.TrimEnd('/');

            if (!string.IsNullOrEmpty(path) && connection.UsePartialScan)
            {
                // Partial scan - notify about specific path change
                var payload = new
                {
                    Updates = new[]
                    {
                        new { Path = path, UpdateType = "Created" }
                    }
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                var url = $"{baseUrl}/emby/Library/Media/Updated";
                _logger.LogInformation("[MediaServer] Triggering Emby partial scan for path: {Path}", path);

                var response = await client.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            else
            {
                // Full library refresh
                var url = $"{baseUrl}/emby/Library/Refresh";
                _logger.LogInformation("[MediaServer] Triggering Emby full library refresh");

                var response = await client.PostAsync(url, null);
                return response.IsSuccessStatusCode;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MediaServer] Error refreshing Emby library");
            return false;
        }
    }

    private HttpClient CreateEmbyClient(MediaServerConnection connection)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-MediaBrowser-Token", connection.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    #endregion
}
