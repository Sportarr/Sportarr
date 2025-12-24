using System.Text.Json.Serialization;

namespace Sportarr.Api.Models;

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
    /// Quality detected from stream
    /// </summary>
    public string? Quality { get; set; }

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
    public List<int> MappedLeagueIds { get; set; } = new();

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
            MappedLeagueIds = channel.LeagueMappings?.Select(m => m.LeagueId).ToList() ?? new()
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

    public EpgSource ToEntity()
    {
        return new EpgSource
        {
            Name = Name,
            Url = Url,
            IsActive = true,
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
    public DateTime Created { get; set; }

    public static DvrRecordingResponse FromEntity(DvrRecording recording)
    {
        return new DvrRecordingResponse
        {
            Id = recording.Id,
            EventId = recording.EventId,
            EventTitle = recording.Event?.Title,
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
}
