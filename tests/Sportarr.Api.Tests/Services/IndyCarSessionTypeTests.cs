using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// IndyCar lost its session definitions in December 2025, when every series
/// except Formula 1 was stripped out. The selector reads its options from
/// those definitions, so the session monitoring the league page offers for
/// Formula 1 disappeared for IndyCar and never came back.
///
/// Titles here are taken from the 2026 calendar as the metadata source
/// publishes them.
/// </summary>
public class IndyCarSessionTypeTests
{
    [Fact]
    public void The_league_offers_session_types_to_monitor()
    {
        var types = EventPartDetector.GetMotorsportSessionTypes("IndyCar Series");

        types.Should().NotBeEmpty("the selector is hidden when nothing is offered");
        types.Should().Contain(new[] { "Practice 1", "Practice 2", "Qualifying", "Race" });
    }

    [Theory]
    [InlineData("IndyCar Series")]
    [InlineData("Indycar")]
    [InlineData("NTT IndyCar Series")]
    public void The_league_is_recognised_however_it_is_named(string leagueName)
    {
        EventPartDetector.GetMotorsportSessionTypes(leagueName).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("Firestone Grand Prix of St. Petersburg Practice 1", "Practice 1")]
    [InlineData("Java House Grand Prix of Arlington Practice 2", "Practice 2")]
    [InlineData("110th Running of the Indianapolis 500 Practice 3", "Practice 3")]
    [InlineData("110th Running of the Indianapolis 500 Practice 7", "Practice")]
    [InlineData("110th Running of the Indianapolis 500 Fast Friday", "Fast Friday")]
    // A release saying "Final Practice" is read as plain practice by the
    // filename parser, so a label of its own here would reject the very
    // release that belongs to the event.
    [InlineData("110th Running of the Indianapolis 500 Final Practice", "Practice")]
    [InlineData("Good Ranchers 250 High Line and Final Practice", "Practice")]
    [InlineData("Acura Grand Prix of Long Beach Qualifying", "Qualifying")]
    // The Indianapolis 500 qualifying rounds share one label for the same
    // reason, as do the two Milwaukee races.
    [InlineData("110th Running of the Indianapolis 500 Qualifying 1", "Qualifying")]
    [InlineData("110th Running of the Indianapolis 500 Qualifying 3", "Qualifying")]
    [InlineData("Snap On Milwaukee 250 Race #1", "Race")]
    [InlineData("Snap On Milwaukee 250 Race #2", "Race")]
    public void A_named_session_is_read_from_the_title(string title, string expected)
    {
        EventPartDetector.DetectMotorsportSessionType(title, "IndyCar Series").Should().Be(expected);
    }

    [Theory]
    [InlineData("Firestone Grand Prix of St. Petersburg")]
    [InlineData("Good Ranchers 250")]
    [InlineData("110th Running of the Indianapolis 500")]
    [InlineData("Childrens of Alabama Indy Grand Prix")]
    [InlineData("IndyCar Grand Prix of Monterey")]
    public void An_event_naming_no_session_is_the_race(string title)
    {
        // IndyCar calls the race after the event itself. Reading it as
        // "no session detected" left it monitored whatever the user chose,
        // so picking Qualifying still brought in every race.
        EventPartDetector.DetectMotorsportSessionType(title, "IndyCar Series").Should().Be("Race");
    }

    [Fact]
    public void Monitoring_only_qualifying_leaves_the_race_and_practices_out()
    {
        const string league = "IndyCar Series";
        const string monitored = "Qualifying";

        EventPartDetector.IsMotorsportSessionMonitored(
            "Acura Grand Prix of Long Beach Qualifying", league, monitored).Should().BeTrue();

        EventPartDetector.IsMotorsportSessionMonitored(
            "Acura Grand Prix of Long Beach", league, monitored).Should().BeFalse("the race is not qualifying");

        EventPartDetector.IsMotorsportSessionMonitored(
            "Acura Grand Prix of Long Beach Practice 1", league, monitored).Should().BeFalse();
    }

    [Fact]
    public void Monitoring_only_the_race_leaves_the_support_sessions_out()
    {
        const string league = "IndyCar Series";
        const string monitored = "Race";

        EventPartDetector.IsMotorsportSessionMonitored(
            "Good Ranchers 250", league, monitored).Should().BeTrue();

        EventPartDetector.IsMotorsportSessionMonitored(
            "Good Ranchers 250 Qualifying", league, monitored).Should().BeFalse();

        EventPartDetector.IsMotorsportSessionMonitored(
            "Good Ranchers 250 Practice 1", league, monitored).Should().BeFalse();
    }

    [Fact]
    public void A_league_with_no_definitions_is_unaffected_by_the_indycar_default()
    {
        // The default answer belongs to IndyCar alone. A series with no
        // definitions must still report nothing, or every one of its events
        // would start calling itself a race.
        EventPartDetector.DetectMotorsportSessionType("Daytona 500", "NASCAR Cup Series")
            .Should().BeNull();
    }

    [Fact]
    public void Release_filename_matching_does_not_answer_race_for_other_sports()
    {
        // DetectMotorsportSessionFromFilename sweeps every league's patterns.
        // A catch-all for IndyCar would have answered here for a series that
        // has nothing to do with it.
        EventPartDetector.DetectMotorsportSessionFromFilename(
            "NASCAR.Cup.Series.2026.Daytona.500.1080p.WEB.h264-GROUP").Should().BeNull();
    }
}
