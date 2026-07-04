using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #143: boxing/MMA events are titled with full fighter
/// names ("Fabio Wardley vs Daniel Dubois") but releases almost never carry
/// first names ("Boxing.2026.05.09.Wardley.vs.Dubois..."). Neither side of
/// the pipeline handled that: search queries used the full-name title (zero
/// indexer hits) and the matcher gave correct surname-only releases no
/// fighter credit, stalling them below the confidence threshold.
/// </summary>
public class FighterSurnameMatchingTests
{
    // ---- Surname extraction rules ----

    [Theory]
    [InlineData("Fabio Wardley vs Daniel Dubois", "Wardley", "Dubois")]
    [InlineData("Fabio Wardley vs. Daniel Dubois", "Wardley", "Dubois")]
    [InlineData("UFC 300: Alex Pereira vs Jamahal Hill", "Pereira", "Hill")]
    [InlineData("Roy Jones Jr vs Mike Tyson", "Jones", "Tyson")]
    [InlineData("Canelo Alvarez vs Terence Crawford - Main Card", "Alvarez", "Crawford")]
    [InlineData("Oleksandr Usyk vs Tyson Fury (Rematch)", "Usyk", "Fury")]
    public void TryExtractFighterSurnames_ExtractsLastNames(string title, string expectedA, string expectedB)
    {
        var ok = EventPartDetector.TryExtractFighterSurnames(title, out var a, out var b);

        ok.Should().BeTrue();
        a.Should().Be(expectedA);
        b.Should().Be(expectedB);
    }

    [Theory]
    [InlineData("UFC Fight Night 240")]      // no matchup at all
    [InlineData("WrestleMania 43")]
    [InlineData("")]
    [InlineData("A vs B vs C")]              // three-way isn't a two-sided matchup
    public void TryExtractFighterSurnames_RejectsNonMatchupTitles(string title)
    {
        EventPartDetector.TryExtractFighterSurnames(title, out _, out _).Should().BeFalse();
    }

    // ---- Query generation ----

    private static EventQueryService CreateQueryService() =>
        new(NullLogger<EventQueryService>.Instance);

    [Fact]
    public void BuildEventQueries_BoxingMatchup_LeadsWithSurnameQuery()
    {
        var evt = new Event
        {
            Title = "Fabio Wardley vs Daniel Dubois",
            Sport = "Fighting",
            EventDate = new DateTime(2026, 5, 9, 21, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "Boxing", Sport = "Fighting" },
        };

        var queries = CreateQueryService().BuildEventQueries(evt);

        // The surname form is what indexers actually publish, so it must be
        // the first (primary) query for a pure matchup title.
        queries[0].Should().Be("Wardley vs Dubois");
    }

    [Fact]
    public void BuildEventQueries_NumberedCard_KeepsCardPrimaryAndAddsSurnameSupplementary()
    {
        var evt = new Event
        {
            Title = "UFC 300: Alex Pereira vs Jamahal Hill",
            Sport = "Fighting",
            EventDate = new DateTime(2026, 4, 13, 22, 0, 0, DateTimeKind.Utc),
            League = new League { Name = "UFC", Sport = "Fighting" },
        };

        var queries = CreateQueryService().BuildEventQueries(evt);

        queries[0].Should().Be("UFC 300");
        queries.Should().Contain("Pereira vs Hill");
    }

    // ---- Release matching ----

    private readonly ReleaseMatchingService _matchingSvc;

    public FighterSurnameMatchingTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _matchingSvc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event WardleyDubois() => new()
    {
        Id = 1,
        Title = "Fabio Wardley vs Daniel Dubois",
        Sport = "Fighting",
        EventDate = new DateTime(2026, 5, 9, 21, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "Boxing", Sport = "Fighting" }
    };

    [Fact]
    public void SurnameOnlyRelease_MatchesTheEvent()
    {
        // The reporter's exact release title.
        var result = _matchingSvc.ValidateRelease(
            Rel("Boxing.2026.05.09.Wardley.vs.Dubois.Full.Event.Replay.WEB.H264-RBB"), WardleyDubois());

        result.MatchReasons.Should().Contain("Both fighter surnames found");
        result.IsMatch.Should().BeTrue();
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void DifferentMatchupRelease_DoesNotMatch()
    {
        // A same-day release for a completely different fight must earn no
        // fighter credit and stay below the match threshold.
        var result = _matchingSvc.ValidateRelease(
            Rel("Boxing.2026.05.09.Usyk.vs.Fury.Full.Event.Replay.WEB.H264-RBB"), WardleyDubois());

        result.MatchReasons.Should().NotContain("Both fighter surnames found");
        result.IsMatch.Should().BeFalse();
    }
}
