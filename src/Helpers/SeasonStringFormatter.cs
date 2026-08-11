using System.Text.RegularExpressions;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Generates future-season strings in the same format the league
/// already uses. The /list/seasons upstream endpoint returns the
/// season identifiers a league has historically reported, but the
/// shape of that identifier varies by sport:
///   - Single-year (e.g. Formula 1, MLB):  "2025"
///   - Two-year span full (e.g. NBA, NHL):  "2025-2026"
///   - Two-year span short (some soccer):   "2024-25"
///   - Slash-separated variants:            "2024/2025", "2024/25"
///
/// LeagueEventSyncService likes to extend the API's season list with
/// a few upcoming years so events that have already been scheduled
/// but not yet folded into /list/seasons still get pulled. The naive
/// "add 2026, 2027, 2028..." approach worked for single-year leagues
/// but produced guaranteed-404 requests for every two-year-span
/// league, which on a per-league per-sync cycle wasted six round
/// trips to sportarr.net plus a slot in the upstream semaphore. This
/// helper inspects the existing seasons, picks the dominant format,
/// and emits future strings that match.
///
/// If we can't recognize any format (empty input, non-year strings),
/// we fall back to the plain 4-digit-year shape -- same as the old
/// blind loop, which is safe for the single-year case and no worse
/// than today's behavior for the unknown case.
/// </summary>
public static class SeasonStringFormatter
{
    private enum SeasonFormat
    {
        SingleYear,         // "2025"
        TwoYearSpanFull,    // "2024-2025"
        TwoYearSpanShort,   // "2024-25"
        TwoYearSlashFull,   // "2024/2025"
        TwoYearSlashShort,  // "2024/25"
    }

    private static readonly Regex SingleYearRe       = new(@"^\d{4}$", RegexOptions.Compiled);
    private static readonly Regex TwoYearSpanFullRe  = new(@"^\d{4}-\d{4}$", RegexOptions.Compiled);
    private static readonly Regex TwoYearSpanShortRe = new(@"^\d{4}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex TwoYearSlashFullRe = new(@"^\d{4}/\d{4}$", RegexOptions.Compiled);
    private static readonly Regex TwoYearSlashShortRe = new(@"^\d{4}/\d{2}$", RegexOptions.Compiled);

    /// <summary>
    /// Yields season strings for <paramref name="yearsAhead"/> years
    /// starting at <paramref name="currentYear"/>, formatted to match
    /// the dominant format in <paramref name="existingSeasons"/>.
    /// </summary>
    public static IEnumerable<string> GenerateFutureSeasons(
        IEnumerable<string> existingSeasons,
        int currentYear,
        int yearsAhead)
    {
        var format = DetectFormat(existingSeasons);
        for (int year = currentYear; year <= currentYear + yearsAhead; year++)
        {
            yield return Format(format, year);
        }
    }

    private static string Format(SeasonFormat format, int year) => format switch
    {
        SeasonFormat.TwoYearSpanFull  => $"{year}-{year + 1}",
        SeasonFormat.TwoYearSpanShort => $"{year}-{(year + 1) % 100:D2}",
        SeasonFormat.TwoYearSlashFull => $"{year}/{year + 1}",
        SeasonFormat.TwoYearSlashShort => $"{year}/{(year + 1) % 100:D2}",
        _ => year.ToString(),
    };

    private static readonly Regex LeadingYearRe = new(@"^\d{4}", RegexOptions.Compiled);

    /// <summary>
    /// Extracts the leading 4-digit year from a season string, regardless of
    /// format (all five documented shapes start with the year). Null if the
    /// string doesn't start with one.
    /// </summary>
    public static int? GetLeadingYear(string season)
    {
        if (string.IsNullOrWhiteSpace(season))
            return null;

        var match = LeadingYearRe.Match(season.Trim());
        return match.Success ? int.Parse(match.Value) : null;
    }

    /// <summary>
    /// The season with the highest leading year that isn't later than
    /// <paramref name="maxYear"/> - i.e. "the most recent season we actually
    /// have data for", distinct from "the season matching the calendar
    /// year right now". During an off-season gap (the hub hasn't listed
    /// next season yet, or it has no events scheduled), this correctly
    /// stays on last season instead of jumping to an empty "current" one.
    /// Falls back to the single highest season overall if every listed
    /// season is somehow after maxYear. Null if the input is empty.
    /// </summary>
    public static string? GetLatestSeasonNotAfter(IEnumerable<string> seasons, int maxYear)
    {
        var withYears = seasons
            .Select(s => (Season: s, Year: GetLeadingYear(s)))
            .Where(x => x.Year.HasValue)
            .ToList();

        if (withYears.Count == 0)
            return null;

        var notAfter = withYears.Where(x => x.Year!.Value <= maxYear).ToList();
        var pool = notAfter.Count > 0 ? notAfter : withYears;

        return pool.OrderByDescending(x => x.Year!.Value).First().Season;
    }

    private static SeasonFormat DetectFormat(IEnumerable<string> seasons)
    {
        var counts = new Dictionary<SeasonFormat, int>();

        foreach (var s in seasons)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            var trimmed = s.Trim();
            SeasonFormat? matched =
                TwoYearSpanFullRe.IsMatch(trimmed)  ? SeasonFormat.TwoYearSpanFull  :
                TwoYearSpanShortRe.IsMatch(trimmed) ? SeasonFormat.TwoYearSpanShort :
                TwoYearSlashFullRe.IsMatch(trimmed) ? SeasonFormat.TwoYearSlashFull :
                TwoYearSlashShortRe.IsMatch(trimmed)? SeasonFormat.TwoYearSlashShort:
                SingleYearRe.IsMatch(trimmed)       ? SeasonFormat.SingleYear        :
                (SeasonFormat?)null;

            if (matched is { } m)
            {
                counts.TryGetValue(m, out var c);
                counts[m] = c + 1;
            }
        }

        if (counts.Count == 0)
        {
            return SeasonFormat.SingleYear;
        }

        return counts.OrderByDescending(kv => kv.Value).First().Key;
    }
}
