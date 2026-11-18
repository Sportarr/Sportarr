using System.Text.Json;
using System.Text.RegularExpressions;
using Sportarr.Api.Constants;

namespace Sportarr.Api.Services.MotorsportLive;

/// <summary>
/// MotoGP, Moto2 and Moto3 from the timing service of motogp.com. The season
/// calendar gives the Grand Prix. The Grand Prix page gives the status of
/// each session.
/// </summary>
public sealed class MotoGpLiveProvider : IMotorsportLiveProvider
{
    private const string DefaultBaseUrl = "https://api.motogp.pulselive.com";

    private static readonly Regex RaceCode = new(@"^RAC\d*$", RegexOptions.Compiled);
    private static readonly Regex SprintCode = new(@"^SPR\d*$", RegexOptions.Compiled);
    private static readonly Regex NumberedCode = new(@"^(?<code>FP|Q|WUP)(?<n>\d)$", RegexOptions.Compiled);

    private readonly MotorsportTimingFetcher _fetcher;
    private readonly TimeProvider _time;
    private readonly string _baseUrl;

    public MotoGpLiveProvider(MotorsportTimingFetcher fetcher, TimeProvider? time = null)
    {
        _fetcher = fetcher;
        _time = time ?? TimeProvider.System;
        _baseUrl = MotorsportTimingUrls.Resolve(EnvironmentVariableNames.TestMotoGpTimingUrl, DefaultBaseUrl);
    }

    public string Source => "motogp.com";

    public bool Supports(MotorsportSeries series) =>
        series is MotorsportSeries.MotoGp or MotorsportSeries.Moto2 or MotorsportSeries.Moto3;

    private static string CategoryCode(MotorsportSeries series) => series switch
    {
        MotorsportSeries.Moto2 => "MT2",
        MotorsportSeries.Moto3 => "MT3",
        _ => "MGP",
    };

    public async Task<MotorsportSourceState?> GetSessionStateAsync(MotorsportSessionQuery query, CancellationToken cancellationToken)
    {
        var year = query.Anchor.UtcDateTime.Year;
        var calendar = await _fetcher.GetAsync($"{_baseUrl}/motogp/v1/events?seasonYear={year}",
            MotorsportTimingFetcher.CalendarTtl, cancellationToken);
        if (calendar == null)
            return null;

        var grandsPrix = ReadGrandsPrix(calendar.Text)
            .Where(gp => query.Anchor >= gp.Start.AddDays(-1) && query.Anchor <= gp.End.AddDays(1))
            .ToList();

        var category = CategoryCode(query.Series);
        var candidates = new List<SessionCandidate>();
        foreach (var gp in grandsPrix)
        {
            var detail = await _fetcher.GetAsync($"{_baseUrl}/motogp/v1/events/{Uri.EscapeDataString(gp.Id)}",
                MotorsportTimingFetcher.LiveTtl, cancellationToken);
            if (detail == null)
                return null;
            // Each page keeps its own fetch time. A cached page can be older.
            candidates.AddRange(ReadSessions(detail.Text, category, gp.ShortName)
                .Select(c => c with { FetchedAt = detail.FetchedAt }));
        }

        var match = MotorsportSessionMatching.Pick(candidates, query);
        if (match == null)
            return null;
        var (phase, finishedAt) = MotorsportSessionMatching.Combine(match.Parts, _time.GetUtcNow());
        return new MotorsportSourceState(phase, match.Key, match.Name, finishedAt, match.FetchedAt, match.Start);
    }

    private sealed record GrandPrix(string Id, string ShortName, DateTimeOffset Start, DateTimeOffset End);

    private static List<GrandPrix> ReadGrandsPrix(string json)
    {
        var result = new List<GrandPrix>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (Str(e, "kind") != "GP")
                continue;
            var id = Str(e, "id");
            var start = MotorsportSessionMatching.ParseTime(Str(e, "date_start"));
            var end = MotorsportSessionMatching.ParseTime(Str(e, "date_end"));
            if (id == null || start == null || end == null)
                continue;
            result.Add(new GrandPrix(id, Str(e, "shortname") ?? id, start.Value, end.Value));
        }
        return result;
    }

    internal static IEnumerable<SessionCandidate> ReadSessions(string json, string category, string grandPrix)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("broadcasts", out var broadcasts) || broadcasts.ValueKind != JsonValueKind.Array)
            return Array.Empty<SessionCandidate>();

        var parts = new List<(MotorsportSessionKind Kind, int? Number, SessionPart Part)>();
        foreach (var b in broadcasts.EnumerateArray())
        {
            if (Str(b, "type") != "SESSION")
                continue;
            if (!b.TryGetProperty("category", out var cat) || Str(cat, "acronym") != category)
                continue;
            var code = Str(b, "shortname")?.Trim() ?? "";
            var session = ReadCode(code);
            var start = MotorsportSessionMatching.ParseTime(Str(b, "date_start"));
            if (session == null || start == null)
                continue;
            var name = Str(b, "name")?.Trim() ?? code;
            parts.Add((session.Value.Kind, session.Value.Number, new SessionPart(
                Str(b, "id") ?? code, $"{category} {name} ({code})", ReadStatus(Str(b, "status")),
                start.Value, null, false)));
        }

        return parts
            .GroupBy(p => (p.Kind, p.Number))
            .Select(g => new SessionCandidate(g.Key.Kind, g.Key.Number,
                $"{grandPrix}:{category}:{g.Key.Kind}:{g.Key.Number}",
                $"{grandPrix} {g.First().Part.Name}",
                g.Select(p => p.Part).ToList()))
            .ToList();
    }

    private static (MotorsportSessionKind Kind, int? Number)? ReadCode(string code)
    {
        // A restarted race can come back as RAC2. It is still the same race.
        if (RaceCode.IsMatch(code))
            return (MotorsportSessionKind.Race, null);
        if (SprintCode.IsMatch(code))
            return (MotorsportSessionKind.Sprint, null);
        if (code == "PR")
            return (MotorsportSessionKind.Practice, null);
        if (code == "QP")
            return (MotorsportSessionKind.Qualifying, null);
        if (code == "WUP")
            return (MotorsportSessionKind.WarmUp, null);
        var numbered = NumberedCode.Match(code);
        if (!numbered.Success)
            return null;
        var number = int.Parse(numbered.Groups["n"].Value);
        return numbered.Groups["code"].Value switch
        {
            "FP" => (MotorsportSessionKind.Practice, number),
            "Q" => (MotorsportSessionKind.Qualifying, number),
            _ => (MotorsportSessionKind.WarmUp, number),
        };
    }

    internal static SessionPartStatus ReadStatus(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "NOT-STARTED" => SessionPartStatus.NotStarted,
        "CURRENT" => SessionPartStatus.Live,
        "FINISHED" => SessionPartStatus.Finished,
        "CANCELLED" => SessionPartStatus.Cancelled,
        _ => SessionPartStatus.Unknown,
    };

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

internal static class MotorsportTimingUrls
{
    public static string Resolve(string testVariable, string defaultUrl)
    {
        var overrideUrl = Environment.GetEnvironmentVariable(testVariable);
        return string.IsNullOrWhiteSpace(overrideUrl) ? defaultUrl : overrideUrl.TrimEnd('/');
    }
}
