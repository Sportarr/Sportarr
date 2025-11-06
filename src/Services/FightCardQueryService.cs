using Fightarr.Api.Models;

namespace Fightarr.Api.Services;

/// <summary>
/// Service for building fight card queries and detecting card types from release names
/// Matches scene naming conventions for MMA events
/// </summary>
public class FightCardQueryService
{
    private readonly ILogger<FightCardQueryService> _logger;

    public FightCardQueryService(ILogger<FightCardQueryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Build search queries for specific fight card types
    /// Matches actual scene naming: "UFC 320 Prelims", "UFC 320 PPV", etc.
    /// </summary>
    public List<(FightCardType cardType, string query)> BuildCardTypeQueries(Event evt, List<FightCard> monitoredCards)
    {
        var queries = new List<(FightCardType, string)>();

        foreach (var card in monitoredCards)
        {
            var query = BuildQueryForCardType(evt, card.CardType);
            if (!string.IsNullOrEmpty(query))
            {
                queries.Add((card.CardType, query));
                _logger.LogInformation("[Card Query] {CardType}: '{Query}'", card.CardType, query);
            }
        }

        return queries;
    }

    /// <summary>
    /// Build search query for a specific card type
    /// Based on scene naming patterns from actual releases
    /// </summary>
    private string BuildQueryForCardType(Event evt, FightCardType cardType)
    {
        return cardType switch
        {
            FightCardType.EarlyPrelims => $"{evt.Title} Early Prelims",
            FightCardType.Prelims => $"{evt.Title} Prelims",
            FightCardType.MainCard => IsPPVEvent(evt) ? $"{evt.Title} PPV" : evt.Title,
            FightCardType.FullEvent => evt.Title, // Search for full event releases
            _ => evt.Title
        };
    }

    /// <summary>
    /// Detect fight card type from release name
    /// Matches scene naming patterns: "UFC.320.Prelims...", "UFC.321.PPV...", etc.
    /// </summary>
    public FightCardType DetectCardType(string releaseName)
    {
        var lower = releaseName.ToLower();

        // Check for early prelims (must check before "prelim" to avoid false match)
        if (lower.Contains("early prelim") || lower.Contains("early.prelim"))
        {
            return FightCardType.EarlyPrelims;
        }

        // Check for prelims (preliminary or prelims)
        if (lower.Contains("prelim"))
        {
            return FightCardType.Prelims;
        }

        // Check for PPV or Main Card keywords
        if (lower.Contains("ppv") || lower.Contains("main card") || lower.Contains("main.card"))
        {
            return FightCardType.MainCard;
        }

        // Check if it looks like a full event (contains "full" or "complete")
        if (lower.Contains("full") || lower.Contains("complete"))
        {
            return FightCardType.FullEvent;
        }

        // Default: assume main card if no specific indicator
        // Most releases without a card type keyword are main card
        return FightCardType.MainCard;
    }

    /// <summary>
    /// Determine if this is a PPV event based on organization and title
    /// </summary>
    private bool IsPPVEvent(Event evt)
    {
        // UFC numbered events are PPV (e.g., UFC 320, UFC 321)
        if (evt.Organization.Equals("UFC", StringComparison.OrdinalIgnoreCase))
        {
            // Check if title is "UFC [number]" pattern
            var titleParts = evt.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (titleParts.Length >= 2 &&
                titleParts[0].Equals("UFC", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(titleParts[1], out _))
            {
                return true; // UFC numbered event = PPV
            }
        }

        // Bellator numbered events are typically PPV
        if (evt.Organization.Equals("Bellator", StringComparison.OrdinalIgnoreCase))
        {
            var titleParts = evt.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (titleParts.Length >= 2 &&
                titleParts[0].Equals("Bellator", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(titleParts[1], out _))
            {
                return true;
            }
        }

        // PFL events with "Championship" are PPV
        if (evt.Organization.Equals("PFL", StringComparison.OrdinalIgnoreCase) &&
            evt.Title.Contains("Championship", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Default: not PPV (Fight Nights, regional events, etc.)
        return false;
    }

    /// <summary>
    /// Get display name for card type
    /// </summary>
    public string GetCardTypeDisplayName(FightCardType cardType)
    {
        return cardType switch
        {
            FightCardType.EarlyPrelims => "Early Prelims",
            FightCardType.Prelims => "Prelims",
            FightCardType.MainCard => "Main Card",
            FightCardType.FullEvent => "Full Event",
            _ => "Unknown"
        };
    }
}
