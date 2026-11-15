using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #383. Some providers rename a block of channels for each match day,
/// such as "(Apple) (MLS) 026 | Salt Lake vs. St. Louis (2026-10-24 21:25:10)".
/// These channels have no guide data, and they are often the only working
/// source for the match. The resolver reads the teams and the time stamp
/// from the name.
/// </summary>
public class EventChannelResolverEventChannelNameTests : IDisposable
{
    private readonly List<string> _dataPaths = new();

    public void Dispose()
    {
        foreach (var path in _dataPaths)
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    private static SportarrDbContext Db() => new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // Null keeps the system zone as the primary zone. The test container
    // runs in UTC.
    private EventChannelResolverService Svc(SportarrDbContext db, string? timeZone = "America/New_York")
    {
        if (timeZone == null)
        {
            return new EventChannelResolverService(db, Mock.Of<ILogger<EventChannelResolverService>>());
        }

        var dataPath = Path.Combine(Path.GetTempPath(), "sportarr-evch-" + Guid.NewGuid().ToString("N"));
        _dataPaths.Add(dataPath);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = dataPath })
            .Build();
        var configService = new ConfigService(configuration, NullLogger<ConfigService>.Instance);
        configService.GetConfigAsync().GetAwaiter().GetResult().TimeZone = timeZone;
        return new EventChannelResolverService(db, Mock.Of<ILogger<EventChannelResolverService>>(), configService);
    }

    private sealed class Rig
    {
        public required IptvSource Source { get; init; }
        public required League League { get; init; }
        public required Event Event { get; init; }
    }

    private static Rig Seed(SportarrDbContext db, string home, string away, DateTime kickoffUtc,
        string league = "American Major League Soccer", string? broadcast = null, string sport = "Soccer",
        string? alternate = null)
    {
        var source = db.IptvSources.FirstOrDefault()
            ?? new IptvSource { Name = "Provider", Url = "http://iptv.test/xtream", IsActive = true };
        if (source.Id == 0) db.IptvSources.Add(source);
        var lg = db.Leagues.FirstOrDefault(l => l.Name == league)
            ?? new League
            {
                Name = league,
                Sport = sport,
                AlternateName = alternate ?? (league == "American Major League Soccer" ? "MLS, Major League Soccer" : null),
            };
        if (lg.Id == 0) db.Leagues.Add(lg);
        db.SaveChanges();

        var homeTeam = new Team { Name = home, Sport = sport, LeagueId = lg.Id };
        var awayTeam = new Team { Name = away, Sport = sport, LeagueId = lg.Id };
        db.Teams.AddRange(homeTeam, awayTeam);
        db.SaveChanges();

        var evt = new Event
        {
            Title = $"{home} vs {away}",
            Sport = sport,
            LeagueId = lg.Id,
            HomeTeamId = homeTeam.Id,
            AwayTeamId = awayTeam.Id,
            HomeTeamName = home,
            AwayTeamName = away,
            Broadcast = broadcast,
            EventDate = kickoffUtc,
        };
        db.Events.Add(evt);
        db.SaveChanges();
        return new Rig { Source = source, League = lg, Event = evt };
    }

    private static IptvChannel Channel(SportarrDbContext db, Rig rig, string name)
    {
        var channel = new IptvChannel
        {
            Name = name,
            StreamUrl = "http://iptv.test/live/" + Guid.NewGuid().ToString("N"),
            SourceId = rig.Source.Id,
            IsEnabled = true,
        };
        db.IptvChannels.Add(channel);
        db.SaveChanges();
        return channel;
    }

    // 21:30 Eastern summer time on 24 October 2026.
    private static readonly DateTime MlsKickoff = new(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_apple_channel_with_an_eastern_stamp_five_minutes_before_kickoff_is_evidence()
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        var english = Channel(db, rig, "(Apple) (MLS) 026 |  Salt Lake vs. St. Louis  (2026-10-24 21:25:10)");

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().ContainSingle();
        ranked[0].ChannelId.Should().Be(english.Id);
        ranked[0].Source.Should().Be(EventChannelResolverService.EventChannelSource);
        ranked[0].Confidence.Should().Be(95);
    }

    [Fact]
    public async Task The_spanish_copy_ranks_below_the_main_copy_and_stays_as_a_backup()
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        // The Spanish copy is added first, so row order cannot explain the result.
        var spanish = Channel(db, rig, "(Apple) (MLS) 027 |  Salt Lake vs. St. Louis (Spanish) (2026-10-24 21:25:15)");
        var english = Channel(db, rig, "(Apple) (MLS) 026 |  Salt Lake vs. St. Louis  (2026-10-24 21:25:10)");

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Select(c => c.ChannelId).Should().Equal(english.Id, spanish.Id);
        ranked[1].Confidence.Should().Be(92);
        ranked[1].Source.Should().Be(EventChannelResolverService.EventChannelSource);
    }

    [Fact]
    public async Task The_paramount_channel_stamped_thirty_minutes_before_kickoff_matches()
    {
        using var db = Db();
        // 19:30 in London on 27 October 2026, 15:30 Eastern.
        var rig = Seed(db, "Fleetwood Town", "Arsenal", new DateTime(2026, 10, 27, 19, 30, 0, DateTimeKind.Utc),
            league: "English League Cup");
        var channel = Channel(db, rig, "US (Paramount 016) | Fleetwood Town vs. Arsenal (2026-10-27 15:00:05)");

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().ContainSingle();
        ranked[0].ChannelId.Should().Be(channel.Id);
        ranked[0].Confidence.Should().Be(95);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Europe/London")]
    [InlineData("Australia/Sydney")]
    public async Task The_source_zone_is_found_from_the_names_whatever_the_app_zone(string? appZone)
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        Channel(db, rig, "(Apple) (MLS) 026 |  Salt Lake vs. St. Louis  (2026-10-24 21:25:10)");

        var ranked = await Svc(db, timeZone: appZone).ResolveAsync(rig.Event.Id);

        ranked.Should().ContainSingle();
        ranked[0].Confidence.Should().Be(95);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Europe/London")]
    public async Task Each_game_of_a_doubleheader_gets_its_own_channel(string? appZone)
    {
        using var db = Db();
        // 15:05 and 16:05 Eastern summer time on 4 July 2026. The early
        // stamp also fits the late game's window, so the nearest-game rule
        // decides.
        var first = Seed(db, "New York Yankees", "Boston Red Sox", new DateTime(2026, 7, 4, 19, 5, 0, DateTimeKind.Utc),
            league: "Major League Baseball", sport: "Baseball", alternate: "MLB");
        var second = Seed(db, "New York Yankees", "Boston Red Sox", new DateTime(2026, 7, 4, 20, 5, 0, DateTimeKind.Utc),
            league: "Major League Baseball", sport: "Baseball");
        var early = Channel(db, first, "(MLB) 001 | Boston Red Sox @ New York Yankees (2026-07-04 15:00:00)");
        var late = Channel(db, first, "(MLB) 002 | Boston Red Sox @ New York Yankees (2026-07-04 16:00:00)");

        var firstRanked = await Svc(db, timeZone: appZone).ResolveAsync(first.Event.Id);
        var secondRanked = await Svc(db, timeZone: appZone).ResolveAsync(second.Event.Id);

        firstRanked.Select(c => c.ChannelId).Should().Equal(early.Id);
        secondRanked.Select(c => c.ChannelId).Should().Equal(late.Id);
    }

    [Fact]
    public async Task A_short_side_slot_mapped_by_hand_stays_a_normal_candidate()
    {
        using var db = Db();
        var rig = Seed(db, "Los Angeles FC", "Seattle Sounders FC", new DateTime(2026, 10, 27, 23, 30, 0, DateTimeKind.Utc));
        // "LA" is too short to say which team it is, so the name is doubtful.
        var slot = Channel(db, rig, "(Apple) (MLS) 005 | LA vs. Seattle (2026-10-27 19:25:00)");
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = slot.Id, LeagueId = rig.League.Id, IsManual = true, Priority = 1, Confidence = 100,
        });
        db.SaveChanges();

        var resolution = await Svc(db).ResolveDetailedAsync(rig.Event.Id);

        resolution.ForeignEventChannelIds.Should().NotContain(slot.Id);
        resolution.Candidates.Should().ContainSingle(c => c.ChannelId == slot.Id && c.Source == "league-mapping");
    }

    [Fact]
    public async Task A_cup_match_on_a_league_slot_of_the_same_sport_is_not_foreign()
    {
        using var db = Db();
        db.Leagues.Add(new League { Name = "American Major League Soccer", Sport = "Soccer", AlternateName = "MLS" });
        db.SaveChanges();
        var rig = Seed(db, "Inter Miami CF", "Club América", new DateTime(2026, 7, 30, 0, 30, 0, DateTimeKind.Utc),
            league: "Leagues Cup");
        var slot = Channel(db, rig, "(Apple) (MLS) 010 | Inter Miami vs. Club America (2026-07-29 20:25:00)");

        var resolution = await Svc(db).ResolveDetailedAsync(rig.Event.Id);

        resolution.ForeignEventChannelIds.Should().NotContain(slot.Id);
        resolution.Candidates.Should().ContainSingle(c => c.ChannelId == slot.Id
            && c.Source == EventChannelResolverService.EventChannelSource);
    }

    [Fact]
    public async Task A_clearly_different_pair_is_foreign()
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        Seed(db, "Austin FC", "FC Dallas", MlsKickoff);
        var other = Channel(db, rig, "(Apple) (MLS) 028 | Austin vs. Dallas (2026-10-24 21:25:10)");
        var later = Channel(db, rig, "(Apple) (MLS) 029 | Salt Lake vs. St. Louis (2026-11-21 21:25:10)");

        var resolution = await Svc(db).ResolveDetailedAsync(rig.Event.Id);

        resolution.ForeignEventChannelIds.Should().BeEquivalentTo(new[] { other.Id, later.Id });
    }

    [Fact]
    public async Task City_names_without_a_league_word_are_not_evidence()
    {
        using var db = Db();
        var rig = Seed(db, "Los Angeles FC", "Charlotte FC", new DateTime(2026, 10, 27, 23, 30, 0, DateTimeKind.Utc));
        var channel = Channel(db, rig, "US | ESPN+ 005 | Los Angeles vs. Charlotte (2026-10-27 19:25:00)");

        var resolution = await Svc(db).ResolveDetailedAsync(rig.Event.Id);

        resolution.Candidates.Should().NotContain(c => c.Source == EventChannelResolverService.EventChannelSource);
        resolution.ForeignEventChannelIds.Should().NotContain(channel.Id);
    }

    [Fact]
    public async Task Each_group_of_a_source_gets_its_own_zone()
    {
        using var db = Db();
        // Summer dates, so London and UTC differ. 19:30 in London and 19:30
        // Eastern summer time.
        var uk1 = Seed(db, "Arsenal", "Chelsea", new DateTime(2026, 8, 15, 18, 30, 0, DateTimeKind.Utc), league: "English Premier League");
        var uk2 = Seed(db, "Liverpool", "Everton", new DateTime(2026, 8, 15, 18, 30, 0, DateTimeKind.Utc), league: "English Premier League");
        var us1 = Seed(db, "Austin FC", "FC Dallas", new DateTime(2026, 8, 15, 23, 30, 0, DateTimeKind.Utc));
        var us2 = Seed(db, "Houston Dynamo", "Sporting Kansas City", new DateTime(2026, 8, 15, 23, 30, 0, DateTimeKind.Utc));
        IptvChannel Grouped(Rig rig, string group, string name)
        {
            var channel = Channel(db, rig, name);
            channel.Group = group;
            db.SaveChanges();
            return channel;
        }
        var arsenal = Grouped(uk1, "UK Events", "UK | Arsenal vs. Chelsea (2026-08-15 19:25:00)");
        Grouped(uk2, "UK Events", "UK | Liverpool vs. Everton (2026-08-15 19:25:00)");
        var austin = Grouped(us1, "US Events", "US | Austin FC vs. FC Dallas (2026-08-15 19:25:00)");
        Grouped(us2, "US Events", "US | Houston Dynamo vs. Sporting Kansas City (2026-08-15 19:25:00)");

        (await Svc(db, timeZone: null).ResolveAsync(uk1.Event.Id)).Select(c => c.ChannelId).Should().Equal(arsenal.Id);
        (await Svc(db, timeZone: null).ResolveAsync(us1.Event.Id)).Select(c => c.ChannelId).Should().Equal(austin.Id);
    }

    [Theory]
    [InlineData("(Apple) (MLS) 026 | Salt Lake vs. St. Louis Matchday 12 (2026-10-24 21:25:10)", 95)]
    [InlineData("(Apple) (MLS) 026 | Week 30: Salt Lake vs. St. Louis (2026-10-24 21:25:10)", 95)]
    [InlineData("(Apple) (MLS) 026 | Round 5 - Salt Lake vs. St. Louis (2026-10-24 21:25:10)", 95)]
    [InlineData("(Apple) (MLS) 026 | Salt Lake vs. St. Louis Leg 2 (2026-10-24 21:25:10)", 95)]
    [InlineData("(Apple) (MLS) 027 | Salt Lake vs. St. Louis - Spanish (2026-10-24 21:25:15)", 92)]
    [InlineData("(Apple) (MLS) 027 | Salt Lake vs. St. Louis [Spanish] (2026-10-24 21:25:15)", 92)]
    public async Task Round_labels_and_dashed_languages_beside_the_teams_still_match(string name, int confidence)
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        var channel = Channel(db, rig, name);

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().ContainSingle(c => c.ChannelId == channel.Id && c.Confidence == confidence
            && c.Source == EventChannelResolverService.EventChannelSource);
    }

    [Fact]
    public async Task A_game_number_beside_the_teams_still_matches()
    {
        using var db = Db();
        var rig = Seed(db, "Los Angeles Lakers", "Boston Celtics", new DateTime(2026, 6, 15, 0, 30, 0, DateTimeKind.Utc),
            league: "National Basketball Association", sport: "Basketball");
        var channel = Channel(db, rig, "(NBA) 007 | Lakers vs Celtics Game 7 (2026-06-14 20:25:00)");

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().ContainSingle(c => c.ChannelId == channel.Id
            && c.Source == EventChannelResolverService.EventChannelSource);
    }

    [Fact]
    public async Task Extra_words_beside_a_team_name_are_doubtful_not_foreign()
    {
        using var db = Db();
        var rig = Seed(db, "Arsenal", "Chelsea", new DateTime(2026, 10, 27, 19, 30, 0, DateTimeKind.Utc),
            league: "English Premier League");
        var channel = Channel(db, rig, "UK | Arsenal vs. Chelsea Watch Along (2026-10-27 19:25:00)");

        var resolution = await Svc(db, timeZone: null).ResolveDetailedAsync(rig.Event.Id);

        resolution.ForeignEventChannelIds.Should().NotContain(channel.Id);
    }

    [Theory]
    // Three hours apart, the late stamp fits only the late game.
    [InlineData(3)]
    // An hour apart, the late stamp also fits the early game, but less well.
    [InlineData(1)]
    public async Task A_mapped_slot_for_the_other_game_of_a_doubleheader_is_foreign(int hoursApart)
    {
        using var db = Db();
        var firstStart = new DateTime(2026, 7, 4, 19, 5, 0, DateTimeKind.Utc);
        var first = Seed(db, "New York Yankees", "Boston Red Sox", firstStart,
            league: "Major League Baseball", sport: "Baseball", alternate: "MLB");
        Seed(db, "New York Yankees", "Boston Red Sox", firstStart.AddHours(hoursApart),
            league: "Major League Baseball", sport: "Baseball");
        var early = Channel(db, first, "(MLB) 001 | Boston Red Sox @ New York Yankees (2026-07-04 15:00:00)");
        var late = Channel(db, first,
            $"(MLB) 002 | Boston Red Sox @ New York Yankees (2026-07-04 {15 + hoursApart}:00:00)");
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = late.Id, LeagueId = first.League.Id, IsManual = true, Priority = 1, Confidence = 100,
        });
        db.SaveChanges();

        var resolution = await Svc(db).ResolveDetailedAsync(first.Event.Id);

        resolution.ForeignEventChannelIds.Should().Contain(late.Id);
        resolution.Candidates.Select(c => c.ChannelId).Should().Equal(early.Id);
    }

    [Fact]
    public async Task A_city_pair_named_for_another_league_is_foreign()
    {
        using var db = Db();
        db.Leagues.Add(new League { Name = "National Hockey League", Sport = "Ice Hockey" });
        db.SaveChanges();
        var rig = Seed(db, "Toronto FC", "New York City FC", new DateTime(2026, 10, 27, 23, 30, 0, DateTimeKind.Utc));
        var hockey = Channel(db, rig, "(NHL) 004 | Toronto vs. New York (2026-10-27 19:25:00)");

        var resolution = await Svc(db).ResolveDetailedAsync(rig.Event.Id);

        resolution.ForeignEventChannelIds.Should().Contain(hockey.Id);
    }

    [Fact]
    public async Task A_womens_league_slot_never_names_a_mens_game()
    {
        using var db = Db();
        db.Leagues.Add(new League
        {
            Name = "Women's National Basketball Association", Sport = "Basketball", AlternateName = "WNBA",
        });
        db.SaveChanges();
        var rig = Seed(db, "Los Angeles Lakers", "Boston Celtics", new DateTime(2026, 6, 15, 0, 30, 0, DateTimeKind.Utc),
            league: "National Basketball Association", sport: "Basketball");
        var womens = Channel(db, rig, "(WNBA) 002 | Lakers vs. Celtics (2026-06-14 20:25:00)");

        var resolution = await Svc(db).ResolveDetailedAsync(rig.Event.Id);

        resolution.ForeignEventChannelIds.Should().Contain(womens.Id);
        resolution.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task A_pacific_time_source_is_read_in_pacific_time()
    {
        using var db = Db();
        // 16:30 Pacific summer time on 27 October 2026.
        var rig = Seed(db, "Los Angeles FC", "Seattle Sounders FC", new DateTime(2026, 10, 27, 23, 30, 0, DateTimeKind.Utc));
        var channel = Channel(db, rig, "(MLS) 001 | Los Angeles FC vs. Seattle Sounders FC (2026-10-27 16:25:00)");

        var ranked = await Svc(db, timeZone: null).ResolveAsync(rig.Event.Id);

        ranked.Should().ContainSingle(c => c.ChannelId == channel.Id && c.Confidence == 95);
    }

    [Fact]
    public async Task A_side_naming_a_team_that_plays_elsewhere_is_foreign()
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        Seed(db, "Austin FC", "FC Dallas", MlsKickoff);
        var mixed = Channel(db, rig, "(Apple) (MLS) 026 | Salt Lake vs. Austin (2026-10-24 21:25:10)");

        var resolution = await Svc(db).ResolveDetailedAsync(rig.Event.Id);

        resolution.ForeignEventChannelIds.Should().Contain(mixed.Id);
    }

    [Theory]
    [InlineData("Tottenham Hotspur", "Arsenal", "UK | Spurs vs. Arsenal (2026-10-27 19:25:00)")]
    [InlineData("Wolverhampton Wanderers", "Chelsea", "UK | Wolves vs. Chelsea (2026-10-27 19:25:00)")]
    [InlineData("Wolverhampton Wanderers", "Tottenham Hotspur", "UK | Wolves vs. Spurs (2026-10-27 19:25:00)")]
    public async Task A_nickname_without_an_alias_is_doubtful_and_kept_for_mappings(string home, string away, string name)
    {
        using var db = Db();
        var rig = Seed(db, home, away, new DateTime(2026, 10, 27, 19, 30, 0, DateTimeKind.Utc), league: "English Premier League");
        var slot = Channel(db, rig, name);
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = slot.Id, LeagueId = rig.League.Id, IsManual = true, Priority = 1, Confidence = 100,
        });
        db.SaveChanges();

        var resolution = await Svc(db, timeZone: null).ResolveDetailedAsync(rig.Event.Id);

        resolution.ForeignEventChannelIds.Should().NotContain(slot.Id);
        resolution.Candidates.Should().ContainSingle(c => c.ChannelId == slot.Id && c.Source == "league-mapping");
    }

    [Fact]
    public async Task A_lone_stamp_is_read_in_the_zone_where_it_fits()
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        // 19:55 is 95 minutes early in Eastern time, but 35 minutes early in
        // Central time. With no other name from the source, the stamp is read
        // in the zone where it fits. This is on purpose.
        var lone = Channel(db, rig, "(Apple) (MLS) 026 | Salt Lake vs. St. Louis (2026-10-24 19:55:00)");

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().ContainSingle(c => c.ChannelId == lone.Id
            && c.Source == EventChannelResolverService.EventChannelSource);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Europe/London")]
    [InlineData("Europe/Paris")]
    public async Task A_lone_doubleheader_slot_that_tied_zones_split_is_doubtful(string? appZone)
    {
        using var db = Db();
        // 13:05 and 17:05 Eastern summer time on 4 July 2026. Read in
        // Eastern time, the slot fits the early game. Read in Pacific time,
        // it fits the late game. One slot cannot settle which.
        var first = Seed(db, "New York Yankees", "Boston Red Sox", new DateTime(2026, 7, 4, 17, 5, 0, DateTimeKind.Utc),
            league: "Major League Baseball", sport: "Baseball", alternate: "MLB");
        var second = Seed(db, "New York Yankees", "Boston Red Sox", new DateTime(2026, 7, 4, 21, 5, 0, DateTimeKind.Utc),
            league: "Major League Baseball", sport: "Baseball");
        var slot = Channel(db, first, "(MLB) 001 | Boston Red Sox @ New York Yankees (2026-07-04 13:00:00)");

        foreach (var game in new[] { first, second })
        {
            var resolution = await Svc(db, timeZone: appZone).ResolveDetailedAsync(game.Event.Id);
            resolution.Candidates.Should().NotContain(c => c.ChannelId == slot.Id);
            resolution.ForeignEventChannelIds.Should().NotContain(slot.Id);
        }
    }

    [Fact]
    public async Task A_game_of_another_league_near_the_stamp_does_not_take_the_slot()
    {
        using var db = Db();
        var kickoff = new DateTime(2026, 10, 27, 23, 30, 0, DateTimeKind.Utc);
        var mls = Seed(db, "Toronto FC", "New York City FC", kickoff);
        Seed(db, "Toronto Maple Leafs", "New York Rangers", kickoff.AddMinutes(-8),
            league: "National Hockey League", sport: "Ice Hockey");
        var slot = Channel(db, mls, "(MLS) 026 | Toronto vs. New York (2026-10-27 19:25:00)");

        var ranked = await Svc(db).ResolveAsync(mls.Event.Id);

        ranked.Should().ContainSingle(c => c.ChannelId == slot.Id
            && c.Source == EventChannelResolverService.EventChannelSource);
    }

    [Fact]
    public async Task A_network_named_after_a_conference_is_not_another_league()
    {
        using var db = Db();
        db.Leagues.Add(new League { Name = "Southeastern Conference", Sport = "American Football", AlternateName = "SEC" });
        db.SaveChanges();
        var rig = Seed(db, "Alabama Crimson Tide", "Auburn Tigers", new DateTime(2026, 2, 1, 1, 0, 0, DateTimeKind.Utc),
            league: "NCAA Division I Basketball", sport: "Basketball");
        var channel = Channel(db, rig, "US | SEC Network 03 | Alabama Crimson Tide vs. Auburn Tigers (2026-01-31 19:55:00)");

        var resolution = await Svc(db).ResolveDetailedAsync(rig.Event.Id);

        resolution.ForeignEventChannelIds.Should().NotContain(channel.Id);
        resolution.Candidates.Should().ContainSingle(c => c.ChannelId == channel.Id
            && c.Source == EventChannelResolverService.EventChannelSource);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_college_slot_never_names_a_professional_game(bool collegeGameAtTheSameTime)
    {
        using var db = Db();
        var kickoff = new DateTime(2026, 3, 1, 1, 0, 0, DateTimeKind.Utc);
        var pro = Seed(db, "Memphis Grizzlies", "Charlotte Hornets", kickoff,
            league: "National Basketball Association", sport: "Basketball");
        if (collegeGameAtTheSameTime)
        {
            Seed(db, "Memphis Tigers", "Charlotte 49ers", kickoff, league: "NCAA Division I Basketball", sport: "Basketball");
        }
        var slot = Channel(db, pro, "US | ESPN+ 012 | Memphis Grizzlies vs. Charlotte Hornets (2026-02-28 19:55:00)");
        slot.Group = "College Basketball";
        db.SaveChanges();

        var resolution = await Svc(db).ResolveDetailedAsync(pro.Event.Id);

        resolution.ForeignEventChannelIds.Should().Contain(slot.Id);
        resolution.Candidates.Should().NotContain(c => c.ChannelId == slot.Id);
    }

    [Fact]
    public async Task A_college_baseball_slot_never_names_a_major_league_game()
    {
        using var db = Db();
        var pro = Seed(db, "Houston Astros", "Texas Rangers", new DateTime(2026, 5, 2, 0, 10, 0, DateTimeKind.Utc),
            league: "Major League Baseball", sport: "Baseball", alternate: "MLB");
        var slot = Channel(db, pro, "US | Houston Astros vs. Texas Rangers (2026-05-01 20:05:00)");
        slot.Group = "College Baseball";
        db.SaveChanges();

        var resolution = await Svc(db).ResolveDetailedAsync(pro.Event.Id);

        resolution.ForeignEventChannelIds.Should().Contain(slot.Id);
        resolution.Candidates.Should().NotContain(c => c.ChannelId == slot.Id);
    }

    [Fact]
    public async Task Team_codes_without_a_league_word_are_not_evidence()
    {
        using var db = Db();
        // Raptors at Celtics, and on the same night Bruins at Leafs. The user
        // follows only the NBA.
        var rig = Seed(db, "Boston Celtics", "Toronto Raptors", new DateTime(2026, 11, 20, 0, 30, 0, DateTimeKind.Utc),
            league: "National Basketball Association", sport: "Basketball");
        foreach (var team in db.Teams.ToList())
        {
            team.ShortName = team.Name.StartsWith("Boston") ? "BOS" : "TOR";
            team.AlternateName = team.Name.StartsWith("Boston") ? "BOS, Celtics" : "TOR, Raptors";
        }
        db.SaveChanges();
        var slot = Channel(db, rig, "Events | BOS @ TOR (2026-11-19 19:00:00)");

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().NotContain(c => c.ChannelId == slot.Id);
    }

    [Theory]
    [InlineData("MLB | Giants vs. Cardinals (2026-10-25 16:20:00)", false)]
    [InlineData("Events | Giants vs. Cardinals (2026-10-25 16:20:00)", false)]
    [InlineData("NFL | Giants vs. Cardinals (2026-10-25 16:20:00)", true)]
    public async Task Nicknames_need_a_league_word_and_another_league_word_blocks_them(string name, bool evidence)
    {
        using var db = Db();
        // The user follows only the NFL. MLB is not in the database.
        var rig = Seed(db, "New York Giants", "Arizona Cardinals", new DateTime(2026, 10, 25, 20, 25, 0, DateTimeKind.Utc),
            league: "National Football League", sport: "American Football");
        var slot = Channel(db, rig, name);

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Any(c => c.ChannelId == slot.Id && c.Source == EventChannelResolverService.EventChannelSource)
            .Should().Be(evidence);
    }

    [Fact]
    public async Task A_nickname_shared_with_a_team_of_another_sport_is_not_foreign()
    {
        using var db = Db();
        var kickoff = new DateTime(2026, 10, 27, 19, 30, 0, DateTimeKind.Utc);
        var rig = Seed(db, "Wolverhampton Wanderers", "Tottenham Hotspur", kickoff, league: "English Premier League");
        Seed(db, "San Antonio Spurs", "Minnesota Timberwolves", kickoff.AddHours(5),
            league: "National Basketball Association", sport: "Basketball");
        var slot = Channel(db, rig, "UK | Wolves vs. Spurs (2026-10-27 19:25:00)");
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = slot.Id, LeagueId = rig.League.Id, IsManual = true, Priority = 1, Confidence = 100,
        });
        db.SaveChanges();

        var resolution = await Svc(db, timeZone: null).ResolveDetailedAsync(rig.Event.Id);

        resolution.ForeignEventChannelIds.Should().NotContain(slot.Id);
        resolution.Candidates.Should().ContainSingle(c => c.ChannelId == slot.Id && c.Source == "league-mapping");
    }

    [Fact]
    public async Task A_bare_sport_word_does_not_unlock_short_team_names()
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        var slot = Channel(db, rig, "Soccer | Salt Lake vs. St. Louis (2026-10-24 21:25:10)");

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().NotContain(c => c.ChannelId == slot.Id);
    }

    [Theory]
    [InlineData("(Apple) (MLS) 029 | Salt Lake vs. St. Louis (Replay) (2026-10-24 21:25:10)")]
    [InlineData("(Apple) (MLS) 029 | Salt Lake vs. St. Louis Encore (2026-10-24 21:25:10)")]
    public async Task A_league_mapped_replay_slot_is_foreign_for_the_live_game(string name)
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        var replay = Channel(db, rig, name);
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = replay.Id, LeagueId = rig.League.Id, IsManual = true, Priority = 1, Confidence = 100,
        });
        db.SaveChanges();

        var resolution = await Svc(db).ResolveDetailedAsync(rig.Event.Id);

        resolution.ForeignEventChannelIds.Should().Contain(replay.Id);
        resolution.Candidates.Should().NotContain(c => c.ChannelId == replay.Id);
    }

    [Fact]
    public async Task A_large_nearby_list_resolves_quickly_and_correctly()
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        // Three hundred other games the same weekend, with a slot for every
        // tenth one.
        for (var i = 0; i < 300; i++)
        {
            var other = Seed(db, $"Home Club {i}", $"Away Club {i}", MlsKickoff.AddMinutes(i % 120));
            if (i % 10 == 0)
            {
                Channel(db, other, $"(Apple) (MLS) {i:000} | Home Club {i} vs. Away Club {i} (2026-10-24 21:25:00)");
            }
        }
        var named = Channel(db, rig, "(Apple) (MLS) 900 | Salt Lake vs. St. Louis (2026-10-24 21:25:10)");
        var resolver = Svc(db);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var ranked = await resolver.ResolveAsync(rig.Event.Id);
        clock.Stop();

        ranked.Select(c => c.ChannelId).Should().Equal(named.Id);
        // A rough bound. It catches a slip into work per channel and per event
        // pair, not small changes.
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task A_city_pair_named_for_another_league_never_matches()
    {
        using var db = Db();
        db.Leagues.Add(new League { Name = "National Hockey League", Sport = "Ice Hockey" });
        db.SaveChanges();
        var rig = Seed(db, "Toronto FC", "New York City FC", new DateTime(2026, 10, 27, 23, 30, 0, DateTimeKind.Utc));
        Channel(db, rig, "(NHL) 004 | Toronto vs. New York (2026-10-27 19:25:00)");
        var mls = Channel(db, rig, "(MLS) 026 | Toronto vs. New York City (2026-10-27 19:25:00)");

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Select(c => c.ChannelId).Should().Equal(mls.Id);
    }

    [Fact]
    public async Task Celtics_and_knicks_never_match_bruins_and_rangers()
    {
        using var db = Db();
        db.Leagues.Add(new League { Name = "National Basketball Association", Sport = "Basketball" });
        db.SaveChanges();
        var rig = Seed(db, "Boston Bruins", "New York Rangers", new DateTime(2026, 10, 27, 23, 0, 0, DateTimeKind.Utc),
            league: "National Hockey League", sport: "Ice Hockey");
        Channel(db, rig, "(NBA) 001 | Boston Celtics vs. New York Knicks (2026-10-27 19:00:00)");
        Channel(db, rig, "Events 002 | Boston vs. New York (2026-10-27 19:00:00)");
        var hockey = Channel(db, rig, "(NHL) 003 | Boston Bruins vs. New York Rangers (2026-10-27 19:00:00)");

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Select(c => c.ChannelId).Should().Equal(hockey.Id);
    }

    [Fact]
    public async Task Hyphenated_names_keep_both_words()
    {
        using var db = Db();
        var kickoff = new DateTime(2026, 10, 27, 17, 0, 0, DateTimeKind.Utc);
        var other = Seed(db, "Al-Ittihad", "Al-Ahli", kickoff, league: "Saudi Pro League");
        var hilal = Seed(db, "Al-Hilal", "Al-Nassr", kickoff, league: "Saudi Pro League");
        var channel = Channel(db, hilal, "SA | Al-Hilal vs. Al-Nassr (2026-10-27 17:00:00)");

        (await Svc(db).ResolveAsync(other.Event.Id)).Should().BeEmpty();
        (await Svc(db).ResolveAsync(hilal.Event.Id)).Select(c => c.ChannelId).Should().Equal(channel.Id);
    }

    [Theory]
    [InlineData("Saint-Etienne", "FR | Saint-Étienne vs. Lyon (2026-10-27 19:00:00)", true)]
    [InlineData("Saint-Etienne", "FR | St. Etienne vs. Lyon (2026-10-27 19:00:00)", true)]
    [InlineData("St Mirren", "FR | Saint-Etienne vs. Lyon (2026-10-27 19:00:00)", false)]
    public async Task Saint_etienne_is_read_as_two_words(string home, string name, bool matches)
    {
        using var db = Db();
        var rig = Seed(db, home, "Lyon", new DateTime(2026, 10, 27, 19, 0, 0, DateTimeKind.Utc), league: "French Ligue 1");
        Channel(db, rig, name);

        var ranked = await Svc(db, timeZone: null).ResolveAsync(rig.Event.Id);

        ranked.Any(c => c.Source == EventChannelResolverService.EventChannelSource).Should().Be(matches);
    }

    [Fact]
    public async Task A_mapped_channel_still_counts_for_an_event_without_team_names()
    {
        using var db = Db();
        var source = new IptvSource { Name = "Provider", Url = "http://iptv.test/xtream", IsActive = true };
        var league = new League { Name = "Boxing", Sport = "Fighting" };
        db.IptvSources.Add(source);
        db.Leagues.Add(league);
        db.SaveChanges();
        var evt = new Event
        {
            Title = "Canelo vs Crawford",
            Sport = "Fighting",
            LeagueId = league.Id,
            EventDate = new DateTime(2026, 10, 28, 2, 0, 0, DateTimeKind.Utc),
        };
        var ppv = new IptvChannel
        {
            Name = "PPV 01 | Canelo vs. Crawford (2026-10-27 21:00:00)",
            StreamUrl = "http://iptv.test/live/ppv1",
            SourceId = source.Id,
            IsEnabled = true,
        };
        db.Events.Add(evt);
        db.IptvChannels.Add(ppv);
        db.SaveChanges();
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = ppv.Id, LeagueId = league.Id, IsManual = true, Priority = 1, Confidence = 100,
        });
        db.SaveChanges();

        var ranked = await Svc(db).ResolveAsync(evt.Id);

        ranked.Should().ContainSingle(c => c.ChannelId == ppv.Id && c.Source == "league-mapping");
    }

    [Theory]
    // In winter, 15:00 Eastern standard time is 20:00 UTC. A stamp at 14:00
    // reads as 19:00 UTC. A summer-time reading gives 18:00 and fails.
    [InlineData("2026-11-07T20:00:00Z", "2026-11-07 14:00:00")]
    // In summer, 19:00 Eastern summer time is 23:00 UTC. A stamp at 19:10
    // reads as 23:10 UTC. A standard-time reading gives 00:10 and fails.
    [InlineData("2026-07-04T23:00:00Z", "2026-07-04 19:10:00")]
    public async Task The_stamp_is_read_with_the_daylight_saving_rules_of_its_date(string kickoff, string stamp)
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC",
            DateTime.Parse(kickoff, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        var channel = Channel(db, rig, $"(Apple) (MLS) 026 | Salt Lake vs. St. Louis ({stamp})");

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().ContainSingle(c => c.ChannelId == channel.Id && c.Confidence == 95);
    }

    [Theory]
    [InlineData("UK (Paramount 003) | Arsenal Women vs. Chelsea Women (2026-10-27 15:00:00)")]
    [InlineData("UK (Paramount 004) | Arsenal U21 vs. Chelsea U21 (2026-10-27 15:00:00)")]
    [InlineData("UK (Paramount 005) | Arsenal vs. Chelsea (W) (2026-10-27 15:00:00)")]
    [InlineData("UK (Paramount 006) | Arsenal vs. Chelsea Reserves (2026-10-27 15:00:00)")]
    public async Task A_channel_for_another_squad_of_the_same_clubs_is_left_out(string name)
    {
        using var db = Db();
        var rig = Seed(db, "Arsenal", "Chelsea", new DateTime(2026, 10, 27, 19, 30, 0, DateTimeKind.Utc),
            league: "English Premier League");
        Channel(db, rig, name);

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().BeEmpty();
    }

    [Theory]
    [InlineData("(Apple) (MLS) 026 | Salt Lake vs. St. Louis Replay (2026-10-24 21:25:10)")]
    [InlineData("(Apple) (MLS) 026 | Salt Lake vs. St. Louis (Highlights) (2026-10-24 21:25:10)")]
    [InlineData("(Apple) (MLS) 026 | Salt Lake vs. St. Louis (Encore) (2026-10-24 21:25:10)")]
    public async Task A_replay_or_highlights_channel_is_left_out(string name)
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        Channel(db, rig, name);

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_channel_named_for_another_pair_is_left_out_even_when_it_is_mapped()
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff, broadcast: "Apple MLS");
        // Austin plays at the same time, so the slot clearly names that match.
        Seed(db, "Austin FC", "FC Dallas", MlsKickoff);
        var other = Channel(db, rig, "(Apple) (MLS) 028 | Austin vs. Dallas (2026-10-24 21:25:10)");
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = other.Id, LeagueId = rig.League.Id, IsManual = true, IsPreferred = true,
            Priority = 1, Confidence = 100,
        });
        db.SaveChanges();

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().NotContain(c => c.ChannelId == other.Id,
            "a per-event channel airs only the match its name gives");
    }

    [Fact]
    public async Task The_event_channel_beats_a_capped_broadcast_and_mapping_guess()
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff, broadcast: "Apple TV");
        var guess = Channel(db, rig, "US: Apple TV");
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping
        {
            ChannelId = guess.Id, LeagueId = rig.League.Id, IsManual = true, IsPreferred = true,
            Priority = 1, Confidence = 100,
        });
        db.SaveChanges();
        var named = Channel(db, rig, "(Apple) (MLS) 026 |  Salt Lake vs. St. Louis  (2026-10-24 21:25:10)");
        var spanish = Channel(db, rig, "(Apple) (MLS) 027 |  Salt Lake vs. St. Louis (Spanish) (2026-10-24 21:25:15)");

        var ranked = await Svc(db, timeZone: null).ResolveAsync(rig.Event.Id);

        ranked.Select(c => c.ChannelId).Should().Equal(named.Id, spanish.Id, guess.Id);
        ranked.Select(c => c.Confidence).Should().Equal(95, 92, 91);
    }

    [Theory]
    [InlineData("Manchester United", "Arsenal", "US (Paramount 010) | Man. United vs. Arsenal (2026-10-27 15:00:00)")]
    [InlineData("Manchester United", "Arsenal", "US (Paramount 010) | Man Utd v Arsenal (2026-10-27 15:00:00)")]
    [InlineData("New York City FC", "Inter Miami CF", "(Apple) (MLS) 011 | New_York_City vs. Inter_Miami (2026-10-27 15:25:00)")]
    [InlineData("Real Salt Lake", "St. Louis City SC", "(Apple) (MLS) 012 | St. Louis @ Salt Lake (2026-10-27 15:25:00)")]
    [InlineData("Real Salt Lake", "St. Louis City SC", "(MLS) 026 | Salt Lake vs. St. Louis (2026-10-27 15:25:00)")]
    [InlineData("New York City FC", "Inter Miami CF", "(Apple) (MLS) 011 | New_York_City_vs_Inter_Miami (2026-10-27 15:25:00)")]
    public async Task Shortened_and_punctuated_team_names_match(string home, string away, string name)
    {
        using var db = Db();
        var rig = Seed(db, home, away, new DateTime(2026, 10, 27, 19, 30, 0, DateTimeKind.Utc));
        var channel = Channel(db, rig, name);

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().ContainSingle(c => c.ChannelId == channel.Id
            && c.Source == EventChannelResolverService.EventChannelSource);
    }

    [Theory]
    [InlineData("(Apple) (MLS) 026 | Salt Lake vs. St. Louis (2026-11-21 21:25:10)")]
    // A stamp an hour and a half early is too early, even on the right day.
    [InlineData("(Apple) (MLS) 026 | Salt Lake vs. St. Louis (2026-10-24 19:55:00)")]
    public async Task A_rematch_on_another_date_or_time_is_left_out(string name)
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        // The source's copy for this game shows that it writes Eastern time.
        // A lone stamp could fit another zone, such as Central time.
        var current = Channel(db, rig, "(Apple) (MLS) 025 | Salt Lake vs. St. Louis (2026-10-24 21:25:10)");
        var tested = Channel(db, rig, name);

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Select(c => c.ChannelId).Should().Equal(current.Id);
        ranked.Should().NotContain(c => c.ChannelId == tested.Id);
    }

    [Fact]
    public async Task A_channel_shared_by_one_team_only_is_not_a_match()
    {
        using var db = Db();
        var rig = Seed(db, "Real Salt Lake", "St. Louis City SC", MlsKickoff);
        Channel(db, rig, "(Apple) (MLS) 026 | Salt Lake vs. Austin (2026-10-24 21:25:10)");
        Channel(db, rig, "(Apple) (MLS) 027 | United vs. City (2026-10-24 21:25:10)");

        var ranked = await Svc(db).ResolveAsync(rig.Event.Id);

        ranked.Should().BeEmpty();
    }

    [Theory]
    [InlineData("(Apple) (MLS) 026 | Salt Lake vs. St. Louis (2026-10-24 21:25:10)", "Salt Lake", "St. Louis", null)]
    [InlineData("US (Paramount 016) | Fleetwood Town vs. Arsenal (2026-10-27 15:00:05)", "Fleetwood Town", "Arsenal", null)]
    [InlineData("(Apple) (MLS) 027 |  Salt Lake vs. St. Louis (Spanish) (2026-10-24 21:25:15)", "Salt Lake", "St. Louis", "Spanish")]
    [InlineData("X | Alpha v Beta (2026-10-24 21:25)", "Alpha", "Beta", null)]
    [InlineData("US | Fleetwood Town vs. Arsenal (HD) (2026-10-27 15:00:05)", "Fleetwood Town", "Arsenal", null)]
    [InlineData("US | Fleetwood Town vs. Arsenal (Spanish) (FHD) (2026-10-27 15:00:05)", "Fleetwood Town", "Arsenal", "Spanish")]
    [InlineData("X | Alpha_vs_Beta (2026-10-24 21:25)", "Alpha", "Beta", null)]
    [InlineData("US | Fleetwood Town vs. Arsenal (ESPN) (2026-10-27 15:00:05)", "Fleetwood Town", "Arsenal", null)]
    [InlineData("US | Fleetwood Town vs. Arsenal (Live) (2026-10-27 15:00:05)", "Fleetwood Town", "Arsenal", null)]
    [InlineData("UK | Arsenal vs. Chelsea (W) (2026-10-27 15:00:05)", "Arsenal", "Chelsea women", null)]
    [InlineData("NBA | Lakers vs Celtics | Game 7 (2026-06-14 20:30:00)", "Lakers", "Celtics", null)]
    public void The_parser_reads_the_sides_language_and_stamp(string name, string a, string b, string? language)
    {
        var parsed = EventChannelNameMatcher.TryParse(name);

        parsed.Should().NotBeNull();
        parsed!.SideA.Should().Be(a);
        parsed.SideB.Should().Be(b);
        parsed.Language.Should().Be(language);
    }

    [Theory]
    [InlineData("Salt Lake vs. St. Louis (2026-10-24 21:25:10)")]
    [InlineData("(Apple) (MLS) 026 | Salt Lake vs. St. Louis (2026-10-24)")]
    [InlineData("(Apple) (MLS) 026 | Salt Lake vs. St. Louis")]
    [InlineData("(Apple) (MLS) 026 | MLS 360 (2026-10-24 21:25:10)")]
    [InlineData("US: ESPN (HD)")]
    public void The_parser_needs_a_pipe_two_sides_and_a_full_stamp(string name)
    {
        EventChannelNameMatcher.TryParse(name).Should().BeNull();
    }

    [Fact]
    public void Evidence_covers_guide_listings_and_event_channels_only()
    {
        EventChannelResolverService.IsEvidence(EventChannelResolverService.EpgProgramSource).Should().BeTrue();
        EventChannelResolverService.IsEvidence(EventChannelResolverService.EventChannelSource).Should().BeTrue();
        EventChannelResolverService.IsEvidence("broadcast").Should().BeFalse();
        EventChannelResolverService.IsEvidence("league-mapping").Should().BeFalse();
        EventChannelResolverService.IsEvidence(null).Should().BeFalse();
    }
}
