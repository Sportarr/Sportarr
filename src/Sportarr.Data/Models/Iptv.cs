using System.Text.Json.Serialization;

namespace Sportarr.Api.Models;

/// <summary>
/// DVR upgrade mode - controls how DVR recordings interact with indexer searches
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DvrUpgradeMode
{
    /// <summary>
    /// DVR only - never search indexers, use DVR recordings exclusively
    /// </summary>
    DvrOnly,

    /// <summary>
    /// DVR with upgrades - DVR recordings can be upgraded by better releases from indexers
    /// </summary>
    DvrWithUpgrades,

    /// <summary>
    /// Indexer preferred - Search indexers first, fall back to DVR if nothing found
    /// </summary>
    IndexerPreferred,

    /// <summary>
    /// DVR disabled - Don't use DVR at all, indexers only (default behavior)
    /// </summary>
    IndexerOnly
}

/// <summary>
/// DVR multi-part recording mode for fighting sports
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DvrMultiPartMode
{
    /// <summary>
    /// Record the entire event as a single file
    /// </summary>
    SingleRecording,

    /// <summary>
    /// Split recording based on configured part durations
    /// </summary>
    ManualPartDurations,

    /// <summary>
    /// Use EPG data to detect and split parts automatically
    /// </summary>
    EpgBased
}

/// <summary>
/// DVR settings configuration
/// </summary>
public class DvrSettings
{
    /// <summary>
    /// How DVR recordings interact with indexer searches
    /// </summary>
    public DvrUpgradeMode UpgradeMode { get; set; } = DvrUpgradeMode.DvrWithUpgrades;

    /// <summary>
    /// How to handle multi-part events (fighting sports)
    /// </summary>
    public DvrMultiPartMode MultiPartMode { get; set; } = DvrMultiPartMode.SingleRecording;

    /// <summary>
    /// Default pre-padding in minutes (start recording before event)
    /// </summary>
    public int DefaultPrePadding { get; set; } = 5;

    /// <summary>
    /// Default post-padding in minutes (continue recording after event)
    /// </summary>
    public int DefaultPostPadding { get; set; } = 30;

    /// <summary>
    /// Output file format (ts, mkv, mp4)
    /// </summary>
    public string OutputFormat { get; set; } = "ts";

    /// <summary>
    /// Auto-import completed recordings to library
    /// </summary>
    public bool AutoImport { get; set; } = true;

    /// <summary>
    /// Delete source recording after successful import
    /// </summary>
    public bool DeleteAfterImport { get; set; } = false;

    /// <summary>
    /// Duration in minutes for "Early Prelims" part (fighting sports)
    /// </summary>
    public int EarlyPrelimsMinutes { get; set; } = 120;

    /// <summary>
    /// Duration in minutes for "Prelims" part (fighting sports)
    /// </summary>
    public int PrelimsMinutes { get; set; } = 120;

    /// <summary>
    /// Duration in minutes for "Main Card" part (fighting sports)
    /// </summary>
    public int MainCardMinutes { get; set; } = 180;

    // Quality and encoding settings (new)

    /// <summary>
    /// Default quality profile ID to use for new recordings
    /// </summary>
    public int DefaultProfileId { get; set; } = 1;

    /// <summary>
    /// Root path for DVR recordings
    /// </summary>
    public string RecordingPath { get; set; } = "";

    /// <summary>
    /// File naming pattern for recordings
    /// Tokens: {Title}, {Date}, {Time}, {League}, {Teams}, {Quality}
    /// </summary>
    public string FileNamingPattern { get; set; } = "{Title} - {Date}";

    /// <summary>
    /// Maximum concurrent recordings (0 = unlimited)
    /// </summary>
    public int MaxConcurrentRecordings { get; set; } = 0;

    /// <summary>
    /// Days to keep recordings before auto-cleanup (0 = never delete)
    /// </summary>
    public int RecordingRetentionDays { get; set; } = 0;

    /// <summary>
    /// Preferred hardware acceleration method
    /// </summary>
    public HardwareAcceleration PreferredHardwareAcceleration { get; set; } = HardwareAcceleration.Auto;

    /// <summary>
    /// Path to FFmpeg binary (empty = use system PATH)
    /// </summary>
    public string FfmpegPath { get; set; } = "";

    /// <summary>
    /// Enable stream reconnection on temporary failures
    /// </summary>
    public bool EnableReconnect { get; set; } = true;

    /// <summary>
    /// Maximum reconnection attempts before failing
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 5;

    /// <summary>
    /// Delay between reconnection attempts in seconds
    /// </summary>
    public int ReconnectDelaySeconds { get; set; } = 5;
}

/// <summary>
/// IPTV source type (M3U playlist or Xtream Codes API)
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IptvSourceType
{
    /// <summary>
    /// Standard M3U/M3U8 playlist URL
    /// </summary>
    M3U,

    /// <summary>
    /// Xtream Codes API (username/password/server)
    /// </summary>
    Xtream
}

/// <summary>
/// IPTV channel status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IptvChannelStatus
{
    Unknown,
    Online,
    Offline,
    Error
}

/// <summary>
/// DVR recording status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DvrRecordingStatus
{
    /// <summary>
    /// Recording is scheduled but not started
    /// </summary>
    Scheduled,

    /// <summary>
    /// Recording is currently in progress
    /// </summary>
    Recording,

    /// <summary>
    /// Recording completed successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Recording failed
    /// </summary>
    Failed,

    /// <summary>
    /// Recording was cancelled by user
    /// </summary>
    Cancelled,

    /// <summary>
    /// Recording is being imported to library
    /// </summary>
    Importing,

    /// <summary>
    /// Recording was imported successfully
    /// </summary>
    Imported
}

/// <summary>
/// How a DVR recording acquires its content.
///
/// Catchup downloads pull the already-aired window from the provider's
/// timeshift archive after the event finishes, so they never guess at
/// start/end times, survive app downtime, and can be retried while the
/// archive retains the window. Live recordings capture the stream in
/// real time and remain the fallback for channels without an archive.
///
/// Catchup/timeshift download method ported from timeshifter by
/// scottrobertson (github.com/scottrobertson/timeshifter).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DvrRecordingMethod
{
    /// <summary>
    /// Real-time capture of the live stream during the scheduled window.
    /// </summary>
    Live = 0,

    /// <summary>
    /// Download from the provider's catchup/timeshift archive after the
    /// event has finished airing.
    /// </summary>
    Catchup = 1
}

/// <summary>
/// IPTV source configuration (M3U playlist or Xtream account)
/// Similar to how Dispatcharr manages M3U accounts
/// </summary>
public class IptvSource
{
    public int Id { get; set; }

    /// <summary>
    /// Display name for this IPTV source
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Type of IPTV source (M3U or Xtream)
    /// </summary>
    public IptvSourceType Type { get; set; }

    /// <summary>
    /// URL for M3U playlist, or server URL for Xtream
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Username for Xtream Codes API (null for M3U)
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for Xtream Codes API (null for M3U)
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Maximum concurrent streams allowed by this source
    /// </summary>
    public int MaxStreams { get; set; } = 1;

    /// <summary>
    /// Whether this source is active and should be used
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Number of channels imported from this source
    /// </summary>
    public int ChannelCount { get; set; }

    /// <summary>
    /// User-Agent string to use when fetching streams
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// When this source was added
    /// </summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the channel list was last updated
    /// </summary>
    public DateTime? LastUpdated { get; set; }

    /// <summary>
    /// Last error message (if any)
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Which timeshift URL style this provider's catchup endpoint
    /// actually serves ("path" or "php"), learned automatically from the
    /// first successful catchup download. Null until detected. With the
    /// catchup URL-style setting on "auto" (the default), downloads try
    /// the detected style first and fall back to the other, so users
    /// never need to know which kind of panel their provider runs.
    /// </summary>
    public string? DetectedCatchupMode { get; set; }

    /// <summary>
    /// Navigation property for channels
    /// </summary>
    public List<IptvChannel> Channels { get; set; } = new();
}

/// <summary>
/// IPTV channel parsed from M3U or Xtream source
/// </summary>
public class IptvChannel
{
    public int Id { get; set; }

    /// <summary>
    /// Source this channel belongs to
    /// </summary>
    public int SourceId { get; set; }
    public IptvSource? Source { get; set; }

    /// <summary>
    /// Channel name from playlist
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Channel number (if provided in playlist)
    /// </summary>
    public int? ChannelNumber { get; set; }

    /// <summary>
    /// Stream URL
    /// </summary>
    public required string StreamUrl { get; set; }

    /// <summary>
    /// Channel logo URL
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Group/category from playlist (e.g., "Sports", "News", "Entertainment")
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// TVG-ID for EPG matching
    /// </summary>
    public string? TvgId { get; set; }

    /// <summary>
    /// TVG-Name for EPG matching
    /// </summary>
    public string? TvgName { get; set; }

    /// <summary>
    /// Whether this is detected as a sports channel
    /// </summary>
    public bool IsSportsChannel { get; set; }

    /// <summary>
    /// Current connection status
    /// </summary>
    public IptvChannelStatus Status { get; set; } = IptvChannelStatus.Unknown;

    /// <summary>
    /// Last time the channel was tested for connectivity
    /// </summary>
    public DateTime? LastChecked { get; set; }

    /// <summary>
    /// Last error message when testing connection
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Whether this channel is enabled for recording
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether this channel is marked as a favorite for quick access
    /// </summary>
    public bool IsFavorite { get; set; } = false;

    /// <summary>
    /// Whether this channel is hidden from the channel list
    /// Hidden channels are not shown in UI but still exist in database
    /// </summary>
    public bool IsHidden { get; set; } = false;

    /// <summary>
    /// Country/region code (e.g., "US", "UK")
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "es")
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// When this channel was added
    /// </summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Detected quality label (e.g., "SD", "HD", "FHD", "4K")
    /// Parsed from channel name if available
    /// </summary>
    public string? DetectedQuality { get; set; }

    /// <summary>
    /// Quality score for ranking channels (higher = better)
    /// 100 = SD, 200 = HD, 300 = FHD, 400 = 4K
    /// </summary>
    public int QualityScore { get; set; } = 200; // Default to HD

    /// <summary>
    /// Detected TV network/broadcaster (e.g., "ESPN", "Sky Sports")
    /// Used for auto-mapping to leagues
    /// </summary>
    public string? DetectedNetwork { get; set; }

    /// <summary>
    /// Canonical iptv-org channel id (format "Name.cc", e.g.
    /// "ESPN.us", "SkySportsMainEvent.uk"). Populated by the
    /// IptvOrgSyncService when it can match this channel against the
    /// public iptv-org/database. Stable across provider rebrands so
    /// it makes a reliable join key for EPG, logo, and country
    /// lookups even when the user's M3U omits tvg-id entirely.
    /// </summary>
    public string? IptvOrgId { get; set; }

    /// <summary>
    /// Confidence (0-100) that IptvOrgId is the right canonical
    /// match for this channel, as scored by the multi-tier matcher.
    /// Useful for UI hints ("This match is 70% confident; click to
    /// override") and for downstream consumers that want to gate on
    /// a high-confidence match before relying on the canonical id.
    /// </summary>
    public int? IptvOrgConfidence { get; set; }

    /// <summary>
    /// Whether the provider keeps a catchup/timeshift archive for this
    /// channel (Xtream "tv_archive"). When true, finished events can be
    /// downloaded from the provider's archive after they air instead of
    /// being captured live, which removes the guesswork around start
    /// and end times entirely.
    /// </summary>
    public bool HasArchive { get; set; }

    /// <summary>
    /// How many days back the provider's catchup archive reaches for
    /// this channel (Xtream "tv_archive_duration"). 0 when HasArchive
    /// is false. A catchup download is only possible while the
    /// recording window is still inside this retention period.
    /// </summary>
    public int ArchiveDays { get; set; }

    /// <summary>
    /// Navigation property for league mappings
    /// </summary>
    public List<ChannelLeagueMapping> LeagueMappings { get; set; } = new();
}

/// <summary>
/// Mapping between IPTV channels and leagues
/// Allows users to specify which channels broadcast which leagues
/// </summary>
public class ChannelLeagueMapping
{
    public int Id { get; set; }

    /// <summary>
    /// The IPTV channel
    /// </summary>
    public int ChannelId { get; set; }
    public IptvChannel? Channel { get; set; }

    /// <summary>
    /// The league that broadcasts on this channel
    /// </summary>
    public int LeagueId { get; set; }
    public League? League { get; set; }

    /// <summary>
    /// Whether this is the preferred channel for this league
    /// (used when multiple channels broadcast the same league)
    /// </summary>
    public bool IsPreferred { get; set; }

    /// <summary>
    /// Priority for this mapping (higher = more preferred)
    /// </summary>
    public int Priority { get; set; } = 1;

    /// <summary>
    /// Confidence score (0-100) computed from the stack of signals that
    /// produced this mapping. Phase 1 adds tvg-id / iptv-org / EPG /
    /// country / network-keyword signals, each contributing a slice of
    /// the score. Used by EventChannelResolverService when picking
    /// among multiple mapped channels and by the explain endpoint to
    /// show why a mapping exists.
    /// </summary>
    public int Confidence { get; set; }

    /// <summary>
    /// When true, the auto-mapper never overwrites or deletes this row.
    /// Set by the admin UI when a user manually maps / unmaps a channel
    /// to a league — that decision becomes durable across every future
    /// re-run of AutoMapAllChannelsAsync. Auto-mapped rows have this
    /// flag false and get refreshed on every run.
    /// </summary>
    public bool IsManual { get; set; }

    /// <summary>
    /// JSON-encoded list of mapping signals that contributed to this
    /// row. Shape: [{"kind": "tvg_id", "score": 30, "detail": "ESPN.us"},
    /// {"kind": "epg_category", "score": 20, "detail": "92 / 124 sport
    /// programs"}]. Powers the /channels/{id}/mapping-explain endpoint
    /// so admins can see why the auto-mapper made a decision.
    /// </summary>
    public string? MappingSignals { get; set; }

    /// <summary>
    /// Last time the auto-mapper touched this row. Helps the bot's
    /// coverage report flag stale mappings whose source data has
    /// changed since the mapping was computed.
    /// </summary>
    public DateTime? LastAutoMapped { get; set; }

    /// <summary>
    /// When this mapping was created
    /// </summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// DVR recording job for IPTV streams
/// </summary>
public class DvrRecording
{
    public int Id { get; set; }

    /// <summary>
    /// The event being recorded (optional - can record without event association)
    /// </summary>
    public int? EventId { get; set; }
    public Event? Event { get; set; }

    /// <summary>
    /// The channel to record from
    /// </summary>
    public int ChannelId { get; set; }
    public IptvChannel? Channel { get; set; }

    /// <summary>
    /// Recording title (auto-generated or user-specified)
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Scheduled start time
    /// </summary>
    public DateTime ScheduledStart { get; set; }

    /// <summary>
    /// Scheduled end time
    /// </summary>
    public DateTime ScheduledEnd { get; set; }

    /// <summary>
    /// Minutes to start recording before scheduled time
    /// </summary>
    public int PrePadding { get; set; } = 5;

    /// <summary>
    /// Minutes to continue recording after scheduled time
    /// </summary>
    public int PostPadding { get; set; } = 15;

    /// <summary>
    /// Actual start time (when recording actually started)
    /// </summary>
    public DateTime? ActualStart { get; set; }

    /// <summary>
    /// Actual end time (when recording actually stopped)
    /// </summary>
    public DateTime? ActualEnd { get; set; }

    /// <summary>
    /// Current recording status
    /// </summary>
    public DvrRecordingStatus Status { get; set; } = DvrRecordingStatus.Scheduled;

    /// <summary>
    /// Path to the output file
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// File size in bytes (updated during recording)
    /// </summary>
    public long? FileSize { get; set; }

    /// <summary>
    /// Current bitrate in bytes per second (during recording)
    /// </summary>
    public long? CurrentBitrate { get; set; }

    /// <summary>
    /// Average bitrate in bytes per second
    /// </summary>
    public long? AverageBitrate { get; set; }

    /// <summary>
    /// Duration recorded in seconds
    /// </summary>
    public int? DurationSeconds { get; set; }

    /// <summary>
    /// Error message if recording failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Part name for multi-part events (e.g., "Main Card", "Prelims")
    /// </summary>
    public string? PartName { get; set; }

    /// <summary>
    /// Quality detected from stream (e.g., "HDTV-1080p")
    /// </summary>
    public string? Quality { get; set; }

    /// <summary>
    /// Quality score based on profile position (for upgrade comparison)
    /// </summary>
    public int? QualityScore { get; set; }

    /// <summary>
    /// Custom format score based on matched formats
    /// </summary>
    public int? CustomFormatScore { get; set; }

    /// <summary>
    /// Video resolution width in pixels
    /// </summary>
    public int? VideoWidth { get; set; }

    /// <summary>
    /// Video resolution height in pixels
    /// </summary>
    public int? VideoHeight { get; set; }

    /// <summary>
    /// Video codec (e.g., "H.264", "HEVC")
    /// </summary>
    public string? VideoCodec { get; set; }

    /// <summary>
    /// Audio codec (e.g., "AAC", "AC3")
    /// </summary>
    public string? AudioCodec { get; set; }

    /// <summary>
    /// Number of audio channels
    /// </summary>
    public int? AudioChannels { get; set; }

    /// <summary>
    /// When this recording was created
    /// </summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this recording was last updated
    /// </summary>
    public DateTime? LastUpdated { get; set; }

    /// <summary>
    /// When this recording was imported to the library
    /// </summary>
    public DateTime? ImportedAt { get; set; }

    /// <summary>
    /// JSON-encoded ranked list of backup channel IDs the auto-scheduler
    /// captured from EventChannelResolverService at scheduling time.
    /// When the primary channel's recording fails (stream drops, codec
    /// error, channel offline), DvrRecordingService re-schedules onto
    /// the next id in this list. Shape: "[42, 17, 91]" (primary not
    /// included — that's ChannelId).
    /// </summary>
    public string? FallbackChannelIds { get; set; }

    /// <summary>
    /// How many times this recording has been auto-rotated to a fallback
    /// channel. Stops the rotation from looping indefinitely if every
    /// backup also fails — once we exhaust FallbackChannelIds the
    /// recording lands in Failed status and the user gets a notification
    /// instead of yet another silent retry.
    /// </summary>
    public int AutoRetryCount { get; set; }

    /// <summary>
    /// How this recording acquires content: Live captures the stream in
    /// real time during the scheduled window; Catchup downloads the
    /// already-aired window from the provider's timeshift archive after
    /// the event ends. Catchup rows are handled exclusively by
    /// CatchupDownloadService — the live scheduler and watchdog ignore
    /// them (their wall-clock rules assume a live window).
    /// </summary>
    public DvrRecordingMethod Method { get; set; } = DvrRecordingMethod.Live;
}

/// <summary>
/// EPG (Electronic Program Guide) source configuration
/// </summary>
public class EpgSource
{
    public int Id { get; set; }

    /// <summary>
    /// Display name for this EPG source
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// URL to the XMLTV EPG file
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Whether this source is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this source was added
    /// </summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the EPG data was last updated
    /// </summary>
    public DateTime? LastUpdated { get; set; }

    /// <summary>
    /// Last error message (if any)
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Number of programs loaded from this source
    /// </summary>
    public int ProgramCount { get; set; }
}

/// <summary>
/// EPG channel entry (from XMLTV)
/// Used for auto-mapping IPTV channels to EPG programs
/// </summary>
public class EpgChannel
{
    public int Id { get; set; }

    /// <summary>
    /// EPG source this channel came from
    /// </summary>
    public int EpgSourceId { get; set; }
    public EpgSource? EpgSource { get; set; }

    /// <summary>
    /// Channel ID from XMLTV (used to match programs)
    /// </summary>
    public required string ChannelId { get; set; }

    /// <summary>
    /// Display name from XMLTV
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Normalized name for matching (lowercase, no special chars)
    /// </summary>
    public string? NormalizedName { get; set; }

    /// <summary>
    /// Icon/logo URL from XMLTV
    /// </summary>
    public string? IconUrl { get; set; }
}

/// <summary>
/// EPG program entry (from XMLTV)
/// </summary>
public class EpgProgram
{
    public int Id { get; set; }

    /// <summary>
    /// EPG source this program came from
    /// </summary>
    public int EpgSourceId { get; set; }
    public EpgSource? EpgSource { get; set; }

    /// <summary>
    /// Channel ID from XMLTV (matches IptvChannel.TvgId)
    /// </summary>
    public required string ChannelId { get; set; }

    /// <summary>
    /// Program title
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Program description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Category/genre (e.g., "Sports", "Football")
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Start time
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// End time
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Icon/thumbnail URL
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Whether this is detected as a sports program
    /// </summary>
    public bool IsSportsProgram { get; set; }

    /// <summary>
    /// Matched event ID (if we found a matching Sportarr event)
    /// </summary>
    public int? MatchedEventId { get; set; }
    public Event? MatchedEvent { get; set; }

    /// <summary>
    /// Confidence of the event match (0-100)
    /// </summary>
    public int MatchConfidence { get; set; }
}

// ============================================================================
// Request/Response DTOs
// ============================================================================

/// <summary>
/// Request to add a new IPTV source
/// </summary>
/// <summary>Request body for deleting several IPTV sources at once.</summary>
public class BulkDeleteSourcesRequest
{
    public List<int>? Ids { get; set; }
}

public class AddIptvSourceRequest
{
    public required string Name { get; set; }
    public IptvSourceType Type { get; set; }
    public required string Url { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int MaxStreams { get; set; } = 1;
    public string? UserAgent { get; set; }

    public IptvSource ToEntity()
    {
        return new IptvSource
        {
            Name = Name,
            Type = Type,
            Url = Url,
            Username = Username,
            Password = Password,
            MaxStreams = MaxStreams,
            UserAgent = UserAgent,
            IsActive = true,
            Created = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Response for IPTV source
/// </summary>
public class IptvSourceResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public IptvSourceType Type { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Username { get; set; }
    /// <summary>
    /// Indicates whether this source has a password configured (for UI to show masked field)
    /// </summary>
    public bool HasPassword { get; set; }
    public int MaxStreams { get; set; }
    public bool IsActive { get; set; }
    public int ChannelCount { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Created { get; set; }
    public DateTime? LastUpdated { get; set; }
    public string? LastError { get; set; }

    public static IptvSourceResponse FromEntity(IptvSource source)
    {
        return new IptvSourceResponse
        {
            Id = source.Id,
            Name = source.Name,
            Type = source.Type,
            Url = source.Url,
            Username = source.Username,
            HasPassword = !string.IsNullOrEmpty(source.Password),
            MaxStreams = source.MaxStreams,
            IsActive = source.IsActive,
            ChannelCount = source.ChannelCount,
            UserAgent = source.UserAgent,
            Created = source.Created,
            LastUpdated = source.LastUpdated,
            LastError = source.LastError
        };
    }
}

/// <summary>
/// Response for IPTV channel
/// </summary>
public class IptvChannelResponse
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ChannelNumber { get; set; }
    public string StreamUrl { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? Group { get; set; }
    public string? TvgId { get; set; }
    public bool IsSportsChannel { get; set; }
    public IptvChannelStatus Status { get; set; }
    public DateTime? LastChecked { get; set; }
    public bool IsEnabled { get; set; }
    public string? Country { get; set; }
    public string? Language { get; set; }
    public string? DetectedQuality { get; set; }
    public int QualityScore { get; set; }
    public string? DetectedNetwork { get; set; }
    public List<int> MappedLeagueIds { get; set; } = new();
    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }

    public static IptvChannelResponse FromEntity(IptvChannel channel)
    {
        return new IptvChannelResponse
        {
            Id = channel.Id,
            SourceId = channel.SourceId,
            Name = channel.Name,
            ChannelNumber = channel.ChannelNumber,
            StreamUrl = channel.StreamUrl,
            LogoUrl = channel.LogoUrl,
            Group = channel.Group,
            TvgId = channel.TvgId,
            IsSportsChannel = channel.IsSportsChannel,
            Status = channel.Status,
            LastChecked = channel.LastChecked,
            IsEnabled = channel.IsEnabled,
            Country = channel.Country,
            Language = channel.Language,
            DetectedQuality = channel.DetectedQuality,
            QualityScore = channel.QualityScore,
            DetectedNetwork = channel.DetectedNetwork,
            MappedLeagueIds = channel.LeagueMappings?.Select(m => m.LeagueId).ToList() ?? new(),
            IsFavorite = channel.IsFavorite,
            IsHidden = channel.IsHidden
        };
    }
}

/// <summary>
/// Request to map a channel to leagues
/// </summary>
public class MapChannelToLeaguesRequest
{
    public int ChannelId { get; set; }
    public List<int> LeagueIds { get; set; } = new();
    public int? PreferredLeagueId { get; set; }
}

/// <summary>
/// Request to add an EPG source
/// </summary>
public class AddEpgSourceRequest
{
    public required string Name { get; set; }
    public required string Url { get; set; }
    public bool IsActive { get; set; } = true;

    public EpgSource ToEntity()
    {
        return new EpgSource
        {
            Name = Name,
            Url = Url,
            IsActive = IsActive,
            Created = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Response for DVR recording
/// </summary>
public class DvrRecordingResponse
{
    public int Id { get; set; }
    public int? EventId { get; set; }
    public string? EventTitle { get; set; }
    public int? LeagueId { get; set; }
    public string? LeagueName { get; set; }
    public int ChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public int PrePadding { get; set; }
    public int PostPadding { get; set; }
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    public DvrRecordingStatus Status { get; set; }
    public string? OutputPath { get; set; }
    public long? FileSize { get; set; }
    public long? CurrentBitrate { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PartName { get; set; }
    public string? Quality { get; set; }
    public int? QualityScore { get; set; }
    public int? CustomFormatScore { get; set; }
    public string? Resolution { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public int? AudioChannels { get; set; }
    public DvrRecordingMethod Method { get; set; }
    public DateTime Created { get; set; }

    // Expected scores for scheduled recordings (based on DVR profile settings)
    public int? ExpectedQualityScore { get; set; }
    public int? ExpectedCustomFormatScore { get; set; }
    public int? ExpectedTotalScore { get; set; }
    public string? ExpectedQualityName { get; set; }
    public string? ExpectedFormatDescription { get; set; }
    public List<string>? ExpectedMatchedFormats { get; set; }

    public static DvrRecordingResponse FromEntity(DvrRecording recording)
    {
        return new DvrRecordingResponse
        {
            Id = recording.Id,
            EventId = recording.EventId,
            EventTitle = recording.Event?.Title,
            LeagueId = recording.Event?.LeagueId,
            LeagueName = recording.Event?.League?.Name,
            ChannelId = recording.ChannelId,
            ChannelName = recording.Channel?.Name ?? string.Empty,
            Title = recording.Title,
            ScheduledStart = recording.ScheduledStart,
            ScheduledEnd = recording.ScheduledEnd,
            PrePadding = recording.PrePadding,
            PostPadding = recording.PostPadding,
            ActualStart = recording.ActualStart,
            ActualEnd = recording.ActualEnd,
            Status = recording.Status,
            OutputPath = recording.OutputPath,
            FileSize = recording.FileSize,
            CurrentBitrate = recording.CurrentBitrate,
            DurationSeconds = recording.DurationSeconds,
            ErrorMessage = recording.ErrorMessage,
            PartName = recording.PartName,
            Quality = recording.Quality,
            QualityScore = recording.QualityScore,
            CustomFormatScore = recording.CustomFormatScore,
            Resolution = recording.VideoHeight.HasValue ? $"{recording.VideoWidth}x{recording.VideoHeight}" : null,
            VideoCodec = recording.VideoCodec,
            AudioCodec = recording.AudioCodec,
            AudioChannels = recording.AudioChannels,
            Method = recording.Method,
            Created = recording.Created
        };
    }
}

/// <summary>
/// Request to schedule a DVR recording
/// </summary>
public class ScheduleDvrRecordingRequest
{
    public int? EventId { get; set; }
    public int ChannelId { get; set; }
    public string? Title { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public int PrePadding { get; set; } = 5;
    public int PostPadding { get; set; } = 15;
    public string? PartName { get; set; }

    /// <summary>
    /// Live (default) records in real time; Catchup defers to the
    /// provider's timeshift archive after the event ends.
    /// </summary>
    public DvrRecordingMethod Method { get; set; } = DvrRecordingMethod.Live;
}
