using System.Text.RegularExpressions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Builds the names that can stand for one team in a title. The terms come
/// from the event's team name, the team's short name, alternate names and
/// user aliases, the name without a known suffix, and the shared variation
/// data. Each caller gives its own normalizer, so each keeps its own text
/// rules.
/// </summary>
public static class TeamParticipantTerms
{
    private static readonly Regex ClubSuffix = new(
        @"\s+(football club|cricket club|afc|fc)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <param name="wholeWordVariations">
    /// When true, a known team name must match the event's team name as whole
    /// words. A plain substring test lets "Al Hilal" pick up names that only
    /// share "Al". Import matching keeps the substring test.
    /// </param>
    public static HashSet<string> Build(
        string canonicalName, Team? team, Func<string, string> normalize, bool wholeWordVariations = false)
    {
        var (terms, shortForms) = BuildClassified(canonicalName, team, normalize, wholeWordVariations);
        terms.UnionWith(shortForms);
        return terms;
    }

    /// <summary>
    /// The same terms, split in two. Full terms are the team name, its
    /// aliases and its known variations of more than one word. Short forms
    /// are the name with a suffix taken off, such as "Toronto" for "Toronto
    /// FC", the short name, aliases that look like codes, such as "TOR", and
    /// one-word variations, such as "Giants". A short form alone can name a
    /// club of another sport. Build joins both sets, so import matching sees
    /// the same terms as before.
    /// </summary>
    public static (HashSet<string> Full, HashSet<string> ShortForms) BuildClassified(
        string canonicalName, Team? team, Func<string, string> normalize, bool wholeWordVariations = false)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            normalize(canonicalName)
        };
        var stripped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddTerm(stripped, team?.ShortName, normalize);
        AddAliases(terms, stripped, team?.AlternateName, normalize);
        AddAliases(terms, stripped, team?.UserAliases, normalize);
        AddTerm(stripped, LeagueNameSuffixStripper.StripKnownSuffixes(canonicalName), normalize);
        AddTerm(stripped, LeagueNameSuffixStripper.StripNationalTeamSportSuffix(canonicalName, team?.Sport), normalize);
        AddTerm(stripped, ClubSuffix.Replace(canonicalName, "").Trim(), normalize);

        var normalizedCanonical = normalize(canonicalName);
        foreach (var (knownName, variations) in TeamNameVariationData.Variations)
        {
            var normalizedKnownName = normalize(knownName);
            var related = wholeWordVariations
                ? ContainsWords(normalizedCanonical, normalizedKnownName) ||
                  ContainsWords(normalizedKnownName, normalizedCanonical)
                : normalizedCanonical.Contains(normalizedKnownName, StringComparison.OrdinalIgnoreCase) ||
                  normalizedKnownName.Contains(normalizedCanonical, StringComparison.OrdinalIgnoreCase);
            if (!related)
            {
                continue;
            }

            foreach (var variation in variations)
            {
                AddTerm(variation.Trim().Contains(' ') ? terms : stripped, variation, normalize);
            }
        }

        terms.RemoveWhere(string.IsNullOrWhiteSpace);
        stripped.RemoveWhere(string.IsNullOrWhiteSpace);
        stripped.ExceptWith(terms);
        return (terms, stripped);
    }

    private static bool ContainsWords(string haystack, string needle) =>
        needle.Length > 0 &&
        $" {haystack} ".Contains($" {needle} ", StringComparison.OrdinalIgnoreCase);

    private static void AddAliases(
        HashSet<string> terms, HashSet<string> codes, string? aliases, Func<string, string> normalize)
    {
        if (string.IsNullOrWhiteSpace(aliases))
        {
            return;
        }

        foreach (var alias in aliases.Split(
                     new[] { ',', '|', '/' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddTerm(ReleaseMatchingService.IsShortCode(alias) ? codes : terms, alias, normalize);
        }
    }

    private static void AddTerm(HashSet<string> terms, string? term, Func<string, string> normalize)
    {
        if (!string.IsNullOrWhiteSpace(term))
        {
            terms.Add(normalize(term));
        }
    }
}
