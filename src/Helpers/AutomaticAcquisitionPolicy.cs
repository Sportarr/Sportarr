using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;

namespace Sportarr.Api.Helpers;

public static class AutomaticAcquisitionPolicy
{
    public static bool CanConsiderEvent(Event evt, DateTime utcNow) =>
        RefusalReason(evt.League, evt.EventDate, false, utcNow) == null ||
        RefusalReason(evt.League, evt.EventDate, true, utcNow) == null;

    public static async Task<string?> RefusalReasonAsync(SportarrDbContext db, int eventId,
        string? part = null, bool isManual = false, bool isPack = false,
        CancellationToken cancellationToken = default)
    {
        if (isManual)
            return null;

        var evt = await db.Events.AsNoTracking().Include(candidate => candidate.League)
            .Include(candidate => candidate.Files)
            .FirstOrDefaultAsync(candidate => candidate.Id == eventId, cancellationToken);
        if (evt == null)
            return "Event no longer exists";

        return RefusalReason(evt, part, DateTime.UtcNow, isPack);
    }

    public static string? RefusalReason(Event evt, string? part, DateTime utcNow, bool isPack = false)
    {
        var league = evt.League;
        if (isPack && league != null && (!league.AutomaticMissingEnabled || !league.AutomaticUpgradesEnabled ||
            league.AutomaticMissingMaxAgeDays > 0 || league.AutomaticUpgradeMaxAgeDays > 0))
            return "Automatic pack coverage cannot be verified against the league acquisition policy";

        var isUpgrade = string.IsNullOrEmpty(part)
            ? evt.HasFile || evt.Files.Any(file => file.Exists)
            : evt.Files.Any(file => file.Exists && (string.IsNullOrEmpty(file.PartName) ||
                string.Equals(file.PartName, part, StringComparison.OrdinalIgnoreCase)));
        return RefusalReason(league, evt.EventDate, isUpgrade, utcNow);
    }

    public static string? RefusalReason(League? league, DateTime eventDate, bool isUpgrade,
        DateTime utcNow, bool isManual = false)
    {
        if (isManual || league == null)
            return null;

        var enabled = isUpgrade ? league.AutomaticUpgradesEnabled : league.AutomaticMissingEnabled;
        var maxAgeDays = isUpgrade ? league.AutomaticUpgradeMaxAgeDays : league.AutomaticMissingMaxAgeDays;
        var action = isUpgrade ? "upgrades" : "missing downloads";

        if (!enabled)
            return $"Automatic {action} are disabled for this league";

        if (maxAgeDays > 0 && (utcNow - eventDate).TotalDays > maxAgeDays)
            return $"Event is outside the {maxAgeDays}-day automatic {action} window";

        return null;
    }
}