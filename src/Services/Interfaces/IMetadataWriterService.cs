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
    /// Removes the NFO/thumb sidecars for a deleted file so Kodi never keeps
    /// scraping a ghost entry.
    /// </summary>
    Task DeleteEventMetadataAsync(EventFile file);

    /// <summary>
    /// Moves the NFO/thumb sidecars alongside a renamed video file. Kodi
    /// matches an NFO to its video by basename, so the sidecars must move
    /// with the file immediately rather than waiting for the next sync.
    /// </summary>
    Task RenameEventMetadataAsync(string oldVideoPath, string newVideoPath);
}
