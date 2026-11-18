using System.Text.RegularExpressions;

namespace Sportarr.Api.Services.MotorsportLive;

/// <summary>
/// Maps a league to a series with a timing source, and reads the session of
/// an event from its title.
/// </summary>
public static class MotorsportSeriesMap
{
    // Names are compared with case and punctuation removed.
    private static readonly Dictionary<string, MotorsportSeries> SeriesByName = new()
    {
        ["motogp"] = MotorsportSeries.MotoGp,
        ["motogpworldchampionship"] = MotorsportSeries.MotoGp,
        ["moto2"] = MotorsportSeries.Moto2,
        ["moto2worldchampionship"] = MotorsportSeries.Moto2,
        ["moto3"] = MotorsportSeries.Moto3,
        ["moto3worldchampionship"] = MotorsportSeries.Moto3,
        ["sbk"] = MotorsportSeries.WorldSbk,
        ["wsbk"] = MotorsportSeries.WorldSbk,
        ["wsb"] = MotorsportSeries.WorldSbk,
        ["worldsbk"] = MotorsportSeries.WorldSbk,
        ["worldsuperbike"] = MotorsportSeries.WorldSbk,
        ["worldsuperbikes"] = MotorsportSeries.WorldSbk,
        ["worldsuperbikechampionship"] = MotorsportSeries.WorldSbk,
        ["superbikeworldchampionship"] = MotorsportSeries.WorldSbk,
        ["fimsuperbikeworldchampionship"] = MotorsportSeries.WorldSbk,
        ["worldssp"] = MotorsportSeries.WorldSsp,
        ["wssp"] = MotorsportSeries.WorldSsp,
        ["worldsupersport"] = MotorsportSeries.WorldSsp,
        ["supersportworldchampionship"] = MotorsportSeries.WorldSsp,
        ["fimsupersportworldchampionship"] = MotorsportSeries.WorldSsp,
        ["worldspb"] = MotorsportSeries.WorldSpb,
        ["worldsportbike"] = MotorsportSeries.WorldSpb,
        ["sportbikeworldchampionship"] = MotorsportSeries.WorldSpb,
        ["fimsportbikeworldchampionship"] = MotorsportSeries.WorldSpb,
        ["formula1"] = MotorsportSeries.Formula1,
        ["formulaone"] = MotorsportSeries.Formula1,
        ["f1"] = MotorsportSeries.Formula1,
        ["formula1worldchampionship"] = MotorsportSeries.Formula1,
        ["fiaformulaoneworldchampionship"] = MotorsportSeries.Formula1,
        ["indycar"] = MotorsportSeries.IndyCar,
        ["indycarseries"] = MotorsportSeries.IndyCar,
        ["nttindycarseries"] = MotorsportSeries.IndyCar,
        ["bsb"] = MotorsportSeries.BritishSuperbike,
    };

    private static readonly Regex BritishSuperbikeName = new(
        @"britishsuperbikes?(?:championship)?(?:withpirelli)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NonAlphanumeric = new(@"[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The session detector keys on league names. Each series uses the table
    // of the series with the same session structure.
    private static string DetectorLeagueName(MotorsportSeries series) => series switch
    {
        MotorsportSeries.MotoGp or MotorsportSeries.Moto2 or MotorsportSeries.Moto3 => "MotoGP",
        MotorsportSeries.WorldSbk => "WorldSBK",
        MotorsportSeries.WorldSsp or MotorsportSeries.WorldSpb => "WorldSSP",
        MotorsportSeries.Formula1 => "Formula 1",
        MotorsportSeries.IndyCar => "IndyCar",
        MotorsportSeries.BritishSuperbike => "British Superbike",
        _ => series.ToString(),
    };

    private static bool IsGrandPrixSeries(MotorsportSeries series) =>
        series is MotorsportSeries.MotoGp or MotorsportSeries.Moto2 or MotorsportSeries.Moto3;

    /// <summary>
    /// The series for a league name or one of its alternate names, or null
    /// when the league has no timing source.
    /// </summary>
    public static MotorsportSeries? Resolve(string? leagueName, string? alternateNames)
    {
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(leagueName))
            names.Add(leagueName);
        if (!string.IsNullOrWhiteSpace(alternateNames))
            names.AddRange(alternateNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var name in names)
        {
            var key = NonAlphanumeric.Replace(name.ToLowerInvariant(), "");
            if (SeriesByName.TryGetValue(key, out var series))
                return series;
            // The British championship carries a sponsor in front of its name.
            if (BritishSuperbikeName.IsMatch(key))
                return MotorsportSeries.BritishSuperbike;
        }
        return null;
    }

    private static readonly Regex MotoGpPracticeWord = new(@"\bpractice\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NumberedOrFreePractice = new(
        @"\bfree\s*practice\b|\bfp\s*\d|\bpractice\s*(?:\d|one|two|three)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RaceNumber = new(@"\brace\s*#?\s*(?<n>[1-3])\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WarmUpNumber = new(@"\bwarm\s*up\s*(?<n>[1-3])\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TrailingNumber = new(@"\s(?<n>\d{1,2})$", RegexOptions.Compiled);

    private static Regex Words(string pattern) =>
        new(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Words that name a class. A title of one league that names another
    // class belongs to that class, so its session must not be matched here.
    private static readonly (MotorsportSeries[] Series, Regex Class)[] ClassWords =
    {
        (new[] { MotorsportSeries.MotoGp }, Words(@"\bmoto\s*gp\b")),
        (new[] { MotorsportSeries.Moto2 }, Words(@"\bmoto\s*2\b")),
        (new[] { MotorsportSeries.Moto3 }, Words(@"\bmoto\s*3\b")),
        (Array.Empty<MotorsportSeries>(), Words(@"\bmoto\s*e\b|\btalent\s+cup\b")),
        (new[] { MotorsportSeries.WorldSbk }, Words(@"\bworld\s*sbk\b|\bsuperbikes?\b")),
        (new[] { MotorsportSeries.WorldSsp }, Words(@"\bworld\s*ssp\b|\bsupersport\b")),
        (new[] { MotorsportSeries.WorldSpb }, Words(@"\bworld\s*spb\b|\bsportbike\b")),
        (Array.Empty<MotorsportSeries>(), Words(@"\bworld\s*wcr\b|\bwomen'?s\s+circuit\s+racing\b|\bsuperstock\b|\bf\s*900\b")),
        (new[] { MotorsportSeries.Formula1 }, Words(@"\bformula\s*(?:1|one)\b|\bf1\b")),
        (Array.Empty<MotorsportSeries>(), Words(@"\bformula\s*[23]\b|\bf[23]\b|\bf1\s+academy\b|\bindy\s*nxt\b")),
    };

    /// <summary>
    /// True when a title names a class other than the class of the series.
    /// The British championship holds Superbike events, so its own word is
    /// the Superbike word.
    /// </summary>
    internal static bool NamesOtherClass(MotorsportSeries series, string title)
    {
        var own = series == MotorsportSeries.BritishSuperbike ? MotorsportSeries.WorldSbk : series;
        return ClassWords.Any(c => !c.Series.Contains(own) && c.Class.IsMatch(title));
    }

    /// <summary>
    /// The session that an event title names, or null when the title names
    /// no session that a timing source can report.
    /// </summary>
    public static MotorsportSessionQuery? DescribeSession(
        MotorsportSeries series, string title, DateTimeOffset anchor, string? location)
    {
        if (string.IsNullOrWhiteSpace(title) ||
            EventPartDetector.NamesNonSessionShow(title) || EventPartDetector.NamesTestingSession(title))
            return null;

        var normalized = Regex.Replace(title, @"[._-]+", " ");
        if (NamesOtherClass(series, normalized))
            return null;

        // MotoGP runs a session called "Practice" next to "Free Practice 1".
        // The detector reads a bare "Practice" as Practice 1, so read it here.
        if (IsGrandPrixSeries(series) && MotoGpPracticeWord.IsMatch(normalized) &&
            !NumberedOrFreePractice.IsMatch(normalized))
            return new MotorsportSessionQuery(series, MotorsportSessionKind.Practice, null, anchor, location);

        var identity = EventPartDetector.DetectMotorsportSessionIdentity(
            normalized, DetectorLeagueName(series), releaseTitle: false);

        if (identity == null)
        {
            // These leagues hold one event for each Grand Prix race and do
            // not always name the session.
            return IsGrandPrixSeries(series)
                ? new MotorsportSessionQuery(series, MotorsportSessionKind.Race, null, anchor, location)
                : null;
        }

        int? number = null;
        var numberMatch = TrailingNumber.Match(identity);
        if (numberMatch.Success)
        {
            number = int.Parse(numberMatch.Groups["n"].Value);
            identity = identity[..numberMatch.Index];
        }

        MotorsportSessionKind? kind = identity switch
        {
            "Race" => MotorsportSessionKind.Race,
            "Sprint" => MotorsportSessionKind.Sprint,
            "Sprint Qualifying" => MotorsportSessionKind.SprintQualifying,
            "Superpole Race" => MotorsportSessionKind.SuperpoleRace,
            "Superpole" => MotorsportSessionKind.Superpole,
            "Qualifying" => MotorsportSessionKind.Qualifying,
            "Practice" => MotorsportSessionKind.Practice,
            "Final Practice" => MotorsportSessionKind.Practice,
            "Warm Up" => MotorsportSessionKind.WarmUp,
            _ => null,
        };
        if (kind == null)
            return null;

        // Some session tables give one label to all races of a round. The
        // title still carries the number.
        if (number == null && kind == MotorsportSessionKind.Race && RaceNumber.Match(normalized) is { Success: true } race)
            number = int.Parse(race.Groups["n"].Value);
        if (number == null && kind == MotorsportSessionKind.WarmUp && WarmUpNumber.Match(normalized) is { Success: true } warmUp)
            number = int.Parse(warmUp.Groups["n"].Value);

        return new MotorsportSessionQuery(series, kind.Value, number, anchor, location);
    }
}
