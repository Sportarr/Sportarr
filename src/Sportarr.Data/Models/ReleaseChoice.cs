namespace Sportarr.Api.Models;

/// <summary>
/// One release that a user picked by hand in the manual search list against
/// the app's verdict, after its file imported. The metadata service counts
/// distinct installs per (event, title key).
/// </summary>
/// <param name="LeagueId">The league short id, such as "lg-000142".</param>
/// <param name="EventId">The event short id, such as "ev-0012345".</param>
/// <param name="Season">The season, such as "2024" or "2024-2025", or null.</param>
/// <param name="TitleNorm">The key that FileStemNormalizer made from TitleDisplay.</param>
/// <param name="TitleDisplay">
/// The release title as the indexer gave it. Do not cut it. The hub makes
/// the key again from this value and drops the row when the keys differ.
/// </param>
/// <param name="Kind">"override" in this phase.</param>
/// <param name="Verdict">"low", "nomatch" or "rejected".</param>
/// <param name="MatchScore">The match score at the grab, 0 to 100.</param>
/// <param name="IndexerFamily">"torznab", "newznab", "rss" or "other". Never the indexer's name.</param>
/// <param name="Protocol">"torrent" or "usenet", or null.</param>
/// <param name="HintShown">True when the search list showed a crowd answer on this release.</param>
public sealed record ReleaseChoice(
    string LeagueId, string EventId, string? Season,
    string TitleNorm, string TitleDisplay,
    string Kind, string Verdict, int MatchScore,
    string IndexerFamily, string? Protocol, bool HintShown);
