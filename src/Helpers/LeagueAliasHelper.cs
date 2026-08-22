using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Helpers;

/// <summary>
/// The single source of truth for "every name that identifies a league" - used
/// by every league-identity gate (organization scoring, TitleNamesLeague,
/// SeriesLabelMatchesLeague, grab validation, and import matching) so a
/// release found only through a user-defined alias cannot later fail
/// league-identity matching because some other code path never learned
/// about that alias.
/// </summary>
public static class LeagueAliasHelper
{
    /// <summary>
    /// "&lt;Word&gt; &lt;number&gt;" series conventionally abbreviate to first letter
    /// + number (Formula 1 -> F1). Generated so leagues whose upstream record
    /// carries no alternate names, and no user alias, still match the common form.
    /// </summary>
    private static readonly Regex AbbreviationPattern = new(@"^([A-Za-z])[A-Za-z]*\s+(\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// Every alias that identifies <paramref name="league"/>, in priority order:
    /// canonical name, upstream alternate names, user-defined aliases, then a
    /// generated abbreviation - case-insensitively deduplicated.
    /// </summary>
    public static IReadOnlyList<string> GetMatchingAliases(League league)
    {
        var aliases = new List<string> { league.Name };
        aliases.AddRange(AliasField.Parse(league.AlternateName));
        aliases.AddRange(AliasField.Parse(league.UserAliases));

        var abbrev = AbbreviationPattern.Match(league.Name ?? "");
        if (abbrev.Success)
            aliases.Add(abbrev.Groups[1].Value + abbrev.Groups[2].Value);

        return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
