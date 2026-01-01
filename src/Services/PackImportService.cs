using System.Text.RegularExpressions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Handles importing multi-file pack downloads (e.g., NFL-2025-Week15 containing all games).
/// Scans all files in a pack against monitored events and imports matching files.
/// Unmatched files are cleaned up after import.
/// </summary>
public class PackImportService
{
    private readonly SportarrDbContext _db;
    private readonly MediaFileParser _parser;
    private readonly FileNamingService _namingService;
    private readonly ConfigService _configService;
    private readonly DiskSpaceService _diskSpaceService;
    private readonly ReleaseEvaluator _releaseEvaluator;
    private readonly ILogger<PackImportService> _logger;

    // Supported video file extensions
    private static readonly string[] VideoExtensions = { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts" };

    public PackImportService(
        SportarrDbContext db,
        MediaFileParser parser,
        FileNamingService namingService,
        ConfigService configService,
        DiskSpaceService diskSpaceService,
        ReleaseEvaluator releaseEvaluator,
        ILogger<PackImportService> logger)
    {
        _db = db;
        _parser = parser;
        _namingService = namingService;
        _configService = configService;
        _diskSpaceService = diskSpaceService;
        _releaseEvaluator = releaseEvaluator;
        _logger = logger;
    }

    /// <summary>
    /// Result of a pack import operation
    /// </summary>
    public class PackImportResult
    {
        public int FilesScanned { get; set; }
        public int FilesImported { get; set; }
        public int FilesSkipped { get; set; }
        public int FilesDeleted { get; set; }
        public List<PackFileMatch> Matches { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Information about a matched file in a pack
    /// </summary>
    public class PackFileMatch
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int EventId { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public int MatchConfidence { get; set; }
        public bool WasImported { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Find all monitored events that would match a pack release.
    /// Used when grabbing a pack to create queue entries for all matching events.
    /// </summary>
    /// <param name="packTitle">The pack release title (e.g., "NFL-2025-Week15" or "NFL 2025 Week 15 1080p WEB-DL")</param>
    /// <param name="leagueId">Optional league to filter by</param>
    /// <returns>List of matching events that are monitored and need files</returns>
    public async Task<List<Event>> FindMatchingEventsForPackAsync(string packTitle, int? leagueId = null)
    {
        var matchingEvents = new List<Event>();

        // Parse the pack title to extract week/date information
        var packInfo = ParsePackTitle(packTitle);
        if (packInfo == null)
        {
            _logger.LogWarning("[Pack Import] Could not parse pack title: {Title}", packTitle);
            return matchingEvents;
        }

        _logger.LogInformation("[Pack Import] Parsed pack: League={League}, Week={Week}, Year={Year}, DateRange={Start} to {End}",
            packInfo.League, packInfo.Week, packInfo.Year, packInfo.StartDate, packInfo.EndDate);

        // Get monitored events that match the pack criteria
        var query = _db.Events
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Where(e => e.Monitored && !e.HasFile);

        // Filter by league if known
        if (!string.IsNullOrEmpty(packInfo.League))
        {
            var leagueName = packInfo.League.ToLowerInvariant();
            query = query.Where(e => e.League != null &&
                (e.League.Name.ToLower().Contains(leagueName) ||
                 (leagueName == "nfl" && e.League.Sport.ToLower().Contains("american football")) ||
                 (leagueName == "nba" && e.League.Sport.ToLower().Contains("basketball")) ||
                 (leagueName == "nhl" && e.League.Sport.ToLower().Contains("hockey")) ||
                 (leagueName == "mlb" && e.League.Sport.ToLower().Contains("baseball"))));
        }

        if (leagueId.HasValue)
        {
            query = query.Where(e => e.LeagueId == leagueId);
        }

        // Filter by date range
        if (packInfo.StartDate.HasValue && packInfo.EndDate.HasValue)
        {
            query = query.Where(e => e.EventDate >= packInfo.StartDate.Value && e.EventDate <= packInfo.EndDate.Value);
        }

        matchingEvents = await query.ToListAsync();

        _logger.LogInformation("[Pack Import] Found {Count} matching monitored events for pack '{Title}'",
            matchingEvents.Count, packTitle);

        return matchingEvents;
    }

    /// <summary>
    /// Information parsed from a pack title
    /// </summary>
    public class PackTitleInfo
    {
        public string? League { get; set; }
        public int? Week { get; set; }
        public int? Year { get; set; }
        public int? Round { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    /// <summary>
    /// Parse a pack title to extract week/date information
    /// </summary>
    public PackTitleInfo? ParsePackTitle(string title)
    {
        var info = new PackTitleInfo();
        var titleLower = title.ToLowerInvariant();

        // Extract league
        if (titleLower.Contains("nfl")) info.League = "NFL";
        else if (titleLower.Contains("nba")) info.League = "NBA";
        else if (titleLower.Contains("nhl")) info.League = "NHL";
        else if (titleLower.Contains("mlb")) info.League = "MLB";
        else if (titleLower.Contains("premier league") || titleLower.Contains("epl")) info.League = "Premier League";
        else if (titleLower.Contains("champions league") || titleLower.Contains("ucl")) info.League = "Champions League";
        else if (titleLower.Contains("la liga")) info.League = "La Liga";
        else if (titleLower.Contains("bundesliga")) info.League = "Bundesliga";
        else if (titleLower.Contains("serie a")) info.League = "Serie A";

        // Extract year (YYYY format)
        var yearMatch = Regex.Match(title, @"20\d{2}");
        if (yearMatch.Success)
        {
            info.Year = int.Parse(yearMatch.Value);
        }

        // Extract week number
        var weekMatch = Regex.Match(title, @"[Ww](?:eek)?[\s\-._]*(\d{1,2})", RegexOptions.IgnoreCase);
        if (weekMatch.Success)
        {
            info.Week = int.Parse(weekMatch.Groups[1].Value);
        }

        // Extract round number (for soccer/other sports)
        var roundMatch = Regex.Match(title, @"[Rr](?:ound)?[\s\-._]*(\d{1,2})", RegexOptions.IgnoreCase);
        if (roundMatch.Success)
        {
            info.Round = int.Parse(roundMatch.Groups[1].Value);
        }

        // Calculate date range based on week/year
        if (info.Year.HasValue && info.Week.HasValue)
        {
            // NFL weeks typically run Thursday-Monday
            // Calculate approximate date range for the week
            var seasonStart = GetSeasonStartDate(info.League, info.Year.Value);
            if (seasonStart.HasValue)
            {
                info.StartDate = seasonStart.Value.AddDays((info.Week.Value - 1) * 7);
                info.EndDate = info.StartDate.Value.AddDays(6);
            }
        }

        // If we couldn't determine dates but have year, use a broad range
        if (!info.StartDate.HasValue && info.Year.HasValue)
        {
            info.StartDate = new DateTime(info.Year.Value, 1, 1);
            info.EndDate = new DateTime(info.Year.Value, 12, 31);
        }

        // Return null if we couldn't extract meaningful info
        if (string.IsNullOrEmpty(info.League) && !info.Week.HasValue && !info.Year.HasValue)
            return null;

        return info;
    }

    /// <summary>
    /// Get the approximate season start date for a league
    /// </summary>
    private DateTime? GetSeasonStartDate(string? league, int year)
    {
        return league?.ToUpperInvariant() switch
        {
            "NFL" => new DateTime(year, 9, 5), // NFL season starts first week of September
            "NBA" => new DateTime(year, 10, 15), // NBA starts mid-October
            "NHL" => new DateTime(year, 10, 1), // NHL starts early October
            "MLB" => new DateTime(year, 3, 28), // MLB starts late March
            "PREMIER LEAGUE" => new DateTime(year, 8, 10), // EPL starts mid-August
            "LA LIGA" => new DateTime(year, 8, 10),
            "BUNDESLIGA" => new DateTime(year, 8, 15),
            "SERIE A" => new DateTime(year, 8, 20),
            "CHAMPIONS LEAGUE" => new DateTime(year, 9, 15),
            _ => new DateTime(year, 1, 1) // Default to start of year
        };
    }

    /// <summary>
    /// Scan a pack download for files matching any monitored events.
    /// Returns information about matches without importing (for preview).
    /// </summary>
    public async Task<List<PackFileMatch>> ScanPackForMatchesAsync(string downloadPath, int? leagueId = null)
    {
        var matches = new List<PackFileMatch>();

        if (!Directory.Exists(downloadPath) && !File.Exists(downloadPath))
        {
            _logger.LogWarning("[Pack Import] Download path not found: {Path}", downloadPath);
            return matches;
        }

        // Find all video files
        var videoFiles = FindVideoFiles(downloadPath);
        _logger.LogInformation("[Pack Import] Found {Count} video files in pack: {Path}", videoFiles.Count, downloadPath);

        if (videoFiles.Count == 0)
            return matches;

        // Get monitored events to match against
        var monitoredEvents = await GetMonitoredEventsAsync(leagueId);
        _logger.LogDebug("[Pack Import] Checking against {Count} monitored events", monitoredEvents.Count);

        foreach (var file in videoFiles)
        {
            var fileName = Path.GetFileName(file);
            var match = await FindBestEventMatchAsync(fileName, monitoredEvents);

            if (match != null)
            {
                matches.Add(new PackFileMatch
                {
                    FilePath = file,
                    FileName = fileName,
                    EventId = match.Event.Id,
                    EventTitle = match.Event.Title,
                    MatchConfidence = match.Confidence
                });

                _logger.LogInformation("[Pack Import] Matched: {File} -> {Event} (confidence: {Confidence}%)",
                    fileName, match.Event.Title, match.Confidence);
            }
            else
            {
                _logger.LogDebug("[Pack Import] No match found for: {File}", fileName);
            }
        }

        return matches;
    }

    /// <summary>
    /// Import all matching files from a pack download.
    /// Files that match monitored events are imported; unmatched files are optionally deleted.
    /// </summary>
    public async Task<PackImportResult> ImportPackAsync(
        string downloadPath,
        int? leagueId = null,
        bool deleteUnmatched = true,
        bool dryRun = false)
    {
        var result = new PackImportResult();

        if (!Directory.Exists(downloadPath) && !File.Exists(downloadPath))
        {
            result.Errors.Add($"Download path not found: {downloadPath}");
            return result;
        }

        // Find all video files
        var videoFiles = FindVideoFiles(downloadPath);
        result.FilesScanned = videoFiles.Count;

        _logger.LogInformation("[Pack Import] Starting pack import: {Count} files in {Path}", videoFiles.Count, downloadPath);

        if (videoFiles.Count == 0)
        {
            result.Errors.Add("No video files found in download");
            return result;
        }

        // Get monitored events that don't have files yet
        var monitoredEvents = await GetMonitoredEventsAsync(leagueId);
        var settings = await GetMediaManagementSettingsAsync();

        foreach (var file in videoFiles)
        {
            var fileName = Path.GetFileName(file);
            var match = await FindBestEventMatchAsync(fileName, monitoredEvents);

            if (match != null && match.Confidence >= 70)
            {
                var fileMatch = new PackFileMatch
                {
                    FilePath = file,
                    FileName = fileName,
                    EventId = match.Event.Id,
                    EventTitle = match.Event.Title,
                    MatchConfidence = match.Confidence
                };

                if (!dryRun)
                {
                    try
                    {
                        await ImportSingleFileAsync(file, match.Event, settings);
                        fileMatch.WasImported = true;
                        result.FilesImported++;

                        // Remove this event from the list so we don't import duplicates
                        monitoredEvents.RemoveAll(e => e.Id == match.Event.Id);

                        _logger.LogInformation("[Pack Import] Imported: {File} -> {Event}",
                            fileName, match.Event.Title);
                    }
                    catch (Exception ex)
                    {
                        fileMatch.Error = ex.Message;
                        result.Errors.Add($"Failed to import {fileName}: {ex.Message}");
                        _logger.LogError(ex, "[Pack Import] Failed to import: {File}", fileName);
                    }
                }
                else
                {
                    fileMatch.WasImported = false;
                    _logger.LogInformation("[Pack Import] [DRY RUN] Would import: {File} -> {Event}",
                        fileName, match.Event.Title);
                }

                result.Matches.Add(fileMatch);
            }
            else
            {
                result.FilesSkipped++;

                if (deleteUnmatched && !dryRun)
                {
                    try
                    {
                        File.Delete(file);
                        result.FilesDeleted++;
                        _logger.LogDebug("[Pack Import] Deleted unmatched file: {File}", fileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Pack Import] Failed to delete unmatched file: {File}", fileName);
                    }
                }
            }
        }

        // Clean up empty directories
        if (!dryRun && deleteUnmatched)
        {
            CleanupEmptyDirectories(downloadPath);
        }

        _logger.LogInformation("[Pack Import] Completed: {Imported} imported, {Skipped} skipped, {Deleted} deleted",
            result.FilesImported, result.FilesSkipped, result.FilesDeleted);

        return result;
    }

    /// <summary>
    /// Parse a pack filename to extract team information.
    /// Handles various naming conventions:
    /// - NFL-2025-12-14.W15_MIN@DAL.mkv
    /// - NFL.2025.12.14_Lac@Kc.mkv
    /// - NFL 14-12-2025 Week15 Arizona Cardinals vs Houston Texans 1080p60_EN_FOX.mkv
    /// </summary>
    public PackFileInfo? ParsePackFileName(string fileName)
    {
        var info = new PackFileInfo { OriginalFileName = fileName };

        // Remove extension
        var name = Path.GetFileNameWithoutExtension(fileName);

        // Try different patterns

        // Pattern 1: NFL-2025-12-14.W15_MIN@DAL or NFL-2025-12-14.W15_ATL@TB
        var shortTeamPattern = new Regex(
            @"(?<league>NFL|NBA|NHL|MLB)[-._](?<year>\d{4})[-._](?<month>\d{2})[-._](?<day>\d{2})\.?W?(?<week>\d+)?[_.]?(?<away>[A-Z]{2,3})[@](?<home>[A-Z]{2,3})",
            RegexOptions.IgnoreCase);

        var match = shortTeamPattern.Match(name);
        if (match.Success)
        {
            info.League = match.Groups["league"].Value.ToUpperInvariant();
            info.Year = int.Parse(match.Groups["year"].Value);
            info.Month = int.Parse(match.Groups["month"].Value);
            info.Day = int.Parse(match.Groups["day"].Value);
            info.HomeTeamAbbr = match.Groups["home"].Value.ToUpperInvariant();
            info.AwayTeamAbbr = match.Groups["away"].Value.ToUpperInvariant();
            if (match.Groups["week"].Success)
                info.Week = int.Parse(match.Groups["week"].Value);
            return info;
        }

        // Pattern 2: NFL.2025.12.14_Lac@Kc (alternate format)
        var altShortPattern = new Regex(
            @"(?<league>NFL|NBA|NHL|MLB)\.(?<year>\d{4})\.(?<month>\d{2})\.(?<day>\d{2})[_](?<away>[A-Za-z]{2,3})[@](?<home>[A-Za-z]{2,3})",
            RegexOptions.IgnoreCase);

        match = altShortPattern.Match(name);
        if (match.Success)
        {
            info.League = match.Groups["league"].Value.ToUpperInvariant();
            info.Year = int.Parse(match.Groups["year"].Value);
            info.Month = int.Parse(match.Groups["month"].Value);
            info.Day = int.Parse(match.Groups["day"].Value);
            info.HomeTeamAbbr = match.Groups["home"].Value.ToUpperInvariant();
            info.AwayTeamAbbr = match.Groups["away"].Value.ToUpperInvariant();
            return info;
        }

        // Pattern 3: NFL 14-12-2025 Week15 Arizona Cardinals vs Houston Texans
        var fullNamePattern = new Regex(
            @"(?<league>NFL|NBA|NHL|MLB)\s+(?<day>\d{2})[-.](?<month>\d{2})[-.](?<year>\d{4})\s+Week\s*(?<week>\d+)\s+(?<away>.+?)\s+(?:vs?\.?|@)\s+(?<home>.+?)(?:\s+\d+p|\s*$)",
            RegexOptions.IgnoreCase);

        match = fullNamePattern.Match(name);
        if (match.Success)
        {
            info.League = match.Groups["league"].Value.ToUpperInvariant();
            info.Year = int.Parse(match.Groups["year"].Value);
            info.Month = int.Parse(match.Groups["month"].Value);
            info.Day = int.Parse(match.Groups["day"].Value);
            info.Week = int.Parse(match.Groups["week"].Value);
            info.HomeTeamName = NormalizeTeamName(match.Groups["home"].Value);
            info.AwayTeamName = NormalizeTeamName(match.Groups["away"].Value);
            return info;
        }

        // Pattern 4: Generic "Team vs Team" with date
        var genericPattern = new Regex(
            @"(?<year>\d{4})[-.](?<month>\d{2})[-.](?<day>\d{2}).*?(?<away>[A-Za-z\s]+?)(?:\.|\s+)(?:vs?\.?|@|Vs)(?:\.|\s+)(?<home>[A-Za-z\s]+?)(?:\.|_|\s+\d|$)",
            RegexOptions.IgnoreCase);

        match = genericPattern.Match(name);
        if (match.Success)
        {
            info.Year = int.Parse(match.Groups["year"].Value);
            info.Month = int.Parse(match.Groups["month"].Value);
            info.Day = int.Parse(match.Groups["day"].Value);
            info.HomeTeamName = NormalizeTeamName(match.Groups["home"].Value);
            info.AwayTeamName = NormalizeTeamName(match.Groups["away"].Value);

            // Try to detect league from filename
            if (name.Contains("NFL", StringComparison.OrdinalIgnoreCase)) info.League = "NFL";
            else if (name.Contains("NBA", StringComparison.OrdinalIgnoreCase)) info.League = "NBA";
            else if (name.Contains("NHL", StringComparison.OrdinalIgnoreCase)) info.League = "NHL";
            else if (name.Contains("MLB", StringComparison.OrdinalIgnoreCase)) info.League = "MLB";

            return info;
        }

        _logger.LogDebug("[Pack Import] Could not parse filename: {FileName}", fileName);
        return null;
    }

    /// <summary>
    /// Information extracted from a pack filename
    /// </summary>
    public class PackFileInfo
    {
        public string OriginalFileName { get; set; } = string.Empty;
        public string? League { get; set; }
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }
        public int? Week { get; set; }
        public string? HomeTeamName { get; set; }
        public string? AwayTeamName { get; set; }
        public string? HomeTeamAbbr { get; set; }
        public string? AwayTeamAbbr { get; set; }

        public DateTime? EventDate =>
            Year.HasValue && Month.HasValue && Day.HasValue
                ? new DateTime(Year.Value, Month.Value, Day.Value)
                : null;
    }

    /// <summary>
    /// Match result for a pack file
    /// </summary>
    private class EventMatch
    {
        public Event Event { get; set; } = null!;
        public int Confidence { get; set; }
    }

    /// <summary>
    /// Find the best matching event for a filename
    /// </summary>
    private Task<EventMatch?> FindBestEventMatchAsync(string fileName, List<Event> monitoredEvents)
    {
        var fileInfo = ParsePackFileName(fileName);
        if (fileInfo == null)
            return Task.FromResult<EventMatch?>(null);

        EventMatch? bestMatch = null;
        var bestScore = 0;

        foreach (var evt in monitoredEvents)
        {
            var score = CalculateMatchScore(fileInfo, evt);
            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = new EventMatch { Event = evt, Confidence = score };
            }
        }

        return Task.FromResult(bestMatch);
    }

    /// <summary>
    /// Calculate match score between parsed file info and an event
    /// </summary>
    private int CalculateMatchScore(PackFileInfo fileInfo, Event evt)
    {
        var score = 0;

        // Date matching (critical)
        if (fileInfo.EventDate.HasValue)
        {
            var dateDiff = Math.Abs((fileInfo.EventDate.Value - evt.EventDate.Date).TotalDays);
            if (dateDiff == 0) score += 40;
            else if (dateDiff <= 1) score += 20;
            else if (dateDiff <= 3) score += 5;
            else return 0; // Date too different, no match
        }

        // Team matching
        var homeTeam = evt.HomeTeamName?.ToLowerInvariant() ?? "";
        var awayTeam = evt.AwayTeamName?.ToLowerInvariant() ?? "";

        // Try full team name matching
        if (!string.IsNullOrEmpty(fileInfo.HomeTeamName))
        {
            var fileHome = fileInfo.HomeTeamName.ToLowerInvariant();
            var fileAway = fileInfo.AwayTeamName?.ToLowerInvariant() ?? "";

            // Check if teams match (in either order since home/away might be swapped)
            var homeMatch = homeTeam.Contains(fileHome) || fileHome.Contains(homeTeam) ||
                           awayTeam.Contains(fileHome) || fileHome.Contains(awayTeam);
            var awayMatch = homeTeam.Contains(fileAway) || fileAway.Contains(homeTeam) ||
                           awayTeam.Contains(fileAway) || fileAway.Contains(awayTeam);

            if (homeMatch) score += 25;
            if (awayMatch) score += 25;
        }

        // Try abbreviation matching
        if (!string.IsNullOrEmpty(fileInfo.HomeTeamAbbr))
        {
            var fileHomeAbbr = fileInfo.HomeTeamAbbr.ToLowerInvariant();
            var fileAwayAbbr = fileInfo.AwayTeamAbbr?.ToLowerInvariant() ?? "";

            // Check abbreviation against full team names
            var homeAbbrMatch = MatchTeamAbbreviation(fileHomeAbbr, homeTeam) ||
                               MatchTeamAbbreviation(fileHomeAbbr, awayTeam);
            var awayAbbrMatch = MatchTeamAbbreviation(fileAwayAbbr, homeTeam) ||
                               MatchTeamAbbreviation(fileAwayAbbr, awayTeam);

            if (homeAbbrMatch) score += 25;
            if (awayAbbrMatch) score += 25;
        }

        // League matching
        if (!string.IsNullOrEmpty(fileInfo.League) && evt.League != null)
        {
            var leagueName = evt.League.Name?.ToLowerInvariant() ?? "";
            if (leagueName.Contains(fileInfo.League.ToLowerInvariant()) ||
                fileInfo.League.Equals("NFL", StringComparison.OrdinalIgnoreCase) && leagueName.Contains("football") ||
                fileInfo.League.Equals("NBA", StringComparison.OrdinalIgnoreCase) && leagueName.Contains("basketball") ||
                fileInfo.League.Equals("NHL", StringComparison.OrdinalIgnoreCase) && leagueName.Contains("hockey") ||
                fileInfo.League.Equals("MLB", StringComparison.OrdinalIgnoreCase) && leagueName.Contains("baseball"))
            {
                score += 10;
            }
        }

        return score;
    }

    /// <summary>
    /// Check if an abbreviation matches a team name
    /// </summary>
    private bool MatchTeamAbbreviation(string abbr, string teamName)
    {
        if (string.IsNullOrEmpty(abbr) || string.IsNullOrEmpty(teamName))
            return false;

        // Common NFL team abbreviations
        var nflAbbreviations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "ARI", new[] { "arizona", "cardinals" } },
            { "ATL", new[] { "atlanta", "falcons" } },
            { "BAL", new[] { "baltimore", "ravens" } },
            { "BUF", new[] { "buffalo", "bills" } },
            { "CAR", new[] { "carolina", "panthers" } },
            { "CHI", new[] { "chicago", "bears" } },
            { "CIN", new[] { "cincinnati", "bengals" } },
            { "CLE", new[] { "cleveland", "browns" } },
            { "DAL", new[] { "dallas", "cowboys" } },
            { "DEN", new[] { "denver", "broncos" } },
            { "DET", new[] { "detroit", "lions" } },
            { "GB", new[] { "green bay", "packers" } },
            { "HOU", new[] { "houston", "texans" } },
            { "IND", new[] { "indianapolis", "colts" } },
            { "JAX", new[] { "jacksonville", "jaguars" } },
            { "KC", new[] { "kansas city", "chiefs" } },
            { "LV", new[] { "las vegas", "raiders" } },
            { "LAC", new[] { "los angeles chargers", "chargers" } },
            { "LAR", new[] { "los angeles rams", "rams" } },
            { "MIA", new[] { "miami", "dolphins" } },
            { "MIN", new[] { "minnesota", "vikings" } },
            { "NE", new[] { "new england", "patriots" } },
            { "NO", new[] { "new orleans", "saints" } },
            { "NYG", new[] { "new york giants", "giants" } },
            { "NYJ", new[] { "new york jets", "jets" } },
            { "PHI", new[] { "philadelphia", "eagles" } },
            { "PIT", new[] { "pittsburgh", "steelers" } },
            { "SF", new[] { "san francisco", "49ers" } },
            { "SEA", new[] { "seattle", "seahawks" } },
            { "TB", new[] { "tampa bay", "buccaneers" } },
            { "TEN", new[] { "tennessee", "titans" } },
            { "WAS", new[] { "washington", "commanders" } },
        };

        // Check if this abbreviation matches the team name
        if (nflAbbreviations.TryGetValue(abbr, out var keywords))
        {
            return keywords.Any(k => teamName.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        // Fallback: check if abbr appears in team name
        return teamName.Contains(abbr, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get monitored events that need files
    /// </summary>
    private async Task<List<Event>> GetMonitoredEventsAsync(int? leagueId = null)
    {
        var query = _db.Events
            .Include(e => e.League)
            .Where(e => e.Monitored && !e.HasFile);

        if (leagueId.HasValue)
        {
            query = query.Where(e => e.LeagueId == leagueId);
        }

        // Get events from recent past (7 days) to near future (30 days)
        var minDate = DateTime.UtcNow.AddDays(-7);
        var maxDate = DateTime.UtcNow.AddDays(30);
        query = query.Where(e => e.EventDate >= minDate && e.EventDate <= maxDate);

        return await query.ToListAsync();
    }

    /// <summary>
    /// Import a single file to an event
    /// </summary>
    private async Task ImportSingleFileAsync(string sourceFile, Event eventInfo, MediaManagementSettings settings)
    {
        var fileInfo = new FileInfo(sourceFile);
        var fileName = Path.GetFileName(sourceFile);
        var parsed = _parser.Parse(fileName);

        _logger.LogInformation("[Pack Import] Importing file: {FileName} -> {Event}", fileName, eventInfo.Title);
        _logger.LogDebug("[Pack Import] Parsed from file: Quality={Quality}, Codec={Codec}, Source={Source}, ReleaseGroup={Group}",
            parsed.Quality, parsed.VideoCodec, parsed.Source, parsed.ReleaseGroup);

        // Evaluate custom formats from the individual file name (not the pack name)
        // This ensures we get accurate CF scores based on actual file quality/encoding
        int customFormatScore = 0;
        var matchedFormats = new List<string>();

        // Get quality profile and custom formats for evaluation
        QualityProfile? qualityProfile = null;
        if (eventInfo.QualityProfileId.HasValue)
        {
            qualityProfile = await _db.QualityProfiles
                .FirstOrDefaultAsync(p => p.Id == eventInfo.QualityProfileId.Value);
        }
        else if (eventInfo.League?.QualityProfileId != null)
        {
            qualityProfile = await _db.QualityProfiles
                .FirstOrDefaultAsync(p => p.Id == eventInfo.League.QualityProfileId.Value);
        }

        var customFormats = await _db.CustomFormats.ToListAsync();

        if (qualityProfile != null || customFormats.Any())
        {
            // Create a fake release to evaluate custom formats from the file name
            var fakeRelease = new ReleaseSearchResult
            {
                Title = fileName,
                Guid = $"pack-import-{Guid.NewGuid()}", // Fake GUID for evaluation
                DownloadUrl = string.Empty, // Not used for CF evaluation
                Indexer = "Pack Import", // Not used for CF evaluation
                Size = fileInfo.Length,
                Quality = parsed.Quality,
                Codec = parsed.VideoCodec,
                Source = parsed.Source
            };

            var evaluation = _releaseEvaluator.EvaluateRelease(
                fakeRelease,
                qualityProfile,
                customFormats,
                null, // qualityDefinitions - not needed for CF eval
                null, // requestedPart
                eventInfo.Sport,
                true, // enableMultiPartEpisodes
                eventInfo.Title);

            customFormatScore = evaluation.CustomFormatScore;
            matchedFormats = evaluation.MatchedFormats?.Select(mf => mf.Name).ToList() ?? new List<string>();

            _logger.LogDebug("[Pack Import] Custom format evaluation: Score={Score}, Formats=[{Formats}]",
                customFormatScore, string.Join(", ", matchedFormats));
        }

        // Build destination path
        var rootFolder = GetBestRootFolder(settings, fileInfo.Length);
        var destinationPath = await BuildDestinationPath(settings, eventInfo, parsed, fileInfo.Extension, rootFolder);

        _logger.LogDebug("[Pack Import] Destination path: {Path}", destinationPath);

        // Create destination directory
        var destDir = Path.GetDirectoryName(destinationPath);
        if (!Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir!);
        }

        // Move file
        File.Move(sourceFile, destinationPath, overwrite: false);

        // Create EventFile record with full quality info from the individual file
        var eventFile = new EventFile
        {
            EventId = eventInfo.Id,
            FilePath = destinationPath,
            Size = fileInfo.Length,
            Quality = _parser.BuildQualityString(parsed),
            Codec = parsed.VideoCodec,
            Source = parsed.Source,
            CustomFormatScore = customFormatScore,
            Added = DateTime.UtcNow,
            LastVerified = DateTime.UtcNow,
            Exists = true,
            OriginalTitle = fileName
        };
        _db.EventFiles.Add(eventFile);

        // Update event with quality info from the actual file (not the pack)
        eventInfo.HasFile = true;
        eventInfo.FilePath = destinationPath;
        eventInfo.FileSize = fileInfo.Length;
        eventInfo.Quality = _parser.BuildQualityString(parsed);

        await _db.SaveChangesAsync();

        _logger.LogInformation("[Pack Import] ✓ Imported: {FileName} -> {Event} (Quality: {Quality}, CF Score: {CFScore})",
            fileName, eventInfo.Title, eventInfo.Quality, customFormatScore);
    }

    /// <summary>
    /// Build destination file path
    /// </summary>
    private async Task<string> BuildDestinationPath(
        MediaManagementSettings settings,
        Event eventInfo,
        ParsedFileInfo parsed,
        string extension,
        string rootFolder)
    {
        var destinationPath = rootFolder;

        if (settings.CreateEventFolder)
        {
            var folderName = _namingService.BuildFolderName(settings.EventFolderFormat, eventInfo);
            destinationPath = Path.Combine(destinationPath, folderName);
        }

        // Note: Use RenameEvents setting (same as FileRenameService) so user has single setting to control renaming
        // RenameFiles was a separate setting that caused confusion - imports should respect RenameEvents
        string filename;
        if (settings.RenameEvents)
        {
            var episodeNumber = await CalculateEpisodeNumberAsync(eventInfo);
            var tokens = new FileNamingTokens
            {
                EventTitle = eventInfo.Title,
                EventTitleThe = eventInfo.Title,
                AirDate = eventInfo.EventDate,
                Quality = parsed.Quality ?? "Unknown",
                QualityFull = _parser.BuildQualityString(parsed),
                ReleaseGroup = parsed.ReleaseGroup ?? string.Empty,
                OriginalTitle = parsed.EventTitle,
                OriginalFilename = Path.GetFileNameWithoutExtension(parsed.EventTitle),
                Series = eventInfo.League?.Name ?? eventInfo.Sport,
                Season = eventInfo.SeasonNumber?.ToString("0000") ?? eventInfo.Season ?? DateTime.UtcNow.Year.ToString(),
                Episode = episodeNumber.ToString("00"),
                Part = string.Empty
            };

            filename = _namingService.BuildFileName(settings.StandardFileFormat, tokens, extension);
        }
        else
        {
            filename = parsed.EventTitle + extension;
        }

        destinationPath = Path.Combine(destinationPath, filename);

        // Handle duplicates
        if (File.Exists(destinationPath))
        {
            var directory = Path.GetDirectoryName(destinationPath)!;
            var filenameWithoutExt = Path.GetFileNameWithoutExtension(destinationPath);
            var counter = 1;
            do
            {
                destinationPath = Path.Combine(directory, $"{filenameWithoutExt} ({counter}){extension}");
                counter++;
            }
            while (File.Exists(destinationPath));
        }

        return destinationPath;
    }

    private async Task<int> CalculateEpisodeNumberAsync(Event eventInfo)
    {
        if (!eventInfo.LeagueId.HasValue) return 1;

        var season = eventInfo.Season ?? eventInfo.SeasonNumber?.ToString() ?? eventInfo.EventDate.Year.ToString();
        var eventsInSeason = await _db.Events
            .Where(e => e.LeagueId == eventInfo.LeagueId &&
                       (e.Season == season ||
                        (e.SeasonNumber.HasValue && e.SeasonNumber.ToString() == season) ||
                        e.EventDate.Year.ToString() == season))
            .OrderBy(e => e.EventDate)
            .ThenBy(e => e.ExternalId)
            .Select(e => e.Id)
            .ToListAsync();

        var position = eventsInSeason.IndexOf(eventInfo.Id);
        return position >= 0 ? position + 1 : eventsInSeason.Count + 1;
    }

    private string GetBestRootFolder(MediaManagementSettings settings, long fileSize)
    {
        var rootFolders = settings.RootFolders
            .Where(rf => rf.Accessible)
            .OrderByDescending(rf => rf.FreeSpace)
            .ToList();

        if (rootFolders.Count == 0)
            throw new Exception("No accessible root folders configured");

        var fileSizeMB = fileSize / 1024 / 1024;
        var folder = rootFolders.FirstOrDefault(rf => rf.FreeSpace > fileSizeMB + settings.MinimumFreeSpace)
                     ?? rootFolders.First();

        return folder.Path;
    }

    private async Task<MediaManagementSettings> GetMediaManagementSettingsAsync()
    {
        var settings = await _db.MediaManagementSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new MediaManagementSettings
            {
                RootFolders = new List<RootFolder>(),
                RenameFiles = true,
                StandardFileFormat = "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}",
                CreateEventFolder = true,
                EventFolderFormat = "{Series}/Season {Season}",
                CopyFiles = false,
                MinimumFreeSpace = 100,
                RemoveCompletedDownloads = true
            };
        }

        var config = await _configService.GetConfigAsync();
        settings.UseHardlinks = config.UseHardlinks;
        settings.SkipFreeSpaceCheck = config.SkipFreeSpaceCheck;
        settings.MinimumFreeSpace = config.MinimumFreeSpace;

        var rootFolders = await _db.RootFolders.ToListAsync();
        if (rootFolders.Any())
        {
            foreach (var folder in rootFolders)
            {
                folder.Accessible = Directory.Exists(folder.Path);
            }
            settings.RootFolders = rootFolders;
        }

        return settings;
    }

    private List<string> FindVideoFiles(string path)
    {
        var files = new List<string>();

        if (File.Exists(path))
        {
            if (IsVideoFile(path))
                files.Add(path);
        }
        else if (Directory.Exists(path))
        {
            files.AddRange(Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(IsVideoFile));
        }

        return files;
    }

    private bool IsVideoFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return VideoExtensions.Contains(ext);
    }

    private string NormalizeTeamName(string name)
    {
        return Regex.Replace(name.Trim(), @"[._]", " ")
            .Trim();
    }

    private void CleanupEmptyDirectories(string rootPath)
    {
        try
        {
            if (!Directory.Exists(rootPath)) return;

            foreach (var dir in Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                        _logger.LogDebug("[Pack Import] Deleted empty directory: {Dir}", dir);
                    }
                }
                catch { /* ignore */ }
            }

            // Check root directory
            if (Directory.Exists(rootPath) && !Directory.EnumerateFileSystemEntries(rootPath).Any())
            {
                Directory.Delete(rootPath);
                _logger.LogDebug("[Pack Import] Deleted empty root directory: {Dir}", rootPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Pack Import] Error cleaning up directories");
        }
    }
}
