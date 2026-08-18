using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #239: {HomeTeam} and {AwayTeam} in a custom search
/// template resolved to empty strings. The tokens read only the
/// HomeTeam/AwayTeam navigations, which need linked Team rows that many
/// leagues never get. Sync writes the denormalized name columns for every
/// event, so the tokens must read those first, the way the reversed-order
/// fallback already did, and fall back to the "Home vs Away" title last.
/// </summary>
public class EventQueryServiceTemplateTeamTokenTests
{
    private static EventQueryService CreateService() =>
        new(NullLogger<EventQueryService>.Instance);

    private static Event NflEvent() => new()
    {
        Title = "Green Bay Packers vs Detroit Lions",
        Sport = "Football",
        EventDate = new DateTime(2027, 1, 10, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "NFL", Sport = "Football" },
    };

    [Fact]
    public void TeamTokens_ResolveFromDenormalizedColumns_WhenNoTeamRowsAreLinked()
    {
        // The reporter's case: NFL events carry team names in the columns,
        // but no linked Team rows, so the navigations are null.
        var evt = NflEvent();
        evt.HomeTeamName = "Green Bay Packers";
        evt.AwayTeamName = "Detroit Lions";

        var query = CreateService().BuildQueryFromTemplate(
            "{HomeTeam} vs {AwayTeam} {Day}.{Month}.{Year}", evt);

        query.Should().Be("Green Bay Packers vs Detroit Lions 10.01.2027");
    }

    [Fact]
    public void TeamTokens_FallBackToTheTitle_WhenColumnsAndRowsAreBothAbsent()
    {
        var query = CreateService().BuildQueryFromTemplate("{HomeTeam} vs {AwayTeam}", NflEvent());

        query.Should().Be("Green Bay Packers vs Detroit Lions");
    }

    [Fact]
    public void TeamTokens_PreferTheColumns_OverTheLinkedRows()
    {
        // Sync keeps the columns current per event; a linked Team row can
        // carry a broader canonical name. The columns win, matching the
        // reversed-order fallback.
        var evt = NflEvent();
        evt.HomeTeamName = "Green Bay Packers";
        evt.AwayTeamName = "Detroit Lions";
        evt.HomeTeam = new Team { Name = "Packers Football Club" };
        evt.AwayTeam = new Team { Name = "Lions Football Club" };

        var query = CreateService().BuildQueryFromTemplate("{HomeTeam} vs {AwayTeam}", evt);

        query.Should().Be("Green Bay Packers vs Detroit Lions");
    }

    [Fact]
    public void TeamTokens_ExplicitOverrides_StillWin()
    {
        // Alias query variants pass explicit names; they must beat every
        // fallback or the alias feature stops working.
        var evt = NflEvent();
        evt.HomeTeamName = "Green Bay Packers";
        evt.AwayTeamName = "Detroit Lions";

        var query = CreateService().BuildQueryFromTemplate(
            "{HomeTeam} vs {AwayTeam}", evt, homeTeamName: "GB Packers", awayTeamName: "Det Lions");

        query.Should().Be("GB Packers vs Det Lions");
    }

    [Fact]
    public void TeamTokens_StayEmpty_WhenNothingNamesTheTeams()
    {
        var evt = new Event
        {
            Title = "Monaco Grand Prix",
            Sport = "Motorsport",
            EventDate = new DateTime(2027, 5, 23, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "Formula 1", Sport = "Motorsport" },
        };

        var query = CreateService().BuildQueryFromTemplate("{HomeTeam}{AwayTeam}ok", evt);

        query.Should().Be("ok", "an event with no matchup has nothing to put in the tokens");
    }
}
