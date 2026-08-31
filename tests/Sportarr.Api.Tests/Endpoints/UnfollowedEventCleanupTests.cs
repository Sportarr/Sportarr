using FluentAssertions;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// "Keep every game in the library" stores games for teams the user does not
/// follow. Turning it off stops new ones arriving but leaves the ones already
/// stored, so the edit dialog offers to remove them. Anything the user asked
/// for by hand has to survive that, because it is outside the filter for
/// exactly the reason they chose.
/// </summary>
public class UnfollowedEventCleanupTests
{
    private static int _nextId = 1;

    private static Event Ev(
        string title,
        string? home = null,
        string? away = null,
        bool manuallyMonitored = false,
        bool hasFile = false,
        string? externalId = "ev-000001") => new()
        {
            Id = _nextId++,
            ExternalId = externalId,
            Title = title,
            Sport = "Sport",
            Season = "2026",
            EventDate = new DateTime(2026, 8, 15),
            HomeTeamExternalId = home,
            AwayTeamExternalId = away,
            ManuallyMonitored = manuallyMonitored,
            Monitored = manuallyMonitored,
            HasFile = hasFile,
        };

    private static LeagueEndpoints.UnfollowedEventSummary Classify(
        List<Event> events, League league, HashSet<int>? busy = null)
    {
        var summary = new LeagueEndpoints.UnfollowedEventSummary { Total = events.Count };
        LeagueEndpoints.ClassifyUnfollowedEvents(
            events, league, busy ?? new HashSet<int>(), new HashSet<int>(), summary);
        return summary;
    }

    private static League TeamLeague(params string[] monitoredTeamExternalIds) => new()
    {
        Name = "NFL",
        Sport = "American Football",
        MonitoredTeams = monitoredTeamExternalIds
            .Select(id => new LeagueTeam { Monitored = true, Team = new Team { ExternalId = id, Name = id } })
            .ToList(),
    };

    [Fact]
    public void OnlyGamesOutsideTheFilterAreRemovable()
    {
        var league = TeamLeague("tm-cowboys");
        var events = new List<Event>
        {
            Ev("New York Giants vs Dallas Cowboys", "tm-giants", "tm-cowboys"),
            Ev("Chicago Bears vs Green Bay Packers", "tm-bears", "tm-packers"),
        };

        var summary = Classify(events, league);

        summary.Removable.Should().ContainSingle()
            .Which.Should().Be(events.Single(e => e.HomeTeamExternalId == "tm-bears").Id);
    }

    [Fact]
    public void AGameTheUserMonitoredByHandIsKept()
    {
        var league = TeamLeague("tm-cowboys");
        var events = new List<Event>
        {
            Ev("New York Giants vs Dallas Cowboys", "tm-giants", "tm-cowboys"),
            Ev("Chicago Bears vs Green Bay Packers", "tm-bears", "tm-packers", manuallyMonitored: true),
        };

        var summary = Classify(events, league);

        summary.Removable.Should().BeEmpty("the user picked this one out of a league they follow by team");
        summary.KeptManuallyMonitored.Should().Be(1);
    }

    [Fact]
    public void AGameDownloadingOrScheduledIsKept()
    {
        var league = TeamLeague("tm-cowboys");
        var busy = Ev("Chicago Bears vs Green Bay Packers", "tm-bears", "tm-packers");
        var followed = Ev("New York Giants vs Dallas Cowboys", "tm-giants", "tm-cowboys");

        var summary = Classify(new List<Event> { busy, followed }, league, new HashSet<int> { busy.Id });

        summary.Removable.Should().BeEmpty("the grab or the recording would land with nowhere to go");
        summary.KeptBusy.Should().Be(1);
    }

    [Fact]
    public void ATeamlessLeagueOffersNothing()
    {
        // The sync stores every session of a motorsport league, and the page
        // hides the ones the user does not follow. Nothing there is ever
        // removable, because removing it would only make the next sync fetch
        // it again.
        var league = new League
        {
            Name = "Formula 1",
            Sport = "Motorsport",
            MonitoredSessionTypes = "Race",
        };
        var events = new List<Event> { Ev("Formula 1 Belgian Grand Prix Practice 1") };

        Classify(events, league)
            .Removable.Should().BeEmpty();
    }

    [Fact]
    public void NothingIsRemovableWhileTheLeagueKeepsEveryGame()
    {
        var league = TeamLeague("tm-cowboys");
        league.KeepAllEvents = true;
        var events = new List<Event>
        {
            Ev("New York Giants vs Dallas Cowboys", "tm-giants", "tm-cowboys"),
            Ev("Chicago Bears vs Green Bay Packers", "tm-bears", "tm-packers"),
        };

        Classify(events, league)
            .Removable.Should().BeEmpty("the sync stores every game while the setting is on");
    }

    [Fact]
    public void ABrokenTeamMappingRemovesNothing()
    {
        // Every event failing the team side of the filter means the team ids
        // do not line up, not that the user follows nobody who plays.
        var league = TeamLeague("tm-does-not-match-anything");
        var events = new List<Event>
        {
            Ev("New York Giants vs Dallas Cowboys", "tm-giants", "tm-cowboys"),
            Ev("Chicago Bears vs Green Bay Packers", "tm-bears", "tm-packers"),
        };

        Classify(events, league)
            .Removable.Should().BeEmpty("a mapping fault must not empty the league");
    }

    [Fact]
    public void AGameWithNoUpstreamIdIsNeverRemovable()
    {
        // Added by hand or created by a library import. No sync at any depth
        // brings it back, so it is not on offer at any price.
        var league = TeamLeague("tm-cowboys");
        var events = new List<Event>
        {
            Ev("New York Giants vs Dallas Cowboys", "tm-giants", "tm-cowboys"),
            Ev("A Game Somebody Added", externalId: null),
        };

        var summary = Classify(events, league);

        summary.Removable.Should().BeEmpty();
        summary.KeptLocalOnly.Should().Be(1);
    }

    [Fact]
    public void AGameWithNoSeasonIsNeverRemovable()
    {
        // Knockout rounds are read from the shape of a whole season, and
        // there is no season here to read.
        var league = TeamLeague("tm-cowboys");
        var seasonless = Ev("Chicago Bears vs Green Bay Packers", "tm-bears", "tm-packers");
        seasonless.Season = null;
        var events = new List<Event>
        {
            Ev("New York Giants vs Dallas Cowboys", "tm-giants", "tm-cowboys"),
            seasonless,
        };

        Classify(events, league).Removable.Should().BeEmpty();
    }

    [Fact]
    public void OneSeasonWithBrokenTeamIdsDoesNotOfferUpAnother()
    {
        // The guard is per season for the same reason the sync applies it per
        // season. A season whose team ids do not line up is a mapping fault.
        var league = TeamLeague("tm-cowboys");
        var legacy = Ev("Old Giants vs Old Cowboys", "12345", "67890");
        legacy.Season = "2019";
        var events = new List<Event>
        {
            Ev("New York Giants vs Dallas Cowboys", "tm-giants", "tm-cowboys"),
            Ev("Chicago Bears vs Green Bay Packers", "tm-bears", "tm-packers"),
            legacy,
        };

        var summary = Classify(events, league);

        summary.Removable.Should().NotContain(legacy.Id, "a mapping fault must not empty a season");
        summary.Removable.Should().ContainSingle();
    }

    [Fact]
    public void AClaimOutlivesAnAutomaticUnmonitor()
    {
        // Switching a league off unmonitors everything in it. Switching it
        // back on must not have quietly made a picked game deletable in
        // between.
        var league = TeamLeague("tm-cowboys");
        var stale = Ev("Chicago Bears vs Green Bay Packers", "tm-bears", "tm-packers", manuallyMonitored: true);
        stale.Monitored = false;
        var events = new List<Event>
        {
            Ev("New York Giants vs Dallas Cowboys", "tm-giants", "tm-cowboys"),
            stale,
        };

        var summary = Classify(events, league);

        summary.Removable.Should().NotContain(stale.Id);
        summary.KeptManuallyMonitored.Should().Be(1);
    }

    [Fact]
    public void TheSignatureNamesTheSetAndNotItsOrder()
    {
        var league = TeamLeague("tm-cowboys");
        var events = new List<Event>
        {
            Ev("New York Giants vs Dallas Cowboys", "tm-giants", "tm-cowboys"),
            Ev("Chicago Bears vs Green Bay Packers", "tm-bears", "tm-packers"),
            Ev("Miami Dolphins vs Buffalo Bills", "tm-dolphins", "tm-bills"),
        };

        var first = Classify(events, league).Signature;
        var reordered = Classify(Enumerable.Reverse(events).ToList(), league).Signature;
        first.Should().Be(reordered, "the same games in another order are the same set");

        events.Add(Ev("Denver Broncos vs Las Vegas Raiders", "tm-broncos", "tm-raiders"));
        Classify(events, league).Signature.Should().NotBe(first, "one more game is a different set");
    }

    [Fact]
    public void AGameSomebodyUnmonitoredIsStillTheirDecision()
    {
        // The claim records that a person decided this event's monitoring,
        // either way, so a sync never argues with it. The clean-up keeps it
        // for the same reason.
        var league = TeamLeague("tm-cowboys");
        var chosen = Ev("Chicago Bears vs Green Bay Packers", "tm-bears", "tm-packers", manuallyMonitored: true);
        chosen.Monitored = false;
        var events = new List<Event>
        {
            Ev("New York Giants vs Dallas Cowboys", "tm-giants", "tm-cowboys"),
            chosen,
        };

        Classify(events, league).Removable.Should().NotContain(chosen.Id);
    }

    [Fact]
    public void ALeagueThatFollowsEveryTeamHasNothingToRemove()
    {
        var league = TeamLeague();
        var events = new List<Event>
        {
            Ev("Chicago Bears vs Green Bay Packers", "tm-bears", "tm-packers"),
        };

        var summary = Classify(events, league);

        summary.Removable.Should().BeEmpty();
    }
}
