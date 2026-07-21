using System.Runtime.InteropServices;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
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
    private readonly SportarrApiClient _sportarrApiClient;
    private readonly DiskSpaceService _diskSpaceService;
    private readonly NotificationService _notificationService;

    private static readonly string[] VideoExtensions = SupportedExtensions.Video;

    public LibraryImportService(
        SportarrDbContext db,
        ILogger<LibraryImportService> logger,
        MediaFileParser fileParser,
        SportsFileNameParser sportsParser,
        FileNamingService namingService,
        EventPartDetector partDetector,
        ConfigService configService,
        SportarrApiClient sportarrApiClient,
        DiskSpaceService diskSpaceService,
        CustomFormatService customFormatService,
        NotificationService notificationService)
    {
        _db = db;
        _logger = logger;
        _fileParser = fileParser;
        _sportsParser = sportsParser;
        _namingService = namingService;
        _partDetector = partDetector;
        _configService = configService;
        _sportarrApiClient = sportarrApiClient;
        _diskSpaceService = diskSpaceService;
        _customFormatService = customFormatService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Scan a folder for video files
    /// </summary>
    public async Task<LibraryScanResult> ScanFolderAsync(string folderPath, bool includeSubfolders = true)
    {
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

            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = SampleFileFilter.FilterSamples(
                    LibraryPathFilter.FilterExcluded(
                        Directory.GetFiles(folderPath, "*.*", searchOption)
                            .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLower()))),
                    folderPath)
                .ToList();

            result.TotalFiles = files.Count;

            // User-ignored files: rejecting a pending import blocklists the
            // file's path. Scans skip those entirely so an ignored file
            // doesn't resurface as matched/unmatched on every rescan
            // (DiskScanService and the file watcher apply the same rule).
            var ignoredPaths = new HashSet<string>(
                await _db.Blocklist
                    .Where(b => b.FilePath != null)
                    .Select(b => b.FilePath!)
                    .ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            // Track event IDs claimed by earlier files in this batch so two files can't match the same event
            var claimedEventIds = new HashSet<int>();

            foreach (var filePath in files)
            {
                if (ignoredPaths.Contains(filePath))
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

                    // Use sports parser if it has high confidence
                    var eventTitle = sportsResult.Confidence >= 60 && !string.IsNullOrEmpty(sportsResult.EventTitle)
                        ? sportsResult.EventTitle
                        : parsedInfo.EventTitle;

                    var organization = sportsResult.Organization;
                    var sport = sportsResult.Sport;
                    var eventDate = sportsResult.EventDate ?? parsedInfo.AirDate;

                    // Extract year from filename, path, and parsed data
                    // CRITICAL: This prevents matching files to wrong events from different years
                    var parsedYear = ExtractYearFromPath(filePath, filename, sportsResult.EventYear, eventDate);

                    // Check for explicit SxxxxExx episode number in filename (e.g. "Formula E - S2025E05 - Jeddah E Prix").
                    // This is the highest-confidence signal — explicit S/E parsing wins over fuzzier matchers.
                    int? explicitEpisodeNumber = null;
                    string? seriesLabel = null;
                    var seMatch = System.Text.RegularExpressions.Regex.Match(filename, @"S\d{4}E(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (seMatch.Success && int.TryParse(seMatch.Groups[1].Value, out var seEp))
                    {
                        explicitEpisodeNumber = seEp;
                        // Library-format names carry the series/league ahead of the
                        // S/E token ("V8 Supercars - S2026E19 - …"). That label is
                        // the file's own statement of WHICH series it belongs to.
                        seriesLabel = filename.Substring(0, seMatch.Index).Trim(' ', '-', '.', '_');
                    }

                    // Detect multi-part files (e.g. "UFC - S2025E04 - pt3 - UFC 312..."
                    // or the episode-attached form "S2024E107pt2"). The lookbehind
                    // replaces \b, which never fires between a digit and 'p'
                    // (both word characters), while still rejecting words that
                    // merely contain pt+digits (Egypt2026).
                    var isPartFile = System.Text.RegularExpressions.Regex.IsMatch(filename, @"(?<![a-zA-Z])pt\d+\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    // Check if file is already in library
                    // First check Event.FilePath (main file path)
                    var existingEvent = await _db.Events
                        .FirstOrDefaultAsync(e => e.FilePath == filePath);

                    // Also check EventFiles table (for multi-part episodes and re-imports)
                    var existingEventFile = await _db.EventFiles
                        .Include(ef => ef.Event)
                        .FirstOrDefaultAsync(ef => ef.FilePath == filePath);

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

                    // Try to find a matching event using multiple strategies
                    Event? matchedEvent = null;
                    int matchConfidence = 0;

                    // AUTHORITATIVE ID TOKEN (docs/RELEASE_NAMING.md): a file
                    // named with {sportarr-ev-XXXXXXX}, or carrying an
                    // embedded SPORTARR tag (surfaced via ffprobe on
                    // parsedInfo), identifies its event exactly - look it up
                    // directly and skip fuzzy scoring. The same
                    // claimed/HasFile eligibility rules apply as for fuzzy
                    // matches, so a token can't double-assign an event
                    // within one scan batch.
                    var scanTokenId = sportsResult.SportarrEventId ?? parsedInfo.SportarrEventId;
                    if (!string.IsNullOrEmpty(scanTokenId))
                    {
                        var tokenEventId = scanTokenId;
                        var tokenEvent = await _db.Events
                            .Include(e => e.League)
                            .FirstOrDefaultAsync(e => e.ExternalId == tokenEventId
                                && (isPartFile || (!e.HasFile && !claimedEventIds.Contains(e.Id))));
                        if (tokenEvent != null)
                        {
                            matchedEvent = tokenEvent;
                            matchConfidence = 100;
                            if (!isPartFile)
                                claimedEventIds.Add(tokenEvent.Id);
                            _logger.LogInformation("[Library Import] Id token match: '{File}' is tagged {Token} = '{Event}'",
                                filename, tokenEventId, tokenEvent.Title);
                        }
                        else
                        {
                            _logger.LogWarning("[Library Import] File '{File}' carries id token {Token} but no eligible local event has that id - falling back to fuzzy matching",
                                filename, tokenEventId);
                        }
                    }

                    if (matchedEvent == null && !string.IsNullOrEmpty(eventTitle))
                    {
                        // Load candidates, excluding events already claimed by earlier files in this scan batch
                        // and events that already have files.
                        // Exception: part files (pt2, pt3…) may match the same event as their main file,
                        // so they bypass BOTH filters - the whole point of a ptN file is
                        // adding another file to an event that already has one, so the
                        // !HasFile filter would otherwise exclude exactly the right event
                        // and the file would sit unmatched at 0% confidence.
                        var candidates = await _db.Events
                            .Include(e => e.League)
                            .Where(e => isPartFile || (!e.HasFile && !claimedEventIds.Contains(e.Id)))
                            .ToListAsync();

                        foreach (var candidate in candidates)
                        {
                            var confidence = CalculateMatchConfidence(
                                eventTitle, candidate.Title, organization, candidate,
                                eventDate, parsedYear, sportsResult.RoundNumber,
                                sportsResult.SeasonYearEnd, explicitEpisodeNumber,
                                sportsResult.Location, _logger, sport, seriesLabel);
                            if (confidence > matchConfidence)
                            {
                                matchConfidence = confidence;
                                matchedEvent = candidate;
                            }
                        }

                        // Only accept matches with reasonable confidence
                        if (matchConfidence < 40)
                        {
                            matchedEvent = null;
                            matchConfidence = 0;
                        }

                        // Reserve this event so no other file in this batch can claim it.
                        // Part files don't claim exclusively — multiple parts share the same event.
                        if (matchedEvent != null && !isPartFile)
                            claimedEventIds.Add(matchedEvent.Id);
                    }

                    // Build destination preview for matched files
                    string? destinationPreview = null;
                    if (matchedEvent != null)
                    {
                        destinationPreview = await BuildDestinationPreviewAsync(matchedEvent, fileInfo.Name, settings);
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
                        MatchConfidence = matchConfidence > 0 ? matchConfidence : null
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
    /// Import matched files into library - moves/copies/hardlinks files to library folder
    /// </summary>
    public async Task<ImportResult> ImportFilesAsync(List<FileImportRequest> requests)
    {
        var result = new ImportResult();

        // Successful imports queued for OnDownload notifications after the
        // batch save, so media-server connections (Plex/Jellyfin/Emby)
        // refresh for manual imports exactly like automatic ones.
        var notifyQueue = new List<(Event Evt, string Path, string Quality)>();

        // Get media management settings for file transfer
        var settings = await GetMediaManagementSettingsAsync();
        var config = await _configService.GetConfigAsync();

        foreach (var request in requests)
        {
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

                // Parse import mode from request: "move", "copy", or "hardlink"
                // Default (Auto) when the request doesn't specify a mode, mirroring
                // the automatic-import planner's precedence:
                // - UseHardlinks=true: Hardlink (regardless of CopyFiles - users who
                //   enabled "Use Hardlinks instead of Copy" expect manual imports to
                //   honor it and keep torrents seeding from the source path)
                // - UseHardlinks=false + CopyFiles=true: Copy
                // - both off: Move (removes source files)
                var importMode = settings.UseHardlinks
                    ? LibraryImportMode.Hardlink
                    : settings.CopyFiles
                        ? LibraryImportMode.Copy
                        : LibraryImportMode.Move;
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

                        // If user selected "Full Event", treat as no part (null)
                        if (EventPartDetector.IsFullEvent(partName))
                        {
                            partName = null;
                            partNumber = null;
                        }
                        else if (string.IsNullOrEmpty(partName) && config.EnableMultiPartEpisodes)
                        {
                            // Auto-detect part from filename
                            var partInfo = _partDetector.DetectPart(parsedInfo.EventTitle, existingEvent.Sport);
                            partName = partInfo?.SegmentName;
                            partNumber = partInfo?.PartNumber;
                        }

                        // Build destination path and transfer file - pass part info and import mode
                        var destinationPath = await TransferFileToLibraryAsync(
                            request.FilePath,
                            existingEvent,
                            parsedInfo,
                            settings,
                            config,
                            partName,
                            partNumber,
                            importMode,
                            modeWasExplicit);

                        // Update event with new file info
                        existingEvent.FilePath = destinationPath;
                        existingEvent.HasFile = true;
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
                            existingFileRecord.PartName = partName;
                            existingFileRecord.PartNumber = partNumber;
                            existingFileRecord.LastVerified = DateTime.UtcNow;
                            existingFileRecord.Exists = true;

                            _logger.LogInformation("Re-imported file to existing event: {EventTitle} -> {FilePath} (Part: {PartName})",
                                existingEvent.Title, destinationPath, partName ?? "N/A");
                        }
                        else
                        {
                            // Guard against creating a second row for a path some record already
                            // holds (the source path was checked above, but the destination path
                            // can differ, and one file must map to exactly one EventFile row).
                            // Without this, the import would create a duplicate episode.
                            var existingByDest = await _db.EventFiles
                                .FirstOrDefaultAsync(f => f.FilePath == destinationPath);

                            if (existingByDest != null)
                            {
                                existingByDest.EventId = existingEvent.Id;
                                existingByDest.Size = sourceFileSize;
                                existingByDest.Quality = request.Quality ?? _fileParser.BuildQualityString(parsedInfo);
                                existingByDest.PartName = partName;
                                existingByDest.PartNumber = partNumber;
                                existingByDest.LastVerified = DateTime.UtcNow;
                                existingByDest.Exists = true;

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
                                    Size = sourceFileSize,
                                    Quality = request.Quality ?? _fileParser.BuildQualityString(parsedInfo),
                                    Codec = request.Codec ?? parsedInfo.VideoCodec,
                                    Source = request.Source ?? parsedInfo.Source,
                                    ReleaseGroup = request.ReleaseGroup ?? parsedInfo.ReleaseGroup,
                                    OriginalTitle = request.OriginalTitle,
                                    Languages = request.Languages ?? new List<string>(),
                                    IndexerFlags = request.IndexerFlags,
                                    PartName = partName,
                                    PartNumber = partNumber,
                                    Added = DateTime.UtcNow,
                                    LastVerified = DateTime.UtcNow,
                                    Exists = true
                                };
                                _db.EventFiles.Add(eventFile);

                                _logger.LogInformation("Imported file to existing event: {EventTitle} -> {FilePath} (Part: {PartName})",
                                    existingEvent.Title, destinationPath, partName ?? "N/A");
                            }
                        }

                        result.Imported.Add(destinationPath);
                        notifyQueue.Add((existingEvent, destinationPath,
                            request.Quality ?? _fileParser.BuildQualityString(parsedInfo)));
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

                    var newEvent = new Event
                    {
                        Title = eventTitle,
                        Sport = sport,
                        LeagueId = request.LeagueId,
                        League = league,
                        Season = request.Season,
                        EventDate = request.EventDate ?? parsedInfo.AirDate ?? DateTime.UtcNow,
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

                    // If user selected "Full Event", treat as no part (null)
                    if (EventPartDetector.IsFullEvent(partName))
                    {
                        partName = null;
                        partNumber = null;
                    }
                    else if (string.IsNullOrEmpty(partName) && config.EnableMultiPartEpisodes && !string.IsNullOrEmpty(parsedInfo.EventTitle))
                    {
                        // Auto-detect part from filename
                        var partInfo = _partDetector.DetectPart(parsedInfo.EventTitle, sport);
                        partName = partInfo?.SegmentName;
                        partNumber = partInfo?.PartNumber;
                    }

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
                    newEvent.HasFile = true;

                    // Create EventFile record (part info already determined above).
                    // User-supplied overrides take precedence over parser values.
                    var eventFile = new EventFile
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
                        PartName = partName,
                        PartNumber = partNumber,
                        Added = DateTime.UtcNow,
                        LastVerified = DateTime.UtcNow,
                        Exists = true
                    };
                    _db.EventFiles.Add(eventFile);

                    result.Created.Add(destinationPath);
                    notifyQueue.Add((newEvent, destinationPath,
                        request.Quality ?? _fileParser.BuildQualityString(parsedInfo)));
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
                _logger.LogError(ex, "Failed to import file: {FilePath}", request.FilePath);
                result.Failed.Add(request.FilePath);
                result.Errors.Add($"{Path.GetFileName(request.FilePath)}: {ex.Message}");
            }
        }

        await _db.SaveChangesAsync();

        // NOTIFICATIONS: manual imports fire the same OnDownload
        // notifications as automatic imports. Previously only the
        // download-queue path notified, so wizard-imported files never
        // triggered a media-server library refresh until a manual scan.
        foreach (var (evt, path, quality) in notifyQueue)
        {
            try
            {
                await _notificationService.SendNotificationAsync(
                    NotificationTrigger.OnDownload,
                    $"Imported: {evt.Title}",
                    $"File: {Path.GetFileName(path)}\nQuality: {quality}",
                    new Dictionary<string, object>
                    {
                        { "eventId", evt.Id },
                        { "eventTitle", evt.Title ?? "" },
                        { "league", evt.League?.Name ?? "" },
                        { "sport", evt.Sport ?? "" },
                        { "filePath", path },
                        { "quality", quality }
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
    /// Transfer file to library folder with proper naming
    /// </summary>
    private async Task<string> TransferFileToLibraryAsync(
        string sourcePath,
        Event eventInfo,
        ParsedFileInfo parsed,
        MediaManagementSettings settings,
        Config config,
        string? partName = null,
        int? partNumber = null,
        LibraryImportMode importMode = LibraryImportMode.Move,
        bool modeWasExplicit = false)
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
        var episodeNumber = await GetApiEpisodeNumberAsync(eventInfo);
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
            // Build part suffix from provided part info (already determined by caller)
            // Part info can come from: 1) Manual UI selection, 2) Auto-detection from filename
            string partSuffix = string.Empty;
            if (!string.IsNullOrEmpty(partName))
            {
                // Build suffix like " - Part 1 (Early Prelims)" or " - Early Prelims"
                if (partNumber.HasValue)
                {
                    partSuffix = $" - Part {partNumber} ({partName})";
                }
                else
                {
                    partSuffix = $" - {partName}";
                }
                _logger.LogDebug("[Import] Using part info for filename: {PartName} (Part {PartNumber})",
                    partName, partNumber?.ToString() ?? "N/A");
            }
            else if (config.EnableMultiPartEpisodes)
            {
                // Fallback: try auto-detection from original filename if no part info provided
                var detectedPart = _partDetector.DetectPart(parsed.EventTitle, eventInfo.Sport);
                if (detectedPart != null)
                {
                    partSuffix = $" - {detectedPart.PartSuffix}";
                    _logger.LogDebug("[Import] Auto-detected multi-part episode: {Segment} ({PartSuffix})",
                        detectedPart.SegmentName, detectedPart.PartSuffix);
                }
            }

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
                Part = partSuffix
            };

            tokens.CustomFormats = _customFormatService.BuildRenameToken(
                Path.GetFileName(sourcePath), await _db.CustomFormats.ToListAsync());

            filename = _namingService.BuildFileName(settings.StandardFileFormat, tokens, extension, settings.ReplaceIllegalCharacters);
        }
        else
        {
            filename = Path.GetFileName(sourcePath);
        }

        destinationPath = Path.Combine(destinationPath, filename);

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

        // Transfer file based on import mode
        await TransferFileAsync(sourcePath, destinationPath, settings, importMode, modeWasExplicit);

        // Set permissions (Linux/macOS only)
        if (settings.SetPermissions && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetFilePermissions(destinationPath, settings);
        }

        return destinationPath;
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
        using var destStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

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
                StandardFileFormat = "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}",
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
        string? seriesLabel = null)
    {
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

        // ── SPORT GATE ──────────────────────────────────────────────────────────
        // When the filename parser identified the sport, an event from a
        // different sport can never be the right match, no matter how the
        // fuzzy title arithmetic works out. Without this a soccer World Cup
        // file auto-matched a World Snooker event.
        if (!string.IsNullOrEmpty(parsedSport) && !string.IsNullOrEmpty(evt.Sport) &&
            !evt.Sport.Equals(parsedSport, StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogDebug("[Match] Sport gate: file parsed as {ParsedSport}, event '{Event}' is {EventSport} - rejecting",
                parsedSport, eventTitle, evt.Sport);
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
        if (parsedRoundNumber.HasValue && eventRoundNumber.HasValue)
        {
            // Authoritative when the event carries real round data.
            if (eventRoundNumber.Value == parsedRoundNumber.Value)
            {
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

        // ── EXPLICIT S/E EPISODE NUMBER ─────────────────────────────────────────
        // Files with S2025E05 in the name — this IS authoritative since it's the
        // Sportarr season/episode format used in library filenames.
        if (explicitEpisodeNumber.HasValue && evt.EpisodeNumber.HasValue)
        {
            if (evt.EpisodeNumber.Value == explicitEpisodeNumber.Value)
                confidence += 50;
            else
                return 0; // S/E notation is authoritative — wrong event
        }

        // ── YEAR / SEASON CHECK ─────────────────────────────────────────────────
        // CRITICAL: sports events repeat every year. Year mismatch = wrong season.
        if (parsedYear.HasValue)
        {
            // Broadcaster-branded year — release groups tag NYE late-Eastern
            // airings with the broadcast year, not the UTC year.
            var eventYear = (evt.BroadcastDate ?? evt.EventDate).Year;
            var eventSeasonYear = evt.SeasonNumber ?? (int.TryParse(evt.Season, out var sy) ? sy : (int?)null);

            // Accept if parsedYear matches:
            // (a) the event's calendar year directly, OR
            // (b) the start year of the season (e.g. SeasonNumber = 2025 for "2025-2026"), OR
            // (c) the end year of a season span (e.g. SeasonYearEnd = 2026 for "2025-2026")
            var yearMatches = eventYear == parsedYear.Value
                || eventSeasonYear == parsedYear.Value
                || (seasonYearEnd.HasValue && parsedYear.Value <= seasonYearEnd.Value
                    && eventSeasonYear.HasValue && parsedYear.Value >= eventSeasonYear.Value);

            if (!yearMatches)
            {
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
        if (parsedDate.HasValue)
        {
            var gateDiff = Math.Abs((evt.EventDate.Date - parsedDate.Value.Date).TotalDays);
            if (evt.BroadcastDate.HasValue)
            {
                gateDiff = Math.Min(gateDiff, Math.Abs((evt.BroadcastDate.Value.Date - parsedDate.Value.Date).TotalDays));
            }
            if (gateDiff > 7)
            {
                logger?.LogDebug("[Match] Date gate: file dated {FileDate:yyyy-MM-dd}, event '{Event}' is {EventDate:yyyy-MM-dd} ({Diff:F0} days apart) - rejecting",
                    parsedDate.Value, eventTitle, evt.EventDate, gateDiff);
                return 0;
            }
        }

        // ── TITLE SIMILARITY ────────────────────────────────────────────────────
        var normalizedSearch = NormalizeTitle(searchTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);

        // A degenerate parsed title carries no signal. Without this guard the
        // contains branch below hands EVERY event a 40-point award when the
        // search string is empty (string.Contains("") is true), which put an
        // arbitrary event exactly on the acceptance floor.
        if (string.IsNullOrWhiteSpace(normalizedSearch) || string.IsNullOrWhiteSpace(normalizedEvent))
        {
            return 0;
        }

        if (normalizedSearch.Equals(normalizedEvent, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 60;
        }
        else if ((normalizedSearch.Length >= 3 && normalizedEvent.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)) ||
                 (normalizedEvent.Length >= 3 && normalizedSearch.Contains(normalizedEvent, StringComparison.OrdinalIgnoreCase)))
        {
            confidence += 40;
        }
        else
        {
            // Partial word match — also check location against event title.
            // Connector words are excluded from the overlap: every matchup
            // title contains "vs", so counting it handed any two-team title
            // free similarity against every other two-team event.
            var searchWords = normalizedSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !MatchConnectorWords.Contains(w)).ToArray();
            var eventWords = normalizedEvent.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !MatchConnectorWords.Contains(w)).ToArray();

            // If we have a parsed location, include it in the word set for matching
            if (!string.IsNullOrEmpty(parsedLocation))
            {
                var locationWords = parsedLocation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                searchWords = searchWords.Union(locationWords, StringComparer.OrdinalIgnoreCase).ToArray();
            }

            var matchingWords = searchWords.Intersect(eventWords, StringComparer.OrdinalIgnoreCase).Count();
            var totalWords = Math.Max(searchWords.Length, eventWords.Length);

            if (matchingWords > 0 && totalWords > 0)
            {
                var matchPercent = (double)matchingWords / totalWords;
                confidence += (int)(30 * matchPercent);
            }
        }

        // ── ORGANIZATION → LEAGUE ───────────────────────────────────────────────
        // Hard cross-sport gate: if we identified an organization (IndyCar, NBA, NFL…),
        // reject any event from a different league immediately. This prevents IndyCar
        // files from matching NBA events, even when the title fuzzy score is high.
        if (!string.IsNullOrEmpty(organization) && evt.League != null)
        {
            var leagueMatch = evt.League.Name.Contains(organization, StringComparison.OrdinalIgnoreCase)
                           || organization.Contains(evt.League.Name, StringComparison.OrdinalIgnoreCase);
            if (!leagueMatch)
                return 0; // Wrong sport — eliminate before any title comparison
            confidence += 15;
        }

        // ── DATE PROXIMITY ──────────────────────────────────────────────────────
        if (parsedDate != null)
        {
            var daysDiff = Math.Abs((evt.EventDate - parsedDate.Value).TotalDays);
            if (daysDiff <= 1) confidence += 15;
            else if (daysDiff <= 3) confidence += 10;
            else if (daysDiff <= 7) confidence += 5;
        }

        // ── RECENCY ─────────────────────────────────────────────────────────────
        if (Math.Abs((DateTime.UtcNow - evt.EventDate).TotalDays) <= 30)
        {
            confidence += 5;
        }

        return Math.Min(100, confidence);
    }

    /// <summary>
    /// Words that connect matchup titles rather than identify them. Present
    /// in virtually every two-team title, so they carry zero matching signal.
    /// </summary>
    private static readonly HashSet<string> MatchConnectorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "vs", "v", "versus", "at", "the", "and", "of", "@",
    };

    private static string NormalizeTitle(string title)
    {
        return title
            .Replace(":", " ")
            .Replace("-", " ")
            .Replace(".", " ")
            .Replace("_", " ")
            .Replace("  ", " ")
            .Trim();
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
    public async Task<string?> BuildDestinationPreviewForEventAsync(int eventId, string originalFileName)
    {
        var evt = await _db.Events.Include(e => e.League).FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt == null) return null;
        var settings = await GetMediaManagementSettingsAsync();
        return await BuildDestinationPreviewAsync(evt, originalFileName, settings);
    }

    private async Task<string> BuildDestinationPreviewAsync(Event matchedEvent, string originalFileName, MediaManagementSettings settings)
    {
        var extension = Path.GetExtension(originalFileName);

        // Use FileNamingService to build folder path - this handles all token replacements
        // ({League}, {Season}, {Year}, {Month}, {Day}, {Episode}, {Event Title}, etc.)
        var folderPath = _namingService.BuildFolderPath(settings, matchedEvent);

        // Build filename using the same logic as actual import
        string filename;
        if (settings.RenameEvents && !string.IsNullOrEmpty(settings.StandardFileFormat))
        {
            // Use the actual file format with all tokens including episode number
            var episodeNumber = matchedEvent.EpisodeNumber ?? 1;
            var brandingDate = matchedEvent.BroadcastDate ?? matchedEvent.EventDate.Date;
            // Parse the real filename so the preview shows the name the
            // import will actually produce. The old hardcoded
            // "WEBDL-1080p" placeholder made every file preview as WEBDL
            // regardless of its actual quality, and the empty release
            // group made {Release Group} formats look broken.
            var parsedPreview = _fileParser.Parse(originalFileName);
            var tokens = new FileNamingTokens
            {
                EventTitle = matchedEvent.Title,
                EventTitleThe = matchedEvent.Title,
                AirDate = brandingDate,
                Quality = parsedPreview.Quality ?? "Unknown",
                QualityFull = _fileParser.BuildQualityString(parsedPreview),
                ReleaseGroup = parsedPreview.ReleaseGroup ?? string.Empty,
                OriginalTitle = matchedEvent.Title,
                OriginalFilename = Path.GetFileNameWithoutExtension(originalFileName),
                Series = matchedEvent.League?.Name ?? matchedEvent.Sport ?? "Unknown",
                Season = matchedEvent.SeasonNumber?.ToString("0000") ?? matchedEvent.Season ?? brandingDate.Year.ToString(),
                Episode = episodeNumber.ToString("00"),
                Part = string.Empty
            };
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
    /// Get episode number from the sportarr.net API - this is the source of truth for Plex/Jellyfin/Emby metadata.
    /// Falls back to existing episode number if API call fails.
    /// </summary>
    private async Task<int> GetApiEpisodeNumberAsync(Event eventInfo)
    {
        // If event already has an episode number from API sync, use it
        if (eventInfo.EpisodeNumber.HasValue && eventInfo.EpisodeNumber.Value > 0)
        {
            _logger.LogDebug("[Episode Number] Using existing API episode number E{EpisodeNumber} for event {EventTitle}",
                eventInfo.EpisodeNumber.Value, eventInfo.Title);
            return eventInfo.EpisodeNumber.Value;
        }

        // No episode number - fetch from API
        if (!eventInfo.LeagueId.HasValue)
        {
            _logger.LogWarning("[Episode Number] No league for event {EventTitle}, defaulting to episode 1", eventInfo.Title);
            return 1;
        }

        var league = await _db.Leagues.FindAsync(eventInfo.LeagueId.Value);
        if (league == null || string.IsNullOrEmpty(league.ExternalId))
        {
            _logger.LogWarning("[Episode Number] League not found or has no ExternalId for event {EventTitle}, defaulting to episode 1", eventInfo.Title);
            return 1;
        }

        var season = eventInfo.Season ?? eventInfo.SeasonNumber?.ToString() ?? (eventInfo.BroadcastDate ?? eventInfo.EventDate).Year.ToString();

        try
        {
            var apiEpisodeMap = await _sportarrApiClient.GetEpisodeNumbersFromApiAsync(league.ExternalId, season);
            if (apiEpisodeMap != null && !string.IsNullOrEmpty(eventInfo.ExternalId) &&
                apiEpisodeMap.TryGetValue(eventInfo.ExternalId, out var apiEpisodeNumber))
            {
                _logger.LogInformation("[Episode Number] Got episode E{EpisodeNumber} from API for event {EventTitle}",
                    apiEpisodeNumber, eventInfo.Title);
                return apiEpisodeNumber;
            }
            else
            {
                _logger.LogWarning("[Episode Number] Event {EventTitle} not found in API episode map (ExternalId: {ExternalId}), defaulting to episode 1",
                    eventInfo.Title, eventInfo.ExternalId);
                return 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Episode Number] Failed to fetch API episode number for event {EventTitle}, defaulting to episode 1", eventInfo.Title);
            return 1;
        }
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
}

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
    public int? ExistingEventId { get; set; }

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
public class ImportResult
{
    public List<string> Imported { get; set; } = new();
    public List<string> Created { get; set; } = new();
    public List<string> Skipped { get; set; } = new();
    public List<string> Failed { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
