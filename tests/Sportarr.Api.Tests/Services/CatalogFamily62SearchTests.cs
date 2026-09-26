using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CatalogFamily62SearchTests
{
    private static readonly EventQueryService Queries = new(NullLogger<EventQueryService>.Instance);
    private static readonly ReleaseMatchingService Matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));
    private static readonly ReleaseMatchScorer Scorer = new();

    [Theory]
    [InlineData("Copa America", "Argentina", "Canada", "2024-06-21", "1")]
    [InlineData("Copa America Femenina", "Colombia Women", "Brazil Women", "2025-08-02", "200")]
    public void MeasuredCopaEventsUseOneDefaultParticipantQuery(
        string league, string home, string away, string date, string round)
    {
        Queries.BuildEventQueries(Game(league, home, away, date, round))
            .Should().Equal($"{home} vs {away}");
    }

    [Fact]
    public void UserTemplateStillOverridesTheDefaultQuery()
    {
        Queries.BuildEventQueries(
                Game("Copa America", "Argentina", "Canada", "2024-06-21", "1"),
                customTemplate: "{League} {Year} {HomeTeam} {AwayTeam}")
            .Should().Equal("CopaAmerica 2024 Argentina Canada");
    }

    [Theory]
    [InlineData("Copa America", "Argentina", "Canada", "2024-06-21", "1", "Аргентина", "Канада", "Copa America 2024 Аргентина Канада")]
    [InlineData("Copa America Femenina", "Colombia Women", "Brazil Women", "2025-08-02", "200", "Колумбия", "Бразилия", "Copa America Femenina 2025 Колумбия Бразилия")]
    public void CopaAmericaPreservesConfiguredTeamAliasSearch(
        string league, string home, string away, string date, string round,
        string homeAlias, string awayAlias, string expectedAlias)
    {
        var evt = Game(league, home, away, date, round);
        evt.HomeTeam = new Team { Name = home, UserAliases = homeAlias };
        evt.AwayTeam = new Team { Name = away, UserAliases = awayAlias };

        Queries.BuildEventQueries(evt).Should().Equal($"{home} vs {away}", expectedAlias);
    }

    [Theory]
    [InlineData("Copa America 2024 06 20 MD1 Argentina vs Canada 720p60 x264 EN TSN")]
    [InlineData("Copa América 2024 FS1 Group A Round 1 Argentina vs Canada Full Match Replay 20/06/2024 720pEng60fps")]
    [InlineData("Copa America 2024 Argentina vs Canada 20 06 720pEN60fps FS1")]
    [InlineData("Copa America 2024 06 20 MD1 Argentina vs Canada 1080p x264 EN PS1")]
    public void MidnightUtcCopaGameAcceptsObservedPreviousDayRelease(string title)
    {
        var evt = Game("Copa America", "Argentina", "Canada", "2024-06-21", "1");
        var result = Matcher.ValidateRelease(Release(title), evt);
        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void PreviousDayCopaReleaseCannotFillAnotherGameBetweenTheSameTeams()
    {
        var evt = Game("Copa America", "Argentina", "Canada", "2024-06-21", "1");
        var previous = Game("Copa America", "Argentina", "Canada", "2024-06-20", "1");
        previous.Id = 2;
        previous.ExternalId = "ev-previous";
        var result = Matcher.ValidateRelease(
            Release("Copa America 2024 06 20 MD1 Argentina vs Canada 720p60 x264 EN TSN"),
            evt, datePeers: [previous]);
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public async Task VerifiedCopaPeersBlockTheWrongGameForEventSearchAndRss()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SportarrDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var league = new League { Id = 1, Name = "Copa America", Sport = "Soccer" };
        var target = Game("Copa America", "Argentina", "Canada", "2024-06-21", "1");
        var previous = Game("Copa America", "Argentina", "Canada", "2024-06-20", "1");
        target.HomeTeamId = target.AwayTeamId = null;
        previous.HomeTeamId = previous.AwayTeamId = null;
        target.League = previous.League = league;
        previous.Id = 2;
        previous.ExternalId = "ev-previous";
        db.Events.AddRange(target, previous);
        await db.SaveChangesAsync();

        const string title = "Copa America 2024 06 20 MD1 Argentina vs Canada 720p60 x264 EN TSN";
        var release = Release(title);
        var eventPeers = await EventDateMatchContext.LoadAsync(db, target);
        var rssPeers = await EventDateMatchContext.LoadAsync(
            db, new[] { new DateTime(2024, 6, 20) },
            new[] { target }.Where(EventDateMatchContext.ShouldLoadPeers)
                .Select(evt => evt.LeagueId!.Value).ToArray());

        eventPeers.Select(peer => peer.Id).Should().Contain(previous.Id);
        rssPeers.Select(peer => peer.Id).Should().Contain(previous.Id);
        Matcher.ValidateRelease(release, target, datePeers: eventPeers).IsMatch.Should().BeFalse();
        Matcher.ValidateRelease(release, target, datePeers: rssPeers).IsMatch.Should().BeFalse();
    }

    [Theory]
    [InlineData("Copa America SF 2024 Argentina vs Canada 09 07 720pEN60fps FS1")]
    [InlineData("Copa America 2024 Final Argentina vs Canada 20 06 720pEN60fps FS1")]
    [InlineData("FIBA World Cup Qualifiers 2026 Canada vs Argentina 01 09 720pEN60fps DAZN")]
    public void CopaDateGraceRejectsAnotherMatchOrSport(string title)
    {
        var evt = Game("Copa America", "Argentina", "Canada", "2024-06-21", "1");
        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeFalse();
    }

    [Fact]
    public void WomensCopaFinalCanOmitTheTeamSuffixWhenCompetitionNamesWomen()
    {
        var evt = Game("Copa America Femenina", "Colombia Women", "Brazil Women", "2025-08-02", "200");
        const string title = "Women's Copa America Final 2025 Colombia vs Brazil 02 08 720pEN60fps FS1";
        var result = Matcher.ValidateRelease(Release(title), evt);
        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void WrittenMonthDateAllowsObservedCopaFinal()
    {
        var evt = Game("Copa America", "Argentina", "Colombia", "2024-07-15", "200");
        const string title = "Copa América FINAL Argentina vs Colombia 14 July 2024 1080pSpn60fps TUDN";
        Matcher.ParseRelease(title).EventDate.Should().Be(new DateTime(2024, 7, 14));
        var result = Matcher.ValidateRelease(Release(title), evt);
        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        Scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("Copa America Final 2025 Colombia vs Brazil 02 08 720pEN60fps FS1")]
    [InlineData("Women's Copa America 2025 Brazil vs Colombia 25 07 720pEN60fps FS2")]
    [InlineData("FIBA Women's AmeriCup 2019 Colombia vs Brazil 22/09 720pEN60fps")]
    public void WomensCopaRejectsMensAndWrongEventReleases(string title)
    {
        var evt = Game("Copa America Femenina", "Colombia Women", "Brazil Women", "2025-08-02", "200");
        Matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeFalse();
    }

    private static Event Game(string league, string home, string away, string dateText, string round)
    {
        var date = DateTime.Parse(dateText);
        var utc = dateText switch
        {
            "2024-06-21" => new DateTime(2024, 6, 21, 0, 0, 0, DateTimeKind.Utc),
            "2024-07-15" => new DateTime(2024, 7, 15, 0, 30, 0, DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc)
        };
        return new Event
        {
            Id = 1,
            ExternalId = "ev-frozen",
            Title = $"{home} vs {away}",
            Sport = "Soccer",
            Season = date.Year.ToString(),
            Round = round,
            EventDate = utc,
            BroadcastDate = date,
            BroadcastDateVerified = true,
            HomeTeamId = 1,
            AwayTeamId = 2,
            HomeTeamName = home,
            AwayTeamName = away,
            LeagueId = 1,
            League = new League { Id = 1, Name = league, Sport = "Soccer" }
        };
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture"
    };
}
