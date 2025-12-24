using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for integrating DVR recordings with events.
/// Handles automatic recording scheduling, status tracking, and recording import.
/// </summary>
public class EventDvrService
{
    private readonly ILogger<EventDvrService> _logger;
    private readonly SportarrDbContext _db;
    private readonly DvrRecordingService _dvrService;
    private readonly IptvSourceService _iptvService;
    private readonly FFmpegRecorderService _ffmpegService;
    private readonly ReleaseEvaluator _releaseEvaluator;

    public EventDvrService(
        ILogger<EventDvrService> logger,
        SportarrDbContext db,
        DvrRecordingService dvrService,
        IptvSourceService iptvService,
        FFmpegRecorderService ffmpegService,
        ReleaseEvaluator releaseEvaluator)
    {
        _logger = logger;
        _db = db;
        _dvrService = dvrService;
        _iptvService = iptvService;
        _ffmpegService = ffmpegService;
        _releaseEvaluator = releaseEvaluator;
    }

    /// <summary>
    /// Schedule DVR recording for an event when it becomes monitored.
    /// Only schedules if:
    /// - Event has a league with a mapped channel
    /// - Event date is in the future
    /// - No existing recording for this event
    /// </summary>
    public async Task<DvrRecording?> ScheduleRecordingForEventAsync(int eventId)
    {
        var evt = await _db.Events
            .Include(e => e.League)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null)
        {
            _logger.LogWarning("[EventDVR] Event {EventId} not found", eventId);
            return null;
        }

        // Only schedule for monitored events
        if (!evt.Monitored)
        {
            _logger.LogDebug("[EventDVR] Event {EventId} is not monitored, skipping DVR scheduling", eventId);
            return null;
        }

        // Only schedule for future events
        if (evt.EventDate <= DateTime.UtcNow)
        {
            _logger.LogDebug("[EventDVR] Event {EventId} is in the past, skipping DVR scheduling", eventId);
            return null;
        }

        // Check if event has a league
        if (evt.LeagueId == null)
        {
            _logger.LogDebug("[EventDVR] Event {EventId} has no league, skipping DVR scheduling", eventId);
            return null;
        }

        // Check if there's a channel mapped to this league
        var channel = await _iptvService.GetPreferredChannelForLeagueAsync(evt.LeagueId.Value);
        if (channel == null)
        {
            _logger.LogDebug("[EventDVR] No IPTV channel mapped to league {LeagueId} for event {EventId}",
                evt.LeagueId, eventId);
            return null;
        }

        // Check if recording already exists
        var existingRecording = await _db.DvrRecordings
            .FirstOrDefaultAsync(r => r.EventId == eventId &&
                                     r.Status != DvrRecordingStatus.Cancelled &&
                                     r.Status != DvrRecordingStatus.Failed);

        if (existingRecording != null)
        {
            _logger.LogDebug("[EventDVR] Recording already exists for event {EventId}: {RecordingId}",
                eventId, existingRecording.Id);
            return existingRecording;
        }

        // Schedule the recording
        try
        {
            var recording = await _dvrService.ScheduleRecordingAsync(new ScheduleDvrRecordingRequest
            {
                EventId = eventId,
                ChannelId = channel.Id,
                ScheduledStart = evt.EventDate,
                ScheduledEnd = evt.EventDate.AddHours(3), // Default 3 hour duration for sports
                PrePadding = 5,
                PostPadding = 30
            });

            _logger.LogInformation("[EventDVR] Scheduled DVR recording for event {EventId}: {Title} on channel {Channel}",
                eventId, evt.Title, channel.Name);

            return recording;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EventDVR] Failed to schedule recording for event {EventId}", eventId);
            return null;
        }
    }

    /// <summary>
    /// Cancel DVR recording for an event when it becomes unmonitored.
    /// </summary>
    public async Task CancelRecordingsForEventAsync(int eventId)
    {
        var recordings = await _db.DvrRecordings
            .Where(r => r.EventId == eventId && r.Status == DvrRecordingStatus.Scheduled)
            .ToListAsync();

        foreach (var recording in recordings)
        {
            await _dvrService.CancelRecordingAsync(recording.Id);
            _logger.LogInformation("[EventDVR] Cancelled DVR recording {RecordingId} for event {EventId}",
                recording.Id, eventId);
        }
    }

    /// <summary>
    /// Handle event monitoring change - schedule or cancel recordings accordingly.
    /// </summary>
    public async Task HandleEventMonitoringChangeAsync(int eventId, bool monitored)
    {
        if (monitored)
        {
            await ScheduleRecordingForEventAsync(eventId);
        }
        else
        {
            await CancelRecordingsForEventAsync(eventId);
        }
    }

    /// <summary>
    /// Get DVR status for an event.
    /// </summary>
    public async Task<EventDvrStatus?> GetEventDvrStatusAsync(int eventId)
    {
        var evt = await _db.Events
            .Include(e => e.League)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt == null)
            return null;

        // Get all recordings for this event
        var recordings = await _db.DvrRecordings
            .Include(r => r.Channel)
            .Where(r => r.EventId == eventId)
            .OrderByDescending(r => r.Created)
            .ToListAsync();

        // Check if league has a mapped channel
        bool hasChannelMapping = false;
        string? mappedChannelName = null;

        if (evt.LeagueId.HasValue)
        {
            var channel = await _iptvService.GetPreferredChannelForLeagueAsync(evt.LeagueId.Value);
            hasChannelMapping = channel != null;
            mappedChannelName = channel?.Name;
        }

        return new EventDvrStatus
        {
            EventId = eventId,
            HasChannelMapping = hasChannelMapping,
            MappedChannelName = mappedChannelName,
            CanScheduleRecording = evt.Monitored && evt.EventDate > DateTime.UtcNow && hasChannelMapping,
            Recordings = recordings.Select(r => new EventDvrRecordingInfo
            {
                Id = r.Id,
                Status = r.Status,
                ChannelName = r.Channel?.Name ?? "Unknown",
                ScheduledStart = r.ScheduledStart,
                ScheduledEnd = r.ScheduledEnd,
                ActualStart = r.ActualStart,
                ActualEnd = r.ActualEnd,
                OutputPath = r.OutputPath,
                FileSize = r.FileSize,
                ErrorMessage = r.ErrorMessage
            }).ToList()
        };
    }

    /// <summary>
    /// Get DVR status for multiple events.
    /// </summary>
    public async Task<Dictionary<int, EventDvrStatus>> GetEventDvrStatusesAsync(IEnumerable<int> eventIds)
    {
        var statuses = new Dictionary<int, EventDvrStatus>();

        foreach (var eventId in eventIds)
        {
            var status = await GetEventDvrStatusAsync(eventId);
            if (status != null)
            {
                statuses[eventId] = status;
            }
        }

        return statuses;
    }

    /// <summary>
    /// Schedule recordings for all monitored upcoming events that don't have recordings.
    /// </summary>
    public async Task<int> ScheduleRecordingsForUpcomingEventsAsync()
    {
        var upcomingEvents = await _db.Events
            .Include(e => e.League)
            .Where(e => e.Monitored)
            .Where(e => e.EventDate > DateTime.UtcNow)
            .Where(e => e.LeagueId != null)
            .ToListAsync();

        int scheduledCount = 0;

        foreach (var evt in upcomingEvents)
        {
            var recording = await ScheduleRecordingForEventAsync(evt.Id);
            if (recording != null)
            {
                scheduledCount++;
            }
        }

        if (scheduledCount > 0)
        {
            _logger.LogInformation("[EventDVR] Scheduled {Count} DVR recordings for upcoming events", scheduledCount);
        }

        return scheduledCount;
    }

    /// <summary>
    /// Import a completed DVR recording as an event file.
    /// </summary>
    public async Task<bool> ImportCompletedRecordingAsync(int recordingId)
    {
        var recording = await _db.DvrRecordings
            .Include(r => r.Event)
            .ThenInclude(e => e!.League)
            .Include(r => r.Channel)
            .FirstOrDefaultAsync(r => r.Id == recordingId);

        if (recording == null)
        {
            _logger.LogWarning("[EventDVR] Recording {RecordingId} not found for import", recordingId);
            return false;
        }

        if (recording.Status != DvrRecordingStatus.Completed)
        {
            _logger.LogWarning("[EventDVR] Recording {RecordingId} is not completed (status: {Status})",
                recordingId, recording.Status);
            return false;
        }

        if (recording.Event == null)
        {
            _logger.LogDebug("[EventDVR] Recording {RecordingId} has no associated event, skipping import",
                recordingId);
            return false;
        }

        if (string.IsNullOrEmpty(recording.OutputPath) || !File.Exists(recording.OutputPath))
        {
            _logger.LogWarning("[EventDVR] Recording {RecordingId} output file not found: {Path}",
                recordingId, recording.OutputPath);
            recording.Status = DvrRecordingStatus.Failed;
            recording.ErrorMessage = "Output file not found";
            await _db.SaveChangesAsync();
            return false;
        }

        // Probe the file to detect quality
        await ProbeAndUpdateRecordingQualityAsync(recording);

        // Check if file already exists for this event
        var existingFile = await _db.EventFiles
            .FirstOrDefaultAsync(f => f.EventId == recording.EventId &&
                                     f.PartName == recording.PartName);

        if (existingFile != null)
        {
            _logger.LogDebug("[EventDVR] Event {EventId} already has a file for part {Part}, skipping import",
                recording.EventId, recording.PartName ?? "Main");
            return true;
        }

        // Get quality score based on event's quality profile
        var qualityScore = recording.QualityScore ?? 50;
        var customFormatScore = recording.CustomFormatScore ?? 0;

        // Create event file record
        var eventFile = new EventFile
        {
            EventId = recording.EventId!.Value,
            FilePath = recording.OutputPath,
            Size = recording.FileSize ?? 0,
            Quality = recording.Quality ?? "DVR",
            QualityScore = qualityScore,
            CustomFormatScore = customFormatScore,
            Source = "IPTV",
            Codec = recording.VideoCodec,
            PartName = recording.PartName,
            PartNumber = !string.IsNullOrEmpty(recording.PartName)
                ? GetPartNumberFromName(recording.PartName)
                : null,
            Added = DateTime.UtcNow,
            LastVerified = DateTime.UtcNow,
            Exists = true,
            OriginalTitle = $"DVR Recording - {recording.Channel?.Name ?? "Unknown"}"
        };

        _db.EventFiles.Add(eventFile);

        // Update event status
        recording.Event.HasFile = true;
        recording.Event.FilePath = recording.OutputPath;
        recording.Event.FileSize = recording.FileSize;
        recording.Event.Quality = recording.Quality ?? "DVR";
        recording.Event.LastUpdate = DateTime.UtcNow;

        // Update recording status to imported
        recording.Status = DvrRecordingStatus.Imported;
        recording.LastUpdated = DateTime.UtcNow;
        recording.ImportedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("[EventDVR] Imported DVR recording {RecordingId} as file for event {EventId}: {Title} ({Quality}, Score: {Score})",
            recordingId, recording.EventId, recording.Event.Title, recording.Quality, qualityScore);

        return true;
    }

    /// <summary>
    /// Probe a recording file to detect and update quality information
    /// </summary>
    private async Task ProbeAndUpdateRecordingQualityAsync(DvrRecording recording)
    {
        if (string.IsNullOrEmpty(recording.OutputPath) || !File.Exists(recording.OutputPath))
            return;

        try
        {
            var probeResult = await _ffmpegService.ProbeFileAsync(recording.OutputPath);
            if (!probeResult.Success)
            {
                _logger.LogWarning("[EventDVR] Failed to probe recording {RecordingId}: {Error}",
                    recording.Id, probeResult.Error);
                return;
            }

            // Update recording with detected quality info
            recording.VideoWidth = probeResult.Width;
            recording.VideoHeight = probeResult.Height;
            recording.VideoCodec = probeResult.GetCodecDisplay();
            recording.AudioCodec = probeResult.AudioCodec;
            recording.AudioChannels = probeResult.AudioChannels;

            // Determine quality based on resolution
            var resolution = probeResult.GetResolution();
            var qualityDef = QualityParser.MapQuality(QualityParser.QualitySource.IPTV, resolution, false);
            recording.Quality = qualityDef.Name;

            // Calculate quality score based on event's quality profile
            if (recording.Event != null)
            {
                var qualityProfile = await GetEventQualityProfileAsync(recording.Event.Id);
                if (qualityProfile != null)
                {
    recording.QualityScore = _releaseEvaluator.CalculateQualityScore(qualityDef.Name, qualityProfile);

                    // Calculate custom format score
                    // For DVR recordings, we can create a synthetic "title" with detected info for custom format matching
                    var syntheticTitle = BuildSyntheticTitle(recording, probeResult);
                    var customFormats = await _db.CustomFormats.Include(cf => cf.Specifications).ToListAsync();
                    var formatScore = _releaseEvaluator.CalculateCustomFormatScore(syntheticTitle, qualityProfile, customFormats);
                    recording.CustomFormatScore = formatScore;
                }
                else
                {
                    // Default score if no profile
                    recording.QualityScore = GetDefaultQualityScore(qualityDef);
                }
            }

            _logger.LogDebug("[EventDVR] Probed recording {RecordingId}: {Width}x{Height} {Codec} -> {Quality} (Score: {Score})",
                recording.Id, recording.VideoWidth, recording.VideoHeight, recording.VideoCodec,
                recording.Quality, recording.QualityScore);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EventDVR] Error probing recording {RecordingId}", recording.Id);
        }
    }

    /// <summary>
    /// Build a synthetic release title from recording info for custom format matching
    /// </summary>
    private static string BuildSyntheticTitle(DvrRecording recording, MediaProbeResult probeResult)
    {
        var parts = new List<string>();

        // Add resolution
        if (probeResult.Height.HasValue)
        {
            parts.Add(probeResult.GetResolutionString());
        }

        // Add codec
        if (!string.IsNullOrEmpty(probeResult.VideoCodec))
        {
            parts.Add(probeResult.GetCodecDisplay());
        }

        // Add audio info
        if (probeResult.AudioChannels.HasValue)
        {
            parts.Add(probeResult.GetAudioChannelsDisplay());
        }

        // Add audio codec
        if (!string.IsNullOrEmpty(probeResult.AudioCodec))
        {
            parts.Add(probeResult.AudioCodec.ToUpperInvariant());
        }

        // Mark as IPTV/DVR source
        parts.Add("IPTV");

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Get the quality profile for an event
    /// </summary>
    private async Task<QualityProfile?> GetEventQualityProfileAsync(int eventId)
    {
        var evt = await _db.Events
            .Include(e => e.League)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (evt?.League?.QualityProfileId == null)
            return null;

        return await _db.QualityProfiles.FirstOrDefaultAsync(p => p.Id == evt.League.QualityProfileId);
    }

    /// <summary>
    /// Get default quality score when no profile is available
    /// </summary>
    private static int GetDefaultQualityScore(QualityParser.QualityDefinition quality)
    {
        // Map quality to a reasonable score based on resolution
        return quality.Resolution switch
        {
            QualityParser.Resolution.R2160p => 400,
            QualityParser.Resolution.R1080p => 300,
            QualityParser.Resolution.R720p => 200,
            QualityParser.Resolution.R576p or QualityParser.Resolution.R540p or QualityParser.Resolution.R480p => 100,
            _ => 50
        };
    }

    /// <summary>
    /// Import all completed recordings that haven't been imported yet.
    /// </summary>
    public async Task<int> ImportAllCompletedRecordingsAsync()
    {
        var completedRecordings = await _db.DvrRecordings
            .Where(r => r.Status == DvrRecordingStatus.Completed)
            .Where(r => r.EventId != null)
            .Select(r => r.Id)
            .ToListAsync();

        int importedCount = 0;

        foreach (var recordingId in completedRecordings)
        {
            if (await ImportCompletedRecordingAsync(recordingId))
            {
                importedCount++;
            }
        }

        if (importedCount > 0)
        {
            _logger.LogInformation("[EventDVR] Imported {Count} completed DVR recordings", importedCount);
        }

        return importedCount;
    }

    /// <summary>
    /// Get part number from part name for fighting sports.
    /// </summary>
    private static int? GetPartNumberFromName(string partName)
    {
        return partName.ToLowerInvariant() switch
        {
            "early prelims" => 1,
            "prelims" => 2,
            "main card" => 3,
            "full event" => 0,
            _ => null
        };
    }
}

/// <summary>
/// DVR status information for an event.
/// </summary>
public class EventDvrStatus
{
    public int EventId { get; set; }
    public bool HasChannelMapping { get; set; }
    public string? MappedChannelName { get; set; }
    public bool CanScheduleRecording { get; set; }
    public List<EventDvrRecordingInfo> Recordings { get; set; } = new();
}

/// <summary>
/// DVR recording information for an event.
/// </summary>
public class EventDvrRecordingInfo
{
    public int Id { get; set; }
    public DvrRecordingStatus Status { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    public string? OutputPath { get; set; }
    public long? FileSize { get; set; }
    public string? ErrorMessage { get; set; }
}
