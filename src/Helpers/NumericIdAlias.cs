using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Derives the numeric id alias for a league or event from its canonical
/// ExternalId, and reverses the derivation for lookups.
///
/// The Sonarr v3 compat surface (and the Plex Guid array emitted by the
/// sportarr.net metadata provider) must carry integers because the arr
/// ecosystem's external-id fields are ints. Canonical ids are short ids
/// (lg-000142, ev-848683), so each entity type gets a reserved integer
/// range: the alias is the short id's numeric part plus a per-type offset.
/// Rows that predate the short-id flip still hold a raw numeric
/// TheSportsDB id; those pass through unchanged (they sit far below the
/// offset ranges) until the sync migration rewrites them.
///
/// The offsets are FROZEN forever. Plex libraries and downstream tools
/// persist these aliases; changing an offset would orphan every stored
/// mapping. See docs/EXTERNAL_IDS.md for the published contract.
/// </summary>
public static class NumericIdAlias
{
    public const int LeagueOffset = 900_000_000;
    public const int EventOffset = 1_000_000_000;

    /// <summary>
    /// Numeric alias for a canonical ExternalId, or 0 when none is
    /// derivable (null/blank, malformed, or out of range). Raw numeric
    /// legacy ids are returned as-is only when they sit below the alias
    /// ranges, keeping the three id spaces disjoint.
    /// </summary>
    public static int FromExternalId(string? externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            return 0;

        var value = externalId.Trim();

        if (value.StartsWith("lg-", StringComparison.OrdinalIgnoreCase))
            return Offset(value.Substring(3), LeagueOffset, EventOffset - 1);

        if (value.StartsWith("ev-", StringComparison.OrdinalIgnoreCase))
            return Offset(value.Substring(3), EventOffset, int.MaxValue);

        // Legacy pre-flip rows: ExternalId is still the raw TheSportsDB
        // numeric id. Real TSDB ids live in the low millions, far below
        // LeagueOffset; anything at or above it can't be disambiguated
        // from an alias and is rejected.
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var legacy)
            && legacy > 0 && legacy < LeagueOffset)
            return legacy;

        return 0;
    }

    /// <summary>
    /// ExternalId values a league row may hold for a given numeric alias,
    /// for `?tvdbId=` style lookups. Alias-range values reverse to the
    /// short id form; below-range values are legacy TheSportsDB ids and
    /// match verbatim. Event-range and non-positive values yield nothing.
    /// </summary>
    public static IReadOnlyList<string> LeagueExternalIdCandidates(int numericId)
    {
        if (numericId >= LeagueOffset && numericId < EventOffset)
        {
            // Short ids are zero-padded to six digits and grow naturally
            // beyond that, which is exactly what D6 produces.
            var n = numericId - LeagueOffset;
            return new[] { "lg-" + n.ToString("D6", CultureInfo.InvariantCulture) };
        }

        if (numericId > 0 && numericId < LeagueOffset)
            return new[] { numericId.ToString(CultureInfo.InvariantCulture) };

        return Array.Empty<string>();
    }

    private static int Offset(string digits, int offset, int ceiling)
    {
        if (digits.Length == 0 || digits.Length > 10)
            return 0;

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n <= 0)
            return 0;

        var aliased = offset + n;
        return aliased <= ceiling ? (int)aliased : 0;
    }
}
