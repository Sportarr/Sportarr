using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily70SearchTests
{
    private static readonly EventQueryService QueryService = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    [Theory]
    [InlineData("Dukla Praha", "Arsenal Česká Lípa", "2026-07-24")]
    [InlineData("Vlašim", "Táborsko", "2026-09-20")]
    public void CzechNationalFootballLeagueSearchesTheEventTeams(string home, string away, string date)
    {
        var evt = TeamEvent("Czech National Football League", "lg-000576", "Soccer", home, away, date);

        QueryService.BuildEventQueries(evt).Should().Equal(
            $"{home} vs {away}", $"{away} vs {home}");
        QueryService.BuildPackQueries(evt).Should().BeEmpty();
        QueryService.BuildQueryFromTemplate("{League} {Year}", evt)
            .Should().Be("CzechNationalFootballLeague 2026");
    }

    [Fact]
    public void GaelicNationalFootballLeagueDivisionDoesNotSearchNfl()
    {
        var evt = TeamEvent("National Football League Division 1", "lg-001184", "Gaelic",
            "Kerry", "Dublin", "2026-02-01");

        QueryService.BuildEventQueries(evt).Should().Equal("Kerry vs Dublin", "Dublin vs Kerry");
        QueryService.BuildPackQueries(evt).Should().BeEmpty();
    }

    [Fact]
    public void NflKeepsItsTwoQueriesAndTemplateToken()
    {
        var evt = TeamEvent("NFL", "lg-000032", "Football",
            "Kansas City Chiefs", "Buffalo Bills", "2026-09-06");

        QueryService.BuildEventQueries(evt).Should().Equal("NFL 2026 09", "NFL 2026");
        QueryService.BuildQueryFromTemplate("{League} {Year}", evt).Should().Be("NFL 2026");
        QueryService.BuildPackQueries(evt).Should().NotBeEmpty();
    }

    [Fact]
    public void ObservedGermanCupReleaseMatchesTheFrozenEvent()
    {
        const string title = "German Cup 2026 Vfl Osnabruck vs FC Bayern Munich 02 09 720pEN60fps DAZN";
        var evt = TeamEvent("DFB-Pokal", "lg-000121", "Soccer",
            "Osnabrück", "Bayern Munich", "2026-09-02");
        evt.Id = 1;
        evt.ExternalId = "ev-2343440";
        evt.LeagueId = 1;
        evt.League!.Id = 1;
        evt.Round = "64";
        evt.EventDate = new DateTime(2026, 9, 2, 18, 45, 0, DateTimeKind.Utc);
        var release = new ReleaseSearchResult
        {
            Title = title,
            Guid = "frozen-german-cup",
            DownloadUrl = "http://fixture.invalid/german-cup.nzb",
            Indexer = "Frozen provider",
            PublishDate = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc)
        };

        var result = Matcher.ValidateRelease(release, evt);

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        new ReleaseMatchScorer().CalculateMatchScore(title, evt)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    private static Event TeamEvent(string leagueName, string leagueId, string sport,
        string home, string away, string date)
    {
        var eventDate = DateTime.Parse(date).ToUniversalTime();
        return new Event
        {
            Title = $"{home} vs {away}",
            Sport = sport,
            EventDate = eventDate,
            BroadcastDate = eventDate.Date,
            HomeTeamName = home,
            AwayTeamName = away,
            HomeTeamId = 1,
            AwayTeamId = 2,
            Season = "2026-2027",
            Round = "1",
            League = new League { Name = leagueName, ExternalId = leagueId, Sport = sport }
        };
    }
}
