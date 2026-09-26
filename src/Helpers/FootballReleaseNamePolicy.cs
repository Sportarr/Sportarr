using System.Text.RegularExpressions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

internal static class FootballReleaseNamePolicy
{
    private static readonly Regex WomensTeamSuffix = new(
        @"\s+(?:WFC|FC\s+Women|Women)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ChampionshipTeamSuffix = new(
        @"\s+Wanderers$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UnitedCupIdentity = new(
        @"(?<![\p{L}\p{N}])United[\s\.\-_]+Cup(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static bool IsEnglishChampionship(string? leagueName)
        => leagueName?.Trim().Equals("English League Championship", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsEnglishWomensSuperLeague(string? leagueName)
        => leagueName?.Trim().Equals("English Womens Super League", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsCopaAmericaFemenina(string? leagueName)
        => leagueName?.Trim().Equals("Copa America Femenina", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsFaCup(string? leagueName)
        => leagueName?.Trim().Equals("FA Cup", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsEuropaLeague(string? leagueName)
        => leagueName?.Trim().Equals("UEFA Europa League", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsFifaWorldCup(string? leagueName)
        => leagueName?.Trim().Equals("FIFA World Cup", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsFifaWomensWorldCup(string? leagueName)
        => leagueName?.Trim().Equals("FIFA Womens World Cup", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsUefaWomensEuro(string? leagueName)
        => leagueName?.Trim().Equals("UEFA Womens Euro", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsNwslChallengeCup(string? leagueName)
        => leagueName?.Trim().Equals("American NWSL Challenge Cup", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool NamesDifferentCompetition(string releaseTitle, string? leagueName)
        => IsFifaWorldCup(leagueName) && UnitedCupIdentity.IsMatch(releaseTitle);

    internal static bool HasNwslParticipantConflict(string releaseTitle, Event evt)
    {
        if (!IsNwslChallengeCup(evt.League?.Name)) return false;

        var titleTeams = Regex.Split(evt.Title ?? string.Empty, @"\s+vs\.?\s+", RegexOptions.IgnoreCase)
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        var (home, away) = EventQueryService.ResolveTeamNames(evt);
        var (titleHome, titleAway) = ResolveTitleParticipantAliases(evt, titleTeams, home, away);
        var homeMatches = MatchesParticipant(releaseTitle, home, titleHome, evt.HomeTeam);
        var awayMatches = MatchesParticipant(releaseTitle, away, titleAway, evt.AwayTeam);
        return !homeMatches || !awayMatches;
    }

    internal static string BaseParticipantName(string teamName, string? leagueName)
    {
        var trimmed = teamName.Trim();
        if (IsEnglishWomensSuperLeague(leagueName) || IsCopaAmericaFemenina(leagueName) ||
            IsFifaWomensWorldCup(leagueName) || IsUefaWomensEuro(leagueName))
            return WomensTeamSuffix.Replace(trimmed, "").Trim();
        if (IsEnglishChampionship(leagueName))
            return ChampionshipTeamSuffix.Replace(trimmed, "").Trim();
        return trimmed;
    }

    internal static IEnumerable<string> LeagueAliases(League league)
    {
        if (IsEnglishChampionship(league.Name))
        {
            yield return "EFL Championship";
            yield return "English Championship";
        }
        else if (IsEnglishWomensSuperLeague(league.Name))
        {
            yield return "WSL";
            yield return "BWSL";
            yield return "FA WSL";
            yield return "Barclays WSL";
            yield return "England Womens Super League";
            yield return "Women's Super League";
        }
        else if (IsCopaAmericaFemenina(league.Name))
        {
            yield return "Women's Copa America";
        }
        else if (IsFifaWomensWorldCup(league.Name))
        {
            yield return "FIFA Women's World Cup";
            yield return "Womens World Cup";
            yield return "Women's World Cup";
        }
        else if (IsUefaWomensEuro(league.Name))
        {
            yield return "UEFA Women's Euro";
            yield return "Womens UEFA Euro";
            yield return "Women's UEFA Euro";
            yield return "Womens Euro";
            yield return "Women's Euro";
        }
        else if (IsEuropaLeague(league.Name))
        {
            yield return "UEL";
        }
        else if (IsFifaWorldCup(league.Name))
        {
            yield return "World Cup";
            yield return "FIFA WC";
        }
    }

    private static bool MatchesParticipant(string releaseTitle, string? canonical, string? titleName, Team? team)
    {
        foreach (var name in new[] { canonical, titleName, team?.ShortName })
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var normalizedTitle = NormalizeParticipantIdentity(releaseTitle);
            var normalizedName = NormalizeParticipantIdentity(name);
            if (Regex.IsMatch(
                    normalizedTitle,
                    $@"(?:^|\s){Regex.Escape(normalizedName)}(?:\s|$)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }

            if (normalizedName.EndsWith(" fc", StringComparison.Ordinal) &&
                Regex.IsMatch(
                    normalizedTitle,
                    $@"(?:^|\s){Regex.Escape(normalizedName[..^3])}(?:\s|$)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        foreach (var aliases in new[] { team?.AlternateName, team?.UserAliases })
        {
            if (string.IsNullOrWhiteSpace(aliases)) continue;
            foreach (var alias in Regex.Split(aliases, @"[,|;/]").Select(value => value.Trim()))
            {
                if (!string.IsNullOrWhiteSpace(alias) && MatchesParticipant(releaseTitle, alias, null, null))
                    return true;
            }
        }

        return false;
    }

    private static (string? Home, string? Away) ResolveTitleParticipantAliases(
        Event evt,
        string[] titleTeams,
        string? home,
        string? away)
    {
        if (titleTeams.Length != 2) return (null, null);

        var alignedEvidence =
            (MatchesParticipant(titleTeams[0], home, null, evt.HomeTeam) ? 1 : 0) +
            (MatchesParticipant(titleTeams[1], away, null, evt.AwayTeam) ? 1 : 0);
        var reversedEvidence =
            (MatchesParticipant(titleTeams[0], away, null, evt.AwayTeam) ? 1 : 0) +
            (MatchesParticipant(titleTeams[1], home, null, evt.HomeTeam) ? 1 : 0);

        if (alignedEvidence == reversedEvidence) return (null, null);
        return alignedEvidence > reversedEvidence
            ? (titleTeams[0], titleTeams[1])
            : (titleTeams[1], titleTeams[0]);
    }

    private static string NormalizeParticipantIdentity(string value) => Regex.Replace(
        SearchNormalizationService.RemoveDiacritics(value).ToLowerInvariant(),
        @"[^\p{L}\p{N}]+",
        " ").Trim();
}
