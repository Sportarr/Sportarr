namespace Sportarr.Api.Models;

/// <summary>
/// Represents a connection to a media server (Plex, Jellyfin, or Emby) for library update notifications.
/// Similar to Sonarr/Radarr's Connections feature.
/// </summary>
public class MediaServerConnection
{
    public int Id { get; set; }

    /// <summary>
    /// Display name for this connection
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Type of media server: "Plex", "Jellyfin", or "Emby"
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// Server URL (e.g., http://localhost:32400 for Plex, http://localhost:8096 for Jellyfin)
    /// </summary>
    public string Url { get; set; } = "";

    /// <summary>
    /// API key or auth token.
    /// For Plex: X-Plex-Token
    /// For Jellyfin/Emby: API Key from admin dashboard
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Optional: Specific library section ID to update.
    /// If null, will attempt to auto-detect or update all libraries.
    /// </summary>
    public string? LibrarySectionId { get; set; }

    /// <summary>
    /// Optional: Library section name for display purposes
    /// </summary>
    public string? LibrarySectionName { get; set; }

    /// <summary>
    /// Path mapping: "from" path (what Sportarr sees).
    /// Used for Docker containers with different mount points.
    /// Example: "/sports"
    /// </summary>
    public string? PathMapFrom { get; set; }

    /// <summary>
    /// Path mapping: "to" path (what the media server sees).
    /// Example: "/data/media/sports"
    /// </summary>
    public string? PathMapTo { get; set; }

    /// <summary>
    /// Whether to trigger library updates on file import
    /// </summary>
    public bool UpdateLibrary { get; set; } = true;

    /// <summary>
    /// Whether to use partial scan (specific path) vs full library refresh.
    /// Partial scans are faster and preferred when supported.
    /// </summary>
    public bool UsePartialScan { get; set; } = true;

    /// <summary>
    /// Whether this connection is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Last time the connection was successfully tested
    /// </summary>
    public DateTime? LastTested { get; set; }

    /// <summary>
    /// Whether the last test/operation was successful
    /// </summary>
    public bool IsHealthy { get; set; } = true;

    /// <summary>
    /// Last error message if unhealthy
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Server name returned from the media server (for display)
    /// </summary>
    public string? ServerName { get; set; }

    /// <summary>
    /// Server version returned from the media server
    /// </summary>
    public string? ServerVersion { get; set; }

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime Modified { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a library section from a media server
/// </summary>
public class MediaServerLibrary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // "movie", "show", etc.
    public string? Path { get; set; }
}

/// <summary>
/// Result of testing a media server connection
/// </summary>
public class MediaServerTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? ServerName { get; set; }
    public string? ServerVersion { get; set; }
    public List<MediaServerLibrary> Libraries { get; set; } = new();
}
