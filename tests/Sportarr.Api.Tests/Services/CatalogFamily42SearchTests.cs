using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily42SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);

    public static TheoryData<string, string, string, string, string, string, string> FrozenEvents => new()
    {
        { "Bulgarian First League", "Soccer", "Spartak Varna vs CSKA 1948", "Spartak Varna", "CSKA 1948", "Spartak Varna vs CSKA 1948", "CSKA 1948 vs Spartak Varna" },
        { "Bulgarian First League", "Soccer", "Botev Vratsa vs Levski Sofia", "Botev Vratsa", "Levski Sofia", "Botev Vratsa vs Levski Sofia", "Levski Sofia vs Botev Vratsa" },
        { "Bulgarian NBL", "Basketball", "BC Botev Vratsa vs BC Beroe", "BC Botev Vratsa", "BC Beroe", "BC Botev Vratsa vs BC Beroe", "BC Beroe vs BC Botev Vratsa" },
        { "Bulgarian NBL", "Basketball", "BC Balkan Botevgrad vs BC Lokomotiv Plovdiv", "BC Balkan Botevgrad", "BC Lokomotiv Plovdiv", "BC Balkan Botevgrad vs BC Lokomotiv Plovdiv", "BC Lokomotiv Plovdiv vs BC Balkan Botevgrad" },
        { "Bulgarian Second League", "Soccer", "Beroe vs Hebar Pazardzhik", "Beroe", "Hebar Pazardzhik", "Beroe vs Hebar Pazardzhik", "Hebar Pazardzhik vs Beroe" },
        { "Bulgarian Second League", "Soccer", "Chernomorets 1919 Burgas vs Spartak Pleven", "Chernomorets 1919 Burgas", "Spartak Pleven", "Chernomorets 1919 Burgas vs Spartak Pleven", "Spartak Pleven vs Chernomorets 1919 Burgas" },
        { "Burkina Faso 1ere Division", "Soccer", "Sporting Cascades vs AJEB", "Sporting Cascades", "AJEB", "Sporting Cascades vs AJEB", "AJEB vs Sporting Cascades" },
        { "Burkina Faso 1ere Division", "Soccer", "Vitesse de Bobo-Dioulasso vs Rahimo", "Vitesse Burkina Faso", "Rahimo", "Vitesse de Bobo-Dioulasso vs Rahimo", "Rahimo vs Vitesse Burkina Faso" }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void ZeroResultCohortRetainsDirectionalQueries(
        string leagueName,
        string sport,
        string title,
        string home,
        string away,
        string forward,
        string reverse)
    {
        var evt = new Event
        {
            Title = title,
            Sport = sport,
            Season = "2026",
            EventDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 1, 1),
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = home,
            AwayTeamName = away,
            League = new League { Name = leagueName, Sport = sport }
        };

        QueryService.BuildEventQueries(evt).Should().Equal(forward, reverse);
    }
}
