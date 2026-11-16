using System.Runtime.InteropServices;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Import;
using Sportarr.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Handles scanning filesystem and importing existing event files into library
/// Performs actual file move/copy/hardlink operations with proper renaming
/// </summary>
public class LibraryImportService
{
    /// <summary>
    /// Minimum match confidence (0-100) at which a scanned file is
    /// auto-imported without admin review. The score comes from this
    /// service's match engine; 85 corresponds to the "high confidence"
    /// tier the manual import UI already displays. Shared by the periodic
    /// library rescan and the real-time file watcher so the two paths can
    /// never disagree on what "safe to import" means.
    /// </summary>
    public const int AutoImportConfidenceFloor = 85;

    private readonly SportarrDbContext _db;
    private readonly ILogger<LibraryImportService> _logger;
    private readonly CustomFormatService _customFormatService;
    private readonly MediaFileParser _fileParser;
    private readonly SportsFileNameParser _sportsParser;
    private readonly FileNamingService _namingService;
    private readonly EventPartDetector _partDetector;
    private readonly ConfigService _configService;
    private readonly EpisodeNumberResolver _episodeNumberResolver;
    private readonly DiskSpaceService _diskSpaceService;
    private readonly NotificationService _notificationService;
    private readonly IMetadataWriterService _metadataWriterService;
    private readonly SportarrApiClient? _apiClient;

    private static readonly string[] VideoExtensions = SupportedExtensions.Video;

    /// <summary>Reads the sidecar of a video. Tests replace it.</summary>
    internal Func<string, NfoSidecar?>? SidecarReader { get; init; }

    /// <summary>The longest wait for one hub league search.</summary>
    internal TimeSpan HubCallTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The longest total wait for hub league searches in one scan.</summary>
    internal TimeSpan HubScanBudget { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The longest wait for the match memory lookup of one scan.</summary>
    internal TimeSpan MatchMemoryTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The confidence of a match that comes from match memory. It is above
    /// the auto-import floor, but only the Library Import page asks match
    /// memory, and the user reviews each match there. So no background scan
    /// imports a file on this match. The page only selects it first.
    /// </summary>
    internal const int MatchMemoryConfidence = 90;

    /// <summary>The last background send of file matches. Tests wait on it.</summary>
    internal Task? LastMatchReport { get; private set; }

    public LibraryImportService(
        SportarrDbContext db,
        ILogger<LibraryImportService> logger,
        MediaFileParser fileParser,
        SportsFileNameParser sportsParser,
        FileNamingService namingService,
        EventPartDetector partDetector,
        ConfigService configService,
        EpisodeNumberResolver episodeNumberResolver,
        DiskSpaceService diskSpaceService,
        CustomFormatService customFormatService,
        NotificationService notificationService,
        IMetadataWriterService metadataWriterService,
        SportarrApiClient? apiClient = null)
    {
        _db = db;
        _logger = logger;
        _fileParser = fileParser;
        _sportsParser = sportsParser;
        _namingService = namingService;
        _partDetector = partDetector;
        _configService = configService;
        _episodeNumberResolver = episodeNumberResolver;
        _diskSpaceService = diskSpaceService;
        _customFormatService = customFormatService;
        _notificationService = notificationService;
        _metadataWriterService = metadataWriterService;
        _apiClient = apiClient;
    }

    /// <summary>
    /// Scan a folder for video files
    /// </summary>
    public Task<LibraryScanResult> ScanFolderAsync(
        string folderPath,
        bool includeSubfolders = true,
        Func<int, int, Task>? onProgress = null,
        bool includeIgnoredFiles = false,
        ScanOptions? options = null) =>
        ScanPathAsync(folderPath, includeSubfolders, onProgress, includeIgnoredFiles, options: options);

    /// <summary>Analyse only this file using the same filters and event matching as a folder scan.</summary>
    public Task<LibraryScanResult> ScanFileAsync(string filePath, ScanOptions? options = null) =>
        ScanPathAsync(Path.GetDirectoryName(Path.GetFullPath(filePath))!, singleFilePath: filePath, options: options);

    private async Task<LibraryScanResult> ScanPathAsync(
        string folderPath,
        bool includeSubfolders = true,
        Func<int, int, Task>? onProgress = null,
        bool includeIgnoredFiles = false,
        string? singleFilePath = null,
        ScanOptions? options = null)
    {
        // A caller that gives no options gets the safest mode.
        var scanOptions = options ?? ScanOptions.DownloadFolder;

        var result = new LibraryScanResult
        {
            FolderPath = folderPath,
            ScannedAt = DateTime.UtcNow
        };

        if (!Directory.Exists(folderPath))
        {
            result.Errors.Add($"Folder does not exist: {folderPath}");
            return result;
        }

        _logger.LogInformation("Scanning folder for library import: {FolderPath}", folderPath);

        try
        {
            // Get media management settings for destination preview
            var settings = await GetMediaManagementSettingsAsync();
            var recycleBin = (await _configService.GetConfigAsync()).RecycleBin;

            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var foundFiles = SampleFileFilter.FilterSamples(
                    LibraryPathFilter.FilterExcluded(
                        (singleFilePath == null
                            ? Directory.GetFiles(folderPath, "*.*", searchOption)
                            : File.Exists(singleFilePath) ? new[] { singleFilePath } : Array.Empty<string>())
                            .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLower())),
                        recycleBin),
                    folderPath)
                .ToList();

            // The sort below needs the sidecar ids, so read every sidecar now.
            var sidecars = scanOptions.ReadSidecars
                ? foundFiles.ToDictionary(f => f, ReadSidecarOrNone)
                : null;

            // A file with an id token names its event exactly. It goes
            // first, so that no fuzzy match takes that event before it.
            // An id in the sidecar names its event the same way.
            // Inside each group the path order is fixed, so the result does
            // not depend on the order that the file system lists the files in.
            var tokenFirst = foundFiles
                .OrderBy(f => SportarrIdToken.ExtractEventId(Path.GetFileNameWithoutExtension(f)) != null ||
                    sidecars?[f]?.SportarrId != null ? 0 : 1);
            var files = (scanOptions.ReversePathOrder
                    ? tokenFirst.ThenByDescending(f => f, StringComparer.Ordinal)
                    : tokenFirst.ThenBy(f => f, StringComparer.Ordinal))
                .ToList();

            result.TotalFiles = files.Count;

            // The files that you ignored. Their names never go to the hub.
            var blocklistedPaths = new HashSet<string>(
                await _db.Blocklist
                    .Where(b => b.FilePath != null)
                    .Select(b => b.FilePath!)
                    .ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            // Manual review may reconsider a file that automatic scans ignore.
            var ignoredPaths = includeIgnoredFiles ? null : blocklistedPaths;

            // Preload every Event/EventFile whose FilePath falls under this scanned
            // folder in two queries total, instead of two FirstOrDefaultAsync calls
            // per file inside the loop below (previously 2 round trips per file, on
            // top of the ffprobe inspection cost, for every file under a potentially
            // large root folder). Scoped by folder prefix rather than an IN-list of
            // every discovered file path, since a full library scan can easily exceed
            // SQLite's 999 host-parameter limit.
            var eventByFilePath = (await _db.Events
                .Where(e => e.FilePath != null && e.FilePath.StartsWith(folderPath))
                .ToListAsync())
                .GroupBy(e => e.FilePath!)
                .ToDictionary(g => g.Key, g => g.First());

            var eventFileByFilePath = (await _db.EventFiles
                .Include(ef => ef.Event)
                .Where(ef => ef.FilePath.StartsWith(folderPath))
                .ToListAsync())
                .GroupBy(ef => ef.FilePath)
                .ToDictionary(g => g.Key, g => g.First());

            // Track event IDs claimed by earlier files in this batch so two files can't match the same event
            var claimedEventIds = new HashSet<int>();
            var supercarsRoundRaceCache = new Dictionary<(int Year, int Round), List<int>>();
            var folders = scanOptions.ResolveFolders ? await FolderResolution.LoadAsync(_db) : null;

            // Ask the hub once, before the loop, for the files that need a
            // match. A file with a Sportarr id in its name or its sidecar
            // names its event already, so its stem does not leave the app.
            // The stem of an ignored file does not leave the app either,
            // also when the page lists that file.
            var memory = scanOptions.LookupMatchMemory
                ? await LookUpMatchMemoryAsync(files.Where(f =>
                    !blocklistedPaths.Contains(f) &&
                    !eventByFilePath.ContainsKey(f) &&
                    !eventFileByFilePath.ContainsKey(f) &&
                    SportarrIdToken.ExtractEventId(Path.GetFileNameWithoutExtension(f)) == null &&
                    string.IsNullOrEmpty(sidecars?[f]?.SportarrId)))
                : null;

            var processedFileCount = 0;
            foreach (var filePath in files)
            {
                if (ignoredPaths?.Contains(filePath) == true)
                {
                    continue;
                }
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var filename = Path.GetFileNameWithoutExtension(filePath);

                    // Try sports-specific parser first for better accuracy
                    var sportsResult = _sportsParser.Parse(filename);
                    // ParseWithInspectionAsync runs ffprobe when the filename alone doesn't
                    // give us a Resolution+Source pair. Costs ~50-200ms per uninformative
                    // file but produces accurate Quality on first scan.
                    var parsedInfo = await _fileParser.ParseWithInspectionAsync(filename, filePath);

                    // SampleFileFilter above only catches samples with a literal
                    // "sample" token in the path. A download client's incomplete
                    // post-processing (e.g. an un-extracted archive) can leave a
                    // short preview clip named just like the real release, with
                    // nothing in the path to flag it - only ffprobe's actual
                    // duration gives that away.
                    if (settings.MinimumImportDurationMinutes > 0 && parsedInfo.Duration.HasValue &&
                        parsedInfo.Duration.Value.TotalMinutes < settings.MinimumImportDurationMinutes)
                    {
                        _logger.LogInformation(
                            "[LibraryImport] Skipping {File}: {Minutes:F1} minutes long, below the configured minimum of {Threshold} minutes",
                            filePath, parsedInfo.Duration.Value.TotalMinutes, settings.MinimumImportDurationMinutes);
                        continue;
                    }

                    // Use sports parser if it has high confidence
                    var eventTitle = sportsResult.Confidence >= 60 && !string.IsNullOrEmpty(sportsResult.EventTitle)
                        ? sportsResult.EventTitle
                        : parsedInfo.EventTitle;
                    eventTitle = WithoutQualityWords(eventTitle);

                    var organization = sportsResult.Organization;
                    var sport = sportsResult.Sport;
                    var eventDate = sportsResult.EventDate ?? parsedInfo.AirDate;

                    // The sidecar keeps the air date and the title of the database
                    // that named the file. Each one fills only a gap in the name.
                    NfoSidecar? sidecar = null;
                    sidecars?.TryGetValue(filePath, out sidecar);
                    if (sidecar != null)
                    {
                        eventDate ??= sidecar.Aired;
                        if (!string.IsNullOrEmpty(sidecar.Title) && (eventTitle?.Trim().Length ?? 0) < 4)
                        {
                            eventTitle = string.IsNullOrWhiteSpace(eventTitle)
                                ? sidecar.Title
                                : $"{eventTitle.Trim()} {sidecar.Title}";
                        }
                    }

                    // Extract year from filename, path, and parsed data
                    // CRITICAL: This prevents matching files to wrong events from different years
                    var parsedYear = ExtractYearFromPath(filePath, filename, sportsResult.EventYear, eventDate);

                    // Read the S/E token ("Formula E - S2025E05 - Jeddah E Prix", "UFC Primetime - S27E03 - ...").
                    // The number can come from another database. The scorer uses it only
                    // when other signals agree, and it never rejects an event alone.
                    int? explicitEpisodeNumber = null;
                    string? seriesLabel = null;
                    var seMatch = System.Text.RegularExpressions.Regex.Match(filename, @"S(\d{2,4})E(\d{1,4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (seMatch.Success && int.TryParse(seMatch.Groups[2].Value, out var seEp))
                    {
                        explicitEpisodeNumber = seEp;
                        // Library-format names carry the series/league ahead of the
                        // S/E token ("V8 Supercars - S2026E19 - …"). That label is
                        // the file's own statement of WHICH series it belongs to.
                        seriesLabel = filename.Substring(0, seMatch.Index).Trim(' ', '-', '.', '_');
                    }

                    // A named fighting part can join an event that already has another part.
                    var hasPartNumber = System.Text.RegularExpressions.Regex.IsMatch(filename,
                        @"(?<![a-zA-Z])pt\d+\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var hasNamedPart = EventPartDetector.IsFightingSport(sport ?? string.Empty) &&
                        (_partDetector.DetectPart(parsedInfo.EventTitle, sport, eventTitle, organization) != null ||
                         PartIdentityResolver.HasNamedPartLabel(filePath));
                    var isPartFile = hasPartNumber || hasNamedPart;

                    // Check if file is already in library - dictionary lookups against
                    // the preloaded maps below instead of two FirstOrDefaultAsync calls
                    // per file (previously 2 round trips per file, on top of the ffprobe
                    // inspection cost, for every file under the scanned folder).
                    eventByFilePath.TryGetValue(filePath, out var existingEvent);
                    eventFileByFilePath.TryGetValue(filePath, out var existingEventFile);

                    if (existingEvent != null || existingEventFile != null)
                    {
                        var linkedEvent = existingEvent ?? existingEventFile?.Event;
                        result.AlreadyInLibrary.Add(new ImportableFile
                        {
                            FilePath = filePath,
                            FileName = fileInfo.Name,
                            FileSize = fileInfo.Length,
                            ParsedTitle = eventTitle,
                            ParsedOrganization = organization,
                            ParsedSport = sport,
                            ParsedDate = eventDate,
                            Quality = parsedInfo.Quality,
                            Source = parsedInfo.Source,
                            Codec = parsedInfo.VideoCodec,
                            AudioCodec = parsedInfo.AudioCodec,
                            ReleaseGroup = parsedInfo.ReleaseGroup,
                            OriginalTitle = filename,
                            Languages = parsedInfo.DetectedLanguages,
                            ExistingEventId = linkedEvent?.Id,
                            MatchedEventTitle = BuildCurrentLabel(linkedEvent, existingEventFile)
                        });
                        continue;
                    }

                    var (folderLeague, folderSeason, folderSeasonYear) = folders == null
                        ? (null, null, null)
                        : await ResolveFileFolderAsync(folders, filePath, scanOptions, result);

                    // Try to find a matching event using multiple strategies
                    Event? matchedEvent = null;
                    int matchConfidence = 0;
                    // The event that match memory gave, if any.
                    int? memoryEventId = null;

                    // AUTHORITATIVE ID TOKEN (docs/RELEASE_NAMING.md): a file
                    // named with sportarr-ev-XXXXXXX, or carrying an
                    // embedded SPORTARR tag (surfaced via ffprobe on
                    // parsedInfo), identifies its event exactly - look it up
                    // directly and skip fuzzy scoring. The id names its
                    // event whether or not that event already has a file, or
                    // an earlier file in this batch claimed it: a second copy
                    // of a game is still that game (the import then decides
                    // between the copies, the way a grab upgrade does). A
                    // fuzzy match would hand it to a different game. Only an
                    // id this instance does not know falls back to fuzzy
                    // scoring.
                    // The sidecar id is tried when the name and the tag give no id
                    // that this instance knows.
                    var scanTokenIds = new[] { sportsResult.SportarrEventId ?? parsedInfo.SportarrEventId, sidecar?.SportarrId }
                        .OfType<string>()
                        .Where(id => id.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                    foreach (var tokenEventId in scanTokenIds)
                    {
                        var tokenEvent = await _db.Events
                            .Include(e => e.League)
                            .FirstOrDefaultAsync(e => e.ExternalId == tokenEventId);
                        if (tokenEvent != null)
                        {
                            matchedEvent = tokenEvent;
                            matchConfidence = 100;
                            if (!isPartFile)
                                claimedEventIds.Add(tokenEvent.Id);
                            _logger.LogInformation("[Library Import] Id token match: '{File}' is tagged {Token} = '{Event}'{Note}",
                                filename, tokenEventId, tokenEvent.Title,
                                tokenEvent.HasFile ? " (the event already has a file)" : "");
                            break;
                        }
                        else
                        {
                            _logger.LogWarning("[Library Import] File '{File}' carries id token {Token} but no local event has that id - falling back to fuzzy matching",
                                filename, tokenEventId);
                        }
                    }

                    // Match memory goes before fuzzy matching. A hit claims
                    // its event the way a fuzzy match does, and the upgrade
                    // rule below judges it the same way.
                    if (matchedEvent == null && memory?.HitFor(filePath) is { } hit)
                    {
                        var memoryEvent = await _db.Events
                            .Include(e => e.League)
                            .FirstOrDefaultAsync(e => e.ExternalId == hit.EventId);
                        if (memoryEvent == null)
                        {
                            memory.NotInLibrary++;
                            _logger.LogDebug("[Library Import] Match memory names {Event} for '{File}', but that event is not in the library",
                                hit.EventId, filename);
                        }
                        else if (folderLeague != null && memoryEvent.LeagueId != folderLeague.Id)
                        {
                            memory.OtherLeague++;
                            _logger.LogDebug("[Library Import] Match memory names '{Event}' for '{File}', but its league is not the '{League}' folder league",
                                memoryEvent.Title, filename, folderLeague.Name);
                        }
                        else if (MatchMemoryDisagreement(memoryEvent, filename, parsedYear, sportsResult.SeasonYearEnd,
                                     folderSeason, folderSeasonYear) is { } disagreement)
                        {
                            memory.Disagrees++;
                            _logger.LogDebug("[Library Import] Match memory names '{Event}' for '{File}', but {Disagreement}",
                                memoryEvent.Title, filename, disagreement);
                        }
                        else if (claimedEventIds.Contains(memoryEvent.Id))
                        {
                            memory.Claimed++;
                            _logger.LogDebug("[Library Import] Match memory names '{Event}' for '{File}', but another file in this scan has that event",
                                memoryEvent.Title, filename);
                        }
                        else if (parsedYear == null && folderSeason == null)
                        {
                            // A name with no year gives the same stem every
                            // year. The hit can then name the event of
                            // another year, and nothing here can check it.
                            memory.NoYear++;
                            _logger.LogDebug("[Library Import] Match memory names '{Event}' for '{File}', but the file names no year and no season",
                                memoryEvent.Title, filename);
                        }
                        else
                        {
                            memory.Applied++;
                            matchedEvent = memoryEvent;
                            matchConfidence = MatchMemoryConfidence;
                            memoryEventId = memoryEvent.Id;
                            if (!isPartFile)
                                claimedEventIds.Add(memoryEvent.Id);
                            _logger.LogDebug("[Library Import] Match memory: '{File}' = '{Event}' ({Installs} installs)",
                                filename, memoryEvent.Title, hit.Installs);
                        }
                    }

                    if (matchedEvent == null && !string.IsNullOrEmpty(eventTitle))
                    {
                        var fightingSports = EventPartDetector.FightingSportNames
                            .Select(name => name.ToLowerInvariant()).ToArray();
                        var hasPartYear = parsedYear is >= 1951 and <= 9997;
                        var partFrom = hasPartYear
                            ? new DateTime(parsedYear!.Value - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                            : DateTime.MinValue;
                        var partTo = hasPartYear
                            ? new DateTime(parsedYear!.Value + 2, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                            : DateTime.MaxValue;
                        var candidateQuery = _db.Events
                            .Include(e => e.HomeTeam)
                            .Include(e => e.AwayTeam)
                            .Include(e => e.League)
                            .Where(e => hasPartNumber ||
                                (!e.HasFile && !claimedEventIds.Contains(e.Id)) ||
                                (hasNamedPart && fightingSports.Contains(e.Sport.ToLower()) &&
                                    (!hasPartYear ||
                                     (e.EventDate >= partFrom && e.EventDate < partTo) ||
                                     (e.BroadcastDate >= partFrom && e.BroadcastDate < partTo))));
                        var scoringLabel = seriesLabel;
                        if (folderLeague != null)
                        {
                            var folderLeagueId = folderLeague.Id;
                            candidateQuery = candidateQuery.Where(e => e.LeagueId == folderLeagueId);
                            if (folderSeason != null)
                            {
                                // An event added by hand can have only a season number.
                                // An empty Season string is no season, the same as null.
                                // InFolderSeason holds the same rule for one event.
                                // Keep the two the same.
                                candidateQuery = candidateQuery.Where(e => e.Season == folderSeason ||
                                    ((e.Season == null || e.Season == "") && e.SeasonNumber == folderSeasonYear));
                            }

                            // The folder is the user's own statement of the league.
                            // It wins over the label in the file name.
                            if (!string.IsNullOrWhiteSpace(seriesLabel) &&
                                !ReleaseMatchingService.SeriesLabelMatchesLeague(seriesLabel, folderLeague))
                            {
                                _logger.LogDebug(
                                    "[Library Import] '{File}' names series '{Label}' but sits in the '{League}' folder - using the folder",
                                    filename, seriesLabel, folderLeague.Name);
                            }
                            scoringLabel = folderLeague.Name;
                        }
                        var candidates = await candidateQuery.ToListAsync();

                        IReadOnlyCollection<Event> datePeers = Array.Empty<Event>();
                        if (eventDate.HasValue)
                        {
                            var driftLeagueIds = candidates
                                .Where(candidate => candidate.LeagueId.HasValue &&
                                    (LeagueReleaseNamePolicy.AllowsObservedDateDrift(filename, candidate, eventDate.Value) ||
                                     ImportDateMatchPolicy.CanUseNearbyDate(candidate, eventDate.Value, filename)))
                                .Select(candidate => candidate.LeagueId!.Value)
                                .Distinct()
                                .ToArray();
                            if (driftLeagueIds.Length > 0)
                            {
                                datePeers = await EventDateMatchContext.LoadAsync(
                                    _db, new[] { eventDate.Value }, driftLeagueIds);
                            }
                        }

                        List<int>? roundRaceNumbers = null;
                        if (sportsResult.RoundNumber.HasValue &&
                            organization?.Contains("Supercars", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            var cacheKey = (parsedYear ?? 0, sportsResult.RoundNumber.Value);
                            if (!supercarsRoundRaceCache.TryGetValue(cacheKey, out roundRaceNumbers))
                            {
                                var roundValue = sportsResult.RoundNumber.Value.ToString();
                                var roundTitles = await _db.Events
                                    .AsNoTracking()
                                    .Where(evt => evt.Round == roundValue &&
                                        evt.League != null && evt.League.Name.Contains("Supercars") &&
                                        (!parsedYear.HasValue || evt.Season == parsedYear.Value.ToString() ||
                                            evt.SeasonNumber == parsedYear.Value))
                                    .Select(evt => evt.Title)
                                    .ToListAsync();
                                roundRaceNumbers = ReleaseMatchingService.RaceNumbersInTitles(roundTitles);
                                supercarsRoundRaceCache[cacheKey] = roundRaceNumbers;
                            }
                        }

                        // A date names one event only when no other candidate falls on it.
                        var candidatesOnDate = eventDate.HasValue
                            ? candidates.Count(candidate => FallsOn(candidate, eventDate.Value))
                            : 0;

                        // Every candidate at the best score, with the S/E bonus flag of each.
                        var tied = new List<(Event Event, bool EpisodeNumberBonus)>();
                        foreach (var candidate in candidates)
                        {
                            if (hasNamedPart && !hasPartNumber &&
                                (candidate.HasFile || claimedEventIds.Contains(candidate.Id)) &&
                                DetectImportPart(filePath, parsedInfo, candidate.Sport, candidate.Title,
                                    candidate.League?.Name) == null)
                            {
                                continue;
                            }

                            var confidence = CalculateMatchConfidence(
                                eventTitle, candidate.Title, organization, candidate,
                                eventDate, parsedYear, sportsResult.RoundNumber,
                                sportsResult.SeasonYearEnd, explicitEpisodeNumber,
                                sportsResult.Location, _logger, sport, scoringLabel,
                                roundRaceNumbers, filename, datePeers,
                                candidatesOnDate > 1, out var episodeNumberBonus);
                            if (confidence > matchConfidence)
                            {
                                matchConfidence = confidence;
                                matchedEvent = candidate;
                                tied.Clear();
                                tied.Add((candidate, episodeNumberBonus));
                            }
                            else if (confidence > 0 && confidence == matchConfidence)
                            {
                                tied.Add((candidate, episodeNumberBonus));
                            }
                        }

                        // A tie is broken only by a signal that one tied event
                        // has and the others lack, so the order of the
                        // candidates never chooses. An S/E number chooses when
                        // its bonus fired on one tied event only.
                        if (tied.Count > 1)
                        {
                            matchedEvent = BreakTie(tied, eventTitle, sportsResult.Location);
                            if (matchedEvent == null)
                            {
                                _logger.LogDebug(
                                    "[Library Import] '{File}' fits {Count} events equally and names none of them - leaving it unmatched",
                                    filename, tied.Count);
                                matchConfidence = 0;
                            }
                        }

                        // Only accept matches with reasonable confidence
                        if (matchConfidence < MatchAcceptanceFloor)
                        {
                            matchedEvent = null;
                            matchConfidence = 0;
                        }

                        // Reserve this event so no other file in this batch can claim it.
                        // Part files don't claim exclusively — multiple parts share the same event.
                        if (matchedEvent != null && !isPartFile)
                            claimedEventIds.Add(matchedEvent.Id);
                    }

                    // What the file the event already holds says about this one,
                    // and whether an import would keep the file in place (a copy
                    // already beside that file is not renamed).
                    var judged = matchedEvent == null || isPartFile
                        ? (Rejections: new List<string>(), InPlace: false)
                        : await UpgradeRejectionsAsync(matchedEvent, filePath, parsedInfo);

                    // Build destination preview for matched files
                    string? destinationPreview = null;
                    if (matchedEvent != null)
                    {
                        destinationPreview = judged.InPlace
                            ? filePath
                            : await BuildDestinationPreviewAsync(matchedEvent, fileInfo.Name, settings);
                    }

                    var importable = new ImportableFile
                    {
                        FilePath = filePath,
                        FileName = fileInfo.Name,
                        FileSize = fileInfo.Length,
                        ParsedTitle = eventTitle,
                        ParsedOrganization = organization,
                        ParsedSport = sport,
                        ParsedDate = eventDate,
                        Quality = parsedInfo.Quality,
                        Source = parsedInfo.Source,
                        Codec = parsedInfo.VideoCodec,
                        AudioCodec = parsedInfo.AudioCodec,
                        ReleaseGroup = parsedInfo.ReleaseGroup,
                        OriginalTitle = filename,
                        Languages = parsedInfo.DetectedLanguages,
                        MatchedEventId = matchedEvent?.Id,
                        MatchedEventTitle = matchedEvent?.Title,
                        MatchedLeagueName = matchedEvent?.League?.Name,
                        MatchedSeason = matchedEvent?.Season ?? matchedEvent?.SeasonNumber?.ToString() ?? (matchedEvent?.BroadcastDate ?? matchedEvent?.EventDate)?.Year.ToString(),
                        DestinationPreview = destinationPreview,
                        MatchConfidence = matchConfidence > 0 ? Math.Min(100, matchConfidence) : null,
                        MatchedByMemory = matchedEvent != null && matchedEvent.Id == memoryEventId,
                        Rejections = judged.Rejections,
                        HeldBySourcePrecedence = judged.Rejections.Contains(SourcePrecedence.IptvRecordingRejection),
                        FolderLeagueId = folderLeague?.Id,
                        FolderLeagueName = folderLeague?.Name,
                        FolderSeason = folderSeason
                    };

                    if (matchedEvent != null)
                    {
                        result.MatchedFiles.Add(importable);
                    }
                    else
                    {
                        result.UnmatchedFiles.Add(importable);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process file: {FilePath}", filePath);
                    result.Errors.Add($"Failed to process {Path.GetFileName(filePath)}: {ex.Message}");
                }

                processedFileCount++;
                if (onProgress != null)
                {
                    await onProgress(processedFileCount, files.Count);
                }
            }

            if (folders?.HubCalls > 0)
            {
                _logger.LogInformation(
                    "[Library Import] Asked the hub about {Calls} folder names in {Seconds:F1} s: {Suggested} suggested{Stopped}",
                    folders.HubCalls, folders.HubTime.Elapsed.TotalSeconds, folders.HubSuggestions,
                    folders.HubStopped ? ", then stopped after a slow or failed call" : "");
            }

            if (memory != null)
            {
                _logger.LogInformation(
                    "[Library Import] Asked match memory about {Stems} stems: {Hits} hits, {Applied} applied, {NotInLibrary} not in the library, {OtherLeague} in another league, {Disagrees} disagree on season, year or session, {Claimed} already claimed, {NoYear} name no year",
                    memory.StemsSent, memory.Hits.Count, memory.Applied, memory.NotInLibrary, memory.OtherLeague,
                    memory.Disagrees, memory.Claimed, memory.NoYear);
            }

            _logger.LogInformation(
                "Scan complete: {Total} files, {Matched} matched, {Unmatched} unmatched, {AlreadyInLibrary} already in library",
                result.TotalFiles, result.MatchedFiles.Count, result.UnmatchedFiles.Count, result.AlreadyInLibrary.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan folder: {FolderPath}", folderPath);
            result.Errors.Add($"Failed to scan folder: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// The sidecar of the video, or null. A sidecar that cannot be read is
    /// treated as absent, because one bad file must not stop the scan.
    /// </summary>
    private NfoSidecar? ReadSidecarOrNone(string videoPath)
    {
        try
        {
            return SidecarReader != null ? SidecarReader(videoPath) : NfoSidecarReader.TryRead(videoPath, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Library Import] Ignoring the sidecar of {Path}", videoPath);
            return null;
        }
    }

    /// <summary>What the scan knows about the library's folders, loaded once per scan.</summary>
    private sealed class FolderResolution
    {
        public required List<string> RootPaths { get; init; }
        public required List<League> Leagues { get; init; }
        public Dictionary<string, League?> LeagueByFolder { get; } = new();
        public Dictionary<int, List<string>> SeasonsByLeague { get; } = new();

        // The hub answers once for each folder name in a scan.
        public Dictionary<string, League?> HubSuggestionByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public System.Diagnostics.Stopwatch HubTime { get; } = new();
        public int HubCalls { get; set; }
        public int HubSuggestions { get; set; }
        // After one slow or failed call the scan asks the hub no more.
        public bool HubStopped { get; set; }

        public static async Task<FolderResolution> LoadAsync(SportarrDbContext db) => new()
        {
            RootPaths = await db.RootFolders.AsNoTracking().Select(r => r.Path).ToListAsync(),
            Leagues = await db.Leagues.AsNoTracking().ToListAsync(),
        };

        /// <summary>
        /// The configured root folder that holds the file, the deepest one
        /// when they nest. Null when no configured root holds it.
        /// </summary>
        public string? ConfiguredRootOf(string filePath)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return RootPaths
                .Select(r => r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Where(r => r.Length > 0 && filePath.StartsWith(r + Path.DirectorySeparatorChar, comparison))
                .OrderByDescending(r => r.Length)
                .FirstOrDefault();
        }

        private readonly Dictionary<string, League?> _leagueByRoot = new();

        /// <summary>
        /// The league that the root's own folder name names exactly, by the
        /// strict rule of the league folders. Null for a root such as "/media".
        /// </summary>
        public League? LeagueOfRoot(string root)
        {
            if (!_leagueByRoot.TryGetValue(root, out var league))
            {
                league = LibraryFolderResolver.LeagueOfRoot(root, Leagues);
                _leagueByRoot[root] = league;
            }
            return league;
        }

        private readonly Dictionary<string, bool> _rootLeagueAppliesByFolder = new();

        /// <summary>
        /// The root's own league for this file, or null. A folder directly
        /// below the root that names a league takes the file from the root.
        /// </summary>
        public League? RootLeagueFor(string root, string filePath)
        {
            var league = LeagueOfRoot(root);
            if (league == null) return null;
            var first = LibraryFolderResolver.FirstFolderBelowRoot(root, filePath);
            if (first == null) return league;

            var folderPath = Path.Combine(root, first);
            if (!_rootLeagueAppliesByFolder.TryGetValue(folderPath, out var applies))
            {
                applies = LibraryFolderResolver.RootLeagueApplies(root, filePath, Leagues);
                _rootLeagueAppliesByFolder[folderPath] = applies;
            }
            return applies ? league : null;
        }
    }

    /// <summary>
    /// The league and the league's season that the file's folders name,
    /// with the first year of the season folder.
    /// A league folder that fits no league goes into the result once.
    /// </summary>
    private async Task<(League? League, string? Season, int? SeasonYear)> ResolveFileFolderAsync(
        FolderResolution folders, string filePath, ScanOptions options, LibraryScanResult result)
    {
        // Only a configured root holds league folders. A file outside every
        // root, as in a downloads folder, gets no folder and no row.
        // The file watcher scans only the file's own folder. The configured
        // root shows where the league folder is.
        var root = folders.ConfiguredRootOf(filePath);
        if (root == null)
            return (null, null, null);
        var (leagueFolder, seasonFolderName) = LibraryFolderResolver.SplitPath(root, filePath);
        var leagueFolderPath = LibraryFolderResolver.LeagueFolderPath(root, filePath);

        // A root whose own name names a league is that league's folder.
        // Its season and event folders get its league. A folder below it
        // that names a league is that league's folder, as under a generic
        // root. Any other root is never a league folder.
        var rootLeague = folders.RootLeagueFor(root, filePath);
        League? league;
        if (rootLeague != null)
        {
            league = rootLeague;
        }
        else
        {
            // A season folder is never a league folder either.
            if (leagueFolderPath.Length <= root.Length ||
                LibraryFolderResolver.ParseSeasonFolder(leagueFolder) != null)
            {
                return (null, null, null);
            }

            if (!folders.LeagueByFolder.TryGetValue(leagueFolderPath, out league))
            {
                league = LibraryFolderResolver.ResolveLeague(leagueFolder, folders.Leagues);
                folders.LeagueByFolder[leagueFolderPath] = league;
                if (league == null)
                    result.UnresolvedFolders.Add(await DescribeUnresolvedFolderAsync(folders, leagueFolderPath, leagueFolder, options));
            }
            if (league == null) return (null, null, null);
        }

        var seasonFolder = seasonFolderName == null ? null : LibraryFolderResolver.ParseSeasonFolder(seasonFolderName);
        if (seasonFolder == null) return (league, null, null);

        if (!folders.SeasonsByLeague.TryGetValue(league.Id, out var seasons))
        {
            var leagueId = league.Id;
            seasons = await _db.Events
                .AsNoTracking()
                .Where(e => e.LeagueId == leagueId && e.Season != null && e.Season != "")
                .Select(e => e.Season!)
                .Distinct()
                .ToListAsync();
            folders.SeasonsByLeague[league.Id] = seasons;
        }
        return (league, LibraryFolderResolver.MapToLeagueSeason(seasonFolder, seasons), seasonFolder.StartYear);
    }

    private async Task<UnresolvedFolder> DescribeUnresolvedFolderAsync(
        FolderResolution folders, string path, string name, ScanOptions options)
    {
        var suggestion = options.AskHubForLeagues && _apiClient != null
            ? await HubSuggestionAsync(folders, name)
            : null;
        return new UnresolvedFolder(path, name, suggestion?.ExternalId, suggestion?.Name, suggestion?.Sport,
            LeagueSportRules.NormalizeSportFormat(suggestion?.SportFormat));
    }

    /// <summary>
    /// The league that the hub suggests for a folder name, or null.
    /// The hub only suggests, so a slow or failed call gives no suggestion.
    /// The search also finds leagues that only share words with the folder
    /// name. Only a league that the folder names exactly is offered.
    /// </summary>
    private async Task<League?> HubSuggestionAsync(FolderResolution folders, string name)
    {
        if (folders.HubSuggestionByName.TryGetValue(name, out var known))
            return known;

        var remaining = HubScanBudget - folders.HubTime.Elapsed;
        if (folders.HubStopped || remaining <= TimeSpan.Zero)
        {
            folders.HubStopped = true;
            return null;
        }

        League? suggestion = null;
        folders.HubCalls++;
        folders.HubTime.Start();
        try
        {
            // The token cancels the request itself when the wait ends.
            using var timeout = new CancellationTokenSource(remaining < HubCallTimeout ? remaining : HubCallTimeout);
            var hits = await _apiClient!.SearchLeagueAsync(name, timeout.Token);
            if (hits == null)
            {
                folders.HubStopped = true;
                _logger.LogDebug("[Library Import] The hub gave no answer for folder '{Folder}'", name);
            }
            else
            {
                suggestion = hits.FirstOrDefault(hit => ReleaseMatchingService.SeriesLabelEqualsLeagueAlias(name, hit));
            }
        }
        catch (Exception ex)
        {
            folders.HubStopped = true;
            _logger.LogDebug(ex, "[Library Import] No hub suggestion for folder '{Folder}'", name);
        }
        finally
        {
            folders.HubTime.Stop();
        }

        if (suggestion != null)
            folders.HubSuggestions++;
        // A name that got no answer is not stored. The scan asks no more anyway.
        if (!folders.HubStopped)
            folders.HubSuggestionByName[name] = suggestion;
        return suggestion;
    }

    /// <summary>The hub's match memory answers for one scan, with the counts for its log line.</summary>
    private sealed class MatchMemory(Dictionary<string, string> stemByPath, IReadOnlyDictionary<string, FileMatchHit> hits)
    {
        public IReadOnlyDictionary<string, FileMatchHit> Hits { get; } = hits;
        public int StemsSent { get; init; }
        public int Applied { get; set; }
        public int NotInLibrary { get; set; }
        public int OtherLeague { get; set; }
        public int Disagrees { get; set; }
        public int Claimed { get; set; }
        public int NoYear { get; set; }

        // Only a file that the scan sent gets a hit.
        public FileMatchHit? HitFor(string filePath) =>
            stemByPath.TryGetValue(filePath, out var stem) && Hits.TryGetValue(stem, out var hit) ? hit : null;
    }

    /// <summary>
    /// Ask the hub for the event that other installs matched each file to.
    /// Returns null when the scan must not ask. The lookup sends file stems,
    /// so it runs only when Send Anonymous Usage Data is on. A slow or
    /// failed lookup gives no hits, and the scan then matches as before.
    /// </summary>
    private async Task<MatchMemory?> LookUpMatchMemoryAsync(IEnumerable<string> files)
    {
        if (_apiClient == null || !(await _configService.GetConfigAsync()).SendAnonymousUsageData)
            return null;

        var stemByPath = new Dictionary<string, string>();
        foreach (var file in files)
        {
            var stem = FileStemNormalizer.Normalize(Path.GetFileNameWithoutExtension(file));
            if (stem.Length > 0)
                stemByPath[file] = stem;
        }
        if (stemByPath.Count == 0)
            return null;

        var stems = stemByPath.Values.Distinct(StringComparer.Ordinal).ToList();
        IReadOnlyDictionary<string, FileMatchHit> hits;
        try
        {
            // The folder league check happens per file, so the request
            // names no league.
            // The hub client retries a failed batch, and the retry waits on
            // this token too. So one retried batch can use up the time and
            // empty the whole lookup. That is safe, because the scan then
            // uses fuzzy matching only.
            using var timeout = new CancellationTokenSource(MatchMemoryTimeout);
            hits = await _apiClient.LookupFileMatchesAsync(stems, null, timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Library Import] Match memory lookup failed (ignored)");
            hits = new Dictionary<string, FileMatchHit>();
        }
        return new MatchMemory(stemByPath, hits) { StemsSent = stems.Count };
    }

    /// <summary>
    /// Report the events that the user tied to files, as anonymous usage
    /// data. Sends nothing when Send Anonymous Usage Data is off. The send
    /// runs in the background. The caller does not wait for it, and a
    /// failure never reaches the caller.
    /// </summary>
    public async Task ReportFileMatchesAsync(IReadOnlyList<FileMatchSuggestion> suggestions)
    {
        if (suggestions.Count == 0 || _apiClient == null)
            return;
        try
        {
            var send = await StartMatchSendAsync(suggestions);
            if (send != null)
                LastMatchReport = send;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Library Import] File match report failed (ignored)");
        }
    }

    /// <summary>
    /// Start the send of file matches, or return null when the user does
    /// not allow usage data. Uses only the config service and the client,
    /// so it is safe after the request scope ends.
    /// </summary>
    private async Task<Task<bool>?> StartMatchSendAsync(IReadOnlyList<FileMatchSuggestion> suggestions)
    {
        if (_apiClient == null || !(await _configService.GetConfigAsync()).SendAnonymousUsageData)
            return null;
        var installId = await _configService.GetOrCreateAnalyticsInstanceIdAsync();
        return installId == null ? null : _apiClient.SubmitFileMatchSuggestionsAsync(installId, suggestions);
    }

    /// <summary>The Sportarr id in the sidecar of a video, or null.</summary>
    public string? ReadSidecarSportarrId(string videoPath) =>
        File.Exists(videoPath) ? ReadSidecarOrNone(videoPath)?.SportarrId : null;

    /// <summary>
    /// Report the match of an accepted pending import as anonymous usage
    /// data. The event lookup and the sidecar read run now. The probe and
    /// the send run in the background, so the response does not wait for
    /// them. A failure never reaches the caller, because the file is
    /// already in the library.
    /// </summary>
    /// <param name="import">The accepted row, with its final event.</param>
    /// <param name="sourceFilePath">The video before the import. Its name is the stem.</param>
    /// <param name="importedFilePath">The video after the import. Its embedded tag moves with it.</param>
    /// <param name="preImportSidecarId">The sidecar id read before the import, if any.</param>
    public async Task ReportAcceptedPendingImportAsync(
        PendingImport import,
        string sourceFilePath,
        string importedFilePath,
        string? preImportSidecarId)
    {
        if (_apiClient == null)
            return;
        try
        {
            if (!(await _configService.GetConfigAsync()).SendAnonymousUsageData)
                return;
            var evt = await _db.Events.AsNoTracking().Include(e => e.League)
                .FirstOrDefaultAsync(e => e.Id == import.SuggestedEventId);
            if (evt == null)
                return;

            // A download names its video only after the import. Its source
            // sidecar is still there unless the import moved it.
            var sidecarId = preImportSidecarId ?? (sourceFilePath == import.FilePath
                ? null
                : ReadSidecarSportarrId(sourceFilePath));
            var suggestion = FileMatchSuggestionCollector.FromAcceptedPendingImport(import, sourceFilePath, evt, sidecarId);
            if (suggestion == null)
                return;

            // The probe can take seconds, so it runs after the response. The
            // work below must not touch the database context, because the
            // request scope ends with the response. The collector already
            // checked the source name. Only the embedded tag is read here,
            // because the library name carries the id that Sportarr wrote.
            var fileParser = _fileParser;
            var logger = _logger;
            LastMatchReport = Task.Run(async () =>
            {
                try
                {
                    if (await fileParser.ReadEmbeddedSportarrIdAsync(importedFilePath) != null)
                        return;
                    var send = await StartMatchSendAsync(new[] { suggestion });
                    if (send != null)
                        await send;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "[Pending Import] File match report failed (ignored)");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Pending Import] File match report failed (ignored)");
        }
    }

    /// <summary>
    /// Import matched files into library - moves/copies/hardlinks files to library folder
    /// </summary>
    public async Task<ImportResult> ImportFilesAsync(
        List<FileImportRequest> requests,
        Func<int, int, Task>? onProgress = null)
    {
        var result = new ImportResult();
        var processed = 0;

        // Successful imports queued for OnDownload notifications after the
        // batch save, so media-server connections (Plex/Jellyfin/Emby)
        // refresh for manual imports exactly like automatic ones.
        var notifyQueue = new List<(Event Evt, string Path, string Quality, EventFile File)>();

        // Get media management settings for file transfer
        var settings = await GetMediaManagementSettingsAsync();
        var config = await _configService.GetConfigAsync();

        // The matches that the user chose. One send follows the batch save.
        var matchReports = new List<FileMatchSuggestion>();
        // Each file that this batch imported, with the file row it now has.
        var importedSources = new List<ImportedSource>();
        var mayReportMatches = config.SendAnonymousUsageData && _apiClient != null;

        foreach (var request in requests)
        {
            if (onProgress != null)
            {
                try { await onProgress(processed, requests.Count); } catch { /* progress must never abort the import */ }
            }
            processed++;
            try
            {
                if (!File.Exists(request.FilePath))
                {
                    result.Failed.Add(request.FilePath);
                    result.Errors.Add($"Source file not found: {request.FilePath}");
                    continue;
                }

                var sourceFileInfo = new FileInfo(request.FilePath);
                // Capture file size BEFORE moving - after move, source file won't exist
                var sourceFileSize = sourceFileInfo.Length;
                // ParseWithInspectionAsync runs ffprobe when the filename alone doesn't
                // yield a usable Resolution+Source pair. This is what saves library imports
                // of files like "match.mkv" from ending up with Quality=Unknown.
                var parsedInfo = await _fileParser.ParseWithInspectionAsync(
                    Path.GetFileNameWithoutExtension(request.FilePath),
                    request.FilePath);

                // A Sportarr id in the tag or the sidecar names the event, so
                // the match is not reported. Read the sidecar before a move takes it.
                // A match memory answer that the user kept is the hub's own
                // answer. A report would count it again, so it is not sent.
                var keptMemoryAnswer = request.SuggestedByMemory && request.SuggestedEventId.HasValue
                    && request.EventId == request.SuggestedEventId;
                var reportMatch = request.ReportMatch && mayReportMatches && !keptMemoryAnswer;
                var carriedSportarrId = reportMatch
                    ? parsedInfo.SportarrEventId ?? parsedInfo.SportarrLeagueId
                        ?? ReadSidecarOrNone(request.FilePath)?.SportarrId
                    : null;

                // Parse import mode from request: "move", "copy", or "hardlink"
                // Default (Auto) when the request doesn't specify a mode, mirroring
                // the automatic-import planner's precedence:
                // - UseHardlinks=true: Hardlink (regardless of CopyFiles - users who
                //   enabled "Use Hardlinks instead of Copy" expect manual imports to
                //   honor it and keep torrents seeding from the source path)
                // - UseHardlinks=false + CopyFiles=true: Copy
                // - both off: Move (removes source files)
                var importMode = DefaultImportMode(settings);
                // An explicit per-import mode is honored literally: explicit Copy
                // must produce a real copy even when Use Hardlinks is enabled
                // (Copy and Hardlink are separate choices in the import UI).
                var modeWasExplicit = false;
                if (!string.IsNullOrEmpty(request.ImportMode))
                {
                    var requested = request.ImportMode.ToLowerInvariant() switch
                    {
                        "copy" => LibraryImportMode.Copy,
                        "hardlink" => LibraryImportMode.Hardlink,
                        "move" => LibraryImportMode.Move,
                        _ => (LibraryImportMode?)null
                    };
                    if (requested.HasValue)
                    {
                        importMode = requested.Value;
                        modeWasExplicit = true;
                    }
                }
                _logger.LogInformation("[Import] Library import mode: {ImportMode} (CopyFiles={CopyFiles}, UseHardlinks={UseHardlinks}, requested: {RequestedMode})",
                    importMode, settings.CopyFiles, settings.UseHardlinks, request.ImportMode ?? "auto");
                var fullEventWasSelected = request.PartName is not null
                    && EventPartDetector.IsFullEvent(request.PartName);

                if (request.EventId.HasValue)
                {
                    // Import to existing event
                    var existingEvent = await _db.Events
                        .Include(e => e.League)
                        .Include(e => e.Files)
                        .FirstOrDefaultAsync(e => e.Id == request.EventId.Value);

                    if (existingEvent != null)
                    {
                        // Check if this is a re-import (file already linked to this event)
                        var existingFileRecord = existingEvent.Files
                            .FirstOrDefault(f => f.FilePath == request.FilePath);
                        var isReimport = existingFileRecord != null;

                        // Use manual part info if provided, otherwise auto-detect
                        // IMPORTANT: Determine part info BEFORE transfer so it can be used in filename
                        // NOTE: "Full Event" selection means no part - store as null
                        string? partName = request.PartName;
                        int? partNumber = request.PartNumber;
                        // A "Full Event" event keeps one whole file, as if parts were off.
                        var eventUsesParts = EventPartDetector.UsesParts(config.EnableMultiPartEpisodes, existingEvent);
                        var wholeEventImport = fullEventWasSelected
                            || (config.EnableMultiPartEpisodes && !eventUsesParts);

                        // If user selected "Full Event", treat as no part (null)
                        if (wholeEventImport)
                        {
                            partName = null;
                            partNumber = null;
                        }
                        else if (string.IsNullOrEmpty(partName) && config.EnableMultiPartEpisodes)
                        {
                            // Auto-detect part from filename
                            var partInfo = DetectImportPart(request.FilePath, parsedInfo, existingEvent.Sport,
                                existingEvent.Title, existingEvent.League?.Name);
                            partName = partInfo?.SegmentName;
                            partNumber = partInfo?.PartNumber;
                        }

                        // The caller can send a part name with no number. The number orders
                        // the parts, so fill it from the name.
                        partNumber ??= EventPartDetector.ResolvePartNumber(partName, existingEvent.Sport,
                            existingEvent.Title, existingEvent.League?.Name);

                        // Preserve a known source on rescan. Only an explicit
                        // DVR marker can classify an untracked library file.
                        var isIptvRecording = request.IsIptvRecording
                            || (!request.FromDownloadClient &&
                                (existingFileRecord?.IsIptvRecording == true ||
                                 string.Equals(request.Source, "IPTV", StringComparison.OrdinalIgnoreCase) ||
                                 (existingFileRecord?.ReleaseTitle == null &&
                                  SourcePrecedence.LooksLikeIptvRecording(request.FilePath,
                                      request.Quality ?? _fileParser.BuildQualityString(parsedInfo)))));

                        // The file the event already holds for this part decides whether
                        // this one may take its place. One rule for every import path
                        // (ImportUpgradeRule): an automatic import stops at a rejection
                        // and leaves the file where it is; a manual import replaces,
                        // except over source precedence, which only an explicit
                        // override (Import anyway) lifts.
                        // A copy that already sits beside the file it replaces is imported
                        // in place and that file stays on disk untracked; a copy from
                        // anywhere else replaces it through the recycle bin.
                        var replacementCandidates = request.ReplaceOccupiedFile
                            ? existingEvent.Files.Where(f => f.Exists &&
                                !string.Equals(f.FilePath, request.FilePath, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(SourcePrecedence.NormalizePart(f.PartName),
                                    SourcePrecedence.NormalizePart(partName), StringComparison.OrdinalIgnoreCase)).ToList()
                            : null;
                        if (replacementCandidates?.Count > 1)
                            throw new InvalidOperationException("Multiple tracked files cover this event part. Resolve them before importing.");
                        var occupant = existingFileRecord == null
                            ? request.ReplaceOccupiedFile
                                ? replacementCandidates?.SingleOrDefault()
                                : ImportUpgradeRule.ExistingFileForPart(existingEvent.Files, partNumber, request.FilePath,
                                    // A whole file never takes a part file's place while parts are on.
                                    config.EnableMultiPartEpisodes)
                            : null;
                        var importInPlace = false;
                        if (occupant == null && request.RequireOccupant)
                        {
                            _logger.LogInformation("[Library Import] {Reason} Left where it is: {Path}",
                                NoComparableFileRejection, request.FilePath);
                            result.Rejected.Add(new ImportRejection(request.FilePath, NoComparableFileRejection));
                            result.Skipped.Add(request.FilePath);
                            continue;
                        }
                        if (occupant != null)
                        {
                            var decision = await DecideUpgradeAsync(occupant, request.FilePath,
                                request.Quality ?? _fileParser.BuildQualityString(parsedInfo), existingEvent,
                                isIptvRecording);
                            importInPlace = IsBesideOccupant(request.FilePath, occupant.FilePath)
                                && !request.ReplaceOccupiedFile;
                            var heldBySourcePrecedence = decision.Rejection == SourcePrecedence.IptvRecordingRejection
                                && !request.OverrideSourcePrecedence;
                            if (heldBySourcePrecedence || (request.OnlyIfUpgrade
                                && (!decision.IsUpgrade || (decision.Equal
                                    && (request.RejectEqual || importInPlace || importMode != LibraryImportMode.Move)))))
                            {
                                // An equal copy is kept out of an automatic import when
                                // taking it over would only repeat: beside the held file
                                // the other copy would take over again on the next rescan,
                                // and a copied or linked source stays where a rescan finds
                                // it again, recycling the held file each time.
                                var reason = decision.Rejection ?? EqualCopyRejection;
                                _logger.LogInformation("[Library Import] {Reason} Left where it is: {Path}", reason, request.FilePath);
                                result.Rejected.Add(new ImportRejection(request.FilePath, reason));
                                result.Skipped.Add(request.FilePath);
                                continue;
                            }
                            if (importInPlace)
                            {
                                _logger.LogInformation("[Library Import] {Path} takes the place of {OldPath}, which stays on disk untracked",
                                    request.FilePath, occupant.FilePath);
                            }
                        }

                        // One part holds one file. Importing a second file for a part that
                        // already has one replaces it, the same way a grab upgrade does.
                        // Keeping both leaves the event with two files named "Main Card",
                        // and an integration that keeps one record per part loses one.
                        StagedPartReplacement? stagedPart = null;
                        if (existingFileRecord == null && !importInPlace)
                        {
                            stagedPart = await StageOccupiedPartAsync(existingEvent, partName, partNumber, request.FilePath, occupant);
                        }

                        // A file the event's mode hides goes straight into the parked folder,
                        // so a media server that watches folders never sees it.
                        var parkOnImport = !importInPlace && existingFileRecord == null
                            && FileParking.ImportWillBeParked(existingEvent.Files, occupant ?? stagedPart?.Occupant,
                                string.IsNullOrEmpty(partName), existingEvent, config.EnableMultiPartEpisodes);
                        using var parkedImport = parkOnImport ? FileParking.BeginParkedImport() : null;

                        // Build destination path and transfer file - pass part info and import mode
                        string destinationPath;
                        try
                        {
                            await _episodeNumberResolver.ResolveAsync(existingEvent);
                            destinationPath = importInPlace ? request.FilePath : await TransferFileToLibraryAsync(
                                request.FilePath,
                                existingEvent,
                                parsedInfo,
                                settings,
                                config,
                                partName,
                                partNumber,
                                importMode,
                                modeWasExplicit,
                                parkOnImport);
                        }
                        catch
                        {
                            RestoreStagedPart(stagedPart);
                            throw;
                        }

                        // Declared out here so the rollback below and the
                        // notification after it can both still see them.
                        EventFile linkedFile;
                        EventFile? newlyAdded = null;

                        // The file it replaces is not discarded yet. The
                        // transfer landing is only half of it: until the new
                        // file has a record and the event points at it, the
                        // old one is the only copy of this part there is.
                        // Discarding it here meant a failure anywhere below
                        // reported an unsuccessful import having already
                        // destroyed the part that was working.
                        try
                        {

                        // Update event with new file info
                        existingEvent.FilePath = destinationPath;
                        existingEvent.FileSize = sourceFileSize;
                        existingEvent.Quality = request.Quality ?? _fileParser.BuildQualityString(parsedInfo);
                        existingEvent.LastUpdate = DateTime.UtcNow;

                        // Part name/number already determined above before TransferFileToLibraryAsync

                        if (existingFileRecord != null)
                        {
                            // Update existing EventFile record (re-import)
                            existingFileRecord.FilePath = destinationPath;
                            existingFileRecord.Size = sourceFileSize;
                            existingFileRecord.Quality = request.Quality ?? _fileParser.BuildQualityString(parsedInfo);
                            existingFileRecord.CustomFormatScore = await ExistingFormatScoreAsync(
                                request, existingEvent, existingFileRecord, false);
                            existingFileRecord.PartName = partName;
                            existingFileRecord.PartNumber = partNumber;
                            // A rescan of a recording isn't told it is one, so the
                            // mark is only ever added here, never cleared.
                            existingFileRecord.IsIptvRecording = SourcePrecedence.PreserveRecordingOnRescan(
                                isIptvRecording, existingFileRecord.IsIptvRecording, request.FromDownloadClient);
                            existingFileRecord.LastVerified = DateTime.UtcNow;
                            existingFileRecord.Exists = true;
                            linkedFile = existingFileRecord;

                            _logger.LogInformation("Re-imported file to existing event: {EventTitle} -> {FilePath} (Part: {PartName})",
                                existingEvent.Title, destinationPath, partName ?? "N/A");
                        }
                        else
                        {
                            // Guard against creating a second row for a path some record already
                            // holds (the source path was checked above, but the destination path
                            // can differ, and one file must map to exactly one EventFile row).
                            // Without this, the import would create a duplicate episode.
                            var existingByDest = await FindEventFileByPathAsync(destinationPath);

                            if (existingByDest != null)
                            {
                                var previousOwnerId = existingByDest.EventId;
                                existingByDest.EventId = existingEvent.Id;
                                existingByDest.Size = sourceFileSize;
                                existingByDest.Quality = request.Quality ?? _fileParser.BuildQualityString(parsedInfo);
                                existingByDest.CustomFormatScore = await ExistingFormatScoreAsync(
                                    request, existingEvent, existingByDest, previousOwnerId != existingEvent.Id);
                                existingByDest.PartName = partName;
                                existingByDest.PartNumber = partNumber;
                                existingByDest.IsIptvRecording = SourcePrecedence.PreserveRecordingOnRescan(
                                    isIptvRecording, existingByDest.IsIptvRecording, request.FromDownloadClient);
                                existingByDest.LastVerified = DateTime.UtcNow;
                                existingByDest.Exists = true;
                                existingByDest.IsParked = FileParking.IsParkedPath(destinationPath);
                                linkedFile = existingByDest;
                                await RefreshPreviousOwnerAsync(previousOwnerId, existingEvent.Id,
                                    existingByDest, config);

                                _logger.LogInformation("Re-linked existing file record to event: {EventTitle} -> {FilePath} (Part: {PartName})",
                                    existingEvent.Title, destinationPath, partName ?? "N/A");
                            }
                            else
                            {
                                // Create new EventFile record. User-supplied overrides
                                // (from the FileMetadataEditor) take precedence over the
                                // parser's guesses for Codec / Source / ReleaseGroup /
                                // OriginalTitle / Languages / IndexerFlags.
                                var eventFile = new EventFile
                                {
                                    EventId = existingEvent.Id,
                                    FilePath = destinationPath,
                                    IsParked = FileParking.IsParkedPath(destinationPath),
                                    Size = sourceFileSize,
                                    Quality = request.Quality ?? _fileParser.BuildQualityString(parsedInfo),
                                    Codec = request.Codec ?? parsedInfo.VideoCodec,
                                    Source = request.Source ?? parsedInfo.Source,
                                    ReleaseGroup = request.ReleaseGroup ?? parsedInfo.ReleaseGroup,
                                    OriginalTitle = request.OriginalTitle,
                                    Languages = request.Languages ?? new List<string>(),
                                    IndexerFlags = request.IndexerFlags,
                                    CustomFormatScore = await ImportedFormatScoreAsync(request, existingEvent),
                                    IsIptvRecording = isIptvRecording,
                                    PartName = partName,
                                    PartNumber = partNumber,
                                    Added = DateTime.UtcNow,
                                    LastVerified = DateTime.UtcNow,
                                    Exists = true
                                };
                                _db.EventFiles.Add(eventFile);
                                linkedFile = eventFile;
                                newlyAdded = eventFile;

                                _logger.LogInformation("Imported file to existing event: {EventTitle} -> {FilePath} (Part: {PartName})",
                                    existingEvent.Title, destinationPath, partName ?? "N/A");
                            }
                        }

                        var presentParts = existingEvent.Files
                            .Where(f => f.Exists && !f.IsParked && !ReferenceEquals(f, linkedFile)
                                && !ReferenceEquals(f, stagedPart?.Occupant)
                                && (!importInPlace || !ReferenceEquals(f, occupant)))
                            .Select(f => f.PartNumber)
                            .Append(linkedFile.PartNumber)
                            .ToArray();
                        existingEvent.HasFile = EventPartDetector.AreAllMonitoredPartsPresent(
                            existingEvent.Sport, existingEvent.Title, existingEvent.League?.Name,
                            existingEvent.MonitoredParts, existingEvent.League?.MonitoredParts,
                            presentParts, config.EnableMultiPartEpisodes);

                        // Persist per file so one bad file cannot fail the whole
                        // batch save at the end, and so the row exists before any
                        // concurrent disk scan sees the freshly moved file.
                        if (newlyAdded != null)
                        {
                            linkedFile = await SaveImportedFileAsync(newlyAdded, config);
                        }
                        else
                        {
                            await _db.SaveChangesAsync();
                        }

                        }
                        catch
                        {
                            // The new state never saved, so put the replaced
                            // file back. Where the new file took the same name
                            // this returns the part to what it was; where it
                            // did not, both are on disk and nothing is lost.
                            // Without its row a parked file is lost to the library.
                            // Move it out first, so the restore can use its path.
                            // Leave it when the restore needs the place it would go to.
                            // When the replaced file used the same parked path, its sidecars
                            // are still there, so only the video moves.
                            if (parkOnImport && !RestoreNeedsVisiblePath(stagedPart, destinationPath))
                                FileParking.ReturnUntrackedParkedFile(destinationPath, _logger,
                                    withSidecars: !string.Equals(stagedPart?.OriginalPath, destinationPath, StringComparison.OrdinalIgnoreCase));
                            RestoreStagedPart(stagedPart);
                            throw;
                        }

                        // Saved. Now the file it replaced can go.
                        await CommitStagedPartAsync(stagedPart, linkedFile);
                        if (importInPlace && occupant != null && !ReferenceEquals(occupant, linkedFile))
                        {
                            // The old copy keeps its place on disk; only its record goes,
                            // so the event holds one file for this part.
                            _db.EventFiles.Remove(occupant);
                            await _db.SaveChangesAsync();
                        }

                        // The import is saved. Now the files follow the event's mode, so a whole
                        // file for a "Full Event" event hides the part files from the media server.
                        if (config.EnableMultiPartEpisodes)
                        {
                            // A replaced parked file means the new file must be hidden the same way.
                            await FileParking.ReconcileAndSaveAsync(_db, new[] { existingEvent }, true,
                                _notificationService, _logger,
                                applyMode: occupant?.IsParked == true || stagedPart?.Occupant.IsParked == true);
                        }

                        result.Imported.Add(destinationPath);
                        importedSources.Add(new ImportedSource(request.FilePath, destinationPath,
                            linkedFile.EventId, linkedFile.PartName));
                        notifyQueue.Add((existingEvent, destinationPath,
                            request.Quality ?? _fileParser.BuildQualityString(parsedInfo), linkedFile));

                        // A re-import of a file the event already holds is not a new choice.
                        if (reportMatch && !isReimport)
                        {
                            var suggestion = FileMatchSuggestionCollector.FromImport(request.FilePath, existingEvent,
                                request.SuggestedEventId, createNew: false, carriedSportarrId);
                            if (suggestion != null)
                                matchReports.Add(suggestion);
                        }
                    }
                    else
                    {
                        result.Failed.Add(request.FilePath);
                        result.Errors.Add($"Event not found: {request.EventId}");
                    }
                }
                else if (request.CreateNew)
                {
                    // Create new event first (needed for naming)
                    var eventTitle = request.EventTitle ?? parsedInfo.EventTitle ?? Path.GetFileNameWithoutExtension(request.FilePath);
                    var organization = request.Organization ?? string.Empty;
                    var sport = DeriveEventSport(organization, eventTitle);

                    // Get league if specified
                    League? league = null;
                    if (request.LeagueId.HasValue)
                    {
                        league = await _db.Leagues.FindAsync(request.LeagueId.Value);
                        if (league != null)
                        {
                            sport = league.Sport; // Use league's sport
                        }
                    }

                    // A year without a date gives no air date. The new event still
                    // takes that year, so its folder and file name carry it.
                    // Keep the year out of the parsed date. The scan's date gate reads it.
                    var fileName = Path.GetFileNameWithoutExtension(request.FilePath);
                    var nameYear = ExtractYearFromPath(request.FilePath, fileName,
                        _sportsParser.Parse(fileName).EventYear, null);
                    var newEvent = new Event
                    {
                        Title = eventTitle,
                        Sport = sport,
                        LeagueId = request.LeagueId,
                        League = league,
                        Season = request.Season,
                        EventDate = request.EventDate ?? parsedInfo.AirDate ??
                            (nameYear.HasValue
                                ? new DateTime(nameYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                                : DateTime.UtcNow),
                        FilePath = string.Empty, // Will be set after transfer
                        HasFile = false, // Will be set after transfer
                        FileSize = sourceFileSize,
                        Quality = request.Quality ?? _fileParser.BuildQualityString(parsedInfo),
                        Monitored = false, // Don't monitor imported files by default
                        Added = DateTime.UtcNow
                    };

                    // Add to DB to get ID (needed for folder structure)
                    _db.Events.Add(newEvent);
                    await _db.SaveChangesAsync();

                    // Use manual part info if provided, otherwise auto-detect
                    // IMPORTANT: Determine part info BEFORE transfer so it can be used in filename
                    // NOTE: "Full Event" selection means no part - store as null
                    string? partName = request.PartName;
                    int? partNumber = request.PartNumber;
                    // A league set to "Full Event" keeps one whole file, as if parts were off.
                    var wholeEventImport = fullEventWasSelected
                        || (config.EnableMultiPartEpisodes && !EventPartDetector.UsesParts(
                            config.EnableMultiPartEpisodes, newEvent.MonitoredParts, league?.MonitoredParts));

                    // If user selected "Full Event", treat as no part (null)
                    if (wholeEventImport)
                    {
                        partName = null;
                        partNumber = null;
                    }
                    else if (string.IsNullOrEmpty(partName) && config.EnableMultiPartEpisodes)
                    {
                        // Auto-detect part from filename
                        var partInfo = DetectImportPart(
                            request.FilePath, parsedInfo, sport, newEvent.Title, league?.Name);
                        partName = partInfo?.SegmentName;
                        partNumber = partInfo?.PartNumber;
                    }

                    // The caller can send a part name with no number. The number orders
                    // the parts, so fill it from the name.
                    partNumber ??= EventPartDetector.ResolvePartNumber(
                        partName, sport, newEvent.Title, league?.Name);

                    // Build destination path and transfer file - pass part info and import mode
                    var destinationPath = await TransferFileToLibraryAsync(
                        request.FilePath,
                        newEvent,
                        parsedInfo,
                        settings,
                        config,
                        partName,
                        partNumber,
                        importMode,
                        modeWasExplicit);

                    // Update event with file path
                    newEvent.FilePath = destinationPath;
                    newEvent.HasFile = EventPartDetector.AreAllMonitoredPartsPresent(
                        newEvent.Sport, newEvent.Title, league?.Name,
                        newEvent.MonitoredParts, league?.MonitoredParts,
                        new int?[] { partNumber }, config.EnableMultiPartEpisodes);

                    // The create-new path needs the same destination-path guard as
                    // the existing-event path: another file earlier in this batch
                    // (or a concurrent scan during a slow transfer) may already own
                    // a row for this path, and a second insert violates the unique
                    // FilePath index.
                    var fileAtDestination = await FindEventFileByPathAsync(destinationPath);
                    var isIptvRecording = request.IsIptvRecording
                        || (!request.FromDownloadClient &&
                            (fileAtDestination?.IsIptvRecording == true ||
                             string.Equals(request.Source, "IPTV", StringComparison.OrdinalIgnoreCase) ||
                             (fileAtDestination?.ReleaseTitle == null &&
                              SourcePrecedence.LooksLikeIptvRecording(request.FilePath,
                                  request.Quality ?? _fileParser.BuildQualityString(parsedInfo)))));
                    EventFile eventFile;
                    if (fileAtDestination != null)
                    {
                        var previousOwnerId = fileAtDestination.EventId;
                        fileAtDestination.EventId = newEvent.Id;
                        fileAtDestination.Size = sourceFileSize;
                        fileAtDestination.Quality = request.Quality ?? _fileParser.BuildQualityString(parsedInfo);
                        fileAtDestination.CustomFormatScore = await ExistingFormatScoreAsync(
                            request, newEvent, fileAtDestination, previousOwnerId != newEvent.Id);
                        fileAtDestination.PartName = partName;
                        fileAtDestination.PartNumber = partNumber;
                        fileAtDestination.LastVerified = DateTime.UtcNow;
                        fileAtDestination.Exists = true;
                        fileAtDestination.IsIptvRecording = SourcePrecedence.PreserveRecordingOnRescan(
                            isIptvRecording, fileAtDestination.IsIptvRecording, request.FromDownloadClient);
                        eventFile = fileAtDestination;
                        await RefreshPreviousOwnerAsync(previousOwnerId, newEvent.Id,
                            fileAtDestination, config);
                        await _db.SaveChangesAsync();

                        _logger.LogInformation("Re-linked existing file record to new event: {EventTitle} -> {FilePath} (Part: {PartName})",
                            newEvent.Title, destinationPath, partName ?? "N/A");
                    }
                    else
                    {
                        // Create EventFile record (part info already determined above).
                        // User-supplied overrides take precedence over parser values.
                        eventFile = new EventFile
                        {
                            EventId = newEvent.Id,
                            FilePath = destinationPath,
                            Size = sourceFileSize,
                            Quality = request.Quality ?? _fileParser.BuildQualityString(parsedInfo),
                            Codec = request.Codec ?? parsedInfo.VideoCodec,
                            Source = request.Source ?? parsedInfo.Source,
                            ReleaseGroup = request.ReleaseGroup ?? parsedInfo.ReleaseGroup,
                            OriginalTitle = request.OriginalTitle,
                            Languages = request.Languages ?? new List<string>(),
                            IndexerFlags = request.IndexerFlags,
                            CustomFormatScore = await ImportedFormatScoreAsync(request, newEvent),
                            IsIptvRecording = isIptvRecording,
                            PartName = partName,
                            PartNumber = partNumber,
                            Added = DateTime.UtcNow,
                            LastVerified = DateTime.UtcNow,
                            Exists = true
                        };
                        _db.EventFiles.Add(eventFile);
                        eventFile = await SaveImportedFileAsync(eventFile, config);
                    }

                    result.Created.Add(destinationPath);
                    importedSources.Add(new ImportedSource(request.FilePath, destinationPath,
                        eventFile.EventId, eventFile.PartName));
                    notifyQueue.Add((newEvent, destinationPath,
                        request.Quality ?? _fileParser.BuildQualityString(parsedInfo), eventFile));
                    _logger.LogInformation("Created new event from file: {EventTitle} -> {FilePath} (Part: {PartName})",
                        newEvent.Title, destinationPath, partName ?? "N/A");
                }
                else
                {
                    result.Skipped.Add(request.FilePath);
                }
            }
            catch (Exception ex)
            {
                // Discard this file's pending changes so a failed file cannot
                // poison the saves of every file after it in the batch.
                foreach (var entry in _db.ChangeTracker.Entries()
                    .Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
                {
                    entry.State = entry.State == EntityState.Added
                        ? EntityState.Detached
                        : EntityState.Unchanged;
                }

                _logger.LogError(ex, "Failed to import file: {FilePath}", request.FilePath);
                result.Failed.Add(request.FilePath);
                result.Errors.Add($"{Path.GetFileName(request.FilePath)}: {ex.Message}");
            }
        }

        await CompletePendingImportsOfAsync(importedSources);
        await _db.SaveChangesAsync();

        await ReportFileMatchesAsync(matchReports);

        // NOTIFICATIONS: manual imports fire the same OnDownload
        // notifications as automatic imports. Previously only the
        // download-queue path notified, so wizard-imported files never
        // triggered a media-server library refresh until a manual scan.
        foreach (var (evt, path, quality, file) in notifyQueue)
        {
            try
            {
                await _metadataWriterService.WriteEventMetadataAsync(evt, file, evt.League);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Import] Failed to write local metadata for manual import: {Path}", path);
            }

            try
            {
                await _notificationService.SendNotificationAsync(
                    NotificationTrigger.OnDownload,
                    $"Imported: {evt.Title}",
                    $"File: {Path.GetFileName(path)}\nQuality: {quality}",
                    new NotificationEventData
                    {
                        EventId = evt.Id,
                        EventExternalId = evt.ExternalId,
                        EventTitle = evt.Title ?? "",
                        League = evt.League?.Name,
                        Sport = evt.Sport,
                        File = new NotificationFileData { Path = path, Quality = quality, Size = file.Size }
                    },
                    evt.League?.Tags);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Import] Failed to send notification for manual import: {Path}", path);
            }
        }

        _logger.LogInformation(
            "Import complete: {Imported} imported, {Created} created, {Skipped} skipped, {Failed} failed",
            result.Imported.Count, result.Created.Count, result.Skipped.Count, result.Failed.Count);

        return result;
    }

    /// <summary>
    /// Complete each pending row of a file that this batch imported. A disk
    /// scan, the watcher or a rescan can list a file in Activity that the
    /// user then imports from Library Import. Without this step the row
    /// stays in the pending list. The row is completed as the accept
    /// endpoint completes it. A rejected row is already gone, and a row in
    /// another state is not changed. When a recording choice row is present,
    /// this method saves all changes under the recording choice gate.
    /// Otherwise the caller saves.
    /// </summary>
    private async Task CompletePendingImportsOfAsync(IReadOnlyCollection<ImportedSource> importedFiles)
    {
        if (importedFiles.Count == 0)
            return;
        var destinations = new Dictionary<string, ImportedSource>(SourcePathComparer);
        foreach (var file in importedFiles)
            destinations[file.Source] = file;
        var pending = await _db.PendingImports
            .Where(p => p.Status == PendingImportStatus.Pending)
            .ToListAsync();
        var matched = pending.Where(p => destinations.ContainsKey(p.FilePath)).ToList();
        foreach (var row in matched.Where(p => !IsDvrChoiceRow(p)))
            CompletePendingRow(row);

        var choices = matched.Where(IsDvrChoiceRow).ToList();
        if (choices.Count == 0)
            return;
        // Recording retention and the accept endpoint resolve the same rows
        // under this gate. Without it, retention can remove a row between the
        // read above and the save, and the save then fails for the whole batch.
        await DvrIndexerChoice.EnterGateAsync();
        try
        {
            foreach (var row in choices)
            {
                var status = await _db.PendingImports.AsNoTracking()
                    .Where(p => p.Id == row.Id)
                    .Select(p => (PendingImportStatus?)p.Status)
                    .FirstOrDefaultAsync();
                if (status != PendingImportStatus.Pending || DvrIndexerChoice.IsImporting(row.Id))
                {
                    _logger.LogInformation("[Import] Pending import {Id} was resolved by another step, so it stays as it is",
                        row.Id);
                    continue;
                }
                CompletePendingRow(row);
                await MarkDvrChoiceRecordingImportedAsync(row, destinations[row.FilePath]);
            }
            await _db.SaveChangesAsync();
        }
        finally
        {
            DvrIndexerChoice.Gate.Release();
        }
    }

    private static bool IsDvrChoiceRow(PendingImport row) =>
        row.DownloadId.StartsWith("dvr-", StringComparison.Ordinal);

    private void CompletePendingRow(PendingImport row)
    {
        row.Status = PendingImportStatus.Completed;
        row.ResolvedAt = DateTime.UtcNow;
        row.ErrorMessage = null;
        _logger.LogInformation("[Import] Completed pending import {Id} because its file was imported: {Path}",
            row.Id, row.FilePath);
    }

    /// <summary>
    /// A "dvr-N" row asks the user to choose between a recording and a
    /// downloaded file. When the user imports the recording's file here, mark
    /// the recording imported, as the accept endpoint does. Otherwise the
    /// recording stays completed, and the next DVR import pass marks it
    /// failed when a move took its file away. The caller holds the recording
    /// choice gate and saves.
    /// </summary>
    private async Task MarkDvrChoiceRecordingImportedAsync(PendingImport row, ImportedSource file)
    {
        if (!row.DownloadId.StartsWith("dvr-", StringComparison.Ordinal)
            || !int.TryParse(row.DownloadId.AsSpan(4), out var recordingId))
            return;
        var recording = await _db.DvrRecordings.FirstOrDefaultAsync(r => r.Id == recordingId);
        // A recording that has changed since the row was written is not this file.
        if (recording == null || recording.Status != DvrRecordingStatus.Completed
            || recording.OutputPath == null || !SourcePathComparer.Equals(recording.OutputPath, row.FilePath))
            return;
        // The accept endpoint takes a recording only for its own event and part.
        // A recording that points at the file of another event would delete
        // that file when the user deletes the recording with its file.
        var recordingPart = EventPartDetector.IsFullEvent(recording.PartName) ? null : recording.PartName;
        var filePart = EventPartDetector.IsFullEvent(file.PartName) ? null : file.PartName;
        if (recording.EventId != file.EventId
            || !string.Equals(recordingPart, filePart, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[Import] Recording {RecordingId} stays as it is because its file went to another event or part",
                recording.Id);
            return;
        }
        // A copy or a hardlink keeps the capture in place. The recording then
        // keeps its own path, so a delete of the recording removes the capture
        // and not the library file.
        if (!File.Exists(file.Source))
            recording.OutputPath = file.Destination;
        recording.Status = DvrRecordingStatus.Imported;
        recording.ImportedAt = DateTime.UtcNow;
        recording.LastUpdated = DateTime.UtcNow;
        recording.ErrorMessage = null;
        _logger.LogInformation("[Import] Marked recording {RecordingId} imported because its file was imported: {Path}",
            recording.Id, file.Destination);
    }

    /// <summary>A file that a batch imported, and the event and part it went to.</summary>
    private readonly record struct ImportedSource(string Source, string Destination, int EventId, string? PartName);

    /// <summary>
    /// Compares source paths. Windows paths ignore case, so a pending row
    /// can spell the path another way than the import request.
    /// </summary>
    internal static StringComparer SourcePathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Transfer file to library folder with proper naming
    /// </summary>
    /// <summary>
    /// Finds the EventFile owning a path, including rows added earlier in the
    /// current batch that are not yet saved. The DB-only lookup this replaces
    /// let two files in one batch claim the same destination path and blow up
    /// the unique FilePath index at save time.
    /// </summary>
    private async Task<EventFile?> FindEventFileByPathAsync(string path)
    {
        return _db.EventFiles.Local.FirstOrDefault(f => f.FilePath == path)
            ?? await _db.EventFiles.FirstOrDefaultAsync(f => f.FilePath == path);
    }

    /// <summary>
    /// Saves a freshly added EventFile row. When another writer created a row
    /// for the same path between our lookup and the save (a disk scan racing a
    /// slow transfer is the realistic case), the pending insert is merged into
    /// the winning row instead of failing the file on the unique index.
    /// </summary>
    private async Task<EventFile> SaveImportedFileAsync(EventFile pending, Config config)
    {
        try
        {
            await _db.SaveChangesAsync();
            return pending;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message.Contains("EventFiles.FilePath") == true)
        {
            _db.Entry(pending).State = EntityState.Detached;
            var winner = await _db.EventFiles.FirstAsync(f => f.FilePath == pending.FilePath);
            var previousOwnerId = winner.EventId;
            winner.EventId = pending.EventId;
            winner.Size = pending.Size;
            winner.Quality = pending.Quality;
            winner.PartName = pending.PartName;
            winner.PartNumber = pending.PartNumber;
            winner.LastVerified = DateTime.UtcNow;
            winner.Exists = true;
            await RefreshPreviousOwnerAsync(previousOwnerId, pending.EventId, winner, config);
            await _db.SaveChangesAsync();

            _logger.LogInformation("[Import] Merged import into the existing file record for {Path} (another writer created it first)",
                pending.FilePath);
            return winner;
        }
    }

    private async Task RefreshPreviousOwnerAsync(int previousOwnerId, int newOwnerId,
        EventFile movedFile, Config config)
    {
        if (previousOwnerId == newOwnerId)
            return;

        var previous = await _db.Events.Include(e => e.League)
            .FirstOrDefaultAsync(e => e.Id == previousOwnerId);
        if (previous == null)
            return;

        var remaining = await _db.EventFiles
            .Where(f => f.EventId == previousOwnerId && f.Id != movedFile.Id && f.Exists && !f.IsParked)
            .ToListAsync();
        if (string.Equals(previous.FilePath, movedFile.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            var selected = remaining.FirstOrDefault();
            previous.FilePath = selected?.FilePath;
            previous.FileSize = selected?.Size;
            previous.Quality = selected?.Quality;
        }

        previous.HasFile = EventPartDetector.AreAllMonitoredPartsPresent(
            previous.Sport, previous.Title, previous.League?.Name,
            previous.MonitoredParts, previous.League?.MonitoredParts,
            remaining.Select(f => f.PartNumber).ToArray(), config.EnableMultiPartEpisodes);
    }

    private async Task<string> TransferFileToLibraryAsync(
        string sourcePath,
        Event eventInfo,
        ParsedFileInfo parsed,
        MediaManagementSettings settings,
        Config config,
        string? partName,
        int? partNumber,
        LibraryImportMode importMode,
        bool modeWasExplicit,
        bool park = false)
    {
        var sourceFileInfo = new FileInfo(sourcePath);
        var extension = sourceFileInfo.Extension;

        // Resolve the root folder for this league. Prefers the league's
        // explicit RootFolderId binding (set via the Add League modal),
        // falls back to the legacy free-space heuristic when the league
        // doesn't have one or the bound folder is missing/inaccessible.
        var rootFolders = await RootFolderLoader.LoadAsync(_db, _diskSpaceService);
        var rootFolder = await GetRootFolderForLeagueAsync(settings, rootFolders, eventInfo.League, sourceFileInfo.Length);

        // Build destination path
        var destinationPath = rootFolder;

        // IMPORTANT: Fetch episode number from API BEFORE building folder path
        // This ensures the {Episode} token in EventFolderFormat has the correct value
        // Episode number is the source of truth from sportarr.net API for Plex/Jellyfin/Emby metadata
        var episodeNumber = await _episodeNumberResolver.ResolveAsync(eventInfo);
        if (episodeNumber != eventInfo.EpisodeNumber)
        {
            eventInfo.EpisodeNumber = episodeNumber;
            _logger.LogDebug("[Import] Set episode number to E{EpisodeNumber} from API for event {EventTitle}",
                episodeNumber, eventInfo.Title);
        }

        // Build folder path using granular folder settings (league/season/event folders)
        // Now uses the correct episode number from API
        var folderPath = _namingService.BuildFolderPath(settings, eventInfo);
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            destinationPath = Path.Combine(destinationPath, folderPath);
        }

        // Build filename
        // Note: Use RenameEvents setting (same as FileRenameService) so user has single setting to control renaming
        // RenameFiles was a separate setting that caused confusion - imports should respect RenameEvents
        string filename;
        if (settings.RenameEvents)
        {
            // The part tokens come from the part the caller stores, the same way a
            // rename builds them. A whole-event file gets no part token.
            var (partSuffix, partNameSuffix) = FileNamingService.PartTokens(partName, partNumber);

            // Filename tokens use BroadcastDate (broadcaster-branded);
            // see FileRenameService for the rationale.
            var brandingDate = eventInfo.BroadcastDate ?? eventInfo.EventDate.Date;

            var tokens = new FileNamingTokens
            {
                EventTitle = eventInfo.Title,
                EventTitleThe = eventInfo.Title,
                SportarrId = eventInfo.ExternalId ?? string.Empty,
                AirDate = brandingDate,
                Quality = parsed.Quality ?? "Unknown",
                QualityFull = _fileParser.BuildQualityString(parsed),
                ReleaseGroup = parsed.ReleaseGroup ?? string.Empty,
                OriginalTitle = parsed.EventTitle,
                OriginalFilename = Path.GetFileNameWithoutExtension(sourcePath),
                Series = eventInfo.League?.Name ?? eventInfo.Sport,
                Season = eventInfo.SeasonNumber?.ToString("0000") ?? eventInfo.Season ?? brandingDate.Year.ToString(),
                Episode = episodeNumber.ToString("00"),
                Part = partSuffix,
                PartName = partNameSuffix
            };

            // No grabbed release to source IndexerFlags from here - this path
            // imports pre-existing files found on disk, not an active download.
            tokens.CustomFormats = _customFormatService.BuildRenameToken(
                Path.GetFileName(sourcePath), await _db.CustomFormats.ToListAsync(), sourceFileInfo.Length);

            filename = _namingService.BuildFileName(settings.StandardFileFormat, tokens, extension, settings.ReplaceIllegalCharacters);
        }
        else
        {
            filename = Path.GetFileName(sourcePath);
        }

        destinationPath = Path.Combine(destinationPath, filename);
        if (park)
            destinationPath = FileParking.ParkedPathFor(destinationPath);

        // Check if source and destination are the same file (file already in correct location)
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[Import] File already in correct location, skipping transfer: {FilePath}", sourcePath);
            return sourcePath;
        }

        // Handle duplicates - but only if destination is a DIFFERENT file than source
        // GetUniqueFilePath adds (1), (2), etc. if destination exists
        destinationPath = GetUniqueFilePath(destinationPath);

        _logger.LogInformation("[Import] Transferring: {Source} -> {Destination}", sourcePath, destinationPath);

        // Create destination directory
        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
            _logger.LogDebug("Created directory: {Directory}", destDir);
        }
        // A parked folder needs its marker files before the file lands.
        if (park && !string.IsNullOrEmpty(destDir))
            FileParking.EnsureParkedFolder(destDir);

        // Transfer file based on import mode
        await TransferFileAsync(sourcePath, destinationPath, settings, importMode, modeWasExplicit);

        // Set permissions (Linux/macOS only)
        if (settings.SetPermissions && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetFilePermissions(destinationPath, settings);
        }

        return destinationPath;
    }

    private async Task SweepEmptiedSourceFolderAsync(string movedSource)
    {
        try
        {
            var candidate = LeftoverFolderPolicy.ResolveOwnedFolder(
                movedSource, Path.GetFileNameWithoutExtension(movedSource));
            if (candidate == null)
                return;

            var rootFolders = await _db.RootFolders.Select(r => r.Path).ToListAsync();
            var clients = await _db.DownloadClients
                .Select(c => new { c.Directory, c.BlackholeFolder, c.WatchFolder })
                .ToListAsync();

            var protectedPaths = new List<string>(rootFolders);
            foreach (var client in clients)
            {
                protectedPaths.Add(client.Directory ?? "");
                protectedPaths.Add(client.BlackholeFolder ?? "");
                protectedPaths.Add(client.WatchFolder ?? "");
            }

            // A library import's source is whatever folder the user chose, so
            // unlike a download client's own directory there is no telling
            // leftover release metadata from someone's notes sitting beside
            // an archived video. Only a folder holding nothing at all is
            // removed here; the metadata-tolerant sweep is reserved for
            // paths a download client reported.
            if (!LeftoverFolderPolicy.MayRemove(candidate, protectedPaths, out var full) || full == null)
            {
                return;
            }

            Directory.Delete(full, recursive: true);
            _logger.LogInformation("[Transfer] Removed the emptied source folder: {Folder}", full);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Transfer] Leftover folder sweep failed for: {Path}", movedSource);
        }
    }

    /// <summary>
    /// Resolve the root folder a league's media should be written into.
    /// Prefers the explicit binding stored on the league, falls back to
    /// the legacy free-space heuristic for legacy leagues without a
    /// binding or whose bound root has gone missing.
    /// </summary>
    private Task<string> GetRootFolderForLeagueAsync(MediaManagementSettings settings, List<RootFolder> rootFolders, League? league, long fileSize)
    {
        if (rootFolders == null || rootFolders.Count == 0)
        {
            throw new Exception("No root folders configured. Please add a root folder in Settings > Media Management.");
        }

        if (league?.RootFolderId is int boundId)
        {
            var bound = rootFolders.FirstOrDefault(rf => rf.Id == boundId);
            if (bound != null && bound.Accessible)
            {
                return Task.FromResult(bound.Path);
            }
            _logger.LogWarning(
                "[Root Folders] League {LeagueId} ({LeagueName}) is bound to RootFolderId={BoundId} but it's missing or inaccessible — falling back to free-space selection.",
                league.Id, league.Name, boundId);
        }

        var accessibleRoots = rootFolders
            .Where(rf => rf.Accessible)
            .OrderByDescending(rf => rf.FreeSpace)
            .ToList();

        if (accessibleRoots.Count == 0)
        {
            throw new Exception("No accessible root folders configured. Please add a root folder in Settings > Media Management.");
        }

        var fileSizeMB = fileSize / 1024 / 1024;
        var folder = accessibleRoots.FirstOrDefault(rf => rf.FreeSpace > fileSizeMB + settings.MinimumFreeSpace);

        if (folder == null)
        {
            folder = accessibleRoots.First();
            _logger.LogWarning("No root folder has enough free space, using folder with most space: {Path}", folder.Path);
        }

        return Task.FromResult(folder.Path);
    }

    /// <summary>
    /// Get unique file path (add number if file exists)
    /// </summary>
    private string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path)!;
        var filenameWithoutExt = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        var counter = 1;
        string newPath;

        do
        {
            newPath = Path.Combine(directory, $"{filenameWithoutExt} ({counter}){extension}");
            counter++;
        }
        while (File.Exists(newPath));

        return newPath;
    }

    /// <summary>
    /// Transfer file based on import mode.
    /// - Move: Moves files from source to destination (regular files only)
    /// - Copy: Creates hardlinks (if UseHardlinks enabled) or copies files
    /// - Symlinks: Always use copy/hardlink to preserve debrid streaming behavior
    ///   Moving symlinks would break debrid streaming as the link target wouldn't be accessible
    /// Manual import lets the user choose between Move and Copy.
    /// </summary>
    private async Task TransferFileAsync(string source, string destination, MediaManagementSettings settings, LibraryImportMode importMode, bool modeWasExplicit = false)
    {
        // Check if source is a symlink (common with debrid services like Decypharr)
        var sourceFileInfo = new FileInfo(source);
        var isSymlink = sourceFileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);

        _logger.LogInformation("[Transfer] Library Import: Mode={ImportMode}, CopyFiles={CopyFiles}, UseHardlinks={UseHardlinks}, IsSymlink={IsSymlink}",
            importMode, settings.CopyFiles, settings.UseHardlinks, isSymlink);

        // For symlinks (debrid services), recreate the symlink at the destination.
        // This preserves debrid streaming — we don't copy file contents, we create
        // a new symlink pointing to the same target.
        if (isSymlink)
        {
            _logger.LogInformation("[Transfer] Symlink detected (debrid service) - recreating symlink to preserve streaming");
            await CopySymbolicLinkAsync(source, destination);
            return;
        }

        // Move mode: Move the file (regular files only - symlinks handled above)
        if (importMode == LibraryImportMode.Move)
        {
            File.Move(source, destination, overwrite: false);
            _logger.LogInformation("[Transfer] File moved: {Source} -> {Destination}", source, destination);

            // The move empties the release's own folder and nothing else ever
            // looked back at it, so a bulk import from a download directory
            // left one dead folder per release behind. The sweep considers
            // the parent only when it is named after this file's release, so
            // the folder the user pointed the import at, a category folder,
            // or any shared directory can never match. The sweeper's own
            // guards then refuse anything holding video, a protected path,
            // or more than metadata.
            await SweepEmptiedSourceFolderAsync(source);
            return;
        }

        // Hardlink mode: try to link, then fall back. Copy mode never upgrades
        // to a hardlink - Copy and Hardlink are separate choices in the import
        // UI, so an explicit Copy must produce real duplicate bytes. (Auto mode
        // already resolves to Hardlink when Use Hardlinks is enabled.)
        // Hardlinks let two paths reference the same on-disk bytes without duplicating storage.
        if (importMode == LibraryImportMode.Hardlink)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    CreateHardLinkWindows(source, destination);
                }
                else
                {
                    CreateHardLinkUnix(source, destination);
                }
                _logger.LogInformation("[Transfer] File hardlinked successfully: {Source} -> {Destination}", source, destination);
                return;
            }
            catch (Exception ex)
            {
                // Check for cross-device/cross-volume errors
                var message = ex.Message.ToLowerInvariant();
                var isCrossDevice = message.Contains("cross-device") ||
                    message.Contains("different file systems") ||
                    message.Contains("invalid cross-device link") ||
                    message.Contains("different volume") ||
                    message.Contains("not on the same disk");

                if (isCrossDevice)
                {
                    _logger.LogWarning("[Transfer] Hardlink failed (cross-device/volume)");
                }
                else
                {
                    _logger.LogWarning(ex, "[Transfer] Hardlink failed");
                }

                // Explicitly requested hardlinks always fall back to COPY, never move.
                // The user asked for links to keep the source seedable, so deleting
                // the source on a fallback would defeat the purpose of the request.
                if (modeWasExplicit)
                {
                    _logger.LogInformation("[Transfer] Hardlink was explicitly requested - falling back to copy to preserve the source file");
                }
                // CRITICAL FIX: When an auto-resolved hardlink fails and CopyFiles=false,
                // fall back to MOVE not copy. This prevents duplicates when the user has
                // "Copy Files" disabled but hardlinks enabled (common on Unraid where
                // downloads are on NVMe and library is on the array).
                else if (!settings.CopyFiles)
                {
                    _logger.LogInformation("[Transfer] CopyFiles=false, falling back to MOVE instead of copy");
                    File.Move(source, destination, overwrite: false);
                    _logger.LogInformation("[Transfer] File moved (hardlink fallback): {Source} -> {Destination}", source, destination);
                    await SweepEmptiedSourceFolderAsync(source);
                    return;
                }
                else
                {
                    _logger.LogInformation("[Transfer] CopyFiles=true, falling back to copy");
                }
                // Fall through to copy
            }
        }

        // Copy the file (CopyFiles=true and hardlink disabled or failed)
        await CopyFileAsync(source, destination);
        _logger.LogInformation("[Transfer] File copied: {Source} -> {Destination}", source, destination);
    }

    /// <summary>
    /// Copy a symbolic link to a new location, preserving the symlink target.
    /// Used for debrid-service compatibility so we don't materialize streamed bytes.
    /// </summary>
    private async Task CopySymbolicLinkAsync(string source, string destination)
    {
        var fileInfo = new FileInfo(source);
        var linkTarget = fileInfo.LinkTarget ?? fileInfo.ResolveLinkTarget(returnFinalTarget: false)?.FullName;

        if (string.IsNullOrEmpty(linkTarget))
        {
            throw new IOException($"Could not resolve symlink target for: {source}");
        }

        _logger.LogDebug("[Transfer] Recreating symlink: {Source} -> {Destination} (target: {Target})",
            source, destination, linkTarget);

        // Determine if we should use relative or absolute path
        // If the original link was relative, try to preserve that
        var isRelative = !Path.IsPathRooted(fileInfo.LinkTarget ?? "");

        if (isRelative)
        {
            // Calculate relative path from new destination to target
            var destDir = Path.GetDirectoryName(destination) ?? "";
            var relativePath = Path.GetRelativePath(destDir, linkTarget);
            await Task.Run(() => File.CreateSymbolicLink(destination, relativePath));
        }
        else
        {
            await Task.Run(() => File.CreateSymbolicLink(destination, linkTarget));
        }

        _logger.LogInformation("[Transfer] Symlink recreated for debrid: {Source} -> {Destination}", source, destination);
    }

    /// <summary>
    /// Copy file asynchronously
    /// </summary>
    private async Task CopyFileAsync(string source, string destination)
    {
        const int bufferSize = 81920; // 80KB buffer

        using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);

        // CreateNew, not Create. GetUniqueFilePath proved this name was free a
        // moment ago, and the move path already refuses to overwrite, so a file
        // sitting here now means a second import claimed the same name in
        // between. Truncating it destroyed whatever that import had written.
        // Failing is the honest outcome, and the caller reports it per file.
        using var destStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

        await sourceStream.CopyToAsync(destStream);
        _logger.LogInformation("File copied successfully");
    }

    /// <summary>
    /// Create hardlink on Unix/Linux/macOS using ln command
    /// </summary>
    private void CreateHardLinkUnix(string source, string destination)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ln",
                Arguments = $"\"{source}\" \"{destination}\"",
                UseShellExecute = false,
                RedirectStandardError = true
            }
        };

        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new Exception($"Failed to create hardlink: {error}");
        }
    }

    /// <summary>
    /// Create hardlink on Windows using kernel32.dll CreateHardLink
    /// Note: Hardlinks only work on the same volume (e.g., same drive letter)
    /// </summary>
    private void CreateHardLinkWindows(string source, string destination)
    {
        // Windows CreateHardLink API: CreateHardLink(newFileName, existingFileName, securityAttributes)
        // Returns true on success, false on failure
        if (!NativeMethods.CreateHardLink(destination, source, IntPtr.Zero))
        {
            var errorCode = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            var errorMessage = errorCode switch
            {
                1 => "Invalid function",
                5 => "Access denied - check permissions",
                17 => "Cannot create a file when that file already exists",
                32 => "The process cannot access the file because it is being used by another process",
                1142 => "An attempt was made to create more than the maximum number of links to a file",
                _ when errorCode >= 1 && errorCode <= 20 => $"Path/drive error (code {errorCode})",
                _ => $"Error code {errorCode}"
            };

            // Check if it's a cross-volume error (error 17 can mean different volumes on some Windows versions)
            if (errorCode == 1142 || !AreSameVolume(source, destination))
            {
                throw new Exception($"Hardlink failed - files are on different volumes or too many links");
            }

            throw new Exception($"Failed to create hardlink: {errorMessage}");
        }
    }

    /// <summary>
    /// Check if two paths are on the same volume (required for hardlinks on Windows)
    /// </summary>
    private static bool AreSameVolume(string path1, string path2)
    {
        try
        {
            var root1 = Path.GetPathRoot(path1)?.ToUpperInvariant();
            var root2 = Path.GetPathRoot(path2)?.ToUpperInvariant();
            return root1 == root2;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Native Windows methods for hardlink creation
    /// </summary>
    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
    }

    /// <summary>
    /// Set file permissions (Unix only)
    /// </summary>
    private void SetFilePermissions(string path, MediaManagementSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.FileChmod))
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"{settings.FileChmod} \"{path}\"",
                    UseShellExecute = false
                }
            };
            process.Start();
            process.WaitForExit();
        }

        if (!string.IsNullOrEmpty(settings.ChownUser))
        {
            var chown = settings.ChownUser;
            if (!string.IsNullOrEmpty(settings.ChownGroup))
                chown += ":" + settings.ChownGroup;

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chown",
                    Arguments = $"{chown} \"{path}\"",
                    UseShellExecute = false
                }
            };
            process.Start();
            process.WaitForExit();
        }
    }

    /// <summary>
    /// A file that an incoming import replaces. The file is renamed aside so
    /// the transfer can take its name, and it stays on disk until the transfer
    /// succeeds.
    /// </summary>
    private sealed record StagedPartReplacement(EventFile Occupant, string OriginalPath, string? StagedPath);

    private static bool RestoreNeedsVisiblePath(StagedPartReplacement? staged, string parkedPath) =>
        staged != null
        && string.Equals(staged.OriginalPath, FileParking.OriginalPathFor(parkedPath), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Why the file may not take the place of the file this event already
    /// holds, as Library Import shows it. Empty when the event holds no file
    /// or the file is an upgrade.
    /// </summary>
    /// <summary>The reason an equal copy is left where it is.</summary>
    internal const string EqualCopyRejection = "The event already has a file as good as this one.";
    internal const string NoComparableFileRejection = "The event holds no file for this part to compare with.";

    /// <summary>
    /// The mode an import uses when the caller names none. A hardlink or a
    /// copy leaves the source file where it is; a move takes it away.
    /// </summary>
    private static LibraryImportMode DefaultImportMode(MediaManagementSettings settings) =>
        settings.UseHardlinks
            ? LibraryImportMode.Hardlink
            : settings.CopyFiles
                ? LibraryImportMode.Copy
                : LibraryImportMode.Move;

    private EventPartInfo? DetectImportPart(
        string filePath, ParsedFileInfo parsedInfo, string sport, string? eventTitle, string? leagueName)
    {
        var fromFilename = PartIdentityResolver.Resolve(
            null, null, filePath, sport, eventTitle, leagueName, true);
        if (fromFilename.Kind == PartIdentityKind.InferredPart)
            return fromFilename.Part;
        if (fromFilename.Kind is PartIdentityKind.CompleteEventLabel or PartIdentityKind.Ambiguous
            or PartIdentityKind.UnsupportedLabel)
            return null;

        return _partDetector.DetectPart(parsedInfo.EventTitle, sport, eventTitle, leagueName);
    }

    private async Task<(List<string> Rejections, bool InPlace)> UpgradeRejectionsAsync(Event evt, string filePath, ParsedFileInfo parsedInfo)
    {
        var none = (new List<string>(), false);
        // HasFile is false for a multi-part event holding only some parts,
        // so a held part file is looked for before giving up.
        if (!evt.HasFile && !await _db.EventFiles.AnyAsync(f => f.EventId == evt.Id && f.Exists)) return none;
        var config = await _configService.GetConfigAsync();
        var held = await _db.EventFiles.AsNoTracking()
            .Where(f => f.EventId == evt.Id && f.Exists)
            .ToListAsync();
        int? partNumber = null;
        var usesParts = EventPartDetector.UsesParts(config.EnableMultiPartEpisodes, evt);
        if (usesParts && !string.IsNullOrEmpty(evt.Sport))
        {
            partNumber = DetectImportPart(filePath, parsedInfo, evt.Sport, evt.Title, evt.League?.Name)?.PartNumber;
        }
        var occupant = ImportUpgradeRule.ExistingFileForPart(held, partNumber, filePath, config.EnableMultiPartEpisodes);
        if (occupant == null) return none;
        var inPlace = IsBesideOccupant(filePath, occupant.FilePath);
        var quality = _fileParser.BuildQualityString(parsedInfo);
        var decision = await DecideUpgradeAsync(occupant, filePath, quality, evt,
                            SourcePrecedence.LooksLikeIptvRecording(filePath, quality));
        // The same test the import makes, so the scan shows every copy an
        // automatic import would leave out, the equal one included.
        var leftOut = decision.Equal
            && (inPlace || DefaultImportMode(await GetMediaManagementSettingsAsync()) != LibraryImportMode.Move);
        if (decision.IsUpgrade && !leftOut) return (new List<string>(), inPlace);
        var rejection = decision.Rejection ?? EqualCopyRejection;
        return (new List<string> { rejection }, inPlace);
    }

    /// <summary>
    /// Compare the incoming file with the event's current file. Score the
    /// incoming name against the profile. Keep the held file's saved score
    /// when its library name no longer contains the original format tag.
    /// </summary>
    private async Task<ImportUpgradeRule.Decision> DecideUpgradeAsync(EventFile occupant, string incomingPath, string? incomingQuality, Event evt,
        bool incomingIsIptvRecording)
    {
        var config = await _configService.GetConfigAsync();
        var incomingName = Path.GetFileNameWithoutExtension(incomingPath);
        var occupantName = occupant.OriginalTitle ?? Path.GetFileNameWithoutExtension(occupant.FilePath ?? string.Empty);
        var profile = await QualityProfileAsync(evt);
        return ImportUpgradeRule.Evaluate(
            occupant.Quality, occupant.CustomFormatScore, occupantName,
            incomingQuality, await FormatScoreAsync(incomingName, profile), incomingName,
            config.DownloadPropersAndRepacks, profile,
            SourcePrecedence.Compare(config, incomingIsIptvRecording, occupant.IsIptvRecording));
    }

    // Custom formats and a profile's scores, loaded once per service
    // lifetime (one request or one scan), keyed by profile id.
    private readonly Dictionary<int, (List<CustomFormat> Formats, Dictionary<int, int> Scores)> _formatScoreCache = new();

    private List<QualityProfile>? _qualityProfiles;

    private async Task<QualityProfile?> QualityProfileAsync(Event evt)
    {
        _qualityProfiles ??= await _db.QualityProfiles.AsNoTracking().ToListAsync();
        return RssSyncService.ResolveQualityProfile(evt, _qualityProfiles);
    }

    /// <summary>
    /// The custom format score a library import stores on its new file, read
    /// from the name the upgrade decision scored. Left at 0, every release
    /// with a positive score would look like an upgrade over this file.
    /// </summary>
    private async Task<int> ImportedFormatScoreAsync(FileImportRequest request, Event evt) =>
        await FormatScoreAsync(request.OriginalTitle ?? Path.GetFileNameWithoutExtension(request.FilePath),
            await QualityProfileAsync(evt));

    private async Task<int> ExistingFormatScoreAsync(
        FileImportRequest request, Event evt, EventFile file, bool ownerChanged)
    {
        if (!ownerChanged)
            return file.CustomFormatScore;
        if (string.IsNullOrWhiteSpace(file.ReleaseTitle))
            return await ImportedFormatScoreAsync(request, evt);
        return await FormatScoreAsync(file.ReleaseTitle, await QualityProfileAsync(evt));
    }

    private async Task<int> FormatScoreAsync(string title, QualityProfile? profile)
    {
        if (string.IsNullOrEmpty(title) || profile == null) return 0;
        var profileId = profile.Id;
        if (!_formatScoreCache.TryGetValue(profileId, out var cached))
        {
            var scores = CustomFormatService.ProfileScores(profile);
            var formats = scores.Count == 0 ? new List<CustomFormat>() : await _db.CustomFormats.AsNoTracking().ToListAsync();
            cached = (formats, scores);
            _formatScoreCache[profileId] = cached;
        }
        if (cached.Scores.Count == 0) return 0;
        return _customFormatService.EvaluateRelease(title, cached.Formats, cached.Scores).Sum(m => m.Score);
    }

    /// <summary>
    /// Whether a file already sits in the folder of the file it would
    /// replace. Such a copy is imported in place and the file it replaces
    /// is left on disk; a copy from anywhere else, a watch folder or another
    /// season folder included, is moved into place and replaces it.
    /// </summary>
    private static bool IsBesideOccupant(string incomingPath, string? occupantPath)
    {
        if (string.IsNullOrEmpty(occupantPath)) return false;
        var occupantDir = Path.GetDirectoryName(Path.GetFullPath(occupantPath));
        if (string.IsNullOrEmpty(occupantDir)) return false;
        var incomingDir = Path.GetDirectoryName(Path.GetFullPath(incomingPath)) ?? string.Empty;
        return incomingDir.Equals(occupantDir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Renames the file already holding this part out of the way. Nothing is
    /// deleted here, so a failed transfer can put it back.
    /// </summary>
    private async Task<StagedPartReplacement?> StageOccupiedPartAsync(Event existingEvent, string? partName, int? partNumber, string incomingPath, EventFile? chosen = null)
    {
        // The file judged by the upgrade rule is the file replaced, so the
        // decision and the replacement always concern the same file.
        var occupant = chosen ?? existingEvent.Files
            .FirstOrDefault(f => (partNumber.HasValue
                    ? f.PartNumber == partNumber
                    : string.Equals(SourcePrecedence.NormalizePart(f.PartName),
                        SourcePrecedence.NormalizePart(partName), StringComparison.OrdinalIgnoreCase)) &&
                                 !string.Equals(f.FilePath, incomingPath, StringComparison.OrdinalIgnoreCase));

        if (occupant == null) return null;

        _logger.LogInformation(
            "[Library Import] Part {Part} of event {EventId} already holds a file. Replacing it: {OldPath}",
            partNumber?.ToString() ?? "(whole event)", existingEvent.Id, occupant.FilePath);

        var originalPath = occupant.FilePath ?? string.Empty;
        string? stagedPath = null;

        if (!string.IsNullOrEmpty(originalPath) && File.Exists(originalPath))
        {
            // A sidecar already sitting here is the original from a
            // replacement that was interrupted before its new file landed,
            // which can make it the only intact copy of the media. Deleting it
            // to reuse the name destroyed it for good, so a free name is found
            // instead and the older one is left for the user.
            var candidate = FindFreeStagingPath(originalPath);
            try
            {
                File.Move(originalPath, candidate);
                stagedPath = candidate;
            }
            catch (Exception ex)
            {
                // The transfer still runs. It gets a numbered name instead of
                // the one this file holds, which the next rename pass corrects.
                _logger.LogWarning(ex, "[Library Import] Could not move the replaced file aside: {Path}", originalPath);
            }
        }

        await Task.CompletedTask;
        return new StagedPartReplacement(occupant, originalPath, stagedPath);
    }

    /// <summary>
    /// A staging name beside the original that nothing is using yet.
    ///
    /// The name used to be fixed, so a second replacement wrote over the
    /// sidecar the first one left. That sidecar holds the original file, and
    /// after an interrupted replacement it can be the only copy there is.
    /// </summary>
    private static string FindFreeStagingPath(string originalPath)
    {
        var candidate = originalPath + ".sportarr-replacing";
        if (!File.Exists(candidate)) return candidate;

        for (var attempt = 2; attempt < 1000; attempt++)
        {
            var numbered = $"{originalPath}.sportarr-replacing-{attempt}";
            if (!File.Exists(numbered)) return numbered;
        }

        // Nothing free after a thousand tries, which means something is very
        // wrong. A unique name still beats writing over one of them.
        return $"{originalPath}.sportarr-replacing-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Puts a staged file back after a failed transfer.
    /// </summary>
    private void RestoreStagedPart(StagedPartReplacement? staged)
    {
        if (staged?.StagedPath == null || !File.Exists(staged.StagedPath)) return;

        try
        {
            // Something sits where the replaced file belongs, and a plain move
            // will not write over it. It is moved aside rather than deleted:
            // on a move-mode import that file is the incoming media and its
            // source is already gone, so deleting it destroyed the only copy
            // while reporting a failed import.
            if (File.Exists(staged.OriginalPath))
            {
                var setAside = FindFreeStagingPath(staged.OriginalPath);
                try
                {
                    File.Move(staged.OriginalPath, setAside);
                    _logger.LogWarning(
                        "[Library Import] The failed import left a file where the replaced one belongs. It is at {Path}",
                        setAside);
                }
                catch (Exception moveEx)
                {
                    // Nowhere to put it. The replaced file stays staged rather
                    // than either of them being destroyed.
                    _logger.LogError(moveEx,
                        "[Library Import] Could not move {Path} aside, so the replaced file stays at {StagedPath}",
                        staged.OriginalPath, staged.StagedPath);
                    return;
                }
            }

            File.Move(staged.StagedPath, staged.OriginalPath);
            _logger.LogInformation("[Library Import] Import failed, so the replaced file is back: {Path}", staged.OriginalPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Library Import] Could not restore the replaced file. It is at {StagedPath}", staged.StagedPath);
        }
    }

    /// <summary>
    /// Discards a staged file once its replacement is in the library, and drops
    /// the record that tracked it.
    /// </summary>
    private async Task CommitStagedPartAsync(StagedPartReplacement? staged, EventFile? reusedRecord = null)
    {
        if (staged == null) return;

        if (staged.StagedPath != null && File.Exists(staged.StagedPath))
        {
            try
            {
                var config = await _configService.GetConfigAsync();
                var recycleBin = config.RecycleBin;
                string? recycledVideoPath = null;
                if (!string.IsNullOrEmpty(recycleBin) && Directory.Exists(recycleBin))
                {
                    var recyclePath = Helpers.RecyclePaths.FindFree(
                        recycleBin, Path.GetFileName(staged.OriginalPath));
                    File.Move(staged.StagedPath, recyclePath);
                    recycledVideoPath = recyclePath;
                    _logger.LogInformation("[Library Import] Moved replaced file to recycle bin: {Path} -> {RecyclePath}",
                        staged.OriginalPath, recyclePath);
                }
                else
                {
                    File.Delete(staged.StagedPath);
                    _logger.LogInformation("[Library Import] Deleted replaced file: {Path}", staged.OriginalPath);
                }
                await _metadataWriterService.DeleteEventMetadataAsync(
                    new EventFile { FilePath = staged.OriginalPath }, recycledVideoPath);
                // A parked folder goes once its last file is gone.
                FileParking.RemoveParkedFolderIfEmpty(staged.OriginalPath, _logger);
            }
            catch (Exception ex)
            {
                // Leave the record removed even when the file stays. A file the
                // library no longer tracks is better than a duplicate part.
                _logger.LogWarning(ex, "[Library Import] Failed to remove replaced file: {Path}", staged.StagedPath);
            }
        }

        // A replacement landing on the same path finds the occupant's own row
        // and updates it, so that row now describes the new file. Removing it
        // here would leave the event pointing at media with nothing recording
        // it.
        if (reusedRecord != null && ReferenceEquals(reusedRecord, staged.Occupant))
        {
            return;
        }

        staged.Occupant.Event?.Files.Remove(staged.Occupant);
        _db.EventFiles.Remove(staged.Occupant);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Get media management settings
    /// </summary>
    private async Task<MediaManagementSettings> GetMediaManagementSettingsAsync()
    {
        var settings = await _db.MediaManagementSettings.FirstOrDefaultAsync();

        if (settings == null)
        {
            // Create default settings with granular folder options
            settings = new MediaManagementSettings
            {
                RenameFiles = true,
                StandardFileFormat = "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full} - {Sportarr Id}",
                // Granular folder settings - default: league/season folders enabled, event folders disabled
                CreateLeagueFolders = true,
                CreateSeasonFolders = true,
                CreateEventFolders = false,
                LeagueFolderFormat = "{Series}",
                SeasonFolderFormat = "Season {Season}",
                EventFolderFormat = "{Event Title} ({Year}-{Month}-{Day}) E{Episode}",
                CopyFiles = false,
                MinimumFreeSpace = 100
                // Note: RemoveCompletedDownloads is now a per-client setting
            };

            _db.MediaManagementSettings.Add(settings);
            await _db.SaveChangesAsync();
        }

        // Root folders live in the RootFolders table (loaded via RootFolderLoader).
        return settings;
    }

    private string DeriveEventSport(string organization, string title)
    {
        var text = $"{organization} {title}".ToLowerInvariant();

        // Motorsports / Racing - Check early to avoid "one" conflicts with Fighting
        var racingKeywords = new[] { "formula 1", "f1", "formula one", "nascar", "indycar", "motogp",
                                     "rally", "grand prix", "racing", "motorsport" };
        if (racingKeywords.Any(k => text.Contains(k)))
            return "Motorsport";

        // Combat Sports / Fighting
        var fightingKeywords = new[] { "ufc", "bellator", "one fc", "one champ", "pfl", "invicta", "cage warriors",
                                       "lfa", "dwcs", "rizin", "ksw", "glory", "combate", "mma", "boxing",
                                       "fight night", "fight", "muay thai", "kickboxing", "jiu-jitsu", "bjj" };
        if (fightingKeywords.Any(k => text.Contains(k)))
            return "Fighting";

        // American Football - Check before Soccer to catch "football" in American context
        var footballKeywords = new[] { "nfl", "ncaa football", "college football", "super bowl",
                                       "american football", "afl", "cfl", "football playoff", "football championship" };
        if (footballKeywords.Any(k => text.Contains(k)))
            return "American Football";

        // Basketball - Check before Cricket to handle "bbl game" before "bbl"
        var basketballKeywords = new[] { "nba", "wnba", "ncaa basketball", "euroleague", "basketball",
                                         "fiba", "acb", "bbl game", "bundesliga basketball" };
        if (basketballKeywords.Any(k => text.Contains(k)))
            return "Basketball";

        // Cricket - Check before Soccer to avoid "world cup" conflicts
        var cricketKeywords = new[] { "cricket", "test match", "odi", "t20", "ipl", "bbl", "big bash" };
        if (cricketKeywords.Any(k => text.Contains(k)))
            return "Cricket";

        // Rugby - Check before Soccer to avoid "world cup" conflicts
        var rugbyKeywords = new[] { "rugby", "six nations", "super rugby", "nrl", "rugby league", "rugby world cup" };
        if (rugbyKeywords.Any(k => text.Contains(k)))
            return "Rugby";

        // Soccer / Football
        var soccerKeywords = new[] { "premier league", "la liga", "serie a", "bundesliga", "ligue 1",
                                     "champions league", "europa league", "fifa", "world cup", "mls",
                                     "soccer", " fc ", "cf ", " united", " city fc", "athletic", " football " };
        if (soccerKeywords.Any(k => text.Contains(k)))
            return "Soccer";

        // Baseball
        var baseballKeywords = new[] { "mlb", "baseball", "world series", "npb", "kbo" };
        if (baseballKeywords.Any(k => text.Contains(k)))
            return "Baseball";

        // Ice Hockey
        var hockeyKeywords = new[] { "nhl", "hockey", "stanley cup", "khl", "shl", "liiga" };
        if (hockeyKeywords.Any(k => text.Contains(k)))
            return "Ice Hockey";

        // Tennis
        var tennisKeywords = new[] { "tennis", "wimbledon", "us open", "french open", "australian open",
                                     "atp", "wta", "grand slam" };
        if (tennisKeywords.Any(k => text.Contains(k)))
            return "Tennis";

        // Golf
        var golfKeywords = new[] { "golf", "pga", "masters", "open championship", "ryder cup" };
        if (golfKeywords.Any(k => text.Contains(k)))
            return "Golf";

        // Default to Fighting for backward compatibility with legacy import lists
        return "Fighting";
    }

    /// <summary>
    /// The one tied event that a signal names, or null. The S/E bonus is
    /// tried first, then title words that the tied events do not all share.
    /// </summary>
    private static Event? BreakTie(List<(Event Event, bool EpisodeNumberBonus)> tied, string searchTitle, string? parsedLocation)
    {
        var withBonus = tied.Where(t => t.EpisodeNumberBonus).Select(t => t.Event).ToList();
        if (withBonus.Count == 1)
            return withBonus[0];

        var pool = withBonus.Count > 1 ? withBonus : tied.Select(t => t.Event).ToList();
        var commonWords = pool
            .Select(e => (IEnumerable<string>)TitleWords(e.Title).ToHashSet(StringComparer.OrdinalIgnoreCase))
            .Aggregate((left, right) => left.Intersect(right, StringComparer.OrdinalIgnoreCase).ToList())
            .ToList();
        var named = pool
            .Where(e => HasTitleEvidence(searchTitle, e.Title, parsedLocation, e.League?.Name, commonWords))
            .ToList();
        return named.Count == 1 ? named[0] : null;
    }

    // The resolution and source words of the file parser. "CAM" and "TS"
    // stay out, because "Onboard Cam" and similar titles use them as words.
    private static readonly System.Text.RegularExpressions.Regex QualityWord = new(
        @"(?<![A-Za-z0-9])(?:" + MediaFileParser.QualityTokens + "|" +
        string.Join("|", MediaFileParser.SourceTokens.Split('|').Where(token => token is not ("CAM" or "TS"))) +
        @")(?![A-Za-z0-9])",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// The parsed title without quality and source words such as "WEBDL"
    /// and "1080p". They name the copy, never the event.
    /// </summary>
    internal static string? WithoutQualityWords(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return title;
        try
        {
            if (!QualityWord.IsMatch(title))
                return title;
            var cleaned = QualityWord.Replace(title, " ");
            // Close the gaps and the separators that the removed words leave.
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(?:\s*[-.]\s*){2,}|\s{2,}", " ");
            return cleaned.Trim(' ', '-', '.', '_');
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return title;
        }
    }

    /// <summary>True when the event's broadcast day or UTC day is the date.</summary>
    private static bool FallsOn(Event evt, DateTime date) =>
        (evt.BroadcastDate ?? evt.EventDate).Date == date.Date || evt.EventDate.Date == date.Date;

    /// <summary>The lowest confidence a library scan accepts as a match.</summary>
    private const int MatchAcceptanceFloor = 40;

    /// <summary>
    /// True when the parsed title is the event title, holds it, or shares a
    /// word with it that tells it from the other candidates. Connector words
    /// such as "vs" do not count. The words in commonWords, the words of the
    /// league name and "Grand", "Prix" and "GP" do not count either, because
    /// every event of the league can share them.
    /// </summary>
    internal static bool HasTitleEvidence(string searchTitle, string eventTitle, string? parsedLocation = null,
        string? leagueName = null, IEnumerable<string>? commonWords = null)
    {
        var normalizedSearch = NormalizeTitle(searchTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);
        if (string.IsNullOrWhiteSpace(normalizedSearch) || string.IsNullOrWhiteSpace(normalizedEvent) ||
            HasFightNightNumberMismatch(normalizedSearch, normalizedEvent))
            return false;
        if (StrongTitleAward(normalizedSearch, normalizedEvent) > 0)
            return true;

        var ignored = new HashSet<string>(MatchConnectorWords, StringComparer.OrdinalIgnoreCase) { "grand", "prix", "gp" };
        if (!string.IsNullOrWhiteSpace(leagueName))
            ignored.UnionWith(TitleWords(leagueName));
        if (commonWords != null)
            ignored.UnionWith(commonWords);

        var searchWords = TitleWords(searchTitle);
        if (!string.IsNullOrEmpty(parsedLocation))
            searchWords = searchWords.Union(parsedLocation.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        return searchWords.Where(w => !ignored.Contains(w))
            .Intersect(TitleWords(eventTitle), StringComparer.OrdinalIgnoreCase).Any();
    }

    private static readonly System.Text.RegularExpressions.Regex FightNightNumber = new(
        @"\bfight\s+night\s+0*(?<number>\d+)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// True when the file names a numbered Fight Night and the event title
    /// names another number or no number. Both inputs must be normalized.
    /// </summary>
    private static bool HasFightNightNumberMismatch(string normalizedSearch, string normalizedEvent)
    {
        var file = FightNightNumber.Match(normalizedSearch);
        if (!file.Success)
            return false;
        var evt = FightNightNumber.Match(normalizedEvent);
        return !evt.Success || evt.Groups["number"].Value != file.Groups["number"].Value;
    }

    private static IEnumerable<string> TitleWords(string title) =>
        NormalizeTitle(title).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Calculate match confidence between a parsed filename and a database event
    /// </summary>
    internal static int CalculateMatchConfidence(
        string searchTitle,
        string eventTitle,
        string? organization,
        Event evt,
        DateTime? parsedDate,
        int? parsedYear = null,
        int? parsedRoundNumber = null,
        int? seasonYearEnd = null,
        int? explicitEpisodeNumber = null,
        string? parsedLocation = null,
        ILogger? logger = null,
        string? parsedSport = null,
        string? seriesLabel = null,
        IReadOnlyList<int>? roundRaceNumbers = null,
        string? sourceTitle = null,
        IReadOnlyCollection<Event>? datePeers = null,
        bool dateSharedByAnotherCandidate = false) =>
        CalculateMatchConfidence(searchTitle, eventTitle, organization, evt, parsedDate, parsedYear,
            parsedRoundNumber, seasonYearEnd, explicitEpisodeNumber, parsedLocation, logger, parsedSport,
            seriesLabel, roundRaceNumbers, sourceTitle, datePeers, dateSharedByAnotherCandidate, out _);

    /// <summary>
    /// Same as the other form. It also tells if the S/E number added its
    /// bonus, so the scan knows if the number chose this event.
    /// The scan sets dateSharedByAnotherCandidate when another candidate
    /// falls on the parsed date. Such a date names no single event.
    /// </summary>
    internal static int CalculateMatchConfidence(
        string searchTitle,
        string eventTitle,
        string? organization,
        Event evt,
        DateTime? parsedDate,
        int? parsedYear,
        int? parsedRoundNumber,
        int? seasonYearEnd,
        int? explicitEpisodeNumber,
        string? parsedLocation,
        ILogger? logger,
        string? parsedSport,
        string? seriesLabel,
        IReadOnlyList<int>? roundRaceNumbers,
        string? sourceTitle,
        IReadOnlyCollection<Event>? datePeers,
        bool dateSharedByAnotherCandidate,
        out bool episodeNumberBonus)
    {
        episodeNumberBonus = false;
        int confidence = 0;

        // ── SERIES LABEL GATE ───────────────────────────────────────────────────
        // A library-format filename names its series ahead of the SxxxxExx token
        // ("V8 Supercars - S2026E19 - …"). When that label names a different
        // series than the candidate's league, an agreeing episode number means
        // nothing — every motorsport league has an episode 19. Without this a
        // V8 Supercars file was suggested against an F1 event at 75% purely on
        // S/E + year agreement.
        if (!string.IsNullOrWhiteSpace(seriesLabel) && evt.League != null &&
            !ReleaseMatchingService.SeriesLabelMatchesLeague(seriesLabel, evt.League))
        {
            logger?.LogDebug("[Match] Series label gate: file names series '{Label}', event '{Event}' is in league '{League}' - rejecting",
                seriesLabel, eventTitle, evt.League.Name);
            return 0;
        }

        var originalTitle = sourceTitle ?? searchTitle;
        if (IsMlsDatedName(originalTitle) && !MlsNameFitsEvent(originalTitle, searchTitle, evt))
            return 0;

        var hasLeagueReleaseIdentity = LeagueReleaseNamePolicy.HasStrongEventIdentity(originalTitle, evt);
        if (CricketRugbyReleaseNamePolicy.HasIdentityConflict(originalTitle, evt) ||
            LeagueReleaseNamePolicy.HasIdentityConflict(originalTitle, evt))
        {
            return 0;
        }

        if (SearchNormalizationService.HasCyclingCategoryConflict(
                originalTitle, eventTitle, evt.League?.Name, evt.Sport))
        {
            return 0;
        }
        if (SearchNormalizationService.HasParticipantCategoryConflict(originalTitle, evt))
        {
            return 0;
        }

        var supercarsRoundRaceIdentity = LeagueReleaseNamePolicy.EvaluateSupercarsRoundRaceIdentity(
            originalTitle, evt, roundRaceNumbers);
        if (supercarsRoundRaceIdentity == false ||
            LeagueReleaseNamePolicy.HasUnresolvedSupercarsRaceIdentity(originalTitle, evt) &&
            supercarsRoundRaceIdentity != true)
        {
            return 0;
        }

        // ── LEAGUE IDENTITY GATE ────────────────────────────────────────────────
        // The series-label gate above only fires for library-format filenames,
        // which name their series ahead of the SxxxxExx token. A raw scene
        // filename — "BSB.2026.Round06.Oulton.Park.International.Race.One…" —
        // has no label, so that gate is skipped and nothing else in this method
        // establishes WHICH series the file belongs to: round, session and year
        // all agree across every series running the same weekend format.
        //
        // ValidateRelease rejects this on the grab side. The import side did not,
        // so a cross-series file that reached disk by any route (manual copy, a
        // grab made before the grab-side gate existed, a hardlinked leftover)
        // could still be matched onto the wrong event and rename itself into the
        // library. Reuse the same helper so the two sides cannot drift.
        //
        // Team sports are exempt: their filenames name the teams rather than the
        // league ("Lakers.vs.Celtics.2026.03.05" never says "NBA"), and the team
        // names are themselves the identity signal, checked separately below.
        //
        // A caller that already resolved the organization is also exempt: callers
        // pass searchTitle as either a raw filename OR an already-parsed short
        // title ("Spain Qualifying"), and the latter legitimately carries no
        // league name. When organization was resolved and agrees with the league,
        // identity is established and the title does not need to repeat it.
        bool organizationEstablishesIdentity =
            !string.IsNullOrWhiteSpace(organization) && evt.League != null &&
            ReleaseMatchingService.SeriesLabelMatchesLeague(organization, evt.League);

        if (string.IsNullOrWhiteSpace(seriesLabel) && evt.League != null &&
            !organizationEstablishesIdentity &&
            string.IsNullOrWhiteSpace(evt.HomeTeamName) && string.IsNullOrWhiteSpace(evt.AwayTeamName) &&
            !hasLeagueReleaseIdentity &&
            !ReleaseMatchingService.TitleHasLeagueIdentity(searchTitle, eventTitle, evt.League))
        {
            logger?.LogDebug("[Match] League identity gate: '{Title}' names neither league '{League}' nor anything distinctive from event '{Event}' - rejecting",
                searchTitle, evt.League.Name, eventTitle);
            return 0;
        }

        // ── SPORT GATE ──────────────────────────────────────────────────────────
        // When the filename parser identified the sport, an event from a
        // different sport can never be the right match, no matter how the
        // fuzzy title arithmetic works out. Without this a soccer World Cup
        // file auto-matched a World Snooker event.
        if (!string.IsNullOrEmpty(parsedSport) && !string.IsNullOrEmpty(evt.Sport) &&
            !LeagueSportRules.AreEquivalentSports(evt.Sport, parsedSport))
        {
            logger?.LogDebug("[Match] Sport gate: file parsed as {ParsedSport}, event '{Event}' is {EventSport} - rejecting",
                parsedSport, eventTitle, evt.Sport);
            return 0;
        }

        // ── SESSION GATE ────────────────────────────────────────────────────────
        // A file that names one session of a weekend is never another session
        // of it. A second copy of a filled session otherwise moves onto a
        // sibling session, because the filled event is not a candidate.
        var sessions = SessionsOf(originalTitle, eventTitle, evt);
        if (NamesAnotherSession(sessions))
        {
            logger?.LogDebug("[Match] Session gate: file names '{FileSession}', event '{Event}' is '{EventSession}' - rejecting",
                sessions.File, eventTitle, sessions.Event);
            return 0;
        }

        // ── ROUND NUMBER ────────────────────────────────────────────────────────
        // A filename "Round09" is the CHAMPIONSHIP ROUND, which must be compared
        // against the event's own Round field, never its EpisodeNumber. For
        // multi-session sports (F1: practice/qualifying/sprint/race) the episode
        // number counts every session, so round 9 (Spain) and episode 9 (an early
        // session of a different weekend) are unrelated. Comparing round to
        // episode both rewarded the wrong event (+50 when its episode happened to
        // equal the round) and penalised the correct one (whose episode number is
        // far higher), which is exactly how "Round09 Spain Qualifying" matched
        // "Chinese GP Free Practice 1".
        var eventRoundNumber = int.TryParse(evt.Round, out var er) ? er : (int?)null;
        var roundAgrees = false;
        if (parsedRoundNumber.HasValue && eventRoundNumber.HasValue)
        {
            var hasExactPreseasonDate = eventRoundNumber.Value == 500 &&
                SearchNormalizationService.HasExactDatedPreseasonIdentity(originalTitle, parsedDate, evt);
            // Authoritative when the event carries real round data.
            if (eventRoundNumber.Value == parsedRoundNumber.Value || hasExactPreseasonDate)
            {
                roundAgrees = true;
                confidence += 50;
                logger?.LogDebug("[Match] Round {Round} matches event Round for '{Event}'", parsedRoundNumber.Value, eventTitle);
            }
            else
            {
                confidence -= 50;
                logger?.LogDebug("[Match] Round mismatch: file round {FileRound} vs event round {EventRound} for '{Event}' (penalising)",
                    parsedRoundNumber.Value, eventRoundNumber.Value, eventTitle);
            }
        }
        else if (parsedRoundNumber.HasValue && evt.EpisodeNumber.HasValue)
        {
            // Fallback ONLY for events with no round data. Some single-session
            // series legitimately number round == episode, so keep the old
            // heuristic here, but weight it lightly so it can never overpower a
            // location or title signal the way the round-vs-episode bug did.
            if (evt.EpisodeNumber.Value == parsedRoundNumber.Value && evt.EpisodeNumber.Value <= 100)
            {
                confidence += 15;
                logger?.LogDebug("[Match] Round {Round} matches EpisodeNumber (no round data) for '{Event}'", parsedRoundNumber.Value, eventTitle);
            }
        }

        // ── YEAR / SEASON CHECK ─────────────────────────────────────────────────
        // CRITICAL: sports events repeat every year. Year mismatch = wrong season.
        if (parsedYear.HasValue)
        {
            if (!YearFits(parsedYear.Value, seasonYearEnd, originalTitle, evt))
            {
                var eventYear = (evt.BroadcastDate ?? evt.EventDate).Year;
                logger?.LogDebug("[Match] Year mismatch: file has {ParsedYear}, event '{Event}' is from {EventYear} (Season: {Season})",
                    parsedYear.Value, eventTitle, eventYear, evt.Season);
                return 0;
            }
            else
            {
                confidence += 25;
            }
        }

        // ── DATE GATE ───────────────────────────────────────────────────────────
        // A dated filename is anchored: sports events are date-identified,
        // and a file dated weeks away from an event can never be that event
        // no matter how the title arithmetic lands ("Spain vs Argentina
        // 19.07.2026" reached the 40-point floor against "Spain vs Saudi
        // Arabia" from June 21 on one shared team plus the year). Seven days
        // tolerates broadcast-vs-UTC dating and multi-day events.
        ImportDateMatchResult? dateMatch = null;
        if (parsedDate.HasValue)
        {
            dateMatch = ImportDateMatchPolicy.Evaluate(evt, parsedDate.Value, originalTitle, datePeers);
            if (dateMatch.Value.DaysDifference > 0 && ReleaseMatchingService.DateMatchesAnotherTeamEvent(
                    evt, parsedDate.Value.Date, datePeers))
            {
                return 0;
            }

            var allowsObservedDateDrift = LeagueReleaseNamePolicy.AllowsObservedDateDrift(
                originalTitle, evt, parsedDate.Value);
            if ((dateMatch.Value.Reject || dateMatch.Value.DaysDifference > 7) &&
                !allowsObservedDateDrift)
            {
                logger?.LogDebug("[Match] Date gate: file dated {FileDate:yyyy-MM-dd}, event '{Event}' is {EventDate:yyyy-MM-dd} ({Diff:F0} days apart) - rejecting",
                    parsedDate.Value, eventTitle, dateMatch.Value.EventDate, dateMatch.Value.DaysDifference);
                return 0;
            }
        }

        // ── EVENT AGREEMENT ─────────────────────────────────────────────────────
        // Find the title tier and the location agreement one time.
        // The S/E block and the title block both use them.
        var normalizedSearch = NormalizeTitle(searchTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);
        var titleIsDegenerate = string.IsNullOrWhiteSpace(normalizedSearch) || string.IsNullOrWhiteSpace(normalizedEvent);
        // "Fight Night 86" is one card. An event with another number or no
        // number gets no credit for the words the two titles share.
        var fightNightMismatch = !titleIsDegenerate && HasFightNightNumberMismatch(normalizedSearch, normalizedEvent);
        var strongTitleAward = titleIsDegenerate || fightNightMismatch ? 0 : StrongTitleAward(normalizedSearch, normalizedEvent);
        var eventWords = normalizedEvent.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !MatchConnectorWords.Contains(w)).ToArray();
        var locationWords = string.IsNullOrEmpty(parsedLocation)
            ? Array.Empty<string>()
            : parsedLocation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var locationAgrees = locationWords.Intersect(eventWords, StringComparer.OrdinalIgnoreCase).Any();

        // ── EXPLICIT S/E EPISODE NUMBER ─────────────────────────────────────────
        // A file with the id token does not reach this scorer. Any other
        // file can carry a number that is not this event's: another database
        // named it, or Sportarr named it without the token. So the number
        // only supports other agreement. It does not decide alone.
        // Corroboration is a strong title, the exact date of this event, an
        // equal round, or a location in the event title. A near date does
        // not corroborate. A week holds many events, so an equal number near
        // it is chance.
        // A round and a location name a weekend, not a session. When the event
        // names a session, they corroborate only if the file names that session.
        // Same number with corroboration: add 50.
        // Same number without corroboration: add nothing. The match is chance.
        // Different number with corroboration: add nothing. The other signals decide.
        // Different number without corroboration: subtract 30.
        // When two or more candidates fall on the file's date, the number
        // adds and subtracts nothing for any candidate. Such a day holds
        // many numbers, so an equal one is chance, and a different one does
        // not tell the events apart.
        var dateSharedByCandidates = parsedDate.HasValue && dateSharedByAnotherCandidate;
        if (explicitEpisodeNumber.HasValue && evt.EpisodeNumber.HasValue && !dateSharedByCandidates)
        {
            var numberAgrees = evt.EpisodeNumber.Value == explicitEpisodeNumber.Value;
            var exactDate = dateMatch is { Reject: false, DaysDifference: 0 };
            var corroborated = strongTitleAward > 0 || exactDate ||
                ((roundAgrees || locationAgrees) && FileNamesEventSession(sessions));
            if (numberAgrees && corroborated)
            {
                confidence += 50;
                episodeNumberBonus = true;
            }
            else if (!numberAgrees && !corroborated)
                confidence -= 30;
        }

        // ── TITLE SIMILARITY ────────────────────────────────────────────────────
        // A degenerate parsed title carries no signal. Without this guard the
        // contains branch below hands EVERY event a 40-point award when the
        // search string is empty (string.Contains("") is true), which put an
        // arbitrary event exactly on the acceptance floor.
        if (titleIsDegenerate)
        {
            return hasLeagueReleaseIdentity ? Math.Min(100, confidence + 50) : 0;
        }

        var hasTitleEvidence = true;
        if (strongTitleAward > 0)
        {
            confidence += strongTitleAward;
        }
        else if (fightNightMismatch)
        {
            hasTitleEvidence = false;
        }
        else
        {
            // Partial word match — also check location against event title.
            // Connector words are excluded from the overlap: every matchup
            // title contains "vs", so counting it handed any two-team title
            // free similarity against every other two-team event.
            var searchWords = normalizedSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !MatchConnectorWords.Contains(w)).ToArray();

            // If we have a parsed location, include it in the word set for matching
            if (locationWords.Length > 0)
            {
                searchWords = searchWords.Union(locationWords, StringComparer.OrdinalIgnoreCase).ToArray();
            }

            var matchingWords = searchWords.Intersect(eventWords, StringComparer.OrdinalIgnoreCase).Count();
            var totalWords = Math.Max(searchWords.Length, eventWords.Length);

            if (matchingWords > 0 && totalWords > 0)
            {
                var matchPercent = (double)matchingWords / totalWords;
                confidence += (int)(30 * matchPercent);
            }
            else
            {
                hasTitleEvidence = false;
            }
        }

        if (CricketRugbyReleaseNamePolicy.HasStrongEventIdentity(originalTitle, evt))
        {
            confidence += 40;
        }
        if (CricketRugbyReleaseNamePolicy.HasStrongWorldCupIdentity(originalTitle, evt, parsedDate))
        {
            confidence += 50;
        }
        if (hasLeagueReleaseIdentity)
        {
            confidence += 50;
        }

        // ── ORGANIZATION → LEAGUE ───────────────────────────────────────────────
        // Hard cross-sport gate: if we identified an organization (IndyCar, NBA, NFL…),
        // reject any event from a different league immediately. This prevents IndyCar
        // files from matching NBA events, even when the title fuzzy score is high.
        if (!string.IsNullOrEmpty(organization) && evt.League != null)
        {
            var leagueMatch = evt.League.Name.Contains(organization, StringComparison.OrdinalIgnoreCase)
                           || organization.Contains(evt.League.Name, StringComparison.OrdinalIgnoreCase);
            if (!leagueMatch && !hasLeagueReleaseIdentity)
                return 0; // Wrong sport — eliminate before any title comparison
            if (leagueMatch) confidence += 15;
        }

        // ── DATE PROXIMITY ──────────────────────────────────────────────────────
        if (dateMatch.HasValue)
        {
            confidence += dateMatch.Value.Score;
        }

        // ── NO IDENTITY ─────────────────────────────────────────────────────────
        // Year and date are true for every event of the day. A name that shares
        // no word with the event, names no league or team pair and carries no
        // round or corroborated episode that fits has not identified this
        // event. Keep it under the acceptance floor. An equal episode number
        // without corroboration is chance, so it is not identity.
        var hasStructuralMatch = episodeNumberBonus ||
            parsedRoundNumber.HasValue && eventRoundNumber.HasValue && eventRoundNumber == parsedRoundNumber;
        if (!hasTitleEvidence && !hasStructuralMatch && !hasLeagueReleaseIdentity &&
            !organizationEstablishesIdentity &&
            !(evt.League != null && ReleaseMatchingService.TitleNamesLeague(originalTitle, evt.League)) &&
            !LeagueReleaseNamePolicy.NamesBothEventTeams(originalTitle, evt) &&
            !CricketRugbyReleaseNamePolicy.HasStrongEventIdentity(originalTitle, evt) &&
            !CricketRugbyReleaseNamePolicy.HasStrongWorldCupIdentity(originalTitle, evt, parsedDate))
        {
            logger?.LogDebug("[Match] No identity: '{Title}' shares nothing with event '{Event}' and names no league - keeping it below the floor",
                searchTitle, eventTitle);
            return Math.Min(confidence, MatchAcceptanceFloor - 1);
        }

        // ── RECENCY ─────────────────────────────────────────────────────────────
        if (Math.Abs((DateTime.UtcNow - evt.EventDate).TotalDays) <= 30)
        {
            confidence += 5;
        }

        // Keep the full score until candidate selection. An early cap can hide the exact date's lead.
        return confidence;
    }

    private static readonly System.Text.RegularExpressions.Regex MlsFixtureSides = new(
        @"^(?<first>.+?)\s+(?:vs?\.?|@)\s+(?<second>.+)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly System.Text.RegularExpressions.Regex MlsDatedName = new(
        @"^(?:MLS|(?:American[._\s-]+)?Major[._\s-]+League[._\s-]+Soccer)[._\s-]+\d{4}[-._\s]+\d{2}[-._\s]+\d{2}(?:[._\s-]|$)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// True for a file name that starts with the MLS league and a full date.
    /// Library scan and manual import both use this test.
    /// </summary>
    internal static bool IsMlsDatedName(string title) =>
        !string.IsNullOrWhiteSpace(title) && MlsDatedName.IsMatch(title);

    /// <summary>
    /// Decides if an MLS file name can belong to the event. The league must
    /// be MLS, so MLS Next Pro games are refused. When the name gives two
    /// teams, they must be the event's teams.
    /// </summary>
    internal static bool MlsNameFitsEvent(string originalTitle, string fixtureTitle, Event evt)
    {
        if (evt.League == null ||
            !ReleaseMatchingService.SeriesLabelMatchesLeague("American Major League Soccer", evt.League))
            return false;

        // Without team names on the event, only the league can be checked.
        if (string.IsNullOrWhiteSpace(evt.HomeTeamName) || string.IsNullOrWhiteSpace(evt.AwayTeamName))
            return true;

        var fixture = WithoutReleaseGroup(fixtureTitle, originalTitle);
        if (!NamesMlsFixture(fixture))
            return true;

        return MlsFixtureMatchesTeams(fixture, evt);
    }

    private static string WithoutReleaseGroup(string fixtureTitle, string originalTitle)
    {
        var releaseGroup = ReleaseGroupParser.Parse(originalTitle);
        if (string.IsNullOrWhiteSpace(releaseGroup))
            return fixtureTitle;

        var bracketedSuffix = $"[{releaseGroup}]";
        if (fixtureTitle.EndsWith(bracketedSuffix, StringComparison.OrdinalIgnoreCase))
            return fixtureTitle[..^bracketedSuffix.Length].TrimEnd();
        foreach (var separator in new[] { " ", "-", "." })
        {
            var suffix = separator + releaseGroup;
            if (fixtureTitle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return fixtureTitle[..^suffix.Length];
        }
        return fixtureTitle;
    }

    private static readonly HashSet<string> MlsClubSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "fc", "cf", "sc", "afc"
    };

    /// <summary>
    /// True when the title names two sides. A league and date name without
    /// teams is matched by date elsewhere.
    /// </summary>
    internal static bool NamesMlsFixture(string title) => MlsFixtureSides.IsMatch(title);

    internal static bool MlsFixtureMatchesTeams(string fixture, Event evt)
    {
        if (string.IsNullOrWhiteSpace(evt.HomeTeamName) || string.IsNullOrWhiteSpace(evt.AwayTeamName))
            return false;

        var sides = MlsFixtureSides.Match(fixture);
        if (!sides.Success)
            return false;

        var first = sides.Groups["first"].Value;
        var second = sides.Groups["second"].Value;
        return MlsSideNamesTeam(first, evt.HomeTeamName, evt.HomeTeam) && MlsSideNamesTeam(second, evt.AwayTeamName, evt.AwayTeam) ||
               MlsSideNamesTeam(first, evt.AwayTeamName, evt.AwayTeam) && MlsSideNamesTeam(second, evt.HomeTeamName, evt.HomeTeam);
    }

    /// <summary>
    /// True when one side of the fixture names this team. Each side is
    /// checked on its own, so a name on the other side cannot count.
    /// Short sides such as "New York" fit more than one club. That is safe
    /// only because both sides must fit, so keep that check.
    /// </summary>
    private static bool MlsSideNamesTeam(string side, string teamName, Team? team)
    {
        var sideRaw = MlsTeamWords(side).ToArray();
        var sideWords = MlsClubWords(side);
        if (sideWords.Length == 0)
            return false;

        // A club suffix ("Los Angeles FC", "NYCFC") marks the side as a full
        // name. It cannot then be the first words of another club's name.
        var sideIsFullName = sideRaw.Any(word => MlsClubSuffixes.Contains(word) || MlsInitialsWithSuffix(word));
        var nameRaw = MlsTeamWords(teamName).ToArray();
        var nameWords = MlsClubWords(teamName);
        if (MlsMatchWords(nameWords, sideWords, out var nameUsed, out var sideUsed))
        {
            var coversName = nameUsed == nameWords.Length;
            if (!coversName)
                return !sideIsFullName && sideUsed == sideWords.Length;

            // "LA Galaxy" must not read as Los Angeles FC plus a stray word.
            // After a name whose suffix is missing, only tags such as "ESPN"
            // may follow.
            // In an all-caps name every word looks like a tag, so none counts.
            var nameHadSuffix = nameRaw.Any(MlsClubSuffixes.Contains);
            var sideHadSuffix = sideIsFullName;
            var sideHasLowercase = side.Any(char.IsLower);
            return !(nameHadSuffix && !sideHadSuffix) ||
                sideUsed == sideWords.Length ||
                sideHasLowercase && sideWords.Skip(sideUsed).All(word => word.All(char.IsUpper));
        }

        // The team's own short and alternate names. An alias must be the
        // whole side, or a Red Bulls alias "New York" would match
        // "New York City FC".
        var aliases = new[] { team?.ShortName, team?.AlternateName, team?.UserAliases }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => System.Text.RegularExpressions.Regex.Split(value!, @"[,|;/]"));
        return aliases.Any(alias =>
        {
            var aliasWords = MlsClubWords(alias);
            return MlsMatchWords(aliasWords, sideWords, out var aliasUsed, out var used) &&
                aliasUsed == aliasWords.Length && used == sideWords.Length;
        });
    }

    private static string[] MlsClubWords(string name) =>
        MlsTeamWords(name)
            .Where(word => !MlsClubSuffixes.Contains(word))
            .Select(word => MlsInitialsWithSuffix(word) ? word[..^2] : word)
            .ToArray();

    // "NYCFC" and "LAFC" are initials joined to a club suffix.
    private static bool MlsInitialsWithSuffix(string word) =>
        word.Length >= 4 && word.All(char.IsUpper) &&
        MlsClubSuffixes.Contains(word[^2..]);

    /// <summary>
    /// Walks the name and the side from the start. A side word fits one
    /// name word, or it is the initials of several, as "KC" for Kansas City.
    /// Succeeds when the whole name or the whole side was used.
    /// </summary>
    private static bool MlsMatchWords(string[] nameWords, string[] sideWords, out int nameUsed, out int sideUsed)
    {
        nameUsed = 0;
        sideUsed = 0;
        while (nameUsed < nameWords.Length && sideUsed < sideWords.Length)
        {
            var word = sideWords[sideUsed];
            if (word.Length >= 2 && word.All(char.IsUpper) && word.Length <= nameWords.Length - nameUsed &&
                nameWords.Skip(nameUsed).Take(word.Length)
                    .Select(name => char.ToUpperInvariant(name[0]))
                    .SequenceEqual(word))
            {
                nameUsed += word.Length;
                sideUsed++;
                continue;
            }

            var name = nameWords[nameUsed];
            if (!name.Equals(word, StringComparison.OrdinalIgnoreCase) &&
                !MlsAbbreviation(name, word) &&
                !MlsOneLetterTypo(name, word))
                break;
            nameUsed++;
            sideUsed++;
        }
        return sideUsed > 0 && (nameUsed == nameWords.Length || sideUsed == sideWords.Length);
    }

    private static IEnumerable<string> MlsTeamWords(string name)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            SearchNormalizationService.RemoveDiacritics(name),
            @"\b(?:[A-Za-z]\.){2,}",
            match => match.Value.Replace(".", string.Empty));
        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized, @"\bD\s+C\b", "DC",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // The parser turns dots into spaces, so "L.A." arrives as "L A".
        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized, @"\b[A-Za-z]\b(?:\s+[A-Za-z]\b)+",
            match => System.Text.RegularExpressions.Regex.Replace(match.Value, @"\s+", string.Empty));
        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized, @"\b(?:LA|NY)\b",
            match => match.Value.Equals("LA", StringComparison.OrdinalIgnoreCase)
                ? "Los Angeles"
                : "New York",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return System.Text.RegularExpressions.Regex.Matches(normalized, @"\p{L}+")
            .Select(match => match.Value)
            .Where(word => word.Length >= 2);
    }

    // Release names shorten words, as in "Rev." for Revolution or "Utd" for
    // United. The short form keeps the first letter and the letter order.
    private static bool MlsAbbreviation(string expected, string actual)
    {
        if (actual.Length < 3 || actual.Length >= expected.Length ||
            char.ToUpperInvariant(actual[0]) != char.ToUpperInvariant(expected[0]))
            return false;
        // An all-caps code such as "MIA" must be a plain prefix, or it would
        // fit Minnesota through the letters in order.
        if (actual.All(char.IsUpper))
            return expected.StartsWith(actual, StringComparison.OrdinalIgnoreCase);
        var next = 0;
        foreach (var letter in expected)
        {
            if (next < actual.Length && char.ToUpperInvariant(letter) == char.ToUpperInvariant(actual[next]))
                next++;
        }
        return next == actual.Length;
    }

    private static bool MlsOneLetterTypo(string expected, string actual)
    {
        if (expected.Length < 6 || expected.Length != actual.Length)
            return false;

        var differences = 0;
        for (var index = 0; index < expected.Length; index++)
        {
            if (char.ToUpperInvariant(expected[index]) == char.ToUpperInvariant(actual[index]))
                continue;
            if (++differences > 1)
                return false;
        }

        return differences == 1;
    }

    /// <summary>
    /// Words that connect matchup titles rather than identify them. Present
    /// in virtually every two-team title, so they carry zero matching signal.
    /// </summary>
    private static readonly HashSet<string> MatchConnectorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "vs", "v", "versus", "at", "the", "and", "of", "@",
    };

    /// <summary>
    /// True when the phrase is in the text and starts and ends on a word edge.
    /// </summary>
    /// <remarks>
    /// A plain substring test let "UFC Fight Night 4" claim "UFC Fight Night 42"
    /// and "UFC Fight Night 86" claim "UFC Fight Night 8".
    /// </remarks>
    internal static bool ContainsTitlePhrase(string text, string phrase)
    {
        if (string.IsNullOrEmpty(phrase))
            return false;

        for (var index = text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
             index >= 0;
             index = text.IndexOf(phrase, index + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = index + phrase.Length;
            var startsOnEdge = index == 0 || !char.IsLetterOrDigit(text[index - 1]) || !char.IsLetterOrDigit(phrase[0]);
            var endsOnEdge = end == text.Length || !char.IsLetterOrDigit(text[end]) || !char.IsLetterOrDigit(phrase[^1]);
            if (startsOnEdge && endsOnEdge)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The motorsport session that the file names and the one the event
    /// names. Each is null when it names none. An event of another sport
    /// has no sessions, so both are null for it.
    /// </summary>
    private static (string? File, string? Event) SessionsOf(string fileTitle, string eventTitle, Event evt)
    {
        // The detector reads "Practice 2" in any title, whatever the sport.
        var sport = string.IsNullOrEmpty(evt.Sport) ? evt.League?.Sport : evt.Sport;
        if (!EventPartDetector.IsMotorsport(sport ?? string.Empty))
            return (null, null);

        var leagueName = evt.League?.Name;
        // The identity form reads a Supercars "Race2" as "Race", the way the
        // league titles its races, so the two sides compare.
        var eventSession = EventPartDetector.DetectMotorsportSessionIdentity(eventTitle, leagueName, releaseTitle: false);
        // A league with one event for each Grand Prix names no session for
        // it. That event is the race, so a session file is not it.
        if (string.IsNullOrEmpty(eventSession) && EventPartDetector.ReadsUnnamedEventAsRace(leagueName))
            eventSession = "Race";
        if (string.IsNullOrEmpty(eventSession))
            return (null, null);

        // A show about the weekend is never one of its sessions.
        if (EventPartDetector.NamesNonSessionShow(fileTitle))
            return (NonSessionShow, eventSession);
        var fileSession = EventPartDetector.DetectMotorsportSessionIdentity(fileTitle, leagueName, releaseTitle: true);
        if (string.IsNullOrEmpty(fileSession) && EventPartDetector.NamesTestingSession(fileTitle))
            fileSession = TestingSession;
        return (fileSession, eventSession);
    }

    // Labels for the file side only. No event session has these names, so
    // the session gate rejects such a file against every event that names
    // a session.
    private const string NonSessionShow = "Show";
    private const string TestingSession = "Testing";

    /// <summary>
    /// True when the event names no session, or the file names the same one.
    /// </summary>
    private static bool FileNamesEventSession((string? File, string? Event) sessions) =>
        string.IsNullOrEmpty(sessions.Event) ||
        string.Equals(sessions.Event, sessions.File, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the file names a session and the event is another one.
    /// The session gate of the scorer rejects such an event.
    /// </summary>
    private static bool NamesAnotherSession((string? File, string? Event) sessions) =>
        !string.IsNullOrEmpty(sessions.File) && !FileNamesEventSession(sessions);

    /// <summary>
    /// The year rule of the scorer. Sports events repeat every year, so a
    /// file year that fits none of these is the wrong season:
    /// the event's calendar year, the start year of its season, a year
    /// inside the file's season span, or a split cricket or rugby season.
    /// </summary>
    private static bool YearFits(int parsedYear, int? seasonYearEnd, string originalTitle, Event evt)
    {
        // Release groups tag a late New Year's Eve airing with the
        // broadcast year, so the broadcast date counts first.
        var eventYear = (evt.BroadcastDate ?? evt.EventDate).Year;
        var eventSeasonYear = evt.SeasonNumber ?? (int.TryParse(evt.Season, out var sy) ? sy : (int?)null);
        return eventYear == parsedYear
            || eventSeasonYear == parsedYear
            || (seasonYearEnd.HasValue && parsedYear <= seasonYearEnd.Value
                && eventSeasonYear.HasValue && parsedYear >= eventSeasonYear.Value)
            || CricketRugbyReleaseNamePolicy.HasSplitSeasonYearMatch(originalTitle, evt);
    }

    /// <summary>
    /// True when the event is in the season that the folder names. This is
    /// the rule of the folder season filter on the candidate query. An
    /// event added by hand can have only a season number. An empty Season
    /// string is no season, the same as null.
    /// </summary>
    private static bool InFolderSeason(Event evt, string folderSeason, int? folderSeasonYear) =>
        evt.Season == folderSeason || (string.IsNullOrEmpty(evt.Season) && evt.SeasonNumber == folderSeasonYear);

    /// <summary>
    /// Why the file and the event of a match memory hit disagree, or null
    /// when they agree. The checks are the scorer's own rules for the
    /// folder season, the year and the motorsport session. Consensus from
    /// other installs does not override what this file says.
    /// </summary>
    private static string? MatchMemoryDisagreement(
        Event evt, string filename, int? parsedYear, int? seasonYearEnd, string? folderSeason, int? folderSeasonYear)
    {
        if (folderSeason != null && !InFolderSeason(evt, folderSeason, folderSeasonYear))
            return $"the folder names season {folderSeason}";
        if (parsedYear.HasValue && !YearFits(parsedYear.Value, seasonYearEnd, filename, evt))
            return $"the file names the year {parsedYear.Value}";
        if (NamesAnotherSession(SessionsOf(filename, evt.Title, evt)))
            return "the file names another session";
        return null;
    }

    /// <summary>
    /// The award of the two strong title tiers: 60 for the same title, 40
    /// when one title holds the other. Zero means a weak title. Both inputs
    /// must be normalized and not empty.
    /// </summary>
    private static int StrongTitleAward(string normalizedSearch, string normalizedEvent)
    {
        if (normalizedSearch.Equals(normalizedEvent, StringComparison.OrdinalIgnoreCase))
            return 60;
        if ((normalizedSearch.Length >= 3 && ContainsTitlePhrase(normalizedEvent, normalizedSearch)) ||
            (normalizedEvent.Length >= 3 && ContainsTitlePhrase(normalizedSearch, normalizedEvent)))
            return 40;
        return 0;
    }

    // " - " in a hub title becomes a run of spaces. Collapse each run to one
    // space, or a parsed title with single spaces can never contain it.
    internal static string NormalizeTitle(string title)
    {
        return string.Join(' ', title
            .Replace(":", " ")
            .Replace("-", " ")
            .Replace(".", " ")
            .Replace("_", " ")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Build a human-readable "Current:" label for Already-in-Library items that includes
    /// season, episode and part info so users can identify exactly what is already imported.
    /// Example: "S2025E03 - UFC Fight Night 250 Adesanya vs Imavov (pt2)"
    /// </summary>
    private static string? BuildCurrentLabel(Event? evt, EventFile? eventFile)
    {
        if (evt == null) return null;

        var label = evt.Title ?? string.Empty;

        // Prepend S/E identifier when available
        if (evt.SeasonNumber.HasValue && evt.EpisodeNumber.HasValue)
            label = $"S{evt.SeasonNumber}E{evt.EpisodeNumber} - {label}";
        else if (evt.SeasonNumber.HasValue)
            label = $"S{evt.SeasonNumber} - {label}";

        // Append part suffix when the file is a specific segment (pt1, pt2, etc.)
        if (eventFile?.PartNumber.HasValue == true && eventFile.PartNumber > 0)
            label += $" (pt{eventFile.PartNumber})";

        return label;
    }

    /// <summary>
    /// Build a preview of the destination path based on user's folder and file naming settings.
    /// Shows the path structure that will be used when the file is actually imported.
    /// Uses the same FileNamingService methods that actual import uses for consistency.
    /// Example: "UFC / Season 2025 / UFC 320 / UFC - S2025E45 - UFC 320.mkv"
    /// </summary>
    /// <summary>
    /// Public wrapper: build the destination preview for a specific event + file combination.
    /// Used by the /api/library/preview endpoint so the UI can refresh the preview after manual selection.
    /// </summary>
    public async Task<string?> BuildDestinationPreviewForEventAsync(int eventId, string originalFileName,
        string? partName = null, int? partNumber = null)
    {
        var evt = await _db.Events.Include(e => e.League).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt == null) return null;
        var settings = await GetMediaManagementSettingsAsync();

        // Apply the same part rules as the import, so the preview shows the
        // name the import gives the file.
        var config = await _configService.GetConfigAsync();
        if (EventPartDetector.IsFullEvent(partName) ||
            (config.EnableMultiPartEpisodes && !EventPartDetector.UsesParts(config.EnableMultiPartEpisodes, evt)))
        {
            partName = null;
            partNumber = null;
        }
        partNumber ??= EventPartDetector.ResolvePartNumber(partName, evt.Sport, evt.Title, evt.League?.Name);

        return await BuildDestinationPreviewAsync(evt, originalFileName, settings, partName, partNumber);
    }

    private async Task<string> BuildDestinationPreviewAsync(Event matchedEvent, string originalFileName, MediaManagementSettings settings,
        string? partName = null, int? partNumber = null)
    {
        var extension = Path.GetExtension(originalFileName);
        var episodeNumber = await _episodeNumberResolver.ResolveAsync(matchedEvent);

        // Use FileNamingService to build folder path - this handles all token replacements
        // ({League}, {Season}, {Year}, {Month}, {Day}, {Episode}, {Event Title}, etc.)
        var folderPath = _namingService.BuildFolderPath(settings, matchedEvent);

        // Build filename using the same logic as actual import
        string filename;
        if (settings.RenameEvents && !string.IsNullOrEmpty(settings.StandardFileFormat))
        {
            // Use the actual file format with all tokens including episode number
            var brandingDate = matchedEvent.BroadcastDate ?? matchedEvent.EventDate.Date;
            // Parse the real filename so the preview shows the name the
            // import will actually produce. The old hardcoded
            // "WEBDL-1080p" placeholder made every file preview as WEBDL
            // regardless of its actual quality, and the empty release
            // group made {Release Group} formats look broken.
            var parsedPreview = _fileParser.Parse(originalFileName);
            var (partSuffix, partNameSuffix) = FileNamingService.PartTokens(partName, partNumber);
            var tokens = new FileNamingTokens
            {
                EventTitle = matchedEvent.Title,
                EventTitleThe = matchedEvent.Title,
                SportarrId = matchedEvent.ExternalId ?? string.Empty,
                AirDate = brandingDate,
                Quality = parsedPreview.Quality ?? "Unknown",
                QualityFull = _fileParser.BuildQualityString(parsedPreview),
                ReleaseGroup = parsedPreview.ReleaseGroup ?? string.Empty,
                OriginalTitle = matchedEvent.Title,
                OriginalFilename = Path.GetFileNameWithoutExtension(originalFileName),
                Series = matchedEvent.League?.Name ?? matchedEvent.Sport ?? "Unknown",
                Season = matchedEvent.SeasonNumber?.ToString("0000") ?? matchedEvent.Season ?? brandingDate.Year.ToString(),
                Episode = episodeNumber.ToString("00"),
                Part = partSuffix,
                PartName = partNameSuffix
            };
            // Preview-only: no resolved file on disk and no grabbed release at
            // this point, so Size/IndexerFlag conditions can't be evaluated
            // here (same as before - this only affects the destination preview
            // shown while picking a file, not the actual import).
            tokens.CustomFormats = _customFormatService.BuildRenameToken(
                originalFileName, await _db.CustomFormats.ToListAsync());
            filename = _namingService.BuildFileName(settings.StandardFileFormat, tokens, extension, settings.ReplaceIllegalCharacters);
        }
        else
        {
            // Keep original filename
            filename = originalFileName;
        }

        // Combine folder path and filename, using " / " for display
        if (!string.IsNullOrEmpty(folderPath))
        {
            // Replace path separators with " / " for display
            var displayPath = folderPath.Replace(Path.DirectorySeparatorChar.ToString(), " / ")
                                        .Replace(Path.AltDirectorySeparatorChar.ToString(), " / ");
            return $"{displayPath} / {filename}";
        }

        return filename;
    }

    /// <summary>
    /// Extract year from file path, filename, or parsed data.
    /// Checks multiple sources: "Season 2016" in path, "S2016" in filename, parsed year, parsed date.
    /// This is CRITICAL for sports - same teams play each other every year.
    /// </summary>
    private int? ExtractYearFromPath(string filePath, string filename, int? parsedYear, DateTime? parsedDate)
    {
        // 1. Check for year from parser first
        if (parsedYear.HasValue)
            return parsedYear.Value;

        // 2. Check for year from parsed date
        if (parsedDate.HasValue)
            return parsedDate.Value.Year;

        // 3. Check file path for "Season YYYY" pattern (most reliable for organized libraries)
        var seasonMatch = System.Text.RegularExpressions.Regex.Match(filePath, @"[Ss]eason[\s\._-]*(\d{4})");
        if (seasonMatch.Success && int.TryParse(seasonMatch.Groups[1].Value, out var seasonYear))
            return seasonYear;

        // 4. Check filename for SYYYYEXX pattern (e.g., S2016E08)
        var seasonEpisodeMatch = System.Text.RegularExpressions.Regex.Match(filename, @"S(\d{4})E\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (seasonEpisodeMatch.Success && int.TryParse(seasonEpisodeMatch.Groups[1].Value, out var seYear))
            return seYear;

        // 5. Check for standalone year in filename or path (less reliable but still useful)
        var yearMatch = System.Text.RegularExpressions.Regex.Match(filePath, @"\b(19[5-9]\d|20[0-2]\d)\b");
        if (yearMatch.Success && int.TryParse(yearMatch.Value, out var year))
        {
            // Sanity check - year should be reasonable (not too far in past/future)
            if (year >= 1950 && year <= DateTime.Now.Year + 1)
                return year;
        }

        // No year found
        return null;
    }

    /// <summary>
    /// Assign episode numbers to all events in a league/season based on date+time order.
    /// This can be used to recalculate episode numbers for an entire season.
    /// </summary>
    public async Task<int> AssignEpisodeNumbersForSeasonAsync(int leagueId, string season)
    {
        var events = await _db.Events
            .Where(e => e.LeagueId == leagueId &&
                       (e.Season == season ||
                        (e.SeasonNumber.HasValue && e.SeasonNumber.ToString() == season) ||
                        (e.BroadcastDate.HasValue ? e.BroadcastDate.Value.Year.ToString() == season : e.EventDate.Year.ToString() == season)))
            .OrderBy(e => e.EventDate)
            .ThenBy(e => e.ExternalId) // Stable, unique ID for events at exact same time
            .ToListAsync();

        if (events.Count == 0)
        {
            _logger.LogDebug("[Episode Number] No events found for league {LeagueId} season {Season}", leagueId, season);
            return 0;
        }

        var updatedCount = 0;
        for (int i = 0; i < events.Count; i++)
        {
            var expectedEpisode = i + 1;
            if (events[i].EpisodeNumber != expectedEpisode)
            {
                events[i].EpisodeNumber = expectedEpisode;
                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("[Episode Number] Updated {Count} episode numbers for league {LeagueId} season {Season}",
                updatedCount, leagueId, season);
        }

        return updatedCount;
    }
}

/// <summary>
/// Result of scanning a folder for importable files
/// </summary>
public class LibraryScanResult
{
    public required string FolderPath { get; set; }
    public DateTime ScannedAt { get; set; }
    public int TotalFiles { get; set; }
    public List<ImportableFile> MatchedFiles { get; set; } = new();
    public List<ImportableFile> UnmatchedFiles { get; set; } = new();
    public List<ImportableFile> AlreadyInLibrary { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// League folders whose name fits no league in the library. Each folder
    /// shows once, with the hub's best suggestion when the scan asked the hub.
    /// </summary>
    public List<UnresolvedFolder> UnresolvedFolders { get; set; } = new();
}

/// <summary>A league folder that fits no league in the library.</summary>
public sealed record UnresolvedFolder(
    string Path,
    string Name,
    string? SuggestedLeagueExternalId,
    string? SuggestedLeagueName,
    string? SuggestedSport,
    // The hub's event format. It tells a league without teams from a team
    // league when the sport name does not.
    string? SuggestedSportFormat);

/// <summary>
/// A file that can potentially be imported
/// </summary>
public class ImportableFile
{
    public required string FilePath { get; set; }
    public required string FileName { get; set; }
    public long FileSize { get; set; }
    public string? ParsedTitle { get; set; }
    public string? ParsedOrganization { get; set; }
    public string? ParsedSport { get; set; }
    public DateTime? ParsedDate { get; set; }
    public string? Quality { get; set; }
    /// <summary>Source bucket (WEBDL / BLURAY / HDTV / DVDRIP / RAWHD) parsed from the
    /// filename, with extension hint and ffprobe augmentation as fallbacks.</summary>
    public string? Source { get; set; }
    /// <summary>Video codec normalized form (x264 / x265 / AV1 / VP9 / MPEG2 / XviD)
    /// — falls back to ffprobe inspection when the filename has no codec token.</summary>
    public string? Codec { get; set; }
    /// <summary>Audio codec (AAC / AC3 / E-AC-3 / DTS / TrueHD / FLAC / Opus / MP3) —
    /// from filename or ffprobe.</summary>
    public string? AudioCodec { get; set; }
    /// <summary>Release group token from filename's trailing "-GROUP".</summary>
    public string? ReleaseGroup { get; set; }
    /// <summary>The full original filename without extension — preserved verbatim so
    /// the user can re-search the indexer with this exact title later.</summary>
    public string? OriginalTitle { get; set; }
    /// <summary>Languages detected by ffprobe from audio stream language tags.</summary>
    public List<string> Languages { get; set; } = new();
    public int? MatchedEventId { get; set; }
    public string? MatchedEventTitle { get; set; }
    public string? MatchedLeagueName { get; set; }
    public string? MatchedSeason { get; set; }
    /// <summary>
    /// Preview of destination path based on user's folder settings (e.g., "UFC / Season 2024 / UFC 310.mkv")
    /// </summary>
    public string? DestinationPreview { get; set; }
    public int? MatchConfidence { get; set; }

    /// <summary>
    /// True when the match came from match memory. The page sends it back
    /// with the import, so a served answer does not count as a new report.
    /// </summary>
    public bool MatchedByMemory { get; set; }
    public int? ExistingEventId { get; set; }

    /// <summary>The league that the file's league folder names, when one fits.</summary>
    public int? FolderLeagueId { get; set; }
    public string? FolderLeagueName { get; set; }

    /// <summary>The league's own season string for the file's season folder.</summary>
    public string? FolderSeason { get; set; }

    /// <summary>
    /// Why the file may not take the place of the file the matched event
    /// already holds, as Library Import shows it. Empty when the event holds
    /// no file for this part or the file is an upgrade. A manual import
    /// replaces regardless, except where source precedence holds it back.
    /// </summary>
    public List<string> Rejections { get; set; } = new();

    /// <summary>
    /// Source precedence keeps the event's indexer file: a manual import of
    /// this recording is rejected unless the request sets
    /// OverrideSourcePrecedence.
    /// </summary>
    public bool HeldBySourcePrecedence { get; set; }

    public string FileSizeFormatted => FormatBytes(FileSize);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Request to import a specific file
/// </summary>
public class FileImportRequest
{
    public required string FilePath { get; set; }
    public int? EventId { get; set; }
    public bool CreateNew { get; set; }

    /// <summary>
    /// The event that the scan suggested when the user imported the file,
    /// or null when it had none. The match report compares it with EventId.
    /// </summary>
    public int? SuggestedEventId { get; set; }

    /// <summary>
    /// The user chose the event of this file, so the import reports the
    /// match as anonymous usage data. Only the server sets this, and only
    /// for the Library Import page and an accepted pending import. An
    /// automatic import leaves it false.
    /// </summary>
    public bool ReportMatch { get; set; }

    /// <summary>
    /// The suggestion came from match memory. An import that keeps it sends
    /// the hub's own answer back, so it is not reported. The page sets this.
    /// </summary>
    public bool SuggestedByMemory { get; set; }

    /// <summary>
    /// An automatic import (the file watcher, a rescan, a completed download
    /// Sportarr did not grab): the file may take the place of a file the
    /// event already holds only when it is an upgrade. A manual import leaves
    /// this false and replaces regardless, except where source precedence
    /// keeps an indexer file from an IPTV recording.
    /// </summary>
    public bool OnlyIfUpgrade { get; set; }

    /// <summary>
    /// With OnlyIfUpgrade, a file that is only as good as the held file is
    /// rejected and the held file stays.
    /// </summary>
    public bool RejectEqual { get; set; }

    /// <summary>
    /// Reject the file when the event holds no file for its part. The caller
    /// then decides another way.
    /// </summary>
    public bool RequireOccupant { get; set; }

    /// <summary>
    /// Import anyway: the user chose to let this IPTV recording replace the
    /// indexer file source precedence would keep.
    /// </summary>
    public bool OverrideSourcePrecedence { get; set; }

    /// <summary>
    /// Remove the held file after an explicit replacement, even when both files share a folder.
    /// </summary>
    public bool ReplaceOccupiedFile { get; set; }

    /// <summary>
    /// The file is an IPTV recording made by the DVR. Source precedence
    /// keeps it from replacing a file that came from an indexer.
    /// </summary>
    public bool IsIptvRecording { get; set; }

    /// <summary>
    /// The file came from a download client, so it is a release and never
    /// an IPTV recording, whatever its container.
    /// </summary>
    public bool FromDownloadClient { get; set; }
    public string? EventTitle { get; set; }
    public string? Organization { get; set; }
    public DateTime? EventDate { get; set; }
    public string? Quality { get; set; }

    /// <summary>
    /// Manual part name override (e.g., "Early Prelims", "Main Card", "Practice", "Race")
    /// If specified, overrides auto-detected part
    /// </summary>
    public string? PartName { get; set; }

    /// <summary>
    /// Manual part number override (1, 2, 3, etc.)
    /// If specified, overrides auto-detected part number
    /// </summary>
    public int? PartNumber { get; set; }

    /// <summary>
    /// League ID for creating new events
    /// </summary>
    public int? LeagueId { get; set; }

    /// <summary>
    /// Season string for creating new events
    /// </summary>
    public string? Season { get; set; }

    /// <summary>
    /// Import mode: "move", "copy", or "hardlink". When omitted, Auto follows
    /// the global settings (hardlink when Use Hardlinks is on, else copy when
    /// Copy Files is on, else move).
    /// - "move": Moves files from source to destination
    /// - "copy": Always copies the actual bytes, even when Use Hardlinks is on
    /// - "hardlink": Creates hardlinks regardless of the global UseHardlinks
    ///   setting, falling back to copy (never move) so seeds stay intact
    /// </summary>
    public string? ImportMode { get; set; }

    /// <summary>
    /// Optional pre-import metadata overrides supplied by the user via the
    /// FileMetadataEditor. Applied to the new EventFile after creation so
    /// user-corrected values stick instead of getting overwritten by the
    /// parser. Mirrors the EventFileEditRequest shape one-to-one.
    /// </summary>
    public string? Source { get; set; }
    public string? Codec { get; set; }
    public string? ReleaseGroup { get; set; }
    public string? OriginalTitle { get; set; }
    public List<string>? Languages { get; set; }
    public string? IndexerFlags { get; set; }
}

/// <summary>
/// Import mode for library import.
/// </summary>
public enum LibraryImportMode
{
    /// <summary>
    /// Move files from source to destination (default for manual import)
    /// </summary>
    Move,

    /// <summary>
    /// Copy files or create hardlinks based on UseHardlinks setting
    /// </summary>
    Copy,

    /// <summary>
    /// Explicitly requested hardlinks (per-import, independent of the global
    /// UseHardlinks setting). Falls back to copy on failure - never move -
    /// so the source file always survives for seeding.
    /// </summary>
    Hardlink
}

/// <summary>
/// Result of importing files
/// </summary>
/// <summary>A file an automatic import left where it was, and why.</summary>
public sealed record ImportRejection(string FilePath, string Reason)
{
    public override string ToString() => $"{FilePath}: {Reason}";
}

public class ImportResult
{
    public List<string> Imported { get; set; } = new();
    public List<string> Created { get; set; } = new();
    public List<string> Skipped { get; set; } = new();

    /// <summary>Files an automatic import left where they were, with the reason.</summary>
    public List<ImportRejection> Rejected { get; set; } = new();
    public List<string> Failed { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
