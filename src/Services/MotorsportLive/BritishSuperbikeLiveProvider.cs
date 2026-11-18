using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Sportarr.Api.Constants;

namespace Sportarr.Api.Services.MotorsportLive;

/// <summary>
/// British Superbikes from the results pages of tsl-timing.com. These pages
/// have no live status. A race counts as finished when its result is
/// published. Until then the state is unknown.
/// </summary>
public sealed class BritishSuperbikeLiveProvider : IMotorsportLiveProvider
{
    private const string DefaultBaseUrl = "https://www.tsl-timing.com";

    private static readonly Regex EventLink = new(
        @"<a href=""/event/(?<id>\d+)"">(?<body>.*?)</a>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex DateRange = new(
        @"<i>\s*(?<d1>\d{1,2})(?:st|nd|rd|th)\s+(?<m1>[A-Za-z]{3})[a-z]*\s*-\s*(?<d2>\d{1,2})(?:st|nd|rd|th)\s+(?<m2>[A-Za-z]{3})[a-z]*\s+(?<y>\d{4})\s*</i>",
        RegexOptions.Compiled);
    private static readonly Regex TrackName = new(
        @"class=""clubEventMapText"">(?<track>[^<]+)<", RegexOptions.Compiled);
    private static readonly Regex ChampionshipBlock = new(
        @"class=""championshipDiv"">(?<body>.*?)(?=class=""championshipDiv""|$)",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Header = new(@"<h3>(?<name>[^<]+)</h3>", RegexOptions.Compiled);
    private static readonly Regex SessionName = new(
        @"class=""sessionName""[^>]*>(?<name>[^<]+)<", RegexOptions.Compiled);

    private readonly MotorsportTimingFetcher _fetcher;
    private readonly string _baseUrl;

    public BritishSuperbikeLiveProvider(MotorsportTimingFetcher fetcher)
    {
        _fetcher = fetcher;
        _baseUrl = MotorsportTimingUrls.Resolve(EnvironmentVariableNames.TestTslTimingUrl, DefaultBaseUrl);
    }

    public string Source => "tsl-timing.com";

    public bool Supports(MotorsportSeries series) => series == MotorsportSeries.BritishSuperbike;

    public async Task<MotorsportSourceState?> GetSessionStateAsync(MotorsportSessionQuery query, CancellationToken cancellationToken)
    {
        if (query.Kind is not (MotorsportSessionKind.Race or MotorsportSessionKind.Sprint) ||
            (query.Kind == MotorsportSessionKind.Race && query.Number == null))
            return null;

        var list = await _fetcher.GetAsync($"{_baseUrl}/Results/bsb", MotorsportTimingFetcher.CalendarTtl, cancellationToken);
        if (list == null)
            return null;

        var round = ReadRounds(list.Text)
            .Where(r => query.Anchor >= r.Start.AddDays(-1) && query.Anchor < r.End.AddDays(2))
            .OrderByDescending(r => MotorsportSessionMatching.LocationOverlap(r.Track, query.Location))
            .FirstOrDefault();
        if (round == null)
            return null;

        var page = await _fetcher.GetAsync($"{_baseUrl}/event/{round.Id}", MotorsportTimingFetcher.LiveTtl, cancellationToken);
        if (page == null)
            return null;

        var resultName = ReadSuperbikeSessions(page.Text).FirstOrDefault(name => IsResultOf(name, query));
        var label = query.Kind == MotorsportSessionKind.Sprint ? "Sprint" : $"Race {query.Number}";
        var key = $"bsb:{round.Id}:{label}";
        return new MotorsportSourceState(
            resultName != null ? MotorsportLivePhase.Finished : MotorsportLivePhase.Unknown,
            key, $"{round.Track} {label}", null, page.FetchedAt);
    }

    internal sealed record BsbRound(string Id, string Track, DateTimeOffset Start, DateTimeOffset End);

    internal static List<BsbRound> ReadRounds(string html)
    {
        var result = new List<BsbRound>();
        // Testing days are listed after the races and are not rounds.
        var testing = html.IndexOf("id=\"testing\"", StringComparison.Ordinal);
        var races = testing > 0 ? html[..testing] : html;
        foreach (Match link in EventLink.Matches(races))
        {
            var body = link.Groups["body"].Value;
            var range = DateRange.Match(body);
            if (!range.Success)
                continue;
            var year = int.Parse(range.Groups["y"].Value);
            if (!TryDate(range.Groups["d1"].Value, range.Groups["m1"].Value, year, out var start) ||
                !TryDate(range.Groups["d2"].Value, range.Groups["m2"].Value, year, out var end))
                continue;
            var track = TrackName.Match(body);
            result.Add(new BsbRound(link.Groups["id"].Value,
                track.Success ? WebUtility.HtmlDecode(track.Groups["track"].Value.Trim()) : "", start, end));
        }
        return result;
    }

    internal static List<string> ReadSuperbikeSessions(string html)
    {
        foreach (Match block in ChampionshipBlock.Matches(html))
        {
            var header = Header.Match(block.Groups["body"].Value);
            if (!header.Success || !header.Groups["name"].Value.Contains("British Superbike", StringComparison.OrdinalIgnoreCase))
                continue;
            return SessionName.Matches(block.Groups["body"].Value)
                .Select(m => WebUtility.HtmlDecode(m.Groups["name"].Value.Trim()))
                .ToList();
        }
        return new List<string>();
    }

    private static bool IsResultOf(string sessionName, MotorsportSessionQuery query)
    {
        // The whole name must match. A name such as "Race 1 Part 1 Result"
        // belongs to a stopped race, not to the finished race.
        var pattern = query.Kind == MotorsportSessionKind.Sprint
            ? @"^Sprint(?:\s+Race)?\s+(?:Provisional\s+)?Result$"
            : $@"^Race\s*{query.Number}\s+(?:Provisional\s+)?Result$";
        return Regex.IsMatch(sessionName.Trim(), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool TryDate(string day, string month, int year, out DateTimeOffset date)
    {
        // The pages give UK dates. A whole day of margin covers the offset.
        var ok = DateTime.TryParseExact($"{day} {month} {year}", "d MMM yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed);
        date = ok ? new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc)) : default;
        return ok;
    }
}
