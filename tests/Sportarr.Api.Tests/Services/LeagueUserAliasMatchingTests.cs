using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Regression: a release found only through a user-defined league alias must
/// not later fail league-identity matching because the matcher never
/// consulted League.UserAliases.
///
/// League "English Prem Rugby" has no upstream alternate name for its
/// sponsor-branded form, but a user has added UserAliases = "Gallagher Prem"
/// so releases tagged with the sponsor name can still be found and matched.
/// Every league-identity gate — organization scoring, TitleNamesLeague,
/// SeriesLabelMatchesLeague, the grab-side ValidateRelease gate, and the
/// import-side CalculateMatchConfidence gate — must honor that alias.
/// </summary>
public class LeagueUserAliasMatchingTests
{
    private static League EnglishPremRugby() => new()
    {
        Id = 42,
        Name = "English Prem Rugby",
        Sport = "Rugby",
        UserAliases = "Gallagher Prem",
    };

    private readonly ReleaseMatchingService _matchingSvc;

    public LeagueUserAliasMatchingTests()
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

    private static Event RoundSix() => new()
    {
        Id = 900,
        Title = "Round 6",
        Sport = "Rugby",
        EventDate = new DateTime(2026, 3, 22, 0, 0, 0, DateTimeKind.Utc),
        League = EnglishPremRugby(),
        LeagueId = 42,
    };

    // ── TitleNamesLeague ─────────────────────────────────────────────────────

    [Fact]
    public void TitleNamesLeague_MatchesUserAliasOnly()
    {
        ReleaseMatchingService.TitleNamesLeague(
            "Gallagher.Prem.2026.Round.06.1080p.WEB-DL", EnglishPremRugby())
        .Should().BeTrue();
    }

    // ── SeriesLabelMatchesLeague ─────────────────────────────────────────────

    [Fact]
    public void SeriesLabelMatchesLeague_MatchesUserAliasOnly()
    {
        ReleaseMatchingService.SeriesLabelMatchesLeague("Gallagher Prem", EnglishPremRugby())
            .Should().BeTrue();
    }

    // ── Organization scoring (VALIDATION 4 inside ValidateRelease) ──────────

    [Fact]
    public void OrganizationScoring_CreditsUserAliasOnly()
    {
        // Feed a pre-parsed result directly so the test does not depend on
        // SportsFileNameParser's fixed organization-prefix table (which has
        // no entry for this fictional rugby league) - only on VALIDATION 4's
        // own alias lookup.
        var preParsed = new SportsParseResult
        {
            OriginalFilename = "Gallagher.Prem.2026.Round.06.1080p.WEB-DL",
            Sport = "Rugby",
            Organization = "Gallagher Prem",
        };

        var result = _matchingSvc.ValidateRelease(
            Rel("Gallagher.Prem.2026.Round.06.1080p.WEB-DL"), RoundSix(), preParsed: preParsed);

        result.MatchReasons.Should().Contain(r => r.StartsWith("League/organization matches"));
    }

    // ── Grab-side gate: full ValidateRelease pipeline, no pre-parsed result ─

    [Fact]
    public void GrabValidation_AcceptsReleaseNamedOnlyByUserAlias()
    {
        var result = _matchingSvc.ValidateRelease(
            Rel("Gallagher.Prem.2026.Round.06.1080p.WEB-DL"), RoundSix());

        result.IsHardRejection.Should().BeFalse(
            $"rejections=[{string.Join(", ", result.Rejections)}]");
    }

    // ── Import-side gate: LibraryImportService.CalculateMatchConfidence ─────

    [Fact]
    public void ImportMatching_AcceptsSeriesLabelNamedOnlyByUserAlias()
    {
        var confidence = LibraryImportService.CalculateMatchConfidence(
            searchTitle: "betr Gallagher Prem Round 6",
            eventTitle: "Round 6",
            organization: null,
            evt: RoundSix(),
            parsedDate: null,
            parsedYear: 2026,
            parsedSport: "Rugby",
            seriesLabel: "Gallagher Prem");

        confidence.Should().BeGreaterThan(0);
    }
}
