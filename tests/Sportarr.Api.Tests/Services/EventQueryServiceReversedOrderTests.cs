using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The reversed home/away fallback query must work from the denormalized
/// team-name columns, not just the Team navigations. Field case: an NCAA
/// game titled "South Florida vs Old Dominion" produced a single query on a
/// build that carried the fallback, because college events have no linked
/// Team rows - only HomeTeamName/AwayTeamName strings - and the fallback
/// read evt.HomeTeam?.Name exclusively. The tracker titled the release
/// "Old Dominion vs South Florida", so the one ordered query found nothing.
/// </summary>
public class EventQueryServiceReversedOrderTests
{
    private static EventQueryService CreateService() =>
        new(NullLogger<EventQueryService>.Instance);

    private static Event CollegeEvent() => new()
    {
        Title = "South Florida vs Old Dominion",
        Sport = "Football",
        EventDate = new DateTime(2025, 12, 17, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "NCAA Division 1", Sport = "Football" },
    };

    [Fact]
    public void TeamSport_NameColumnsOnly_AddsReversedPairingQuery()
    {
        var service = CreateService();
        var evt = CollegeEvent();
        evt.HomeTeamName = "South Florida";
        evt.AwayTeamName = "Old Dominion";

        var queries = service.BuildEventQueries(evt);

        queries.Should().Contain("Old Dominion vs South Florida");
    }

    [Fact]
    public void TeamSport_NoTeamData_DerivesReversedPairingFromTitle()
    {
        var service = CreateService();
        var evt = CollegeEvent();

        var queries = service.BuildEventQueries(evt);

        queries.Should().Contain("Old Dominion vs South Florida");
    }

    [Fact]
    public void TeamSport_NavigationsOnly_StillAddsReversedPairingQuery()
    {
        var service = CreateService();
        var evt = CollegeEvent();
        evt.HomeTeam = new Team { Name = "South Florida", Sport = "Football" };
        evt.AwayTeam = new Team { Name = "Old Dominion", Sport = "Football" };

        var queries = service.BuildEventQueries(evt);

        queries.Should().Contain("Old Dominion vs South Florida");
    }

    [Fact]
    public void Fighting_MatchupTitle_AddsBothSurnameOrders()
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = "Fabio Wardley vs Daniel Dubois",
            Sport = "Boxing",
            EventDate = new DateTime(2026, 5, 9, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "Boxing", Sport = "Boxing" },
        };

        var queries = service.BuildEventQueries(evt);

        queries.Should().Contain("Wardley vs Dubois");
        queries.Should().Contain("Dubois vs Wardley");
    }

    [Fact]
    public void Fighting_NumberedCard_AddsBothSurnameOrdersAfterCardQuery()
    {
        var service = CreateService();
        var evt = new Event
        {
            Title = "UFC 299: O'Malley vs Vera",
            Sport = "MMA",
            EventDate = new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "UFC", Sport = "MMA" },
        };

        var queries = service.BuildEventQueries(evt);

        queries[0].Should().Be("UFC 299");
        var surnameIdx = queries.FindIndex(q => q.Contains("vs", StringComparison.OrdinalIgnoreCase) && !q.StartsWith("UFC", StringComparison.OrdinalIgnoreCase));
        surnameIdx.Should().BePositive();
        var reversedExists = queries.Exists(q => q.Split(" vs ") is { Length: 2 } p && queries.Contains($"{p[1]} vs {p[0]}"));
        reversedExists.Should().BeTrue();
    }
}
