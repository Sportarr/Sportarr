using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily43SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);

    public static TheoryData<string, string, string, string, string, string, string, string, string> FrozenEvents => new()
    {
        { "Burundi Ligue A", "Soccer", "Rukinzo vs Panthère Noir", "Rukinzo", "Panthère Noir", "2026-2027", "2026-08-14", "Rukinzo vs Panthère Noir", "Panthère Noir vs Rukinzo" },
        { "Burundi Ligue A", "Soccer", "Le Messager Ngozi vs Panthère Noir", "Le Messager Ngozi", "Panthère Noir", "2026-2027", "2026-09-13", "Le Messager Ngozi vs Panthère Noir", "Panthère Noir vs Le Messager Ngozi" },
        { "CAF Champions League", "Soccer", "AS Port vs Zamalek", "AS Port", "Zamalek", "2026-2027", "2026-09-04", "AS Port vs Zamalek", "Zamalek vs AS Port" },
        { "CAF Champions League", "Soccer", "Rivers United vs FC San Pédro", "Rivers United", "FC San Pédro", "2026-2027", "2026-09-13", "Rivers United vs FC San Pédro", "FC San Pédro vs Rivers United" },
        { "CAF Confederation Cup", "Soccer", "PSI vs Korhogo", "PSI", "Korhogo", "2026-2027", "2026-09-04", "PSI vs Korhogo", "Korhogo vs PSI" },
        { "CAF Confederation Cup", "Soccer", "Nations FC vs Diarra", "Nations FC", "Diarra", "2026-2027", "2026-09-13", "Nations FC vs Diarra", "Diarra vs Nations FC" },
        { "CAF Womens Olympic Qualifying Tournament", "Soccer", "Uganda Women vs Rwanda Women", "Uganda Women", "Rwanda Women", "2024", "2023-07-12", "Uganda Women vs Rwanda Women", "Rwanda Women vs Uganda Women" },
        { "CAF Womens Olympic Qualifying Tournament", "Soccer", "Morocco Women vs Zambia Women", "Morocco Women", "Zambia Women", "2024", "2024-04-10", "Morocco Women vs Zambia Women", "Zambia Women vs Morocco Women" }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void ZeroResultCohortRetainsDirectionalQueries(
        string leagueName,
        string sport,
        string title,
        string home,
        string away,
        string season,
        string broadcastDate,
        string forward,
        string reverse)
    {
        var date = DateTime.Parse(broadcastDate, System.Globalization.CultureInfo.InvariantCulture);
        var evt = new Event
        {
            Title = title,
            Sport = sport,
            Season = season,
            EventDate = DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
            BroadcastDate = date,
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
