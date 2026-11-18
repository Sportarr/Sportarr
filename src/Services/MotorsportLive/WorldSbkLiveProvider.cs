using System.Text.Json;
using System.Text.RegularExpressions;
using Sportarr.Api.Constants;

namespace Sportarr.Api.Services.MotorsportLive;

/// <summary>
/// WorldSBK, WorldSSP and WorldSPB from the timing service of worldsbk.com.
/// The round list gives the round. The round page gives each session.
/// </summary>
public sealed class WorldSbkLiveProvider : IMotorsportLiveProvider
{
    private const string DefaultBaseUrl = "https://api.pulselive.worldsbk.com";

    private static readonly Regex Code = new(@"^(?<code>RC|FP|WUP)(?<n>\d)?$", RegexOptions.Compiled);

    private readonly MotorsportTimingFetcher _fetcher;
    private readonly TimeProvider _time;
    private readonly string _baseUrl;

    public WorldSbkLiveProvider(MotorsportTimingFetcher fetcher, TimeProvider? time = null)
    {
        _fetcher = fetcher;
        _time = time ?? TimeProvider.System;
        _baseUrl = MotorsportTimingUrls.Resolve(EnvironmentVariableNames.TestWorldSbkTimingUrl, DefaultBaseUrl);
    }

    public string Source => "worldsbk.com";

    public bool Supports(MotorsportSeries series) =>
        series is MotorsportSeries.WorldSbk or MotorsportSeries.WorldSsp or MotorsportSeries.WorldSpb;

    private static string CategoryCode(MotorsportSeries series) => series switch
    {
        MotorsportSeries.WorldSsp => "SSP",
        MotorsportSeries.WorldSpb => "SPB",
        _ => "SBK",
    };

    public async Task<MotorsportSourceState?> GetSessionStateAsync(MotorsportSessionQuery query, CancellationToken cancellationToken)
    {
        var year = query.Anchor.UtcDateTime.Year;
        var rounds = await _fetcher.GetAsync($"{_baseUrl}/wsbk-events/v1/seasons/{year}/rounds",
            MotorsportTimingFetcher.CalendarTtl, cancellationToken);
        if (rounds == null)
            return null;

        var category = CategoryCode(query.Series);
        // The end date is midnight at the start of the last day.
        var inRange = ReadRounds(rounds.Text, category)
            .Where(r => query.Anchor >= r.Start.AddDays(-1) && query.Anchor < r.End.AddDays(2))
            .OrderByDescending(r => MotorsportSessionMatching.LocationOverlap(r.Name, query.Location))
            .ToList();
        if (inRange.Count == 0)
            return null;

        var round = inRange[0];
        var sessions = await _fetcher.GetAsync(
            $"{_baseUrl}/wsbk-events/v1/seasons/{year}/rounds/{Uri.EscapeDataString(round.Code)}/sessions",
            MotorsportTimingFetcher.LiveTtl, cancellationToken);
        if (sessions == null)
            return null;

        var match = MotorsportSessionMatching.Pick(ReadSessions(sessions.Text, category, round.Code), query);
        if (match == null)
            return null;
        var (phase, finishedAt) = MotorsportSessionMatching.Combine(match.Parts, _time.GetUtcNow());
        return new MotorsportSourceState(phase, match.Key, match.Name, finishedAt, sessions.FetchedAt, match.Start);
    }

    private sealed record Round(string Code, string Name, DateTimeOffset Start, DateTimeOffset End);

    private static List<Round> ReadRounds(string json, string category)
    {
        var result = new List<Round>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var r in data.EnumerateArray())
        {
            if (!r.TryGetProperty("attributes", out var a))
                continue;
            var code = Str(a, "source_id");
            var start = MotorsportSessionMatching.ParseTime(Str(a, "start_date"));
            var end = MotorsportSessionMatching.ParseTime(Str(a, "end_date"));
            if (code == null || start == null || end == null || !RunsCategory(r, category))
                continue;
            result.Add(new Round(code, $"{Str(a, "name")} {Str(a, "description")}", start.Value, end.Value));
        }
        return result;
    }

    private static bool RunsCategory(JsonElement round, string category)
    {
        if (!round.TryGetProperty("relationships", out var rel) || !rel.TryGetProperty("categories", out var cats) ||
            !cats.TryGetProperty("data", out var list) || list.ValueKind != JsonValueKind.Array)
            return true;
        return list.EnumerateArray().Any(c => Str(c, "id") == category);
    }

    internal static List<SessionCandidate> ReadSessions(string json, string category, string roundCode)
    {
        using var doc = JsonDocument.Parse(json);
        var parts = new List<(MotorsportSessionKind Kind, int? Number, SessionPart Part)>();
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return new List<SessionCandidate>();

        foreach (var s in data.EnumerateArray())
        {
            if (!s.TryGetProperty("attributes", out var a) || SessionCategory(s) != category)
                continue;
            var code = Str(a, "short_name")?.Trim() ?? "";
            var session = ReadCode(code);
            var start = MotorsportSessionMatching.ParseTime(Str(a, "start_date_utc"));
            if (session == null || start == null)
                continue;
            var name = Str(a, "description")?.Trim() ?? code;
            // The stopped part of a red-flagged race gets the suffix. The
            // restart keeps the plain name.
            var redFlag = name.Contains("red flag", StringComparison.OrdinalIgnoreCase);
            parts.Add((session.Value.Kind, session.Value.Number, new SessionPart(
                Str(s, "id") ?? code, $"{category} {name}", MotogpStyleStatus(Str(a, "status")),
                start.Value, MotorsportSessionMatching.ParseTime(Str(a, "effective_end_date_utc")), redFlag)));
        }

        return parts
            .GroupBy(p => (p.Kind, p.Number))
            .Select(g =>
            {
                var named = g.FirstOrDefault(p => !p.Part.IsRedFlagPart).Part ?? g.First().Part;
                return new SessionCandidate(g.Key.Kind, g.Key.Number,
                    $"{roundCode}:{category}:{g.Key.Kind}:{g.Key.Number}", $"{roundCode} {named.Name}",
                    g.Select(p => p.Part).ToList());
            })
            .ToList();
    }

    private static string? SessionCategory(JsonElement session) =>
        session.TryGetProperty("relationships", out var rel) && rel.TryGetProperty("category", out var cat) &&
        cat.TryGetProperty("data", out var data) ? Str(data, "id") : null;

    private static (MotorsportSessionKind Kind, int? Number)? ReadCode(string code)
    {
        if (code == "SPRC")
            return (MotorsportSessionKind.SuperpoleRace, null);
        if (code == "SP")
            return (MotorsportSessionKind.Superpole, null);
        var match = Code.Match(code);
        if (!match.Success)
            return null;
        int? number = match.Groups["n"].Success ? int.Parse(match.Groups["n"].Value) : null;
        return match.Groups["code"].Value switch
        {
            "RC" when number != null => (MotorsportSessionKind.Race, number),
            "FP" => (MotorsportSessionKind.Practice, number),
            "WUP" => (MotorsportSessionKind.WarmUp, number),
            _ => null,
        };
    }

    // This service uses the same three status words as motogp.com.
    private static SessionPartStatus MotogpStyleStatus(string? status) => MotoGpLiveProvider.ReadStatus(status);

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
