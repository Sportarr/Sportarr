using System.Text.Json;
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
    private readonly ChannelAutoMappingService _autoMappingService;
    private readonly FFmpegRecorderService _ffmpegService;
    private readonly ReleaseEvaluator _releaseEvaluator;
    private readonly EpgSchedulingService _epgSchedulingService;
    private readonly EventChannelResolverService _channelResolver;
    private readonly ConfigService _configService;

    public EventDvrService(
        ILogger<EventDvrService> logger,
        SportarrDbContext db,
        DvrRecordingService dvrService,
        IptvSourceService iptvService,
        ChannelAutoMappingService autoMappingService,
        FFmpegRecorderService ffmpegService,
        ReleaseEvaluator releaseEvaluator,
        EpgSchedulingService epgSchedulingService,
        EventChannelResolverService channelResolver,
        ConfigService configService)
    {
        _logger = logger;
        _db = db;
        _dvrService = dvrService;
        _iptvService = iptvService;
        _autoMappingService = autoMappingService;
        _ffmpegService = ffmpegService;
        _releaseEvaluator = releaseEvaluator;
        _epgSchedulingService = epgSchedulingService;
        _channelResolver = channelResolver;
        _configService = configService;
    }

    /// <summary>
    /// Schedule DVR recording for an event when it becomes monitored.
    /// Only schedules if:
    /// - Event has a league with a mapped channel
    /// - Event date is in the future, OR the event already aired and the
    ///   resolved channel keeps a catchup archive (catchup backfill)
    /// - No existing recording for this event
    /// Channels with an Xtream catchup archive get Method=Catchup
    /// (downloaded after airing by CatchupDownloadService); the rest
    /// keep the live recording path.
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

        // Past events can't be recorded live, but they CAN still be
        // downloaded from a catchup archive if the resolved channel keeps
        // one (checked after channel resolution below). Remember the
        // distinction here instead of rejecting outright.
        var isPastEvent = evt.EventDate <= DateTime.UtcNow;

        // Check if event has a league
        if (evt.LeagueId == null)
        {
            _logger.LogDebug("[EventDVR] Event {EventId} has no league, skipping DVR scheduling", eventId);
            return null;
        }

        // An explicit per-team channel preference beats every scored
        // resolver: a user who mapped "Lakers -> LA regional channel" did
        // so precisely because the automatic pick records the wrong feed
        // for that team. Home team first (regional channels follow the
        // hosting market), then away, then the scored resolution below.
        var teamPreferred = await _iptvService.GetPreferredChannelForEventAsync(
            evt.HomeTeamId, evt.AwayTeamId, leagueId: null);
        if (teamPreferred != null)
        {
            _logger.LogDebug("[EventDVR] Using team-preferred channel '{Channel}' for event {EventId}",
                teamPreferred.Name, eventId);
        }

        // Pull the full ranked candidate list from the event-channel
        // resolver. The head of the list is the primary channel; the
        // rest become FallbackChannelIds on the recording so the DVR
        // service can rotate to them automatically when a recording
        // fails partway through (codec error, stream drop, source
        // tuner saturated, etc.). Phase 3 added this for resilience.
        var candidates = await _channelResolver.ResolveAsync(evt.Id);

        IptvChannel? channel = teamPreferred;
        var fallbackIds = new List<int>();
        if (channel != null && candidates.Count > 0)
        {
            // Team preference is primary; the resolver's picks become the
            // fallback rotation (excluding the primary itself).
            fallbackIds = candidates
                .Where(c => c.ChannelId != channel.Id)
                .Take(4)
                .Select(c => c.ChannelId)
                .ToList();
        }
        else if (candidates.Count > 0)
        {
            // Use the resolver's recommendation as primary.
            channel = await _db.IptvChannels
                .Include(c => c.Source)
                .FirstOrDefaultAsync(c => c.Id == candidates[0].ChannelId);

            // Up to 4 backup channels — the auto-retry loop in
            // DvrRecordingService.RescheduleOnFallbackAsync caps at
            // AutoRetryCount which gives us plenty of room for
            // transient failures.
            fallbackIds = candidates.Skip(1).Take(4).Select(c => c.ChannelId).ToList();
        }

        // Fall back to the legacy single-channel paths when the resolver
        // didn't return anything (e.g., league has no broadcast string,
        // no EPG match, no scored mappings) — these still produce a
        // recording, just without fallback candidates.
        if (channel == null)
        {
            channel = await _autoMappingService.GetBestChannelForLeagueAsync(evt.LeagueId.Value);
        }
        if (channel == null)
        {
            channel = await _iptvService.GetPreferredChannelForLeagueAsync(evt.LeagueId.Value);
        }

        if (channel == null)
        {
            _logger.LogDebug("[EventDVR] No IPTV channel mapped to league {LeagueId} for event {EventId}",
                evt.LeagueId, eventId);
            return null;
        }

        // Log quality info for the selected channel
        _logger.LogDebug("[EventDVR] Selected channel '{Channel}' (Quality: {Quality}, Score: {Score}) for event {EventId}",
            channel.Name, channel.DetectedQuality ?? "Unknown", channel.QualityScore, eventId);

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

        // Catchup-first: when the resolved channel keeps a provider-side
        // archive (Xtream tv_archive), defer to a catchup download after
        // the event finishes instead of capturing live. No start/end
        // guessing, immune to app downtime during the broadcast, and
        // retryable while the archive retains the window. Channels
        // without an archive keep the live path - catchup augments the
        // recorder, it doesn't replace it. Method ported from timeshifter
        // by scottrobertson (github.com/scottrobertson/timeshifter).
        var config = await _configService.GetConfigAsync();
        var sourceType = channel.Source?.Type
            ?? await _db.IptvSources
                .Where(s => s.Id == channel.SourceId)
                .Select(s => s.Type)
                .FirstOrDefaultAsync();
        var useCatchup = config.DvrUseCatchupWhenAvailable
            && channel.HasArchive
            && sourceType == IptvSourceType.Xtream;

        // A past event can only be acquired from an archive. Without a
        // catchup-capable channel there is nothing to record from.
        if (isPastEvent && !useCatchup)
        {
            _logger.LogDebug(
                "[EventDVR] Event {EventId} already aired and channel '{Channel}' has no catchup archive, skipping",
                eventId, channel.Name);
            return null;
        }

        // Resolve padding from the league override + sport defaults
        // before falling back to a generic 5/30. NFL games run long
        // and need ~30min post-roll; soccer matches usually need
        // ~15. The League can override the sport default.
        var league = evt.League;
        var pad = Sportarr.Api.Helpers.DvrPaddingDefaults.Resolve(
            league?.Sport,
            league?.DvrPrePadMinutes,
            league?.DvrPostRollMinutes,
            fallbackPre: 5,
            fallbackPost: 30);
        var prePadding = pad.PrePadMinutes;
        var postPadding = pad.PostRollMinutes;
        var timeOptimization = await _epgSchedulingService.GetOptimizedRecordingTimesAsync(
            evt, channel, prePadding, postPadding);

        if (timeOptimization.TimeMismatchDetected)
        {
            _logger.LogWarning("[EventDVR] EPG time mismatch for event {EventId}: {Title} - " +
                "Sports API: {ApiTime}, EPG differs by {Diff:+0;-0} minutes. Using optimized times.",
                eventId, evt.Title, evt.EventDate.ToString("HH:mm"), timeOptimization.TimeMismatchMinutes);
        }

        // Schedule the recording with EPG-optimized times
        try
        {
            var recording = await _dvrService.ScheduleRecordingAsync(new ScheduleDvrRecordingRequest
            {
                EventId = eventId,
                ChannelId = channel.Id,
                ScheduledStart = timeOptimization.OptimizedStartTime.AddMinutes(prePadding), // Remove double padding
                ScheduledEnd = timeOptimization.OptimizedEndTime.AddMinutes(-postPadding), // Remove double padding
                PrePadding = prePadding,
                PostPadding = postPadding,
                Method = useCatchup ? DvrRecordingMethod.Catchup : DvrRecordingMethod.Live
            });

            // Redundant recording: when configured, also record the event
            // from the next-best DISTINCT channels, preferring a different
            // source than the primary, so one provider dropping mid-event
            // can't lose the whole recording. The copies get no fallback
            // rotation of their own (the primary owns rotation).
            var redundantChannelIds = new List<int>();
            var dvrConfig = await _configService.GetConfigAsync();
            var simultaneous = Math.Clamp(dvrConfig.DvrSimultaneousChannels, 1, 5);
            if (simultaneous > 1 && !useCatchup && recording != null)
            {
                var usedChannelIds = new HashSet<int> { channel.Id };
                var usedSourceIds = new HashSet<int> { channel.SourceId };
                var extraCandidateIds = candidates
                    .Select(c => c.ChannelId)
                    .Where(id => id != channel.Id)
                    .ToList();
                var extraChannels = await _db.IptvChannels
                    .Include(c => c.Source)
                    .Where(c => extraCandidateIds.Contains(c.Id))
                    .ToListAsync();
                // Different-source channels first (cross-provider redundancy
                // is the point), resolver confidence order within each group.
                var ordered = extraChannels
                    .OrderBy(c => usedSourceIds.Contains(c.SourceId) ? 1 : 0)
                    .ThenBy(c => extraCandidateIds.IndexOf(c.Id))
                    .ToList();

                foreach (var extra in ordered)
                {
                    if (redundantChannelIds.Count >= simultaneous - 1) break;
                    if (!usedChannelIds.Add(extra.Id)) continue;
                    try
                    {
                        var extraTimes = await _epgSchedulingService.GetOptimizedRecordingTimesAsync(
                            evt, extra, prePadding, postPadding);
                        var extraRecording = await _dvrService.ScheduleRecordingAsync(new ScheduleDvrRecordingRequest
                        {
                            EventId = eventId,
                            ChannelId = extra.Id,
                            ScheduledStart = extraTimes.OptimizedStartTime.AddMinutes(prePadding),
                            ScheduledEnd = extraTimes.OptimizedEndTime.AddMinutes(-postPadding),
                            PrePadding = prePadding,
                            PostPadding = postPadding,
                            Method = DvrRecordingMethod.Live
                        });
                        if (extraRecording != null)
                        {
                            redundantChannelIds.Add(extra.Id);
                            usedSourceIds.Add(extra.SourceId);
                            _logger.LogInformation(
                                "[EventDVR] Scheduled redundant recording for event {EventId} on channel '{Channel}' (source: {Source})",
                                eventId, extra.Name, extra.Source?.Name ?? extra.SourceId.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "[EventDVR] Failed to schedule redundant recording on channel {ChannelId} for event {EventId}",
                            extra.Id, eventId);
                    }
                }
            }

            // Persist fallback channel list so the DVR service can
            // auto-rotate to backups on failure. Drop the primary out
            // of the list (it's already ChannelId), drop any channel a
            // redundant copy is already recording from (a failed primary
            // must not rotate onto a channel that's mid-recording the
            // same event), and keep the rest in confidence order.
            if (recording != null && fallbackIds.Count > 0)
            {
                var backups = fallbackIds
                    .Where(id => id != channel.Id && !redundantChannelIds.Contains(id))
                    .ToList();
                if (backups.Count > 0)
                {
                    recording.FallbackChannelIds = JsonSerializer.Serialize(backups);
                    await _db.SaveChangesAsync();
                }
            }

            var durationInfo = timeOptimization.UsedEpgData
                ? $"{timeOptimization.DurationMinutes}min from EPG"
                : $"{timeOptimization.DurationMinutes}min (default)";

            _logger.LogInformation("[EventDVR] Scheduled {Method} recording for event {EventId}: {Title} on channel {Channel} ({Quality}) - {Duration} (fallbacks: {Fallbacks})",
                useCatchup ? "catchup" : "live DVR", eventId, evt.Title, channel.Name, channel.DetectedQuality ?? "HD", durationInfo, fallbackIds.Count);

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
            var channel = await _iptvService.GetPreferredChannelForEventAsync(
                evt.HomeTeamId, evt.AwayTeamId, evt.LeagueId.Value);
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
    /// Build a synthetic release title from recording info for custom format matching.
    /// Follows scene release naming conventions so TRaSH Guide custom formats can match properly.
    /// Example: "Event.Name.2024.1080p.HDTV.H.264.AAC.2.0-DVR"
    /// </summary>
    private static string BuildSyntheticTitle(DvrRecording recording, MediaProbeResult probeResult)
    {
        var parts = new List<string>();

        // Add event title if available (sanitized for release name)
        if (!string.IsNullOrEmpty(recording.Title))
        {
            parts.Add(SanitizeForReleaseName(recording.Title));
        }

        // Add year
        parts.Add(DateTime.UtcNow.Year.ToString());

        // Add resolution (e.g., "1080p", "720p", "2160p")
        if (probeResult.Height.HasValue)
        {
            parts.Add(probeResult.GetResolutionString());
        }

        // Add source type - HDTV for DVR/IPTV recordings (matches scene naming conventions)
        // DVR recordings are essentially TV captures, so HDTV is the correct source tag
        parts.Add("HDTV");

        // Add video codec in scene format (H.264, HEVC, x264, x265)
        if (!string.IsNullOrEmpty(probeResult.VideoCodec))
        {
            var codec = probeResult.VideoCodec.ToLowerInvariant() switch
            {
                "h264" or "avc" or "avc1" => "H.264",
                "hevc" or "h265" or "hvc1" => "HEVC",
                "vp9" => "VP9",
                "av1" => "AV1",
                "mpeg2video" => "MPEG2",
                _ => probeResult.VideoCodec.ToUpperInvariant()
            };
            parts.Add(codec);
        }

        // Add audio codec in scene format (AAC, AC3, DTS, EAC3, etc.)
        if (!string.IsNullOrEmpty(probeResult.AudioCodec))
        {
            var audioCodec = probeResult.AudioCodec.ToLowerInvariant() switch
            {
                "aac" => "AAC",
                "ac3" or "ac-3" => "DD", // Dolby Digital
                "eac3" or "e-ac-3" => "DDP", // Dolby Digital Plus
                "dts" => "DTS",
                "truehd" => "TrueHD",
                "flac" => "FLAC",
                "mp3" => "MP3",
                "opus" => "OPUS",
                "vorbis" => "Vorbis",
                "mp2" => "MP2",
                _ => probeResult.AudioCodec.ToUpperInvariant()
            };
            parts.Add(audioCodec);

            // Add audio channel layout (2.0, 5.1, 7.1)
            if (probeResult.AudioChannels.HasValue)
            {
                var channelLayout = probeResult.AudioChannels.Value switch
                {
                    1 => "1.0",
                    2 => "2.0",
                    6 => "5.1",
                    8 => "7.1",
                    _ => $"{probeResult.AudioChannels}.0"
                };
                parts.Add(channelLayout);
            }
        }

        // Add release group suffix to indicate DVR source
        var result = string.Join(".", parts);
        result += "-DVR";

        return result;
    }

    /// <summary>
    /// Sanitize a title for use in a scene-style release name
    /// </summary>
    private static string SanitizeForReleaseName(string title)
    {
        if (string.IsNullOrEmpty(title))
            return string.Empty;

        // Replace spaces and special characters with dots
        var sanitized = System.Text.RegularExpressions.Regex.Replace(title, @"[^\w\d]+", ".");
        // Remove consecutive dots
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\.+", ".");
        // Trim dots from start and end
        return sanitized.Trim('.');
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
