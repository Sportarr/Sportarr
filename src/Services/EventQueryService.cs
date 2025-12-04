using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Universal event query service for all sports
/// Builds search queries based on sport type, league, and teams
/// Implements Sonarr-style query building with scene naming conventions
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
    ///
    /// Scene naming conventions handled:
    /// - Dots instead of spaces: "UFC.299.Main.Card"
    /// - Various team formats: "Lakers vs Celtics", "Lakers.Celtics", "LAL.BOS"
    /// - Date formats: "2024.03.15", "2024-03-15"
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
            var homeTeam = NormalizeTeamName(evt.HomeTeam.Name);
            var awayTeam = NormalizeTeamName(evt.AwayTeam.Name);

            // Use full team names - indexers have good fuzzy matching
            // Format: "Lakers vs Celtics" (works for most sports indexers)
            queries.Add($"{homeTeam} vs {awayTeam}");

            // PRIORITY 2: Alternative format without "vs" (some release groups drop it)
            queries.Add($"{homeTeam} {awayTeam}");

            // PRIORITY 3: Scene format with dots (e.g., "Lakers.vs.Celtics")
            queries.Add($"{homeTeam.Replace(" ", ".")} vs {awayTeam.Replace(" ", ".")}");

            // PRIORITY 4: League + Teams (for disambiguation when team names are generic)
            if (evt.League != null)
            {
                var leagueName = NormalizeLeagueName(evt.League.Name);
                queries.Add($"{leagueName} {homeTeam} {awayTeam}");
            }

            // PRIORITY 5: Short names as fallback (only if different from full names)
            if (!string.IsNullOrEmpty(evt.HomeTeam.ShortName) &&
                !string.IsNullOrEmpty(evt.AwayTeam.ShortName) &&
                evt.HomeTeam.ShortName != evt.HomeTeam.Name)
            {
                queries.Add($"{evt.HomeTeam.ShortName} vs {evt.AwayTeam.ShortName}");
                queries.Add($"{evt.HomeTeam.ShortName} {evt.AwayTeam.ShortName}");
            }

            // PRIORITY 6: Date-based search (scene format: "2024.03.15")
            var dateStrDot = evt.EventDate.ToString("yyyy.MM.dd");
            var dateStrDash = evt.EventDate.ToString("yyyy-MM-dd");
            queries.Add($"{homeTeam} {awayTeam} {dateStrDot}");
            queries.Add($"{homeTeam} {awayTeam} {dateStrDash}");
        }
        else
        {
            // Non-team sport or individual event (UFC, Formula 1, etc.)
            var normalizedTitle = NormalizeEventTitle(evt.Title);

            // PRIORITY 1: Normalized event title (e.g., "UFC 295", "Monaco Grand Prix")
            queries.Add(normalizedTitle);

            // PRIORITY 2: Scene format with dots
            queries.Add(normalizedTitle.Replace(" ", "."));

            // PRIORITY 3: League + Event title for disambiguation
            if (evt.League != null)
            {
                var leagueName = NormalizeLeagueName(evt.League.Name);
                queries.Add($"{leagueName} {normalizedTitle}");
            }

            // PRIORITY 4: With year for numbered events (e.g., "UFC 299 2024")
            if (IsNumberedEvent(evt.Title))
            {
                queries.Add($"{normalizedTitle} {evt.EventDate.Year}");
            }
        }

        // Deduplicate queries (case-insensitive)
        queries = queries
            .Select(q => q.Trim())
            .Where(q => !string.IsNullOrEmpty(q))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // If searching for a specific multi-part episode segment, create part-specific queries
        // This helps find releases specifically labeled with "Early Prelims", "Prelims", or "Main Card"
        if (!string.IsNullOrEmpty(part))
        {
            _logger.LogInformation("[EventQuery] Adding multi-part segment '{Part}' variations to queries", part);

            var partVariations = GetPartVariations(part);
            var originalQueries = queries.ToList();
            var partQueries = new List<string>();

            // Add part-specific queries (highest priority for part searches)
            foreach (var query in originalQueries.Take(3)) // Only add to top 3 queries
            {
                foreach (var partVar in partVariations)
                {
                    partQueries.Add($"{query} {partVar}");
                }
            }

            // Part queries first, then original queries as fallback
            queries = partQueries.Concat(originalQueries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        _logger.LogInformation("[EventQuery] Built {Count} prioritized queries (will try in order, stopping at first results)", queries.Count);

        for (int i = 0; i < queries.Count; i++)
        {
            _logger.LogDebug("[EventQuery] Priority {Priority}: {Query}", i + 1, queries[i]);
        }

        return queries;
    }

    /// <summary>
    /// Normalize team name for search queries.
    /// Removes common suffixes and standardizes format.
    /// </summary>
    private string NormalizeTeamName(string teamName)
    {
        // Remove common suffixes that might not be in release titles
        var suffixes = new[] { " FC", " SC", " CF", " AFC", " United", " City" };
        var normalized = teamName;

        foreach (var suffix in suffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                // Only remove if team name is long enough after removal
                var withoutSuffix = normalized[..^suffix.Length];
                if (withoutSuffix.Length >= 3)
                {
                    normalized = withoutSuffix;
                }
                break;
            }
        }

        return normalized.Trim();
    }

    /// <summary>
    /// Normalize league name for search queries.
    /// Handles common abbreviations and variations.
    /// </summary>
    private string NormalizeLeagueName(string leagueName)
    {
        // Common league name mappings for searches
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Ultimate Fighting Championship", "UFC" },
            { "National Basketball Association", "NBA" },
            { "National Football League", "NFL" },
            { "National Hockey League", "NHL" },
            { "Major League Baseball", "MLB" },
            { "English Premier League", "EPL" },
            { "Premier League", "EPL" },
            { "UEFA Champions League", "UCL" },
            { "Formula 1", "F1" },
            { "Formula One", "F1" },
        };

        if (mappings.TryGetValue(leagueName, out var abbreviated))
        {
            return abbreviated;
        }

        return leagueName;
    }

    /// <summary>
    /// Normalize event title for search queries.
    /// </summary>
    private string NormalizeEventTitle(string title)
    {
        // Remove common prefixes that are redundant
        var prefixes = new[] { "UFC ", "Bellator ", "PFL ", "ONE ", "WWE ", "AEW " };

        // Keep the prefix but ensure proper format
        foreach (var prefix in prefixes)
        {
            if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return title; // Already has proper prefix
            }
        }

        return title.Trim();
    }

    /// <summary>
    /// Check if this is a numbered event (UFC 299, Bellator 300, etc.)
    /// </summary>
    private bool IsNumberedEvent(string title)
    {
        return Regex.IsMatch(title, @"(UFC|Bellator|PFL|ONE|WrestleMania)\s+\d+", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Get variations of a part name for searching.
    /// Handles scene naming conventions.
    /// </summary>
    private List<string> GetPartVariations(string part)
    {
        var variations = new List<string> { part };

        // Add common variations
        switch (part.ToLowerInvariant())
        {
            case "early prelims":
                variations.AddRange(new[] { "Early.Prelims", "EarlyPrelims", "Early Prelims" });
                break;
            case "prelims":
                variations.AddRange(new[] { "Prelims", "Preliminary", "Prelim" });
                break;
            case "main card":
                variations.AddRange(new[] { "Main.Card", "MainCard", "Main Card", "Main" });
                break;
            case "main event":
                variations.AddRange(new[] { "Main.Event", "MainEvent" });
                break;
        }

        return variations.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
