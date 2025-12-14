using System.Runtime.InteropServices;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Handles scanning filesystem and importing existing event files into library
/// Performs actual file move/copy/hardlink operations with proper renaming
/// </summary>
public class LibraryImportService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<LibraryImportService> _logger;
    private readonly MediaFileParser _fileParser;
    private readonly SportsFileNameParser _sportsParser;
    private readonly FileNamingService _namingService;
    private readonly EventPartDetector _partDetector;
    private readonly ConfigService _configService;

    private static readonly string[] VideoExtensions = { ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".webm", ".flv" };

    public LibraryImportService(
        SportarrDbContext db,
        ILogger<LibraryImportService> logger,
        MediaFileParser fileParser,
        SportsFileNameParser sportsParser,
        FileNamingService namingService,
        EventPartDetector partDetector,
        ConfigService configService)
    {
        _db = db;
        _logger = logger;
        _fileParser = fileParser;
        _sportsParser = sportsParser;
        _namingService = namingService;
        _partDetector = partDetector;
        _configService = configService;
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
            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(folderPath, "*.*", searchOption)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            result.TotalFiles = files.Count;

            foreach (var filePath in files)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var filename = Path.GetFileNameWithoutExtension(filePath);

                    // Try sports-specific parser first for better accuracy
                    var sportsResult = _sportsParser.Parse(filename);
                    var parsedInfo = _fileParser.Parse(filename);

                    // Use sports parser if it has high confidence
                    var eventTitle = sportsResult.Confidence >= 60 && !string.IsNullOrEmpty(sportsResult.EventTitle)
                        ? sportsResult.EventTitle
                        : parsedInfo.EventTitle;

                    var organization = sportsResult.Organization;
                    var sport = sportsResult.Sport;
                    var eventDate = sportsResult.EventDate ?? parsedInfo.AirDate;

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
                            ExistingEventId = linkedEvent?.Id,
                            MatchedEventTitle = linkedEvent?.Title
                        });
                        continue;
                    }

                    // Try to find a matching event using multiple strategies
                    Event? matchedEvent = null;
                    int matchConfidence = 0;

                    if (!string.IsNullOrEmpty(eventTitle))
                    {
                        // Strategy 1: Direct title match
                        var candidates = await _db.Events
                            .Include(e => e.League)
                            .Where(e => !e.HasFile)
                            .ToListAsync();

                        foreach (var candidate in candidates)
                        {
                            var confidence = CalculateMatchConfidence(eventTitle, candidate.Title, organization, candidate, eventDate);
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
                        MatchedEventId = matchedEvent?.Id,
                        MatchedEventTitle = matchedEvent?.Title,
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
                var parsedInfo = _fileParser.Parse(Path.GetFileNameWithoutExtension(request.FilePath));

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

                        // Build destination path and transfer file
                        var destinationPath = await TransferFileToLibraryAsync(
                            request.FilePath,
                            existingEvent,
                            parsedInfo,
                            settings,
                            config);

                        // Update event with new file info
                        existingEvent.FilePath = destinationPath;
                        existingEvent.HasFile = true;
                        existingEvent.FileSize = sourceFileSize;
                        existingEvent.Quality = request.Quality ?? _fileParser.BuildQualityString(parsedInfo);
                        existingEvent.LastUpdate = DateTime.UtcNow;

                        // Use manual part info if provided, otherwise auto-detect
                        string? partName = request.PartName;
                        int? partNumber = request.PartNumber;

                        if (string.IsNullOrEmpty(partName) && config.EnableMultiPartEpisodes)
                        {
                            var partInfo = _partDetector.DetectPart(parsedInfo.EventTitle, existingEvent.Sport);
                            partName = partInfo?.SegmentName;
                            partNumber = partInfo?.PartNumber;
                        }

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
                            // Create new EventFile record
                            var eventFile = new EventFile
                            {
                                EventId = existingEvent.Id,
                                FilePath = destinationPath,
                                Size = sourceFileSize,
                                Quality = request.Quality ?? _fileParser.BuildQualityString(parsedInfo),
                                Codec = parsedInfo.VideoCodec,
                                Source = parsedInfo.Source,
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

                        result.Imported.Add(destinationPath);
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

                    // Build destination path and transfer file
                    var destinationPath = await TransferFileToLibraryAsync(
                        request.FilePath,
                        newEvent,
                        parsedInfo,
                        settings,
                        config);

                    // Update event with file path
                    newEvent.FilePath = destinationPath;
                    newEvent.HasFile = true;

                    // Create EventFile record
                    // Use manual part info if provided, otherwise auto-detect
                    string? partName = request.PartName;
                    int? partNumber = request.PartNumber;

                    if (string.IsNullOrEmpty(partName) && config.EnableMultiPartEpisodes)
                    {
                        var partInfo = _partDetector.DetectPart(parsedInfo.EventTitle, sport);
                        partName = partInfo?.SegmentName;
                        partNumber = partInfo?.PartNumber;
                    }

                    var eventFile = new EventFile
                    {
                        EventId = newEvent.Id,
                        FilePath = destinationPath,
                        Size = sourceFileSize,
                        Quality = request.Quality ?? _fileParser.BuildQualityString(parsedInfo),
                        Codec = parsedInfo.VideoCodec,
                        Source = parsedInfo.Source,
                        PartName = partName,
                        PartNumber = partNumber,
                        Added = DateTime.UtcNow,
                        LastVerified = DateTime.UtcNow,
                        Exists = true
                    };
                    _db.EventFiles.Add(eventFile);

                    result.Created.Add(destinationPath);
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
        Config config)
    {
        var sourceFileInfo = new FileInfo(sourcePath);
        var extension = sourceFileInfo.Extension;

        // Get best root folder
        var rootFolder = await GetBestRootFolderAsync(settings, sourceFileInfo.Length);

        // Build destination path
        var destinationPath = rootFolder;

        // Add event folder if configured
        if (settings.CreateEventFolder)
        {
            var folderName = _namingService.BuildFolderName(settings.EventFolderFormat, eventInfo);
            destinationPath = Path.Combine(destinationPath, folderName);
        }

        // Build filename
        string filename;
        if (settings.RenameFiles)
        {
            // Detect multi-part episode segment
            string partSuffix = string.Empty;
            if (config.EnableMultiPartEpisodes)
            {
                var partInfo = _partDetector.DetectPart(parsed.EventTitle, eventInfo.Sport);
                if (partInfo != null)
                {
                    partSuffix = $" - {partInfo.PartSuffix}";
                    _logger.LogDebug("[Import] Detected multi-part episode: {Segment} ({PartSuffix})",
                        partInfo.SegmentName, partInfo.PartSuffix);
                }
            }

            // Calculate episode number based on date order within the league/season
            var episodeNumber = await CalculateEpisodeNumberAsync(eventInfo);

            // Update the event's episode number if it's different or not set
            if (!eventInfo.EpisodeNumber.HasValue || eventInfo.EpisodeNumber.Value != episodeNumber)
            {
                eventInfo.EpisodeNumber = episodeNumber;
                _logger.LogDebug("[Import] Set episode number to {EpisodeNumber} for event {EventTitle}",
                    episodeNumber, eventInfo.Title);
            }

            var tokens = new FileNamingTokens
            {
                EventTitle = eventInfo.Title,
                EventTitleThe = eventInfo.Title,
                AirDate = eventInfo.EventDate,
                Quality = parsed.Quality ?? "Unknown",
                QualityFull = _fileParser.BuildQualityString(parsed),
                ReleaseGroup = parsed.ReleaseGroup ?? string.Empty,
                OriginalTitle = parsed.EventTitle,
                OriginalFilename = Path.GetFileNameWithoutExtension(sourcePath),
                Series = eventInfo.League?.Name ?? eventInfo.Sport,
                Season = eventInfo.SeasonNumber?.ToString("0000") ?? eventInfo.Season ?? DateTime.UtcNow.Year.ToString(),
                Episode = episodeNumber.ToString("00"),
                Part = partSuffix
            };

            filename = _namingService.BuildFileName(settings.StandardFileFormat, tokens, extension);
        }
        else
        {
            filename = Path.GetFileName(sourcePath);
        }

        destinationPath = Path.Combine(destinationPath, filename);

        // Handle duplicates
        destinationPath = GetUniqueFilePath(destinationPath);

        _logger.LogInformation("[Import] Transferring: {Source} -> {Destination}", sourcePath, destinationPath);

        // Create destination directory
        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
            _logger.LogDebug("Created directory: {Directory}", destDir);
        }

        // Transfer file based on settings
        await TransferFileAsync(sourcePath, destinationPath, settings);

        // Set permissions (Linux/macOS only)
        if (settings.SetPermissions && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetFilePermissions(destinationPath, settings);
        }

        return destinationPath;
    }

    /// <summary>
    /// Get best root folder based on free space
    /// </summary>
    private Task<string> GetBestRootFolderAsync(MediaManagementSettings settings, long fileSize)
    {
        var rootFolders = settings.RootFolders
            .Where(rf => rf.Accessible)
            .OrderByDescending(rf => rf.FreeSpace)
            .ToList();

        if (rootFolders.Count == 0)
        {
            throw new Exception("No accessible root folders configured. Please add a root folder in Settings > Media Management.");
        }

        var fileSizeMB = fileSize / 1024 / 1024;
        var folder = rootFolders.FirstOrDefault(rf => rf.FreeSpace > fileSizeMB + settings.MinimumFreeSpace);

        if (folder == null)
        {
            folder = rootFolders.First();
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
    /// Transfer file (move, copy, or hardlink)
    /// </summary>
    private async Task TransferFileAsync(string source, string destination, MediaManagementSettings settings)
    {
        _logger.LogDebug("[Transfer] Settings: UseHardlinks={UseHardlinks}, CopyFiles={CopyFiles}, IsWindows={IsWindows}",
            settings.UseHardlinks, settings.CopyFiles, RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

        if (settings.UseHardlinks)
        {
            // Try to create hardlink
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
                if (message.Contains("cross-device") ||
                    message.Contains("different file systems") ||
                    message.Contains("invalid cross-device link") ||
                    message.Contains("different volume") ||
                    message.Contains("not on the same disk"))
                {
                    _logger.LogWarning("[Transfer] Hardlink failed (cross-device/volume) - falling back to {Fallback}",
                        settings.CopyFiles ? "copy" : "move");
                }
                else
                {
                    _logger.LogWarning(ex, "[Transfer] Hardlink failed - falling back to {Fallback}",
                        settings.CopyFiles ? "copy" : "move");
                }
                // Fall through to copy or move
            }
        }

        if (settings.CopyFiles)
        {
            await CopyFileAsync(source, destination);
            _logger.LogInformation("[Transfer] File copied: {Source} -> {Destination}", source, destination);
        }
        else
        {
            // Move file
            File.Move(source, destination, overwrite: false);
            _logger.LogInformation("[Transfer] File moved: {Source} -> {Destination}", source, destination);
        }
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

            _db.MediaManagementSettings.Add(settings);
            await _db.SaveChangesAsync();
        }

        // Load root folders from separate RootFolders table
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
    private int CalculateMatchConfidence(string searchTitle, string eventTitle, string? organization, Event evt, DateTime? parsedDate)
    {
        int confidence = 0;

        // Normalize titles
        var normalizedSearch = NormalizeTitle(searchTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);

        // Exact title match = 60 points
        if (normalizedSearch.Equals(normalizedEvent, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 60;
        }
        // Contains match = 40 points
        else if (normalizedEvent.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                 normalizedSearch.Contains(normalizedEvent, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 40;
        }
        // Partial word match
        else
        {
            var searchWords = normalizedSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var eventWords = normalizedEvent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matchingWords = searchWords.Intersect(eventWords, StringComparer.OrdinalIgnoreCase).Count();
            var totalWords = Math.Max(searchWords.Length, eventWords.Length);

            if (matchingWords > 0 && totalWords > 0)
            {
                var matchPercent = (double)matchingWords / totalWords;
                confidence += (int)(30 * matchPercent);
            }
        }

        // Organization matches league = +15 points
        if (!string.IsNullOrEmpty(organization) && evt.League != null)
        {
            if (evt.League.Name.Contains(organization, StringComparison.OrdinalIgnoreCase) ||
                organization.Contains(evt.League.Name, StringComparison.OrdinalIgnoreCase))
            {
                confidence += 15;
            }
        }

        // Date match bonus
        if (parsedDate != null)
        {
            var daysDiff = Math.Abs((evt.EventDate - parsedDate.Value).TotalDays);
            if (daysDiff <= 1) confidence += 15;
            else if (daysDiff <= 3) confidence += 10;
            else if (daysDiff <= 7) confidence += 5;
        }

        // Event is recent (within 30 days) = 5 points
        if (Math.Abs((DateTime.UtcNow - evt.EventDate).TotalDays) <= 30)
        {
            confidence += 5;
        }

        return Math.Min(100, confidence);
    }

    private string NormalizeTitle(string title)
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
    /// Calculate episode number for an event based on its date position within its league/season.
    /// Events are ordered by date, and episode numbers are assigned sequentially (1, 2, 3, ...).
    /// If the event already has an episode number and it's correct, returns that number.
    /// </summary>
    private async Task<int> CalculateEpisodeNumberAsync(Event eventInfo)
    {
        // If no league, default to episode 1
        if (!eventInfo.LeagueId.HasValue)
        {
            _logger.LogDebug("[Episode Number] No league for event {EventTitle}, defaulting to episode 1", eventInfo.Title);
            return 1;
        }

        // Determine the season for this event
        var season = eventInfo.Season ?? eventInfo.SeasonNumber?.ToString() ?? eventInfo.EventDate.Year.ToString();

        // Get all events in this league/season, ordered by date
        var eventsInSeason = await _db.Events
            .Where(e => e.LeagueId == eventInfo.LeagueId &&
                       (e.Season == season ||
                        (e.SeasonNumber.HasValue && e.SeasonNumber.ToString() == season) ||
                        e.EventDate.Year.ToString() == season))
            .OrderBy(e => e.EventDate)
            .ThenBy(e => e.Id) // Secondary sort by ID for events on same date
            .Select(e => new { e.Id, e.EventDate, e.EpisodeNumber })
            .ToListAsync();

        if (eventsInSeason.Count == 0)
        {
            _logger.LogDebug("[Episode Number] No events found in season {Season} for league {LeagueId}, defaulting to episode 1",
                season, eventInfo.LeagueId);
            return 1;
        }

        // Find the position of this event in the date-sorted list
        var position = eventsInSeason.FindIndex(e => e.Id == eventInfo.Id);

        if (position < 0)
        {
            // Event not in list yet (shouldn't happen if called after SaveChanges)
            // Find where it would be inserted based on date
            position = eventsInSeason.Count(e => e.EventDate < eventInfo.EventDate ||
                (e.EventDate == eventInfo.EventDate && e.Id < eventInfo.Id));
        }

        // Episode number is 1-indexed position
        var episodeNumber = position + 1;

        _logger.LogDebug("[Episode Number] Event {EventTitle} is episode {EpisodeNumber} of {TotalEvents} in season {Season}",
            eventInfo.Title, episodeNumber, eventsInSeason.Count, season);

        return episodeNumber;
    }

    /// <summary>
    /// Assign episode numbers to all events in a league/season based on date order.
    /// This can be used to recalculate episode numbers for an entire season.
    /// </summary>
    public async Task<int> AssignEpisodeNumbersForSeasonAsync(int leagueId, string season)
    {
        var events = await _db.Events
            .Where(e => e.LeagueId == leagueId &&
                       (e.Season == season ||
                        (e.SeasonNumber.HasValue && e.SeasonNumber.ToString() == season) ||
                        e.EventDate.Year.ToString() == season))
            .OrderBy(e => e.EventDate)
            .ThenBy(e => e.Id)
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
    public int? MatchedEventId { get; set; }
    public string? MatchedEventTitle { get; set; }
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
