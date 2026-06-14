using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for managing DVR recordings.
/// Handles scheduling, starting, stopping, and managing recordings.
/// </summary>
public class DvrRecordingService
{
    private readonly ILogger<DvrRecordingService> _logger;
    private readonly SportarrDbContext _db;
    private readonly FFmpegRecorderService _ffmpegRecorder;
    private readonly IptvSourceService _iptvService;
    private readonly ConfigService _configService;
    private readonly FileNamingService _namingService;
    private readonly DiskSpaceService _diskSpaceService;

    public DvrRecordingService(
        ILogger<DvrRecordingService> logger,
        SportarrDbContext db,
        FFmpegRecorderService ffmpegRecorder,
        IptvSourceService iptvService,
        ConfigService configService,
        FileNamingService namingService,
        DiskSpaceService diskSpaceService)
    {
        _logger = logger;
        _db = db;
        _ffmpegRecorder = ffmpegRecorder;
        _iptvService = iptvService;
        _configService = configService;
        _namingService = namingService;
        _diskSpaceService = diskSpaceService;
    }

    // ============================================================================
    // Recording CRUD
    // ============================================================================

    /// <summary>
    /// Get all recordings with optional filtering
    /// </summary>
    public async Task<List<DvrRecording>> GetRecordingsAsync(
        DvrRecordingStatus? status = null,
        int? eventId = null,
        int? channelId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? limit = null)
    {
        var query = _db.DvrRecordings
            .Include(r => r.Event)
            .Include(r => r.Channel)
            .ThenInclude(c => c!.Source)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        if (eventId.HasValue)
        {
            query = query.Where(r => r.EventId == eventId.Value);
        }

        if (channelId.HasValue)
        {
            query = query.Where(r => r.ChannelId == channelId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(r => r.ScheduledStart >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(r => r.ScheduledEnd <= toDate.Value);
        }

        query = query.OrderByDescending(r => r.ScheduledStart);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// Get a recording by ID
    /// </summary>
    public async Task<DvrRecording?> GetRecordingByIdAsync(int id)
    {
        return await _db.DvrRecordings
            .Include(r => r.Event)
            .Include(r => r.Channel)
            .ThenInclude(c => c!.Source)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    /// <summary>
    /// Schedule a new recording
    /// </summary>
    public async Task<DvrRecording> ScheduleRecordingAsync(ScheduleDvrRecordingRequest request)
    {
        var channel = await _db.IptvChannels
            .Include(c => c.Source)
            .FirstOrDefaultAsync(c => c.Id == request.ChannelId);

        if (channel == null)
        {
            throw new ArgumentException($"Channel {request.ChannelId} not found");
        }

        // Conflict check: enforce the configured policy if scheduling
        // would push the IPTV source past its MaxStreams cap or the
        // global concurrent-recording cap during the new run's
        // window. Only counts recordings that overlap in time and
        // are still active (Scheduled or Recording).
        //
        // Catchup rows skip this: they consume no tuner during the event
        // window (the archive download happens after airing, and
        // CatchupDownloadService serializes downloads to a single
        // connection), so overlapping live recordings are not in
        // contention with them.
        if (request.Method != DvrRecordingMethod.Catchup)
        {
            await EnforceConflictPolicyAsync(request, channel);
        }

        Event? evt = null;
        if (request.EventId.HasValue)
        {
            evt = await _db.Events.FindAsync(request.EventId.Value);
            if (evt == null)
            {
                throw new ArgumentException($"Event {request.EventId} not found");
            }
        }

        // Generate title if not provided
        var title = request.Title;
        if (string.IsNullOrEmpty(title))
        {
            if (evt != null)
            {
                title = evt.Title;
                if (!string.IsNullOrEmpty(request.PartName))
                {
                    title += $" - {request.PartName}";
                }
            }
            else
            {
                title = $"Recording - {channel.Name} - {request.ScheduledStart:yyyy-MM-dd HH:mm}";
            }
        }

        // Map channel's detected quality to HDTV quality name for scoring
        // Pass channel name as fallback to detect quality from names like "Sky Sports 4K"
        var qualityName = MapChannelQualityToHdtvQuality(channel.DetectedQuality, channel.QualityScore, channel.Name);

        var recording = new DvrRecording
        {
            EventId = request.EventId,
            ChannelId = request.ChannelId,
            Title = title,
            ScheduledStart = request.ScheduledStart,
            ScheduledEnd = request.ScheduledEnd,
            PrePadding = request.PrePadding,
            PostPadding = request.PostPadding,
            PartName = request.PartName,
            Status = DvrRecordingStatus.Scheduled,
            Quality = qualityName, // Set quality based on channel's detected quality
            Method = request.Method,
            Created = DateTime.UtcNow
        };

        _db.DvrRecordings.Add(recording);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[DVR] Scheduled {Method} recording: {Title} on {Channel} from {Start} to {End}",
            recording.Method == DvrRecordingMethod.Catchup ? "catchup" : "live",
            recording.Title, channel.Name, recording.ScheduledStart, recording.ScheduledEnd);

        return recording;
    }

    /// <summary>
    /// Update a scheduled recording
    /// </summary>
    public async Task<DvrRecording?> UpdateRecordingAsync(int id, ScheduleDvrRecordingRequest request)
    {
        var recording = await _db.DvrRecordings.FindAsync(id);
        if (recording == null)
        {
            return null;
        }

        // Can only update scheduled recordings
        if (recording.Status != DvrRecordingStatus.Scheduled)
        {
            throw new InvalidOperationException($"Cannot update recording in status {recording.Status}");
        }

        recording.ChannelId = request.ChannelId;
        recording.ScheduledStart = request.ScheduledStart;
        recording.ScheduledEnd = request.ScheduledEnd;
        recording.PrePadding = request.PrePadding;
        recording.PostPadding = request.PostPadding;
        recording.PartName = request.PartName;
        recording.LastUpdated = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(request.Title))
        {
            recording.Title = request.Title;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("[DVR] Updated recording {Id}: {Title}", id, recording.Title);

        return recording;
    }

    /// <summary>
    /// Cancel a scheduled recording
    /// </summary>
    public async Task<bool> CancelRecordingAsync(int id)
    {
        var recording = await _db.DvrRecordings.FindAsync(id);
        if (recording == null)
        {
            return false;
        }

        if (recording.Status == DvrRecordingStatus.Recording)
        {
            // Stop active recording first
            await StopRecordingAsync(id);
        }

        recording.Status = DvrRecordingStatus.Cancelled;
        recording.LastUpdated = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("[DVR] Cancelled recording {Id}: {Title}", id, recording.Title);

        return true;
    }

    /// <summary>
    /// Delete a recording (and optionally its file)
    /// </summary>
    public async Task<bool> DeleteRecordingAsync(int id, bool deleteFile = false)
    {
        var recording = await _db.DvrRecordings.FindAsync(id);
        if (recording == null)
        {
            return false;
        }

        // Stop if currently recording
        if (recording.Status == DvrRecordingStatus.Recording)
        {
            await StopRecordingAsync(id);
        }

        // Delete file if requested
        if (deleteFile && !string.IsNullOrEmpty(recording.OutputPath) && File.Exists(recording.OutputPath))
        {
            try
            {
                File.Delete(recording.OutputPath);
                _logger.LogInformation("[DVR] Deleted recording file: {Path}", recording.OutputPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DVR] Failed to delete recording file: {Path}", recording.OutputPath);
            }
        }

        _db.DvrRecordings.Remove(recording);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[DVR] Deleted recording {Id}: {Title}", id, recording.Title);

        return true;
    }

    // ============================================================================
    // Recording Control
    // ============================================================================

    /// <summary>
    /// Start a recording immediately
    /// </summary>
    public async Task<RecordingResult> StartRecordingAsync(int recordingId)
    {
        var recording = await _db.DvrRecordings
            .Include(r => r.Channel)
            .ThenInclude(c => c!.Source)
            .Include(r => r.Event)
            .ThenInclude(e => e!.League)
            .FirstOrDefaultAsync(r => r.Id == recordingId);

        if (recording == null)
        {
            return new RecordingResult { Success = false, Error = "Recording not found" };
        }

        if (recording.Status == DvrRecordingStatus.Recording)
        {
            return new RecordingResult { Success = false, Error = "Recording already in progress" };
        }

        if (recording.Channel == null)
        {
            return new RecordingResult { Success = false, Error = "Channel not found" };
        }

        // Generate output path
        var outputPath = await GenerateOutputPathAsync(recording);
        recording.OutputPath = outputPath;

        // Get user agent from source
        var userAgent = recording.Channel.Source?.UserAgent;

        // Start the recording
        var result = await _ffmpegRecorder.StartRecordingAsync(
            recordingId,
            recording.Channel.StreamUrl,
            outputPath,
            userAgent);

        if (result.Success)
        {
            recording.Status = DvrRecordingStatus.Recording;
            recording.ActualStart = DateTime.UtcNow;
            recording.LastUpdated = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("[DVR] Started recording {Id}: {Title} -> {Path}",
                recordingId, recording.Title, outputPath);
        }
        else
        {
            recording.Status = DvrRecordingStatus.Failed;
            recording.ErrorMessage = result.Error;
            recording.LastUpdated = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogError("[DVR] Failed to start recording {Id}: {Error}", recordingId, result.Error);

            // Phase 3 — auto-rotate to fallback channel when start
            // fails. Most "failed to start" cases are channel-specific
            // (offline source, ffmpeg can't open the stream, source
            // tuner saturated) and a different channel airing the same
            // event will succeed.
            await TryRescheduleOnFallbackAsync(recording, "start failed: " + result.Error);
        }

        return result;
    }

    /// <summary>
    /// When a recording fails, rotate to the next channel in
    /// DvrRecording.FallbackChannelIds (Phase 3). Creates a NEW
    /// DvrRecording row carrying the same event + scheduled times +
    /// padding but with the next channel as primary and the remaining
    /// fallbacks as backups. Returns the new recording id, or null
    /// when no fallbacks remain.
    /// </summary>
    public async Task<int?> TryRescheduleOnFallbackAsync(DvrRecording failed, string reason)
    {
        // Cap auto-retries so a broken event (every channel fails)
        // doesn't loop forever. Once we exhaust the fallback list
        // we leave the failed recording as the final state.
        const int MaxAutoRetries = 4;
        if (failed.AutoRetryCount >= MaxAutoRetries)
        {
            _logger.LogWarning("[DVR] Recording {Id} exhausted fallback retries ({Count}); not re-rotating",
                failed.Id, failed.AutoRetryCount);
            return null;
        }

        if (string.IsNullOrWhiteSpace(failed.FallbackChannelIds))
        {
            _logger.LogDebug("[DVR] Recording {Id} failed but no fallback channels stored", failed.Id);
            return null;
        }

        List<int>? backups;
        try
        {
            backups = JsonSerializer.Deserialize<List<int>>(failed.FallbackChannelIds);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[DVR] Recording {Id}: FallbackChannelIds parse failed", failed.Id);
            return null;
        }
        if (backups == null || backups.Count == 0) return null;

        // Pick the next channel that's actually enabled + online. Skip
        // any whose source already has a recording in progress (tuner
        // conflict — same MaxStreams-saturated source would just fail
        // again immediately).
        var sourceIdsAtCapacity = await GetSourceIdsAtCapacityAsync();
        IptvChannel? nextChannel = null;
        var remainingBackups = new List<int>(backups);
        while (remainingBackups.Count > 0)
        {
            var candidateId = remainingBackups[0];
            remainingBackups.RemoveAt(0);
            var candidate = await _db.IptvChannels
                .Include(c => c.Source)
                .FirstOrDefaultAsync(c => c.Id == candidateId);
            if (candidate == null || !candidate.IsEnabled) continue;
            if (candidate.Source == null || !candidate.Source.IsActive) continue;
            if (sourceIdsAtCapacity.Contains(candidate.SourceId)) continue;
            nextChannel = candidate;
            break;
        }

        if (nextChannel == null)
        {
            _logger.LogInformation("[DVR] Recording {Id} ({Title}): no usable fallback channels remain (reason: {Reason})",
                failed.Id, failed.Title, reason);
            return null;
        }

        // Create the rotated recording carrying forward times + padding.
        var rotated = new DvrRecording
        {
            EventId = failed.EventId,
            ChannelId = nextChannel.Id,
            Title = failed.Title,
            ScheduledStart = failed.ScheduledStart,
            ScheduledEnd = failed.ScheduledEnd,
            PrePadding = failed.PrePadding,
            PostPadding = failed.PostPadding,
            Status = DvrRecordingStatus.Scheduled,
            FallbackChannelIds = remainingBackups.Count > 0 ? JsonSerializer.Serialize(remainingBackups) : null,
            AutoRetryCount = failed.AutoRetryCount + 1,
            Created = DateTime.UtcNow,
        };
        _db.DvrRecordings.Add(rotated);
        // Update the failed row's message so the UI surfaces what happened.
        failed.ErrorMessage = $"{failed.ErrorMessage ?? reason} | rotated to channel {nextChannel.Name}";
        failed.LastUpdated = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("[DVR] Recording {Id} failed; rotated to fallback channel '{Channel}' as recording {NewId} (retry {Count}/{Max})",
            failed.Id, nextChannel.Name, rotated.Id, rotated.AutoRetryCount, MaxAutoRetries);

        // If the failed recording was scheduled to start now-ish,
        // attempt to start the rotated one immediately. Otherwise it
        // will be picked up by the scheduler's normal start loop.
        if (rotated.ScheduledStart <= DateTime.UtcNow.AddMinutes(2))
        {
            try
            {
                await StartRecordingAsync(rotated.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DVR] Failed to immediately start rotated recording {Id}", rotated.Id);
            }
        }

        return rotated.Id;
    }

    /// <summary>
    /// Return the set of IPTV source ids that already have at least
    /// MaxStreams active recordings — picking another channel from
    /// these sources would deadlock at the tuner level. Phase 3
    /// tuner-conflict awareness for both initial scheduling and
    /// auto-fallback rotation.
    /// </summary>
    private async Task<HashSet<int>> GetSourceIdsAtCapacityAsync()
    {
        var active = await _db.DvrRecordings
            .Include(r => r.Channel)
            .ThenInclude(c => c!.Source)
            .Where(r => r.Status == DvrRecordingStatus.Recording)
            .Where(r => r.Channel != null && r.Channel.Source != null)
            .Select(r => new { SourceId = r.Channel!.SourceId, MaxStreams = r.Channel.Source!.MaxStreams })
            .ToListAsync();

        return active
            .GroupBy(a => a.SourceId)
            .Where(g => g.Count() >= g.First().MaxStreams && g.First().MaxStreams > 0)
            .Select(g => g.Key)
            .ToHashSet();
    }

    /// <summary>
    /// Stop an active recording
    /// </summary>
    public async Task<RecordingResult> StopRecordingAsync(int recordingId)
    {
        var recording = await _db.DvrRecordings.FindAsync(recordingId);
        if (recording == null)
        {
            return new RecordingResult { Success = false, Error = "Recording not found" };
        }

        var result = await _ffmpegRecorder.StopRecordingAsync(recordingId);

        recording.ActualEnd = DateTime.UtcNow;
        recording.LastUpdated = DateTime.UtcNow;

        if (result.Success)
        {
            recording.Status = DvrRecordingStatus.Completed;
            recording.FileSize = result.FileSize;
            recording.DurationSeconds = result.DurationSeconds;

            // Calculate average bitrate
            if (result.FileSize.HasValue && result.DurationSeconds.HasValue && result.DurationSeconds > 0)
            {
                recording.AverageBitrate = (result.FileSize.Value * 8) / result.DurationSeconds.Value;
            }

            _logger.LogInformation("[DVR] Completed recording {Id}: {Title}, Duration: {Duration}s, Size: {Size}",
                recordingId, recording.Title, result.DurationSeconds, result.FileSize);
        }
        else
        {
            recording.Status = DvrRecordingStatus.Failed;
            recording.ErrorMessage = result.Error;

            _logger.LogError("[DVR] Recording {Id} failed to stop properly: {Error}", recordingId, result.Error);
        }

        await _db.SaveChangesAsync();

        // Phase 3 — if the recording errored partway through, also
        // try to rotate to a fallback channel so a brief blip doesn't
        // leave the viewer with a half-complete file. Skip when the
        // recording completed cleanly (the common case).
        if (recording.Status == DvrRecordingStatus.Failed)
        {
            await TryRescheduleOnFallbackAsync(recording, "stop failed: " + result.Error);
        }

        return result;
    }

    /// <summary>
    /// Get live status of an active recording
    /// </summary>
    public RecordingStatus? GetRecordingStatus(int recordingId)
    {
        return _ffmpegRecorder.GetRecordingStatus(recordingId);
    }

    /// <summary>
    /// Get all active recordings
    /// </summary>
    public List<RecordingStatus> GetActiveRecordings()
    {
        return _ffmpegRecorder.GetAllActiveRecordings();
    }

    // ============================================================================
    // Scheduling Helpers
    // ============================================================================

    /// <summary>
    /// Get recordings that should start soon (for scheduler)
    /// </summary>
    public async Task<List<DvrRecording>> GetUpcomingRecordingsAsync(int minutesAhead = 5)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(minutesAhead);

        // Recover recordings whose effective start is anywhere from
        // ScheduledEnd ago up to `cutoff` in the future. A scheduler
        // that filtered too aggressively here (the previous version
        // refused anything more than 1 minute past) silently dropped
        // every recording that was due during app downtime - they
        // sat in Scheduled forever and the user never saw them
        // start. Now we still pick them up if the recording window
        // hasn't fully closed: ScheduledEnd + PostPadding hasn't
        // passed yet.
        return await _db.DvrRecordings
            .Include(r => r.Channel)
            .ThenInclude(c => c!.Source)
            .Where(r => r.Status == DvrRecordingStatus.Scheduled)
            // Catchup rows never start a live capture - they wait for
            // CatchupDownloadService to pull the archive after the event.
            .Where(r => r.Method == DvrRecordingMethod.Live)
            .Where(r => r.ScheduledStart.AddMinutes(-r.PrePadding) <= cutoff)
            .Where(r => r.ScheduledEnd.AddMinutes(r.PostPadding) > now)
            .OrderBy(r => r.ScheduledStart)
            .ToListAsync();
    }

    /// <summary>
    /// Get recordings that should stop (past their scheduled end + post-padding)
    /// </summary>
    public async Task<List<DvrRecording>> GetRecordingsToStopAsync()
    {
        var now = DateTime.UtcNow;

        return await _db.DvrRecordings
            .Where(r => r.Status == DvrRecordingStatus.Recording)
            // Catchup downloads are in Recording state while pulling from
            // the archive, with a window that's in the past by design -
            // the wall-clock stop rule only applies to live captures.
            .Where(r => r.Method == DvrRecordingMethod.Live)
            .Where(r => r.ScheduledEnd.AddMinutes(r.PostPadding) <= now)
            .ToListAsync();
    }

    /// <summary>
    /// Schedule recordings for an event based on channel-league mappings
    /// </summary>
    public async Task<List<DvrRecording>> ScheduleRecordingsForEventAsync(int eventId)
    {
        var evt = await _db.Events
            .Include(e => e.League)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null || evt.League == null)
        {
            throw new ArgumentException($"Event {eventId} not found or has no league");
        }

        // Find channels mapped to this event's league
        var channel = await _iptvService.GetPreferredChannelForLeagueAsync(evt.League.Id);

        if (channel == null)
        {
            _logger.LogWarning("[DVR] No channel mapped to league {League} for event {Event}",
                evt.League.Name, evt.Title);
            return new List<DvrRecording>();
        }

        var recordings = new List<DvrRecording>();

        // Check if recording already exists for this event
        var existingRecording = await _db.DvrRecordings
            .FirstOrDefaultAsync(r => r.EventId == eventId && r.Status != DvrRecordingStatus.Cancelled);

        if (existingRecording != null)
        {
            _logger.LogDebug("[DVR] Recording already exists for event {EventId}", eventId);
            return recordings;
        }

        // Create recording
        var recording = await ScheduleRecordingAsync(new ScheduleDvrRecordingRequest
        {
            EventId = eventId,
            ChannelId = channel.Id,
            ScheduledStart = evt.EventDate,
            ScheduledEnd = evt.EventDate.AddHours(3), // Default 3 hour duration
            PrePadding = 5,
            PostPadding = 30  // Extra padding for sports events
        });

        recordings.Add(recording);

        return recordings;
    }

    // ============================================================================
    // Helpers
    // ============================================================================

    /// <summary>
    /// Generate output path for DVR recording using the same folder structure as regular imports.
    /// Uses MediaManagementSettings and FileNamingService for consistency with indexer downloads.
    /// Public so CatchupDownloadService produces identical event-aware paths for archive downloads.
    /// </summary>
    public async Task<string> GenerateOutputPathAsync(DvrRecording recording)
    {
        var config = await _configService.GetConfigAsync();
        var settings = await GetMediaManagementSettingsAsync();

        // Get root folder (same logic as FileImportService)
        var rootFolder = settings.RootFolders
            .Where(rf => rf.Accessible)
            .OrderByDescending(rf => rf.FreeSpace)
            .FirstOrDefault();

        var basePath = rootFolder?.Path ?? Path.Combine(AppContext.BaseDirectory, "recordings");

        // Get container format
        var container = config.DvrContainer ?? "mp4";
        container = container.TrimStart('.').ToLowerInvariant();

        // Build destination path using same logic as FileImportService
        var destinationPath = basePath;

        if (recording.Event != null)
        {
            // Linked to an event - use same folder structure as regular imports
            var eventInfo = recording.Event;

            // IMPORTANT: Calculate episode number BEFORE building folder path
            // This ensures the {Episode} token in EventFolderFormat has the correct value
            var episodeNumber = await CalculateEpisodeNumberAsync(eventInfo);

            // Update event's episode number if needed
            if (!eventInfo.EpisodeNumber.HasValue || eventInfo.EpisodeNumber.Value != episodeNumber)
            {
                eventInfo.EpisodeNumber = episodeNumber;
            }

            // Build folder path using granular folder settings (league/season/event folders)
            // Now uses the correct episode number
            var folderPath = _namingService.BuildFolderPath(settings, eventInfo);
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                destinationPath = Path.Combine(destinationPath, folderPath);
            }

            // Build filename using FileNamingService with same tokens as regular imports
            // Note: Use RenameEvents setting (same as FileRenameService) so user has single setting to control renaming
            // RenameFiles was a separate setting that caused confusion - imports should respect RenameEvents
            if (settings.RenameEvents)
            {
                var partSuffix = !string.IsNullOrEmpty(recording.PartName)
                    ? $" - {recording.PartName}"
                    : "";

                // Use the broadcaster-branding date for filename tokens —
                // see FileRenameService for the UTC-rollover rationale.
                var brandingDate = eventInfo.BroadcastDate ?? eventInfo.EventDate.Date;

                var tokens = new FileNamingTokens
                {
                    EventTitle = eventInfo.Title,
                    EventTitleThe = eventInfo.Title,
                    AirDate = brandingDate,
                    Quality = recording.Quality ?? "HDTV-1080p",
                    QualityFull = $"{recording.Quality ?? "HDTV-1080p"}.DVR",
                    ReleaseGroup = "DVR",
                    OriginalTitle = recording.Title,
                    OriginalFilename = recording.Title,
                    Series = eventInfo.League?.Name ?? eventInfo.Sport,
                    Season = eventInfo.SeasonNumber?.ToString("0000") ?? eventInfo.Season ?? brandingDate.Year.ToString(),
                    Episode = episodeNumber.ToString("00"),
                    Part = partSuffix
                };

                var filename = _namingService.BuildFileName(settings.StandardFileFormat, tokens, $".{container}");
                destinationPath = Path.Combine(destinationPath, filename);
            }
            else
            {
                // No renaming - use event title with timestamp
                var timestamp = recording.ScheduledStart.ToString("yyyy-MM-dd_HHmm");
                var partSuffix = !string.IsNullOrEmpty(recording.PartName)
                    ? $" - {SanitizeFileName(recording.PartName)}"
                    : "";
                var filename = $"{SanitizeFileName(eventInfo.Title)}{partSuffix} [{timestamp}].{container}";
                destinationPath = Path.Combine(destinationPath, filename);
            }
        }
        else
        {
            // Manual recording (no linked event) - use DVR subfolder for organization
            var folderPath = Path.Combine(basePath, "DVR", "Manual");
            var timestamp = recording.ScheduledStart.ToString("yyyy-MM-dd_HHmm");
            var partSuffix = !string.IsNullOrEmpty(recording.PartName)
                ? $" - {SanitizeFileName(recording.PartName)}"
                : "";
            var filename = $"{SanitizeFileName(recording.Title)}{partSuffix} [{timestamp}].{container}";
            destinationPath = Path.Combine(folderPath, filename);
        }

        // Ensure directory exists
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogDebug("[DVR] Created directory: {Directory}", directory);
        }

        return destinationPath;
    }

    /// <summary>
    /// Get media management settings (same as FileImportService)
    /// </summary>
    private async Task<MediaManagementSettings> GetMediaManagementSettingsAsync()
    {
        var settings = await _db.MediaManagementSettings.FirstOrDefaultAsync();

        if (settings == null)
        {
            // Create default settings with granular folder options
            settings = new MediaManagementSettings
            {
                RootFolders = new List<RootFolder>(),
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
        }

        // Load root folders from separate table
        var rootFolders = await _db.RootFolders.ToListAsync();
        if (rootFolders.Any())
        {
            _diskSpaceService.RefreshLiveState(rootFolders);
            settings.RootFolders = rootFolders;
        }

        return settings;
    }

    /// <summary>
    /// Calculate episode number for an event (same logic as FileImportService)
    /// </summary>
    private async Task<int> CalculateEpisodeNumberAsync(Event eventInfo)
    {
        if (!eventInfo.LeagueId.HasValue)
            return 1;

        var season = eventInfo.Season ?? eventInfo.SeasonNumber?.ToString() ?? (eventInfo.BroadcastDate ?? eventInfo.EventDate).Year.ToString();

        var eventsInSeason = await _db.Events
            .Where(e => e.LeagueId == eventInfo.LeagueId &&
                       (e.Season == season ||
                        (e.SeasonNumber.HasValue && e.SeasonNumber.ToString() == season) ||
                        (e.BroadcastDate.HasValue ? e.BroadcastDate.Value.Year.ToString() == season : e.EventDate.Year.ToString() == season)))
            .OrderBy(e => e.EventDate)
            .ThenBy(e => e.ExternalId)
            .Select(e => new { e.Id, e.EventDate, e.ExternalId })
            .ToListAsync();

        if (eventsInSeason.Count == 0)
            return 1;

        var position = eventsInSeason.FindIndex(e => e.Id == eventInfo.Id);
        if (position < 0)
        {
            position = eventsInSeason.Count(e => e.EventDate < eventInfo.EventDate ||
                (e.EventDate == eventInfo.EventDate && string.Compare(e.ExternalId, eventInfo.ExternalId, StringComparison.Ordinal) < 0));
        }

        return position + 1;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    /// <summary>
    /// Map channel's detected quality to HDTV quality name for scoring
    /// Uses QualityScore, DetectedQuality, and channel name for best accuracy
    /// </summary>
    private static string MapChannelQualityToHdtvQuality(string? detectedQuality, int qualityScore, string? channelName = null)
    {
        // First try to map by quality score (most reliable if available)
        if (qualityScore >= 400)
            return "HDTV-2160p";  // 4K/UHD
        if (qualityScore >= 300)
            return "HDTV-1080p";  // FHD
        if (qualityScore >= 200)
            return "HDTV-720p";   // HD
        if (qualityScore >= 100)
            return "SDTV";        // SD

        // Try detected quality string
        if (!string.IsNullOrEmpty(detectedQuality))
        {
            var quality = detectedQuality.ToUpperInvariant();
            if (quality.Contains("4K") || quality.Contains("UHD") || quality.Contains("2160"))
                return "HDTV-2160p";
            if (quality.Contains("FHD") || quality.Contains("1080"))
                return "HDTV-1080p";
            if (quality.Contains("HD") || quality.Contains("720"))
                return "HDTV-720p";
            if (quality.Contains("SD") || quality.Contains("480") || quality.Contains("576"))
                return "SDTV";
        }

        // Fall back to channel name - many channels include quality in their name
        // e.g., "Sky Sports 4K", "ESPN HD", "BBC One FHD"
        if (!string.IsNullOrEmpty(channelName))
        {
            var name = channelName.ToUpperInvariant();
            // Check for 4K/UHD first (most specific)
            if (name.Contains("4K") || name.Contains("UHD") || name.Contains("2160"))
                return "HDTV-2160p";
            // Check for FHD (before HD to avoid false matches)
            if (name.Contains("FHD") || name.Contains("1080"))
                return "HDTV-1080p";
            // Check for HD/720p
            if (name.Contains(" HD") || name.Contains("-HD") || name.Contains("720"))
                return "HDTV-720p";
            // Check for SD
            if (name.Contains(" SD") || name.Contains("-SD") || name.Contains("480") || name.Contains("576"))
                return "SDTV";
        }

        // Default to 1080p if quality cannot be determined
        return "HDTV-1080p";
    }

    /// <summary>
    /// Check if FFmpeg is available
    /// </summary>
    public async Task<(bool Available, string? Version, string? Path)> CheckFFmpegAsync()
    {
        return await _ffmpegRecorder.CheckFFmpegAvailableAsync();
    }

    /// <summary>
    /// Apply DvrConflictPolicy when scheduling a new recording would
    /// otherwise push an IPTV source past MaxStreams or the global
    /// DvrMaxConcurrentRecordings cap during the requested window.
    ///
    /// Three policies:
    ///   - Refuse: throw InvalidOperationException with a clear
    ///     message; the API surfaces it as 409. Default and safest.
    ///   - Queue: silently allow the schedule; the recorder will
    ///     start it when a slot frees. Existing scheduler/watchdog
    ///     handle the late start.
    ///   - Preempt: cancel the lowest-priority overlapping
    ///     recording on the conflicting source to make room. Never
    ///     preempts a recording that has already started (Status =
    ///     Recording) - only Scheduled rows are eligible victims.
    /// </summary>
    private async Task EnforceConflictPolicyAsync(ScheduleDvrRecordingRequest request, IptvChannel channel)
    {
        var config = await _configService.GetConfigAsync();
        var policy = (config.DvrConflictPolicy ?? "Refuse").Trim();

        var windowStart = request.ScheduledStart.AddMinutes(-request.PrePadding);
        var windowEnd = request.ScheduledEnd.AddMinutes(request.PostPadding);

        // Only Scheduled and Recording rows compete for a slot.
        var overlapping = await _db.DvrRecordings
            .Include(r => r.Channel).ThenInclude(c => c!.Source)
            .Where(r => r.Status == DvrRecordingStatus.Scheduled || r.Status == DvrRecordingStatus.Recording)
            .Where(r => r.ScheduledStart.AddMinutes(-r.PrePadding) < windowEnd
                     && r.ScheduledEnd.AddMinutes(r.PostPadding) > windowStart)
            .ToListAsync();

        // Per-source MaxStreams check.
        var sameSourceCount = channel.SourceId == 0
            ? 0
            : overlapping.Count(r => r.Channel?.SourceId == channel.SourceId);
        var sourceCap = channel.Source?.MaxStreams ?? 0;
        var sourceConflict = sourceCap > 0 && sameSourceCount >= sourceCap;

        // Global concurrent-recording check.
        var globalCap = config.DvrMaxConcurrentRecordings;
        var globalConflict = globalCap > 0 && overlapping.Count >= globalCap;

        if (!sourceConflict && !globalConflict) return;

        var reason = sourceConflict
            ? $"IPTV source '{channel.Source?.Name ?? "(unknown)"}' is at its MaxStreams cap of {sourceCap} during this window"
            : $"Global DvrMaxConcurrentRecordings cap of {globalCap} would be exceeded during this window";

        if (string.Equals(policy, "Queue", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "[DVR] Conflict-policy=Queue: {Reason}. Recording will be queued and start late once a slot frees.",
                reason);
            return; // proceed with insert; scheduler picks it up when a slot opens.
        }

        if (string.Equals(policy, "Preempt", StringComparison.OrdinalIgnoreCase))
        {
            // Pick the lowest-priority overlapping victim. Currently
            // priority is implicit (we don't have a priority column
            // yet) so use Created-ascending: cancel the oldest
            // Scheduled row that doesn't itself belong to a higher-
            // value event. Recordings already in Status=Recording
            // are off-limits.
            var victim = overlapping
                .Where(r => r.Status == DvrRecordingStatus.Scheduled)
                .Where(r => sourceConflict ? r.Channel?.SourceId == channel.SourceId : true)
                .OrderBy(r => r.Created)
                .FirstOrDefault();

            if (victim != null)
            {
                _logger.LogWarning(
                    "[DVR] Conflict-policy=Preempt: cancelling recording {VictimId} ('{Title}') to make room. {Reason}",
                    victim.Id, victim.Title, reason);
                victim.Status = DvrRecordingStatus.Cancelled;
                victim.ErrorMessage = (victim.ErrorMessage ?? "") +
                    $"Preempted by a higher-priority schedule at {DateTime.UtcNow:o}.";
                await _db.SaveChangesAsync();
                return;
            }
            // Nothing to preempt - fall through to refuse.
            _logger.LogWarning(
                "[DVR] Conflict-policy=Preempt: no eligible victim to cancel. {Reason}. Falling back to Refuse.",
                reason);
        }

        // Default: Refuse.
        throw new InvalidOperationException(
            $"Cannot schedule recording: {reason}. " +
            $"Either cancel a conflicting recording, raise the cap, or change DvrConflictPolicy to Queue or Preempt.");
    }
}
