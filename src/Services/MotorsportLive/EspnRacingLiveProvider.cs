using System.Text.Json;
using Sportarr.Api.Constants;

namespace Sportarr.Api.Services.MotorsportLive;

/// <summary>
/// Formula 1 and IndyCar from the ESPN scoreboard. Each session of a race
/// weekend is one competition with its own status.
/// </summary>
public sealed class EspnRacingLiveProvider : IMotorsportLiveProvider
{
    private const string DefaultBaseUrl = "https://site.api.espn.com";

    private readonly MotorsportTimingFetcher _fetcher;
    private readonly TimeProvider _time;
    private readonly string _baseUrl;

    public EspnRacingLiveProvider(MotorsportTimingFetcher fetcher, TimeProvider? time = null)
    {
        _fetcher = fetcher;
        _time = time ?? TimeProvider.System;
        _baseUrl = MotorsportTimingUrls.Resolve(EnvironmentVariableNames.TestEspnTimingUrl, DefaultBaseUrl);
    }

    public string Source => "espn.com";

    public bool Supports(MotorsportSeries series) =>
        series is MotorsportSeries.Formula1 or MotorsportSeries.IndyCar;

    public async Task<MotorsportSourceState?> GetSessionStateAsync(MotorsportSessionQuery query, CancellationToken cancellationToken)
    {
        var path = query.Series == MotorsportSeries.IndyCar ? "irl" : "f1";
        var board = await _fetcher.GetAsync($"{_baseUrl}/apis/site/v2/sports/racing/{path}/scoreboard?dates={DateRange(query.Anchor)}",
            MotorsportTimingFetcher.LiveTtl, cancellationToken);
        if (board == null)
            return null;

        var match = MotorsportSessionMatching.Pick(ReadSessions(board.Text, query.Series), query);
        if (match == null)
            return null;
        var (phase, finishedAt) = MotorsportSessionMatching.Combine(match.Parts, _time.GetUtcNow());
        return new MotorsportSourceState(phase, match.Key, match.Name, finishedAt, board.FetchedAt, match.Start);
    }

    /// <summary>
    /// The dates value for a time. The board keys events on US Eastern days.
    /// A range leaves out its last day, and the Eastern day is the UTC day or
    /// the day before. So the range from the UTC day before to the UTC day
    /// after always holds the event. The 2026-11-18 captures show this.
    /// </summary>
    internal static string DateRange(DateTimeOffset time)
    {
        var day = time.UtcDateTime.Date;
        return $"{day.AddDays(-1):yyyyMMdd}-{day.AddDays(1):yyyyMMdd}";
    }

    internal static List<SessionCandidate> ReadSessions(string json, MotorsportSeries series)
    {
        var result = new List<SessionCandidate>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var e in events.EnumerateArray())
        {
            var eventName = Str(e, "name") ?? "";
            if (!e.TryGetProperty("competitions", out var competitions) || competitions.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var c in competitions.EnumerateArray())
            {
                var abbreviation = c.TryGetProperty("type", out var type) ? Str(type, "abbreviation") : null;
                var session = ReadType(abbreviation, series);
                var start = MotorsportSessionMatching.ParseTime(Str(c, "date") ?? Str(c, "startDate"));
                if (session == null || start == null)
                    continue;
                var status = ReadStatus(c);
                var id = Str(c, "id") ?? $"{eventName}:{abbreviation}";
                var label = abbreviation ?? "Race";
                result.Add(new SessionCandidate(session.Value.Kind, session.Value.Number, $"espn:{id}",
                    $"{eventName} {label}",
                    new[] { new SessionPart(id, label, status, start.Value, null, false) }));
            }
        }
        return result;
    }

    private static (MotorsportSessionKind Kind, int? Number)? ReadType(string? abbreviation, MotorsportSeries series) =>
        abbreviation switch
        {
            "FP1" => (MotorsportSessionKind.Practice, 1),
            "FP2" => (MotorsportSessionKind.Practice, 2),
            "FP3" => (MotorsportSessionKind.Practice, 3),
            "Qual" => (MotorsportSessionKind.Qualifying, null),
            "SS" => (MotorsportSessionKind.SprintQualifying, null),
            "SR" => (MotorsportSessionKind.Sprint, null),
            "Race" => (MotorsportSessionKind.Race, null),
            // The IndyCar board lists only races, and gives them no type.
            null when series == MotorsportSeries.IndyCar => (MotorsportSessionKind.Race, null),
            _ => null,
        };

    private static SessionPartStatus ReadStatus(JsonElement competition)
    {
        if (!competition.TryGetProperty("status", out var status) || !status.TryGetProperty("type", out var type))
            return SessionPartStatus.Unknown;
        var name = Str(type, "name") ?? "";
        if (name.Contains("CANCEL", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("POSTPONE", StringComparison.OrdinalIgnoreCase))
            return SessionPartStatus.Cancelled;

        var completed = type.TryGetProperty("completed", out var c) && c.ValueKind == JsonValueKind.True;
        return Str(type, "state") switch
        {
            "pre" => SessionPartStatus.NotStarted,
            "in" => SessionPartStatus.Live,
            "post" when completed => SessionPartStatus.Finished,
            _ => SessionPartStatus.Unknown,
        };
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
