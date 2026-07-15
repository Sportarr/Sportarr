using System.Text.RegularExpressions;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Extracts Sportarr id tokens from release names and filenames per the
/// release naming standard (docs/RELEASE_NAMING.md). The canonical form is
/// "{sportarr-ev-2336155}", with lenient acceptance of the variants real
/// pipelines produce: braces optional when the sportarr prefix is present,
/// separators as dash/dot/underscore/space, any casing. The bare short form
/// ("ev-2336155" with no braces or brand) is also accepted, but under
/// stricter rules since it has no disambiguator: the separator is required
/// (no space), and the id must be 6+ digits. Sportarr ids are always
/// zero-padded to at least 6 digits, while years and other numbers that
/// appear organically next to short letter runs are 4, so the strict form
/// cannot collide with real title text.
/// </summary>
public static partial class SportarrIdToken
{
    // Braced: {sportarr-ev-2336155}, {ev-2336155}, {SPORTARR EV 2336155}, {lg.000123}
    [GeneratedRegex(@"\{\s*(?:sportarr[-._ ]*)?(?<prefix>ev|lg)[-._ ]*(?<digits>\d{4,10})\s*\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BracedToken();

    // Unbraced but explicitly branded: sportarr-ev-2336155, sportarr.ev.2336155
    [GeneratedRegex(@"(?<![a-z0-9])sportarr[-._ ]+(?<prefix>ev|lg)[-._ ]*(?<digits>\d{4,10})(?![0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrandedToken();

    // Bare short form: ev-2338110, ev.2338110, lg_000123. No brace or brand
    // to disambiguate, so the rules tighten: exactly one -/./_ separator
    // (never a space) and 6-10 digits. "ev-2026" (a year) and glued text
    // like "ev2338110" stay unmatched by construction.
    [GeneratedRegex(@"(?<![a-z0-9])(?<prefix>ev|lg)[-._](?<digits>\d{6,10})(?![0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareToken();

    // Whole-value form for structured carriers (embedded mkv tag values,
    // torznab/newznab attrs, upload-form fields): the id alone, tolerating
    // brace wrapping, the sportarr brand, casing, and separator drift from
    // sloppy producers. Anchored, so a value carrying anything else is
    // rejected rather than fished from.
    [GeneratedRegex(@"^\{?\s*(?:sportarr[-._ ]*)?(?<prefix>ev|lg)[-._ ]?(?<digits>\d{4,10})\s*\}?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ValueForm();

    /// <summary>
    /// Normalize a structured field value (mkv SPORTARR tag, torznab
    /// "sportarrid" attr) to the canonical id form ("ev-2336155" /
    /// "lg-000123"). Returns null when the value is not a sportarr id.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var m = ValueForm().Match(value.Trim());
        if (!m.Success)
            return null;

        return $"{m.Groups["prefix"].Value.ToLowerInvariant()}-{m.Groups["digits"].Value}";
    }

    /// <summary>
    /// Extract the event id token ("ev-2336155") from a release name or
    /// filename, normalized to the canonical dash form the API uses.
    /// Returns null when no event token is present.
    /// </summary>
    public static string? ExtractEventId(string? name) => Extract(name, "ev");

    /// <summary>
    /// Extract the league id token ("lg-000123"), used by pack releases.
    /// </summary>
    public static string? ExtractLeagueId(string? name) => Extract(name, "lg");

    /// <summary>
    /// Remove every id token from a name so downstream parsing (dates,
    /// years, rounds) never chews on token digits.
    /// </summary>
    public static string Strip(string name)
    {
        var stripped = BracedToken().Replace(name, " ");
        stripped = BrandedToken().Replace(stripped, " ");
        stripped = BareToken().Replace(stripped, " ");
        return stripped;
    }

    private static string? Extract(string? name, string wantedPrefix)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var regex in new[] { BracedToken(), BrandedToken(), BareToken() })
        {
            foreach (Match m in regex.Matches(name))
            {
                if (m.Groups["prefix"].Value.Equals(wantedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return $"{wantedPrefix}-{m.Groups["digits"].Value}";
                }
            }
        }

        return null;
    }
}
