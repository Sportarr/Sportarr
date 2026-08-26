using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A session label has to survive the whole way round, not just be readable
/// from an event title.
///
/// The event side reads a label from the title. The release side reads one
/// from a filename, and it sweeps every series' patterns in order, so an
/// earlier series often answers first. Both sides are then normalised and
/// compared, and a difference is a hard rejection. A label that only one side
/// can produce therefore rejects the release that belongs to the event, which
/// looks like the release is missing.
///
/// Event titles below are real, taken from the 2026 calendars. Filenames are
/// shaped the way indexers publish them.
/// </summary>
public class MotorsportSessionRoundTripTests
{
    public static TheoryData<string, string, string> Sessions() => new()
    {
        { "IndyCar Series", "Firestone Grand Prix of St. Petersburg Practice 1", "IndyCar.2026.St.Petersburg.Practice.1.1080p.WEB-GRP" },
        { "IndyCar Series", "110th Running of the Indianapolis 500 Practice 7", "IndyCar.2026.Indy500.Practice.7.1080p.WEB-GRP" },
        { "IndyCar Series", "110th Running of the Indianapolis 500 Final Practice", "IndyCar.2026.Indy500.Final.Practice.1080p.WEB-GRP" },
        { "IndyCar Series", "Good Ranchers 250 High Line and Final Practice", "IndyCar.2026.Good.Ranchers.250.Final.Practice.1080p.WEB-GRP" },
        { "IndyCar Series", "110th Running of the Indianapolis 500 Fast Friday", "IndyCar.2026.Indy500.Fast.Friday.1080p.WEB-GRP" },
        { "IndyCar Series", "110th Running of the Indianapolis 500 Qualifying 1", "IndyCar.2026.Indy500.Qualifying.1.1080p.WEB-GRP" },
        { "IndyCar Series", "Acura Grand Prix of Long Beach Qualifying", "IndyCar.2026.Long.Beach.Qualifying.1080p.WEB-GRP" },
        { "IndyCar Series", "Snap On Milwaukee 250 Race #1", "IndyCar.2026.Milwaukee.250.Race.1.1080p.WEB-GRP" },

        { "WEC", "Qatar 1812 KM Free Practice 1", "WEC.2026.Qatar.1812.KM.Free.Practice.1.1080p.WEB-GRP" },
        { "WEC", "Qatar 1812 KM Free Practice 3", "WEC.2026.Qatar.1812.KM.Free.Practice.3.1080p.WEB-GRP" },
        { "WEC", "Qatar 1812 KM Qualifying - LMGT3", "WEC.2026.Qatar.1812.KM.Qualifying.1080p.WEB-GRP" },
        { "WEC", "Qatar 1812 KM Hyperpole - Hypercar", "WEC.2026.Qatar.1812.KM.Hyperpole.1080p.WEB-GRP" },

        { "Formula E", "Jeddah E Prix Free Practice 1", "FormulaE.2026.Jeddah.E.Prix.Free.Practice.1.1080p.WEB-GRP" },
        { "Formula E", "Jeddah E Prix Qualifying", "FormulaE.2026.Jeddah.E.Prix.Qualifying.1080p.WEB-GRP" },

        { "V8 Supercars", "Repco Bathurst 1000 Practice 3", "Supercars.2026.Bathurst.1000.Practice.3.1080p.WEB-GRP" },
        { "V8 Supercars", "Repco Bathurst 1000 Practice 6", "Supercars.2026.Bathurst.1000.Practice.6.1080p.WEB-GRP" },
        { "V8 Supercars", "Repco Bathurst 1000 Boost Mobile Top 10 Shootout", "Supercars.2026.Bathurst.1000.Top.10.Shootout.1080p.WEB-GRP" },
        { "V8 Supercars", "Repco Bathurst 1000 Boost Mobile Qualifying", "Supercars.2026.Bathurst.1000.Qualifying.1080p.WEB-GRP" },
        { "V8 Supercars", "Thrifty Sydney 500  Race 2", "Supercars.2026.Sydney.500.Race.2.1080p.WEB-GRP" },

        { "WorldSSP", "Australian Round Free Practice", "WorldSSP.2026.Australian.Round.Free.Practice.1080p.WEB-GRP" },
        { "WorldSSP", "Australian Round Superpole", "WorldSSP.2026.Australian.Round.Superpole.1080p.WEB-GRP" },
        { "WorldSSP", "Australian Round Warm Up 2", "WorldSSP.2026.Australian.Round.Warm.Up.2.1080p.WEB-GRP" },
        { "WorldSSP", "Australian Round Race 2", "WorldSSP.2026.Australian.Round.Race.2.1080p.WEB-GRP" },
    };

    [Theory]
    [MemberData(nameof(Sessions))]
    public void The_event_and_its_release_agree_on_the_session(string league, string eventTitle, string releaseFile)
    {
        var eventSession = EventPartDetector.DetectMotorsportSessionType(eventTitle, league);
        eventSession.Should().NotBeNull("the event names a session");

        var releaseSession = EventPartDetector.DetectMotorsportSessionFromFilename(releaseFile);

        var normalizedEvent = EventPartDetector.NormalizeMotorsportSession(eventSession);
        var normalizedRelease = EventPartDetector.NormalizeMotorsportSession(releaseSession);

        // A release naming no session is accepted for a race, which is how the
        // matcher treats it, so that pairing counts as agreement.
        var agrees = normalizedEvent == normalizedRelease
                     || (normalizedRelease == null && normalizedEvent == "Race");

        agrees.Should().BeTrue(
            "event '{0}' reads as '{1}' and release '{2}' reads as '{3}'; a difference is a hard rejection",
            eventTitle, normalizedEvent, releaseFile, normalizedRelease);
    }

    [Theory]
    [InlineData("IndyCar Series", "Good Ranchers 250")]
    [InlineData("WEC", "24 Hours of Le Mans")]
    [InlineData("WEC", "Qatar 1812 KM")]
    [InlineData("Formula E", "Jeddah E Prix")]
    public void A_series_that_does_not_name_its_race_still_reads_one(string league, string title)
    {
        EventPartDetector.DetectMotorsportSessionType(title, league).Should().Be("Race");
    }

    [Theory]
    [InlineData("NASCAR Cup Series", "DAYTONA 500")]
    [InlineData("NASCAR Truck Series", "Baptist Health 200")]
    public void A_series_with_no_definitions_reads_no_session(string league, string title)
    {
        // NASCAR publishes only races, so it has no session definitions and
        // must not pick up another series' default.
        EventPartDetector.DetectMotorsportSessionType(title, league).Should().BeNull();
    }

    [Theory]
    [InlineData("WEC")]
    [InlineData("Formula E")]
    [InlineData("V8 Supercars")]
    [InlineData("WorldSSP")]
    public void Each_restored_series_offers_session_types(string league)
    {
        EventPartDetector.GetMotorsportSessionTypes(league)
            .Should().NotBeEmpty("the selector is hidden when nothing is offered");
    }

    [Theory]
    [InlineData("IndyCar Series")]
    [InlineData("WEC")]
    [InlineData("Formula E")]
    [InlineData("V8 Supercars")]
    [InlineData("WorldSSP")]
    public void Every_series_offers_the_race_itself(string league)
    {
        // The race is what most people monitor. For the series that do not
        // name it in the title it is read from a default, which the selector
        // cannot see, so it has to be listed as well or it cannot be chosen.
        EventPartDetector.GetMotorsportSessionTypes(league).Should().Contain("Race");
    }

    [Fact]
    public void NASCAR_offers_none_because_it_publishes_only_races()
    {
        EventPartDetector.GetMotorsportSessionTypes("NASCAR Cup Series").Should().BeEmpty();
    }
}
