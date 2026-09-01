using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Follow-up to issue #256. The release names in these tests are the ones
/// from the report, and the event dates are the ones the metadata holds for
/// those games. None of them cross a UTC day boundary, so the rollover fix
/// was not what put them a day off. Three other things did: the candidate
/// search never reached the named game in a league that plays every day,
/// the scorer let "no file yet" outweigh the exact date, and the MLB name
/// pattern rejected any three-word club.
/// </summary>
public class ImportNeighbourGameTests
{
    private const string RedSoxAug12 = "mlb.2026.08.12.boston.red.sox.vs.toronto.blue.jays.1080p.web.h264-nightninjas";
    private const string RedSoxAug13 = "mlb.2026.08.13.boston.red.sox.vs.toronto.blue.jays.1080p.web.h264-nightninjas";
    private const string MarinersAug30 = "MLB.2026.08.30.Seattle.Mariners.vs.Toronto.Blue.Jays.1080p.WEB.h264-NiGHTNiNJAS";

    private sealed class Rig
    {
        public SportarrDbContext Db { get; }
        public ImportMatchingService Service { get; }
        public SportsFileNameParser Parser { get; }
        private readonly League _league;
        private int _nextId = 1;

        public Rig()
        {
            var options = new DbContextOptionsBuilder<SportarrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            Db = new SportarrDbContext(options);
            _league = new League { Id = 1, Name = "MLB", Sport = "Baseball" };
            Db.Leagues.Add(_league);
            Parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
            Service = new ImportMatchingService(
                Db,
                new MediaFileParser(NullLogger<MediaFileParser>.Instance),
                Parser,
                new EventPartDetector(NullLogger<EventPartDetector>.Instance),
                NullLogger<ImportMatchingService>.Instance);
        }

        public Event Game(string title, DateTime utcStart, bool hasFile = false, bool verified = true)
        {
            var evt = new Event
            {
                Id = _nextId++,
                Title = title,
                Sport = "Baseball",
                EventDate = utcStart,
                BroadcastDate = utcStart.Date,
                BroadcastDateVerified = verified,
                HomeTeamId = 10,
                AwayTeamId = 20,
                Season = "2026",
                LeagueId = 1,
                League = _league,
                HasFile = hasFile
            };
            Db.Events.Add(evt);
            return evt;
        }

        public void Save() => Db.SaveChanges();

        public async Task<Event?> Best(string release)
        {
            var suggestion = await Service.FindBestMatchAsync(release, "/downloads/" + release + ".mkv");
            return suggestion?.EventId is int id ? await Db.Events.FindAsync(id) : null;
        }
    }

    private static readonly string[] OtherGames =
    {
        "New York Mets vs Atlanta Braves", "Los Angeles Dodgers vs San Diego Padres",
        "Chicago Cubs vs St. Louis Cardinals", "Houston Astros vs Texas Rangers",
        "Seattle Mariners vs Oakland Athletics", "Cleveland Guardians vs Detroit Tigers",
        "Minnesota Twins vs Kansas City Royals", "Philadelphia Phillies vs Miami Marlins",
        "Milwaukee Brewers vs Pittsburgh Pirates", "San Francisco Giants vs Arizona Diamondbacks",
        "Tampa Bay Rays vs Baltimore Orioles", "Colorado Rockies vs Cincinnati Reds",
        "Washington Nationals vs Chicago White Sox", "Los Angeles Angels vs Boston Red Sox"
    };

    private static Rig BlueJaysSeries(Func<int, bool> hasFileOnDay)
    {
        var rig = new Rig();
        const string redSox = "Toronto Blue Jays vs Boston Red Sox";
        rig.Game(redSox, new DateTime(2026, 8, 10, 23, 7, 0), hasFileOnDay(10));
        rig.Game(redSox, new DateTime(2026, 8, 11, 23, 7, 0), hasFileOnDay(11));
        rig.Game(redSox, new DateTime(2026, 8, 12, 23, 7, 0), hasFileOnDay(12));
        rig.Game(redSox, new DateTime(2026, 8, 13, 19, 7, 0), hasFileOnDay(13));
        rig.Game("Toronto Blue Jays vs New York Yankees", new DateTime(2026, 8, 14, 23, 15, 0), hasFileOnDay(14));
        rig.Game("Toronto Blue Jays vs New York Yankees", new DateTime(2026, 8, 15, 19, 7, 0), hasFileOnDay(15));
        rig.Save();
        return rig;
    }

    [Fact]
    public async Task Reimporting_over_an_earlier_mistake_keeps_the_game_the_name_dates()
    {
        // The wrong file from before already sits on 12 and 13 August, and
        // the neighbours are empty. The date in the name still decides.
        var rig = BlueJaysSeries(day => day is 12 or 13);

        (await rig.Best(RedSoxAug12))!.BroadcastDate.Should().Be(new DateTime(2026, 8, 12));
        (await rig.Best(RedSoxAug13))!.BroadcastDate.Should().Be(new DateTime(2026, 8, 13));
    }

    [Fact]
    public async Task A_grab_dated_the_next_day_imports_to_the_next_day()
    {
        var rig = new Rig();
        const string mariners = "Toronto Blue Jays vs Seattle Mariners";
        rig.Game(mariners, new DateTime(2026, 8, 28, 23, 15, 0));
        rig.Game(mariners, new DateTime(2026, 8, 29, 19, 7, 0));
        rig.Game(mariners, new DateTime(2026, 8, 30, 17, 37, 0), hasFile: true);
        rig.Save();

        (await rig.Best(MarinersAug30))!.BroadcastDate.Should().Be(new DateTime(2026, 8, 30));
    }

    [Fact]
    public async Task A_league_that_plays_every_day_still_offers_the_named_game()
    {
        var rig = new Rig();
        for (var day = new DateTime(2026, 8, 1); day <= new DateTime(2026, 9, 18); day = day.AddDays(1))
        {
            foreach (var title in OtherGames)
            {
                rig.Game(title, day.AddHours(23));
            }
        }
        const string redSox = "Toronto Blue Jays vs Boston Red Sox";
        rig.Game(redSox, new DateTime(2026, 8, 11, 23, 7, 0));
        var wanted = rig.Game(redSox, new DateTime(2026, 8, 12, 23, 7, 0));
        rig.Game(redSox, new DateTime(2026, 8, 13, 19, 7, 0));
        rig.Save();

        (await rig.Best(RedSoxAug12))!.Id.Should().Be(wanted.Id);

        var offered = await rig.Service.GetAllPossibleMatchesAsync(RedSoxAug12);
        offered.Select(o => o.EventId).Should().Contain(wanted.Id,
            "the manual import list has to show the game the file names");
    }

    [Fact]
    public async Task A_date_crowded_by_other_leagues_still_offers_the_named_game()
    {
        var rig = new Rig();
        var hockey = new League { Id = 2, Name = "NHL", Sport = "Ice Hockey" };
        rig.Db.Leagues.Add(hockey);
        // More games on the named date than the same-day pass keeps, all
        // from another league and all earlier in the day.
        for (var i = 0; i < 45; i++)
        {
            rig.Db.Events.Add(new Event
            {
                Id = 1000 + i,
                Title = $"Club {i} vs Club {i + 100}",
                Sport = "Ice Hockey",
                EventDate = new DateTime(2026, 8, 12, 0, 5, 0).AddMinutes(i),
                BroadcastDate = new DateTime(2026, 8, 12),
                Season = "2026",
                LeagueId = 2,
                League = hockey
            });
        }
        const string redSox = "Toronto Blue Jays vs Boston Red Sox";
        rig.Game(redSox, new DateTime(2026, 8, 11, 23, 7, 0));
        var wanted = rig.Game(redSox, new DateTime(2026, 8, 12, 23, 7, 0));
        rig.Game(redSox, new DateTime(2026, 8, 13, 19, 7, 0));
        rig.Save();

        (await rig.Best(RedSoxAug12))!.Id.Should().Be(wanted.Id);

        var offered = await rig.Service.GetAllPossibleMatchesAsync(RedSoxAug12);
        offered.Select(o => o.EventId).Should().Contain(wanted.Id);
    }

    [Fact]
    public void A_verified_fixture_a_day_off_is_another_game()
    {
        var rig = new Rig();
        var verified = rig.Game("Toronto Blue Jays vs Boston Red Sox", new DateTime(2026, 8, 11, 23, 7, 0));
        var legacy = rig.Game("Toronto Blue Jays vs Boston Red Sox", new DateTime(2026, 8, 11, 23, 7, 0), verified: false);
        rig.Save();

        var parsed = rig.Parser.Parse(RedSoxAug12);

        rig.Service.CalculateMatchConfidence(parsed.EventTitle!, verified.Title, null, verified, parsed)
            .Should().BeLessOrEqualTo(0, "a series plays the same clubs the day before");
        rig.Service.CalculateMatchConfidence(parsed.EventTitle!, legacy.Title, null, legacy, parsed)
            .Should().BePositive("an unverified date may still hold the UTC day, so the grace stays");
    }

    [Fact]
    public async Task Recency_and_a_missing_file_only_break_ties()
    {
        // Unverified dates keep the one-day grace, so nothing vetoes the
        // neighbour here. It is empty and the named game is not. The
        // evidence in the name ranks first, so the named game still leads,
        // and the neighbour only collects the tie-break points.
        var rig = new Rig();
        const string redSox = "Toronto Blue Jays vs Boston Red Sox";
        var neighbour = rig.Game(redSox, new DateTime(2026, 8, 11, 23, 7, 0), verified: false);
        var named = rig.Game(redSox, new DateTime(2026, 8, 12, 23, 7, 0), hasFile: true, verified: false);
        rig.Save();
        var parsed = rig.Parser.Parse(RedSoxAug12);

        var namedScore = rig.Service.ScoreMatch(parsed.EventTitle!, named.Title, null, named, parsed);
        var neighbourScore = rig.Service.ScoreMatch(parsed.EventTitle!, neighbour.Title, null, neighbour, parsed);

        namedScore.Core.Should().BeGreaterThan(neighbourScore.Core);
        neighbourScore.TieBreak.Should().BeGreaterThan(namedScore.TieBreak);
        (await rig.Best(RedSoxAug12))!.Id.Should().Be(named.Id);
    }

    [Fact]
    public async Task A_legacy_date_a_day_out_is_still_offered_when_nothing_closer_exists()
    {
        // Before its next sync a legacy row may hold the UTC day, one day
        // after the date in the name. With no closer candidate the grace
        // still has to carry it over the suggestion threshold.
        var rig = new Rig();
        var legacy = rig.Game("Toronto Blue Jays vs Boston Red Sox", new DateTime(2026, 8, 13, 0, 7, 0), verified: false);
        rig.Save();

        (await rig.Best(RedSoxAug12))!.Id.Should().Be(legacy.Id);
    }

    [Fact]
    public void The_named_game_clears_the_threshold_on_the_evidence_alone()
    {
        // The name puts the away side first and the title the home side.
        // That is the same fixture, and with the date it has to pass the
        // suggestion gate without help from recency or an empty slot.
        var rig = new Rig();
        var named = rig.Game("Toronto Blue Jays vs Boston Red Sox", new DateTime(2026, 8, 12, 23, 7, 0), hasFile: true);
        rig.Save();
        var parsed = rig.Parser.Parse(RedSoxAug12);

        rig.Service.ScoreMatch(parsed.EventTitle!, named.Title, null, named, parsed).Core.Should().BeGreaterOrEqualTo(50);
    }

    [Fact]
    public async Task A_legacy_library_in_a_busy_league_still_reaches_the_game()
    {
        // Every row is unverified, and a west-coast night game holds the
        // UTC day, one after the date in the name. The other games on
        // those days must not crowd it out of the candidates.
        var rig = new Rig();
        for (var day = new DateTime(2026, 8, 1); day <= new DateTime(2026, 8, 31); day = day.AddDays(1))
        {
            foreach (var title in OtherGames)
            {
                rig.Game(title, day.AddHours(23), verified: false);
            }
        }
        var wanted = rig.Game("Toronto Blue Jays vs Boston Red Sox", new DateTime(2026, 8, 13, 2, 10, 0), verified: false);
        rig.Save();

        (await rig.Best(RedSoxAug12))!.Id.Should().Be(wanted.Id);
    }

    [Fact]
    public void A_nickname_only_name_clears_the_threshold_on_the_evidence_alone()
    {
        var rig = new Rig();
        var named = rig.Game("Pittsburgh Pirates vs Chicago Cubs", new DateTime(2026, 7, 25, 23, 5, 0), hasFile: true);
        rig.Save();
        var parsed = rig.Parser.Parse("MLB.2026.07.25.Cubs.vs.Pirates.720p.WEB.h264-GRP");

        rig.Service.ScoreMatch(parsed.EventTitle!, named.Title, null, named, parsed).Core.Should().BeGreaterOrEqualTo(50);
    }

    [Fact]
    public async Task A_dateless_name_prefers_the_leg_it_names()
    {
        // No date in the name, and the library holds both legs. The order
        // in the name is the only thing that tells them apart, and it has
        // to beat the empty return leg.
        var rig = new Rig();
        var soccer = new League { Id = 3, Name = "Premier League", Sport = "Soccer" };
        rig.Db.Leagues.Add(soccer);
        Event Leg(int id, string title, DateTime utc, bool hasFile) => new()
        {
            Id = id, Title = title, Sport = "Soccer", EventDate = utc, BroadcastDate = utc.Date,
            BroadcastDateVerified = true, Season = "2026", LeagueId = 3, League = soccer, HasFile = hasFile
        };
        var named = Leg(2000, "Arsenal vs Chelsea", new DateTime(2026, 11, 7, 15, 0, 0), hasFile: true);
        rig.Db.Events.Add(named);
        rig.Db.Events.Add(Leg(2001, "Chelsea vs Arsenal", new DateTime(2026, 3, 14, 15, 0, 0), hasFile: false));
        rig.Save();

        (await rig.Best("Soccer.Arsenal.vs.Chelsea.720p.HDTV.x264-GRP"))!.Id.Should().Be(named.Id);
    }

    [Theory]
    [InlineData(RedSoxAug12, "2026-08-12", "boston red sox vs toronto blue jays")]
    [InlineData(MarinersAug30, "2026-08-30", "seattle mariners vs toronto blue jays")]
    [InlineData("MLB.2026.07.12.New.York.Yankees.vs.Washington.Nationals.ev-483957.MLBTV.1080p.HFR.AAC.2.0.H.264-UMBR3LLA", "2026-07-12", "new york yankees vs washington nationals")]
    [InlineData("MLB.2026.07.25.Cubs.vs.Pirates.720p.WEB.h264-GRP", "2026-07-25", "cubs vs pirates")]
    [InlineData("MLB.2026.08.12.Cubs.vs.Atlanta.Braves.ESPN.1080p.WEB.h264-GRP", "2026-08-12", "cubs vs atlanta braves")]
    [InlineData("MLB.2026.07.12.LOS.ANGELES.vs.SAN.DIEGO.1080p.WEB.h264-GRP", "2026-07-12", "los angeles vs san diego")]
    [InlineData("MLB.2026.07.12.Cubs.vs.Red.Sox.web.dl.1080p-GRP", "2026-07-12", "cubs vs red sox")]
    [InlineData("MLB.2026.07.12.Toronto.Blue.Jays.vs.Boston.Red.Sox.Sportsnet.1080p-GRP", "2026-07-12", "toronto blue jays vs boston red sox")]
    [InlineData("MLB.2026.07.25.Cubs.vs.Pirates", "2026-07-25", "cubs vs pirates")]
    [InlineData("MLB.2026.07.12.Yankees.vs.Red.Sox.Sportsnet.1080p-GRP", "2026-07-12", "yankees vs red sox")]
    [InlineData("MLB.2026.07.12.Cubs.vs.Pirates-Bally-1080p", "2026-07-12", "cubs vs pirates")]
    [InlineData("MLB.2026.07.12.Tampa.Bay.Rays.vs.Kansas.City.Royals.BSKC.1080p-GRP", "2026-07-12", "tampa bay rays vs kansas city royals")]
    [InlineData("MLB.2026.07.12.Athletics.vs.Chicago.White.Sox.MLB.TV.720p.AAC.2.0.H.264-GRP", "2026-07-12", "athletics vs chicago white sox")]
    public void Clubs_of_up_to_three_words_parse_as_a_dated_fixture(string release, string date, string fixture)
    {
        var parsed = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance).Parse(release);

        parsed.Organization.Should().Be("MLB");
        parsed.EventDate.Should().Be(DateTime.Parse(date));
        parsed.EventTitle!.ToLowerInvariant().Should().EndWith(fixture);
    }
}
