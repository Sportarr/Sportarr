using Sportarr.Api.Models;

namespace Sportarr.Api.Helpers;

/// <summary>
/// The single authoritative catalog of every token a custom search query
/// template understands. Before this existed, three places each carried
/// their own idea of "the token list" - EventQueryService.BuildQueryFromTemplate
/// (19 tokens), the frontend token picker (16), and the
/// "GET /api/search/available-tokens" endpoint (12) - and they drifted out
/// of sync, so {Round:00}, {Stage:0}, and {vs} could be typed into a
/// template and honored by the builder but never offered by the UI.
///
/// This list is the one place that changes when a token is added, removed,
/// or documented differently. EventQueryService's replacement map and this
/// catalog's token set are asserted equal in
/// SearchTemplateTokensTests, so the two can never drift apart again.
/// </summary>
public static class SearchTemplateTokens
{
    public static readonly IReadOnlyList<SearchTemplateToken> All = new List<SearchTemplateToken>
    {
        new("{League}", "League name (normalized abbreviation)", "NFL, UFC, Formula1"),
        new("{Year}", "Event year (4 digits)", "2025"),
        new("{Month}", "Event month (2 digits)", "01, 12"),
        new("{Day}", "Event day (2 digits)", "01, 31"),
        new("{Round}", "Round/race number, zero-padded (default, e.g. 01, 15)", "01, 15"),
        new("{Round:00}", "Round/race number, zero-padded (same as {Round})", "01, 15"),
        new("{Round:0}", "Round/race number, no padding", "1, 15"),
        new("{Week}", "Week number (for team sports)", "1, 15"),
        new("{EventTitle}", "Full event title (raw)", "UFC 299, Super Bowl LVIII"),
        new("{EventName}", "Event title with trailing 'fighter1 vs fighter2' stripped (use for fighting cards where releases name the card, not the fighters)", "ONE Friday Fights 150 (from 'ONE Friday Fights 150 Kompetch vs Attachai')"),
        new("{Stage}", "Stage number of a stage race, no padding; empty when the title names no stage", "16"),
        new("{Stage:00}", "Stage number of a stage race, zero-padded", "16, 01"),
        new("{Stage:0}", "Stage number of a stage race, no padding (same as {Stage})", "16"),
        new("{HomeTeam}", "Home team name", "Chiefs, Lakers"),
        new("{AwayTeam}", "Away team name", "Raiders, Celtics"),
        new("{vs}", "Versus separator", "vs"),
        new("{Season}", "Season identifier", "2024-25, 2025"),
        new("{Part}", "Part being searched (Prelims, Main Card, ...); empty for whole-event searches", "Prelims, Main Card"),
        new("{EventType}", "Detected fighting event type, spaced for release-name matching; empty when the title doesn't classify", "PPV, Fight Night, Contender Series, Weekly"),
    };
}
