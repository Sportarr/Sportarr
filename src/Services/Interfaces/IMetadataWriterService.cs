using Sportarr.Api.Models;

namespace Sportarr.Api.Services.Interfaces;

/// <summary>
/// Writes local NFO metadata and poster/fanart images for media players that
/// scrape from disk rather than a network agent - currently Kodi. Every
/// method is a no-op when no enabled MetadataProvider applies, so callers can
/// invoke these unconditionally at their hook points.
/// </summary>
public interface IMetadataWriterService
{
    /// <summary>
    /// Writes the episode-level NFO (and thumb image, if enabled) for a
    /// single imported file. The NFO shares the video file's own basename,
    /// matching Kodi's local-scrape convention.
    /// </summary>
    Task WriteEventMetadataAsync(Event evt, EventFile file, League? league);

    /// <summary>
    /// Writes the league-level tvshow.nfo plus poster/banner images at the
    /// league's root folder. Idempotent - skips a rewrite when the source
    /// content hasn't changed.
    /// </summary>
    Task WriteLeagueMetadataAsync(League league);

    /// <summary>
    /// Removes metadata and subtitle sidecars for a deleted file.
    /// </summary>
    Task DeleteEventMetadataAsync(EventFile file, string? recycledVideoPath = null);

    /// <summary>
    /// Removes or recycles subtitles without touching the replacement video's metadata.
    /// </summary>
    Task DeleteSubtitleSidecarsAsync(string videoPath, string? recycledVideoPath = null);

    /// <summary>
    /// Moves metadata and subtitle sidecars with a renamed video file.
    /// </summary>
    Task RenameEventMetadataAsync(string oldVideoPath, string newVideoPath);
}
