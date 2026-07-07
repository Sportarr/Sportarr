using System.Text.RegularExpressions;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Revision rank of a release title: 2 for REPACK/RERIP, 1 for PROPER or
/// fully uppercase REAL (scene convention - "Real" in mixed case is a team
/// name, not a revision marker), 0 for none. A higher revision at the SAME
/// quality is a legitimate upgrade: the original upload was broken and the
/// group re-released it fixed.
/// </summary>
public static class ReleaseRevision
{
    private static readonly Regex RepackRegex = new(@"\b(REPACK|RERIP)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ProperRegex = new(@"\bPROPER\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RealRegex = new(@"\bREAL\b", RegexOptions.Compiled);

    public static int Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return 0;
        if (RepackRegex.IsMatch(title))
            return 2;
        if (ProperRegex.IsMatch(title) || RealRegex.IsMatch(title))
            return 1;
        return 0;
    }
}
