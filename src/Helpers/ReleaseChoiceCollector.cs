using System.Text.RegularExpressions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Makes the release choice that an install reports when a release that the
/// user picked by hand imports. The metadata service counts installs per
/// (event, title key). Nothing in this class does I/O.
/// </summary>
public static partial class ReleaseChoiceCollector
{
    public const string Override = "override";
    public const string Low = "low", NoMatch = "nomatch", Rejected = "rejected";

    /// <summary>The longest title, in code points, that the app reports.</summary>
    public const int MaxTitleLength = 300;

    private const int MinimumKeyLength = 4;
    private const int MaxSeasonLength = 16;

    [GeneratedRegex(@"^\d+(-\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SeasonShape();

    // A bracket whose text holds a letter or digit, a dot, then two or more
    // letters names a site, such as "[eztv.re]". "[AAC2.0]" and "[H.264]" do not.
    [GeneratedRegex(@"[\[\(\{][^\]\)\}]*[a-z0-9]\.[a-z]{2,}[^\]\)\}]*[\]\)\}]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SiteInBrackets();

    // A name.tld token anywhere in the title. These endings are not common
    // words. A dot before the name is no boundary, so "F1.TV" passes.
    [GeneratedRegex(@"(?<=^|[\s\[\(\{_-])[a-z0-9][a-z0-9-]*\.(com|net|org|info|io|ru|nl|xyz|st|lol|co)(?=$|[\s\]\)\}-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DomainToken();

    // At the start or the end of a title, a site name is likely, so endings
    // that are also words, such as "to", "re" and "club", count there only.
    // A dot can also follow a leading site, as in "rarbg.to.Formula1".
    // Only endings that are not words count there, so "St.Louis" and
    // "F1.TV.2024" pass.
    [GeneratedRegex(@"^\s*[a-z0-9][a-z0-9-]*\.(?:(?:" + AllTopLevelDomains + @")(?=[\s-])|(?:" + LeadingDotTopLevelDomains + @")(?=\.))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingDomain();

    [GeneratedRegex(@"(?:^|[.\s-])(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+(" + AllTopLevelDomains + @")\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingDomain();

    private const string AllTopLevelDomains = "com|net|org|info|io|ru|nl|xyz|st|lol|co|uk|tv|to|re|club|me|se|cc|cx|si|ws|ag|eu";
    private const string LeadingDotTopLevelDomains = "com|net|org|io|ru|nl|xyz|lol|cx|si|ws|ag|eu|cc|to";

    // The hub's release choice handler accepts exactly six digits.
    [GeneratedRegex(@"^lg-[0-9]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HubLeagueShortId();

    /// <summary>
    /// The verdict to report, or null when the app agreed with the pick.
    /// A hard rejection comes first, because those votes must never be served.
    /// </summary>
    public static string? VerdictOf(int? matchScore, bool identityRejected)
    {
        if (identityRejected) return Rejected;
        if (matchScore == null) return null;
        if (matchScore < ReleaseMatchScorer.MinimumMatchScore) return NoMatch;
        if (matchScore < ReleaseMatchScorer.AutoGrabMatchScore) return Low;
        return null;
    }

    /// <summary>
    /// The kind of indexer. A type that names one tracker becomes "other",
    /// so the report never tells which tracker the user belongs to.
    /// </summary>
    public static string IndexerFamilyOf(IndexerType? type) => type switch
    {
        IndexerType.Torznab => "torznab",
        IndexerType.Newznab => "newznab",
        IndexerType.Rss => "rss",
        _ => "other",
    };

    public static string? ProtocolOf(string? protocol)
    {
        if (string.Equals(protocol, "Torrent", StringComparison.OrdinalIgnoreCase)) return "torrent";
        if (string.Equals(protocol, "Usenet", StringComparison.OrdinalIgnoreCase)) return "usenet";
        return null;
    }

    /// <summary>
    /// True when the title holds a web address anywhere. A site name in the
    /// key would split the votes and name the site. A dotted release name
    /// such as "F1.TV" is not an address.
    /// </summary>
    public static bool HasWebAddress(string title) =>
        title.Contains("://", StringComparison.Ordinal) ||
        title.Contains("www.", StringComparison.OrdinalIgnoreCase) ||
        SiteInBrackets().IsMatch(title) ||
        DomainToken().IsMatch(title) ||
        LeadingDomain().IsMatch(title) ||
        TrailingDomain().IsMatch(title);

    /// <summary>
    /// The release choice for one import, or null when it must not be reported.
    /// </summary>
    /// <param name="grab">The grab history row of the imported release.</param>
    /// <param name="isPackDownload">True when the download is a pack.</param>
    /// <param name="evt">The event, with its league loaded.</param>
    /// <param name="titleGivesYear">True when the release title names a year.</param>
    /// <param name="indexerType">The type of the indexer, or null when it is gone.</param>
    public static ReleaseChoice? FromImport(
        GrabHistory grab, bool isPackDownload, Event evt,
        bool titleGivesYear, IndexerType? indexerType)
    {
        if (grab.GrabSource != GrabSource.InteractivePick || isPackDownload)
            return null;

        var verdict = VerdictOf(grab.MatchScoreAtGrab, grab.IdentityRejectedAtGrab);
        if (verdict == null || grab.MatchScoreAtGrab is not int matchScore)
            return null;

        var eventId = evt.ExternalId;
        var leagueId = evt.League?.ExternalId;
        if (eventId == null || !FileMatchSuggestionCollector.EventShortId().IsMatch(eventId) ||
            leagueId == null || !HubLeagueShortId().IsMatch(leagueId))
            return null;

        // The hub trims the title before it makes the key.
        var title = grab.Title.Trim();
        // A yearless name is the same every year, so it cannot name one event.
        if (FileMatchSuggestionCollector.CarriesIdToken(title) || !titleGivesYear)
            return null;

        // The normalizer cuts at the last slash, so the key would lose its start.
        if (title.Contains('/') || title.Contains('\\') || HasWebAddress(title))
            return null;

        // A leading tag in brackets often names a site. The vote is lost rather than the site sent.
        if (title.StartsWith('[') || title.StartsWith('(') || title.StartsWith('{'))
            return null;

        // The hub makes the key from the full text, so the title goes uncut.
        if (title.EnumerateRunes().Count() > MaxTitleLength)
            return null;

        var titleNorm = FileStemNormalizer.Normalize(title);
        if (titleNorm.Length < MinimumKeyLength)
            return null;

        var season = evt.Season != null && evt.Season.Length <= MaxSeasonLength && SeasonShape().IsMatch(evt.Season)
            ? evt.Season
            : null;
        return new ReleaseChoice(leagueId, eventId, season, titleNorm, title, Override, verdict, matchScore,
            IndexerFamilyOf(indexerType), ProtocolOf(grab.Protocol), grab.CrowdHintShown);
    }
}
