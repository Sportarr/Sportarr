using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Services;

/// <summary>
/// Writes Kodi-native local NFO files and poster/fanart images alongside
/// imported video files. Zero network calls back to Sportarr are needed on
/// Kodi's side - it reads these files directly during its own local scrape,
/// the same way Sonarr/Radarr's XbmcMetadata consumer works.
///
/// Deliberately excludes the &lt;episodeguide&gt;&lt;url&gt; tag some *arr
/// implementations write: Kodi's local-only scraper tries to resolve that
/// URL online and the failed lookup corrupts the local library entry
/// (confirmed pattern in Sonarr/Radarr issue trackers). Sportarr's Kodi
/// Connect notification (NotificationService.SendKodiAsync) is the intended
/// way to tell Kodi about new content, not an online episode guide URL.
/// </summary>
public class MetadataWriterService : IMetadataWriterService
{
    private readonly SportarrDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MetadataWriterService> _logger;

    private const string SourceMarkerSuffix = ".sportarr-source";

    public MetadataWriterService(SportarrDbContext db, IHttpClientFactory httpClientFactory, ILogger<MetadataWriterService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task WriteEventMetadataAsync(Event evt, EventFile file, League? league)
    {
        var providers = await GetApplicableProvidersAsync(league);
        if (providers.Count == 0) return;

        if (!evt.EpisodeNumber.HasValue)
        {
            _logger.LogDebug("[Metadata] Skipping NFO for '{Title}' - no episode number assigned yet", evt.Title);
            return;
        }

        foreach (var provider in providers)
        {
            try
            {
                if (provider.EventNfo)
                {
                    await WriteEventNfoAsync(evt, file, provider);
                }

                if (provider.EventImages)
                {
                    var thumbUrl = evt.ThumbUrl ?? evt.PosterUrl;
                    if (!string.IsNullOrEmpty(thumbUrl))
                    {
                        var thumbPath = Path.ChangeExtension(file.FilePath, null) + "-thumb.jpg";
                        await DownloadIfChangedAsync(thumbUrl, thumbPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Metadata] Failed to write event metadata for '{Title}' via provider '{Provider}'", evt.Title, provider.Name);
            }
        }
    }

    public async Task WriteLeagueMetadataAsync(League league)
    {
        var providers = await GetApplicableProvidersAsync(league);
        if (providers.Count == 0) return;

        // No dedicated "league root path" is stored anywhere - derive it from
        // any file already on disk for this league. Files sit flat under
        // "{Series}/Season {year}/", so the league folder is the season
        // folder's parent, or the file's own directory when season folders
        // are disabled. Nothing to write into before the first file lands.
        var sampleFile = await _db.EventFiles
            .Where(f => f.Exists && f.Event != null && f.Event.LeagueId == league.Id)
            .Select(f => f.FilePath)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(sampleFile)) return;

        var dir = Path.GetDirectoryName(sampleFile);
        if (!string.IsNullOrEmpty(dir) && Path.GetFileName(dir).StartsWith("Season ", StringComparison.OrdinalIgnoreCase))
        {
            dir = Path.GetDirectoryName(dir);
        }

        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        foreach (var provider in providers)
        {
            try
            {
                if (provider.ShowNfo)
                {
                    await WriteShowNfoAsync(league, dir, provider);
                }

                if (provider.LeagueLogos)
                {
                    var posterUrl = league.PosterUrl ?? league.LogoUrl;
                    if (!string.IsNullOrEmpty(posterUrl))
                    {
                        await DownloadIfChangedAsync(posterUrl, Path.Combine(dir, provider.EventPosterFilename));
                    }

                    if (!string.IsNullOrEmpty(league.BannerUrl))
                    {
                        await DownloadIfChangedAsync(league.BannerUrl, Path.Combine(dir, provider.EventFanartFilename));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Metadata] Failed to write league metadata for '{League}' via provider '{Provider}'", league.Name, provider.Name);
            }
        }
    }

    public Task DeleteEventMetadataAsync(EventFile file)
    {
        try
        {
            var nfoPath = Path.ChangeExtension(file.FilePath, ".nfo");
            var thumbPath = Path.ChangeExtension(file.FilePath, null) + "-thumb.jpg";

            TryDelete(nfoPath);
            TryDelete(thumbPath + SourceMarkerSuffix);
            TryDelete(thumbPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Metadata] Failed to delete metadata sidecars for '{Path}'", file.FilePath);
        }

        return Task.CompletedTask;
    }

    public Task RenameEventMetadataAsync(string oldVideoPath, string newVideoPath)
    {
        try
        {
            MoveIfExists(Path.ChangeExtension(oldVideoPath, ".nfo"), Path.ChangeExtension(newVideoPath, ".nfo"));

            var oldThumb = Path.ChangeExtension(oldVideoPath, null) + "-thumb.jpg";
            var newThumb = Path.ChangeExtension(newVideoPath, null) + "-thumb.jpg";
            MoveIfExists(oldThumb, newThumb);
            MoveIfExists(oldThumb + SourceMarkerSuffix, newThumb + SourceMarkerSuffix);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Metadata] Failed to move metadata sidecars: {Old} -> {New}", oldVideoPath, newVideoPath);
        }

        return Task.CompletedTask;
    }

    private async Task<List<MetadataProvider>> GetApplicableProvidersAsync(League? league)
    {
        var providers = await _db.MetadataProviders
            .Where(p => p.Enabled && p.Type == MetadataType.Kodi)
            .ToListAsync();

        if (providers.Count == 0) return providers;

        var leagueTags = league?.Tags ?? new List<int>();
        return providers.Where(p => TagHelper.TagsMatch(p.Tags, leagueTags)).ToList();
    }

    private async Task WriteEventNfoAsync(Event evt, EventFile file, MetadataProvider provider)
    {
        var nfoPath = Path.ChangeExtension(file.FilePath, ".nfo");

        var episode = new XElement("episodedetails",
            new XElement("title", evt.Title),
            new XElement("showtitle", evt.League?.Name ?? evt.Sport),
            new XElement("season", evt.SeasonNumber ?? evt.EventDate.Year),
            new XElement("episode", evt.EpisodeNumber!.Value),
            new XElement("aired", (evt.BroadcastDate ?? evt.EventDate).ToString("yyyy-MM-dd")));

        if (!string.IsNullOrEmpty(evt.Description))
        {
            episode.Add(new XElement("plot", evt.Description));
        }

        episode.Add(new XElement("genre", evt.Sport));

        // Deliberately no <episodeguide> element - see the class-level comment.
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), episode);
        await using var stream = File.Create(nfoPath);
        await doc.SaveAsync(stream, SaveOptions.None, CancellationToken.None);
    }

    private async Task WriteShowNfoAsync(League league, string leagueDir, MetadataProvider provider)
    {
        var nfoPath = Path.Combine(leagueDir, "tvshow.nfo");

        var show = new XElement("tvshow",
            new XElement("title", league.Name),
            new XElement("genre", league.Sport));

        if (!string.IsNullOrEmpty(league.Description))
        {
            show.Add(new XElement("plot", league.Description));
        }

        show.Add(new XElement("studio", league.Name));

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), show);
        await using var stream = File.Create(nfoPath);
        await doc.SaveAsync(stream, SaveOptions.None, CancellationToken.None);
    }

    /// <summary>
    /// Downloads bytes as-is with no re-encoding (no image library in this
    /// codebase). Skips the request entirely when a marker file next to the
    /// target already records this exact source URL and the target still
    /// exists - cheap idempotency without hashing file contents.
    /// </summary>
    private async Task DownloadIfChangedAsync(string sourceUrl, string targetPath)
    {
        var markerPath = targetPath + SourceMarkerSuffix;
        if (File.Exists(targetPath) && File.Exists(markerPath))
        {
            var existingSource = await File.ReadAllTextAsync(markerPath);
            if (existingSource == sourceUrl) return;
        }

        var client = _httpClientFactory.CreateClient("MetadataImageClient");
        using var response = await client.GetAsync(sourceUrl);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[Metadata] Failed to download image {Url}: {Status}", sourceUrl, response.StatusCode);
            return;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        await File.WriteAllBytesAsync(targetPath, bytes);
        await File.WriteAllTextAsync(markerPath, sourceUrl);
    }

    private void TryDelete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private void MoveIfExists(string oldPath, string newPath)
    {
        if (!File.Exists(oldPath)) return;

        var newDir = Path.GetDirectoryName(newPath);
        if (!string.IsNullOrEmpty(newDir) && !Directory.Exists(newDir))
        {
            Directory.CreateDirectory(newDir);
        }

        File.Move(oldPath, newPath, overwrite: true);
    }
}
