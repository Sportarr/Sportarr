using Fightarr.Api.Models;

namespace Fightarr.Api.Services;

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
    /// </summary>
    public List<string> BuildEventQueries(Event evt)
    {
        var sport = evt.Sport ?? "Fighting";
        var queries = new List<string>();

        _logger.LogInformation("[EventQuery] Building queries for {Title} ({Sport})", evt.Title, sport);

        // Primary query: Event title
        queries.Add(evt.Title);

        // Add league context if available (UFC, Premier League, NBA, etc.)
        if (evt.League != null)
        {
            queries.Add($"{evt.League.Name} {evt.Title}");
        }

        // Add team-based queries for team sports
        if (evt.HomeTeam != null && evt.AwayTeam != null)
        {
            // Full team names
            queries.Add($"{evt.HomeTeam.Name} vs {evt.AwayTeam.Name}");
            queries.Add($"{evt.HomeTeam.Name} {evt.AwayTeam.Name}");

            // Short names if available
            if (!string.IsNullOrEmpty(evt.HomeTeam.ShortName) && !string.IsNullOrEmpty(evt.AwayTeam.ShortName))
            {
                queries.Add($"{evt.HomeTeam.ShortName} vs {evt.AwayTeam.ShortName}");
                queries.Add($"{evt.HomeTeam.ShortName} {evt.AwayTeam.ShortName}");
            }

            // Add league context with teams
            if (evt.League != null)
            {
                queries.Add($"{evt.League.Name} {evt.HomeTeam.Name} {evt.AwayTeam.Name}");
            }
        }

        // Add season/round context if available
        if (!string.IsNullOrEmpty(evt.Season))
        {
            queries.Add($"{evt.Title} {evt.Season}");

            if (!string.IsNullOrEmpty(evt.Round))
            {
                queries.Add($"{evt.Title} {evt.Season} {evt.Round}");
            }
        }

        // Add date for specificity
        var dateStr = evt.EventDate.ToString("yyyy-MM-dd");
        queries.Add($"{evt.Title} {dateStr}");

        // Deduplicate queries
        queries = queries.Distinct().ToList();

        _logger.LogInformation("[EventQuery] Built {Count} query variations", queries.Count);

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
