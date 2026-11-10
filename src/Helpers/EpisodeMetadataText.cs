using Sportarr.Api.Models;

namespace Sportarr.Api.Helpers;

/// <summary>
/// The episode summary the metadata agent API and the Kodi NFO writer hand
/// to media servers.
///
/// The hub sync fills Event.Description with a source write-up, and for a
/// finished event that write-up usually names the winner. Media servers show
/// the summary next to an unwatched event, so the summary is always the
/// spoiler-free line and never the write-up. The stored hub data stays
/// intact.
///
/// The line must match the hub's compose_summary in
/// app/services/episode_overview.py exactly. Both sides test the same
/// table in spoiler_free_summary_cases.json, so change them together.
/// </summary>
public static class EpisodeMetadataText
{
    private const string Separator = " · ";

    public static string? Summary(Event evt, League? league) =>
        SpoilerFreeLine(evt.HomeTeamName, evt.AwayTeamName, evt.Title, league?.Name ?? evt.League?.Name,
            evt.Season, evt.Round, evt.Venue);

    /// <summary>
    /// "{Home} vs {Away}" or "{Title}", then "{League} {Season}", "Round {N}"
    /// and "{Venue}". Parts the event does not have are left out. Null when
    /// no part is left.
    /// </summary>
    public static string? SpoilerFreeLine(
        string? homeTeam,
        string? awayTeam,
        string? eventTitle,
        string? leagueName,
        string? seasonName,
        string? round,
        string? venueName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(homeTeam) && !string.IsNullOrEmpty(awayTeam))
            parts.Add($"{homeTeam} vs {awayTeam}");
        else if (!string.IsNullOrEmpty(eventTitle))
            parts.Add(eventTitle);

        var competition = string.Join(" ", new[] { leagueName, seasonName }.Where(p => !string.IsNullOrEmpty(p)));
        if (competition.Length > 0)
            parts.Add(competition);

        // The hub reports "0" for events that are not part of a round.
        var roundText = round?.Trim() ?? string.Empty;
        if (roundText.Length > 0 && roundText != "0")
            parts.Add($"Round {roundText}");

        if (!string.IsNullOrEmpty(venueName))
            parts.Add(venueName);

        return parts.Count > 0 ? string.Join(Separator, parts) : null;
    }
}
