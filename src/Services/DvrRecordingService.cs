using System.Collections.Concurrent;
using System.Diagnostics;
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
    /// <summary>
    /// Error returned when a start is refused because another start for
    /// the same recording is still in flight. The scheduler treats this
    /// as benign (its tick raced a manual start), so it must stay a
    /// stable, comparable value.
    /// </summary>
    public const string StartAlreadyInProgressError = "Recording start already in progress";

    // The service is scoped, so the in-flight set must be static to be
    // shared across the manual-start endpoint's scope and the
    // scheduler's scope. FFmpeg takes several seconds to spawn and
    // probe before the row flips to Recording; without this gate both
    // callers pass the status check and spawn duplicate processes
    // writing the same output file.
    private static readonly ConcurrentDictionary<int, byte> _startsInFlight = new();

    // Overtime guard state. Total minutes each recording has been extended
    // past its original end (caps runaway extension), and a short-lived
    // per-league livescore cache so a scheduler tick with several
    // recordings ending doesn't hammer the hub. Static: the service is
    // scoped, the scheduler ticks in fresh scopes. Reset on app restart -
    // worst case a restart mid-overtime grants a fresh extension budget.
    private static readonly ConcurrentDictionary<int, int> _overtimeExtensions = new();
    private static readonly ConcurrentDictionary<string, (DateTime FetchedAt, List<Event> Events)> _livescoreCache = new();
    private const int OvertimeStepMinutes = 10;

    private readonly ILogger<DvrRecordingService> _logger;
    private readonly SportarrDbContext _db;
    private readonly FFmpegRecorderService _ffmpegRecorder;
    private readonly IptvSourceService _iptvService;
    private readonly ConfigService _configService;
    private readonly FileNamingService _namingService;
    private readonly DiskSpaceService _diskSpaceService;
    private readonly NotificationService _notificationService;
    private readonly SportarrApiClient _sportarrApiClient;

    public DvrRecordingService(
        ILogger<DvrRecordingService> logger,
        SportarrDbContext db,
        FFmpegRecorderService ffmpegRecorder,
        IptvSourceService iptvService,
        ConfigService configService,
        FileNamingService namingService,
        DiskSpaceService diskSpaceService,
        NotificationService notificationService,
        SportarrApiClient sportarrApiClient)
    {
        _logger = logger;
        _db = db;
        _ffmpegRecorder = ffmpegRecorder;
        _iptvService = iptvService;
        _configService = configService;
        _namingService = namingService;
        _diskSpaceService = diskSpaceService;
        _notificationService = notificationService;
        _sportarrApiClient = sportarrApiClient;
    }

    /// <summary>
    /// Send a recording lifecycle notification. Failures are logged and
    /// swallowed - a broken Discord webhook must never break a recording.
    /// </summary>
    private async Task NotifyRecordingAsync(NotificationTrigger trigger, string title, string message, DvrRecording recording)
    {
        try
        {
            await _notificationService.SendNotificationAsync(trigger, title, message,
                new Dictionary<string, object>
                {
                    { "recordingId", recording.Id },
                    { "recordingTitle", recording.Title },
                    { "channelId", recording.ChannelId },
                    { "eventId", recording.EventId ?? 0 },
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DVR] Failed to send {Trigger} notification for recording {Id}", trigger, recording.Id);
        }
    }

    /// <summary>
    /// Remove a failed recording's output file when it holds no usable
    /// content (under 1 MB). Larger partials are kept deliberately - half
    /// a match is still watchable and the user can decide what to do with
    /// it. Shared with the watchdog's failure paths.
    /// </summary>
    public void CleanupWorthlessPartial(DvrRecording recording)
    {
        try
        {
            if (string.IsNullOrEmpty(recording.OutputPath) || !File.Exists(recording.OutputPath))
                return;

            var info = new FileInfo(recording.OutputPath);
            if (info.Length < 1024 * 1024)
            {
                File.Delete(recording.OutputPath);
                _logger.LogInformation("[DVR] Removed worthless partial file ({Bytes} bytes) for failed recording {Id}",
                    info.Length, recording.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[DVR] Partial-file cleanup failed for recording {Id}", recording.Id);
        }
    }

    /// <summary>
    /// Failure notification shared with the watchdog, which detects dead
    /// and stalled recorders on its own tick.
    /// </summary>
    public Task NotifyRecordingFailedAsync(DvrRecording recording, string reason, int? rotatedToRecordingId = null)
        => NotifyRecordingAsync(
            NotificationTrigger.OnRecordingFailed,
            $"Recording failed: {recording.Title}",
            reason + (rotatedToRecordingId.HasValue
                ? " Retrying on a fallback channel."
                : ""),
            recording);

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
        // All scheduling math compares against DateTime.UtcNow, so pin
        // the incoming times to UTC. JSON datetimes without an offset
        // deserialize as Kind=Unspecified (meant as UTC by the frontend);
        // offset-carrying values arrive Kind=Local and need converting.
        request.ScheduledStart = NormalizeToUtc(request.ScheduledStart);
        request.ScheduledEnd = NormalizeToUtc(request.ScheduledEnd);

        if (request.ScheduledEnd <= request.ScheduledStart)
        {
            throw new ArgumentException("Scheduled end must be after scheduled start.");
        }

        // A live recording whose whole window already passed can never
        // capture anything - without this check the row is accepted,
        // sits in Scheduled, and the watchdog flags it "missed" minutes
        // later with a misleading downtime/slot explanation. Catchup is
        // exempt: a closed window is its normal trigger condition.
        if (request.Method != DvrRecordingMethod.Catchup &&
            request.ScheduledEnd.AddMinutes(request.PostPadding) <= DateTime.UtcNow)
        {
            throw new ArgumentException(
                $"The scheduled window is already in the past (ends {request.ScheduledEnd:yyyy-MM-dd HH:mm} UTC). Nothing would be recorded - check the start and end times.");
        }

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

    private static DateTime NormalizeToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

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
        if (!_startsInFlight.TryAdd(recordingId, 0))
        {
            _logger.LogDebug("[DVR] Start of recording {Id} skipped: another start is already in flight", recordingId);
            return new RecordingResult { Success = false, Error = StartAlreadyInProgressError };
        }

        try
        {
            return await StartRecordingCoreAsync(recordingId);
        }
        finally
        {
            _startsInFlight.TryRemove(recordingId, out _);
        }
    }

    private async Task<RecordingResult> StartRecordingCoreAsync(int recordingId)
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

        // Disk-space gate. GenerateOutputPathAsync already picked the
        // ROOMIEST accessible root, so if even that sits below the floor,
        // every root is full - failing now beats recording until the disk
        // jams and the watchdog reports a confusing "output stalled".
        // Deliberately no fallback rotation: another channel writes to the
        // same disk. Honors the same MinimumFreeSpace / SkipFreeSpaceCheck
        // settings imports use; an unknown free-space reading proceeds.
        var startConfig = await _configService.GetConfigAsync();
        if (!startConfig.SkipFreeSpaceCheck)
        {
            var availableBytes = _diskSpaceService.GetAvailableSpace(Path.GetDirectoryName(outputPath) ?? outputPath);
            var minFreeBytes = (long)startConfig.MinimumFreeSpace * 1024 * 1024;
            if (availableBytes.HasValue && availableBytes.Value < minFreeBytes)
            {
                var mbFree = availableBytes.Value / 1024 / 1024;
                recording.Status = DvrRecordingStatus.Failed;
                recording.ErrorMessage = $"Not enough disk space to record ({mbFree} MB free, {startConfig.MinimumFreeSpace} MB floor)";
                recording.LastUpdated = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                _logger.LogError("[DVR] Refusing to start recording {Id}: {Free} MB free is below the {Min} MB floor",
                    recordingId, mbFree, startConfig.MinimumFreeSpace);
                await NotifyRecordingFailedAsync(recording, $"Not enough disk space ({mbFree} MB free, {startConfig.MinimumFreeSpace} MB floor).");

                return new RecordingResult { Success = false, Error = recording.ErrorMessage };
            }
        }

        // Get per-source stream options
        var userAgent = recording.Channel.Source?.UserAgent;
        var extraInputArgs = recording.Channel.Source?.FfmpegInputArgs;

        // Start the recording
        var result = await _ffmpegRecorder.StartRecordingAsync(
            recordingId,
            recording.Channel.StreamUrl,
            outputPath,
            userAgent,
            extraInputArgs);

        if (result.Success)
        {
            recording.Status = DvrRecordingStatus.Recording;
            recording.ActualStart = DateTime.UtcNow;
            recording.LastUpdated = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("[DVR] Started recording {Id}: {Title} -> {Path}",
                recordingId, recording.Title, outputPath);

            await NotifyRecordingAsync(NotificationTrigger.OnRecordingStarted,
                $"Recording started: {recording.Title}",
                $"Channel: {recording.Channel.Name}\nWindow: {recording.ScheduledStart:yyyy-MM-dd HH:mm} - {recording.ScheduledEnd:HH:mm} UTC",
                recording);
        }
        else
        {
            recording.Status = DvrRecordingStatus.Failed;
            recording.ErrorMessage = result.Error;
            recording.LastUpdated = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogError("[DVR] Failed to start recording {Id}: {Error}", recordingId, result.Error);
            CleanupWorthlessPartial(recording);

            // Phase 3 — auto-rotate to fallback channel when start
            // fails. Most "failed to start" cases are channel-specific
            // (offline source, ffmpeg can't open the stream, source
            // tuner saturated) and a different channel airing the same
            // event will succeed.
            var rotatedId = await TryRescheduleOnFallbackAsync(recording, "start failed: " + result.Error);

            await NotifyRecordingFailedAsync(recording,
                $"Could not start on channel {recording.Channel.Name}: {result.Error}",
                rotatedId);
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
    /// Called by the recorder's monitor task when an ffmpeg process
    /// exited without a stop request (stream death, provider drop,
    /// crash). Fails the row and rotates to a fallback channel while
    /// the scheduled window is still open; when the exit landed at the
    /// natural end of the window with data on disk, finalizes it as
    /// Completed instead so a stream that ends exactly on time isn't
    /// reported as a failure.
    /// </summary>
    public async Task HandleRecorderExitAsync(int recordingId, int exitCode, string? errorSummary)
    {
        var recording = await _db.DvrRecordings
            .Include(r => r.Channel)
            .ThenInclude(c => c!.Source)
            .FirstOrDefaultAsync(r => r.Id == recordingId);

        // The stop path or the watchdog may have finalized the row
        // between process exit and this callback; never overwrite a
        // terminal state.
        if (recording == null || recording.Status != DvrRecordingStatus.Recording)
            return;

        var now = DateTime.UtcNow;
        var windowEnd = recording.ScheduledEnd.AddMinutes(recording.PostPadding);
        _overtimeExtensions.TryRemove(recordingId, out _);

        long fileSize = 0;
        if (!string.IsNullOrEmpty(recording.OutputPath) && File.Exists(recording.OutputPath))
        {
            try { fileSize = new FileInfo(recording.OutputPath).Length; }
            catch (IOException) { /* transient - treated as no data */ }
        }

        recording.ActualEnd = now;
        recording.LastUpdated = now;

        // Exited within 30s of the natural end with data on disk:
        // the stream simply ended on time, so this is a completed run.
        if (fileSize > 0 && now >= windowEnd.AddSeconds(-30))
        {
            recording.Status = DvrRecordingStatus.Completed;
            recording.FileSize = fileSize;
            var started = recording.ActualStart ?? recording.ScheduledStart;
            var duration = (int)Math.Max(1, (now - started).TotalSeconds);
            recording.DurationSeconds = duration;
            recording.AverageBitrate = (fileSize * 8) / duration;
            await _db.SaveChangesAsync();

            _logger.LogInformation("[DVR] Recording {Id}: recorder exited at the end of its window; finalized as completed ({Size} bytes)",
                recordingId, fileSize);
            FirePostRecordingCommand(recording);

            await NotifyRecordingAsync(NotificationTrigger.OnRecordingCompleted,
                $"Recording completed: {recording.Title}",
                $"Duration: {duration / 60}m\nSize: {fileSize / 1024.0 / 1024.0:F0} MB",
                recording);
            return;
        }

        recording.Status = DvrRecordingStatus.Failed;
        recording.ErrorMessage = $"Recorder exited unexpectedly (code {exitCode})" +
            (string.IsNullOrEmpty(errorSummary) ? "" : $": {errorSummary}");
        await _db.SaveChangesAsync();

        _logger.LogWarning("[DVR] Recording {Id}: recorder exited mid-window (code {Code}); marked Failed", recordingId, exitCode);
        CleanupWorthlessPartial(recording);

        int? rotatedId = null;
        if (now < windowEnd)
        {
            rotatedId = await TryRescheduleOnFallbackAsync(recording, "recorder exited mid-window");
        }

        await NotifyRecordingFailedAsync(recording,
            $"The stream died mid-recording ({recording.ErrorMessage}).",
            rotatedId);
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

        _overtimeExtensions.TryRemove(recordingId, out _);

        // The watchdog or the recorder's exit callback may have finalized
        // (and possibly fallback-rotated) this row while we waited for the
        // process to stop - both re-check before writing and so must we.
        // Overwriting a terminal state here re-marked watchdog-failed rows
        // and rotated them a second time, creating duplicate fallback
        // recordings for the same event.
        await _db.Entry(recording).ReloadAsync();
        if (recording.Status is DvrRecordingStatus.Completed
            or DvrRecordingStatus.Failed
            or DvrRecordingStatus.Cancelled
            or DvrRecordingStatus.Importing
            or DvrRecordingStatus.Imported)
        {
            _logger.LogDebug("[DVR] Recording {Id} was already finalized as {Status} while stopping; not overwriting",
                recordingId, recording.Status);
            return recording.Status == DvrRecordingStatus.Completed
                ? new RecordingResult { Success = true, FileSize = recording.FileSize, DurationSeconds = recording.DurationSeconds }
                : new RecordingResult { Success = false, Error = recording.ErrorMessage ?? "Recording was finalized as failed while stopping" };
        }

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
            var rotatedId = await TryRescheduleOnFallbackAsync(recording, "stop failed: " + result.Error);
            await NotifyRecordingFailedAsync(recording, $"Recording did not stop cleanly: {result.Error}", rotatedId);
        }
        else if (recording.Status == DvrRecordingStatus.Completed)
        {
            FirePostRecordingCommand(recording);

            var sizeMb = (result.FileSize ?? 0) / 1024.0 / 1024.0;
            await NotifyRecordingAsync(NotificationTrigger.OnRecordingCompleted,
                $"Recording completed: {recording.Title}",
                $"Duration: {(result.DurationSeconds ?? 0) / 60}m\nSize: {sizeMb:F0} MB",
                recording);
        }

        return result;
    }

    /// <summary>
    /// Run the user's post-recording command for a completed recording.
    /// Custom-script hook in the arr tradition: the configured executable
    /// runs once per completed recording with the details passed as
    /// SPORTARR_* environment variables, never on the command line, so a
    /// recording path with spaces or quotes can't inject arguments. Fire
    /// and forget: commercial detection on a multi-hour recording can run
    /// a long time and must never block the recorder or the API.
    /// </summary>
    private void FirePostRecordingCommand(DvrRecording recording)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var config = await _configService.GetConfigAsync();
                var command = config.DvrPostRecordingCommand?.Trim();
                if (string.IsNullOrEmpty(command) || string.IsNullOrEmpty(recording.OutputPath))
                {
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                psi.Environment["SPORTARR_RECORDING_PATH"] = recording.OutputPath;
                psi.Environment["SPORTARR_RECORDING_TITLE"] = recording.Title;
                psi.Environment["SPORTARR_RECORDING_ID"] = recording.Id.ToString();
                psi.Environment["SPORTARR_EVENT_ID"] = recording.EventId?.ToString() ?? "";
                psi.Environment["SPORTARR_DURATION_SECONDS"] = recording.DurationSeconds?.ToString() ?? "";
                psi.Environment["SPORTARR_FILE_SIZE"] = recording.FileSize?.ToString() ?? "";

                _logger.LogInformation("[DVR] Running post-recording command for recording {Id}: {Command}",
                    recording.Id, command);

                using var process = Process.Start(psi);
                if (process == null)
                {
                    _logger.LogWarning("[DVR] Post-recording command failed to start: {Command}", command);
                    return;
                }

                // Read both streams concurrently so neither pipe buffer can
                // fill up and deadlock a chatty script.
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                // Generous ceiling: comskip on a four-hour recording is slow,
                // but a hung script must not leak a process forever.
                using var cts = new CancellationTokenSource(TimeSpan.FromHours(6));
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("[DVR] Post-recording command timed out after 6h for recording {Id}; killing it", recording.Id);
                    try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    return;
                }

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode == 0)
                {
                    _logger.LogInformation("[DVR] Post-recording command finished for recording {Id} (exit 0)", recording.Id);
                    if (!string.IsNullOrWhiteSpace(stdout))
                    {
                        _logger.LogDebug("[DVR] Post-recording command output: {Output}", stdout.Trim());
                    }
                }
                else
                {
                    _logger.LogWarning("[DVR] Post-recording command exited {Code} for recording {Id}: {Error}",
                        process.ExitCode, recording.Id,
                        string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DVR] Post-recording command failed for recording {Id}", recording.Id);
            }
        });
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
    /// Overtime guard: before an event-linked live recording is stopped at
    /// its scheduled end, ask the league's livescore feed whether the event
    /// is still in progress. If it is, push ScheduledEnd out another
    /// increment (which the scheduler and watchdog both respect) instead of
    /// cutting off the finish - the padding-based cutoff losing overtime,
    /// extra innings, and stoppage time is the classic sports DVR failure.
    /// Extension only happens on POSITIVE evidence: the event must appear
    /// in the feed with a non-terminal status. No feed, no match, an error,
    /// or the cap reached all mean "stop as scheduled", so the guard can
    /// never keep a recording alive on ambiguity.
    /// </summary>
    public async Task<bool> ShouldExtendForOvertimeAsync(DvrRecording recording)
    {
        if (recording.EventId == null || recording.Method != DvrRecordingMethod.Live)
            return false;

        try
        {
            var config = await _configService.GetConfigAsync();
            if (!config.DvrOvertimeGuardEnabled || config.DvrOvertimeMaxExtensionMinutes <= 0)
                return false;

            var extendedSoFar = _overtimeExtensions.GetValueOrDefault(recording.Id);
            if (extendedSoFar >= config.DvrOvertimeMaxExtensionMinutes)
            {
                _logger.LogWarning(
                    "[DVR] Recording {Id} hit the overtime extension ceiling ({Max}m); stopping at current end",
                    recording.Id, config.DvrOvertimeMaxExtensionMinutes);
                return false;
            }

            var evt = await _db.Events
                .Include(e => e.League)
                .FirstOrDefaultAsync(e => e.Id == recording.EventId.Value);
            if (evt?.ExternalId == null || evt.League?.ExternalId == null)
                return false;

            var livescore = await GetLivescoreCachedAsync(evt.League.ExternalId);
            var liveEntry = livescore?.FirstOrDefault(l => l.ExternalId == evt.ExternalId);
            if (liveEntry == null || !IndicatesInProgress(liveEntry.Status))
                return false;

            recording.ScheduledEnd = recording.ScheduledEnd.AddMinutes(OvertimeStepMinutes);
            recording.LastUpdated = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _overtimeExtensions[recording.Id] = extendedSoFar + OvertimeStepMinutes;

            _logger.LogInformation(
                "[DVR] Overtime guard: '{Title}' is still in progress ({Status}); extended recording {Id} by {Step}m ({Total}m/{Max}m used)",
                evt.Title, liveEntry.Status, recording.Id, OvertimeStepMinutes,
                extendedSoFar + OvertimeStepMinutes, config.DvrOvertimeMaxExtensionMinutes);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DVR] Overtime check failed for recording {Id}; stopping as scheduled", recording.Id);
            return false;
        }
    }

    /// <summary>
    /// A status counts as in-progress when the event appears in the
    /// livescore feed and its status is not one of the known terminal or
    /// pre-game labels. Being listed in the feed at all is already strong
    /// evidence of liveness; the label check filters the edges.
    /// </summary>
    public static bool IndicatesInProgress(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        var s = status.Trim().ToLowerInvariant();

        // Pre-game labels
        if (s is "scheduled" or "not started" or "ns" or "time to be defined" or "tbd")
            return false;

        // Terminal labels
        if (s.Contains("finish") || s.Contains("final") || s.Contains("ended") ||
            s.Contains("completed") || s.Contains("after") ||
            s is "ft" or "aet" or "aot" or "pen" or "postponed" or "cancelled" or "canceled" or "abandoned" or "suspended")
            return false;

        // Everything else on a live feed ("live", "1st half", "q3", "ht",
        // period/lap/round descriptors...) counts as in progress.
        return true;
    }

    private async Task<List<Event>?> GetLivescoreCachedAsync(string leagueExternalId)
    {
        if (_livescoreCache.TryGetValue(leagueExternalId, out var cached) &&
            DateTime.UtcNow - cached.FetchedAt < TimeSpan.FromSeconds(60))
        {
            return cached.Events;
        }

        var events = await _sportarrApiClient.GetLivescoreByLeagueAsync(leagueExternalId);
        if (events != null)
        {
            _livescoreCache[leagueExternalId] = (DateTime.UtcNow, events);
        }
        return events;
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

        // Find the mapped channel: the event's team preference first,
        // then the league preference.
        var channel = await _iptvService.GetPreferredChannelForEventAsync(
            evt.HomeTeamId, evt.AwayTeamId, evt.League.Id);

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

        // Get root folder (same logic as FileImportService). Root folders live
        // in the RootFolders table, loaded + live-state-refreshed here.
        var rootFolders = await RootFolderLoader.LoadAsync(_db, _diskSpaceService);
        var rootFolder = rootFolders
            .Where(rf => rf.Accessible)
            .OrderByDescending(rf => rf.FreeSpace)
            .FirstOrDefault();

        var basePath = rootFolder?.Path ?? Path.Combine(AppContext.BaseDirectory, "recordings");
        if (rootFolder == null)
        {
            _logger.LogWarning(
                "[DVR] No accessible root folder configured; recording to the application directory ({Path}). In Docker this lives INSIDE the container and is invisible on the host - add a root folder under Settings > Media Management to record somewhere durable.",
                basePath);
        }

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
                    SportarrId = eventInfo.ExternalId ?? string.Empty,
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

                var filename = _namingService.BuildFileName(settings.StandardFileFormat, tokens, $".{container}", settings.ReplaceIllegalCharacters);
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

        // Root folders live in the RootFolders table (loaded via RootFolderLoader).
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
