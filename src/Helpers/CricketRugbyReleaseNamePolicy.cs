using System.Text.RegularExpressions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

public static class CricketRugbyReleaseNamePolicy
{
    private static readonly Regex MatchNumberPattern = new(
        @"(?<![\p{L}\p{M}\p{N}])M0*(?<number>[1-9][0-9]{0,2})(?![\p{L}\p{M}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CricketSingleInningsPattern = new(
        @"\b(?:(?:[1-4](?:st|nd|rd|th)|First|Second|Third|Fourth)[\s._-]+Innings|Innings[\s._-]+[1-4])\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RoundPattern = new(
        @"(?<![\p{L}\p{M}\p{N}])Round[\s._-]*0*(?<number>[1-9][0-9]{0,2})(?![\p{L}\p{M}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DayMonthPattern = new(
        @"(?<![\p{L}\p{M}\p{N}])(?<day>0?[1-9]|[12][0-9]|3[01])[\s._/-]+(?<month>0?[1-9]|1[0-2])(?:[\s._/-]+(?<year>20[0-9]{2}))?(?![\p{L}\p{M}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex YearMonthDayPattern = new(
        @"(?<![\p{L}\p{M}\p{N}])(?<year>20[0-9]{2})[\s._/-]+(?<month>0?[1-9]|1[0-2])[\s._/-]+(?<day>0?[1-9]|[12][0-9]|3[01])(?![\p{L}\p{M}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AudioChannelPrefixPattern = new(
        @"(?:^|[^\p{L}\p{M}\p{N}])(?:DDP?|E[\s._-]*AC[\s._-]*3|AC[\s._-]*3|AAC|DTS(?:[\s._-]*HD)?(?:[\s._-]*MA)?)[\s._-]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? BuildQuery(Event evt)
    {
        var league = LeagueKey(evt.League?.Name);
        if (league == null)
        {
            return null;
        }

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away) ||
            string.Equals(home, away, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var date = evt.BroadcastDate ?? evt.EventDate.Date;
        var year = date.Year;
        int.TryParse(evt.Round, out var round);

        return league switch
        {
            "CWC" => $"Cricket World Cup {year}",
            "IPL" when round is > 0 and < 100 => $"IPL {year} M{round:D2}",
            "IPL" when round == 200 => $"IPL {year} Final {home} {away}",
            "BBL" => BuildBigBashQuery(evt.Season, year, round, home, away),
            "SixNations" => $"Six Nations Rugby {year} {BaseRugbyTeam(home)} {BaseRugbyTeam(away)}",
            "RugbyChampionship" => $"Rugby Championship {year} {BaseRugbyTeam(home)} {BaseRugbyTeam(away)}",
            "URC" => $"URC {year} {BaseRugbyTeam(home)} {BaseRugbyTeam(away)}",
            "NRL" => $"NRL {year} {NrlSearchName(home)} {NrlSearchName(away)}",
            _ => null
        };
    }

    public static bool HasIdentityConflict(string releaseTitle, Event evt)
    {
        if (HasChampionsTrophyStageConflict(releaseTitle, evt))
        {
            return true;
        }

        var eventLeague = LeagueKey(evt.League?.Name);
        if (eventLeague == null)
        {
            return false;
        }

        var releaseLeague = ReleaseLeagueKey(releaseTitle);
        if (releaseLeague != null && !string.Equals(eventLeague, releaseLeague, StringComparison.Ordinal))
        {
            return true;
        }

        if (eventLeague is "IPL" or "BBL")
        {
            if (HasCricketStageConflict(releaseTitle, evt.Round))
            {
                return true;
            }
            if (int.TryParse(evt.Round, out var stageRound) && stageRound >= 100 &&
                !Regex.IsMatch(releaseTitle, @"(?<![0-9])20[0-9]{2}(?![0-9])", RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        if (eventLeague == "CWC" &&
            (CricketSingleInningsPattern.IsMatch(releaseTitle) ||
             HasCricketWorldCupStageConflict(releaseTitle, evt.Round)))
        {
            return true;
        }

        if (eventLeague == "NRL" && int.TryParse(evt.Round, out var eventRound) && eventRound > 50)
        {
            if (RoundPattern.IsMatch(releaseTitle))
            {
                return true;
            }
        }

        if (eventLeague is "SixNations" or "RugbyChampionship" or "URC" or "NRL" &&
            HasConflictingDayMonth(releaseTitle, evt.BroadcastDate ?? evt.EventDate.Date))
        {
            return true;
        }

        return false;
    }

    private static bool HasChampionsTrophyStageConflict(string releaseTitle, Event evt)
    {
        if (!string.Equals(evt.League?.Name, "ICC Champions Trophy", StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(releaseTitle, @"\b(?:ICC[\s._-]+)?Champions[\s._-]+Trophy\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !int.TryParse(evt.Round, out var round))
        {
            return false;
        }

        var semiFinal = Regex.IsMatch(releaseTitle, @"\bSemi[\s._-]*Finals?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var final = Regex.IsMatch(releaseTitle, @"\bFinal\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (round == 200)
        {
            var numberedMatch = MatchNumberPattern.IsMatch(releaseTitle);
            return semiFinal || !final && (numberedMatch ||
                !HasExactDayMonth(releaseTitle, evt.BroadcastDate ?? evt.EventDate.Date));
        }

        return round is > 0 and < 100 && (semiFinal || final);
    }

    public static bool HasStrongEventIdentity(string releaseTitle, Event evt)
    {
        var eventLeague = LeagueKey(evt.League?.Name);
        if (eventLeague == null || HasIdentityConflict(releaseTitle, evt) ||
            !string.Equals(eventLeague, ReleaseLeagueKey(releaseTitle), StringComparison.Ordinal))
        {
            return false;
        }

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
        {
            return false;
        }

        var homeIdentity = eventLeague == "NRL" ? NrlSearchName(home) : BaseRugbyTeam(home);
        var awayIdentity = eventLeague == "NRL" ? NrlSearchName(away) : BaseRugbyTeam(away);
        var normalizedRelease = Normalize(releaseTitle);
        var teamsMatch = ContainsPhrase(normalizedRelease, Normalize(homeIdentity)) &&
                         ContainsPhrase(normalizedRelease, Normalize(awayIdentity));
        if (eventLeague == "URC")
        {
            return teamsMatch && HasExactDayMonth(releaseTitle, evt.BroadcastDate ?? evt.EventDate.Date);
        }
        return teamsMatch;
    }

    public static bool HasStrongWorldCupIdentity(string releaseTitle, Event evt, DateTime? releaseDate)
    {
        if (LeagueKey(evt.League?.Name) != "CWC" || HasIdentityConflict(releaseTitle, evt) ||
            Regex.IsMatch(releaseTitle, @"\bInnings\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        var eventDate = (evt.BroadcastDate ?? evt.EventDate).Date;
        var year = Regex.Match(releaseTitle, @"(?<!\d)20\d{2}(?!\d)");
        if (!year.Success || int.Parse(year.Value) != eventDate.Year ||
            releaseDate.HasValue && releaseDate.Value.Date != eventDate)
        {
            return false;
        }

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        home = LeagueNameSuffixStripper.StripNationalTeamSportSuffix(home, "Cricket") ?? home;
        away = LeagueNameSuffixStripper.StripNationalTeamSportSuffix(away, "Cricket") ?? away;
        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away) ||
            string.Equals(home, away, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedRelease = Normalize(releaseTitle);
        if (!ContainsPhrase(normalizedRelease, Normalize(home)) ||
            !ContainsPhrase(normalizedRelease, Normalize(away)))
        {
            return false;
        }

        if (ReleaseLeagueKey(releaseTitle) == "CWC")
        {
            if (!int.TryParse(evt.Round, out var round)) return false;
            if (round == 200)
                return Regex.IsMatch(releaseTitle, @"\bFinal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (round is > 0 and < 100)
            {
                var match = MatchNumberPattern.Match(releaseTitle);
                return match.Success && int.Parse(match.Groups["number"].Value) == round;
            }
            return false;
        }

        return releaseDate?.Date == eventDate &&
            Regex.IsMatch(releaseTitle, @"\bCricket\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            Regex.IsMatch(releaseTitle, @"\bFull[\s._-]+Match\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            !Regex.IsMatch(releaseTitle, @"\b(?:T20I|ODI)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool HasSplitSeasonYearMatch(string releaseTitle, Event evt)
    {
        if (LeagueKey(evt.League?.Name) != "BBL")
        {
            return false;
        }

        var season = Regex.Match(evt.Season ?? string.Empty, @"^(?<start>20[0-9]{2})-(?<end>[0-9]{2}|20[0-9]{2})$");
        if (!season.Success)
        {
            return false;
        }

        var start = int.Parse(season.Groups["start"].Value);
        var endText = season.Groups["end"].Value;
        var end = endText.Length == 2 ? start / 100 * 100 + int.Parse(endText) : int.Parse(endText);
        if (end < start)
        {
            end += 100;
        }

        var eventYear = (evt.BroadcastDate ?? evt.EventDate.Date).Year;
        return eventYear >= start && eventYear <= end &&
               Regex.IsMatch(releaseTitle, $@"(?<![0-9]){start}(?![0-9])", RegexOptions.CultureInvariant);
    }

    public static string? LeagueKey(string? leagueName)
    {
        if (string.IsNullOrWhiteSpace(leagueName))
        {
            return null;
        }

        if (leagueName.Contains("Indian Premier League", StringComparison.OrdinalIgnoreCase)) return "IPL";
        if (leagueName.Contains("Women", StringComparison.OrdinalIgnoreCase) &&
            leagueName.Contains("Big Bash", StringComparison.OrdinalIgnoreCase)) return "WBBL";
        if (leagueName.Contains("Women", StringComparison.OrdinalIgnoreCase) &&
            leagueName.Contains("Six Nations", StringComparison.OrdinalIgnoreCase)) return "WomensSixNations";
        if (leagueName.Contains("Big Bash", StringComparison.OrdinalIgnoreCase)) return "BBL";
        if (leagueName.Equals("Cricket World Cup", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("ICC Cricket World Cup", StringComparison.OrdinalIgnoreCase)) return "CWC";
        if (leagueName.Contains("Six Nations", StringComparison.OrdinalIgnoreCase)) return "SixNations";
        if (leagueName.Contains("United Rugby Championship", StringComparison.OrdinalIgnoreCase)) return "URC";
        if (leagueName.Contains("Rugby Championship", StringComparison.OrdinalIgnoreCase)) return "RugbyChampionship";
        if (leagueName.Equals("Rugby World Cup", StringComparison.OrdinalIgnoreCase)) return "RugbyWorldCup";
        if (leagueName.Contains("National Rugby League", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(leagueName, @"\bNRL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "NRL";
        return null;
    }

    public static string? ReleaseLeagueKey(string releaseTitle)
    {
        if (Regex.IsMatch(releaseTitle, @"\bWorld[\s._-]+Test[\s._-]+Championship\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "WTC";
        if (Regex.IsMatch(releaseTitle, @"\bCricket[\s._-]+World[\s._-]+Cup\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "CWC";
        if (Regex.IsMatch(releaseTitle, @"\bWPL\b|\bWomen(?:'s|s)?\b.*\bIPL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "WPL";
        if (Regex.IsMatch(releaseTitle, @"\bWBBL\b|\bWomen(?:'s|s)?\b.*\b(?:BBL|Big[\s._-]+Bash)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "WBBL";
        if (Regex.IsMatch(releaseTitle, @"\bNRL[\s._-]+Women(?:'s|s)?\b|\bWomen(?:'s|s)?[\s._-]+NRL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "WNRL";
        if (Regex.IsMatch(releaseTitle, @"\bWomen(?:'s|s)?\b.*\bSix[\s._-]+Nations\b|\bSix[\s._-]+Nations\b.*\bWomen(?:'s|s)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "WomensSixNations";
        if (Regex.IsMatch(releaseTitle, @"\bWPL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "WPL";
        if (Regex.IsMatch(releaseTitle, @"\bIPL(?:\b|(?=20[0-9]{2}\b))|\bIndian[\s._-]+Premier[\s._-]+League\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "IPL";
        if (Regex.IsMatch(releaseTitle, @"\bWBBL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "WBBL";
        if (Regex.IsMatch(releaseTitle, @"\bBBL(?:\b|(?=20[0-9]{2}\b))|\bBig[\s._-]+Bash[\s._-]+League\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "BBL";
        if (Regex.IsMatch(releaseTitle, @"\bSix[\s._-]+Nations\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "SixNations";
        if (Regex.IsMatch(releaseTitle, @"\bURC\b|\bUnited[\s._-]+Rugby[\s._-]+Championship\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "URC";
        if (Regex.IsMatch(releaseTitle, @"\bRugby[\s._-]+Championship\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "RugbyChampionship";
        if (Regex.IsMatch(releaseTitle, @"\bRugby[\s._-]+(?:Union[\s._-]+)?World[\s._-]+Cup\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "RugbyWorldCup";
        if (Regex.IsMatch(releaseTitle, @"\bNRL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "NRL";
        return null;
    }

    private static string BuildBigBashQuery(string? season, int eventYear, int round, string home, string away)
    {
        var seasonText = Regex.Match(season ?? string.Empty, @"(?<start>\d{4})-(?<end>\d{2,4})");
        var start = seasonText.Success ? seasonText.Groups["start"].Value : (eventYear - 1).ToString();
        var end = seasonText.Success ? seasonText.Groups["end"].Value[^2..] : eventYear.ToString()[^2..];
        var stage = round == 200 ? " Final" : string.Empty;
        return $"BBL {start} {end}{stage} {home} {away}";
    }

    private static string BaseRugbyTeam(string value) =>
        Regex.Replace(value.Trim(), @"\s+Rugby$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string NrlSearchName(string canonical)
    {
        var candidate = TeamNameVariationData.Variations
            .Where(pair => canonical.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Key.Length)
            .Select(pair => pair.Value.FirstOrDefault())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return candidate ?? canonical.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
    }

    private static bool HasCricketStageConflict(string releaseTitle, string? eventRoundText)
    {
        if (!int.TryParse(eventRoundText, out var eventRound))
        {
            return false;
        }

        var matchNumber = MatchNumberPattern.Match(releaseTitle);
        if (eventRound is > 0 and < 100 && matchNumber.Success &&
            int.Parse(matchNumber.Groups["number"].Value) != eventRound)
        {
            return true;
        }

        var hasFinal = Regex.IsMatch(releaseTitle, @"\bFinal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var hasQualifier = Regex.IsMatch(releaseTitle, @"\bQualifier\b|\bChallenger\b|\bEliminator\b|\bSemi[\s._-]*Final\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (eventRound is > 0 and < 100)
        {
            return hasFinal || hasQualifier;
        }
        if (eventRound == 200)
        {
            return !hasFinal;
        }
        if (eventRound >= 100)
        {
            return !hasQualifier;
        }

        return false;
    }

    private static bool HasCricketWorldCupStageConflict(string releaseTitle, string? eventRoundText)
    {
        if (!int.TryParse(eventRoundText, out var eventRound)) return false;

        var match = MatchNumberPattern.Match(releaseTitle);
        var hasSemiFinal = Regex.IsMatch(releaseTitle, @"\bSemi[\s._-]*Final\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var hasFinal = !hasSemiFinal &&
            Regex.IsMatch(releaseTitle, @"\bFinal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (eventRound is > 0 and < 100)
            return match.Success && int.Parse(match.Groups["number"].Value) != eventRound || hasFinal || hasSemiFinal;
        if (eventRound == 200)
            return hasSemiFinal || match.Success && !hasFinal;
        if (eventRound >= 100)
            return hasFinal || match.Success && !hasSemiFinal;
        return false;
    }

    private static bool HasConflictingDayMonth(string releaseTitle, DateTime eventDate)
    {
        var fullDates = YearMonthDayPattern.Matches(releaseTitle)
            .Where(match => !IsAudioChannelCount(releaseTitle, match))
            .ToArray();
        if (fullDates.Length > 0)
        {
            return !fullDates.Any(match =>
                int.Parse(match.Groups["year"].Value) == eventDate.Year &&
                int.Parse(match.Groups["month"].Value) == eventDate.Month &&
                int.Parse(match.Groups["day"].Value) == eventDate.Day);
        }

        var dates = DayMonthPattern.Matches(releaseTitle)
            .Where(match => !IsAudioChannelCount(releaseTitle, match))
            .ToArray();
        if (dates.Length == 0)
        {
            return false;
        }

        return !dates.Any(match =>
            int.Parse(match.Groups["day"].Value) == eventDate.Day &&
            int.Parse(match.Groups["month"].Value) == eventDate.Month &&
            (!match.Groups["year"].Success || int.Parse(match.Groups["year"].Value) == eventDate.Year));
    }

    private static bool HasExactDayMonth(string releaseTitle, DateTime eventDate)
    {
        var fullDates = YearMonthDayPattern.Matches(releaseTitle)
            .Where(match => !IsAudioChannelCount(releaseTitle, match))
            .ToArray();
        if (fullDates.Length > 0)
        {
            return fullDates.Any(match =>
                int.Parse(match.Groups["year"].Value) == eventDate.Year &&
                int.Parse(match.Groups["month"].Value) == eventDate.Month &&
                int.Parse(match.Groups["day"].Value) == eventDate.Day);
        }

        return DayMonthPattern.Matches(releaseTitle)
            .Where(match => !IsAudioChannelCount(releaseTitle, match))
            .Any(match =>
            int.Parse(match.Groups["day"].Value) == eventDate.Day &&
            int.Parse(match.Groups["month"].Value) == eventDate.Month &&
            (!match.Groups["year"].Success || int.Parse(match.Groups["year"].Value) == eventDate.Year));
    }

    private static bool IsAudioChannelCount(string releaseTitle, Match match) =>
        match.Groups["day"].Success &&
        int.Parse(match.Groups["day"].Value) <= 9 &&
        AudioChannelPrefixPattern.IsMatch(releaseTitle[..match.Index]);

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{M}\p{N}]+", " ").Trim();

    private static bool ContainsPhrase(string normalizedTitle, string normalizedPhrase) =>
        Regex.IsMatch(normalizedTitle, $@"(?:^| )({Regex.Escape(normalizedPhrase)})(?: |$)", RegexOptions.CultureInvariant);
}
