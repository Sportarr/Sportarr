using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Universal event query service for all sports
/// Builds search queries based on sport type, league, and teams
/// </summary>
public class EventQueryService
{
    private readonly ILogger<EventQueryService> _logger;

    public EventQueryService(ILogger<EventQueryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Build search queries for an event based on its sport type and data
    /// Universal approach - works for UFC, Premier League, NBA, etc.
    ///
    /// OPTIMIZATION: Returns queries in priority order (most specific first)
    /// Sonarr/Radarr-style: Use primary query first, fallback queries only if needed
    /// </summary>
    /// <param name="evt">The event to build queries for</param>
    /// <param name="part">Optional multi-part episode segment (e.g., "Early Prelims", "Prelims", "Main Card")</param>
    public List<string> BuildEventQueries(Event evt, string? part = null)
    {
        var sport = evt.Sport ?? "Fighting";
        var queries = new List<string>();

        _logger.LogInformation("[EventQuery] Building optimized queries for {Title} ({Sport}){Part}",
            evt.Title, sport, part != null ? $" - {part}" : "");

        // PRIORITY 1: Most specific query - team names (most common for sports releases)
        // This covers 80%+ of releases on sports indexers
        if (evt.HomeTeam != null && evt.AwayTeam != null)
        {
            // Use full team names - indexers have good fuzzy matching
            // Format: "Lakers vs Celtics" (works for most sports indexers)
            queries.Add($"{evt.HomeTeam.Name} vs {evt.AwayTeam.Name}");

            // PRIORITY 2: Alternative format without "vs" (some release groups drop it)
            queries.Add($"{evt.HomeTeam.Name} {evt.AwayTeam.Name}");

            // PRIORITY 3: League + Teams (for disambiguation when team names are generic)
            if (evt.League != null)
            {
                queries.Add($"{evt.League.Name} {evt.HomeTeam.Name} {evt.AwayTeam.Name}");
            }

            // PRIORITY 4: Short names as fallback (only if different from full names)
            if (!string.IsNullOrEmpty(evt.HomeTeam.ShortName) &&
                !string.IsNullOrEmpty(evt.AwayTeam.ShortName) &&
                evt.HomeTeam.ShortName != evt.HomeTeam.Name)
            {
                queries.Add($"{evt.HomeTeam.ShortName} vs {evt.AwayTeam.ShortName}");
            }
        }
        else
        {
            // Non-team sport or individual event (UFC, Formula 1, etc.)
            // PRIORITY 1: Event title (e.g., "UFC 295", "Monaco Grand Prix")
            queries.Add(evt.Title);

            // PRIORITY 2: League + Event title for disambiguation
            if (evt.League != null)
            {
                queries.Add($"{evt.League.Name} {evt.Title}");
            }
        }

        // PRIORITY 5: Add date as last fallback for very specific searches
        // Only if we have teams (date alone for non-team events is too broad)
        if (evt.HomeTeam != null && evt.AwayTeam != null)
        {
            var dateStr = evt.EventDate.ToString("yyyy-MM-dd");
            queries.Add($"{evt.HomeTeam.Name} {evt.AwayTeam.Name} {dateStr}");
        }

        // Deduplicate queries (in case team names match titles, etc.)
        queries = queries.Distinct().ToList();

        // If searching for a specific multi-part episode segment, append the part keyword to all queries
        // This helps find releases specifically labeled with "Early Prelims", "Prelims", or "Main Card"
        if (!string.IsNullOrEmpty(part))
        {
            _logger.LogInformation("[EventQuery] Appending multi-part segment '{Part}' to all queries", part);
            queries = queries.Select(q => $"{q} {part}").ToList();
        }

        _logger.LogInformation("[EventQuery] Built {Count} prioritized queries (will try in order, stopping at first results)", queries.Count);

        for (int i = 0; i < queries.Count; i++)
        {
            _logger.LogDebug("[EventQuery] Priority {Priority}: {Query}", i + 1, queries[i]);
        }

        return queries;
    }

    /// <summary>
    /// Detect content type from release name (universal - works for all sports)
    /// Examples: "Highlights" vs "Full Game" for team sports, "Full Event" for combat sports
    /// </summary>
    public string DetectContentType(Event evt, string releaseName)
    {
        var lower = releaseName.ToLower();

        // Universal content detection
        if (lower.Contains("highlight") || lower.Contains("extended highlight"))
        {
            return "Highlights";
        }

        if (lower.Contains("condensed") || lower.Contains("recap"))
        {
            return "Condensed";
        }

        if (lower.Contains("full") || lower.Contains("complete"))
        {
            return "Full Event";
        }

        // Default: assume full event
        return "Full Event";
    }
}
