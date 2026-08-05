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

    [Fact]
    public void BuildEventQueries_CollegeFootballGame_IncludesReversedTeamOrder()
    {
        var service = CreateService();
        var homeTeam = new Team { Name = "South Florida" };
        var awayTeam = new Team { Name = "Old Dominion" };
        var evt = new Event
        {
            Title = "South Florida vs Old Dominion",
            Sport = "Football",
            EventDate = new DateTime(2025, 12, 17, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "NCAA Division 1", Sport = "Football" },
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
        };

        var queries = service.BuildEventQueries(evt);

        // College football has no recognized league-prefix shorthand (unlike
        // NFL/NBA/etc.), so the query is the event title verbatim - but some
        // indexers title releases in broadcast order rather than Sportarr's
        // home/away designation, so the reversed pairing must also be tried.
        queries.Should().Contain("South Florida vs Old Dominion");
        queries.Should().Contain("Old Dominion vs South Florida");
    }
}
