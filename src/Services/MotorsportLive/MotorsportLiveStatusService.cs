using Sportarr.Api.Models;

namespace Sportarr.Api.Services.MotorsportLive;

public interface IMotorsportLiveStatusService
{
    /// <summary>The series of a league, or null when no timing source covers it.</summary>
    MotorsportSeries? ResolveSeries(League? league);

    /// <summary>
    /// The live state of the session that a recording captures, or null when
    /// the league or the session has no timing source.
    /// </summary>
    Task<MotorsportLiveStatus?> GetStatusAsync(Event evt, DvrRecording recording, CancellationToken cancellationToken);
}

/// <summary>
/// Asks the timing source of a series about the session of a recording. It
/// also keeps the first time a finish was seen, for sources that give no
/// finish time.
/// </summary>
public sealed class MotorsportLiveStatusService : IMotorsportLiveStatusService
{
    private static readonly TimeSpan ConfirmationGap = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan TrackRetention = TimeSpan.FromHours(12);

    // A cached page can be up to one cache period older than the start.
    private static readonly TimeSpan StartTolerance = TimeSpan.FromMinutes(2);

    // A recording that starts this long after the session start shows a
    // delayed broadcast. The live finish does not end it.
    private static readonly TimeSpan DelayedBroadcastThreshold = TimeSpan.FromMinutes(15);

    private sealed class Track
    {
        public DateTimeOffset? FirstFinishedFetch;
        public DateTimeOffset? LastUnfinishedFetch;
        public DateTimeOffset? FirstLiveFetch;
        public bool SawNotStarted;
        public DateTimeOffset LastUsed;
    }

    private readonly IReadOnlyList<IMotorsportLiveProvider> _providers;
    private readonly ILogger<MotorsportLiveStatusService> _logger;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, Track> _tracks = new();
    private readonly object _gate = new();

    public MotorsportLiveStatusService(IEnumerable<IMotorsportLiveProvider> providers,
        ILogger<MotorsportLiveStatusService> logger, TimeProvider? time = null)
    {
        _providers = providers.ToList();
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public MotorsportSeries? ResolveSeries(League? league) =>
        league == null ? null : MotorsportSeriesMap.Resolve(league.Name, league.AlternateName);

    public async Task<MotorsportLiveStatus?> GetStatusAsync(Event evt, DvrRecording recording, CancellationToken cancellationToken)
    {
        var series = ResolveSeries(evt.League);
        if (series == null)
            return null;

        var anchor = new DateTimeOffset(DateTime.SpecifyKind(recording.ScheduledStart, DateTimeKind.Utc));
        var query = MotorsportSeriesMap.DescribeSession(series.Value, evt.Title, anchor, evt.Venue ?? evt.Location);
        var provider = _providers.FirstOrDefault(p => p.Supports(series.Value));
        if (query == null || provider == null)
        {
            _logger.LogDebug("[DVR] Series timing: '{Title}' names no session that a timing source reports", evt.Title);
            return null;
        }

        MotorsportSourceState? state;
        try
        {
            state = await provider.GetSessionStateAsync(query, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[DVR] Series timing {Source} could not be read: {Error}", provider.Source, ex.Message);
            state = null;
        }

        if (state == null)
        {
            _logger.LogDebug("[DVR] Series timing {Source}: no {Kind} {Number} found for '{Title}'",
                provider.Source, query.Kind, query.Number, evt.Title);
            return MotorsportLiveStatus.Unknown(provider.Source);
        }

        // British Superbikes results give no session times. The event time
        // stands in, so the delayed broadcast check still runs. A date with
        // no time of day says nothing about the start, so it is not used.
        if (state.SessionStart == null && series == MotorsportSeries.BritishSuperbike &&
            evt.EventDate.TimeOfDay != TimeSpan.Zero)
            state = state with { SessionStart = new DateTimeOffset(DateTime.SpecifyKind(evt.EventDate, DateTimeKind.Utc)) };

        var status = Remember(provider.Source, query, state, anchor, recording.ActualStart);
        _logger.LogDebug("[DVR] Series timing {Source}: '{Title}' matched {Session}: {Phase}",
            provider.Source, evt.Title, status.SessionName, status.Phase);
        return status;
    }

    private MotorsportLiveStatus Remember(string source, MotorsportSessionQuery query, MotorsportSourceState state,
        DateTimeOffset anchor, DateTime? actualStart)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            foreach (var stale in _tracks.Where(t => now - t.Value.LastUsed > TrackRetention).Select(t => t.Key).ToList())
                _tracks.Remove(stale);

            var key = $"{source}|{state.SessionKey}";
            if (!_tracks.TryGetValue(key, out var track))
                _tracks[key] = track = new Track();
            track.LastUsed = now;

            if (state.Phase == MotorsportLivePhase.NotStarted)
                track.SawNotStarted = true;
            if (state.Phase == MotorsportLivePhase.Live &&
                (track.FirstLiveFetch == null || state.FetchedAt < track.FirstLiveFetch))
                track.FirstLiveFetch = state.FetchedAt;

            if (state.Phase != MotorsportLivePhase.Finished)
            {
                track.FirstFinishedFetch = null;
                if (track.LastUnfinishedFetch == null || state.FetchedAt > track.LastUnfinishedFetch)
                    track.LastUnfinishedFetch = state.FetchedAt;
                return new MotorsportLiveStatus(state.Phase, source, state.SessionName, null, false, false, false,
                    state.SessionStart);
            }

            if (track.FirstFinishedFetch == null || state.FetchedAt < track.FirstFinishedFetch)
                track.FirstFinishedFetch = state.FetchedAt;

            var finishedAt = state.FinishedAt ?? track.FirstFinishedFetch.Value;

            // A race that stops well short of its length had a red flag. A
            // restart can follow, so the race counts as live for a while.
            // After that wait, the state is unknown. It never becomes a finish.
            // The first live state marks the start only when the session was
            // seen before it began. A first look in mid race tells nothing.
            var startedAt = track.SawNotStarted && track.FirstLiveFetch.HasValue
                ? track.FirstLiveFetch
                : state.SessionStart;
            // Without a reported finish, the race can have ended any time after
            // the last unfinished state. The earliest time decides if it was
            // short, so a gap in the source cannot hide a red flag. The buffer
            // still counts from the first finished state.
            var earliestFinish = state.FinishedAt ?? track.LastUnfinishedFetch ?? finishedAt;
            if (EndedShort(query, startedAt, earliestFinish))
            {
                return now - finishedAt < MotorsportSessionMatching.RedFlagRestartWait
                    ? new MotorsportLiveStatus(MotorsportLivePhase.Live, source,
                        $"{state.SessionName} (stopped short, a restart can follow)", null, false, false, false,
                        state.SessionStart)
                    : new MotorsportLiveStatus(MotorsportLivePhase.Unknown, source,
                        $"{state.SessionName} (stopped short, no restart seen)", null, false, false, false,
                        state.SessionStart);
            }

            var confirmed = state.FetchedAt - track.FirstFinishedFetch.Value >= ConfirmationGap;

            // A finish before the recording began belongs to an earlier
            // broadcast, such as the live race before a delayed showing.
            var start = actualStart.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(actualStart.Value, DateTimeKind.Utc)) - StartTolerance
                : (DateTimeOffset?)null;
            var duringRecording = start.HasValue && (state.FinishedAt.HasValue
                ? state.FinishedAt.Value >= start.Value
                : track.LastUnfinishedFetch >= start.Value);
            if (state.SessionStart.HasValue && anchor > state.SessionStart.Value + DelayedBroadcastThreshold)
                duringRecording = false;

            return new MotorsportLiveStatus(MotorsportLivePhase.Finished, source, state.SessionName,
                finishedAt, state.FinishedAt.HasValue, confirmed, duringRecording, state.SessionStart);
        }
    }

    /// <summary>
    /// True when a race finished before about 80 percent of its usual length,
    /// counted from when it was seen to go live, or else from its scheduled
    /// start. A red flag that stops a race this early usually leads to a
    /// restart.
    /// </summary>
    internal static bool EndedShort(MotorsportSessionQuery query, DateTimeOffset? startedAt, DateTimeOffset finishedAt)
    {
        if (startedAt == null)
            return false;
        var minimum = MinimumRaceMinutes(query.Series, query.Kind);
        return minimum > 0 && (finishedAt - startedAt.Value).TotalMinutes < minimum;
    }

    // About 80 percent of a normal race of each class. The WorldSBK figures
    // come from the median race times of the 2026 captures. The MotoGP
    // service gives no finish times, so its figures are estimates from the
    // usual race lengths. Formula 1 and IndyCar races vary too much in
    // length for one limit. British Superbikes results give no times.
    private static int MinimumRaceMinutes(MotorsportSeries series, MotorsportSessionKind kind) => (series, kind) switch
    {
        (MotorsportSeries.MotoGp, MotorsportSessionKind.Race) => 33,
        (MotorsportSeries.Moto2 or MotorsportSeries.Moto3, MotorsportSessionKind.Race) => 30,
        (MotorsportSeries.MotoGp, MotorsportSessionKind.Sprint) => 16,
        (MotorsportSeries.WorldSbk, MotorsportSessionKind.Race) => 28,
        (MotorsportSeries.WorldSbk, MotorsportSessionKind.SuperpoleRace) => 14,
        (MotorsportSeries.WorldSsp, MotorsportSessionKind.Race) => 26,
        (MotorsportSeries.WorldSpb, MotorsportSessionKind.Race) => 18,
        _ => 0,
    };
}
