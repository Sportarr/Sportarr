using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.Services;

[CollectionDefinition("RSS matching measurements", DisableParallelization = true)]
public class RssMatchingMeasurementCollection;

[Collection("RSS matching measurements")]
public class RssMatchingBenchmarkTests(ITestOutputHelper output)
{
    private delegate Event? FindMatch(ReleaseSearchResult release, List<Event> events,
        ReleaseMatchingService matcher, EventPartDetector partDetector, bool multiPart,
        IReadOnlyDictionary<int, int?> earlyLimits,
        IReadOnlyCollection<League> knownLeagues, IReadOnlyCollection<Event> datePeers,
        IReadOnlyDictionary<(int? LeagueId, string? Season, string? Round), List<int>> roundRaceNumbersByRound);

    [Fact]
    public void MixedFeed_SelectsExpectedEvents_OnFirstAndRepeatPass()
    {
        var fullSize = Environment.GetEnvironmentVariable("SPORTARR_RSS_BENCHMARK") == "1";
        var events = CreateEvents();
        var releases = CreateReleases(fullSize ? 900 : 20);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var rss = new RssSyncService(services, NullLogger<RssSyncService>.Instance);
        var matcher = new ReleaseMatchingService(NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var partDetector = new EventPartDetector(NullLogger<EventPartDetector>.Instance);
        var findMatch = typeof(RssSyncService).GetMethod("FindMatchingEvent",
            BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<FindMatch>(rss);
        var earlyLimits = new Dictionary<int, int?>();
        var knownLeagues = events.Select(evt => evt.League!).DistinctBy(league => league.Id).ToArray();

        output.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; " +
            $"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; " +
            $"architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        output.WriteLine($"Releases: {releases.Count}; events: {events.Count}; pairs/pass: {releases.Count * events.Count}");

        var first = Measure("first", findMatch, releases, events, matcher, partDetector, earlyLimits, knownLeagues);
        var repeat = Measure("repeat", findMatch, releases, events, matcher, partDetector, earlyLimits, knownLeagues);

        first.Should().Equal(releases.Select(release => release.ExpectedId));
        repeat.Should().Equal(first);
    }

    [Fact]
    public void TeamFeed_SelectsExpectedEvents_OnFirstAndRepeatPass()
    {
        var fullSize = Environment.GetEnvironmentVariable("SPORTARR_RSS_BENCHMARK") == "1";
        var events = CreateTeamEvents();
        var releases = CreateTeamReleases(fullSize ? 900 : 20);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var rss = new RssSyncService(services, NullLogger<RssSyncService>.Instance);
        var matcher = new ReleaseMatchingService(NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var partDetector = new EventPartDetector(NullLogger<EventPartDetector>.Instance);
        var findMatch = typeof(RssSyncService).GetMethod("FindMatchingEvent",
            BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<FindMatch>(rss);
        var earlyLimits = new Dictionary<int, int?>();
        var knownLeagues = events.Select(evt => evt.League!).DistinctBy(league => league.Id).ToArray();

        output.WriteLine($"Team releases: {releases.Count}; events: {events.Count}; pairs/pass: {releases.Count * events.Count}");

        var first = Measure("team first", findMatch, releases, events, matcher, partDetector, earlyLimits, knownLeagues);
        var repeat = Measure("team repeat", findMatch, releases, events, matcher, partDetector, earlyLimits, knownLeagues);

        first.Should().Equal(releases.Select(release => release.ExpectedId));
        repeat.Should().Equal(first);
    }

    [Fact]
    public void SupercarsFeedMapsRoundRaceToSeasonRace()
    {
        var league = new League { Id = 18, Name = "Supercars", Sport = "Motorsport" };
        Event Race(int number, int day) => new()
        {
            Id = number,
            Title = $"Century Batteries Ipswich Super 440 - Race {number}",
            Sport = "Motorsport",
            LeagueId = league.Id,
            League = league,
            Season = "2026",
            Round = "9",
            EventDate = new DateTime(2026, 8, day),
            Monitored = true
        };
        var events = new List<Event> { Race(26, 21), Race(27, 22), Race(28, 23) };
        var release = new ReleaseSearchResult
        {
            Title = "Supercars 2026 Round09 Ipswich Race 3 2160p FoxSports WEB DL DD H265 English",
            Guid = "supercars-round09-race3",
            DownloadUrl = "http://test/supercars-round09-race3",
            Indexer = "Test"
        };
        using var services = new ServiceCollection().BuildServiceProvider();
        using var rss = new RssSyncService(services, NullLogger<RssSyncService>.Instance);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var findMatch = typeof(RssSyncService).GetMethod(
            "FindMatchingEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<FindMatch>(rss);

        var result = findMatch(
            release,
            new List<Event> { events[2] },
            matcher,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            true,
            new Dictionary<int, int?>(),
            new[] { league },
            events,
            RoundSchedule(events));

        result.Should().BeSameAs(events[2]);
    }

    [Fact]
    public void TeamFeed_prefers_the_named_day_over_an_unverified_adjacent_game()
    {
        var league = new League { Id = 1, Name = "MLB", Sport = "Baseball" };
        var adjacentGame = new Event
        {
            Id = 1,
            Title = "Detroit Tigers vs Los Angeles Dodgers",
            Sport = "Baseball",
            LeagueId = league.Id,
            League = league,
            HomeTeamId = 10,
            AwayTeamId = 11,
            HomeTeamName = "Detroit Tigers",
            AwayTeamName = "Los Angeles Dodgers",
            EventDate = new DateTime(2026, 8, 29, 17, 10, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 8, 29),
            BroadcastDateVerified = false,
            Monitored = true
        };
        var namedGame = new Event
        {
            Id = 2,
            Title = "Los Angeles Dodgers vs Detroit Tigers",
            Sport = "Baseball",
            LeagueId = league.Id,
            League = league,
            HomeTeamId = 11,
            AwayTeamId = 10,
            HomeTeamName = "Los Angeles Dodgers",
            AwayTeamName = "Detroit Tigers",
            EventDate = new DateTime(2026, 8, 28, 17, 10, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 8, 28),
            BroadcastDateVerified = false,
            Monitored = false
        };
        var release = new ReleaseSearchResult
        {
            Title = "MLB RS 2026 Los Angeles Dodgers vs Detroit Tigers 28 08 1080pEN60fps SNLA",
            Guid = "mlb-2026-08-28",
            DownloadUrl = "http://test/mlb-2026-08-28",
            Indexer = "Test"
        };
        using var services = new ServiceCollection().BuildServiceProvider();
        using var rss = new RssSyncService(services, NullLogger<RssSyncService>.Instance);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var findMatch = typeof(RssSyncService).GetMethod(
            "FindMatchingEvent",
            BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<FindMatch>(rss);

        var result = findMatch(
            release,
            new List<Event> { adjacentGame },
            matcher,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            true,
            new Dictionary<int, int?>(),
            new[] { league },
            new[] { adjacentGame, namedGame },
            RoundSchedule(Array.Empty<Event>()));

        result.Should().BeNull();
    }

    [Fact]
    public void MultipartWrestlingSidePackageCanReachTheMonitoredEvent()
    {
        var league = new League { Id = 10, Name = "AEW", Sport = "Wrestling" };
        var evt = new Event
        {
            Id = 10,
            Title = "Forbidden Door",
            Sport = "Wrestling",
            LeagueId = league.Id,
            League = league,
            EventDate = new DateTime(2026, 6, 28, 20, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 6, 28),
            BroadcastDateVerified = true,
            Monitored = true
        };
        var release = new ReleaseSearchResult
        {
            Title = "AEW.Forbidden.Door.2026.Zero.Hour.1080p.WEB.H264-GROUP",
            Guid = "aew-zero-hour",
            DownloadUrl = "http://fixture.invalid/aew-zero-hour",
            Indexer = "Fixture"
        };
        using var services = new ServiceCollection().BuildServiceProvider();
        using var rss = new RssSyncService(services, NullLogger<RssSyncService>.Instance);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance));
        var findMatch = typeof(RssSyncService).GetMethod(
            "FindMatchingEvent",
            BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<FindMatch>(rss);

        findMatch(
                release,
                new List<Event> { evt },
                matcher,
                new EventPartDetector(NullLogger<EventPartDetector>.Instance),
                true,
                new Dictionary<int, int?>(),
                new[] { league },
                Array.Empty<Event>(),
                RoundSchedule(Array.Empty<Event>()))
            .Should().BeSameAs(evt);
    }

    [Fact]
    public void WeeklyWrestlingReleaseFromAnAdjacentDateCannotReachRss()
    {
        var league = new League { Id = 11, Name = "WWE", Sport = "Combat" };
        var evt = new Event
        {
            Id = 11,
            Title = "RAW #1724",
            Sport = "Combat",
            LeagueId = league.Id,
            League = league,
            EventDate = new DateTime(2026, 6, 28, 20, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 6, 28),
            BroadcastDateVerified = true,
            Monitored = true
        };
        var release = new ReleaseSearchResult
        {
            Title = "WWE.RAW.2026.06.27.1080p.WEB.H264-GROUP",
            Guid = "wwe-adjacent-date",
            DownloadUrl = "http://fixture.invalid/wwe-adjacent-date",
            Indexer = "Fixture"
        };
        using var services = new ServiceCollection().BuildServiceProvider();
        using var rss = new RssSyncService(services, NullLogger<RssSyncService>.Instance);
        var partDetector = new EventPartDetector(NullLogger<EventPartDetector>.Instance);
        var matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
            partDetector);
        var findMatch = typeof(RssSyncService).GetMethod(
            "FindMatchingEvent",
            BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<FindMatch>(rss);

        findMatch(
            release,
            new List<Event> { evt },
            matcher,
            partDetector,
            true,
            new Dictionary<int, int?>(),
            new[] { league },
            Array.Empty<Event>(),
            RoundSchedule(Array.Empty<Event>())).Should().BeNull();
    }

    private int?[] Measure(string pass, FindMatch findMatch,
        List<(ReleaseSearchResult Release, int? ExpectedId)> releases, List<Event> events,
        ReleaseMatchingService matcher, EventPartDetector partDetector,
        IReadOnlyDictionary<int, int?> earlyLimits,
        IReadOnlyCollection<League> knownLeagues)
    {
        using var process = Process.GetCurrentProcess();
        var results = new int?[releases.Count];
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var cpuBefore = process.TotalProcessorTime;
        var timer = Stopwatch.StartNew();
        var roundSchedule = RoundSchedule(Array.Empty<Event>());
        for (var index = 0; index < releases.Count; index++)
        {
            results[index] = findMatch(
                releases[index].Release,
                events,
                matcher,
                partDetector,
                true,
                earlyLimits,
                knownLeagues,
                events,
                roundSchedule)?.Id;
        }
        timer.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var cpu = process.TotalProcessorTime - cpuBefore;
        output.WriteLine($"{pass}: elapsed={timer.Elapsed.TotalMilliseconds:F1} ms; " +
            $"process CPU={cpu.TotalMilliseconds:F1} ms; thread allocations={allocated:N0} bytes; " +
            $"matches={results.Count(result => result.HasValue)}");
        return results;
    }

    private static IReadOnlyDictionary<(int? LeagueId, string? Season, string? Round), List<int>> RoundSchedule(
        IEnumerable<Event> events) => events
        .Where(evt => evt.League?.Name.Contains("Supercars", StringComparison.OrdinalIgnoreCase) == true)
        .GroupBy(evt => (evt.LeagueId, evt.Season, evt.Round))
        .ToDictionary(
            group => group.Key,
            group => ReleaseMatchingService.RaceNumbersInTitles(group.Select(evt => evt.Title)));

    private static List<Event> CreateEvents()
    {
        var venues = new[]
        {
            ("China", "Chinese"), ("Canada", "Canadian"), ("Bahrain", "Bahrain"),
            ("Australia", "Australian"), ("Japan", "Japanese"), ("Miami", "Miami"),
            ("Monaco", "Monaco"), ("Spain", "Spanish"), ("Austria", "Austrian"),
            ("Britain", "British"), ("Hungary", "Hungarian"), ("Belgium", "Belgian"),
            ("Netherlands", "Dutch"), ("Italy", "Italian"), ("Azerbaijan", "Azerbaijan"),
            ("Singapore", "Singapore"), ("Mexico", "Mexican"), ("Brazil", "Brazilian"),
            ("Qatar", "Qatar"), ("Abu Dhabi", "Abu Dhabi"), ("Las Vegas", "Las Vegas"),
            ("Saudi Arabia", "Saudi Arabian"), ("United States", "United States")
        };
        var league = new League { Id = 1, Name = "Formula 1", Sport = "Motorsport" };
        var events = new List<Event>();
        foreach (var (location, adjective) in venues)
        {
            foreach (var session in new[] { "Practice 1", "Qualifying", "Race" })
            {
                events.Add(new Event
                {
                    Id = events.Count + 1, Title = $"{adjective} Grand Prix - {session}",
                    Sport = "Motorsport", League = league, LeagueId = league.Id,
                    Location = location, Season = "2024", Monitored = true,
                    EventDate = new DateTime(2024, 3, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(events.Count)
                });
            }
        }
        foreach (var session in new[] { "Practice 2", "Practice 3" })
        {
            events.Add(new Event
            {
                Id = events.Count + 1, Title = $"Bahrain Grand Prix - {session}",
                Sport = "Motorsport", League = league, LeagueId = league.Id,
                Location = "Bahrain", Season = "2024", Monitored = true,
                EventDate = new DateTime(2024, 3, 2, 12, 0, 0, DateTimeKind.Utc)
            });
        }
        return events;
    }

    private static List<(ReleaseSearchResult Release, int? ExpectedId)> CreateReleases(int count)
    {
        var samples = new (string Title, int? ExpectedId)[]
        {
            ("Formula1.2024.China.Grand.Prix.Qualifying.1080p.WEB.h264", 2),
            ("Formula1.2024.Canada.Grand.Prix.Race.2160p.WEB.h265", 6),
            ("NBA.2024.03.02.Lakers.vs.Celtics.1080p.WEB.h264", null),
            ("NHL.2024.03.02.Bruins.vs.Canadiens.720p.WEB.h264", null),
            ("UFC.299.Main.Card.1080p.WEB.h264", null),
            ("MotoGP.2024.Qatar.Race.1080p.WEB.h264", null),
            ("The.Example.Show.S02E03.1080p.WEB.h264", null),
            ("Example.Movie.2024.1080p.BluRay.x264", null),
            ("Formula1.2023.China.Grand.Prix.Qualifying.1080p.WEB.h264", null),
            ("Formula1.2024.Canada.Grand.Prix.Race.720p.WEB.h264", 6)
        };
        return Enumerable.Range(0, count).Select(index =>
        {
            var sample = samples[index % samples.Length];
            return (new ReleaseSearchResult
            {
                Title = $"{sample.Title}-BENCH{index:D4}", Guid = $"benchmark-{index}",
                DownloadUrl = $"https://example.invalid/releases/{index}", Indexer = "Synthetic"
            }, sample.ExpectedId);
        }).ToList();
    }

    private static List<Event> CreateTeamEvents()
    {
        var teams = new[]
        {
            ("Athletics", "Toronto Blue Jays"),
            ("New York Yankees", "Boston Red Sox"),
            ("Baltimore Orioles", "Tampa Bay Rays"),
            ("Cleveland Guardians", "Detroit Tigers"),
            ("Kansas City Royals", "Minnesota Twins"),
            ("Houston Astros", "Texas Rangers"),
            ("Seattle Mariners", "Los Angeles Angels"),
            ("Atlanta Braves", "Miami Marlins"),
            ("New York Mets", "Philadelphia Phillies"),
            ("Washington Nationals", "Chicago Cubs"),
            ("Cincinnati Reds", "Milwaukee Brewers"),
            ("Pittsburgh Pirates", "St Louis Cardinals"),
            ("Arizona Diamondbacks", "Colorado Rockies"),
            ("Los Angeles Dodgers", "San Diego Padres"),
            ("San Francisco Giants", "Chicago White Sox")
        };
        var league = new League { Id = 2, Name = "MLB", Sport = "Baseball" };
        var events = new List<Event>();

        for (var day = 0; day < 6; day++)
        {
            foreach (var (home, away) in teams)
            {
                var date = new DateTime(2026, 9, 1).AddDays(day);
                events.Add(new Event
                {
                    Id = 1000 + events.Count,
                    Title = $"{home} vs {away}",
                    Sport = "Baseball",
                    League = league,
                    LeagueId = league.Id,
                    HomeTeamName = home,
                    AwayTeamName = away,
                    Season = "2026",
                    Monitored = true,
                    EventDate = DateTime.SpecifyKind(date.AddHours(23), DateTimeKind.Utc),
                    BroadcastDate = date,
                    BroadcastDateVerified = true
                });
            }
        }

        return events;
    }

    private static List<(ReleaseSearchResult Release, int? ExpectedId)> CreateTeamReleases(int count)
    {
        var samples = new (string Title, int? ExpectedId)[]
        {
            ("MLB.2026.09.01.Athletics.vs.Toronto.Blue.Jays.1080p.WEB.h264", 1000),
            ("MLB.2026.09.01.Athletic.vs.Toronto.Blue.Jays.1080p.WEB.h264", 1000),
            ("Toronto.Blue.Jays.vs.Athletic.2026.09.01.1080p.WEB.h264", 1000),
            ("MLB.2026.09.02.New.York.Yankees.vs.Boston.Red.Sox.720p.HDTV.x264", 1016),
            ("MLB.2026.09.03.Los.Angeles.Dodgers.vs.San.Diego.Padres.1080p.WEB.h264", 1043),
            ("MLB.2026.09.04.Seattle.Mariners.vs.Los.Angeles.Angels.1080p.WEB.h264", 1051),
            ("NBA.2026.09.01.Lakers.vs.Celtics.1080p.WEB.h264", null),
            ("LaLiga.2026.09.01.Athletic.Bilbao.vs.Barcelona.1080p.WEB.h264", null),
            ("MLB.2025.09.01.Athletics.vs.Toronto.Blue.Jays.1080p.WEB.h264", null),
            ("Example.Movie.2026.1080p.BluRay.x264", null)
        };

        return Enumerable.Range(0, count).Select(index =>
        {
            var sample = samples[index % samples.Length];
            return (new ReleaseSearchResult
            {
                Title = $"{sample.Title}-TEAMBENCH{index:D4}",
                Guid = $"team-benchmark-{index}",
                DownloadUrl = $"https://example.invalid/team-releases/{index}",
                Indexer = "Synthetic"
            }, sample.ExpectedId);
        }).ToList();
    }
}
