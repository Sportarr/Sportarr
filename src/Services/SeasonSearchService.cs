using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for season-level search operations.
/// Unlike per-event searches, season search finds releases that match ANY event in the season,
/// allowing users to grab season packs that contain multiple events.
///
/// This is similar to how Sonarr handles season pack searching - when a user clicks "Search Season",
/// it searches for releases matching the show + season, then matches individual episodes from the pack.
/// </summary>
public class SeasonSearchService
{
    private readonly ILogger<SeasonSearchService> _logger;
    private readonly SportarrDbContext _db;
    private readonly IndexerSearchService _indexerSearchService;
    private readonly ReleaseMatchingService _releaseMatchingService;
    private readonly ReleaseEvaluator _releaseEvaluator;
    private readonly EventPartDetector _eventPartDetector;
    private readonly ConfigService _configService;

    public SeasonSearchService(
        ILogger<SeasonSearchService> logger,
        SportarrDbContext db,
        IndexerSearchService indexerSearchService,
        ReleaseMatchingService releaseMatchingService,
        ReleaseEvaluator releaseEvaluator,
        EventPartDetector eventPartDetector,
        ConfigService configService)
    {
        _logger = logger;
        _db = db;
        _indexerSearchService = indexerSearchService;
        _releaseMatchingService = releaseMatchingService;
        _releaseEvaluator = releaseEvaluator;
        _eventPartDetector = eventPartDetector;
        _configService = configService;
    }

    /// <summary>
    /// Search for all events in a season and return aggregated results.
    /// Returns releases with information about which events they match.
    /// </summary>
    public async Task<SeasonSearchResults> SearchSeasonAsync(int leagueId, string season, int? qualityProfileId = null)
    {
        var league = await _db.Leagues.FirstOrDefaultAsync(l => l.Id == leagueId);

        if (league == null)
        {
            throw new ArgumentException($"League {leagueId} not found");
        }

        // Get all events in this season (fetch separately since League doesn't have Events navigation property)
        var seasonEvents = await _db.Events
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Where(e => e.LeagueId == leagueId && e.Season == season)
            .OrderBy(e => e.EventDate)
            .ToListAsync();

        if (seasonEvents.Count == 0)
        {
            return new SeasonSearchResults
            {
                LeagueId = leagueId,
                LeagueName = league.Name,
                Season = season,
                EventCount = 0,
                Releases = new List<SeasonSearchRelease>()
            };
        }

        _logger.LogInformation("[Season Search] Searching for {Count} events in {League} season {Season}",
            seasonEvents.Count, league.Name, season);

        // Get config for multi-part episode handling
        var config = await _configService.GetConfigAsync();
        var enableMultiPart = config.EnableMultiPartEpisodes;

        // Build search query for the season
        // Use league name + season for the search query
        var searchQuery = BuildSeasonSearchQuery(league, season, seasonEvents);

        _logger.LogDebug("[Season Search] Search query: '{Query}'", searchQuery);

        // Search all indexers
        var allReleases = await _indexerSearchService.SearchAllIndexersAsync(
            query: searchQuery,
            maxResultsPerIndexer: 100,
            qualityProfileId: qualityProfileId ?? league.QualityProfileId,
            requestedPart: null, // Don't filter by part - we want all releases
            sport: league.Sport,
            enableMultiPartEpisodes: enableMultiPart
        );

        _logger.LogInformation("[Season Search] Found {Count} raw releases from indexers", allReleases.Count);

        // Match releases to events
        var seasonReleases = new List<SeasonSearchRelease>();
        var seenGuids = new HashSet<string>();

        foreach (var release in allReleases)
        {
            // Skip duplicates
            if (seenGuids.Contains(release.Guid))
                continue;
            seenGuids.Add(release.Guid);

            // Try to match this release to one or more events
            var matchedEvents = new List<SeasonEventMatch>();

            foreach (var evt in seasonEvents)
            {
                var matchResult = _releaseMatchingService.ValidateRelease(
                    release,
                    evt,
                    requestedPart: null,
                    enableMultiPartEpisodes: enableMultiPart);

                if (matchResult.IsMatch)
                {
                    // Detect which part this release is for (if any)
                    var detectedPart = _eventPartDetector.DetectPart(release.Title, evt.Sport);

                    matchedEvents.Add(new SeasonEventMatch
                    {
                        EventId = evt.Id,
                        EventTitle = evt.Title,
                        EventDate = evt.EventDate,
                        EpisodeNumber = evt.EpisodeNumber,
                        Confidence = matchResult.Confidence,
                        MatchReasons = matchResult.MatchReasons,
                        DetectedPart = detectedPart?.SegmentName,
                        HasFile = evt.HasFile,
                        Monitored = evt.Monitored
                    });
                }
            }

            if (matchedEvents.Count > 0)
            {
                // Determine if this is a "season pack" (matches multiple events or has season pack keywords)
                // Season search should ONLY return season packs - users wanting specific events/parts
                // should use the event-level search instead
                var isSeasonPack = matchedEvents.Count > 1 || IsLikelySeasonPack(release.Title, season, league.Name);

                // Skip non-season packs - season search is specifically for season-level content
                if (!isSeasonPack)
                {
                    continue;
                }

                seasonReleases.Add(new SeasonSearchRelease
                {
                    Title = release.Title,
                    Guid = release.Guid,
                    DownloadUrl = release.DownloadUrl,
                    InfoUrl = release.InfoUrl,
                    Indexer = release.Indexer,
                    Protocol = release.Protocol,
                    Size = release.Size,
                    Quality = release.Quality,
                    Source = release.Source,
                    Codec = release.Codec,
                    Language = release.Language,
                    Seeders = release.Seeders,
                    Leechers = release.Leechers,
                    PublishDate = release.PublishDate,
                    Score = release.Score,
                    QualityScore = release.QualityScore,
                    IndexerFlags = release.IndexerFlags,
                    MatchedFormats = release.MatchedFormats,
                    Approved = release.Approved,
                    Rejections = release.Rejections,
                    TorrentInfoHash = release.TorrentInfoHash,

                    // Season-specific fields
                    IsSeasonPack = isSeasonPack,
                    MatchedEvents = matchedEvents,
                    MatchedEventCount = matchedEvents.Count,
                    BestConfidence = matchedEvents.Max(m => m.Confidence),
                    DetectedPart = matchedEvents.FirstOrDefault()?.DetectedPart
                });
            }
        }

        // Sort by: matched event count (more events = better season coverage), then by quality score
        seasonReleases = seasonReleases
            .OrderByDescending(r => r.MatchedEventCount)
            .ThenByDescending(r => r.Score)
            .ThenByDescending(r => r.Seeders ?? 0)
            .ToList();

        _logger.LogInformation("[Season Search] Found {Count} season packs matching season events", seasonReleases.Count);

        return new SeasonSearchResults
        {
            LeagueId = leagueId,
            LeagueName = league.Name,
            Season = season,
            EventCount = seasonEvents.Count,
            MonitoredEventCount = seasonEvents.Count(e => e.Monitored),
            DownloadedEventCount = seasonEvents.Count(e => e.HasFile),
            Releases = seasonReleases,
            Events = seasonEvents.Select(e => new SeasonEventInfo
            {
                Id = e.Id,
                Title = e.Title,
                EventDate = e.EventDate,
                EpisodeNumber = e.EpisodeNumber,
                Monitored = e.Monitored,
                HasFile = e.HasFile
            }).ToList()
        };
    }

    /// <summary>
    /// Build a search query for the season.
    /// Uses league name + season identifier to find relevant releases.
    /// </summary>
    private string BuildSeasonSearchQuery(League league, string season, List<Event> events)
    {
        // For sports, the search query depends on the sport type
        var sport = league.Sport?.ToLower() ?? "";

        // For numbered event series (UFC, Bellator, etc.), the season might be a year
        // and we want to search for the league name + year
        if (sport.Contains("fighting") || sport.Contains("mma") || sport.Contains("boxing"))
        {
            // For fighting sports: "UFC 2024" or just "UFC" if season is a year
            return $"{league.Name} {season}";
        }

        // For team sports with seasons (NFL, NBA, etc.)
        if (sport.Contains("american football") || sport.Contains("basketball") ||
            sport.Contains("hockey") || sport.Contains("baseball"))
        {
            // "NFL 2024" or "NBA 2024-25"
            return $"{league.Name} {season}";
        }

        // For soccer/football leagues
        if (sport.Contains("soccer") || sport.Contains("football"))
        {
            // "Premier League 2024-25" or "La Liga 2024"
            return $"{league.Name} {season}";
        }

        // For motorsport
        if (sport.Contains("motorsport") || sport.Contains("racing") || sport.Contains("formula"))
        {
            // "Formula 1 2024" or "MotoGP 2024"
            return $"{league.Name} {season}";
        }

        // Default: league name + season
        return $"{league.Name} {season}";
    }

    /// <summary>
    /// Check if a release title suggests it's a season pack.
    /// </summary>
    private bool IsLikelySeasonPack(string title, string season, string leagueName)
    {
        var normalizedTitle = title.ToLower();

        // Check for common season pack indicators
        var seasonPackIndicators = new[]
        {
            "complete",
            "full season",
            "season pack",
            "all events",
            "collection",
            $"s{season}",  // S2024 style
            $"season {season}",
            $"{season} complete",
            $"{season} season"
        };

        foreach (var indicator in seasonPackIndicators)
        {
            if (normalizedTitle.Contains(indicator.ToLower()))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Results from a season search operation
/// </summary>
public class SeasonSearchResults
{
    public int LeagueId { get; set; }
    public string LeagueName { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public int MonitoredEventCount { get; set; }
    public int DownloadedEventCount { get; set; }
    public List<SeasonSearchRelease> Releases { get; set; } = new();
    public List<SeasonEventInfo> Events { get; set; } = new();
}

/// <summary>
/// A release found during season search, with information about which events it matches
/// </summary>
public class SeasonSearchRelease
{
    // Standard release fields
    public string Title { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string? InfoUrl { get; set; }
    public string Indexer { get; set; } = string.Empty;
    public string Protocol { get; set; } = "Unknown";
    public long Size { get; set; }
    public string? Quality { get; set; }
    public string? Source { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public int? Seeders { get; set; }
    public int? Leechers { get; set; }
    public DateTime PublishDate { get; set; }
    public int Score { get; set; }
    public int QualityScore { get; set; }
    public string? IndexerFlags { get; set; }
    public List<MatchedFormat> MatchedFormats { get; set; } = new();
    public bool Approved { get; set; } = true;
    public List<string> Rejections { get; set; } = new();
    public string? TorrentInfoHash { get; set; }

    // Season-specific fields
    public bool IsSeasonPack { get; set; }
    public int MatchedEventCount { get; set; }
    public int BestConfidence { get; set; }
    public string? DetectedPart { get; set; }
    public List<SeasonEventMatch> MatchedEvents { get; set; } = new();
}

/// <summary>
/// Information about an event matched by a release
/// </summary>
public class SeasonEventMatch
{
    public int EventId { get; set; }
    public string EventTitle { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public int? EpisodeNumber { get; set; }
    public int Confidence { get; set; }
    public List<string> MatchReasons { get; set; } = new();
    public string? DetectedPart { get; set; }
    public bool HasFile { get; set; }
    public bool Monitored { get; set; }
}

/// <summary>
/// Basic info about events in the season
/// </summary>
public class SeasonEventInfo
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public int? EpisodeNumber { get; set; }
    public bool Monitored { get; set; }
    public bool HasFile { get; set; }
}
