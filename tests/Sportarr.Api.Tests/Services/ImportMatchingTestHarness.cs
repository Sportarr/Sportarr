using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

internal static class ImportMatchingTestHarness
{
    public static ImportMatchingService Service()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ImportMatchingService(
            new SportarrDbContext(options),
            new MediaFileParser(NullLogger<MediaFileParser>.Instance),
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            NullLogger<ImportMatchingService>.Instance);
    }

    public static Event Event(string title, string eventSport, string league, string leagueSport) => new()
    {
        Id = 1,
        Title = title,
        Sport = eventSport,
        EventDate = new DateTime(2026, 6, 15, 18, 0, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2026, 6, 15),
        League = new League { Id = 1, Name = league, Sport = leagueSport }
    };
}
