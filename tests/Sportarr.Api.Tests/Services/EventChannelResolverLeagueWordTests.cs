using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report (#356/#376): a Gold Coast AFL programme was competing with the
/// Supercars broadcast. For non-team events, title-word overlap alone is too
/// weak; a league-word hit is required once the overlap is only two words.
/// </summary>
public class EventChannelResolverLeagueWordTests
{
    private const string EpgProgramSource = "epg_program";

    private static SportarrDbContext Db() => new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static EventChannelResolverService Svc(SportarrDbContext db) =>
        new(db, Mock.Of<ILogger<EventChannelResolverService>>());

    private static DateTime EventStart() => DateTime.UtcNow.Date.AddDays(2).AddHours(1);

    private static IptvSource AddIptvSource(SportarrDbContext db)
    {
        var source = new IptvSource
        {
            Name = "Provider",
            Url = "http://iptv.test/m3u",
            IsActive = true
        };

        db.IptvSources.Add(source);
        db.SaveChanges();

        return source;
    }

    private static EpgSource AddEpgSource(SportarrDbContext db)
    {
        var source = new EpgSource
        {
            Name = "EPG",
            Url = "http://epg.test",
            IsActive = true
        };

        db.EpgSources.Add(source);
        db.SaveChanges();

        return source;
    }

    private static IptvChannel AddIptvChannel(
        SportarrDbContext db,
        IptvSource source,
        string name,
        string tvgId)
    {
        var channel = new IptvChannel
        {
            Name = name,
            StreamUrl = $"http://iptv.test/stream/{tvgId}",
            SourceId = source.Id,
            IsEnabled = true,
            TvgId = tvgId
        };

        db.IptvChannels.Add(channel);
        db.SaveChanges();

        return channel;
    }

    private static League AddLeague(SportarrDbContext db, string name, string sport)
    {
        var league = new League
        {
            Name = name,
            Sport = sport
        };

        db.Leagues.Add(league);
        db.SaveChanges();

        return league;
    }

    private static Event AddEvent(
        SportarrDbContext db,
        League league,
        string title,
        string sport,
        DateTime eventDate)
    {
        var evt = new Event
        {
            Title = title,
            Sport = sport,
            EventDate = eventDate,
            LeagueId = league.Id
        };

        db.Events.Add(evt);
        db.SaveChanges();

        return evt;
    }

    private static EpgProgram AddEpgProgram(
        SportarrDbContext db,
        EpgSource epgSource,
        string channelId,
        string title,
        DateTime startTime,
        DateTime endTime,
        string description = "")
    {
        var program = new EpgProgram
        {
            EpgSourceId = epgSource.Id,
            ChannelId = channelId,
            Title = title,
            Description = description,
            StartTime = startTime,
            EndTime = endTime
        };

        db.EpgPrograms.Add(program);
        db.SaveChanges();

        return program;
    }

    private static void AddManualLeagueMapping(
        SportarrDbContext db,
        int channelId,
        League league)
    {
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = channelId,
            LeagueId = league.Id,
            IsManual = true,
            Confidence = 100
        });

        db.SaveChanges();
    }

    [Fact]
    public async Task ALeagueWordHitIsRequiredForNonTeamEventsWithOnlyTwoTitleHits()
    {
        using var db = Db();
        var iptvSource = AddIptvSource(db);
        var epgSource = AddEpgSource(db);

        var afl = AddIptvChannel(db, iptvSource, "AFL Channel", "afl.au");
        var fox = AddIptvChannel(db, iptvSource, "Fox506", "fox506.au");

        var league = AddLeague(db, "Supercars Championship", "Motorsport");
        var eventDate = EventStart();
        var evt = AddEvent(
            db,
            league,
            "Boost Mobile Gold Coast 500 Qualifying - Race 32",
            "Motorsport",
            eventDate);

        AddEpgProgram(
            db,
            epgSource,
            "afl.au",
            "AFL Women's Premiership - Gold Coast SUNS vs Richmond Tigers",
            eventDate.AddMinutes(10),
            eventDate.AddHours(2));

        AddEpgProgram(
            db,
            epgSource,
            "fox506.au",
            "Supercars Championship - Boost Mobile Gold Coast 500: Top 10 Shootout",
            eventDate.AddMinutes(-20),
            eventDate.AddHours(2));

        var ranked = (await Svc(db).ResolveAsync(evt.Id)).ToList();

        ranked.Should().NotBeEmpty();
        ranked[0].ChannelId.Should().Be(fox.Id);
        ranked[0].Source.Should().Be(EpgProgramSource);

        var aflEpgCandidates = ranked
            .Where(candidate => candidate.Source == EpgProgramSource && candidate.ChannelId == afl.Id)
            .ToList();

        aflEpgCandidates.Should().BeEmpty(
            "a Gold Coast AFL programme must not win an event channel without a Supercars league-word hit");
    }

    [Fact]
    public async Task AProgramThatStartedBeforeTheWindowButIsStillOnAirIsConsidered()
    {
        using var db = Db();
        var iptvSource = AddIptvSource(db);
        var epgSource = AddEpgSource(db);

        var fox = AddIptvChannel(db, iptvSource, "Fox506", "fox506.au");

        var league = AddLeague(db, "Supercars Championship", "Motorsport");
        var eventDate = EventStart();
        var evt = AddEvent(
            db,
            league,
            "Boost Mobile Gold Coast 500 Qualifying - Race 32",
            "Motorsport",
            eventDate);

        AddEpgProgram(
            db,
            epgSource,
            "fox506.au",
            "Supercars Championship - Boost Mobile Gold Coast 500 Qualifying",
            eventDate.AddMinutes(-65),
            eventDate.AddMinutes(60));

        var ranked = (await Svc(db).ResolveAsync(evt.Id)).ToList();

        ranked
            .Where(candidate => candidate.Source == EpgProgramSource && candidate.ChannelId == fox.Id)
            .Should()
            .NotBeEmpty("a block already running across the event start should be considered as if on time");
    }

    [Fact]
    public async Task AFormulaOneShortFormCountsAsALeagueWordHit()
    {
        using var db = Db();
        var iptvSource = AddIptvSource(db);
        var epgSource = AddEpgSource(db);

        var fox = AddIptvChannel(db, iptvSource, "Fox506", "fox506.au");

        var league = AddLeague(db, "Formula 1", "Motorsport");
        var eventDate = EventStart();
        var evt = AddEvent(
            db,
            league,
            "Australian Grand Prix - Race",
            "Motorsport",
            eventDate);

        AddEpgProgram(
            db,
            epgSource,
            "fox506.au",
            "F1: Australian GP Race",
            eventDate.AddMinutes(-10),
            eventDate.AddHours(1));

        var ranked = (await Svc(db).ResolveAsync(evt.Id)).ToList();

        ranked
            .Where(candidate => candidate.Source == EpgProgramSource && candidate.ChannelId == fox.Id)
            .Should()
            .NotBeEmpty("F1 is the short-form league-word hit for Formula 1");
    }

    [Fact]
    public async Task AManualLeagueMappingCannotOutrankAnEpgProgramMatch()
    {
        using var db = Db();
        var iptvSource = AddIptvSource(db);
        var epgSource = AddEpgSource(db);

        var afl = AddIptvChannel(db, iptvSource, "AFL Channel", "afl.au");
        var fox = AddIptvChannel(db, iptvSource, "Fox506", "fox506.au");

        var league = AddLeague(db, "Supercars Championship", "Motorsport");
        var eventDate = EventStart();
        var evt = AddEvent(
            db,
            league,
            "Boost Mobile Gold Coast 500 Qualifying - Race 32",
            "Motorsport",
            eventDate);

        AddManualLeagueMapping(db, afl.Id, league);

        AddEpgProgram(
            db,
            epgSource,
            "fox506.au",
            "Supercars Championship - Boost Mobile Gold Coast 500 Qualifying",
            // 20 minutes early scores 92, below the 97 the mapping used to reach.
            eventDate.AddMinutes(-20),
            eventDate.AddHours(1));

        var ranked = (await Svc(db).ResolveAsync(evt.Id)).ToList();

        ranked.Should().NotBeEmpty();
        ranked[0].ChannelId.Should().Be(fox.Id);
        ranked[0].Source.Should().Be(EpgProgramSource);

        var mappedCandidate = ranked
            .FirstOrDefault(candidate => candidate.ChannelId == afl.Id);

        mappedCandidate.Should().NotBeNull("the manual league mapping should still be returned, capped");

        var candidate = mappedCandidate!;
        candidate.Source.Should().NotBe(EpgProgramSource);
        candidate.Confidence.Should().BeLessThanOrEqualTo(
            91,
            "non-EPG candidates are capped below any guide match so a manual mapping cannot reach 97");
    }
}
