namespace Sportarr.Api.Services.MotorsportLive;

/// <summary>The series that have a timing source.</summary>
public enum MotorsportSeries
{
    MotoGp,
    Moto2,
    Moto3,
    WorldSbk,
    WorldSsp,
    WorldSpb,
    Formula1,
    IndyCar,
    BritishSuperbike,
}

public enum MotorsportSessionKind
{
    Race,
    Sprint,
    SuperpoleRace,
    Qualifying,
    SprintQualifying,
    Superpole,
    Practice,
    WarmUp,
}

public enum MotorsportLivePhase
{
    Unknown,
    NotStarted,
    Live,
    Finished,
}

/// <summary>
/// The session of one event, read from its title, plus the time the
/// recording expects it.
/// </summary>
public sealed record MotorsportSessionQuery(
    MotorsportSeries Series,
    MotorsportSessionKind Kind,
    int? Number,
    DateTimeOffset Anchor,
    string? Location);

/// <summary>
/// What a source says about the session that it matched. FinishedAt is the
/// finish time that the source gives, or null when it gives none.
/// SessionStart is the scheduled start, or null when the source gives none.
/// </summary>
public sealed record MotorsportSourceState(
    MotorsportLivePhase Phase,
    string SessionKey,
    string SessionName,
    DateTimeOffset? FinishedAt,
    DateTimeOffset FetchedAt,
    DateTimeOffset? SessionStart = null);

/// <summary>
/// The answer for one recording. FinishedAt is the reported finish time, or
/// the first time this app saw the finish. Confirmed is true when two
/// fetches at least one minute apart both show the finish.
/// FinishedDuringRecording is true when the recording shows the session
/// live. Then the recording started before the finish, and not long after
/// the session start. A delayed broadcast is not stopped by the live result.
/// SessionStart is the scheduled start, or null when the source gives none.
/// </summary>
public sealed record MotorsportLiveStatus(
    MotorsportLivePhase Phase,
    string Source,
    string? SessionName,
    DateTimeOffset? FinishedAt,
    bool FinishedAtReported,
    bool Confirmed,
    bool FinishedDuringRecording,
    DateTimeOffset? SessionStart = null)
{
    public static MotorsportLiveStatus Unknown(string source) =>
        new(MotorsportLivePhase.Unknown, source, null, null, false, false, false);
}

/// <summary>One timing source. A source fails quietly and returns null.</summary>
public interface IMotorsportLiveProvider
{
    /// <summary>Short source name for the log, such as "motogp.com".</summary>
    string Source { get; }

    bool Supports(MotorsportSeries series);

    Task<MotorsportSourceState?> GetSessionStateAsync(MotorsportSessionQuery query, CancellationToken cancellationToken);
}
