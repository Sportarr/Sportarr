using System.Text.Json;
using System.Text.Json.Serialization;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using FluentAssertions;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// The episode summary media servers get is always the spoiler-free line,
/// because hub write-ups of finished events usually name the winner. The
/// episode title never changes.
/// </summary>
public class EpisodeMetadataTextTests
{
    private const string WriteUp = "Toprak Razgatlioglu won Race 2 at Jerez.";
    private const string NeutralLine =
        "Pirelli Spanish Round - Race 2 · SBK 2026 · Round 12 · Circuito de Jerez – Ángel Nieto";

    private static Event Race() => new()
    {
        Title = "Pirelli Spanish Round - Race 2",
        Sport = "Motorsport",
        Description = WriteUp,
        Season = "2026",
        Round = "12",
        Venue = "Circuito de Jerez – Ángel Nieto",
    };

    private static League Sbk() => new() { Name = "SBK", Sport = "Motorsport" };

    [Fact]
    public void WriteUp_IsNeverTheSummary()
    {
        var summary = EpisodeMetadataText.Summary(Race(), Sbk());

        summary.Should().Be(NeutralLine);
        summary.Should().NotContain(WriteUp);
    }

    [Fact]
    public void EventWithoutDescription_GetsTheSameLine()
    {
        var evt = Race();
        evt.Description = null;

        EpisodeMetadataText.Summary(evt, Sbk()).Should().Be(NeutralLine);
    }

    [Fact]
    public void WithoutALeague_UsesTheEventLeagueName()
    {
        var evt = Race();
        evt.League = Sbk();

        EpisodeMetadataText.Summary(evt, null).Should().Be(NeutralLine);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    public void MissingRound_IsLeftOut(string? round)
    {
        var evt = Race();
        evt.Round = round;

        EpisodeMetadataText.Summary(evt, Sbk())
            .Should().Be("Pirelli Spanish Round - Race 2 · SBK 2026 · Circuito de Jerez – Ángel Nieto");
    }

    [Fact]
    public void MissingVenue_IsLeftOut()
    {
        var evt = Race();
        evt.Venue = null;

        EpisodeMetadataText.Summary(evt, Sbk()).Should().Be("Pirelli Spanish Round - Race 2 · SBK 2026 · Round 12");
    }

    [Fact]
    public void EmptyTitle_DoesNotStartWithASeparator()
    {
        var evt = Race();
        evt.Title = "";

        EpisodeMetadataText.Summary(evt, Sbk()).Should().Be("SBK 2026 · Round 12 · Circuito de Jerez – Ángel Nieto");
    }

    [Fact]
    public void TeamEvent_LeadsWithTheMatchup()
    {
        var game = new Event
        {
            Title = "Arsenal vs Chelsea",
            Sport = "Soccer",
            HomeTeamName = "Arsenal",
            AwayTeamName = "Chelsea",
            Season = "2025-2026",
            Round = "10",
            Venue = "Emirates Stadium",
            Description = "Arsenal won 3-1.",
        };
        var league = new League { Name = "English Premier League", Sport = "Soccer" };

        EpisodeMetadataText.Summary(game, league)
            .Should().Be("Arsenal vs Chelsea · English Premier League 2025-2026 · Round 10 · Emirates Stadium");
    }

    public sealed record SharedCase(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("home_team")] string? HomeTeam,
        [property: JsonPropertyName("away_team")] string? AwayTeam,
        [property: JsonPropertyName("event_title")] string? EventTitle,
        [property: JsonPropertyName("league_name")] string? LeagueName,
        [property: JsonPropertyName("season_name")] string? SeasonName,
        [property: JsonPropertyName("round")] string? Round,
        [property: JsonPropertyName("venue_name")] string? VenueName,
        [property: JsonPropertyName("expected")] string? Expected)
    {
        public override string ToString() => Name;
    }

    // The hub tests the same file against compose_summary. Change both sides together.
    public static IEnumerable<object[]> SharedCases() =>
        JsonSerializer.Deserialize<List<SharedCase>>(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Services", "Fixtures", "spoiler_free_summary_cases.json")))!
            .Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(SharedCases))]
    public void SpoilerFreeLine_MatchesTheHubTable(SharedCase c)
    {
        EpisodeMetadataText.SpoilerFreeLine(c.HomeTeam, c.AwayTeam, c.EventTitle, c.LeagueName, c.SeasonName, c.Round, c.VenueName)
            .Should().Be(c.Expected);
    }
}
