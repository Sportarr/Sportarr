using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Sportarr.Api.Data;

/// <summary>
/// Normalizes every DateTime column to Kind=Utc on both write and read. Neither SQLite's
/// TEXT storage nor Postgres's default "timestamp without time zone" mapping track Kind, so
/// a value written as Local/Unspecified comes back as Unspecified - the root cause behind
/// the scattered DateTime.SpecifyKind(..., Utc) fixups across the codebase (and issue #146,
/// "eventDate stored without timezone causes wrong calendar time"). Applying this globally
/// via ConfigureConventions means every DateTime property gets this fix without touching
/// each entity individually.
/// </summary>
public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    // ValueConverter's constructor takes Expression<Func<...>>, and expression trees cannot
    // contain switch expressions - hence the nested ternaries instead of a switch here.
    // Genuinely Local values need a real conversion via ToUniversalTime(); Unspecified is
    // the common case (e.g. a value round-tripped through SQLite once already, or built
    // from DateTime.UtcNow after Kind was lost upstream) and its wall-clock value is
    // already correct UTC, so it's re-stamped only, matching the existing
    // DateTime.SpecifyKind(..., Utc) fixups elsewhere in the codebase. Calling
    // ToUniversalTime() on an Unspecified value would wrongly shift it by the machine's
    // local offset.
    public UtcDateTimeConverter() : base(
        toProvider => toProvider.Kind == DateTimeKind.Utc
            ? toProvider
            : toProvider.Kind == DateTimeKind.Local
                ? toProvider.ToUniversalTime()
                : DateTime.SpecifyKind(toProvider, DateTimeKind.Utc),
        fromProvider => DateTime.SpecifyKind(fromProvider, DateTimeKind.Utc))
    {
    }
}
