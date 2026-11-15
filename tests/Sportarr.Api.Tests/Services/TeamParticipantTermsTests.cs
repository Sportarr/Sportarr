using System.Text.RegularExpressions;
using FluentAssertions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The team terms moved out of ImportMatchingService into a shared helper,
/// so the per-event channel matcher can use them too. Import matching must
/// build the same terms as before the move.
/// </summary>
public class TeamParticipantTermsTests
{
    // The import matcher's terms as they were built before the move.
    private static HashSet<string> TermsBeforeTheMove(string canonicalName, Team? team)
    {
        string N(string s) => ImportMatchingService.NormalizeTitle(s);
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { N(canonicalName) };
        void Add(string? term)
        {
            if (!string.IsNullOrWhiteSpace(term)) terms.Add(N(term));
        }
        void Aliases(string? aliases)
        {
            if (string.IsNullOrWhiteSpace(aliases)) return;
            foreach (var alias in aliases.Split(new[] { ',', '|', '/' },
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Add(alias);
        }

        Add(team?.ShortName);
        Aliases(team?.AlternateName);
        Aliases(team?.UserAliases);
        Add(LeagueNameSuffixStripper.StripKnownSuffixes(canonicalName));
        Add(LeagueNameSuffixStripper.StripNationalTeamSportSuffix(canonicalName, team?.Sport));
        Add(Regex.Replace(canonicalName, @"\s+(football club|cricket club|afc|fc)$", "", RegexOptions.IgnoreCase).Trim());

        var normalizedCanonical = N(canonicalName);
        foreach (var (knownName, variations) in TeamNameVariationData.Variations)
        {
            var normalizedKnownName = N(knownName);
            if (!normalizedCanonical.Contains(normalizedKnownName, StringComparison.OrdinalIgnoreCase) &&
                !normalizedKnownName.Contains(normalizedCanonical, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var variation in variations) Add(variation);
        }

        terms.RemoveWhere(string.IsNullOrWhiteSpace);
        return terms;
    }

    public static IEnumerable<object?[]> Teams()
    {
        yield return new object?[] { "Los Angeles Lakers", new Team { Name = "Los Angeles Lakers", ShortName = "LAL", Sport = "Basketball" } };
        yield return new object?[] { "Arsenal FC", new Team { Name = "Arsenal FC", AlternateName = "Arsenal, The Gunners", Sport = "Soccer" } };
        yield return new object?[] { "St. Louis City SC", new Team { Name = "St. Louis City SC", UserAliases = "STL City|St Louis/STL", Sport = "Soccer" } };
        yield return new object?[] { "England Women", new Team { Name = "England Women", Sport = "Cricket" } };
        yield return new object?[] { "Sydney Swans", null };
        yield return new object?[] { "Golden State Warriors", new Team { Name = "Golden State Warriors" } };
    }

    [Theory]
    [MemberData(nameof(Teams))]
    public void Import_matching_builds_the_same_terms_as_before(string canonicalName, Team? team)
    {
        var shared = TeamParticipantTerms.Build(canonicalName, team, ImportMatchingService.NormalizeTitle);

        shared.Should().BeEquivalentTo(TermsBeforeTheMove(canonicalName, team));
        shared.Comparer.Should().Be(StringComparer.OrdinalIgnoreCase);
    }
}
