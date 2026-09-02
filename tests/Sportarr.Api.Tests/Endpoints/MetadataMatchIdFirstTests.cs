using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Xunit;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// The Sportarr id in a file name is the match key for the media-server
/// agents, the way a tvdb id names a show: a file that carries one names
/// its event exactly, whatever the series or the season and episode
/// numbers say. The numbers are the fallback for a file that carries none.
/// </summary>
public class MetadataMatchIdFirstTests
{
    private static SportarrDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SportarrDbContext(options);
    }

    private static (League nfl, League nba, Event game, Event other) Seed(SportarrDbContext db)
    {
        var nfl = new League { Name = "NFL", Sport = "American Football", ExternalId = "lg-000032" };
        var nba = new League { Name = "NBA", Sport = "Basketball", ExternalId = "lg-000045" };
        db.Leagues.AddRange(nfl, nba);
        db.SaveChanges();
        var game = new Event
        {
            Title = "Atlanta Falcons vs Detroit Lions", Sport = "American Football", Season = "2025",
            EventDate = new DateTime(2025, 9, 14), LeagueId = nfl.Id, ExternalId = "ev-312922",
            SeasonNumber = 2025, EpisodeNumber = 5, Status = "completed",
        };
        var other = new Event
        {
            Title = "Boston Celtics vs Miami Heat", Sport = "Basketball", Season = "2025",
            EventDate = new DateTime(2025, 10, 22), LeagueId = nba.Id, ExternalId = "ev-500001",
            SeasonNumber = 2025, EpisodeNumber = 1, Status = "completed",
        };
        db.Events.AddRange(game, other);
        db.SaveChanges();
        return (nfl, nba, game, other);
    }

    [Fact]
    public async Task AFileNamedWithTheIdMatchesItsEventWhateverTheNumbersSay()
    {
        using var db = CreateDb();
        var (nfl, _, game, _) = Seed(db);

        var resolved = await MetadataAgentEndpoints.ResolveMatchAsync(
            db, series: "lg-000045", season: "1999", episode: 99,
            filename: "Wrong - S1999E99 - sportarr-ev-312922.mkv");

        resolved.Error.Should().BeNull();
        resolved.Source.Should().Be("id");
        resolved.Event!.Id.Should().Be(game.Id);
        resolved.League!.Id.Should().Be(nfl.Id, "the id names the league too");
        resolved.SeasonNumber.Should().Be(2025);
        resolved.SeasonEvents.Should().ContainSingle(e => e.Id == game.Id);
    }

    [Fact]
    public async Task TheIdAloneIsEnough()
    {
        using var db = CreateDb();
        var (_, _, game, _) = Seed(db);

        var resolved = await MetadataAgentEndpoints.ResolveMatchAsync(
            db, series: null, season: null, episode: null, filename: "{sportarr-ev-312922}.mkv");

        resolved.Error.Should().BeNull();
        resolved.Event!.Id.Should().Be(game.Id);
    }

    [Fact]
    public async Task AFileWithoutAnIdStillMatchesByItsNumbers()
    {
        using var db = CreateDb();
        var (_, _, game, _) = Seed(db);

        var resolved = await MetadataAgentEndpoints.ResolveMatchAsync(
            db, series: "lg-000032", season: "2025", episode: 5,
            filename: "NFL - S2025E05 - Atlanta Falcons vs Detroit Lions.mkv");

        resolved.Source.Should().Be("numbering");
        resolved.Event!.Id.Should().Be(game.Id);
    }

    [Fact]
    public async Task AnUnknownIdFallsBackToTheNumbers()
    {
        using var db = CreateDb();
        var (_, _, game, _) = Seed(db);

        var resolved = await MetadataAgentEndpoints.ResolveMatchAsync(
            db, series: "lg-000032", season: "2025", episode: 5, filename: "NFL - S2025E05 - sportarr-ev-999999.mkv");

        resolved.Source.Should().Be("numbering");
        resolved.Event!.Id.Should().Be(game.Id);
    }

    [Fact]
    public async Task WithoutAnIdTheNumbersAreRequired()
    {
        using var db = CreateDb();
        Seed(db);

        var resolved = await MetadataAgentEndpoints.ResolveMatchAsync(db, null, null, null, "NFL - something.mkv");

        resolved.Error.Should().Be("series, season and episode are required");
    }

    [Fact]
    public async Task AnIdNamesItsEventEvenWhenTheEventIsCancelled()
    {
        using var db = CreateDb();
        var (nfl, _, _, _) = Seed(db);
        var cancelled = new Event
        {
            Title = "Cancelled Game", Sport = "American Football", Season = "2025",
            EventDate = new DateTime(2025, 9, 21), LeagueId = nfl.Id, ExternalId = "ev-312999",
            SeasonNumber = 2025, EpisodeNumber = null, Status = "cancelled",
        };
        db.Events.Add(cancelled);
        db.SaveChanges();

        var resolved = await MetadataAgentEndpoints.ResolveMatchAsync(
            db, series: "lg-000032", season: "2025", episode: 5, filename: "NFL - S2025E05 - x - sportarr-ev-312999.mkv");

        resolved.Source.Should().Be("id");
        resolved.Event!.Id.Should().Be(cancelled.Id, "the file is of that event, whatever its numbers would have named");
    }

    [Fact]
    public async Task AFolderIsNamedByTheIdInOneOfItsFiles()
    {
        using var db = CreateDb();
        var (nfl, _, _, _) = Seed(db);

        var byEvent = await MetadataAgentEndpoints.LeagueFromHintAsync(db, "Sports/Wrong/Season 2025/Wrong - S2025E05 - sportarr-ev-312922.mkv");
        var byLeague = await MetadataAgentEndpoints.LeagueFromHintAsync(db, "Sports/NFL {sportarr-lg-000032}/tvshow.nfo");
        var none = await MetadataAgentEndpoints.LeagueFromHintAsync(db, "Sports/NFL/Season 2025/NFL - S2025E05.mkv");

        byEvent!.Id.Should().Be(nfl.Id);
        byLeague!.Id.Should().Be(nfl.Id);
        none.Should().BeNull();
    }
}
