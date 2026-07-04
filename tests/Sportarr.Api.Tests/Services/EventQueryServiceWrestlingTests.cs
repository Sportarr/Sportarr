using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class EventQueryServiceWrestlingTests
{
    private static EventQueryService CreateService() =>
        new(NullLogger<EventQueryService>.Instance);

    [Fact]
    public void BuildEventQueries_WweWeeklyShow_UsesDateNotEpisodeNumber()
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = "SmackDown #1377",
            Sport = "Wrestling",
            EventDate = new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "WWE", Sport = "Wrestling" },
        };

        var queries = service.BuildEventQueries(evt);

        // Date-based org + show query, the format that actually returns results.
        queries.Should().Contain("WWE SmackDown 2026 01 09");
        // The episode number from the title ("#1377") must never leak into a query.
        queries.Should().NotContain(q => q.Contains("1377"));
    }

    [Fact]
    public void BuildEventQueries_WweWeeklyShow_HasMonthLevelFallback()
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = "Monday Night Raw #1500",
            Sport = "Wrestling",
            EventDate = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "WWE", Sport = "Wrestling" },
        };

        var queries = service.BuildEventQueries(evt);

        // Day-level primary and month-level fallback, again without the episode number.
        queries.Should().Contain("WWE Raw 2026 03 02");
        queries.Should().Contain("WWE Raw 2026 03");
        queries.Should().NotContain(q => q.Contains("1500"));
    }
}
