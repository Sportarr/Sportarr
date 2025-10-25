namespace Fightarr.Api.Models;

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
    public bool FightCardNfo { get; set; } = false;

    // Image Settings - Download images for events and fighters
    public bool EventImages { get; set; } = true;
    public bool FighterImages { get; set; } = false;
    public bool OrganizationLogos { get; set; } = false;

    // Filename patterns (support tokens like {Event Title}, {Organization}, etc.)
    public string EventNfoFilename { get; set; } = "{Event Title}.nfo";
    public string EventPosterFilename { get; set; } = "poster.jpg";
    public string EventFanartFilename { get; set; } = "fanart.jpg";

    // Advanced settings
    public bool UseEventFolder { get; set; } = true;
    public int ImageQuality { get; set; } = 95; // JPEG quality 1-100

    public List<int> Tags { get; set; } = new();
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Metadata provider types for different media server formats
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
