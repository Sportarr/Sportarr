using System;
using System.Globalization;

namespace Jellyfin.Plugin.Sportarr
{
    /// <summary>
    /// Derives the numeric Tvdb-namespace alias for a Sportarr id, mirroring the
    /// published contract in the Sportarr repo's docs/EXTERNAL_IDS.md: league alias
    /// is 900,000,000 plus the short id number, event alias is 1,000,000,000 plus
    /// the short id number, and raw legacy numeric ids below the alias ranges pass
    /// through unchanged. The offsets are frozen forever; media servers persist the
    /// values, so this must always agree with the Sportarr server's own derivation.
    /// </summary>
    internal static class SportarrIdAlias
    {
        private const long LeagueOffset = 900_000_000;
        private const long EventOffset = 1_000_000_000;

        /// <summary>
        /// Returns the numeric alias for a Sportarr id (lg-/ev- short id or raw
        /// legacy numeric), or null when no alias is derivable.
        /// </summary>
        /// <param name="sportarrId">The stored Sportarr provider id.</param>
        /// <returns>The alias digits, or null.</returns>
        public static string? TvdbAliasFor(string? sportarrId)
        {
            if (string.IsNullOrWhiteSpace(sportarrId))
            {
                return null;
            }

            var value = sportarrId.Trim();

            if (value.StartsWith("lg-", StringComparison.OrdinalIgnoreCase))
            {
                return Offset(value.Substring(3), LeagueOffset, EventOffset - 1);
            }

            if (value.StartsWith("ev-", StringComparison.OrdinalIgnoreCase))
            {
                return Offset(value.Substring(3), EventOffset, int.MaxValue);
            }

            if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var legacy)
                && legacy > 0 && legacy < LeagueOffset)
            {
                return legacy.ToString(CultureInfo.InvariantCulture);
            }

            return null;
        }

        private static string? Offset(string digits, long offset, long ceiling)
        {
            if (digits.Length == 0 || digits.Length > 10)
            {
                return null;
            }

            if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n <= 0)
            {
                return null;
            }

            var aliased = offset + n;
            return aliased <= ceiling ? aliased.ToString(CultureInfo.InvariantCulture) : null;
        }
    }
}
