using System.Text.RegularExpressions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

public static class LeagueReleaseNamePolicy
{
    private static readonly Regex AflRoundPattern = new(@"\bRound[\s._-]*0*(?<round>[1-9][0-9]?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AflFinalPattern = new(@"\b(?:WC|QF|EF|SF|GF)[\s._-]*[1-9]?\b|\bGrand[\s._-]+Final\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RoundPattern = new(@"\b(?:Round|R)[\s._-]*0*(?<round>[1-9][0-9]?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RacePattern = new(@"\bRaces?[\s._-]*0*(?<race>[1-9][0-9]?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DayMonthYearPattern = new(@"(?<![0-9])(?<day>0?[1-9]|[12][0-9]|3[01])[\s._/-]+(?<month>0?[1-9]|1[0-2])[\s._/-]+(?<year>20[0-9]{2})(?![0-9])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SnookerDayPattern = new(
        @"\bDay[\s._-]*0*(?<number>[1-9][0-9]?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SnookerPartPattern = new(
        @"\b(?:Part|Session|Day)[\s._-]*0*(?<number>[1-9][0-9]?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? BuildQuery(Event evt)
    {
        var league = LeagueKey(evt.League?.Name);
        var date = evt.BroadcastDate ?? evt.EventDate.Date;
        var (home, away) = EventQueryService.ResolveTeamNames(evt);

        if (league == "FISAlpine")
        {
            var location = SkiLocation(evt);
            return location == null ? null : $"FIS Alpine {date.Year} {location}";
        }
        if (league == "OlympicsSwimming") return $"Olympics {OlympicEdition(evt, date)} Swimming {date:MM dd}";
        if (league == "DiamondLeague")
        {
            var meeting = DiamondLeagueMeeting(evt.Title);
            return meeting == null ? null : $"Diamond League {date.Year} {meeting}";
        }
        if (league == "OlympicsSkateboarding")
        {
            var eventGender = Gender(evt.Title);
            var gender = eventGender == "Women" ? "Womens" : eventGender == "Men" ? "Mens" : null;
            var discipline = SkateboardingDiscipline(evt.Title);
            return gender == null || discipline == null
                ? null
                : $"Olympics {OlympicEdition(evt, date)} Skateboarding {gender} {discipline}";
        }

        if (league == "Snooker")
        {
            var tournament = SnookerTournament(evt.Title);
            return tournament == null ? null : $"Snooker {date.Year} {tournament}";
        }
        if (league == "Supercars")
        {
            var race = RacePattern.Match(evt.Title ?? string.Empty);
            return race.Success ? $"Supercars {date.Year} Race {int.Parse(race.Groups["race"].Value)}" : null;
        }
        if (league == "FormulaE") return $"FormulaE {date.Year}";
        if (league == "IMSA" && int.TryParse(evt.Round, out var imsaRound)) return $"IMSA {date.Year} Round{imsaRound:D2}";

        if (league == "PDC")
        {
            var tournament = Regex.Replace(evt.Title ?? string.Empty, @"^(?:Winmau|Blåkläder)\s+|\s+Day\s+[0-9]+$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
            return string.IsNullOrWhiteSpace(tournament) ? null : $"PDC {date.Year} {tournament}";
        }

        if (league == null || string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away)) return null;
        var homeSearch = SearchTeamName(home);
        var awaySearch = SearchTeamName(away);
        return league switch
        {
            "AFL" => $"AFL {date.Year} {homeSearch} {awaySearch}",
            "EuroLeague" => $"EuroLeague {date.Year} {homeSearch} {awaySearch}",
            "NCAAF" => $"NCAAF {date.Year} {homeSearch} {awaySearch}",
            "NCAAM" => $"NCAAM {date.Year} {homeSearch} {awaySearch} {date:dd MM}",
            _ => null
        };
    }

    public static bool HasIdentityConflict(string releaseTitle, Event evt)
    {
        var league = LeagueKey(evt.League?.Name);
        if (league == "AFL")
        {
            if (Regex.IsMatch(releaseTitle, @"\bAFLW\b|\bAFL[\s._-]+Women", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
            if (!int.TryParse(evt.Round, out var eventRound)) return false;
            var releaseRound = AflRoundPattern.Match(releaseTitle);
            if (eventRound < 100) return releaseRound.Success && int.Parse(releaseRound.Groups["round"].Value) != eventRound;
            if (releaseRound.Success) return true;
            var expected = eventRound switch { 100 => "WC", 125 => "QF", 150 => "SF", 160 => "EF", 200 => "GF", _ => null };
            if (expected == null) return false;
            var expectedPattern = $@"\b{expected}[\s._-]*[1-9]?\b" + (expected == "GF" ? @"|\bGrand[\s._-]+Final\b" : string.Empty);
            return !AflFinalPattern.IsMatch(releaseTitle) || !Regex.IsMatch(releaseTitle, expectedPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (league is "NCAAF" or "NCAAM")
        {
            var releaseLeague = ReleaseCollegeLeague(releaseTitle);
            return (releaseLeague != null && !string.Equals(league, releaseLeague, StringComparison.Ordinal)) ||
                HasCollegeYearConflict(releaseTitle, evt);
        }
        if (league == "EuroLeague" && Regex.IsMatch(releaseTitle, @"\bPOG[0-9]+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return true;
        }
        if (league == "PDC") return HasPdcConflict(releaseTitle, evt);
        if (league == "Snooker") return HasSnookerConflict(releaseTitle, evt);
        if (league == "Supercars") return HasSupercarsConflict(releaseTitle, evt);
        if (league == "FormulaE") return HasFormulaEConflict(releaseTitle, evt);
        if (league == "IMSA") return HasImsaConflict(releaseTitle, evt);
        if (league == "FISAlpine") return HasAlpineConflict(releaseTitle, evt);
        if (league == "OlympicsSwimming") return HasOlympicSwimmingConflict(releaseTitle, evt);
        if (league == "DiamondLeague") return HasDiamondLeagueConflict(releaseTitle, evt);
        if (league == "OlympicsSkateboarding") return HasOlympicSkateboardingConflict(releaseTitle, evt);
        return false;
    }

    public static bool HasStrongEventIdentity(string releaseTitle, Event evt)
    {
        var league = LeagueKey(evt.League?.Name);
        if (league is null || HasIdentityConflict(releaseTitle, evt)) return false;
        if (league == "Snooker")
        {
            var tournament = SnookerTournament(evt.Title);
            return tournament != null &&
                Regex.IsMatch(releaseTitle, @"\bSnooker\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                HasSnookerTournamentIdentity(releaseTitle, tournament) &&
                (HasExactDate(releaseTitle, evt.BroadcastDate ?? evt.EventDate.Date) ||
                 HasMatchingUndatedFinal(releaseTitle, evt));
        }
        if (league == "Supercars") return HasSupercarsIdentity(releaseTitle, evt);
        if (league == "FormulaE") return HasFormulaEIdentity(releaseTitle, evt);
        if (league == "IMSA") return HasImsaIdentity(releaseTitle, evt);
        if (league == "FISAlpine") return HasAlpineIdentity(releaseTitle, evt);
        if (league == "OlympicsSwimming") return HasOlympicSwimmingIdentity(releaseTitle, evt);
        if (league == "DiamondLeague") return HasDiamondLeagueIdentity(releaseTitle, evt);
        if (league == "OlympicsSkateboarding") return HasOlympicSkateboardingIdentity(releaseTitle, evt);
        if (league == "PDC")
        {
            var tournament = Regex.Replace(
                evt.Title ?? string.Empty,
                @"^(?:Winmau|Blåkläder)\s+|\s+Day\s+[0-9]+$",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
            return !string.IsNullOrWhiteSpace(tournament) &&
                Regex.IsMatch(releaseTitle, @"\bPDC\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                ContainsPhrase(Normalize(releaseTitle), Normalize(tournament));
        }
        if (league is not ("AFL" or "EuroLeague" or "NCAAF" or "NCAAM")) return false;
        if (league == "AFL" && !Regex.IsMatch(releaseTitle, @"\bAFL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return false;
        if (league == "EuroLeague" && !Regex.IsMatch(releaseTitle, @"\bEuro[\s._-]*League\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return false;
        if (league is "NCAAF" or "NCAAM" && !string.Equals(league, ReleaseCollegeLeague(releaseTitle), StringComparison.Ordinal)) return false;
        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        return HasTeam(releaseTitle, home) && HasTeam(releaseTitle, away);
    }

    public static bool HasUnresolvedSupercarsRaceIdentity(string releaseTitle, Event evt)
    {
        if (LeagueKey(evt.League?.Name) != "Supercars") return false;
        var releaseRound = RoundPattern.Match(releaseTitle);
        var eventRace = RacePattern.Match(evt.Title ?? string.Empty);
        if (!releaseRound.Success || !eventRace.Success ||
            !int.TryParse(evt.Round, out var eventRound) ||
            int.Parse(releaseRound.Groups["round"].Value) != eventRound)
        {
            return false;
        }

        var releaseRaces = SupercarsRaceNumbers(releaseTitle);
        return releaseRaces.Length > 0 &&
            !releaseRaces.Contains(int.Parse(eventRace.Groups["race"].Value));
    }

    public static bool? EvaluateSupercarsRoundRaceIdentity(
        string releaseTitle,
        Event evt,
        IReadOnlyList<int>? roundRaceNumbers)
    {
        if (LeagueKey(evt.League?.Name) == "Supercars" &&
            roundRaceNumbers is { Count: > 0 } &&
            SupercarsRaceNumbers(releaseTitle).Length == 0)
        {
            var releaseRound = RoundPattern.Match(releaseTitle);
            var singleRoundEventRace = RacePattern.Match(evt.Title ?? string.Empty);
            if (releaseRound.Success && singleRoundEventRace.Success &&
                int.TryParse(evt.Round, out var eventRound) &&
                int.Parse(releaseRound.Groups["round"].Value) == eventRound)
            {
                var singleRoundRaces = roundRaceNumbers.Distinct().OrderBy(number => number).ToArray();
                return singleRoundRaces.Length == 1 &&
                    singleRoundRaces[0] == int.Parse(singleRoundEventRace.Groups["race"].Value);
            }
        }

        if (!HasUnresolvedSupercarsRaceIdentity(releaseTitle, evt)) return null;
        if (roundRaceNumbers is not { Count: > 0 }) return null;

        var eventRace = RacePattern.Match(evt.Title ?? string.Empty);
        var ordered = roundRaceNumbers.Distinct().OrderBy(number => number).ToArray();
        var mappedRaces = SupercarsRaceNumbers(releaseTitle)
            .Where(number => number <= ordered.Length)
            .Select(number => ordered[number - 1])
            .ToArray();

        return mappedRaces.Length > 0 &&
            mappedRaces.Contains(int.Parse(eventRace.Groups["race"].Value));
    }

    public static string? LeagueKey(string? leagueName)
    {
        if (string.IsNullOrWhiteSpace(leagueName)) return null;
        if (leagueName.Contains("Australian AFL", StringComparison.OrdinalIgnoreCase)) return "AFL";
        if (leagueName.Contains("EuroLeague", StringComparison.OrdinalIgnoreCase)) return "EuroLeague";
        if (leagueName.Contains("NCAA Division I Basketball Mens", StringComparison.OrdinalIgnoreCase)) return "NCAAM";
        if (leagueName.Contains("NCAA Division 1", StringComparison.OrdinalIgnoreCase)) return "NCAAF";
        if (leagueName.Contains("PDC Darts", StringComparison.OrdinalIgnoreCase)) return "PDC";
        if (leagueName.Contains("World Snooker", StringComparison.OrdinalIgnoreCase)) return "Snooker";
        if (leagueName.Contains("Supercars", StringComparison.OrdinalIgnoreCase)) return "Supercars";
        if (leagueName.Equals("Formula E", StringComparison.OrdinalIgnoreCase)) return "FormulaE";
        if (leagueName.Contains("IMSA SportsCar", StringComparison.OrdinalIgnoreCase)) return "IMSA";
        if (leagueName.Contains("FIS Alpine Ski", StringComparison.OrdinalIgnoreCase)) return "FISAlpine";
        if (leagueName.Equals("Olympics Swimming", StringComparison.OrdinalIgnoreCase)) return "OlympicsSwimming";
        if (leagueName.Contains("Diamond League", StringComparison.OrdinalIgnoreCase)) return "DiamondLeague";
        if (leagueName.Equals("Olympics Skateboarding", StringComparison.OrdinalIgnoreCase)) return "OlympicsSkateboarding";
        return null;
    }

    public static string? ReleaseLeagueKey(string title)
    {
        if (Regex.IsMatch(title, @"\bAFLW\b|\bAFL[\s._-]+Women", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "AFLW";
        if (Regex.IsMatch(title, @"\bAFL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "AFL";
        if (Regex.IsMatch(title, @"\bEuro[\s._-]*League\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "EuroLeague";
        if (ReleaseCollegeLeague(title) is { } college) return college;
        if (Regex.IsMatch(title, @"\bPDC\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "PDC";
        if (Regex.IsMatch(title, @"\bSnooker\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Snooker";
        if (Regex.IsMatch(title, @"\bSupercars?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Supercars";
        if (Regex.IsMatch(title, @"\bIMSA\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "IMSA";
        if (IsAlpineRelease(title)) return "FISAlpine";
        if (IsOlympicSwimmingRelease(title)) return "OlympicsSwimming";
        if (IsDiamondLeagueRelease(title)) return "DiamondLeague";
        if (IsOlympicSkateboardingRelease(title)) return "OlympicsSkateboarding";
        return null;
    }

    public static bool UsesCompleteCatalogIdentity(Event evt) => LeagueKey(evt.League?.Name) is
        "FISAlpine" or "OlympicsSwimming" or "DiamondLeague" or "OlympicsSkateboarding";

    private static bool HasAlpineConflict(string title, Event evt)
    {
        if (!IsAlpineRelease(title)) return false;
        var eventGender = Gender(evt.Title);
        var releaseGender = Gender(title);
        if (eventGender != null && releaseGender != null && eventGender != releaseGender) return true;
        var eventDiscipline = AlpineDiscipline(evt.Title);
        var releaseDiscipline = AlpineDiscipline(title);
        if (eventDiscipline != null && releaseDiscipline != null && eventDiscipline != releaseDiscipline) return true;
        return !HasCatalogDate(title, evt);
    }

    private static bool HasAlpineIdentity(string title, Event evt)
    {
        if (!IsAlpineRelease(title) || HasAlpineConflict(title, evt)) return false;
        var eventGender = Gender(evt.Title);
        var eventDiscipline = AlpineDiscipline(evt.Title);
        var location = SkiLocation(evt);
        return eventGender != null && Gender(title) == eventGender &&
            eventDiscipline != null && AlpineDiscipline(title) == eventDiscipline &&
            location != null && ContainsLocation(title, location);
    }

    private static bool HasOlympicSwimmingConflict(string title, Event evt)
    {
        if (!IsOlympicSwimmingRelease(title)) return false;
        if (!HasOlympicEdition(title, evt) || !HasCatalogDate(title, evt)) return true;

        var eventIdentity = SwimmingIdentity(evt.Title);
        var releaseIdentity = SwimmingIdentity(title);
        if (eventIdentity.Gender != null && releaseIdentity.Gender != null &&
            eventIdentity.Gender != releaseIdentity.Gender) return true;
        if (eventIdentity.Stage != null && releaseIdentity.Stage != null &&
            eventIdentity.Stage != releaseIdentity.Stage) return true;
        if (eventIdentity.StageNumber != null && releaseIdentity.StageNumber != null &&
            eventIdentity.StageNumber != releaseIdentity.StageNumber) return true;
        if (releaseIdentity.DistanceMetres != null &&
            eventIdentity.DistanceMetres != releaseIdentity.DistanceMetres) return true;
        if (releaseIdentity.Stroke != null &&
            eventIdentity.Stroke != releaseIdentity.Stroke) return true;
        if (!releaseIdentity.IsSpecific) return false;
        if (!eventIdentity.IsSpecific) return true;
        return eventIdentity.IsRelay != releaseIdentity.IsRelay;
    }

    private static bool HasOlympicSwimmingIdentity(string title, Event evt)
    {
        if (!IsOlympicSwimmingRelease(title) || HasOlympicSwimmingConflict(title, evt)) return false;
        var eventIdentity = SwimmingIdentity(evt.Title);
        var releaseIdentity = SwimmingIdentity(title);
        if (releaseIdentity.IsSpecific) return eventIdentity.IsSpecific;
        if (Regex.IsMatch(title, @"\bSession\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
        return eventIdentity.Stage != null && eventIdentity.Stage == releaseIdentity.Stage;
    }

    private static bool HasDiamondLeagueConflict(string title, Event evt)
    {
        if (!IsDiamondLeagueRelease(title)) return false;
        var meeting = DiamondLeagueMeeting(evt.Title);
        return meeting != null && (!ContainsLocation(title, meeting) || HasYearConflict(title, evt));
    }

    private static bool HasDiamondLeagueIdentity(string title, Event evt)
    {
        var meeting = DiamondLeagueMeeting(evt.Title);
        return meeting != null &&
            IsDiamondLeagueRelease(title) &&
            !HasDiamondLeagueConflict(title, evt);
    }

    private static bool HasOlympicSkateboardingConflict(string title, Event evt)
    {
        if (!IsOlympicSkateboardingRelease(title)) return false;
        if (!HasOlympicEdition(title, evt) || !HasCatalogDate(title, evt)) return true;
        var eventGender = Gender(evt.Title);
        var releaseGender = Gender(title);
        if (eventGender != null && releaseGender != null && eventGender != releaseGender) return true;
        var eventDiscipline = SkateboardingDiscipline(evt.Title);
        var releaseDiscipline = SkateboardingDiscipline(title);
        if (eventDiscipline != null && releaseDiscipline != null && eventDiscipline != releaseDiscipline) return true;
        var eventStage = CompetitionStage(evt.Title);
        var releaseStage = CompetitionStage(title);
        return eventStage != null && releaseStage != null && eventStage != releaseStage;
    }

    private static bool HasOlympicSkateboardingIdentity(string title, Event evt)
    {
        if (!IsOlympicSkateboardingRelease(title) || HasOlympicSkateboardingConflict(title, evt)) return false;
        var eventGender = Gender(evt.Title);
        var eventDiscipline = SkateboardingDiscipline(evt.Title);
        var eventStage = CompetitionStage(evt.Title);
        var releaseStage = CompetitionStage(title);
        var stageMatches = eventStage == null || eventStage == releaseStage;
        return eventGender != null && Gender(title) == eventGender &&
            eventDiscipline != null && SkateboardingDiscipline(title) == eventDiscipline &&
            stageMatches;
    }

    private static bool IsAlpineRelease(string title) => Regex.IsMatch(title,
        @"\bFIS[\s._-]+Alpine[\s._-]+Ski(?:ing)?[\s._-]+World[\s._-]+Cup\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsOlympicSwimmingRelease(string title) =>
        Regex.IsMatch(title, @"\bOlympics?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
        Regex.IsMatch(title, @"\bSwimming\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsDiamondLeagueRelease(string title) => Regex.IsMatch(title,
        @"\bDiamond[\s._-]+League\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsOlympicSkateboardingRelease(string title) =>
        Regex.IsMatch(title, @"\bOlympics?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
        Regex.IsMatch(title, @"\bSkateboarding\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? Gender(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        if (Regex.IsMatch(title, @"\bMixed\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Mixed";
        if (Regex.IsMatch(title, @"\bWomen(?:'s|s)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Women";
        if (Regex.IsMatch(title, @"\bMen(?:'s|s)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Men";
        return null;
    }

    private static string? AlpineDiscipline(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        if (Regex.IsMatch(title, @"\bGiant[\s._-]+Slalom\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "GiantSlalom";
        if (Regex.IsMatch(title, @"\bSuper[\s._-]*G\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "SuperG";
        if (Regex.IsMatch(title, @"\bDownhill\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Downhill";
        if (Regex.IsMatch(title, @"\bSlalom\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Slalom";
        return null;
    }

    private static string? SkateboardingDiscipline(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        if (Regex.IsMatch(title, @"\bPark\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Park";
        if (Regex.IsMatch(title, @"\bStreet\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Street";
        return null;
    }

    private static SwimmingEventIdentity SwimmingIdentity(string? title)
    {
        title ??= string.Empty;
        var distance = Regex.Match(title, @"(?<![0-9])(?<distance>[1-9][0-9]{1,3})[\s._-]*m\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var distanceMetres = distance.Success ? int.Parse(distance.Groups["distance"].Value) : (int?)null;
        if (distanceMetres == null)
        {
            var kilometres = Regex.Match(title, @"(?<![0-9])(?<distance>[1-9][0-9]?)[\s._-]*km\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (kilometres.Success) distanceMetres = int.Parse(kilometres.Groups["distance"].Value) * 1000;
        }

        var stroke = Regex.Match(title,
            @"\b(?<stroke>Freestyle|Butterfly|Backstroke|Breaststroke|Medley|Marathon|Synchronized|Artistic)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var strokeName = stroke.Success ? stroke.Groups["stroke"].Value.ToLowerInvariant() : null;
        var isRelay = Regex.IsMatch(title, @"\bRelay\b|(?<![0-9])4[\s._-]*x[\s._-]*[0-9]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var stage = CompetitionStage(title);
        return new SwimmingEventIdentity(
            Gender(title),
            distanceMetres,
            strokeName,
            isRelay,
            stage,
            CompetitionStageNumber(title, stage),
            (distanceMetres != null && strokeName != null) || isRelay);
    }

    private static string? CompetitionStage(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        if (Regex.IsMatch(title, @"\bSemi[\s._-]*Finals?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Semifinal";
        if (Regex.IsMatch(title, @"\b(?:Heats?|Prelims?|Qualif(?:ication|ying|iers?)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Preliminary";
        if (Regex.IsMatch(title, @"\bFinals?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Final";
        return null;
    }

    private static int? CompetitionStageNumber(string title, string? stage)
    {
        var pattern = stage switch
        {
            "Semifinal" => @"\bSemi[\s._-]*Finals?[\s._-]*(?<number>[1-9][0-9]?)\b",
            "Preliminary" => @"\b(?:Heats?|Prelims?|Qualif(?:ication|ying|iers?)?)[\s._-]*(?<number>[1-9][0-9]?)\b",
            "Final" => @"\bFinals?[\s._-]*(?<number>[1-9][0-9]?)\b",
            _ => null
        };
        if (pattern == null) return null;
        var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? int.Parse(match.Groups["number"].Value) : null;
    }

    private sealed record SwimmingEventIdentity(
        string? Gender,
        int? DistanceMetres,
        string? Stroke,
        bool IsRelay,
        string? Stage,
        int? StageNumber,
        bool IsSpecific);

    private static string? SkiLocation(Event evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.Location)) return evt.Location.Trim();
        var match = Regex.Match(evt.Title ?? string.Empty, @"\bat[\s._-]+(?<location>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["location"].Value.Trim() : null;
    }

    private static string? DiamondLeagueMeeting(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var match = Regex.Match(title,
            @"\bMeeting[\s._-]+(?:de|of|di|del|der)[\s._-]+(?<meeting>[\p{L}\p{M}'-]+(?:[\s._-]+[\p{L}\p{M}'-]+){0,2})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? Regex.Replace(match.Groups["meeting"].Value, @"[\s._-]+", " ").Trim() : null;
    }

    private static int OlympicEdition(Event evt, DateTime date) =>
        int.TryParse(evt.Season, out var season) ? season : date.Year;

    private static bool HasOlympicEdition(string title, Event evt)
    {
        var edition = OlympicEdition(evt, evt.BroadcastDate ?? evt.EventDate.Date);
        return Regex.IsMatch(title, $@"\bOlympics?[\s._-]*{edition}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasCatalogDate(string title, Event evt) =>
        SearchNormalizationService.HasDayMonthDateToken(title, evt.BroadcastDate ?? evt.EventDate.Date);

    private static bool ContainsLocation(string title, string location)
    {
        var normalizedTitle = Normalize(title);
        var words = Normalize(location).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 2 && word is not "the")
            .ToArray();
        return words.Length > 0 && words.All(word =>
            Regex.IsMatch(normalizedTitle, $@"(?:^| ){Regex.Escape(word)}(?: |$)", RegexOptions.CultureInvariant));
    }

    private static string? SnookerTournament(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var value = Regex.Replace(title, @"^(?:Halo|BetVictor)\s+", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        value = Regex.Replace(value, @"\bSnooker\b|\b(?:Week|Day)\s+[0-9]+\b|\b(?:Quarter[\s._-]*Final|Semi[\s._-]*Final|Final)\b", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(value, @"\s+", " ").Trim() is { Length: > 0 } tournament ? tournament : null;
    }

    private static bool HasSnookerConflict(string title, Event evt)
    {
        if (Regex.IsMatch(title, @"\bTennis\b|\bATP\b|\bWTA\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
        if (HasYearConflict(title, evt) || HasConflictingDate(title, evt.BroadcastDate ?? evt.EventDate.Date)) return true;
        var eventTitle = evt.Title ?? string.Empty;
        var eventStage = DetectSnookerStage(eventTitle);
        var releaseStage = DetectSnookerStage(title);
        if (eventStage != null && releaseStage != null && eventStage != releaseStage) return true;
        if (eventStage == "Final" && Regex.IsMatch(title,
            @"\bRound[\s._-]+[1-9]\b|\bR[12](?:S[1-9])?[\s._-]*[1-9]?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
        var eventDay = SnookerDayPattern.Match(eventTitle);
        var releasePart = SnookerPartPattern.Match(title);
        if (eventDay.Success && releasePart.Success &&
            eventDay.Groups["number"].Value != releasePart.Groups["number"].Value)
        {
            return true;
        }
        return false;
    }

    private static string? DetectSnookerStage(string title)
    {
        if (Regex.IsMatch(title, @"\bQuarter[\s._-]*Final\b|\bQF(?:[\s._-]*[1-9])?\b|\b1/4[\s._-]+Final\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "QuarterFinal";
        if (Regex.IsMatch(title, @"\bSemi[\s._-]*Final\b|\bSF(?:[\s._-]*[1-9])?\b|\b1/2[\s._-]+Final\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "SemiFinal";
        if (Regex.IsMatch(title, @"\bFinal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Final";
        return null;
    }

    private static bool HasPdcConflict(string title, Event evt)
    {
        if (HasYearConflict(title, evt)) return true;
        var eventDay = SnookerDayPattern.Match(evt.Title ?? string.Empty);
        var releaseDay = SnookerDayPattern.Match(title);
        return eventDay.Success && (!releaseDay.Success ||
            eventDay.Groups["number"].Value != releaseDay.Groups["number"].Value);
    }

    private static bool HasSnookerTournamentIdentity(string title, string tournament)
    {
        var normalizedTitle = Normalize(title);
        if (string.Equals(Normalize(tournament), "world championship", StringComparison.Ordinal))
        {
            return Regex.IsMatch(normalizedTitle, @"\bworld (?:snooker )?championships?\b", RegexOptions.CultureInvariant);
        }
        return ContainsPhrase(normalizedTitle, Normalize(tournament));
    }

    private static bool HasMatchingUndatedFinal(string title, Event evt)
    {
        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        var eventTitle = evt.Title ?? string.Empty;
        var eventDay = SnookerDayPattern.Match(eventTitle);
        var releasePart = SnookerPartPattern.Match(title);
        return Regex.IsMatch(eventTitle, @"\bFinal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            Regex.IsMatch(title, @"\bFinal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            Regex.IsMatch(title, $@"(?<![0-9]){eventDate.Year}(?![0-9])", RegexOptions.CultureInvariant) &&
            (!eventDay.Success || releasePart.Success &&
                eventDay.Groups["number"].Value == releasePart.Groups["number"].Value);
    }

    private static bool HasSupercarsConflict(string title, Event evt)
    {
        if (HasYearConflict(title, evt) || HasMotorsportSessionConflict(title, evt)) return true;
        var releaseRound = RoundPattern.Match(title);
        if (releaseRound.Success)
        {
            return int.TryParse(evt.Round, out var eventRound) &&
                int.Parse(releaseRound.Groups["round"].Value) != eventRound;
        }

        var eventRace = RacePattern.Match(evt.Title ?? string.Empty);
        if (!eventRace.Success) return false;
        var expected = int.Parse(eventRace.Groups["race"].Value);
        var releaseRaces = SupercarsRaceNumbers(title);
        return releaseRaces.Length > 0 && !releaseRaces.Contains(expected);
    }

    private static bool HasFormulaEConflict(string title, Event evt)
    {
        if (Regex.IsMatch(title, @"\bFormula[\s._-]*1\b|\bF1\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
        if (HasYearConflict(title, evt)) return true;
        if (int.TryParse(evt.Round, out var expectedRound))
        {
            var releaseRound = RoundPattern.Match(title);
            if (releaseRound.Success && int.Parse(releaseRound.Groups["round"].Value) != expectedRound) return true;
        }
        return HasMotorsportSessionConflict(title, evt);
    }

    private static bool HasImsaConflict(string title, Event evt)
    {
        if (Regex.IsMatch(title,
            @"\bIMSA[\s._-]+(?:MX[\s._-]*5[\s._-]+Cup|Pilot[\s._-]+Challenge|VP[\s._-]+Racing[\s._-]+SportsCar[\s._-]+Challenge)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
        if (HasYearConflict(title, evt) || HasMotorsportSessionConflict(title, evt)) return true;
        if (!int.TryParse(evt.Round, out var expectedRound)) return false;
        var releaseRound = RoundPattern.Match(title);
        return releaseRound.Success && int.Parse(releaseRound.Groups["round"].Value) != expectedRound;
    }

    private static bool HasSupercarsIdentity(string title, Event evt)
    {
        if (!Regex.IsMatch(title, @"\bSupercars?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return false;
        if (RoundPattern.IsMatch(title)) return false;
        var eventRace = RacePattern.Match(evt.Title ?? string.Empty);
        return eventRace.Success && SupercarsRaceNumbers(title)
            .Contains(int.Parse(eventRace.Groups["race"].Value));
    }

    private static int[] SupercarsRaceNumbers(string title)
    {
        var match = Regex.Match(
            title,
            @"\bRaces?[\s._-]*0*(?<race>[1-9][0-9]?)(?:[\s._-]+(?:and|&)[\s._-]+0*(?<race>[1-9][0-9]?))*\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
            ? match.Groups["race"].Captures.Select(capture => int.Parse(capture.Value)).ToArray()
            : Array.Empty<int>();
    }

    private static bool HasFormulaEIdentity(string title, Event evt)
    {
        if (!Regex.IsMatch(title, @"\bFormula[\s._-]*E\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !int.TryParse(evt.Round, out var expectedRound)) return false;
        var releaseRound = RoundPattern.Match(title);
        return releaseRound.Success && int.Parse(releaseRound.Groups["round"].Value) == expectedRound;
    }

    private static bool HasImsaIdentity(string title, Event evt)
    {
        if (!Regex.IsMatch(title, @"\bIMSA\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !int.TryParse(evt.Round, out var expectedRound)) return false;
        var releaseRound = RoundPattern.Match(title);
        return releaseRound.Success && int.Parse(releaseRound.Groups["round"].Value) == expectedRound;
    }

    private static bool HasMotorsportSessionConflict(string title, Event evt)
    {
        if (LeagueKey(evt.League?.Name) == "Supercars")
        {
            var releaseIdentity = EventPartDetector.DetectMotorsportSessionIdentity(
                title, evt.League?.Name, releaseTitle: true);
            var eventIdentity = EventPartDetector.DetectMotorsportSessionIdentity(
                evt.Title ?? string.Empty, evt.League?.Name, releaseTitle: false) ?? "Race";
            if (releaseIdentity == null) return eventIdentity != "Race";
            return !string.Equals(eventIdentity, releaseIdentity, StringComparison.OrdinalIgnoreCase);
        }

        var releaseSession = MotorsportSession(title);
        var eventSession = MotorsportSession(evt.Title ?? string.Empty) ?? "Race";
        if (releaseSession == null) return eventSession != "Race";
        return !string.Equals(eventSession, releaseSession, StringComparison.Ordinal);
    }

    private static string? MotorsportSession(string title)
    {
        if (Regex.IsMatch(title, @"\bQualifying\b|\bQuali\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Qualifying";
        if (Regex.IsMatch(title, @"\bFP[1-9]\b|\bPractice\b|\bWarm[\s._-]*Up\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Practice";
        if (Regex.IsMatch(title, @"\bRace(?:[\s._-]+(?:One|Two|1|2))?\b|\bE[\s._-]*Prix\b|\b24[\s._-]*(?:Hours?|H)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Race";
        return null;
    }

    private static bool HasYearConflict(string title, Event evt)
    {
        var years = Regex.Matches(title, @"(?<![0-9])20[0-9]{2}(?![0-9])").Select(match => int.Parse(match.Value)).ToArray();
        return years.Length > 0 && years.All(year => year != (evt.BroadcastDate ?? evt.EventDate.Date).Year);
    }

    private static bool HasConflictingDate(string title, DateTime eventDate)
    {
        var dates = DayMonthYearPattern.Matches(title);
        return dates.Count > 0 && !dates.Any(match =>
            int.Parse(match.Groups["day"].Value) == eventDate.Day &&
            int.Parse(match.Groups["month"].Value) == eventDate.Month &&
            int.Parse(match.Groups["year"].Value) == eventDate.Year);
    }

    private static bool HasExactDate(string title, DateTime eventDate) =>
        DayMonthYearPattern.Matches(title).Any(match =>
            int.Parse(match.Groups["day"].Value) == eventDate.Day &&
            int.Parse(match.Groups["month"].Value) == eventDate.Month &&
            int.Parse(match.Groups["year"].Value) == eventDate.Year);

    private static string? ReleaseCollegeLeague(string title)
    {
        if (Regex.IsMatch(title, @"\bNCAAF\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "NCAAF";
        if (Regex.IsMatch(title, @"\bNCAAM\b|\bNCAABM?\b|\bNCAA[\s._-]+(?:Men(?:'s)?[\s._-]+)?Basketball\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "NCAAM";
        if (Regex.IsMatch(title, @"\bNCAA[\s._-]+(?:Women(?:'s)?[\s._-]+Basketball|Baseball|Volleyball)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "OtherNCAA";
        return null;
    }

    private static string SearchTeamName(string value) => Regex.Replace(value.Trim(), @"\s+(?:Football Club|BC|Basket|Baloncesto)$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool HasCollegeYearConflict(string title, Event evt)
    {
        var years = Regex.Matches(title, @"(?<![0-9])(?:19|20)[0-9]{2}(?![0-9])")
            .Select(match => int.Parse(match.Value))
            .ToArray();
        if (years.Length == 0) return false;
        var allowed = new HashSet<int> { (evt.BroadcastDate ?? evt.EventDate.Date).Year };
        foreach (Match match in Regex.Matches(evt.Season ?? string.Empty, @"(?:19|20)[0-9]{2}")) allowed.Add(int.Parse(match.Value));
        var splitEnd = Regex.Match(evt.Season ?? string.Empty, @"^(?<start>20[0-9]{2})-(?<end>[0-9]{2})$");
        if (splitEnd.Success) allowed.Add(int.Parse(splitEnd.Groups["start"].Value[..2] + splitEnd.Groups["end"].Value));
        return years.All(year => !allowed.Contains(year));
    }

    private static bool HasTeam(string title, string? canonical)
    {
        if (string.IsNullOrWhiteSpace(canonical)) return false;
        var aliases = new List<string> { canonical, SearchTeamName(canonical) };
        aliases.AddRange(TeamNameVariationData.Variations.Where(pair => canonical.Contains(pair.Key, StringComparison.OrdinalIgnoreCase)).SelectMany(pair => pair.Value));
        var normalizedTitle = Normalize(title);
        return aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)).Any(alias =>
            Regex.IsMatch(normalizedTitle, $@"(?:^| ){Regex.Escape(Normalize(alias))}(?: |$)", RegexOptions.CultureInvariant));
    }

    private static bool ContainsPhrase(string normalizedTitle, string normalizedPhrase) =>
        Regex.IsMatch(normalizedTitle, $@"(?:^| ){Regex.Escape(normalizedPhrase)}(?: |$)", RegexOptions.CultureInvariant);

    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{M}\p{N}]+", " ").Trim();
}
