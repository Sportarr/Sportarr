using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The calendar importer only accepted the all-day "yyyyMMdd" form of
/// DTSTART. Every calendar that publishes a real start time, which is nearly
/// all of them, produced events with no date and they were dropped silently.
/// </summary>
public class ImportListParsingTests
{
    [Theory]
    [InlineData("DTSTART:20260823T190000Z", 2026, 8, 23, 19)]
    // 15:00 in New York in August is 19:00 UTC. Reading the clock face as UTC
    // put the event four hours early and recorded the wrong window.
    [InlineData("DTSTART;TZID=America/New_York:20260823T150000", 2026, 8, 23, 19)]
    [InlineData("DTSTART;TZID=\"Europe/London\":20260823T150000", 2026, 8, 23, 14)]
    [InlineData("DTSTART;TZID=Australia/Sydney:20260823T150000", 2026, 8, 23, 5)]
    // A zone this host cannot resolve falls back to reading the time as UTC
    // rather than dropping the event.
    [InlineData("DTSTART;TZID=Not/AZone:20260823T150000", 2026, 8, 23, 15)]
    // No zone at all is a floating time, still read as UTC.
    [InlineData("DTSTART:20260823T150000", 2026, 8, 23, 15)]
    [InlineData("DTSTART;VALUE=DATE:20260823", 2026, 8, 23, 0)]
    [InlineData("DTSTART:2026-08-23T19:00:00Z", 2026, 8, 23, 19)]
    public void TryParseIcalDate_reads_the_common_forms(string line, int year, int month, int day, int hour)
    {
        ImportListService.TryParseIcalDate(line, out var value).Should().BeTrue();
        value.Year.Should().Be(year);
        value.Month.Should().Be(month);
        value.Day.Should().Be(day);
        value.Hour.Should().Be(hour);
        value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData("DTSTART")]
    [InlineData("DTSTART:")]
    [InlineData("DTSTART:not a date")]
    public void TryParseIcalDate_refuses_what_it_cannot_read(string line)
    {
        ImportListService.TryParseIcalDate(line, out _).Should().BeFalse();
    }

    [Fact]
    public void UnescapeIcalText_undoes_the_calendar_escaping()
    {
        ImportListService.UnescapeIcalText(@"Round 1\, Silverstone\; UK")
            .Should().Be("Round 1, Silverstone; UK");
    }

    [Fact]
    public void ExtractEventDate_prefers_a_date_written_in_the_text()
    {
        ImportListService.ExtractEventDate("UFC 320 on 2026-10-04")
            .Should().Be(new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
        ImportListService.ExtractEventDate("Grand Prix, 4 October 2026")
            .Should().Be(new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
        ImportListService.ExtractEventDate("Grand Prix October 4, 2026")
            .Should().Be(new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ExtractEventDate_returns_nothing_when_there_is_no_date()
    {
        ImportListService.ExtractEventDate("UFC 320: Main card").Should().BeNull();
        ImportListService.ExtractEventDate("").Should().BeNull();
    }

    /// <summary>
    /// Two events sharing a title and date are told apart by venue. A stored
    /// row with no venue is the same event seen before the feed published one,
    /// so it is claimed and given that venue. Leaving it blank let the second
    /// discovery land on the same row, look like an event already stored, and
    /// be dropped, which is the double header this check exists to keep.
    /// </summary>
    [Fact]
    public void A_venueless_row_is_claimed_once_and_keeps_the_venue_that_claimed_it()
    {
        var stored = new Event { Id = 1, Title = "Grand Prix", Sport = "Motorsport", EventDate = new DateTime(2026, 5, 3), Venue = null };
        var candidates = new List<Event> { stored };

        var first = ImportListService.MatchExistingEvent(candidates,
            new DiscoveredEvent { Title = "Grand Prix", EventDate = new DateTime(2026, 5, 3), Venue = "Silverstone" });

        first.Should().BeSameAs(stored);
        stored.Venue.Should().Be("Silverstone", "the row now names the venue that claimed it");

        var second = ImportListService.MatchExistingEvent(candidates,
            new DiscoveredEvent { Title = "Grand Prix", EventDate = new DateTime(2026, 5, 3), Venue = "Brands Hatch" });

        second.Should().BeNull("a different venue is a different event and has to be added");
    }

    [Fact]
    public void A_matching_venue_still_wins_over_a_venueless_row()
    {
        var venueless = new Event { Id = 1, Title = "Grand Prix", Sport = "Motorsport", EventDate = new DateTime(2026, 5, 3), Venue = null };
        var exact = new Event { Id = 2, Title = "Grand Prix", Sport = "Motorsport", EventDate = new DateTime(2026, 5, 3), Venue = "Silverstone" };
        var candidates = new List<Event> { venueless, exact };

        var match = ImportListService.MatchExistingEvent(candidates,
            new DiscoveredEvent { Title = "Grand Prix", EventDate = new DateTime(2026, 5, 3), Venue = "Silverstone" });

        match.Should().BeSameAs(exact);
        venueless.Venue.Should().BeNull("the venueless row was not claimed");
    }

    [Fact]
    public void A_discovery_with_no_venue_takes_the_first_row_and_changes_nothing()
    {
        var stored = new Event { Id = 1, Title = "Grand Prix", Sport = "Motorsport", EventDate = new DateTime(2026, 5, 3), Venue = "Silverstone" };
        var candidates = new List<Event> { stored };

        var match = ImportListService.MatchExistingEvent(candidates,
            new DiscoveredEvent { Title = "Grand Prix", EventDate = new DateTime(2026, 5, 3), Venue = null });

        match.Should().BeSameAs(stored);
        stored.Venue.Should().Be("Silverstone");
    }
}
