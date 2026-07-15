using System;
using System.Collections.Generic;
using System.Text.Json;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Resolves the quality profile a league's SessionTypeQualityProfiles map
/// assigns to an event, by classifying the event title the same way the
/// monitoring filters do: motorsport session names (Race, Qualifying,
/// Free Practice 1, ...) for motorsport leagues, fighting/wrestling event
/// type names (PPV, FightNight, Weekly, ...) for fighting leagues.
/// Returns null when the league has no map, the title doesn't classify,
/// or the classified type has no entry - callers fall back to the league's
/// own quality profile in that case.
///
/// The override is applied by stamping Event.QualityProfileId. Multi-part
/// fight cards are one Event whose parts (Early Prelims, Prelims, Main
/// Card) all search through the same event row, so a PPV mapped to a 4K
/// profile grabs every part at that profile - parts can never diverge.
/// </summary>
public static class SessionTypeQualityResolver
{
    public static int? Resolve(League league, string? eventTitle)
    {
        if (string.IsNullOrWhiteSpace(league.SessionTypeQualityProfiles) || string.IsNullOrWhiteSpace(eventTitle))
            return null;

        Dictionary<string, int>? map;
        try
        {
            map = JsonSerializer.Deserialize<Dictionary<string, int>>(league.SessionTypeQualityProfiles);
        }
        catch (JsonException)
        {
            return null;
        }

        if (map == null || map.Count == 0)
            return null;

        string? typeName = null;
        if (EventPartDetector.IsMotorsport(league.Sport))
        {
            typeName = EventPartDetector.DetectMotorsportSessionType(eventTitle, league.Name);
        }
        else if (EventPartDetector.IsFightingSport(league.Sport))
        {
            var fightingType = EventPartDetector.DetectFightingEventTypeName(eventTitle, league.Name);
            typeName = string.IsNullOrEmpty(fightingType) ? null : fightingType;
        }

        if (typeName == null)
            return null;

        // Case-insensitive lookup so a UI-sent key never misses on casing.
        foreach (var entry in map)
        {
            if (string.Equals(entry.Key, typeName, StringComparison.OrdinalIgnoreCase) && entry.Value > 0)
            {
                return entry.Value;
            }
        }

        return null;
    }
}
