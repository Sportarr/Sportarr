using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily49SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);

    public static TheoryData<Event, string> FrozenEvents => new()
    {
        { CardEvent("Caged Steel", "Caged Steel 42", "2026-03-14"), "Caged Steel 42" },
        { CardEvent("Caged Steel", "Caged Steel 43", "2026-07-04"), "Caged Steel 43" },
        { CardEvent("Call of Duty League", "Call of Duty League Major 1 Final", "2026-01-25", "ESports"), "Call of Duty Major 1" },
        { CardEvent("Call of Duty League", "Call of Duty League Major 3 Final", "2026-05-17", "ESports"), "Call of Duty Major 3" },
        { TeamEvent("Cambodia C-League", "Phnom Penh Crown vs NagaWorld", "Phnom Penh Crown", "NagaWorld", "2026-09-05"), "NagaWorld Phnom Penh Crown" },
        { TeamEvent("Cambodia C-League", "MOI Kompong Dewa vs Visakha", "Visakha", "MOI Kompong Dewa", "2026-09-13"), "MOI Kompong Dewa Visakha" },
        { TeamEvent("Cambodian Hun Sen Cup", "Kirivong Sok Sen Chey vs Svay Rieng", "Kirivong Sok Sen Chey", "Svay Rieng", "2026-02-25"), "Kirivong Sok Sen Chey Svay Rieng" },
        { TeamEvent("Cambodian Hun Sen Cup", "Svay Rieng vs Visakha", "Svay Rieng", "Visakha", "2026-05-24"), "Svay Rieng Visakha" }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void VerifiedFamilyUsesMeasuredQueryPlan(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
    }

    private static Event CardEvent(string leagueName, string title, string broadcastDate, string sport = "Combat")
    {
        var date = DateTime.Parse(broadcastDate, System.Globalization.CultureInfo.InvariantCulture);
        return new Event
        {
            Title = title,
            Sport = sport,
            Season = "2026",
            EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            League = new League { Name = leagueName, Sport = sport }
        };
    }

    private static Event TeamEvent(
        string leagueName,
        string title,
        string home,
        string away,
        string broadcastDate)
    {
        var date = DateTime.Parse(broadcastDate, System.Globalization.CultureInfo.InvariantCulture);
        return new Event
        {
            Title = title,
            Sport = "Soccer",
            Season = "2026-2027",
            EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = home,
            AwayTeamName = away,
            League = new League { Name = leagueName, Sport = "Soccer" }
        };
    }
}
