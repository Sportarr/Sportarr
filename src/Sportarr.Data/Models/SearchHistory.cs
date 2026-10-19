namespace Sportarr.Api.Models;

/// <summary>
/// One event search and what it decided.
/// Only the newest entry for an event keeps the release list. Older entries
/// keep the counts and reasons, so the table stays small.
/// </summary>
public class SearchHistoryEntry
{
    public int Id { get; set; }

    public int EventId { get; set; }
    public Event? Event { get; set; }

    /// <summary>
    /// The part the search asked for, or null for the whole event.
    /// </summary>
    public string? Part { get; set; }

    public DateTime SearchedAt { get; set; }

    /// <summary>
    /// True when you started the search. False for scheduled and RSS searches.
    /// </summary>
    public bool IsManual { get; set; }

    /// <summary>
    /// One of the <see cref="SearchOutcome"/> values.
    /// </summary>
    public string Outcome { get; set; } = SearchOutcome.NothingPassed;

    /// <summary>
    /// The search result message, for example "Cutoff met".
    /// </summary>
    public string Message { get; set; } = "";

    public int ReleasesFound { get; set; }
    public int ReleasesPassed { get; set; }
    public string? SelectedRelease { get; set; }
    public string? QualityProfile { get; set; }

    /// <summary>
    /// JSON list of rejection reasons with their release counts.
    /// </summary>
    public string ReasonsJson { get; set; } = "[]";

    /// <summary>
    /// JSON list of the releases the search saw. Null on older entries.
    /// </summary>
    public string? ReleasesJson { get; set; }

    /// <summary>
    /// JSON list of the indexers that ran and how each one finished.
    /// </summary>
    public string IndexersJson { get; set; } = "[]";
}

public static class SearchOutcome
{
    public const string Grabbed = "grabbed";
    public const string NoResults = "noResults";
    public const string NothingPassed = "nothingPassed";
    public const string NotGrabbed = "notGrabbed";
    public const string Failed = "failed";
}
