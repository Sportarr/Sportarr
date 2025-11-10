namespace Sportarr.Api.Models;

/// <summary>
/// Delay profile for controlling when downloads are grabbed
/// Allows reducing number of downloads by waiting for better releases
/// Matches Sonarr/Radarr delay profile functionality
/// </summary>
public class DelayProfile
{
    public int Id { get; set; }

    /// <summary>
    /// Order/priority of this delay profile (1 = highest priority)
    /// </summary>
    public int Order { get; set; } = 1;

    /// <summary>
    /// Preferred download protocol (Usenet or Torrent)
    /// </summary>
    public string PreferredProtocol { get; set; } = "Usenet";

    /// <summary>
    /// Delay in minutes for Usenet downloads
    /// Timer starts from upload time reported by indexer
    /// </summary>
    public int UsenetDelay { get; set; } = 0;

    /// <summary>
    /// Delay in minutes for Torrent downloads
    /// Timer starts from upload time reported by indexer
    /// </summary>
    public int TorrentDelay { get; set; } = 0;

    /// <summary>
    /// Bypass delay if release has highest quality with preferred protocol
    /// </summary>
    public bool BypassIfHighestQuality { get; set; } = false;

    /// <summary>
    /// Bypass delay if release is first from preferred protocol
    /// </summary>
    public bool BypassIfAboveCustomFormatScore { get; set; } = false;

    /// <summary>
    /// Minimum custom format score to bypass delay
    /// Only used if BypassIfAboveCustomFormatScore is true
    /// </summary>
    public int MinimumCustomFormatScore { get; set; } = 0;

    /// <summary>
    /// Tags associated with this delay profile
    /// Empty = default profile for all events without specific tags
    /// </summary>
    public List<int> Tags { get; set; } = new();

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastModified { get; set; }
}
