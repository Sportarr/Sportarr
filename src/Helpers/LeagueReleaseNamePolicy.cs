using System.Text.RegularExpressions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

public static class LeagueReleaseNamePolicy
{
    private static readonly Regex AflRoundPattern = new(@"\bRound[\s._-]*0*(?<round>[1-9][0-9]?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AflFinalPattern = new(@"\b(?:WC|QF|EF|SF|GF)[\s._-]*[1-9]?\b|\bGrand[\s._-]+Final\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AflWFinalStagePattern = new(
        @"\b(?:(?<code>QF|EF|SF|PF|GF)[\s._-]*[1-9]?|(?<name>Qualifying|Elimination|Semi|Preliminary|Grand)[\s._-]+Final)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RoundPattern = new(@"\b(?:Round|R)[\s._-]*0*(?<round>[1-9][0-9]?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RacePattern = new(@"\bRaces?[\s._-]*0*(?<race>[1-9][0-9]?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AmaSupercrossRoundPattern = new(@"\b(?:Round|Rd|R)[\s._-]*0*(?<round>[1-9][0-9]?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LibraryEpisodePattern = new(@"\bS(?<season>20[0-9]{2})E0*(?<episode>[1-9][0-9]*)(?=$|[^A-Za-z0-9]|pt[0-9]+\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DayMonthYearPattern = new(@"(?<![0-9])(?<day>0?[1-9]|[12][0-9]|3[01])[\s._/-]+(?<month>0?[1-9]|1[0-2])[\s._/-]+(?<year>20[0-9]{2})(?![0-9])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex YearMonthDayPattern = new(@"(?<![0-9])(?<year>20[0-9]{2})[\s._/-]+(?<month>0?[1-9]|1[0-2])[\s._/-]+(?<day>0?[1-9]|[12][0-9]|3[01])(?![0-9])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CompactYearMonthDayPattern = new(@"(?<![0-9])(?<year>20[0-9]{2})(?<month>0[1-9]|1[0-2])(?<day>0[1-9]|[12][0-9]|3[01])(?![0-9])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DayMonthPattern = new(
        @"(?<![0-9])(?<day>0?[1-9]|[12][0-9]|3[01])[\s._/-]+(?<month>0?[1-9]|1[0-2])(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EHFDayMonthStampPattern = new(
        @"(?<![0-9])(?<day>0?[1-9]|[12][0-9]|3[01])[\s._/-]+(?<month>0?[1-9]|1[0-2])(?=[\s._-]+(?:[0-9]{3,4}p|WEB|HDTV|SDTV|Blu|x26[45]|[Hh]\.?26[45])|[\s._-]*$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ShortDayMonthYearPattern = new(
        @"(?<![0-9])(?<day>0?[1-9]|[12][0-9]|3[01])[\s._/-]+(?<month>0?[1-9]|1[0-2])[\s._/-]+(?<year>[0-9]{2})(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CatalogShortDatePattern = new(
        @"(?<![0-9])(?<first>0?[1-9]|[12][0-9]|3[01])[\s._/-]+(?<second>0?[1-9]|[12][0-9]|3[01])[\s._/-]+(?<year>[0-9]{2})(?![\p{L}\p{M}\p{N}]|:[0-9]{2})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CatalogLongDatePattern = new(
        @"(?<![0-9])(?<first>0?[1-9]|[12][0-9]|3[01])[\s._/-]+(?<second>0?[1-9]|[12][0-9]|3[01])[\s._/-]+(?<year>(?:19|20)[0-9]{2})(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CanadianDayMonthYearPattern = new(
        @"(?<![0-9])(?<day>0?[1-9]|[12][0-9]|3[01])[\s._/-]+(?<month>0?[1-9]|1[0-2])[\s._/-]+(?<year>(?:19|20)[0-9]{2})(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CanadianYearMonthDayPattern = new(
        @"(?<![0-9])(?<year>(?:19|20)[0-9]{2})[\s._/-]+(?<month>0?[1-9]|1[0-2])[\s._/-]+(?<day>0?[1-9]|[12][0-9]|3[01])(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CanadianCompactYearMonthDayPattern = new(
        @"(?<![0-9])(?<year>(?:19|20)[0-9]{2})(?<month>0[1-9]|1[0-2])(?<day>0[1-9]|[12][0-9]|3[01])(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CanadianTechnicalTokenPattern = new(
        @"(?<![\p{L}\p{M}\p{N}])(?:(?:DDP?|EAC3|AC3|AAC|Atmos|True[\s._-]*HD)[\s._/-]*[0-9](?:[._][0-9])?|DTS(?:[\s._/-]*HD)?(?:[\s._/-]*MA)?[\s._/-]*[0-9](?:[._][0-9])?|[0-9]{2,3}fps)(?![\p{L}\p{M}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SnookerDayPattern = new(
        @"\bDay[\s._-]*0*(?<number>[1-9][0-9]?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SnookerPartPattern = new(
        @"\b(?:Part|Session|Day)[\s._-]*0*(?<number>[1-9][0-9]?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BkfcCardPattern = new(
        @"\bBKFC[\s._-]+0*(?<card>[1-9][0-9]{0,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BtccRoundPattern = new(
        @"\bRound[\s._-]*0*(?<round>[1-9][0-9]?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BtccRoundRangePattern = new(
        @"\bRound[\s._-]*0*[1-9][0-9]?[\s._-]+0*[1-9][0-9]?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BtccRacePattern = new(
        @"\bRace[\s._-]*(?<race>[1-3]|One|Two|Three)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BtccCombinedCoveragePattern = new(
        @"\b(?:Highlights?|Sunday[\s._-]+Coverage)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BsbRoundPattern = new(
        @"\bRound[\s._-]*0*(?<round>[1-9][0-9]?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BsbRaceSessionPattern = new(
        @"\bRace[\s._-]*(?<race>One|Two|Three|0?[1-3])\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PaddedDayMonthPattern = new(
        @"(?<![0-9])(?<day>0[1-9]|[12][0-9]|3[01])[\s._/-]+(?<month>0[1-9]|1[0-2])(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BalticCupSiblingPattern = new(
        @"\b(?:FIBA|Basketball|Beach[\s._-]+Soccer|Women(?:'s|s)?|Ladies|U[\s._-]?(?:17|19|20|21|23)|Under[\s._-]?(?:17|19|20|21|23))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BellatorChampionsSeriesPattern = new(
        @"\bBellator[\s._-]+Champions[\s._-]+Series[\s._-]+0*(?<number>[1-9][0-9]?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CffcCardPattern = new(
        @"\bCFFC[\s._-]+0*(?<card>[1-9][0-9]{0,2})(?![\p{L}\p{M}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? BuildQuery(Event evt)
    {
        var league = LeagueKey(evt.League?.Name);
        var date = evt.BroadcastDate ?? evt.EventDate.Date;
        var (home, away) = EventQueryService.ResolveTeamNames(evt);

        if (league == "EuropeanAthleticsChampionships")
            return $"European Athletics Championships {date.Year}";
        if (league == "DutchEredivisie") return $"Eredivisie {date.Year} {date.Month:D2}";
        if (string.Equals(evt.League?.Name, "English Rugby League Super League", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
            return $"Super League Rugby {date.Year} {date.Month:D2}";
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
        if (league == "GAAFootball" && !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return $"GAA Football All Ireland {date.Year} {CatalogTeamName(home, league)} {CatalogTeamName(away, league)}";
        }
        if (league == "EHFChampionsLeague" && !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return $"EHF Champions League {date.Year} {CatalogTeamName(home, league)} {CatalogTeamName(away, league)}";
        }
        if (league == "ChampionsHockeyLeague")
        {
            var participants = StructuredEventTeamPair(evt) ??
                (!string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away)
                    ? (home, away)
                    : ((string, string)?)null);
            if (participants is { } pair)
            {
                return "CHL " + string.Join(" ", new[]
                {
                    ChampionsHockeyLeagueTeamCore(pair.Item1),
                    ChampionsHockeyLeagueTeamCore(pair.Item2)
                }.Order(StringComparer.OrdinalIgnoreCase));
            }
        }
        if (league == "ChinaTour")
        {
            var eventTitle = Regex.Replace(evt.Title ?? string.Empty, @"[\s._-]+", " ").Trim();
            return string.IsNullOrWhiteSpace(eventTitle) ? null : eventTitle;
        }
        if (league == "CombatZoneWrestling")
        {
            return CzwQuery(evt, date);
        }
        if (league == "DREAM" && Regex.IsMatch(evt.Title ?? string.Empty,
                @"\bGenki[\s._-]+Desu[\s._-]+Ka\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return $"DREAM Genki Desu Ka {date.Year}";
        }
        if (league == "CommonwealthGamesAthletics")
        {
            return $"Commonwealth Games {date.Year} {date:dd MM}";
        }
        if (league is "ChineseWCBA" or "ChristyRingCup" or "ColombiaPrimeraA" or "ColombiaPrimeraB" or
            "ChilePrimeraDivision" or "ChileSegundaDivision" or "ChileanCopaDeLaLiga" or
            "ChinaFACup" or "ChinaLeagueOne" or "ChinaLeagueTwo" or "ChineseCBA" or
            "ChineseProfessionalBaseballLeague" or "ChineseSuperLeague")
        {
            var participants = StructuredEventTeamPair(evt) ??
                (!string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away)
                    ? (home, away)
                    : ((string, string)?)null);
            if (participants is { } pair) return $"{pair.Item1} vs {pair.Item2}";
        }
        if (league == "ClubFriendlies")
        {
            var participants = StructuredEventTeamPair(evt) ??
                (!string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away)
                    ? (home, away)
                    : ((string, string)?)null);
            if (participants is { } pair)
            {
                return $"{ClubFriendlyQueryTeamName(pair.Item1)} vs {ClubFriendlyQueryTeamName(pair.Item2)}";
            }
        }
        if (league == "WorldMensCurling" && !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            var curlingHome = CatalogTeamName(home, league);
            var curlingAway = CatalogTeamName(away, league);
            return int.TryParse(evt.Round, out var round) && round == 200
                ? $"Curling World Championship {date.Year} {curlingAway} {curlingHome}"
                : $"Curling World Championship {date.Year} {curlingHome} {curlingAway}";
        }
        if (league == "AMASupercross")
        {
            var location = Regex.Replace(evt.Title ?? string.Empty, @"[\s._-]+", " ").Trim();
            return string.IsNullOrWhiteSpace(location) ? null : $"AMA Supercross {date.Year} {location}";
        }
        if (league == "AAF" && !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return $"AAF {date.Year} {home.Trim()} {away.Trim()}";
        }
        if (league == "ACA")
        {
            var card = AcaCardNumber(evt.Title);
            return card == null ? null : $"ACA {card}";
        }
        if (league is ("AFCChampionsElite" or "AFCChampionsTwo" or "AFCWomensChampions") &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            var prefix = league switch
            {
                "AFCChampionsTwo" => "AFC Cup",
                "AFCWomensChampions" => "AFC Womens Champions League",
                _ => "AFC Champions League"
            };
            return $"{prefix} {CatalogQueryTeamName(home)} {CatalogQueryTeamName(away)}";
        }
        if (league == "AFLW" && int.TryParse(evt.Round, out var aflWRound) &&
            aflWRound is >= 1 and < 100)
        {
            return $"AFLW {date.Year} Round {aflWRound}";
        }
        if (league == "AFLW" && !string.IsNullOrWhiteSpace(home) &&
            !string.IsNullOrWhiteSpace(away))
        {
            return $"AFLW {date.Year} {AflwQueryTeamName(home)} {AflwQueryTeamName(away)}";
        }
        if (league == "AIW")
        {
            var eventName = Regex.Replace(
                SearchNormalizationService.RemoveDiacritics(evt.Title ?? string.Empty),
                @"[^\p{L}\p{N}]+",
                " ").Trim();
            eventName = Regex.Replace(eventName, @"\s+20[0-9]{2}$", string.Empty, RegexOptions.CultureInvariant);
            return string.IsNullOrWhiteSpace(eventName) ? null : $"{eventName} {date.Year}";
        }
        if (league == "AJKF")
        {
            var eventName = CatalogEventTitle(evt.Title);
            eventName = Regex.Replace(eventName, @"\bSuperfights\b", "Superfights",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return string.IsNullOrWhiteSpace(eventName) ? null : eventName;
        }
        if (league == "AJPW")
        {
            var eventName = CatalogEventTitle(evt.Title);
            eventName = Regex.Replace(eventName, @"\s+Day\s+(?<day>[0-9]+)$", $" {date.Year} Day ${{day}}",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return string.IsNullOrWhiteSpace(eventName) ? null : $"AJPW {eventName}";
        }
        if (league == "AJW")
        {
            var eventName = CatalogEventTitle(evt.Title);
            return string.IsNullOrWhiteSpace(eventName) ? null : $"AJW {eventName}";
        }
        if (league == "ABALeague2" && !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return $"ABA League 2 {AbaTeamName(home)} {AbaTeamName(away)}";
        }
        if (league == "ABALeague" && !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return $"ABA League {date.Year} {AbaTeamName(home)} {AbaTeamName(away)}";
        }
        if (league is "WAFCON" or "AFCON" or "AFCONQualifying" &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            var competition = league switch
            {
                "WAFCON" => "Africa Cup of Nations Women",
                "AFCONQualifying" => "Africa Cup of Nations Qualifying",
                _ => "Africa Cup of Nations"
            };
            return $"{competition} {AfconTeamName(home)} {AfconTeamName(away)}";
        }
        if (league == "AFCWomensAsianCup" &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return string.Join(" ", new[] { CatalogQueryTeamName(home), CatalogQueryTeamName(away) }
                .Order(StringComparer.OrdinalIgnoreCase));
        }
        if (league == "AustralianALeague" &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return string.Join(" ", new[] { CatalogQueryTeamName(home), CatalogQueryTeamName(away) }
                .Order(StringComparer.OrdinalIgnoreCase));
        }
        if (league == "BalticCup" &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            var teams = string.Join(" ", new[] { CatalogQueryTeamName(home), CatalogQueryTeamName(away) }
                .Order(StringComparer.OrdinalIgnoreCase));
            return $"Friendly {date.Year} {teams}";
        }
        if (league == "BasketballAfricaLeague" &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return string.Join(" ", new[] { CatalogQueryTeamName(home), CatalogQueryTeamName(away) }
                .Order(StringComparer.OrdinalIgnoreCase));
        }
        if (league == "BelgianProLeague" &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return string.Join(" ", new[] { CatalogQueryTeamName(home), CatalogQueryTeamName(away) }
                .Order(StringComparer.OrdinalIgnoreCase));
        }
        if ((IsConcacafLeague(league) || league == "ConmebolWomensNationsLeague") &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return string.Join(" ", new[] { CatalogQueryTeamName(home), CatalogQueryTeamName(away) }
                .Order(StringComparer.OrdinalIgnoreCase));
        }
        if (league is "ConmebolPreOlympic" or "CosafaCup" &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return string.Join(" ", new[] { CatalogQueryTeamName(home), CatalogQueryTeamName(away) }
                .Order(StringComparer.OrdinalIgnoreCase));
        }
        if (league == "CFFC")
        {
            var card = CffcCardNumber(evt.Title);
            return card == null ? null : $"CFFC {card}";
        }
        if (league == "CallOfDutyLeague")
        {
            var major = Regex.Match(
                evt.Title ?? string.Empty,
                @"\bMajor\s+(?<number>[0-9]+)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return major.Success ? $"Call of Duty Major {major.Groups["number"].Value}" : null;
        }
        if (league == "CanadianTeamCompetition")
        {
            var participants = StructuredEventTeamPair(evt) ??
                (!string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away)
                    ? (home, away)
                    : ((string, string)?)null);
            if (participants is { } pair)
            {
                return string.Join(" ", new[] { CatalogQueryTeamName(pair.Item1), CatalogQueryTeamName(pair.Item2) }
                    .Order(StringComparer.OrdinalIgnoreCase));
            }
        }
        if (league is "CambodiaCLeague" or "CambodianHunSenCup" or "PortugueseCampeonato" &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return string.Join(" ", new[] { CatalogQueryTeamName(home), CatalogQueryTeamName(away) }
                .Order(StringComparer.OrdinalIgnoreCase));
        }
        if (league == "Bellator" && IsBellatorSeriesFiveEvent(evt)) return "Bellator Champions";
        if (league == "BritishIrishLions" &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return string.Join(" ", new[] { home.Trim(), away.Trim() }
                .Order(StringComparer.OrdinalIgnoreCase));
        }
        if (league == "BKFC")
        {
            var card = BkfcCardNumber(evt.Title);
            return card == null ? null : $"BKFC {card}";
        }
        if (league == "BTCC") return $"BTCC {date.Year}";

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

        if (league == "ConfederationsCup" &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return evt.Title.Trim();
        }
        if (league is "CopaAmerica" or "CopaAmericaFemenina" &&
            !string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            return evt.Title.Trim();
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
        if (HasAsianGamesCompetitionConflict(releaseTitle, evt.League?.Name)) return true;
        if (string.Equals(evt.League?.Name, "NBA Summer League", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(releaseTitle, @"\bNBA[\s._-]+(?:RS|Regular[\s._-]+Season)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            !Regex.IsMatch(releaseTitle, @"\bSummer[\s._-]+League\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return true;
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
        if (league is "GAAFootball" or "EHFChampionsLeague" or "WorldMensCurling")
            return HasCatalogTeamConflict(releaseTitle, evt, league);
        if (league == "AMASupercross") return HasAmaSupercrossConflict(releaseTitle, evt);
        if (league == "AAF") return HasAafConflict(releaseTitle, evt);
        if (league == "ACA") return HasAcaConflict(releaseTitle, evt);
        if (league == "AFLW") return HasAflWConflict(releaseTitle, evt);
        if (league == "ABALeague") return HasAbaConflict(releaseTitle, evt);
        if (league is "WAFCON" or "AFCON" or "AFCONQualifying")
            return HasAfconConflict(releaseTitle, evt, league);
        if (league == "AFCWomensAsianCup")
            return HasAfcWomensAsianCupConflict(releaseTitle, evt);
        if (league == "AustralianALeague")
            return HasAustralianALeagueConflict(releaseTitle, evt);
        if (league == "AustralianNBL")
            return HasAustralianNblConflict(releaseTitle, evt);
        if (league == "BalticCup") return HasBalticCupConflict(releaseTitle, evt);
        if (league == "BasketballAfricaLeague") return !HasBasketballAfricaLeagueIdentity(releaseTitle, evt);
        if (league == "BaseballAllStar") return HasBaseballAllStarConflict(releaseTitle, evt);
        if (league == "BasketballAllStar") return IsSupportedBasketballAllStarEvent(evt) &&
            !HasBasketballAllStarIdentity(releaseTitle, evt);
        if (league == "BelgianProLeague") return !HasBelgianProLeagueIdentity(releaseTitle, evt);
        if (league == "ChampionsHockeyLeague") return !HasChampionsHockeyLeagueIdentity(releaseTitle, evt);
        if (league == "ChinaFACup") return !HasChinaFaCupIdentity(releaseTitle, evt);
        if (league == "ChineseSuperLeague") return !HasChineseSuperLeagueIdentity(releaseTitle, evt);
        if (league == "CombatZoneWrestling") return !HasCzwIdentity(releaseTitle, evt);
        if (league == "CommonwealthGamesAthletics") return !HasCommonwealthAthleticsIdentity(releaseTitle, evt);
        if (league == "EuropeanAthleticsChampionships" &&
            AthleticsStage(Normalize(evt.Title ?? string.Empty)) == "final" &&
            AthleticsGender(Normalize(evt.Title ?? string.Empty)) != null &&
            AthleticsEventDiscipline(Normalize(evt.Title ?? string.Empty)) != null)
            return !HasEuropeanAthleticsFinalIdentity(releaseTitle, evt);
        if (league != null && league.StartsWith("CommonwealthGames", StringComparison.Ordinal))
            return HasCommonwealthDisciplineConflict(releaseTitle, evt, league);
        if (league == "ClubFriendlies") return !HasClubFriendlyIdentity(releaseTitle, evt);
        if (league == "PremierLeagueSummerSeries")
            return !HasPremierLeagueSummerSeriesIdentity(releaseTitle, evt);
        if (league == "ConfederationsCup") return HasConfederationsCupConflict(releaseTitle, evt);
        if (league is "ColombiaPrimeraA" or "ColombiaPrimeraB")
            return !HasColombiaPrimeraIdentity(releaseTitle, evt, league);
        if (league == "CanadianTeamCompetition") return HasCanadianTeamCompetitionConflict(releaseTitle, evt);
        if (IsConcacafLeague(league)) return HasConcacafConflict(releaseTitle, evt, league!);
        if (league == "ConmebolWomensNationsLeague")
            return !HasConmebolWomensNationsLeagueIdentity(releaseTitle, evt);
        if (league == "CFFC") return !HasCffcIdentity(releaseTitle, evt);
        if (league == "Bellator" && IsSupportedBellatorEvent(evt))
            return !HasBellatorIdentity(releaseTitle, evt);
        if (league == "BritishIrishLions") return !HasBritishIrishLionsIdentity(releaseTitle, evt);
        if (league == "BSB") return !HasBsbIdentity(releaseTitle, evt);
        if (league == "BKFC") return HasBkfcConflict(releaseTitle, evt);
        if (league == "BTCC") return HasBtccConflict(releaseTitle, evt);
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
        if (league is "GAAFootball" or "EHFChampionsLeague" or "WorldMensCurling")
            return HasCatalogTeamIdentity(releaseTitle, evt, league);
        if (league == "AMASupercross") return HasAmaSupercrossIdentity(releaseTitle, evt);
        if (league == "AAF") return IsAafRelease(releaseTitle) && !HasAafConflict(releaseTitle, evt);
        if (league == "ACA") return IsAcaRelease(releaseTitle) && !HasAcaConflict(releaseTitle, evt);
        if (league == "AFLW")
        {
            var (aflHome, aflAway) = EventQueryService.ResolveTeamNames(evt);
            return string.Equals(ReleaseLeagueKey(releaseTitle), "AFLW", StringComparison.Ordinal) &&
                !HasAflWConflict(releaseTitle, evt) &&
                HasTeam(releaseTitle, aflHome, evt.HomeTeam) &&
                HasTeam(releaseTitle, aflAway, evt.AwayTeam);
        }
        if (league == "ABALeague") return !HasAbaConflict(releaseTitle, evt);
        if (league is "WAFCON" or "AFCON" or "AFCONQualifying")
            return HasAfconIdentity(releaseTitle, evt, league);
        if (league == "AFCWomensAsianCup")
            return !HasAfcWomensAsianCupConflict(releaseTitle, evt);
        if (league == "AustralianALeague")
            return string.Equals(ReleaseLeagueKey(releaseTitle), league, StringComparison.Ordinal) &&
                !HasAustralianALeagueConflict(releaseTitle, evt);
        if (league == "AustralianNBL")
            return HasAustralianNblIdentity(releaseTitle, evt);
        if (league == "BalticCup") return !HasBalticCupConflict(releaseTitle, evt);
        if (league == "BasketballAfricaLeague") return HasBasketballAfricaLeagueIdentity(releaseTitle, evt);
        if (league == "BaseballAllStar") return HasBaseballHomeRunDerbyIdentity(releaseTitle, evt);
        if (league == "BasketballAllStar") return HasBasketballAllStarIdentity(releaseTitle, evt);
        if (league == "BelgianProLeague") return HasBelgianProLeagueIdentity(releaseTitle, evt);
        if (league == "ChampionsHockeyLeague") return HasChampionsHockeyLeagueIdentity(releaseTitle, evt);
        if (league == "ChinaFACup") return HasChinaFaCupIdentity(releaseTitle, evt);
        if (league == "ChineseSuperLeague") return HasChineseSuperLeagueIdentity(releaseTitle, evt);
        if (league == "CombatZoneWrestling") return HasCzwIdentity(releaseTitle, evt);
        if (league == "CommonwealthGamesAthletics") return HasCommonwealthAthleticsIdentity(releaseTitle, evt);
        if (league == "EuropeanAthleticsChampionships") return HasEuropeanAthleticsFinalIdentity(releaseTitle, evt);
        if (league == "ClubFriendlies") return HasClubFriendlyIdentity(releaseTitle, evt);
        if (league == "PremierLeagueSummerSeries")
            return HasPremierLeagueSummerSeriesIdentity(releaseTitle, evt);
        if (league == "ConfederationsCup") return HasConfederationsCupIdentity(releaseTitle, evt);
        if (league is "ColombiaPrimeraA" or "ColombiaPrimeraB")
            return HasColombiaPrimeraIdentity(releaseTitle, evt, league);
        if (league == "CanadianTeamCompetition") return HasCanadianTeamCompetitionIdentity(releaseTitle, evt);
        if (IsConcacafLeague(league)) return HasConcacafIdentity(releaseTitle, evt, league);
        if (league == "ConmebolWomensNationsLeague")
            return HasConmebolWomensNationsLeagueIdentity(releaseTitle, evt);
        if (league == "CFFC") return HasCffcIdentity(releaseTitle, evt);
        if (league == "Bellator") return HasBellatorIdentity(releaseTitle, evt);
        if (league == "BritishIrishLions") return HasBritishIrishLionsIdentity(releaseTitle, evt);
        if (league == "BSB") return HasBsbIdentity(releaseTitle, evt);
        if (league == "BKFC") return HasBkfcIdentity(releaseTitle, evt);
        if (league == "BTCC") return HasBtccIdentity(releaseTitle, evt);
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
        if (leagueName.Equals("AFL Womens", StringComparison.OrdinalIgnoreCase)) return "AFLW";
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
        if (leagueName.Contains("All-Ireland Senior Football", StringComparison.OrdinalIgnoreCase)) return "GAAFootball";
        if (leagueName.Contains("EHF Champions League", StringComparison.OrdinalIgnoreCase)) return "EHFChampionsLeague";
        if (leagueName.Equals("Champions Hockey League", StringComparison.OrdinalIgnoreCase)) return "ChampionsHockeyLeague";
        if (leagueName.Equals("Dutch Eredivisie", StringComparison.OrdinalIgnoreCase)) return "DutchEredivisie";
        if (leagueName.Equals("Chile Primera Division", StringComparison.OrdinalIgnoreCase)) return "ChilePrimeraDivision";
        if (leagueName.Equals("Chile Segunda División", StringComparison.OrdinalIgnoreCase)) return "ChileSegundaDivision";
        if (leagueName.Equals("Chilean Copa de la Liga", StringComparison.OrdinalIgnoreCase)) return "ChileanCopaDeLaLiga";
        if (leagueName.Equals("China FA Cup", StringComparison.OrdinalIgnoreCase)) return "ChinaFACup";
        if (leagueName.Equals("China League One", StringComparison.OrdinalIgnoreCase)) return "ChinaLeagueOne";
        if (leagueName.Equals("China league Two", StringComparison.OrdinalIgnoreCase)) return "ChinaLeagueTwo";
        if (leagueName.Equals("China Tour", StringComparison.OrdinalIgnoreCase)) return "ChinaTour";
        if (leagueName.Equals("Chinese CBA", StringComparison.OrdinalIgnoreCase)) return "ChineseCBA";
        if (leagueName.Equals("Chinese Professional Baseball League", StringComparison.OrdinalIgnoreCase)) return "ChineseProfessionalBaseballLeague";
        if (leagueName.Equals("Chinese Super League", StringComparison.OrdinalIgnoreCase)) return "ChineseSuperLeague";
        if (leagueName.Equals("Chinese WCBA", StringComparison.OrdinalIgnoreCase)) return "ChineseWCBA";
        if (leagueName.Equals("Christy Ring Cup", StringComparison.OrdinalIgnoreCase)) return "ChristyRingCup";
        if (leagueName.Equals("Combat Zone Wrestling", StringComparison.OrdinalIgnoreCase)) return "CombatZoneWrestling";
        if (leagueName.Equals("DREAM", StringComparison.OrdinalIgnoreCase)) return "DREAM";
        if (leagueName.Equals("Commonwealth Games 3x3 Basketball Women", StringComparison.OrdinalIgnoreCase)) return "CommonwealthGames3x3BasketballWomen";
        if (leagueName.Equals("Commonwealth Games 7s Rugby", StringComparison.OrdinalIgnoreCase)) return "CommonwealthGames7sRugby";
        if (leagueName.Equals("Commonwealth Games Artistic Gymnastics", StringComparison.OrdinalIgnoreCase)) return "CommonwealthGamesGymnastics";
        if (leagueName.Equals("Commonwealth Games Athletics", StringComparison.OrdinalIgnoreCase)) return "CommonwealthGamesAthletics";
        if (leagueName.Equals("European Athletics Championships", StringComparison.OrdinalIgnoreCase)) return "EuropeanAthleticsChampionships";
        if (leagueName.Equals("Club Friendlies", StringComparison.OrdinalIgnoreCase)) return "ClubFriendlies";
        if (leagueName.Equals("English Premier League Summer Series", StringComparison.OrdinalIgnoreCase)) return "PremierLeagueSummerSeries";
        if (leagueName.Equals("Confederations Cup", StringComparison.OrdinalIgnoreCase)) return "ConfederationsCup";
        if (leagueName.Equals("Copa America", StringComparison.OrdinalIgnoreCase)) return "CopaAmerica";
        if (leagueName.Equals("Copa America Femenina", StringComparison.OrdinalIgnoreCase)) return "CopaAmericaFemenina";
        if (leagueName.Equals("Colombia Categoría Primera A", StringComparison.OrdinalIgnoreCase)) return "ColombiaPrimeraA";
        if (leagueName.Equals("Colombian Categoría Primera B", StringComparison.OrdinalIgnoreCase)) return "ColombiaPrimeraB";
        if (leagueName.Contains("World Mens Curling Championship", StringComparison.OrdinalIgnoreCase)) return "WorldMensCurling";
        if (leagueName.Contains("AMA Supercross Championship", StringComparison.OrdinalIgnoreCase)) return "AMASupercross";
        if (leagueName.Equals("AAF", StringComparison.OrdinalIgnoreCase)) return "AAF";
        if (leagueName.Equals("ACA", StringComparison.OrdinalIgnoreCase)) return "ACA";
        if (leagueName.Equals("AFC Champions League Elite", StringComparison.OrdinalIgnoreCase)) return "AFCChampionsElite";
        if (leagueName.Equals("AFC Champions League Two", StringComparison.OrdinalIgnoreCase)) return "AFCChampionsTwo";
        if (leagueName.Equals("AFC Womens Champions League", StringComparison.OrdinalIgnoreCase)) return "AFCWomensChampions";
        if (leagueName.Equals("AIW Wrestling", StringComparison.OrdinalIgnoreCase)) return "AIW";
        if (leagueName.Equals("AJKF", StringComparison.OrdinalIgnoreCase)) return "AJKF";
        if (leagueName.Equals("AJPW", StringComparison.OrdinalIgnoreCase)) return "AJPW";
        if (leagueName.Equals("AJW", StringComparison.OrdinalIgnoreCase)) return "AJW";
        if (leagueName.Equals("Adriatic ABA League 2", StringComparison.OrdinalIgnoreCase)) return "ABALeague2";
        if (leagueName.Equals("Adriatic ABA League", StringComparison.OrdinalIgnoreCase)) return "ABALeague";
        if (leagueName.Equals("Africa Cup of Nations Women", StringComparison.OrdinalIgnoreCase)) return "WAFCON";
        if (leagueName.Equals("African Cup of Nations", StringComparison.OrdinalIgnoreCase)) return "AFCON";
        if (leagueName.Equals("African Cup of Nations Qualifying", StringComparison.OrdinalIgnoreCase)) return "AFCONQualifying";
        if (leagueName.Equals("Asian Cup Women", StringComparison.OrdinalIgnoreCase)) return "AFCWomensAsianCup";
        if (leagueName.Equals("Australian NBL", StringComparison.OrdinalIgnoreCase)) return "AustralianNBL";
        if (leagueName.Equals("Australian A-League", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("A-League", StringComparison.OrdinalIgnoreCase)) return "AustralianALeague";
        if (leagueName.Equals("Baltic Cup", StringComparison.OrdinalIgnoreCase)) return "BalticCup";
        if (leagueName.Equals("Basketball Africa League", StringComparison.OrdinalIgnoreCase)) return "BasketballAfricaLeague";
        if (leagueName.Equals("Baseball All-Star Games", StringComparison.OrdinalIgnoreCase)) return "BaseballAllStar";
        if (leagueName.Equals("Basketball All-Star Games", StringComparison.OrdinalIgnoreCase)) return "BasketballAllStar";
        if (leagueName.Equals("Belgian Pro League", StringComparison.OrdinalIgnoreCase)) return "BelgianProLeague";
        if (leagueName.Equals("CONCACAF Caribbean Cup", StringComparison.OrdinalIgnoreCase)) return "ConcacafCaribbeanCup";
        if (leagueName.Equals("CONCACAF Central American Cup", StringComparison.OrdinalIgnoreCase)) return "ConcacafCentralAmericanCup";
        if (leagueName.Equals("CONCACAF Champions Cup", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("CONCACAF Champions League", StringComparison.OrdinalIgnoreCase)) return "ConcacafChampionsCup";
        if (leagueName.Equals("CONCACAF Gold Cup", StringComparison.OrdinalIgnoreCase)) return "ConcacafGoldCup";
        if (leagueName.Equals("CONCACAF Gold Cup Qualifying", StringComparison.OrdinalIgnoreCase)) return "ConcacafGoldCupQualifying";
        if (leagueName.Equals("CONCACAF Nations League", StringComparison.OrdinalIgnoreCase)) return "ConcacafNationsLeague";
        if (leagueName.Equals("CONCACAF Series", StringComparison.OrdinalIgnoreCase)) return "ConcacafSeries";
        if (leagueName.Equals("CONCACAF W Champions Cup", StringComparison.OrdinalIgnoreCase)) return "ConcacafWChampionsCup";
        if (leagueName.Equals("CONCACAF W Gold Cup", StringComparison.OrdinalIgnoreCase)) return "ConcacafWGoldCup";
        if (leagueName.Equals("CONMEBOL Liga de Naciones Femenina", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("CONMEBOL Women's Nations League", StringComparison.OrdinalIgnoreCase)) return "ConmebolWomensNationsLeague";
        if (leagueName.Equals("CONMEBOL Pre-Olympic Tournament", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("CONMEBOL Preolímpico", StringComparison.OrdinalIgnoreCase)) return "ConmebolPreOlympic";
        if (leagueName.Equals("COSAFA Cup", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("COSAFA Senior Challenge Cup", StringComparison.OrdinalIgnoreCase)) return "CosafaCup";
        if (leagueName.Equals("Cage Fury Fighting Championships", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Cage Fury FC", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("CFFC", StringComparison.OrdinalIgnoreCase)) return "CFFC";
        if (leagueName.Equals("Call of Duty League", StringComparison.OrdinalIgnoreCase)) return "CallOfDutyLeague";
        if (leagueName.Equals("Cambodia C-League", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Metfone Cambodian League", StringComparison.OrdinalIgnoreCase)) return "CambodiaCLeague";
        if (leagueName.Equals("Cambodian Hun Sen Cup", StringComparison.OrdinalIgnoreCase)) return "CambodianHunSenCup";
        if (leagueName.Equals("Campeonato Nacional Feminino", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Campeonato de Portugal Serie A", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Campeonato de Portugal Serie B", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Campeonato de Portugal Serie C", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Campeonato de Portugal Serie D", StringComparison.OrdinalIgnoreCase)) return "PortugueseCampeonato";
        if (leagueName.Equals("Canada Cup", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Canadian Championship", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Canadian Elite Basketball League", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Canadian Memorial Cup", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Canadian Northern Super League", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Canadian OHL", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Canadian Premier League", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Canadian QMJHL", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Canadian WHL", StringComparison.OrdinalIgnoreCase)) return "CanadianTeamCompetition";
        if (leagueName.Equals("Bellator", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Equals("Bellator Fighting Championships", StringComparison.OrdinalIgnoreCase)) return "Bellator";
        if (leagueName.Equals("British and Irish Lions Tours", StringComparison.OrdinalIgnoreCase)) return "BritishIrishLions";
        if (leagueName.Equals("BSB", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Contains("British Superbike", StringComparison.OrdinalIgnoreCase)) return "BSB";
        if (leagueName.Equals("BKFC", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Contains("Bare Knuckle", StringComparison.OrdinalIgnoreCase)) return "BKFC";
        if (leagueName.Equals("BTCC", StringComparison.OrdinalIgnoreCase) ||
            leagueName.Contains("British Touring Car", StringComparison.OrdinalIgnoreCase)) return "BTCC";
        return null;
    }

    public static string? ReleaseLeagueKey(string title)
    {
        if (AfconReleaseLeague(title) is { } afcon) return afcon;
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
        if (IsGaaFootballRelease(title)) return "GAAFootball";
        if (IsEhfChampionsLeagueRelease(title)) return "EHFChampionsLeague";
        if (IsWorldCurlingRelease(title)) return "WorldMensCurling";
        if (IsOlympicCurlingRelease(title)) return "OlympicCurling";
        if (Regex.IsMatch(title, @"\bAMA[\s._-]+Supercross\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "AMASupercross";
        if (IsAafRelease(title)) return "AAF";
        if (IsAcaRelease(title)) return "ACA";
        if (IsAfcWomensAsianCupRelease(title)) return "AFCWomensAsianCup";
        if (Regex.IsMatch(title, @"\bBritish[\s._-]+(?:and|&)[\s._-]+Irish[\s._-]+Lions\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "BritishIrishLions";
        if (Regex.IsMatch(title, @"\bBSB\b|\bBritish[\s._-]+Superbike\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "BSB";
        if (Regex.IsMatch(
                title,
                @"\b(?:Ninja[\s._-]+A[\s._-]*League|A[\s._-]*League[\s._-]+Women(?:s)?|W[\s._-]*League)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "AustralianALeagueWomen";
        if (Regex.IsMatch(
                title,
                @"\bA[\s._-]*League\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "AustralianALeague";
        if (BkfcCardPattern.IsMatch(title)) return "BKFC";
        if (Regex.IsMatch(title, @"\bBTCC\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "BTCC";
        var normalizedCatalogTitle = Normalize(title);
        if (ContainsPhrase(normalizedCatalogTitle, "czw") || ContainsPhrase(normalizedCatalogTitle, "combat zone wrestling"))
            return "CombatZoneWrestling";
        if (ContainsPhrase(normalizedCatalogTitle, "commonwealth games"))
        {
            if (Regex.IsMatch(normalizedCatalogTitle, @"\bathletics?\b", RegexOptions.CultureInvariant))
                return "CommonwealthGamesAthletics";
            if (Regex.IsMatch(normalizedCatalogTitle, @"\bgymnastics?\b", RegexOptions.CultureInvariant))
                return "CommonwealthGamesGymnastics";
        }
        return null;
    }

    public static bool UsesCompleteCatalogIdentity(Event evt) => LeagueKey(evt.League?.Name) is
        "FISAlpine" or "OlympicsSwimming" or "DiamondLeague" or "OlympicsSkateboarding" or
        "GAAFootball" or "EHFChampionsLeague" or "WorldMensCurling" or "AMASupercross" or
        "AAF" or "ACA" or "ABALeague" or "WAFCON" or "AFCON" or "AFCONQualifying" or
        "AFCWomensAsianCup" or "BalticCup" or "BasketballAfricaLeague" or
        "BaseballAllStar" or "BasketballAllStar" or "BelgianProLeague" or "ChampionsHockeyLeague" or
        "ChinaFACup" or "ChineseSuperLeague" or "CombatZoneWrestling" or
        "CommonwealthGamesAthletics" or "ClubFriendlies" or "PremierLeagueSummerSeries" or
        "ColombiaPrimeraA" or "ColombiaPrimeraB" or "Bellator" or
        "BritishIrishLions" or "BSB" or "BKFC" or "BTCC" or
        "ConcacafCaribbeanCup" or "ConcacafCentralAmericanCup" or
        "ConcacafChampionsCup" or "ConcacafGoldCup" or "ConcacafGoldCupQualifying" or
        "ConcacafNationsLeague" or "ConcacafSeries" or "ConcacafWChampionsCup" or
        "ConcacafWGoldCup" or "ConmebolWomensNationsLeague" or "CFFC";

    public static bool AllowsCrossSportLabel(string releaseTitle, Event evt, string sport) =>
        sport.Equals("Olympics", StringComparison.OrdinalIgnoreCase) &&
        LeagueKey(evt.League?.Name)?.StartsWith("CommonwealthGames", StringComparison.Ordinal) == true &&
        ContainsPhrase(Normalize(releaseTitle), "commonwealth games");

    public static bool AllowsCombinedParticipantCategory(string releaseTitle, Event evt)
    {
        if (LeagueKey(evt.League?.Name) != "CommonwealthGamesAthletics") return false;

        var normalizedTitle = Normalize(releaseTitle);
        return ContainsPhrase(normalizedTitle, "commonwealth games") &&
            Regex.IsMatch(normalizedTitle, @"\bathletics?\b", RegexOptions.CultureInvariant) &&
            AthleticsGender(normalizedTitle) == "both";
    }

    public static bool HasExactTeamGameIdentity(string releaseTitle, Event evt)
    {
        var league = LeagueKey(evt.League?.Name);
        if (league == "ABALeague") return HasStrongEventIdentity(releaseTitle, evt);
        if (league != "AFL" || !HasStrongEventIdentity(releaseTitle, evt) ||
            !int.TryParse(evt.Round, out var eventRound) || eventRound >= 100) return false;

        var releaseRound = AflRoundPattern.Match(releaseTitle);
        var eventYear = (evt.BroadcastDate ?? evt.EventDate).Year;
        return releaseRound.Success && int.Parse(releaseRound.Groups["round"].Value) == eventRound &&
            Regex.IsMatch(releaseTitle, $@"(?<![0-9]){eventYear}(?![0-9])", RegexOptions.CultureInvariant);
    }

    public static bool HasExactCanadianTeamPair(string releaseTitle, Event evt) =>
        LeagueKey(evt.League?.Name) == "CanadianTeamCompetition" &&
        HasCanadianTeamPair(releaseTitle, evt);

    public static bool HasExactChampionsHockeyLeagueTeamPair(string releaseTitle, Event evt) =>
        LeagueKey(evt.League?.Name) == "ChampionsHockeyLeague" &&
        HasChampionsHockeyLeagueTeamPair(releaseTitle, evt);

    private static bool HasAbaConflict(string title, Event evt)
    {
        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away)) return true;
        var normalizedTitle = Normalize(title);
        var hasLeague = Regex.IsMatch(title, @"\bABA[\s._-]+League\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var hasTeams = ContainsPhrase(normalizedTitle, Normalize(AbaTeamName(home))) &&
            ContainsPhrase(normalizedTitle, Normalize(AbaTeamName(away)));

        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
            return !libraryEpisodeMatches || !hasLeague || !hasTeams;

        var hasEventYear = Regex.IsMatch(
                title,
                $@"(?<![0-9]){eventDate.Year}(?![0-9])",
                RegexOptions.CultureInvariant) ||
            Regex.IsMatch(
                title,
                $@"(?<![0-9]){eventDate:yyyyMMdd}(?![0-9])",
                RegexOptions.CultureInvariant);
        return !hasLeague ||
            HasYearConflict(title, evt) ||
            !hasEventYear ||
            !SearchNormalizationService.HasDayMonthDateToken(title, eventDate) ||
            !hasTeams;
    }

    private static bool HasAfconConflict(string title, Event evt, string league)
    {
        var releaseLeague = AfconReleaseLeague(title);
        if (releaseLeague == null) return HasYearConflict(title, evt);
        if (!string.Equals(releaseLeague, league, StringComparison.Ordinal)) return true;

        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (!HasAfconParticipants(title, home, away, evt.HomeTeam, evt.AwayTeam)) return true;

        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
            return !libraryEpisodeMatches;

        var eventYear = eventDate.Year;
        var releaseYears = Regex.Matches(title, @"(?<![0-9])20[0-9]{2}(?![0-9])")
            .Select(match => int.Parse(match.Value));
        var hasExpectedYear = releaseYears.Contains(eventYear) ||
            Regex.IsMatch(title, $@"(?<![0-9]){eventDate:yyyyMMdd}(?![0-9])", RegexOptions.CultureInvariant);
        if (!hasExpectedYear) return true;

        return (!SearchNormalizationService.HasDayMonthDateToken(title, eventDate) &&
                !HasExactDate(title, eventDate));
    }

    private static bool HasAfconIdentity(string title, Event evt, string league)
    {
        return string.Equals(AfconReleaseLeague(title), league, StringComparison.Ordinal) &&
            !HasAfconConflict(title, evt, league);
    }

    private static bool HasAfcWomensAsianCupConflict(string title, Event evt)
    {
        if (Regex.IsMatch(
                title,
                @"\b(?:U|Under)[\s._-]*(?:17|19|20|21|23)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return true;

        var hasLibraryEpisode = TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches);
        var hasCompetitionIdentity = IsAfcWomensAsianCupRelease(title) ||
            hasLibraryEpisode && IsCanonicalAfcWomensAsianCupName(title);
        if (!hasCompetitionIdentity) return true;

        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (!HasAfconParticipants(title, home, away, evt.HomeTeam, evt.AwayTeam)) return true;

        if (hasLibraryEpisode)
            return !libraryEpisodeMatches;

        var hasExpectedYear = Regex.IsMatch(
            title,
            $@"(?<![0-9]){eventDate.Year}(?![0-9])",
            RegexOptions.CultureInvariant);
        if (!hasExpectedYear) return true;

        var hasExactDate = SearchNormalizationService.HasDayMonthDateToken(title, eventDate) ||
            HasExactDate(title, eventDate);
        var isFinalEvent = int.TryParse(evt.Round, out var eventRound) && eventRound == 200;
        var isUndatedFinal = isFinalEvent &&
            Regex.IsMatch(title, @"\bFinal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            !Regex.IsMatch(
                title,
                @"\b(?:Semi|Quarter)[\s._-]*Final\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return !hasExactDate && !isUndatedFinal;
    }

    private static bool HasAustralianALeagueConflict(string title, Event evt)
    {
        var releaseLeague = ReleaseLeagueKey(title);
        if (releaseLeague == "AustralianALeagueWomen") return true;
        if (releaseLeague != "AustralianALeague") return false;

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (!HasAustralianALeagueTeam(title, home, evt.HomeTeam) ||
            !HasAustralianALeagueTeam(title, away, evt.AwayTeam)) return true;

        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
            return !libraryEpisodeMatches;

        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        return !HasExactDate(title, eventDate) &&
            !SearchNormalizationService.HasDayMonthDateToken(title, eventDate);
    }

    private static bool HasAustralianNblConflict(string title, Event evt)
    {
        if (Regex.IsMatch(
                title,
                @"\bNew[\s._-]+Zealand[\s._-]+NBL\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return true;
        if (!Regex.IsMatch(title, @"\bNBL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return false;

        var eventSeason = Regex.Match(
            evt.Season ?? string.Empty,
            @"^(?<start>20[0-9]{2})[-/](?<end>(?:20)?[0-9]{2})$",
            RegexOptions.CultureInvariant);
        if (!eventSeason.Success) return HasYearConflict(title, evt);

        var eventStart = int.Parse(eventSeason.Groups["start"].Value);
        var eventEnd = ExpandSeasonYear(eventSeason.Groups["end"].Value, eventStart);
        var releaseSeason = Regex.Match(
            title,
            @"\bNBL[\s._-]+(?<start>(?:20)?[0-9]{2})[\s._-]+(?<end>(?:20)?[0-9]{2})\b(?![\s._-]+[0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (releaseSeason.Success)
        {
            var releaseStart = ExpandSeasonYear(releaseSeason.Groups["start"].Value, eventStart);
            var releaseEnd = ExpandSeasonYear(releaseSeason.Groups["end"].Value, releaseStart);
            return releaseStart != eventStart || releaseEnd != eventEnd;
        }

        var championshipYear = Regex.Match(
            title,
            @"\bNBL[\s._-]+(?<year>[0-9]{2})(?=[\s._-]+Australia[\s._-]+Championship[\s._-]+Series\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return championshipYear.Success &&
            2000 + int.Parse(championshipYear.Groups["year"].Value) != eventEnd;
    }

    private static bool HasAustralianNblIdentity(string title, Event evt)
    {
        if (HasAustralianNblConflict(title, evt) ||
            !Regex.IsMatch(title, @"\bNBL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !HasCatalogDate(title, evt))
            return false;

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        return HasTeam(title, home, evt.HomeTeam) && HasTeam(title, away, evt.AwayTeam);
    }

    private static int ExpandSeasonYear(string value, int referenceYear)
    {
        var year = int.Parse(value);
        if (value.Length == 4) return year;
        var expanded = referenceYear / 100 * 100 + year;
        return expanded < referenceYear ? expanded + 100 : expanded;
    }

    private static bool HasAustralianALeagueTeam(string title, string? canonical, Team? team)
    {
        if (HasTeam(title, canonical, team)) return true;
        if (string.IsNullOrWhiteSpace(canonical)) return false;

        var stripped = Regex.Replace(
            canonical.Trim(),
            @"\s+(?:Football Club|Cricket Club|AFC|FC)$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (stripped.Equals(canonical, StringComparison.OrdinalIgnoreCase)) return false;

        var normalizedTeam = Normalize(stripped);
        var sides = Regex.Split(Normalize(title), @"\s+(?:v|vs|versus)\s+", RegexOptions.CultureInvariant);
        for (var index = 0; index < sides.Length - 1; index++)
        {
            var left = sides[index];
            var right = sides[index + 1];
            if (left.EndsWith($" {normalizedTeam}", StringComparison.Ordinal) ||
                left.Equals(normalizedTeam, StringComparison.Ordinal))
            {
                var prefix = left[..^normalizedTeam.Length].TrimEnd();
                var prefixToken = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (prefixToken == null || Regex.IsMatch(
                        prefixToken,
                        @"^(?:a|league|mens?|final|round|r[0-9]+|[0-9]+)$",
                        RegexOptions.CultureInvariant))
                    return true;
            }

            if (right.Equals(normalizedTeam, StringComparison.Ordinal)) return true;
            if (!right.StartsWith($"{normalizedTeam} ", StringComparison.Ordinal)) continue;
            var suffixToken = right[(normalizedTeam.Length + 1)..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (suffixToken != null && Regex.IsMatch(
                    suffixToken,
                    @"^(?:[0-9]{1,2}|[0-9]{3,4}p|hdtv|pdtv|sdtv|uhd|web|webdl|webrip|xvid|x26[45]|h26[45]|hevc|av1|proper|repack)$",
                    RegexOptions.CultureInvariant))
                return true;
        }

        return false;
    }

    private static bool IsAfcWomensAsianCupRelease(string title) => Regex.IsMatch(
        title,
        @"\bAFC[\s._-]+(?:Women(?:'s|s)?[\s._-]+Asian[\s._-]+Cup|Asian[\s._-]+Cup[\s._-]+Women(?:'s|s)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool HasAsianGamesCompetitionConflict(string title, string? leagueName) =>
        leagueName?.Contains("Asian Games", StringComparison.OrdinalIgnoreCase) == true &&
        Regex.IsMatch(
            title,
            @"\b(?:Summer|World)[\s._-]+University[\s._-]+Games\b|\bUniversiade\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsCanonicalAfcWomensAsianCupName(string title) => Regex.IsMatch(
        title,
        @"\bAsian[\s._-]+Cup[\s._-]+Women\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? AfconReleaseLeague(string title)
    {
        var hasCompetition = Regex.IsMatch(
            title,
            @"\bAFCON\b|\bWAFCON\b|\bAfric(?:a|an)[\s._-]+Cup(?:[\s._-]+of)?[\s._-]+Nations\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!hasCompetition) return null;
        var isWomens = Regex.IsMatch(
            title,
            @"\bWAFCON\b|\bWomen(?:s|'s)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var isQualifying = Regex.IsMatch(
            title,
            @"\bQuali(?:f\w*)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (isWomens && isQualifying) return "WAFCONQualifying";
        if (isWomens)
            return "WAFCON";
        if (isQualifying) return "AFCONQualifying";
        return "AFCON";
    }

    private static bool HasAafConflict(string title, Event evt)
    {
        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        var (home, away) = EventQueryService.ResolveTeamNames(evt);

        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
        {
            return !libraryEpisodeMatches ||
                !IsAafRelease(title) ||
                !HasTeam(title, home) ||
                !HasTeam(title, away);
        }

        return !IsAafRelease(title) ||
            HasYearConflict(title, evt) ||
            !Regex.IsMatch(title, $@"(?<![0-9]){eventDate.Year}(?![0-9])", RegexOptions.CultureInvariant) ||
            !SearchNormalizationService.HasDayMonthDateToken(title, eventDate) ||
            !HasTeam(title, home) ||
            !HasTeam(title, away);
    }

    private static bool HasAcaConflict(string title, Event evt)
    {
        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches) && !libraryEpisodeMatches)
            return true;

        var eventCard = AcaCardNumber(evt.Title);
        var releaseCard = AcaCardNumber(title);
        return eventCard == null || releaseCard == null || eventCard != releaseCard;
    }

    private static bool HasAflWConflict(string title, Event evt)
    {
        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (!string.Equals(ReleaseLeagueKey(title), "AFLW", StringComparison.Ordinal) ||
            HasYearConflict(title, evt) ||
            !HasTeam(title, home, evt.HomeTeam) ||
            !HasTeam(title, away, evt.AwayTeam))
        {
            return true;
        }

        var releaseRound = AflRoundPattern.Match(title);
        var hasEventRound = int.TryParse(evt.Round, out var eventRound);
        if (releaseRound.Success && hasEventRound &&
            int.Parse(releaseRound.Groups["round"].Value) != eventRound)
        {
            return true;
        }

        var releaseStage = AflWFinalStage(title);
        if (releaseStage == null)
        {
            if (!hasEventRound || eventRound < 100 || releaseRound.Success) return false;
            var eventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
            return !ExplicitDates(title).Contains(eventDate) &&
                !(TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches) && libraryEpisodeMatches);
        }

        if (!hasEventRound) return true;
        if (releaseStage == "Final")
            return eventRound < 100 ||
                !ExplicitDates(title).Contains((evt.BroadcastDate ?? evt.EventDate.Date).Date);

        return eventRound switch
        {
            160 or 170 => releaseStage is not ("QF" or "EF"),
            125 or 180 => releaseStage != "SF",
            150 => releaseStage != "PF",
            200 => releaseStage != "GF",
            _ => true
        };
    }

    private static string? AflWFinalStage(string title)
    {
        var match = AflWFinalStagePattern.Match(title);
        if (match.Success)
        {
            if (match.Groups["code"].Success) return match.Groups["code"].Value.ToUpperInvariant();
            return match.Groups["name"].Value.ToLowerInvariant() switch
            {
                "qualifying" => "QF",
                "elimination" => "EF",
                "semi" => "SF",
                "preliminary" => "PF",
                "grand" => "GF",
                _ => null
            };
        }

        return Regex.IsMatch(title, @"\bFinals?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            ? "Final"
            : null;
    }

    private static int? AcaCardNumber(string? title)
    {
        var match = Regex.Match(title ?? string.Empty, @"(?<![A-Za-z0-9])ACA[\s._-]+0*(?<card>[1-9][0-9]{0,3})(?:\b|[\s._-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["card"].Value, out var card) ? card : null;
    }

    private static int? BkfcCardNumber(string? title)
    {
        int? card = null;
        foreach (Match match in BkfcCardPattern.Matches(title ?? string.Empty))
        {
            if (!int.TryParse(match.Groups["card"].Value, out var candidate)) continue;
            if (candidate is >= 1900 and <= 2099) continue;
            card = candidate;
        }

        return card;
    }

    private static int? CffcCardNumber(string? title)
    {
        var match = CffcCardPattern.Match(title ?? string.Empty);
        return match.Success && int.TryParse(match.Groups["card"].Value, out var card)
            ? card
            : null;
    }

    private static bool HasCffcIdentity(string title, Event evt)
    {
        var eventCard = CffcCardNumber(evt.Title);
        var releaseCard = CffcCardNumber(title);
        return eventCard.HasValue && releaseCard == eventCard;
    }

    private static bool HasBalticCupConflict(string title, Event evt)
    {
        var hasLibraryEpisode = TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches);
        var eventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (BalticCupSiblingPattern.IsMatch(title) ||
            !HasTeam(title, home, evt.HomeTeam) ||
            !HasTeam(title, away, evt.AwayTeam) ||
            !Regex.IsMatch(title, @"\bFriendly\b|\bBaltic[\s._-]+Cup\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (hasLibraryEpisode)
            return !libraryEpisodeMatches || HasBalticCupExplicitDateConflict(title, eventDate);

        var years = Regex.Matches(title, @"(?<![0-9])20[0-9]{2}(?![0-9])")
            .Select(match => int.Parse(match.Value));
        if (!years.Contains(eventDate.Year)) return true;

        return !SearchNormalizationService.HasDayMonthDateToken(title, eventDate);
    }

    private static bool HasBalticCupExplicitDateConflict(string title, DateTime eventDate)
    {
        var dates = DayMonthYearPattern.Matches(title)
            .Concat(YearMonthDayPattern.Matches(title))
            .Concat(CompactYearMonthDayPattern.Matches(title))
            .ToArray();
        return dates.Length > 0 && !dates.Any(match =>
            int.Parse(match.Groups["day"].Value) == eventDate.Day &&
            int.Parse(match.Groups["month"].Value) == eventDate.Month &&
            int.Parse(match.Groups["year"].Value) == eventDate.Year);
    }

    private static bool HasBasketballAfricaLeagueIdentity(string title, Event evt)
    {
        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        return Regex.IsMatch(
                title,
                @"\bBasketball[\s._-]+Africa[\s._-]+League\b|\bBAL\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            !HasYearConflict(title, evt) &&
            !HasExplicitDateOutsideWindow(title, eventDate, 1) &&
            Regex.IsMatch(title, $@"(?<![0-9]){eventDate.Year}(?![0-9])", RegexOptions.CultureInvariant) &&
            HasTeam(title, home, evt.HomeTeam) &&
            HasTeam(title, away, evt.AwayTeam);
    }

    private static bool HasBaseballAllStarConflict(string title, Event evt)
    {
        if (IsHomeRunDerby(evt.Title)) return !HasBaseballHomeRunDerbyIdentity(title, evt);
        return IsHomeRunDerby(title) && IsBaseballAllStarGame(evt.Title);
    }

    private static bool HasBaseballHomeRunDerbyIdentity(string title, Event evt)
    {
        if (!IsHomeRunDerby(evt.Title) ||
            !IsHomeRunDerby(title) ||
            !Regex.IsMatch(
                title,
                @"\bMLB\b|\bMajor[\s._-]+League[\s._-]+Baseball\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            HasYearConflict(title, evt))
        {
            return false;
        }

        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
        {
            return libraryEpisodeMatches && !HasExplicitDateOutsideWindow(title, eventDate, 0);
        }

        return Regex.IsMatch(title, $@"(?<![0-9]){eventDate.Year}(?![0-9])", RegexOptions.CultureInvariant) &&
            (HasDayMonth(title, eventDate) ||
             SearchNormalizationService.HasDayMonthDateToken(title, eventDate));
    }

    private static bool IsHomeRunDerby(string? title) => Regex.IsMatch(
        title ?? string.Empty,
        @"\bHome[\s._-]+Run[\s._-]+Derby\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsBaseballAllStarGame(string? title) =>
        !IsHomeRunDerby(title) && Regex.IsMatch(
            title ?? string.Empty,
            @"\b(?:MLB|Major[\s._-]+League[\s._-]+Baseball)[\s._-]+All[\s._-]+Star[\s._-]+Game\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool HasBasketballAllStarIdentity(string title, Event evt)
    {
        var eventTitle = evt.Title ?? string.Empty;
        var isNbaEvent = Regex.IsMatch(
            eventTitle,
            @"\bNBA[\s._-]+All[\s._-]+Star[\s._-]+Celebrity[\s._-]+Game\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var isWnbaEvent = Regex.IsMatch(
            eventTitle,
            @"\bWNBA[\s._-]+All[\s._-]+Star[\s._-]+Game\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var hasNbaIdentity = Regex.IsMatch(
            title,
            @"\bNBA(?:[\s._-]+20[0-9]{2})?(?:[\s._/-]+(?:0?[1-9]|1[0-2])[\s._/-]+(?:0?[1-9]|[12][0-9]|3[01]))?[\s._-]+All[\s._-]+Star(?:[\s._-]+Weekend)?[\s._-]+Celebrity[\s._-]+Game\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var hasWnbaIdentity = Regex.IsMatch(
            title,
            @"\bWNBA(?:[\s._-]+20[0-9]{2})?(?:[\s._-]+Regular[\s._-]+Season)?[\s._-]+All[\s._-]+Star[\s._-]+Game\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if ((!isNbaEvent && !isWnbaEvent) ||
            isNbaEvent != hasNbaIdentity ||
            isWnbaEvent != hasWnbaIdentity ||
            Regex.IsMatch(
                title,
                @"\b(?:Postgame|Media[\s._-]+Availability)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            HasYearConflict(title, evt))
        {
            return false;
        }

        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        var years = Regex.Matches(title, @"(?<![0-9])20[0-9]{2}(?![0-9])")
            .Select(match => int.Parse(match.Value))
            .ToArray();
        var explicitDates = ExplicitDates(title);
        if (explicitDates.Length > 0)
        {
            return years.Contains(eventDate.Year) &&
                explicitDates.Any(date => Math.Abs((date - eventDate.Date).TotalDays) <= 1);
        }

        var hasMatchingDate = HasDayMonth(title, eventDate) ||
            HasDayMonth(title, eventDate.AddDays(-1)) ||
            HasDayMonth(title, eventDate.AddDays(1));
        if (years.Length == 0) return HasDayMonth(title, eventDate);
        return years.Contains(eventDate.Year) &&
            (!DayMonthPattern.IsMatch(title) || hasMatchingDate);
    }

    private static bool IsSupportedBasketballAllStarEvent(Event evt) => Regex.IsMatch(
        evt.Title ?? string.Empty,
        @"\bNBA[\s._-]+All[\s._-]+Star[\s._-]+Celebrity[\s._-]+Game\b|\bWNBA[\s._-]+All[\s._-]+Star[\s._-]+Game\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool HasBelgianProLeagueIdentity(string title, Event evt)
    {
        if (!Regex.IsMatch(
                title,
                @"\b(?:Belgian[\s._-]*Pro[\s._-]*League|Jupiler[\s._-]+Pro[\s._-]+League|Football[\s._-]+Jupiler[\s._-]+League)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (!HasTeam(title, home, evt.HomeTeam) || !HasTeam(title, away, evt.AwayTeam)) return false;

        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
        {
            var libraryDates = ExplicitDates(title);
            return libraryEpisodeMatches &&
                (libraryDates.Length > 0
                    ? libraryDates.Contains(eventDate.Date)
                    : !DayMonthPattern.IsMatch(title) || HasDayMonth(title, eventDate));
        }

        if (HasYearConflict(title, evt)) return false;
        var explicitDates = ExplicitDates(title);
        if (explicitDates.Length > 0) return explicitDates.Contains(eventDate.Date);
        if (!Regex.IsMatch(title, $@"(?<![0-9]){eventDate.Year}(?![0-9])", RegexOptions.CultureInvariant))
            return false;
        return HasDayMonth(title, eventDate) ||
            SearchNormalizationService.HasDayMonthDateToken(title, eventDate);
    }

    private static bool HasCanadianTeamCompetitionIdentity(string title, Event evt)
    {
        if (!HasCanadianTeamPair(title, evt)) return false;
        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        return CanadianDateIdentity(title, eventDate).Match;
    }

    private static bool HasChampionsHockeyLeagueIdentity(string title, Event evt)
    {
        if (!IsChampionsHockeyLeagueRelease(title) ||
            !HasChampionsHockeyLeagueTeamPair(title, evt))
        {
            return false;
        }

        var eventDate = (evt.BroadcastDate ?? evt.EventDate).Date;
        var explicitDates = ExplicitDates(title);
        if (explicitDates.Length > 0) return explicitDates.Contains(eventDate);

        var dateSource = CanadianTechnicalTokenPattern.Replace(
            SearchNormalizationService.PrepareReleaseIdentityTitle(title),
            " ");
        var shortDates = ShortDayMonthYearPattern.Matches(dateSource);
        if (shortDates.Count > 0)
        {
            return shortDates.Any(match =>
                int.Parse(match.Groups["day"].Value) == eventDate.Day &&
                int.Parse(match.Groups["month"].Value) == eventDate.Month &&
                int.Parse(match.Groups["year"].Value) == eventDate.Year % 100);
        }

        var hasExpectedYear = Regex.IsMatch(
            title,
            $@"(?<![0-9]){eventDate.Year}(?![0-9])",
            RegexOptions.CultureInvariant);
        return hasExpectedYear &&
            (HasDayMonth(title, eventDate) ||
             SearchNormalizationService.HasDayMonthDateToken(title, eventDate));
    }

    private static bool HasChinaFaCupIdentity(string title, Event evt)
    {
        if (!Regex.IsMatch(
                title,
                @"(?<![\p{L}\p{M}\p{N}])(?:(?:China|Chinese)[\s._-]*FA[\s._-]*Cup|CFA[\s._-]*Cup)(?![\p{L}\p{M}\p{N}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (!HasTeam(title, home, evt.HomeTeam) || !HasTeam(title, away, evt.AwayTeam)) return false;

        var eventDate = (evt.BroadcastDate ?? evt.EventDate).Date;
        return HasChinaFaCupDateIdentity(title, eventDate);
    }

    private static bool HasChinaFaCupDateIdentity(string title, DateTime eventDate)
    {
        var dateSource = CanadianTechnicalTokenPattern.Replace(
            SearchNormalizationService.PrepareReleaseIdentityTitle(title),
            " ");
        var fullDates = ExplicitDates(dateSource);
        if (fullDates.Length > 0)
        {
            return fullDates.Any(date => date.Date == eventDate.Date || date.Date == eventDate.Date.AddDays(1));
        }

        foreach (Match match in ShortDayMonthYearPattern.Matches(dateSource))
        {
            var shortYear = int.Parse(match.Groups["year"].Value);
            var year = shortYear <= 69 ? 2000 + shortYear : 1900 + shortYear;
            var month = int.Parse(match.Groups["month"].Value);
            var day = int.Parse(match.Groups["day"].Value);
            if (day > DateTime.DaysInMonth(year, month)) continue;

            var releaseDate = new DateTime(year, month, day);
            if (releaseDate == eventDate.Date || releaseDate == eventDate.Date.AddDays(1)) return true;
        }

        return false;
    }

    private static bool HasChineseSuperLeagueIdentity(string title, Event evt)
    {
        if (!Regex.IsMatch(
                title,
                @"(?<![\p{L}\p{M}\p{N}])(?:(?:China|Chinese)[\s._-]*Super[\s._-]*League|CSL)(?![\p{L}\p{M}\p{N}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
        {
            if (!libraryEpisodeMatches) return false;

            var hasExplicitMatchup = HasFixtureSeparator(title);
            if (hasExplicitMatchup &&
                (!HasChineseSuperLeagueTeam(title, home, evt.HomeTeam) ||
                 !HasChineseSuperLeagueTeam(title, away, evt.AwayTeam)))
            {
                return false;
            }

            return HasNoConflictingChineseSuperLeagueDate(title, evt);
        }

        if (!HasChineseSuperLeagueTeam(title, home, evt.HomeTeam) ||
            !HasChineseSuperLeagueTeam(title, away, evt.AwayTeam))
        {
            return false;
        }

        return HasExactChineseSuperLeagueDate(title, evt);
    }

    private static bool HasNoConflictingChineseSuperLeagueDate(string title, Event evt)
    {
        var dates = ChineseSuperLeagueDates(title);
        return dates.Length == 0 || dates.Contains((evt.BroadcastDate ?? evt.EventDate).Date);
    }

    private static bool HasExactChineseSuperLeagueDate(string title, Event evt) =>
        ChineseSuperLeagueDates(title).Contains((evt.BroadcastDate ?? evt.EventDate).Date);

    private static DateTime[] ChineseSuperLeagueDates(string title)
    {
        var dateSource = CanadianTechnicalTokenPattern.Replace(
            SearchNormalizationService.PrepareReleaseIdentityTitle(title),
            " ");
        var fullDates = ExplicitDates(dateSource);
        if (fullDates.Length > 0) return fullDates;

        var dates = new List<DateTime>();
        foreach (Match match in ShortDayMonthYearPattern.Matches(dateSource))
        {
            var shortYear = int.Parse(match.Groups["year"].Value);
            var year = shortYear <= 69 ? 2000 + shortYear : 1900 + shortYear;
            var month = int.Parse(match.Groups["month"].Value);
            var day = int.Parse(match.Groups["day"].Value);
            if (day <= DateTime.DaysInMonth(year, month)) dates.Add(new DateTime(year, month, day));
        }

        return dates.Distinct().ToArray();
    }

    private static bool HasFixtureSeparator(string title) => Regex.IsMatch(
        title,
        @"@|(?<![\p{L}\p{M}\p{N}])(?:v|vs|versus)(?![\p{L}\p{M}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool HasChineseSuperLeagueTeam(string title, string? teamName, Team? team)
    {
        if (HasTeam(title, teamName, team)) return true;
        if (string.IsNullOrWhiteSpace(teamName) ||
            !teamName.EndsWith(" Tiger", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ContainsPhrase(Normalize(title), Normalize(teamName + "s"));
    }

    private static string? CzwQuery(Event evt, DateTime date)
    {
        var eventName = Regex.Replace(evt.Title ?? string.Empty, @"[^\p{L}\p{M}\p{N}]+", " ").Trim();
        var day = Regex.Match(
            eventName,
            @"\bTournament\s+Of\s+Death\s+(?<card>[0-9]+)\s+Day\s+(?<day>[12])\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!day.Success) return null;

        var night = day.Groups["day"].Value == "1" ? "One" : "Two";
        return $"CZW Tournament Of Death {day.Groups["card"].Value} Night {night} {date.Year}";
    }

    private static bool HasCzwIdentity(string title, Event evt)
    {
        var normalizedTitle = Normalize(title);
        if (!ContainsPhrase(normalizedTitle, "czw") &&
            !ContainsPhrase(normalizedTitle, "combat zone wrestling"))
            return false;

        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
            return libraryEpisodeMatches &&
                !HasCzwEventConflict(title, evt) &&
                HasCatalogDateIdentity(title, evt, allowYearOnly: true);

        var eventName = Normalize(evt.Title ?? string.Empty);
        var aliases = new HashSet<string>(StringComparer.Ordinal)
        {
            eventName,
            Regex.Replace(eventName, @"\bday 1\b", "night one", RegexOptions.CultureInvariant),
            Regex.Replace(eventName, @"\bday 2\b", "night two", RegexOptions.CultureInvariant)
        };
        return aliases.Any(alias => !string.IsNullOrWhiteSpace(alias) && ContainsPhrase(normalizedTitle, alias)) &&
            HasCatalogDateIdentity(title, evt, allowYearOnly: true);
    }

    private static bool HasCzwEventConflict(string title, Event evt)
    {
        var normalizedEvent = Normalize(evt.Title ?? string.Empty);
        var normalizedRelease = Normalize(title);
        var libraryName = Regex.Match(
            normalizedRelease,
            @"(?:^| )s[0-9]+e[0-9]+ (?<name>.+?)(?: (?:2160p|1080p|720p|480p|web|hdtv|bluray|mkv|mp4|avi)(?: |$)|$)",
            RegexOptions.CultureInvariant);
        if (libraryName.Success)
        {
            var expectedNames = new[]
            {
                normalizedEvent,
                Regex.Replace(normalizedEvent, @"\bday 1\b", "night one", RegexOptions.CultureInvariant),
                Regex.Replace(normalizedEvent, @"\bday 2\b", "night two", RegexOptions.CultureInvariant)
            };
            if (!expectedNames.Any(expected =>
                    !string.IsNullOrWhiteSpace(expected) && ContainsPhrase(libraryName.Groups["name"].Value, expected)))
                return true;
        }

        var expected = Regex.Match(
            normalizedEvent,
            @"(?:^| )tournament of death (?<card>[0-9]+)(?: (?<part>day|night) (?<number>1|2|one|two))?(?: |$)",
            RegexOptions.CultureInvariant);
        var actual = Regex.Match(
            normalizedRelease,
            @"(?:^| )tournament of death (?<card>[0-9]+)(?: (?<part>day|night) (?<number>1|2|one|two))?(?: |$)",
            RegexOptions.CultureInvariant);
        if (!expected.Success || !actual.Success) return false;
        if (!string.Equals(expected.Groups["card"].Value, actual.Groups["card"].Value, StringComparison.Ordinal))
            return true;
        if (!expected.Groups["number"].Success || !actual.Groups["number"].Success) return false;

        return CzwPartNumber(expected.Groups["number"].Value) != CzwPartNumber(actual.Groups["number"].Value);
    }

    private static int CzwPartNumber(string value) => value switch
    {
        "1" or "one" => 1,
        "2" or "two" => 2,
        _ => 0
    };

    private static bool HasCommonwealthAthleticsIdentity(string title, Event evt)
    {
        var normalizedTitle = Normalize(title);
        if (!ContainsPhrase(normalizedTitle, "commonwealth games") ||
            HasCommonwealthDisciplineConflict(title, evt, "CommonwealthGamesAthletics"))
            return false;

        var hasAthletics = Regex.IsMatch(normalizedTitle, @"\bathletics?\b", RegexOptions.CultureInvariant);
        var eventName = Normalize(evt.Title ?? string.Empty);
        var hasEventName = !string.IsNullOrWhiteSpace(eventName) && ContainsPhrase(normalizedTitle, eventName);
        return !HasCommonwealthAthleticsEventConflict(normalizedTitle, eventName) &&
            (hasAthletics || hasEventName) &&
            HasCatalogDateIdentity(title, evt, allowYearOnly: hasEventName);
    }

    private static bool HasEuropeanAthleticsFinalIdentity(string title, Event evt)
    {
        var releaseTitle = Normalize(title);
        var eventTitle = Normalize(evt.Title ?? string.Empty);
        if (!ContainsPhrase(releaseTitle, "european athletics championships") ||
            Regex.IsMatch(releaseTitle, @"(?:^| )u\s*[0-9]{2}(?: |$)", RegexOptions.CultureInvariant) ||
            AthleticsStage(eventTitle) != "final" || AthleticsStage(releaseTitle) != "final")
            return false;

        var eventGender = AthleticsGender(eventTitle);
        var eventDiscipline = AthleticsEventDiscipline(eventTitle);
        return eventGender != null && eventGender == AthleticsGender(releaseTitle) &&
            eventDiscipline != null && eventDiscipline == AthleticsEventDiscipline(releaseTitle) &&
            !HasCommonwealthAthleticsEventConflict(releaseTitle, eventTitle) &&
            HasNoConflictingCatalogDate(title, (evt.BroadcastDate ?? evt.EventDate).Date) &&
            HasCatalogDateIdentity(title, evt, allowYearOnly: false);
    }

    private static bool HasCommonwealthAthleticsEventConflict(string releaseTitle, string eventTitle)
    {
        var releaseDistance = AthleticsDistance(releaseTitle);
        var eventDistance = AthleticsDistance(eventTitle);
        if (releaseDistance != null && eventDistance != null && releaseDistance != eventDistance) return true;

        var releaseDiscipline = AthleticsEventDiscipline(releaseTitle);
        var eventDiscipline = AthleticsEventDiscipline(eventTitle);
        if (releaseDiscipline != null && eventDiscipline != null && releaseDiscipline != eventDiscipline) return true;

        var releaseGender = AthleticsGender(releaseTitle);
        var eventGender = AthleticsGender(eventTitle);
        if (releaseGender != null && eventGender != null && releaseGender != "both" && releaseGender != eventGender) return true;

        var releaseStage = AthleticsStage(releaseTitle);
        var eventStage = AthleticsStage(eventTitle);
        return releaseStage != null && eventStage != null && releaseStage != eventStage;
    }

    private static string? AthleticsDistance(string title)
    {
        if (Regex.IsMatch(title, @"(?:^| )one mile(?: |$)", RegexOptions.CultureInvariant)) return "mile";
        var distance = Regex.Match(title, @"(?:^| )(?<value>[0-9]+)\s*(?:metres|meters|m)(?: |$)", RegexOptions.CultureInvariant);
        return distance.Success ? distance.Groups["value"].Value : null;
    }

    private static string? AthleticsEventDiscipline(string title)
    {
        foreach (var discipline in new[]
                 {
                     "hurdles", "relay", "steeplechase", "race walk", "marathon", "high jump", "long jump",
                     "triple jump", "pole vault", "shot put", "discus", "hammer", "javelin", "decathlon", "heptathlon"
                 })
        {
            if (ContainsPhrase(title, discipline)) return discipline;
        }

        return AthleticsDistance(title) != null ? "track" : null;
    }

    private static string? AthleticsGender(string title)
    {
        var hasMen = Regex.IsMatch(title, @"(?:^| )(?:men|mens|male)(?: |$)", RegexOptions.CultureInvariant);
        var hasWomen = Regex.IsMatch(title, @"(?:^| )(?:women|womens|female)(?: |$)", RegexOptions.CultureInvariant);
        if (hasMen && hasWomen) return "both";
        if (hasMen) return "men";
        if (hasWomen) return "women";
        return null;
    }

    private static string? AthleticsStage(string title)
    {
        var round = Regex.Match(title, @"(?:^| )round (?<number>[0-9]+)(?: |$)", RegexOptions.CultureInvariant);
        if (round.Success) return "round " + round.Groups["number"].Value;
        if (Regex.IsMatch(title, @"(?:^| )(?:semi finals?|semifinals?)(?: |$)", RegexOptions.CultureInvariant)) return "semifinal";
        if (Regex.IsMatch(title, @"(?:^| )final(?: |$)", RegexOptions.CultureInvariant)) return "final";
        if (Regex.IsMatch(title, @"(?:^| )(?:qualification|qualifying)(?: |$)", RegexOptions.CultureInvariant)) return "qualification";
        return null;
    }

    private static bool HasCommonwealthDisciplineConflict(string title, Event evt, string league)
    {
        if (HasYearConflict(title, evt)) return true;

        var normalizedTitle = Normalize(title);
        var hasCommonwealth = ContainsPhrase(normalizedTitle, "commonwealth games");
        if (!hasCommonwealth &&
            (ContainsPhrase(normalizedTitle, "olympic games") || ContainsPhrase(normalizedTitle, "olympics games")))
            return true;
        if (!hasCommonwealth) return false;

        var releaseDiscipline = Regex.IsMatch(normalizedTitle, @"\bathletics?\b", RegexOptions.CultureInvariant)
            ? "CommonwealthGamesAthletics"
            : Regex.IsMatch(normalizedTitle, @"\bgymnastics?\b", RegexOptions.CultureInvariant)
                ? "CommonwealthGamesGymnastics"
                : Regex.IsMatch(normalizedTitle, @"\brugby\b", RegexOptions.CultureInvariant)
                    ? "CommonwealthGames7sRugby"
                    : Regex.IsMatch(normalizedTitle, @"\bbasketball\b|\b3x3\b", RegexOptions.CultureInvariant)
                        ? "CommonwealthGames3x3BasketballWomen"
                        : null;
        return releaseDiscipline != null && !string.Equals(releaseDiscipline, league, StringComparison.Ordinal);
    }

    private static bool HasClubFriendlyIdentity(string title, Event evt)
    {
        if (!Regex.IsMatch(
                title,
                @"(?<![\p{L}\p{M}\p{N}])(?:Club[\s._-]+Friendl(?:y|ies)|European[\s._-]+Friendly|Friendly)(?![\p{L}\p{M}\p{N}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        return HasFixtureTeamDateIdentity(title, evt);
    }

    private static bool HasPremierLeagueSummerSeriesIdentity(string title, Event evt) =>
        (HasClubFriendlyIdentity(title, evt) ||
            ContainsPhrase(Normalize(title), "premier league summer series") &&
            HasFixtureTeamDateIdentity(title, evt));

    private static bool HasFixtureTeamDateIdentity(string title, Event evt)
    {
        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (!HasNormalizedTeam(title, home, evt.HomeTeam) ||
            !HasNormalizedTeam(title, away, evt.AwayTeam))
        {
            return false;
        }

        return HasCatalogDateIdentity(title, evt, allowYearOnly: false);
    }

    private static bool HasConfederationsCupConflict(string title, Event evt)
    {
        var normalized = Normalize(title);
        if (HasYearConflict(title, evt) ||
            ContainsPhrase(normalized, "caf confederation cup"))
        {
            return true;
        }

        if (!int.TryParse(evt.Round, out var round)) return false;

        var group = Regex.IsMatch(normalized, @"\bgroup\b", RegexOptions.CultureInvariant);
        var semiFinal = Regex.IsMatch(normalized, @"\bsemi final\b", RegexOptions.CultureInvariant);
        var quarterFinal = Regex.IsMatch(normalized, @"\bquarter final\b", RegexOptions.CultureInvariant);
        var final = !semiFinal && !quarterFinal &&
            Regex.IsMatch(normalized, @"\bfinal\b", RegexOptions.CultureInvariant);

        if (round < 100) return final || semiFinal || quarterFinal;
        if (group) return true;
        if (round == 200)
        {
            if (semiFinal || quarterFinal) return true;
            var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
            return !final && !SearchNormalizationService.HasDayMonthDateToken(title, eventDate);
        }

        return final;
    }

    private static bool HasConfederationsCupIdentity(string title, Event evt)
    {
        if (HasConfederationsCupConflict(title, evt)) return false;

        var normalized = Normalize(title);
        if (!Regex.IsMatch(normalized, @"\bconfederations? cup\b", RegexOptions.CultureInvariant))
            return false;

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (!HasTeam(title, home, evt.HomeTeam) || !HasTeam(title, away, evt.AwayTeam))
            return false;

        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        if (SearchNormalizationService.HasDayMonthDateToken(title, eventDate)) return true;

        if (!int.TryParse(evt.Round, out var round)) return false;
        if (round == 200)
        {
            return Regex.IsMatch(normalized, @"\bfinal\b", RegexOptions.CultureInvariant) &&
                !Regex.IsMatch(normalized, @"\b(?:semi|quarter) final\b", RegexOptions.CultureInvariant);
        }

        return round < 100 && Regex.IsMatch(normalized, @"\bgroup\b", RegexOptions.CultureInvariant);
    }

    private static bool HasColombiaPrimeraIdentity(string title, Event evt, string league)
    {
        var releaseLeague = Regex.IsMatch(
            title,
            @"(?<![\p{L}\p{M}\p{N}])(?:Colombia|Categoria|Categoría)[\s._/-]+Primera[\s._/-]+A(?![\p{L}\p{M}\p{N}])|(?<![\p{L}\p{M}\p{N}])Liga[\s._/-]+BetPlay(?![\p{L}\p{M}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            ? "ColombiaPrimeraA"
            : Regex.IsMatch(
                title,
                @"(?<![\p{L}\p{M}\p{N}])(?:Colombia|Categoria|Categoría)[\s._/-]+Primera[\s._/-]+B(?![\p{L}\p{M}\p{N}])|(?<![\p{L}\p{M}\p{N}])Torneo[\s._/-]+BetPlay(?![\p{L}\p{M}\p{N}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                ? "ColombiaPrimeraB"
                : null;
        if (!string.Equals(releaseLeague, league, StringComparison.Ordinal)) return false;

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        return HasNormalizedTeam(title, home, evt.HomeTeam) &&
            HasNormalizedTeam(title, away, evt.AwayTeam) &&
            HasCatalogDateIdentity(title, evt, allowYearOnly: false);
    }

    private static bool HasCatalogDateIdentity(string title, Event evt, bool allowYearOnly)
    {
        var hasLibraryEpisode = TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches);
        if (hasLibraryEpisode && !libraryEpisodeMatches) return false;

        var eventDate = (evt.BroadcastDate ?? evt.EventDate).Date;
        if (SearchNormalizationService.IsCoupangPlayRelease(title))
            return SearchNormalizationService.ParseCoupangMonthFirstDate(title) == eventDate;

        var dateSource = CanadianTechnicalTokenPattern.Replace(
            SearchNormalizationService.PrepareReleaseIdentityTitle(title),
            " ");
        var explicitDates = CatalogExplicitDates(dateSource);
        if (explicitDates.Length > 0)
            return explicitDates.Contains(eventDate);

        var years = Regex.Matches(dateSource, @"(?<![0-9])20[0-9]{2}(?![0-9])")
            .Select(match => int.Parse(match.Value))
            .ToArray();
        if (years.Length == 0 || years.All(year => year != eventDate.Year)) return false;

        var numericPairs = Regex.Matches(
                dateSource,
                @"(?<![0-9])(?<first>0?[1-9]|[12][0-9]|3[01])[\s._/-]+(?<second>0?[1-9]|[12][0-9]|3[01])(?![0-9])",
                RegexOptions.CultureInvariant)
            .Select(match => (
                First: int.Parse(match.Groups["first"].Value),
                Second: int.Parse(match.Groups["second"].Value)))
            .ToArray();
        if (numericPairs.Length == 0) return hasLibraryEpisode || allowYearOnly;

        return numericPairs.Any(pair =>
            pair.First == eventDate.Month && pair.Second == eventDate.Day ||
            pair.First == eventDate.Day && pair.Second == eventDate.Month);
    }

    private static DateTime[] CatalogExplicitDates(string title)
    {
        var fullDates = ExplicitDates(title).ToList();
        foreach (Match match in CatalogLongDatePattern.Matches(title))
        {
            var year = int.Parse(match.Groups["year"].Value);
            var first = int.Parse(match.Groups["first"].Value);
            var second = int.Parse(match.Groups["second"].Value);
            AddDate(fullDates, year, second, first);
            AddDate(fullDates, year, first, second);
        }
        if (fullDates.Count > 0) return fullDates.Distinct().ToArray();

        var dates = new List<DateTime>();
        foreach (Match match in CatalogShortDatePattern.Matches(title))
        {
            var shortYear = int.Parse(match.Groups["year"].Value);
            var year = shortYear <= 69 ? 2000 + shortYear : 1900 + shortYear;
            var first = int.Parse(match.Groups["first"].Value);
            var second = int.Parse(match.Groups["second"].Value);
            AddDate(dates, year, second, first);
            AddDate(dates, year, first, second);
        }

        return dates.Distinct().ToArray();
    }

    private static void AddDate(List<DateTime> dates, int year, int month, int day)
    {
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return;
        dates.Add(new DateTime(year, month, day));
    }

    private static bool HasNormalizedTeam(string title, string? canonical, Team? team)
    {
        if (string.IsNullOrWhiteSpace(canonical)) return false;

        var names = new List<string?> { canonical, SearchTeamName(canonical), team?.ShortName };
        names.AddRange(TeamNameVariationData.Variations
            .Where(pair => canonical.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value));
        foreach (var value in new[] { team?.AlternateName, team?.UserAliases })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            names.AddRange(Regex.Split(value, @"[,|;/]").Select(alias => alias.Trim()));
        }

        var normalizedTitle = Normalize(SearchNormalizationService.RemoveDiacritics(title));
        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => Normalize(SearchNormalizationService.RemoveDiacritics(name!)))
            .Where(name => name.Length >= 3)
            .Any(name => ContainsPhrase(normalizedTitle, name));
    }

    private static string ClubFriendlyQueryTeamName(string value)
    {
        if (value.Equals("Tottenham Hotspur", StringComparison.OrdinalIgnoreCase)) return "Tottenham";
        if (value.Equals("Bayern Munich", StringComparison.OrdinalIgnoreCase)) return "Bayern";
        return value.Trim();
    }

    public static bool IsChineseCbaSeasonPack(Event evt) =>
        LeagueKey(evt.League?.Name) == "ChineseCBA";

    public static bool HasChineseCbaSeasonPackIdentity(string title, Event evt)
    {
        if (!IsChineseCbaSeasonPack(evt) ||
            !Regex.IsMatch(
                title,
                @"(?<![\p{L}\p{M}\p{N}])Chinese[\s._-]+Basketball[\s._-]+Association(?![\p{L}\p{M}\p{N}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (HasFixtureSeparator(title))
        {
            return false;
        }

        var dateSource = CanadianTechnicalTokenPattern.Replace(
            SearchNormalizationService.PrepareReleaseIdentityTitle(title),
            " ");
        if (ExplicitDates(dateSource).Length > 0 || ShortDayMonthYearPattern.IsMatch(dateSource))
        {
            return false;
        }

        var season = Regex.Match(
            evt.Season ?? string.Empty,
            @"^(?<start>20[0-9]{2})[^0-9]+(?<end>(?:20)?[0-9]{2})$",
            RegexOptions.CultureInvariant);
        if (!season.Success) return false;

        var startYear = int.Parse(season.Groups["start"].Value);
        var endText = season.Groups["end"].Value;
        var endYear = endText.Length == 2
            ? startYear / 100 * 100 + int.Parse(endText)
            : int.Parse(endText);
        if (endYear <= startYear) endYear += 100;

        return Regex.IsMatch(
            title,
            $@"(?<![0-9]){startYear}[\s._-]+(?:{endYear}|{endYear % 100:D2})(?![0-9])",
            RegexOptions.CultureInvariant);
    }

    private static bool IsChampionsHockeyLeagueRelease(string title)
    {
        if (!Regex.IsMatch(
                title,
                @"^(?:CHL|Champions[\s._-]+Hockey[\s._-]+League)(?:[\s._-]|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        var normalizedTitle = Regex.Replace(title, @"[._-]+", " ");
        return !Regex.IsMatch(
            normalizedTitle,
            @"\b(?:Memorial\s+Cup|OHL|WHL|QMJHL|LHJMQ|USA\s+Prospects?|Top\s+Prospects?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasChampionsHockeyLeagueTeamPair(string title, Event evt)
    {
        var participants = StructuredEventTeamPair(evt);
        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        var first = participants?.Home ?? home;
        var second = participants?.Away ?? away;
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;

        var normalizedTitle = Normalize(SearchNormalizationService.RemoveDiacritics(title));
        var firstCore = Normalize(ChampionsHockeyLeagueTeamCore(first));
        var secondCore = Normalize(ChampionsHockeyLeagueTeamCore(second));
        return firstCore.Length >= 3 && secondCore.Length >= 3 &&
            ContainsPhrase(normalizedTitle, firstCore) &&
            ContainsPhrase(normalizedTitle, secondCore);
    }

    private static bool HasCanadianTeamCompetitionConflict(string title, Event evt)
    {
        if (!HasCanadianTeamPair(title, evt))
        {
            return HasAnyCanadianEventTeam(title, evt) || Regex.IsMatch(
                title,
                @"(?:^|[\s._-])(?:vs?\.?|@)(?:[\s._-]|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        return CanadianDateIdentity(title, eventDate).Conflict;
    }

    private static bool HasCanadianTeamPair(string title, Event evt)
    {
        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away)) return false;
        var eventPair = StructuredEventTeamPair(evt);
        if (StructuredReleaseTeamPair(title) is { } releasePair)
        {
            if (eventPair is { } structuredPair && CanadianFixtureSidesMatch(
                    releasePair, structuredPair, null, null))
            {
                return true;
            }
            return CanadianFixtureSidesMatch(releasePair, (home, away), evt.HomeTeam, evt.AwayTeam);
        }
        if (eventPair is { } pair && HasTeam(title, pair.Home) && HasTeam(title, pair.Away))
        {
            return true;
        }
        var normalizedTitle = Normalize(SearchNormalizationService.RemoveDiacritics(title));
        var hasHome = HasTeam(title, home, evt.HomeTeam) || ContainsPhrase(
            normalizedTitle,
            Normalize(SearchNormalizationService.RemoveDiacritics(SearchTeamName(home))));
        var hasAway = HasTeam(title, away, evt.AwayTeam) || ContainsPhrase(
            normalizedTitle,
            Normalize(SearchNormalizationService.RemoveDiacritics(SearchTeamName(away))));
        return hasHome && hasAway;
    }

    private static bool CanadianFixtureSidesMatch(
        (string Left, string Right) releasePair,
        (string Home, string Away) eventPair,
        Team? homeTeam,
        Team? awayTeam) =>
        HasCanadianTeamOnFixtureSide(releasePair.Left, eventPair.Home, homeTeam, atEnd: true) &&
        HasCanadianTeamOnFixtureSide(releasePair.Right, eventPair.Away, awayTeam, atEnd: false) ||
        HasCanadianTeamOnFixtureSide(releasePair.Left, eventPair.Away, awayTeam, atEnd: true) &&
        HasCanadianTeamOnFixtureSide(releasePair.Right, eventPair.Home, homeTeam, atEnd: false);

    private static bool HasAnyCanadianEventTeam(string title, Event evt)
    {
        if (StructuredEventTeamPair(evt) is { } pair &&
            (HasTeam(title, pair.Home) || HasTeam(title, pair.Away)))
        {
            return true;
        }

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        return HasTeam(title, home, evt.HomeTeam) || HasTeam(title, away, evt.AwayTeam);
    }

    private static bool HasCanadianTeamOnFixtureSide(
        string side,
        string? canonical,
        Team? team,
        bool atEnd)
    {
        if (string.IsNullOrWhiteSpace(canonical)) return false;
        var normalizedSide = Normalize(SearchNormalizationService.RemoveDiacritics(side));
        return CanadianTeamNames(canonical, team).Any(name => Regex.IsMatch(
            normalizedSide,
            atEnd
                ? $@"(?:^| ){Regex.Escape(name)}(?: (?:fc|hc|sc|club))*$"
                : $@"^{Regex.Escape(name)}(?: |$)",
            RegexOptions.CultureInvariant));
    }

    private static string[] CanadianTeamNames(string canonical, Team? team)
    {
        var names = new List<string?> { canonical, SearchTeamName(canonical), team?.ShortName };
        names.AddRange(TeamNameVariationData.Variations
            .Where(pair => canonical.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value));
        foreach (var value in new[] { team?.AlternateName, team?.UserAliases })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            names.AddRange(Regex.Split(value, @"[,|;/]").Select(alias => alias.Trim()));
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in names.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalized = Normalize(SearchNormalizationService.RemoveDiacritics(value!));
            if (normalized.Length > 0) result.Add(normalized);
            var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            while (words.Count > 1 && words[^1] is "fc" or "hc" or "sc" or "club" or "hockey" or "basketball")
            {
                words.RemoveAt(words.Count - 1);
            }
            if (words.Count > 1 && words[^1] == "ice") words.RemoveAt(words.Count - 1);
            if (words.Count == 0) continue;
            if (words.Count == 1)
            {
                result.Add(words[0]);
                continue;
            }
            if (words[0] is "atletico" or "afc" or "cf" or "fc")
            {
                var shortName = string.Join(' ', words.Skip(1));
                if (shortName.Length >= 4) result.Add(shortName);
            }
        }
        return result.ToArray();
    }

    private static (string Home, string Away)? StructuredEventTeamPair(Event evt)
    {
        var match = Regex.Match(
            evt.Title ?? string.Empty,
            @"^\s*(?<home>.+?)\s+(?:vs?\.?|@)\s+(?<away>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        var home = match.Groups["home"].Value.Trim();
        var away = match.Groups["away"].Value.Trim();
        return string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away)
            ? null
            : (home, away);
    }

    private static (string Left, string Right)? StructuredReleaseTeamPair(string title)
    {
        var normalizedTitle = Regex.Replace(
            SearchNormalizationService.RemoveDiacritics(title),
            @"[._-]+",
            " ").Trim();
        var match = Regex.Match(
            normalizedTitle,
            @"^(?<left>.+?)\s+(?:vs?\.?|@)\s+(?<right>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        var left = match.Groups["left"].Value.Trim();
        var right = match.Groups["right"].Value.Trim();
        return string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)
            ? null
            : (left, right);
    }

    private static (bool Match, bool Conflict) CanadianDateIdentity(string title, DateTime eventDate)
    {
        var dateSource = CanadianTechnicalTokenPattern.Replace(
            SearchNormalizationService.PrepareReleaseIdentityTitle(title),
            " ");
        var fullDates = CanadianDayMonthYearPattern.Matches(dateSource)
            .Concat(CanadianYearMonthDayPattern.Matches(dateSource))
            .Concat(CanadianCompactYearMonthDayPattern.Matches(dateSource))
            .Select(match => DateFromMatch(match))
            .Where(date => date.HasValue)
            .Select(date => date!.Value.Date)
            .Distinct()
            .ToArray();
        if (fullDates.Length > 0)
        {
            var matches = fullDates.Contains(eventDate.Date);
            return (matches, !matches);
        }

        var shortDates = ShortDayMonthYearPattern.Matches(dateSource);
        if (shortDates.Count > 0)
        {
            var matches = shortDates.Any(match =>
                int.Parse(match.Groups["day"].Value) == eventDate.Day &&
                int.Parse(match.Groups["month"].Value) == eventDate.Month &&
                int.Parse(match.Groups["year"].Value) == eventDate.Year % 100);
            return (matches, !matches);
        }

        var years = Regex.Matches(dateSource, @"(?<![0-9])(?:19|20)[0-9]{2}(?![0-9])")
            .Select(match => int.Parse(match.Value))
            .ToArray();
        var dayMonths = DayMonthPattern.Matches(dateSource);
        if (dayMonths.Count == 0)
        {
            return (false, years.Length > 0 && !years.Contains(eventDate.Year));
        }

        var dayMonthMatches = dayMonths.Any(match =>
            int.Parse(match.Groups["day"].Value) == eventDate.Day &&
            int.Parse(match.Groups["month"].Value) == eventDate.Month);
        if (years.Length == 0) return (false, !dayMonthMatches);
        var matchesDate = years.Contains(eventDate.Year) && dayMonthMatches;
        return (matchesDate, !matchesDate);
    }

    private static DateTime? DateFromMatch(Match match)
    {
        var year = int.Parse(match.Groups["year"].Value);
        var month = int.Parse(match.Groups["month"].Value);
        var day = int.Parse(match.Groups["day"].Value);
        return day <= DateTime.DaysInMonth(year, month) ? new DateTime(year, month, day) : null;
    }

    private static bool HasBritishIrishLionsIdentity(string title, Event evt)
    {
        if (!string.Equals(ReleaseLeagueKey(title), "BritishIrishLions", StringComparison.Ordinal))
            return false;

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away)) return false;
        var normalized = Normalize(title);
        var homeIdentity = Regex.Replace(home.Trim(), @"\s+Rugby$", string.Empty, RegexOptions.IgnoreCase);
        var awayIdentity = Regex.Replace(away.Trim(), @"\s+Rugby$", string.Empty, RegexOptions.IgnoreCase);
        if (!HasLionsParticipantIdentity(normalized, homeIdentity) ||
            !HasLionsParticipantIdentity(normalized, awayIdentity))
        {
            return false;
        }

        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        if (HasYearConflict(title, evt)) return false;
        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
            return libraryEpisodeMatches && HasNoConflictingCatalogDate(title, eventDate);

        if (!Regex.IsMatch(title, $@"(?<![0-9]){eventDate.Year}(?![0-9])", RegexOptions.CultureInvariant))
        {
            return false;
        }

        var explicitDates = ExplicitDates(title);
        return explicitDates.Length > 0
            ? explicitDates.Contains(eventDate.Date)
            : HasDayMonth(title, eventDate) ||
              SearchNormalizationService.HasDayMonthDateToken(title, eventDate);
    }

    private static bool HasBsbIdentity(string title, Event evt)
    {
        if (!string.Equals(ReleaseLeagueKey(title), "BSB", StringComparison.Ordinal) ||
            Regex.IsMatch(title, @"\b(?:World[\s._-]*Superbike|WorldSBK|WSBK)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !int.TryParse(evt.Round, out var eventRound))
        {
            return false;
        }

        var location = !string.IsNullOrWhiteSpace(evt.Venue)
            ? evt.Venue
            : !string.IsNullOrWhiteSpace(evt.Location) && evt.Location.Length > 2
                ? evt.Location
                : Regex.Split(evt.Title ?? string.Empty, @"\s+-\s+", RegexOptions.CultureInvariant)[0];
        if (string.IsNullOrWhiteSpace(location) ||
            !ContainsPhrase(Normalize(title), Normalize(location)))
        {
            return false;
        }

        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        if (HasYearConflict(title, evt)) return false;

        var eventRace = BsbRaceNumber(evt.Title);
        var releaseRace = BsbRaceNumber(title);
        var eventSession = eventRace == null
            ? SportsFileNameParser.DetectMotorsportSession(evt.Title ?? string.Empty)
            : null;
        var releaseSession = eventRace == null
            ? SportsFileNameParser.DetectMotorsportSession(title)
            : null;
        var sessionMatches = eventRace != null
            ? releaseRace == eventRace && !HasAmbiguousNumericBsbRace(title, eventDate)
            : eventSession is not null and not "Race" &&
              releaseRace == null && eventSession == releaseSession;
        if (!sessionMatches) return false;
        var releaseRound = BsbRoundPattern.Match(title);
        var roundMatches = !releaseRound.Success ||
            int.Parse(releaseRound.Groups["round"].Value) == eventRound;
        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
        {
            return libraryEpisodeMatches &&
                roundMatches &&
                HasNoConflictingCatalogDate(title, eventDate);
        }

        if (!Regex.IsMatch(title, $@"(?<![0-9]){eventDate.Year}(?![0-9])", RegexOptions.CultureInvariant))
        {
            return false;
        }

        return releaseRound.Success &&
            roundMatches &&
            HasNoConflictingCatalogDate(title, eventDate);
    }

    private static bool HasLionsParticipantIdentity(string normalizedTitle, string identity)
    {
        if (ContainsPhrase(normalizedTitle, Normalize(identity))) return true;

        var withoutAnd = Regex.Replace(
            identity,
            @"\band\b",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return ContainsPhrase(normalizedTitle, Normalize(withoutAnd));
    }

    private static int? BsbRaceNumber(string? title)
    {
        var match = BsbRaceSessionPattern.Match(title ?? string.Empty);
        if (!match.Success) return null;

        return match.Groups["race"].Value.ToLowerInvariant() switch
        {
            "one" => 1,
            "two" => 2,
            "three" => 3,
            var number => int.Parse(number),
        };
    }

    private static bool HasAmbiguousNumericBsbRace(string title, DateTime eventDate)
    {
        var match = Regex.Match(
            title,
            @"\bRace[\s._-]*(?<race>0?[1-3])[\s._-]+(?<next>0?[1-9]|1[0-2])(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;

        var dayMonth = PaddedDayMonthPattern.Match(title, match.Groups["next"].Index);
        return match.Groups["race"].Length != 1 ||
            !dayMonth.Success ||
            dayMonth.Index != match.Groups["next"].Index ||
            int.Parse(dayMonth.Groups["day"].Value) != eventDate.Day ||
            int.Parse(dayMonth.Groups["month"].Value) != eventDate.Month;
    }

    private static bool HasNoConflictingCatalogDate(string title, DateTime eventDate)
    {
        var dates = ExplicitDates(title);
        if (dates.Length > 0) return dates.Contains(eventDate.Date);

        var paddedDates = PaddedDayMonthPattern.Matches(title)
            .Select(match => (
                Day: int.Parse(match.Groups["day"].Value),
                Month: int.Parse(match.Groups["month"].Value)))
            .ToArray();
        return paddedDates.Length == 0 ||
            paddedDates.Any(date => date.Day == eventDate.Day && date.Month == eventDate.Month);
    }

    private static bool HasBellatorIdentity(string title, Event evt)
    {
        if (!Regex.IsMatch(title, @"\bBellator\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        var eventDate = evt.BroadcastDate ?? evt.EventDate.Date;
        if (IsBellatorPflCrossoverEvent(evt))
        {
            if (!Regex.IsMatch(title, @"\bPFL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return false;
            }

            if (TryMatchLibraryEpisode(title, evt, out var crossoverEpisodeMatches))
                return crossoverEpisodeMatches && HasNoConflictingDate(title, eventDate);

            if (HasYearConflict(title, evt) ||
                !Regex.IsMatch(title, $@"(?<![0-9]){eventDate.Year}(?![0-9])", RegexOptions.CultureInvariant))
                return false;

            var dates = ExplicitDates(title);
            if (dates.Length > 0) return dates.Contains(eventDate.Date);
            return HasDayMonth(title, eventDate) ||
                SearchNormalizationService.HasDayMonthDateToken(title, eventDate);
        }

        if (!IsBellatorSeriesFiveEvent(evt)) return false;
        var hasParticipants = HasTeam(title, "McCourt") && HasTeam(title, "Collins");
        var series = BellatorChampionsSeriesPattern.Match(title);
        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
        {
            return libraryEpisodeMatches &&
                series.Success &&
                int.Parse(series.Groups["number"].Value) == 5 &&
                hasParticipants &&
                HasNoConflictingDate(title, eventDate);
        }

        if (HasYearConflict(title, evt)) return false;
        var hasEventYear = Regex.IsMatch(
            title,
            $@"(?<![0-9]){eventDate.Year}(?![0-9])",
            RegexOptions.CultureInvariant);
        if (series.Success)
        {
            return int.Parse(series.Groups["number"].Value) == 5 &&
                hasParticipants &&
                hasEventYear &&
                HasNoConflictingDate(title, eventDate);
        }

        return Regex.IsMatch(
                title,
                @"\bBellator[\s._-]+Champions[\s._-]+Series[\s._-]+London\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            (!Regex.IsMatch(title, @"\b(?:v(?:s)?|versus)\b\.?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
             hasParticipants) &&
            ExplicitDates(title).Contains(eventDate.Date);
    }

    private static bool HasNoConflictingDate(string title, DateTime eventDate)
    {
        var dates = ExplicitDates(title);
        if (dates.Length > 0) return dates.Contains(eventDate.Date);
        return !DayMonthPattern.IsMatch(title) || HasDayMonth(title, eventDate);
    }

    private static bool IsSupportedBellatorEvent(Event evt) =>
        IsBellatorSeriesFiveEvent(evt) || IsBellatorPflCrossoverEvent(evt);

    private static bool IsBellatorSeriesFiveEvent(Event evt) => Regex.IsMatch(
        evt.Title ?? string.Empty,
        @"\bBellator[\s._-]+Champions[\s._-]+Series[\s._-]+5\b.*\bMcCourt\b.*\bCollins\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsBellatorPflCrossoverEvent(Event evt) => Regex.IsMatch(
        evt.Title ?? string.Empty,
        @"^PFL[\s._-]+vs?\.?[\s._-]+Bellator$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool HasDayMonth(string title, DateTime date) => DayMonthPattern.Matches(title).Any(match =>
        int.Parse(match.Groups["day"].Value) == date.Day &&
        int.Parse(match.Groups["month"].Value) == date.Month);

    private static bool HasExplicitDateOutsideWindow(string title, DateTime eventDate, int allowedDays)
    {
        var dates = ExplicitDates(title);
        return dates.Length > 0 && dates.All(date => Math.Abs((date - eventDate.Date).TotalDays) > allowedDays);
    }

    private static DateTime[] ExplicitDates(string title)
    {
        var dates = new List<DateTime>();
        var matches = DayMonthYearPattern.Matches(title)
            .Concat(YearMonthDayPattern.Matches(title))
            .Concat(CompactYearMonthDayPattern.Matches(title));
        foreach (var match in matches)
        {
            var year = int.Parse(match.Groups["year"].Value);
            var month = int.Parse(match.Groups["month"].Value);
            var day = int.Parse(match.Groups["day"].Value);
            if (day <= DateTime.DaysInMonth(year, month)) dates.Add(new DateTime(year, month, day));
        }

        return dates.Distinct().ToArray();
    }

    private static bool HasBkfcConflict(string title, Event evt)
    {
        var releaseCard = BkfcCardNumber(title);
        if (releaseCard == null) return false;
        var eventCard = BkfcCardNumber(evt.Title);
        return eventCard == null || releaseCard != eventCard;
    }

    private static bool HasBkfcIdentity(string title, Event evt)
    {
        var releaseCard = BkfcCardNumber(title);
        var eventCard = BkfcCardNumber(evt.Title);
        return releaseCard != null && releaseCard == eventCard;
    }

    private static bool HasBtccConflict(string title, Event evt)
    {
        if (!Regex.IsMatch(title, @"\bBTCC\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        if (HasYearConflict(title, evt) ||
            BtccRoundRangePattern.IsMatch(title) ||
            BtccCombinedCoveragePattern.IsMatch(title))
        {
            return true;
        }

        var releaseRound = BtccRoundPattern.Match(title);
        if (!int.TryParse(evt.Round, out var eventRound)) return false;
        if (releaseRound.Success && int.Parse(releaseRound.Groups["round"].Value) != eventRound)
            return true;

        var eventRace = BtccRaceNumber(evt.Title);
        var releaseRace = BtccRaceNumber(title);
        if (eventRace != null && releaseRound.Success && releaseRace == null) return true;
        return eventRace != null && releaseRace != null && eventRace != releaseRace;
    }

    private static bool HasBtccIdentity(string title, Event evt)
    {
        if (!Regex.IsMatch(title, @"\bBTCC\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            HasBtccConflict(title, evt) ||
            !int.TryParse(evt.Round, out var eventRound))
        {
            return false;
        }

        var releaseRound = BtccRoundPattern.Match(title);
        var eventRace = BtccRaceNumber(evt.Title);
        var releaseRace = BtccRaceNumber(title);
        return releaseRound.Success &&
            int.Parse(releaseRound.Groups["round"].Value) == eventRound &&
            eventRace != null &&
            releaseRace == eventRace;
    }

    private static int? BtccRaceNumber(string? title)
    {
        var match = BtccRacePattern.Match(title ?? string.Empty);
        if (!match.Success) return null;

        return match.Groups["race"].Value.ToLowerInvariant() switch
        {
            "1" or "one" => 1,
            "2" or "two" => 2,
            "3" or "three" => 3,
            _ => null
        };
    }

    internal static bool HasMatchingEHFSeasonEpisode(string title, Event evt) =>
        LeagueKey(evt.League?.Name) == "EHFChampionsLeague" &&
        TryMatchLibraryEpisode(title, evt, out var matches) && matches;

    internal static bool HasMatchingEHFSeasonStartIdentity(string title, Event evt, int releaseYear)
    {
        if (!TryGetEHFSeasonYears(evt, out var startYear, out _) || startYear != releaseYear)
            return false;

        var eventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
        var explicitDateMatches = CatalogExplicitDates(title).Any(date =>
            Math.Abs((date - eventDate).TotalDays) <= 1);
        var writtenDate = SportsFileNameParser.ParseWrittenMonthDate(title);
        if (!HasMatchingEHFSeasonSpan(title, evt) &&
            !explicitDateMatches &&
            (!writtenDate.HasValue || Math.Abs((writtenDate.Value - eventDate).TotalDays) > 1))
            return false;

        return HasCatalogTeamIdentity(title, evt, "EHFChampionsLeague");
    }

    private static bool HasMatchingEHFSeasonSpan(string title, Event evt)
    {
        if (!TryGetEHFSeasonYears(evt, out var startYear, out var endYear)) return false;
        return Regex.IsMatch(title,
            $@"(?<![0-9]){startYear}(?:[\s._/-]+{endYear}|[-/]{endYear % 100:00})(?![0-9])",
            RegexOptions.CultureInvariant);
    }

    private static bool TryGetEHFSeasonYears(Event evt, out int startYear, out int endYear)
    {
        startYear = 0;
        endYear = 0;
        if (LeagueKey(evt.League?.Name) != "EHFChampionsLeague") return false;
        var season = Regex.Match(evt.Season ?? string.Empty,
            @"^(?<start>20[0-9]{2})[-/](?<end>20[0-9]{2})$", RegexOptions.CultureInvariant);
        return season.Success &&
            int.TryParse(season.Groups["start"].Value, out startYear) &&
            int.TryParse(season.Groups["end"].Value, out endYear) &&
            endYear == (evt.BroadcastDate ?? evt.EventDate.Date).Year;
    }

    private static bool TryMatchLibraryEpisode(string title, Event evt, out bool matches)
    {
        matches = false;
        var libraryEpisode = LibraryEpisodePattern.Match(title);
        if (!libraryEpisode.Success) return false;

        var expectedSeason = evt.SeasonNumber ??
            (int.TryParse(evt.Season, out var seasonNumber)
                ? seasonNumber
                : (evt.BroadcastDate ?? evt.EventDate.Date).Year);
        matches = int.TryParse(libraryEpisode.Groups["season"].Value, out var season) &&
            int.TryParse(libraryEpisode.Groups["episode"].Value, out var episode) &&
            evt.EpisodeNumber.HasValue &&
            season == expectedSeason &&
            episode == evt.EpisodeNumber.Value;
        return true;
    }

    private static bool IsAafRelease(string title) => Regex.IsMatch(title,
        @"^AAF[\s._-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsAcaRelease(string title) => AcaCardNumber(title).HasValue;

    private static string CatalogQueryTeamName(string value)
    {
        var name = SearchNormalizationService.RemoveDiacritics(SearchTeamName(value));
        name = Regex.Replace(name, @"\s+SC$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(name, @"[^\p{L}\p{N}]+", " ").Trim();
    }

    private static string ChampionsHockeyLeagueTeamCore(string value) => Regex.Replace(
        CatalogQueryTeamName(value),
        @"\s+(?:HC|HF|BK)$",
        string.Empty,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string CatalogEventTitle(string? value) => Regex.Replace(
        SearchNormalizationService.RemoveDiacritics(value ?? string.Empty),
        @"[^\p{L}\p{N}]+",
        " ").Trim();

    private static string AbaTeamName(string value)
    {
        var name = CatalogQueryTeamName(value);
        name = Regex.Replace(name, @"^H?KK\s+", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(name, @"\s+Basketball$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string AfconTeamName(string? value) => Regex.Replace(
        CatalogQueryTeamName(value ?? string.Empty),
        @"\s+Women$",
        string.Empty,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string AflwQueryTeamName(string value) => Regex.Replace(
        CatalogQueryTeamName(value),
        @"\s+Women$",
        string.Empty,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool HasAfconParticipants(
        string title,
        string? home,
        string? away,
        Team? homeTeam,
        Team? awayTeam)
    {
        var fixture = Regex.Match(
            Normalize(SearchNormalizationService.RemoveDiacritics(title)),
            @"^(?<left>.+?)\s+(?:vs|v)\s+(?<right>.+)$",
            RegexOptions.CultureInvariant);
        if (!fixture.Success) return false;

        var left = fixture.Groups["left"].Value;
        var right = fixture.Groups["right"].Value;
        var homeNames = AfconTeamNames(home, homeTeam);
        var awayNames = AfconTeamNames(away, awayTeam);
        return MatchesAfconSide(left, homeNames, atEnd: true) &&
               MatchesAfconSide(right, awayNames, atEnd: false) ||
            MatchesAfconSide(left, awayNames, atEnd: true) &&
               MatchesAfconSide(right, homeNames, atEnd: false);
    }

    private static string[] AfconTeamNames(string? canonical, Team? team)
    {
        var names = new List<string?> { canonical, team?.ShortName };
        foreach (var value in new[] { team?.AlternateName, team?.UserAliases })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            names.AddRange(Regex.Split(value, @"[,|;/]").Select(alias => alias.Trim()));
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .SelectMany(name => new[]
            {
                Normalize(CatalogQueryTeamName(name!)),
                Normalize(AfconTeamName(name))
            })
            .Where(name => name.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool MatchesAfconSide(string side, string[] names, bool atEnd) => names.Any(name =>
    {
        var edgePattern = atEnd
            ? $@"(?:^| ){Regex.Escape(name)}$"
            : $@"^{Regex.Escape(name)}(?: |$)";
        if (!Regex.IsMatch(side, edgePattern, RegexOptions.CultureInvariant)) return false;

        var collisionName = Regex.Replace(name, @"\s+women$", string.Empty, RegexOptions.CultureInvariant);
        return collisionName switch
        {
            "guinea" => !ContainsPhrase(side, "equatorial guinea") &&
                !ContainsPhrase(side, "guinea bissau"),
            "congo" => !ContainsPhrase(side, "dr congo") &&
                !ContainsPhrase(side, "congo dr") &&
                !ContainsPhrase(side, "democratic republic congo") &&
                !ContainsPhrase(side, "democratic republic of the congo"),
            "sudan" => !ContainsPhrase(side, "south sudan"),
            _ => true
        };
    });

    private static bool HasAmaSupercrossConflict(string title, Event evt)
    {
        var expectedYear = (evt.BroadcastDate ?? evt.EventDate.Date).Year;
        var releaseYears = Regex.Matches(title, @"(?<![0-9])20[0-9]{2}(?![0-9])")
            .Select(match => int.Parse(match.Value));
        if (!releaseYears.Contains(expectedYear)) return true;

        var eventLocation = Normalize(evt.Title ?? string.Empty);
        var libraryEpisode = LibraryEpisodePattern.Match(title);
        if (libraryEpisode.Success)
        {
            if (!int.TryParse(libraryEpisode.Groups["season"].Value, out var librarySeason) ||
                !int.TryParse(libraryEpisode.Groups["episode"].Value, out var libraryEpisodeNumber))
            {
                return true;
            }

            return eventLocation.Length == 0 ||
                !ContainsPhrase(Normalize(title), eventLocation) ||
                !evt.EpisodeNumber.HasValue ||
                librarySeason != expectedYear ||
                libraryEpisodeNumber != evt.EpisodeNumber.Value;
        }

        var releaseRound = AmaSupercrossRoundPattern.Match(title);
        if (!releaseRound.Success ||
            !int.TryParse(evt.Round, out var eventRound) ||
            int.Parse(releaseRound.Groups["round"].Value) != eventRound)
        {
            return true;
        }

        return eventLocation.Length == 0 || !ContainsPhrase(Normalize(title), eventLocation);
    }

    private static bool HasAmaSupercrossIdentity(string title, Event evt)
    {
        if (!string.Equals(ReleaseLeagueKey(title), "AMASupercross", StringComparison.Ordinal) ||
            HasAmaSupercrossConflict(title, evt))
        {
            return false;
        }

        var releaseRound = AmaSupercrossRoundPattern.Match(title);
        return releaseRound.Success &&
            int.TryParse(evt.Round, out var eventRound) &&
            int.Parse(releaseRound.Groups["round"].Value) == eventRound &&
            ContainsPhrase(Normalize(title), Normalize(evt.Title ?? string.Empty));
    }

    public static bool AllowsAdjacentDateDriftLeague(Event evt) =>
        string.Equals(evt.League?.Name, "English Rugby League Super League", StringComparison.OrdinalIgnoreCase) ||
        LeagueKey(evt.League?.Name) is
            "ConcacafWGoldCup" or "ConcacafCentralAmericanCup" or
            "ConcacafGoldCupQualifying" or "ChinaFACup" or
            "EHFChampionsLeague" or "WorldMensCurling";

    public static bool AllowsObservedDateDrift(string releaseTitle, Event evt, DateTime parsedDate)
    {
        var league = LeagueKey(evt.League?.Name);
        if (string.Equals(evt.League?.Name, "English Rugby League Super League", StringComparison.OrdinalIgnoreCase))
        {
            var fixtureDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
            var title = Normalize(releaseTitle);
            return (parsedDate.Date - fixtureDate).TotalDays == 1 &&
                (ContainsPhrase(title, "super league rugby") || ContainsPhrase(title, "rugby super league")) &&
                HasTeam(releaseTitle, evt.HomeTeamName, evt.HomeTeam) &&
                HasTeam(releaseTitle, evt.AwayTeamName, evt.AwayTeam) &&
                !HasYearConflict(releaseTitle, evt);
        }
        if (CanUseVerifiedLibertadoresUtcDate(evt) &&
            parsedDate.Date == evt.EventDate.Date)
        {
            var normalizedTitle = Normalize(SearchNormalizationService.RemoveDiacritics(releaseTitle));
            return ContainsPhrase(normalizedTitle, "copa libertadores") &&
                !SearchNormalizationService.HasParticipantCategoryConflict(releaseTitle, evt) &&
                HasTeam(releaseTitle, evt.HomeTeamName, evt.HomeTeam) &&
                HasTeam(releaseTitle, evt.AwayTeamName, evt.AwayTeam) &&
                !HasYearConflict(releaseTitle, evt);
        }
        if (league == "CopaAmerica")
        {
            var copaEventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
            var normalizedTitle = Normalize(SearchNormalizationService.RemoveDiacritics(releaseTitle));
            var namesCompetition = ContainsPhrase(normalizedTitle, "copa america");
            var namesDifferentStage = Regex.IsMatch(normalizedTitle,
                @"(?:^| )(?:semi final|semifinal|quarter final|quarterfinal|qf|sf)(?: |$)",
                RegexOptions.CultureInvariant) || ContainsPhrase(normalizedTitle, "final");
            return CanUseVerifiedCopaDateWindow(evt) &&
                (copaEventDate - parsedDate.Date).TotalDays == 1 &&
                namesCompetition &&
                (!int.TryParse(evt.Round, out var round) || round >= 200 || !namesDifferentStage) &&
                HasTeam(releaseTitle, evt.HomeTeamName, evt.HomeTeam) &&
                HasTeam(releaseTitle, evt.AwayTeamName, evt.AwayTeam) &&
                !HasYearConflict(releaseTitle, evt);
        }
        if (league == "ConcacafWGoldCup")
        {
            return ((evt.BroadcastDate ?? evt.EventDate.Date).Date - parsedDate.Date).TotalDays == 1 &&
                HasConcacafCompetitionAndParticipants(releaseTitle, evt, league) &&
                !HasYearConflict(releaseTitle, evt);
        }
        if (league is "ConcacafCentralAmericanCup" or "ConcacafGoldCupQualifying")
        {
            return Math.Abs(((evt.BroadcastDate ?? evt.EventDate.Date).Date - parsedDate.Date).TotalDays) <= 1 &&
                HasConcacafCompetitionAndParticipants(releaseTitle, evt, league) &&
                !HasYearConflict(releaseTitle, evt);
        }
        if (league == "ChinaFACup")
        {
            var chinaEventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
            return (parsedDate.Date - chinaEventDate).TotalDays == 1 &&
                HasChinaFaCupIdentity(releaseTitle, evt);
        }

        if (league is not ("EHFChampionsLeague" or "WorldMensCurling")) return false;
        var eventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
        if (Math.Abs((eventDate - parsedDate.Date).TotalDays) > 1 ||
            !HasCatalogTeamIdentity(releaseTitle, evt, league))
        {
            return false;
        }

        if (league != "WorldMensCurling") return true;
        var eventStage = CatalogTeamStage(evt, league);
        return eventStage == null || string.Equals(
            CurlingStage(releaseTitle), eventStage, StringComparison.Ordinal);
    }

    public static bool CanUseVerifiedCopaDateWindow(Event evt) =>
        LeagueKey(evt.League?.Name) == "CopaAmerica" &&
        evt.BroadcastDateVerified &&
        evt.BroadcastDate?.Date == evt.EventDate.Date &&
        evt.EventDate.TimeOfDay < TimeSpan.FromHours(4);

    public static bool CanUseVerifiedLibertadoresUtcDate(Event evt) =>
        string.Equals(evt.League?.Name, "Copa Libertadores", StringComparison.OrdinalIgnoreCase) &&
        evt.BroadcastDateVerified &&
        evt.BroadcastDate?.Date.AddDays(1) == evt.EventDate.Date &&
        evt.EventDate.TimeOfDay < TimeSpan.FromHours(4);

    private static bool IsConcacafLeague(string? league) => league is
        "ConcacafCaribbeanCup" or "ConcacafCentralAmericanCup" or
        "ConcacafChampionsCup" or "ConcacafGoldCup" or "ConcacafGoldCupQualifying" or
        "ConcacafNationsLeague" or "ConcacafSeries" or "ConcacafWChampionsCup" or
        "ConcacafWGoldCup";

    private static bool HasConcacafConflict(string title, Event evt, string league)
    {
        var releaseLeague = ConcacafReleaseLeague(title);
        if (releaseLeague != null && !string.Equals(releaseLeague, league, StringComparison.Ordinal))
            return true;
        if (ConcacafDateMatches(title, evt, league, out var hasExplicitDate) == false && hasExplicitDate)
            return true;

        return HasYearConflict(title, evt) || Regex.IsMatch(
            title,
            @"\b(?:FIBA|AmeriCup|Baseball|Softball|Volley(?:ball)?|World[\s._-]+Cup|International[\s._-]+Friendly|Copa[\s._-]+America|Beach[\s._-]+Soccer)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasConcacafIdentity(string title, Event evt, string league)
    {
        if (HasConcacafConflict(title, evt, league) ||
            !HasConcacafCompetitionAndParticipants(title, evt, league))
        {
            return false;
        }

        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
            return libraryEpisodeMatches;

        if (ConcacafDateMatches(title, evt, league, out var hasExplicitDate)) return true;
        if (hasExplicitDate) return false;

        return int.TryParse(evt.Round, out var round) && round == 200 &&
            Regex.IsMatch(title, @"\bFinal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            !Regex.IsMatch(title, @"\b(?:Semi|Quarter)[\s._-]*Final\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasConcacafCompetitionAndParticipants(string title, Event evt, string league)
    {
        if (!string.Equals(ConcacafReleaseLeague(title), league, StringComparison.Ordinal))
            return false;

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        return HasConfederationTeam(title, home, evt.HomeTeam) &&
            HasConfederationTeam(title, away, evt.AwayTeam) &&
            !HasConcacafParticipantCategoryConflict(title, evt, league);
    }

    private static string? ConcacafReleaseLeague(string title)
    {
        if (Regex.IsMatch(title, @"\bCONCACAF[\s._-]+(?:W|Women(?:'s|s)?)[\s._-]+Champions[\s._-]+Cup\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "ConcacafWChampionsCup";
        if (Regex.IsMatch(title, @"\bCONCACAF[\s._-]+(?:W|Women(?:'s|s)?)[\s._-]+Gold[\s._-]+Cup\b|\bCONCACAF[\s._-]+Gold[\s._-]+Cup\b.*\bWomen(?:'s|s)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "ConcacafWGoldCup";
        if (Regex.IsMatch(title, @"\bCONCACAF[\s._-]+Nations[\s._-]+League\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "ConcacafNationsLeague";
        if (Regex.IsMatch(title, @"\bCONCACAF[\s._-]+Series\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "ConcacafSeries";
        if (Regex.IsMatch(title, @"\bCONCACAF[\s._-]+Caribbean[\s._-]+Cup\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "ConcacafCaribbeanCup";
        if (Regex.IsMatch(title, @"\bCONCACAF[\s._-]+Central[\s._-]+American[\s._-]+Cup\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "ConcacafCentralAmericanCup";
        if (Regex.IsMatch(title, @"\bCONCACAF[\s._-]+Champions(?:hip)?[\s._-]+(?:Cup|League)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "ConcacafChampionsCup";
        if (Regex.IsMatch(title, @"\bCONCACAF[\s._-]+Gold[\s._-]+Cup[\s._-]+(?:Qualif\w*|Prelims?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "ConcacafGoldCupQualifying";
        if (Regex.IsMatch(title, @"\bCONCACAF[\s._-]+Gold[\s._-]+Cup\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "ConcacafGoldCup";
        return null;
    }

    private static bool ConcacafDateMatches(string title, Event evt, string league, out bool hasExplicitDate)
    {
        var eventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
        var releaseDates = ConcacafReleaseDates(title, eventDate.Year);
        hasExplicitDate = releaseDates.Count > 0;
        return releaseDates.Any(date =>
            date == eventDate ||
            ((league is "ConcacafCentralAmericanCup" or "ConcacafGoldCupQualifying") &&
             Math.Abs((eventDate - date).TotalDays) <= 1) ||
            (league == "ConcacafWGoldCup" && (eventDate - date).TotalDays == 1));
    }

    private static bool HasConmebolWomensNationsLeagueIdentity(string title, Event evt)
    {
        if (!Regex.IsMatch(
                title,
                @"\bCONMEBOL[\s._-]+(?:Liga[\s._-]+de[\s._-]+Naciones[\s._-]+Femenina|Women(?:'s|s)?[\s._-]+Nations[\s._-]+League)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            HasYearConflict(title, evt) ||
            SearchNormalizationService.HasParticipantCategoryConflict(title, evt))
        {
            return false;
        }

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        if (!HasConfederationTeam(title, home, evt.HomeTeam) ||
            !HasConfederationTeam(title, away, evt.AwayTeam))
        {
            return false;
        }

        var eventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
        var releaseDates = ConcacafReleaseDates(title, eventDate.Year);
        if (releaseDates.Count > 0 && !releaseDates.Any(date => date == eventDate)) return false;

        if (TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches))
            return libraryEpisodeMatches;

        return releaseDates.Any(date => date == eventDate);
    }

    private static bool HasConcacafParticipantCategoryConflict(string title, Event evt, string league)
    {
        if (!SearchNormalizationService.HasParticipantCategoryConflict(title, evt)) return false;
        if (league is not ("ConcacafWChampionsCup" or "ConcacafWGoldCup")) return true;
        return Regex.IsMatch(
            title,
            @"\b(?:U|Under)[\s._-]*(?:17|19|20|21|23)\b|\b(?:Youth|Reserves?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasConfederationTeam(string title, string? canonical, Team? team)
    {
        if (HasTeam(title, canonical, team)) return true;
        if (string.IsNullOrWhiteSpace(canonical)) return false;
        var normalizedTitle = Regex.Replace(Normalize(title), @"\bwomens\b", "women", RegexOptions.CultureInvariant);
        var normalizedTeam = Regex.Replace(Normalize(canonical), @"\bwomens\b", "women", RegexOptions.CultureInvariant);
        return ContainsPhrase(normalizedTitle, normalizedTeam);
    }

    private static IReadOnlyList<DateTime> ConcacafReleaseDates(string title, int eventYear)
    {
        var dateSource = Regex.Replace(
            title,
            @"(?<![\p{L}\p{N}])(?:AAC|AC3|EAC3|DDP?|TRUEHD|DTS(?:[\s._/+\-]*(?:HD|MA)){0,2})[\s._/+\-]*[257][\s._/+\-]*[014](?![0-9])",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var dates = new List<DateTime>();
        foreach (Match match in YearMonthDayPattern.Matches(dateSource))
        {
            AddConcacafDate(dates, match.Groups["year"].Value, match.Groups["month"].Value, match.Groups["day"].Value);
        }
        foreach (Match match in DayMonthYearPattern.Matches(dateSource))
        {
            AddConcacafDate(dates, match.Groups["year"].Value, match.Groups["month"].Value, match.Groups["day"].Value);
        }
        foreach (Match match in CompactYearMonthDayPattern.Matches(dateSource))
        {
            AddConcacafDate(dates, match.Groups["year"].Value, match.Groups["month"].Value, match.Groups["day"].Value);
        }
        foreach (Match match in ShortDayMonthYearPattern.Matches(dateSource))
        {
            var shortYear = int.Parse(match.Groups["year"].Value);
            var fullYear = shortYear <= 69 ? 2000 + shortYear : 1900 + shortYear;
            AddConcacafDate(dates, fullYear.ToString(), match.Groups["month"].Value, match.Groups["day"].Value);
        }

        if (dates.Count > 0) return dates.Distinct().ToArray();
        foreach (Match match in DayMonthPattern.Matches(dateSource))
        {
            var startsInsideToken = match.Index > 0 && char.IsLetterOrDigit(dateSource[match.Index - 1]);
            var end = match.Index + match.Length;
            var endsInsideToken = end < dateSource.Length && char.IsLetterOrDigit(dateSource[end]);
            var startsAfterAudioCodec = Regex.IsMatch(
                dateSource[..match.Index],
                @"(?:^|[^\p{L}\p{N}])(?:AAC|AC3|EAC3|DDP?|DTS(?:HD)?|TRUEHD)[\s._-]*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (startsInsideToken || endsInsideToken || startsAfterAudioCodec) continue;
            AddConcacafDate(dates, eventYear.ToString(), match.Groups["month"].Value, match.Groups["day"].Value);
        }
        return dates.Distinct().ToArray();
    }

    private static void AddConcacafDate(List<DateTime> dates, string year, string month, string day)
    {
        if (DateTime.TryParseExact(
            $"{year}-{month.PadLeft(2, '0')}-{day.PadLeft(2, '0')}",
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var date))
        {
            dates.Add(date);
        }
    }

    private static bool HasCatalogTeamConflict(string title, Event evt, string league)
    {
        var releaseLeague = ReleaseLeagueKey(title);
        if (releaseLeague != null && !string.Equals(releaseLeague, league, StringComparison.Ordinal)) return true;
        if (league == "EHFChampionsLeague")
        {
            if (!HasEHFGameDate(title, evt)) return true;
        }
        else if (HasYearConflict(title, evt)) return true;

        if (league == "WorldMensCurling")
        {
            if (IsOlympicCurlingRelease(title)) return true;
            var releaseGender = Gender(title);
            if (releaseGender is "Women" or "Mixed") return true;
            var eventStage = CatalogTeamStage(evt, league);
            var releaseStage = CurlingStage(title);
            if (eventStage != null && releaseStage != null && eventStage != releaseStage) return true;
        }

        if (league == "GAAFootball")
        {
            var eventStage = CatalogTeamStage(evt, league);
            var releaseStage = CompetitionStage(title);
            if (eventStage != null && releaseStage != null && eventStage != releaseStage) return true;
        }

        return false;
    }

    private static bool HasEHFGameDate(string title, Event evt)
    {
        var eventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
        var hasLibraryEpisode = TryMatchLibraryEpisode(title, evt, out var libraryEpisodeMatches);
        if (hasLibraryEpisode && !libraryEpisodeMatches) return false;
        var dateSource = CanadianTechnicalTokenPattern.Replace(title, " ");

        var explicitDates = CatalogExplicitDates(dateSource);
        if (explicitDates.Length > 0)
            return explicitDates.Any(date => Math.Abs((date - eventDate).TotalDays) <= 1);

        var writtenDate = SportsFileNameParser.ParseWrittenMonthDate(dateSource);
        if (writtenDate.HasValue)
            return Math.Abs((writtenDate.Value - eventDate).TotalDays) <= 1;

        if (hasLibraryEpisode)
        {
            var stamp = EHFDayMonthStampPattern.Match(dateSource);
            if (!stamp.Success) return true;
            return new[] { eventDate.AddDays(-1), eventDate, eventDate.AddDays(1) }.Any(date =>
                int.Parse(stamp.Groups["day"].Value) == date.Day &&
                int.Parse(stamp.Groups["month"].Value) == date.Month);
        }
        var year = eventDate.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!Regex.IsMatch(dateSource, $@"(?<![0-9]){year}(?![0-9])", RegexOptions.CultureInvariant) &&
            !HasMatchingEHFSeasonSpan(title, evt))
            return false;

        return HasDayMonth(dateSource, eventDate.AddDays(-1)) ||
            HasDayMonth(dateSource, eventDate) ||
            HasDayMonth(dateSource, eventDate.AddDays(1));
    }

    private static bool HasCatalogTeamIdentity(string title, Event evt, string league)
    {
        if (!string.Equals(ReleaseLeagueKey(title), league, StringComparison.Ordinal) ||
            HasCatalogTeamConflict(title, evt, league))
        {
            return false;
        }

        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        return HasCatalogTeam(title, home, league) && HasCatalogTeam(title, away, league);
    }

    private static string? CatalogTeamStage(Event evt, string league)
    {
        if (!int.TryParse(evt.Round, out var round)) return null;
        if (round == 200) return "Final";
        if (league == "WorldMensCurling" && round == 150) return "Semifinal";
        if (league == "WorldMensCurling" && round == 160) return "Qualification";
        if (league == "WorldMensCurling" && round < 100) return "RoundRobin";
        return null;
    }

    private static string? CurlingStage(string title)
    {
        if (Regex.IsMatch(title, @"\bSemi[\s._-]*Finals?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Semifinal";
        if (Regex.IsMatch(title, @"\b(?:Round[\s._-]+Robin|RR)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "RoundRobin";
        if (Regex.IsMatch(title, @"\bQualif(?:ication|ier|ying)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Qualification";
        if (Regex.IsMatch(title, @"\bFinal\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "Final";
        return null;
    }

    private static bool HasCatalogTeam(string title, string? team, string league)
    {
        if (string.IsNullOrWhiteSpace(team)) return false;
        var normalizedTitle = Normalize(SearchNormalizationService.RemoveDiacritics(title));
        var normalizedTeam = Normalize(CatalogTeamName(team, league));
        return normalizedTeam.Length > 0 && ContainsPhrase(normalizedTitle, normalizedTeam);
    }

    private static string CatalogTeamName(string value, string league)
    {
        var name = SearchNormalizationService.RemoveDiacritics(value).Trim();
        name = league switch
        {
            "GAAFootball" => Regex.Replace(name, @"\s+GAA(?:\s+Football)?$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "EHFChampionsLeague" => Regex.Replace(
                Regex.Replace(name, @"\s+Handball$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                @"^(?:SC\s+Pick|SC)\s+", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "WorldMensCurling" => Regex.Replace(name, @"\s+Curling$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => name
        };
        return Regex.Replace(name, @"\s+", " ").Trim();
    }

    private static bool IsGaaFootballRelease(string title) => Regex.IsMatch(title,
        @"\bGAA[\s._-]+Football[\s._-]+All[\s._-]+Ireland[\s._-]+Senior[\s._-]+Championship\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsEhfChampionsLeagueRelease(string title) => Regex.IsMatch(title,
        @"\bEHF[\s._-]+Champions(?:h(?:ip)?)?[\s._-]+League\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsWorldCurlingRelease(string title) => Regex.IsMatch(title,
        @"\bCurling[\s._-]+World[\s._-]+Championships?\b|\bWorld[\s._-]+(?:Mens?[\s._-]+)?Curling[\s._-]+Championships?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsOlympicCurlingRelease(string title) =>
        Regex.IsMatch(title, @"\b(?:Winter[\s._-]+Olympic|WOG)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
        Regex.IsMatch(title, @"\bCurling\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    private static bool HasTeam(string title, string? canonical, Team? team)
    {
        if (HasTeam(title, canonical)) return true;

        var aliases = new List<string>();
        if (!string.IsNullOrWhiteSpace(team?.ShortName)) aliases.Add(team.ShortName);
        foreach (var value in new[] { team?.AlternateName, team?.UserAliases })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            aliases.AddRange(Regex.Split(value, @"[,|;/]").Select(alias => alias.Trim()));
        }

        var normalizedTitle = Normalize(title);
        return aliases.Where(alias => alias.Length >= 2).Any(alias =>
            ContainsPhrase(normalizedTitle, Normalize(alias)));
    }

    private static bool ContainsPhrase(string normalizedTitle, string normalizedPhrase) =>
        Regex.IsMatch(normalizedTitle, $@"(?:^| ){Regex.Escape(normalizedPhrase)}(?: |$)", RegexOptions.CultureInvariant);

    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{M}\p{N}]+", " ").Trim();
}
