using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class EventQueryServiceTeamSportTests
{
    private static EventQueryService CreateService() =>
        new(NullLogger<EventQueryService>.Instance);

    [Fact]
    public void BuildEventQueries_NhlGame_UsesSpacesNotDots()
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = "New Jersey Devils vs New York Rangers",
            Sport = "Ice Hockey",
            EventDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "NHL", Sport = "Ice Hockey" },
        };

        var queries = service.BuildEventQueries(evt);

        // Space-separated is the format trackers accept; dot-separated ("NHL.2026.01")
        // returned nothing on some trackers.
        queries.Should().Contain("NHL 2026 01");
        queries.Should().NotContain(q => q.Contains("NHL.2026"));
    }
}
