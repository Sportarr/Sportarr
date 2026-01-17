using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Sports-specific filename parser for extracting event information from common sports file naming conventions.
/// Handles UFC, WWE, NFL, NBA, Soccer, and other sports-specific patterns.
/// </summary>
public class SportsFileNameParser
{
    private readonly ILogger<SportsFileNameParser> _logger;

    // Sports-specific naming patterns
    private static readonly List<SportsPattern> SportsPatterns = new()
    {
        // UFC patterns: UFC.299.2024.PPV.1080p, UFC.Fight.Night.230.2024.1080p
        new SportsPattern
        {
            Sport = "Fighting",
            Organization = "UFC",
            Pattern = new Regex(@"UFC[\.\-\s]+(?<number>\d+)[\.\-\s]+(?<year>\d{4})[\.\-\s]*(?:PPV)?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"UFC {match.Groups["number"].Value}"
        },
        new SportsPattern
        {
            Sport = "Fighting",
            Organization = "UFC",
            Pattern = new Regex(@"UFC[\.\-\s]+Fight[\.\-\s]+Night[\.\-\s]+(?<number>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"UFC Fight Night {match.Groups["number"].Value}"
        },
        // UFC with names: UFC.299.OMalley.vs.Vera.2
        new SportsPattern
        {
            Sport = "Fighting",
            Organization = "UFC",
            Pattern = new Regex(@"UFC[\.\-\s]+(?<number>\d+)[\.\-\s]+(?<fighter1>[A-Za-z]+)[\.\-\s]+vs?[\.\-\s]+(?<fighter2>[A-Za-z]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"UFC {match.Groups["number"].Value}: {match.Groups["fighter1"].Value} vs {match.Groups["fighter2"].Value}"
        },

        // Bellator patterns: Bellator.300.2024.1080p
        new SportsPattern
        {
            Sport = "Fighting",
            Organization = "Bellator",
            Pattern = new Regex(@"Bellator[\.\-\s]+(?<number>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"Bellator {match.Groups["number"].Value}"
        },

        // PFL patterns: PFL.2024.Season.Week.1
        new SportsPattern
        {
            Sport = "Fighting",
            Organization = "PFL",
            Pattern = new Regex(@"PFL[\.\-\s]+(?<year>\d{4})[\.\-\s]+(?:Season[\.\-\s]+)?(?:Week[\.\-\s]+)?(?<number>\d+)?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => match.Groups["number"].Success ? $"PFL {match.Groups["year"].Value} Week {match.Groups["number"].Value}" : $"PFL {match.Groups["year"].Value}"
        },

        // ONE Championship patterns: ONE.Championship.X.2024
        new SportsPattern
        {
            Sport = "Fighting",
            Organization = "ONE Championship",
            Pattern = new Regex(@"ONE[\.\-\s]+(?:Championship[\.\-\s]+)?(?<name>[A-Za-z]+[\.\-\s]*[A-Za-z]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"ONE Championship: {match.Groups["name"].Value.Replace(".", " ").Trim()}"
        },

        // Boxing patterns: Boxing.Canelo.vs.Bivol.2024
        new SportsPattern
        {
            Sport = "Fighting",
            Organization = "Boxing",
            Pattern = new Regex(@"Boxing[\.\-\s]+(?<fighter1>[A-Za-z]+)[\.\-\s]+vs?[\.\-\s]+(?<fighter2>[A-Za-z]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"{match.Groups["fighter1"].Value} vs {match.Groups["fighter2"].Value}"
        },

        // WWE patterns: WWE.Raw.2024.01.15, WWE.SmackDown.2024.01.12, WWE.NXT.2024.01.16
        new SportsPattern
        {
            Sport = "Wrestling",
            Organization = "WWE",
            Pattern = new Regex(@"WWE[\.\-\s]+(?<show>Raw|SmackDown|NXT|Main[\.\-\s]*Event)[\.\-\s]+(?<year>\d{4})[\.\-\s]+(?<month>\d{2})[\.\-\s]+(?<day>\d{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"WWE {match.Groups["show"].Value.Replace(".", " ")} {match.Groups["year"].Value}-{match.Groups["month"].Value}-{match.Groups["day"].Value}"
        },
        // WWE PPV: WWE.WrestleMania.40.2024
        new SportsPattern
        {
            Sport = "Wrestling",
            Organization = "WWE",
            Pattern = new Regex(@"WWE[\.\-\s]+(?<ppv>WrestleMania|Royal[\.\-\s]*Rumble|SummerSlam|Survivor[\.\-\s]*Series|Money[\.\-\s]*in[\.\-\s]*the[\.\-\s]*Bank|Hell[\.\-\s]*in[\.\-\s]*a[\.\-\s]*Cell|Elimination[\.\-\s]*Chamber|Backlash|Clash[\.\-\s]*at[\.\-\s]*the[\.\-\s]*Castle|Night[\.\-\s]*of[\.\-\s]*Champions)[\.\-\s]*(?<number>\d*)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => match.Groups["number"].Success && match.Groups["number"].Value.Length > 0
                ? $"WWE {match.Groups["ppv"].Value.Replace(".", " ")} {match.Groups["number"].Value}"
                : $"WWE {match.Groups["ppv"].Value.Replace(".", " ")}"
        },

        // AEW patterns: AEW.Dynamite.2024.01.17, AEW.Collision.2024.01.13
        new SportsPattern
        {
            Sport = "Wrestling",
            Organization = "AEW",
            Pattern = new Regex(@"AEW[\.\-\s]+(?<show>Dynamite|Rampage|Collision|Dark|Elevation)[\.\-\s]+(?<year>\d{4})[\.\-\s]+(?<month>\d{2})[\.\-\s]+(?<day>\d{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"AEW {match.Groups["show"].Value} {match.Groups["year"].Value}-{match.Groups["month"].Value}-{match.Groups["day"].Value}"
        },
        // AEW PPV: AEW.All.Out.2024
        new SportsPattern
        {
            Sport = "Wrestling",
            Organization = "AEW",
            Pattern = new Regex(@"AEW[\.\-\s]+(?<ppv>Double[\.\-\s]*or[\.\-\s]*Nothing|All[\.\-\s]*Out|All[\.\-\s]*In|Full[\.\-\s]*Gear|Revolution|Dynasty|Forbidden[\.\-\s]*Door|Worlds[\.\-\s]*End)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"AEW {match.Groups["ppv"].Value.Replace(".", " ")}"
        },

        // NFL patterns: NFL.2024.Week.10.Patriots.vs.Jets or NFL.2024.Week.10.Kansas.City.Chiefs.vs.Tampa.Bay.Buccaneers
        // Team names can be 1-3 words (e.g., "Patriots", "Green Bay Packers", "Kansas City Chiefs")
        new SportsPattern
        {
            Sport = "American Football",
            Organization = "NFL",
            Pattern = new Regex(@"NFL[\.\-\s]+(?<year>\d{4})[\.\-\s]+Week[\.\-\s]+(?<week>\d+)[\.\-\s]+(?<team1>(?:[A-Za-z]+[\.\-\s]+){1,3})(?:vs?|@)[\.\-\s]+(?<team2>(?:[A-Za-z]+[\.\-\s]*)+?)(?=[\.\-\s]+\d{3,4}p|[\.\-\s]+(?:WEB|HDTV|BluRay)|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"NFL Week {match.Groups["week"].Value}: {CleanTeamName(match.Groups["team1"].Value)} vs {CleanTeamName(match.Groups["team2"].Value)}"
        },
        new SportsPattern
        {
            Sport = "American Football",
            Organization = "NFL",
            Pattern = new Regex(@"NFL[\.\-\s]+Super[\.\-\s]+Bowl[\.\-\s]+(?<number>[LXVI]+|\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"NFL Super Bowl {match.Groups["number"].Value}"
        },

        // NBA patterns: NBA.2024.01.15.Lakers.vs.Celtics or NBA.2024.03.12.Indiana.Pacers.Vs.Oklahoma.City.Thunder
        // Team names can be 1-3 words (e.g., "Lakers", "Trail Blazers", "Oklahoma City Thunder")
        new SportsPattern
        {
            Sport = "Basketball",
            Organization = "NBA",
            Pattern = new Regex(@"NBA[\.\-\s]+(?<year>\d{4})[\.\-\s]+(?<month>\d{2})[\.\-\s]+(?<day>\d{2})[\.\-\s]+(?<team1>(?:[A-Za-z]+[\.\-\s]+){1,3})(?:vs?|@)[\.\-\s]+(?<team2>(?:[A-Za-z]+[\.\-\s]*)+?)(?=[\.\-\s]+\d{3,4}p|[\.\-\s]+(?:WEB|HDTV|BluRay)|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"NBA {match.Groups["year"].Value}-{match.Groups["month"].Value}-{match.Groups["day"].Value}: {CleanTeamName(match.Groups["team1"].Value)} vs {CleanTeamName(match.Groups["team2"].Value)}"
        },
        new SportsPattern
        {
            Sport = "Basketball",
            Organization = "NBA",
            Pattern = new Regex(@"NBA[\.\-\s]+(?<team1>(?:[A-Za-z]+[\.\-\s]+){1,3})(?:vs?|@)[\.\-\s]+(?<team2>(?:[A-Za-z]+[\.\-\s]+){1,3})(?<year>\d{4})[\.\-\s]+(?<month>\d{2})[\.\-\s]+(?<day>\d{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"NBA {match.Groups["year"].Value}-{match.Groups["month"].Value}-{match.Groups["day"].Value}: {CleanTeamName(match.Groups["team1"].Value)} vs {CleanTeamName(match.Groups["team2"].Value)}"
        },

        // NHL patterns: NHL.2024.01.15.Bruins.vs.Canadiens or NHL.2024.01.15.Tampa.Bay.Lightning.vs.Toronto.Maple.Leafs
        // Team names can be 1-3 words
        new SportsPattern
        {
            Sport = "Ice Hockey",
            Organization = "NHL",
            Pattern = new Regex(@"NHL[\.\-\s]+(?<year>\d{4})[\.\-\s]+(?<month>\d{2})[\.\-\s]+(?<day>\d{2})[\.\-\s]+(?<team1>(?:[A-Za-z]+[\.\-\s]+){1,3})(?:vs?|@)[\.\-\s]+(?<team2>(?:[A-Za-z]+[\.\-\s]*)+?)(?=[\.\-\s]+\d{3,4}p|[\.\-\s]+(?:WEB|HDTV|BluRay)|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"NHL {match.Groups["year"].Value}-{match.Groups["month"].Value}-{match.Groups["day"].Value}: {CleanTeamName(match.Groups["team1"].Value)} vs {CleanTeamName(match.Groups["team2"].Value)}"
        },

        // MLB patterns: MLB.2024.04.15.Yankees.vs.Red.Sox
        new SportsPattern
        {
            Sport = "Baseball",
            Organization = "MLB",
            Pattern = new Regex(@"MLB[\.\-\s]+(?<year>\d{4})[\.\-\s]+(?<month>\d{2})[\.\-\s]+(?<day>\d{2})[\.\-\s]+(?<team1>[A-Za-z]+(?:[\.\-\s]+[A-Za-z]+)?)[\.\-\s]+(?:vs?|@)[\.\-\s]+(?<team2>[A-Za-z]+(?:[\.\-\s]+[A-Za-z]+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"MLB {match.Groups["year"].Value}-{match.Groups["month"].Value}-{match.Groups["day"].Value}: {match.Groups["team1"].Value.Replace(".", " ")} vs {match.Groups["team2"].Value.Replace(".", " ")}"
        },

        // Soccer/Football patterns
        // Premier League: EPL.2024.Matchday.20.Arsenal.vs.Liverpool
        new SportsPattern
        {
            Sport = "Soccer",
            Organization = "Premier League",
            Pattern = new Regex(@"(?:EPL|Premier[\.\-\s]*League)[\.\-\s]+(?<year>\d{4})[\.\-\s]+(?:Matchday|Week|Round)[\.\-\s]+(?<round>\d+)[\.\-\s]+(?<team1>[A-Za-z]+(?:[\.\-\s]+[A-Za-z]+)?)[\.\-\s]+(?:vs?|@)[\.\-\s]+(?<team2>[A-Za-z]+(?:[\.\-\s]+[A-Za-z]+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"Premier League Matchday {match.Groups["round"].Value}: {match.Groups["team1"].Value.Replace(".", " ")} vs {match.Groups["team2"].Value.Replace(".", " ")}"
        },
        // Champions League: UCL.2024.Round.of.16.Real.Madrid.vs.Liverpool
        new SportsPattern
        {
            Sport = "Soccer",
            Organization = "Champions League",
            Pattern = new Regex(@"(?:UCL|UEFA[\.\-\s]*Champions[\.\-\s]*League)[\.\-\s]+(?<year>\d{4})[\.\-\s]+(?<round>[A-Za-z]+[\.\-\s]+(?:of[\.\-\s]+)?\d*|Group[\.\-\s]+[A-H]|Final|Semi[\.\-\s]*Final|Quarter[\.\-\s]*Final)[\.\-\s]+(?<team1>[A-Za-z]+(?:[\.\-\s]+[A-Za-z]+)?)[\.\-\s]+(?:vs?|@)[\.\-\s]+(?<team2>[A-Za-z]+(?:[\.\-\s]+[A-Za-z]+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"Champions League {match.Groups["round"].Value.Replace(".", " ")}: {match.Groups["team1"].Value.Replace(".", " ")} vs {match.Groups["team2"].Value.Replace(".", " ")}"
        },
        // Generic soccer: Soccer.Team1.vs.Team2.2024.01.15
        new SportsPattern
        {
            Sport = "Soccer",
            Organization = null,
            Pattern = new Regex(@"(?:Soccer|Football)[\.\-\s]+(?<team1>[A-Za-z]+(?:[\.\-\s]+[A-Za-z]+)?)[\.\-\s]+(?:vs?|@)[\.\-\s]+(?<team2>[A-Za-z]+(?:[\.\-\s]+[A-Za-z]+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"{match.Groups["team1"].Value.Replace(".", " ")} vs {match.Groups["team2"].Value.Replace(".", " ")}"
        },

        // F1/Motorsport patterns: F1.2024.Round.05.Chinese.GP, Formula.1.2024.Monaco.Grand.Prix
        new SportsPattern
        {
            Sport = "Motorsport",
            Organization = "Formula 1",
            Pattern = new Regex(@"(?:F1|Formula[\.\-\s]*1|Formula[\.\-\s]*One)[\.\-\s]+(?<year>\d{4})[\.\-\s]+(?:Round[\.\-\s]+(?<round>\d+)[\.\-\s]+)?(?<name>[A-Za-z]+(?:[\.\-\s]+[A-Za-z]+)?)[\.\-\s]*(?:GP|Grand[\.\-\s]*Prix)?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => match.Groups["round"].Success
                ? $"F1 {match.Groups["year"].Value} Round {match.Groups["round"].Value}: {match.Groups["name"].Value.Replace(".", " ")} GP"
                : $"F1 {match.Groups["year"].Value} {match.Groups["name"].Value.Replace(".", " ")} GP"
        },

        // NASCAR patterns: NASCAR.2024.Daytona.500
        new SportsPattern
        {
            Sport = "Motorsport",
            Organization = "NASCAR",
            Pattern = new Regex(@"NASCAR[\.\-\s]+(?<year>\d{4})[\.\-\s]+(?<name>[A-Za-z]+(?:[\.\-\s]+[A-Za-z0-9]+)*)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => $"NASCAR {match.Groups["year"].Value} {match.Groups["name"].Value.Replace(".", " ")}"
        },

        // Tennis patterns: Tennis.Australian.Open.2024.Final
        new SportsPattern
        {
            Sport = "Tennis",
            Organization = null,
            Pattern = new Regex(@"Tennis[\.\-\s]+(?<tournament>Australian[\.\-\s]+Open|French[\.\-\s]+Open|Wimbledon|US[\.\-\s]+Open|ATP[\.\-\s]+\d+)[\.\-\s]+(?<year>\d{4})[\.\-\s]+(?<round>Final|Semi[\.\-\s]*Final|Quarter[\.\-\s]*Final|Round[\.\-\s]+\d+)?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => match.Groups["round"].Success
                ? $"{match.Groups["tournament"].Value.Replace(".", " ")} {match.Groups["year"].Value} {match.Groups["round"].Value.Replace(".", " ")}"
                : $"{match.Groups["tournament"].Value.Replace(".", " ")} {match.Groups["year"].Value}"
        },

        // Golf patterns: Golf.PGA.Masters.2024.Round.4
        new SportsPattern
        {
            Sport = "Golf",
            Organization = "PGA",
            Pattern = new Regex(@"Golf[\.\-\s]+(?:PGA[\.\-\s]+)?(?<tournament>Masters|US[\.\-\s]+Open|Open[\.\-\s]+Championship|PGA[\.\-\s]+Championship|Ryder[\.\-\s]+Cup)[\.\-\s]+(?<year>\d{4})[\.\-\s]+(?:Round[\.\-\s]+(?<round>\d+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            TitleBuilder = (match) => match.Groups["round"].Success
                ? $"{match.Groups["tournament"].Value.Replace(".", " ")} {match.Groups["year"].Value} Round {match.Groups["round"].Value}"
                : $"{match.Groups["tournament"].Value.Replace(".", " ")} {match.Groups["year"].Value}"
        }
    };

    // Date extraction patterns
    private static readonly Regex DatePattern = new(@"(?<year>\d{4})[\.\-\s]+(?<month>\d{2})[\.\-\s]+(?<day>\d{2})", RegexOptions.Compiled);
    private static readonly Regex YearOnlyPattern = new(@"\b(?<year>20[12]\d)\b", RegexOptions.Compiled);
    // Season span pattern for multi-year seasons like "2025-2026" or "2025-26"
    private static readonly Regex SeasonSpanPattern = new(@"\b(?<startYear>20[12]\d)[\-/](?<endYear>20[12]\d|[12]\d)\b", RegexOptions.Compiled);

    public SportsFileNameParser(ILogger<SportsFileNameParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parse a sports filename to extract structured information
    /// </summary>
    public SportsParseResult Parse(string filename)
    {
        _logger.LogDebug("Parsing sports filename: {Filename}", filename);

        var result = new SportsParseResult
        {
            OriginalFilename = filename
        };

        // Clean the filename (remove extension, replace dots/underscores with spaces for processing)
        var cleanName = CleanFilename(filename);

        // Try each sports pattern
        foreach (var pattern in SportsPatterns)
        {
            var match = pattern.Pattern.Match(cleanName);
            if (match.Success)
            {
                result.Sport = pattern.Sport;
                result.Organization = pattern.Organization;
                result.EventTitle = pattern.TitleBuilder(match);
                result.MatchedPattern = pattern.Pattern.ToString();
                result.Confidence = 90; // High confidence for pattern match

                _logger.LogInformation("Matched sports pattern: {Sport}/{Org} - {Title}",
                    result.Sport, result.Organization, result.EventTitle);
                break;
            }
        }

        // Extract date from filename
        var dateMatch = DatePattern.Match(cleanName);
        if (dateMatch.Success)
        {
            if (int.TryParse(dateMatch.Groups["year"].Value, out var year) &&
                int.TryParse(dateMatch.Groups["month"].Value, out var month) &&
                int.TryParse(dateMatch.Groups["day"].Value, out var day))
            {
                try
                {
                    result.EventDate = new DateTime(year, month, day);
                    _logger.LogDebug("[SportsFileNameParser] Extracted date {Date} from '{Filename}'",
                        result.EventDate.Value.ToString("yyyy-MM-dd"), filename);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[SportsFileNameParser] Invalid date {Year}-{Month}-{Day} in '{Filename}': {Error}",
                        year, month, day, filename, ex.Message);
                }
            }
        }
        else
        {
            // Try season span extraction first (e.g., "2025-2026" or "2025-26")
            var seasonSpanMatch = SeasonSpanPattern.Match(cleanName);
            if (seasonSpanMatch.Success && int.TryParse(seasonSpanMatch.Groups["startYear"].Value, out var startYear))
            {
                result.EventYear = startYear;

                // Parse end year - handle both full year (2026) and short year (26)
                var endYearStr = seasonSpanMatch.Groups["endYear"].Value;
                if (int.TryParse(endYearStr, out var endYearParsed))
                {
                    // If it's a 2-digit year, convert to full year based on start year century
                    if (endYearParsed < 100)
                    {
                        endYearParsed = (startYear / 100) * 100 + endYearParsed;
                    }
                    result.SeasonYearEnd = endYearParsed;
                }

                _logger.LogDebug("[SportsFileNameParser] Extracted season span {StartYear}-{EndYear} from '{Filename}'",
                    startYear, result.SeasonYearEnd ?? startYear, filename);
            }
            else
            {
                // Try year-only extraction
                var yearMatch = YearOnlyPattern.Match(cleanName);
                if (yearMatch.Success && int.TryParse(yearMatch.Groups["year"].Value, out var year))
                {
                    result.EventYear = year;
                    _logger.LogDebug("[SportsFileNameParser] Extracted year-only {Year} from '{Filename}'",
                        year, filename);
                }
                else
                {
                    _logger.LogDebug("[SportsFileNameParser] No date/year found in '{Filename}' (cleanName: '{CleanName}')",
                        filename, cleanName);
                }
            }
        }

        // If no pattern matched, try generic organization extraction
        if (string.IsNullOrEmpty(result.EventTitle))
        {
            result = ExtractGenericInfo(cleanName, result);
        }

        return result;
    }

    /// <summary>
    /// Attempt to extract organization and event info from generic filenames
    /// </summary>
    private SportsParseResult ExtractGenericInfo(string cleanName, SportsParseResult result)
    {
        // Try to extract known organization prefixes
        var orgPatterns = new Dictionary<string, (string Sport, string Org)>
        {
            { @"^UFC[\.\-\s]", ("Fighting", "UFC") },
            { @"^Bellator[\.\-\s]", ("Fighting", "Bellator") },
            { @"^PFL[\.\-\s]", ("Fighting", "PFL") },
            { @"^ONE[\.\-\s]", ("Fighting", "ONE Championship") },
            { @"^WWE[\.\-\s]", ("Wrestling", "WWE") },
            { @"^AEW[\.\-\s]", ("Wrestling", "AEW") },
            { @"^NFL[\.\-\s]", ("American Football", "NFL") },
            { @"^NBA[\.\-\s]", ("Basketball", "NBA") },
            { @"^NHL[\.\-\s]", ("Ice Hockey", "NHL") },
            { @"^MLB[\.\-\s]", ("Baseball", "MLB") },
            { @"^(?:EPL|Premier[\.\-\s]*League)[\.\-\s]", ("Soccer", "Premier League") },
            { @"^(?:UCL|Champions[\.\-\s]*League)[\.\-\s]", ("Soccer", "Champions League") },
            { @"^(?:F1|Formula[\.\-\s]*1)[\.\-\s]", ("Motorsport", "Formula 1") },
            { @"^NASCAR[\.\-\s]", ("Motorsport", "NASCAR") },
        };

        foreach (var (pattern, info) in orgPatterns)
        {
            var match = Regex.Match(cleanName, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                result.Sport = info.Sport;
                result.Organization = info.Org;
                result.Confidence = 60; // Medium confidence for org-only match

                // Extract remaining text as potential event title
                var remaining = cleanName.Substring(match.Length).Trim();
                // Remove quality/source markers
                remaining = Regex.Replace(remaining, @"\b(2160p|1080p|720p|480p|4K|BluRay|WEB-DL|WEBRip|HDTV|x264|x265|HEVC)\b.*$", "", RegexOptions.IgnoreCase).Trim();

                if (!string.IsNullOrEmpty(remaining))
                {
                    result.EventTitle = $"{info.Org} {remaining}";
                }
                break;
            }
        }

        return result;
    }

    private string CleanFilename(string filename)
    {
        // Remove file extension
        var extensions = new[] { ".mkv", ".mp4", ".avi", ".m4v", ".ts", ".wmv", ".mov" };
        foreach (var ext in extensions)
        {
            if (filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                filename = filename[..^ext.Length];
                break;
            }
        }

        // Replace dots with spaces for matching, but preserve the original for pattern matching
        return filename.Replace('_', ' ');
    }

    /// <summary>
    /// Clean team name extracted from regex - remove trailing separators and normalize
    /// </summary>
    private static string CleanTeamName(string teamName)
    {
        // Remove trailing dots, dashes, spaces
        return Regex.Replace(teamName.Trim(), @"[\.\-\s]+$", "").Replace('.', ' ').Replace('-', ' ').Trim();
    }
}

/// <summary>
/// Pattern definition for matching sports filenames
/// </summary>
public class SportsPattern
{
    public required string Sport { get; set; }
    public string? Organization { get; set; }
    public required Regex Pattern { get; set; }
    public required Func<Match, string> TitleBuilder { get; set; }
}

/// <summary>
/// Result of parsing a sports filename
/// </summary>
public class SportsParseResult
{
    public required string OriginalFilename { get; set; }
    public string? EventTitle { get; set; }
    public string? Sport { get; set; }
    public string? Organization { get; set; }
    public DateTime? EventDate { get; set; }
    public int? EventYear { get; set; }
    /// <summary>
    /// End year for season spans (e.g., 2026 for "2025-2026" season).
    /// When set, the release should match events where EventYear falls within [EventYear, SeasonYearEnd].
    /// </summary>
    public int? SeasonYearEnd { get; set; }
    public int Confidence { get; set; } // 0-100
    public string? MatchedPattern { get; set; }
}
