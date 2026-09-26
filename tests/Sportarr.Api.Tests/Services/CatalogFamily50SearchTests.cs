using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily50SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);

    public static TheoryData<Event, string> FrozenEvents => new()
    {
        { TeamEvent("Campeonato Nacional Feminino", "Valadares Gaia Feminino vs Porto Feminino", "Valadares Gaia Feminino", "Porto Feminino", "2026-09-12"), "Porto Feminino Valadares Gaia Feminino" },
        { TeamEvent("Campeonato Nacional Feminino", "Benfica Women vs Racing Power", "Benfica Women", "Racing Power", "2026-09-12"), "Benfica Women Racing Power" },
        { TeamEvent("Campeonato de Portugal Serie A", "Braga B vs Maia Lidador", "Braga B", "Maia Lidador", "2026-08-16"), "Braga B Maia Lidador" },
        { TeamEvent("Campeonato de Portugal Serie A", "Maria da Fonte vs Braga B", "Maria da Fonte", "Braga B", "2026-09-13"), "Braga B Maria da Fonte" },
        { TeamEvent("Campeonato de Portugal Serie B", "Machico vs Estrela da Calheta", "Machico", "Estrela da Calheta", "2026-08-15"), "Estrela da Calheta Machico" },
        { TeamEvent("Campeonato de Portugal Serie B", "Alpendorada vs Machico", "Alpendorada", "Machico", "2026-09-13"), "Alpendorada Machico" },
        { TeamEvent("Campeonato de Portugal Serie C", "Naval 1893 vs União da Serra", "Naval 1893", "União da Serra", "2026-08-16"), "Naval 1893 Uniao da Serra" },
        { TeamEvent("Campeonato de Portugal Serie C", "Fátima vs Oliveira do Hospital", "Fátima", "Oliveira do Hospital", "2026-09-13"), "Fatima Oliveira do Hospital" },
        { TeamEvent("Campeonato de Portugal Serie D", "Juventude de Évora vs Alcochetense", "Juventude de Évora", "Alcochetense", "2026-08-16"), "Alcochetense Juventude de Evora" },
        { TeamEvent("Campeonato de Portugal Serie D", "Sintrense vs Santa Clara B", "Sintrense", "Santa Clara B", "2026-09-13"), "Santa Clara B Sintrense" }
    };

    [Theory]
    [MemberData(nameof(FrozenEvents))]
    public void VerifiedFamilyUsesMeasuredQueryPlan(Event evt, string expected)
    {
        QueryService.BuildEventQueries(evt).Should().Equal(expected);
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
