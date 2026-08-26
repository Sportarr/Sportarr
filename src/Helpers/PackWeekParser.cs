using System.Text.RegularExpressions;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Reads the single week a season pack claims to cover, if it claims one.
/// </summary>
/// <remarks>
/// A pack search validates the week against the event's own, because the
/// general validation compares numbers in the release title against numbers
/// in the event title and a team fixture carries none.
///
/// This was a single regex three times and wrong three times: a range read
/// as its first week, a resolution after a dash read as a range end, and a
/// dash before the word Week refusing the week entirely. It is plain code
/// now. The first week named is the candidate, and only the text after it
/// decides whether it is one week or the start of a range. A range end has
/// to be a bare one or two digit token that is not the start of a longer
/// number, has to be at least the start week, and must not be a count such
/// as "12 Games" or "1 of 2".
/// </remarks>
public static partial class PackWeekParser
{
    public static int? SingleWeek(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var first = WeekRegex().Match(title);
        if (!first.Success) return null;
        if (!int.TryParse(first.Groups["week"].Value, out var week)) return null;

        var tail = title.Substring(first.Index + first.Length);
        var range = RangeTailRegex().Match(tail);
        if (!range.Success) return week;
        if (!int.TryParse(range.Groups["end"].Value, out var end)) return week;

        // "Week 5 - 2 Games" and "Week 5 - 1 of 2" count games; a range
        // never runs backwards.
        if (end < week) return week;

        // "Week 5 - 12 Games" counts games too.
        var afterEnd = tail.Substring(range.Index + range.Length);
        if (CountWordRegex().IsMatch(afterEnd)) return week;

        return null;
    }

    // Explicit boundaries rather than \b: an underscore is a word character,
    // so \b never fires beside one and "NFL_2025_Week_5" found no week.
    [GeneratedRegex(@"(?<![A-Za-z0-9])Week[\s\.\-_]*(?<week>\d{1,2})(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex WeekRegex();

    // A separator, then an optional second "Week", then one or two digits
    // that end at a boundary. "1080p" carries more digits so it never reads
    // as a range end, and an ISO date starts with four. A day-first or
    // month-first date does start with two, so that shape alone is refused:
    // two digits, a separator, then more digits. Refusing any digit after
    // any separator went too far, because a range followed by a resolution
    // ("Week 1-18.1080p") is the commonest season pack name of all, and it
    // read as week one again. A decimal is refused as well: "Week 5 - 7.5GB"
    // is a size, not a range to week seven, and reading it as one hid the
    // week. Sizes are written with one to three decimals and with a dot or
    // a comma, and a resolution after the dot ends in p or i, so a
    // decimal here is one to three digits followed by none of a digit, a
    // p or an i. This does
    // read "Week 1-18.15GB" as week one; a size joined to a range end by a
    // bare dot is ambiguous, and the rejection that follows is visible in
    // the search list, while a hidden week approves a wrong-week pack in
    // silence.
    [GeneratedRegex(
        @"^[\s\.\-_–]*(?:-|–|(?<![A-Za-z0-9])(?:to|thru|through)(?![A-Za-z0-9]))[\s\.\-_]*(?:Week[\s\.\-_]*)?(?<end>\d{1,2})(?![A-Za-z0-9])(?![\-\.\/]\d{1,2}[\-\.\/]\d{2,4})(?![\.,]\d{1,3}(?![0-9pi]))",
        RegexOptions.IgnoreCase)]
    private static partial Regex RangeTailRegex();

    [GeneratedRegex(@"^[\s\.\-_]*(?:games?|of|parts?|files?)(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex CountWordRegex();
}
