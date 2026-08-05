namespace Sportarr.Api.Models;

/// <summary>
/// Metadata provider for generating NFO files and downloading images for media servers
/// Supports Kodi, Plex, Emby, Jellyfin, and WDTV formats
/// </summary>
public class MetadataProvider
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MetadataType Type { get; set; } = MetadataType.Kodi;
    public bool Enabled { get; set; } = true;

    // NFO Settings - Generate XML metadata files
    public bool EventNfo { get; set; } = true;
    public bool EventCardNfo { get; set; } = false;

    /// <summary>
    /// Writes the league-level tvshow.nfo. Split from EventNfo because a
    /// user may want per-episode NFOs without a show-level one, or vice
    /// versa - there was previously no way to control these independently.
    /// </summary>
    public bool ShowNfo { get; set; } = true;

    // Image Settings - Download images for events and players
    public bool EventImages { get; set; } = true;
    public bool PlayerImages { get; set; } = false;
    public bool LeagueLogos { get; set; } = false;

    // League-root image filenames. These are Kodi's own naming convention
    // (poster.jpg/fanart.jpg at the show root) - changing them isn't
    // recommended, Kodi's local scraper looks for these exact names.
    public string EventPosterFilename { get; set; } = "poster.jpg";
    public string EventFanartFilename { get; set; } = "fanart.jpg";

    // Advanced settings
    /// <summary>
    /// Whether to nest each event's video (and its sidecar NFO/thumb) in its
    /// own subfolder. Sportarr's actual file layout is flat files directly
    /// under "{Series}/Season {year}/" - defaulting this true would disagree
    /// with what the renamer actually writes to disk.
    /// </summary>
    public bool UseEventFolder { get; set; } = false;
    public int ImageQuality { get; set; } = 95; // JPEG quality 1-100 - reserved, not yet implemented (no image re-encoding in this codebase)

    public List<int> Tags { get; set; } = new();
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Metadata provider types for different media server formats. Only Kodi is
/// implemented by MetadataWriterService today - Plex/Jellyfin/Emby have
/// their own dedicated agents (see agents/) instead of local NFO files, and
/// WDTV has no writer at all. The other values stay defined so the schema
/// doesn't need another migration if one of them gets a writer later, but
/// the UI only lets a user create a Kodi provider for now.
/// </summary>
public enum MetadataType
{
    /// <summary>
    /// Kodi/XBMC NFO format - Most common, works with Kodi media center
    /// </summary>
    Kodi = 0,

    /// <summary>
    /// Plex-compatible metadata format
    /// </summary>
    Plex = 1,

    /// <summary>
    /// Emby-compatible metadata format
    /// </summary>
    Emby = 2,

    /// <summary>
    /// Jellyfin-compatible metadata format (similar to Emby)
    /// </summary>
    Jellyfin = 3,

    /// <summary>
    /// WDTV metadata format for WDTV media players
    /// </summary>
    WDTV = 4
}
