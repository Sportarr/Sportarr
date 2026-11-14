namespace Sportarr.Api.Models;

/// <summary>
/// One file stem that a user tied to an event. The metadata service counts
/// distinct installs per (event, stem). An approved count becomes shared
/// match memory.
/// </summary>
/// <param name="LeagueId">The league short id, such as "lg-000123".</param>
/// <param name="EventId">The event short id, such as "ev-000456".</param>
/// <param name="StemNorm">The key that FileStemNormalizer made from StemDisplay.</param>
/// <param name="StemDisplay">
/// The exact stem that the app normalized. This is the file name without
/// the directory and the extension. Do not cut it first. The hub makes the
/// key again from this value and drops the row when the keys differ.
/// </param>
/// <param name="Season">The season, such as "2024" or "2024-2025", or null.</param>
/// <param name="Kind">"manual" when the user picked an event other than the scan's suggestion. "confirmed" when the user kept the suggestion.</param>
public sealed record FileMatchSuggestion(string LeagueId, string EventId, string StemNorm, string StemDisplay, string? Season, string Kind);

/// <summary>
/// One approved match that the metadata service holds for a stem.
/// </summary>
public sealed record FileMatchHit(string EventId, string LeagueId, int Installs);
