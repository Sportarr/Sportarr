namespace Fightarr.Api.Services;

using Fightarr.Api.Models;
using Fightarr.Api.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Service for managing fight cards within events
/// Similar to how Sonarr manages episodes within seasons
/// </summary>
public class FightCardService
{
    private readonly FightarrDbContext _db;
    private readonly ILogger<FightCardService> _logger;

    public FightCardService(FightarrDbContext db, ILogger<FightCardService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Ensures an event has the standard fight cards created
    /// (Early Prelims, Prelims, Main Card)
    /// </summary>
    /// <param name="eventId">The event ID</param>
    /// <param name="monitoredCardTypes">Optional list of card type IDs to monitor (1=EarlyPrelims, 2=Prelims, 3=MainCard, 4=FullEvent). If null, all cards inherit event's monitored status.</param>
    public async Task EnsureFightCardsExistAsync(int eventId, List<int>? monitoredCardTypes = null)
    {
        var existingCards = await _db.FightCards
            .Where(fc => fc.EventId == eventId)
            .ToListAsync();

        // If fight cards already exist, don't create duplicates
        if (existingCards.Any())
        {
            _logger.LogDebug("Event {EventId} already has {Count} fight cards", eventId, existingCards.Count);
            return;
        }

        var evt = await _db.Events.FindAsync(eventId);
        if (evt == null)
        {
            _logger.LogWarning("Cannot create fight cards for non-existent event {EventId}", eventId);
            return;
        }

        _logger.LogInformation("Creating default fight cards for event {EventId}: {EventTitle}", eventId, evt.Title);

        // Helper function to determine if a card type should be monitored
        bool ShouldMonitor(FightCardType cardType)
        {
            // If no specific card types provided, use event's monitored status for all cards
            if (monitoredCardTypes == null || !monitoredCardTypes.Any())
                return evt.Monitored;

            // Check if this card type is in the monitored list
            return monitoredCardTypes.Contains((int)cardType);
        }

        // Create the standard three fight cards (+ optional full event card)
        var fightCards = new List<FightCard>
        {
            new FightCard
            {
                EventId = eventId,
                CardType = FightCardType.EarlyPrelims,
                CardNumber = 1,
                Monitored = ShouldMonitor(FightCardType.EarlyPrelims),
                HasFile = false,
                AirDate = evt.EventDate.AddHours(-3) // Typically 3 hours before main event
            },
            new FightCard
            {
                EventId = eventId,
                CardType = FightCardType.Prelims,
                CardNumber = 2,
                Monitored = ShouldMonitor(FightCardType.Prelims),
                HasFile = false,
                AirDate = evt.EventDate.AddHours(-1.5) // Typically 1.5 hours before main event
            },
            new FightCard
            {
                EventId = eventId,
                CardType = FightCardType.MainCard,
                CardNumber = 3,
                Monitored = ShouldMonitor(FightCardType.MainCard),
                HasFile = false,
                AirDate = evt.EventDate
            }
        };

        // Optionally add Full Event card if user requested it
        if (monitoredCardTypes != null && monitoredCardTypes.Contains((int)FightCardType.FullEvent))
        {
            fightCards.Add(new FightCard
            {
                EventId = eventId,
                CardType = FightCardType.FullEvent,
                CardNumber = 4,
                Monitored = true,
                HasFile = false,
                AirDate = evt.EventDate.AddHours(-3) // Same as early prelims
            });
        }

        _db.FightCards.AddRange(fightCards);
        await _db.SaveChangesAsync();

        var monitoredCount = fightCards.Count(fc => fc.Monitored);
        _logger.LogInformation("Created {Count} fight cards for event {EventId} ({Monitored} monitored)",
            fightCards.Count, eventId, monitoredCount);
    }

    /// <summary>
    /// Bulk creates fight cards for multiple events
    /// Useful for backfilling existing events
    /// </summary>
    public async Task EnsureFightCardsExistForAllEventsAsync()
    {
        var eventsWithoutCards = await _db.Events
            .Where(e => !_db.FightCards.Any(fc => fc.EventId == e.Id))
            .ToListAsync();

        if (!eventsWithoutCards.Any())
        {
            _logger.LogInformation("All events already have fight cards");
            return;
        }

        _logger.LogInformation("Creating fight cards for {Count} events without them", eventsWithoutCards.Count);

        foreach (var evt in eventsWithoutCards)
        {
            await EnsureFightCardsExistAsync(evt.Id);
        }

        _logger.LogInformation("Completed creating fight cards for all events");
    }

    /// <summary>
    /// Updates the monitored status of all fight cards for an event
    /// Used when the event's monitored status changes
    /// </summary>
    public async Task UpdateFightCardMonitoringAsync(int eventId, bool monitored)
    {
        var fightCards = await _db.FightCards
            .Where(fc => fc.EventId == eventId)
            .ToListAsync();

        if (!fightCards.Any())
        {
            _logger.LogDebug("No fight cards found for event {EventId} to update monitoring", eventId);
            return;
        }

        foreach (var card in fightCards)
        {
            card.Monitored = monitored;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Updated monitoring status to {Monitored} for {Count} fight cards in event {EventId}",
            monitored, fightCards.Count, eventId);
    }

    /// <summary>
    /// Checks if an event has at least one monitored fight card
    /// Used to determine if automatic downloads should occur
    /// </summary>
    public async Task<bool> HasAnyMonitoredFightCardsAsync(int eventId)
    {
        var hasMonitoredCards = await _db.FightCards
            .AnyAsync(fc => fc.EventId == eventId && fc.Monitored);

        return hasMonitoredCards;
    }

    /// <summary>
    /// Gets the list of monitored fight cards for an event
    /// Useful for determining which specific cards to download
    /// </summary>
    public async Task<List<FightCard>> GetMonitoredFightCardsAsync(int eventId)
    {
        return await _db.FightCards
            .Where(fc => fc.EventId == eventId && fc.Monitored)
            .OrderBy(fc => fc.CardNumber)
            .ToListAsync();
    }
}
