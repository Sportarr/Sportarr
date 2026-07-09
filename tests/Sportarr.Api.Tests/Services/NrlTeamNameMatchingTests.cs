using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// NRL coverage from the field report: KAYO/scene releases name games by
/// bare nickname ("NRL 2026 Round 18 Eels v Sea Eagles") while canonical
/// team names are place+nickname ("Parramatta Eels", "Manly Sea Eagles",
/// sometimes hyphenated "Manly-Warringah Sea Eagles"). Team validation
/// found only the alias-covered side and hard-rejected every release with
/// "Only one team name found". Also covers the query side: the league name
/// "Australian National Rugby League" produced queries no release has ever
/// been tagged with.
/// </summary>
public class NrlTeamNameMatchingTests
{
    private readonly ReleaseMatchingService _svc;

    public NrlTeamNameMatchingTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _svc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event NrlEvent(string home = "Parramatta Eels", string away = "Manly Sea Eagles") => new()
    {
        Id = 1,
        Title = $"{home} vs {away}",
        Sport = "Rugby League",
        HomeTeamName = home,
        AwayTeamName = away,
        HomeTeam = new Team { Name = home, Sport = "Rugby League" },
        AwayTeam = new Team { Name = away, Sport = "Rugby League" },
        EventDate = new DateTime(2026, 7, 4, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "Australian National Rugby League", Sport = "Rugby League" }
    };

    private const string ReportedRelease = "NRL.2026.Round.18.Eels.v.Sea.Eagles.2160p.KAYO.WEB-DL.HEVC-nVa";

    [Fact]
    public void NicknameOnlyRelease_MatchesBothTeams()
    {
        var result = _svc.ValidateRelease(Rel(ReportedRelease), NrlEvent());

        result.Rejections.Should().NotContain(r => r.Contains("Only one team name found"));
        result.Rejections.Should().NotContain(r => r.Contains("Team names not found"));
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void HyphenatedCanonicalNames_StillMatchNicknameRelease()
    {
        var result = _svc.ValidateRelease(
            Rel(ReportedRelease),
            NrlEvent(away: "Manly-Warringah Sea Eagles"));

        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void DifferentMatchup_StillRejected()
    {
        var result = _svc.ValidateRelease(
            Rel("NRL.2026.Round.18.Broncos.v.Sharks.2160p.KAYO.WEB-DL.HEVC.nVa"),
            NrlEvent());

        result.MatchReasons.Should().NotContain("Both team names found");
        result.Rejections.Should().Contain(r => r.Contains("Team names not found"));
    }

    [Fact]
    public void OneSharedTeam_RemainsWrongMatchupHardRejection()
    {
        var result = _svc.ValidateRelease(
            Rel("NRL.2026.Round.18.Eels.v.Broncos.1080p.KAYO.WEB-DL.H.264-nVa"),
            NrlEvent());

        result.Rejections.Should().Contain(r => r.Contains("Only one team name found"));
    }

    [Fact]
    public void NrlLeague_QueriesUseNrlPrefixNotFullMetadataName()
    {
        var service = new EventQueryService(NullLogger<EventQueryService>.Instance);
        var queries = service.BuildEventQueries(NrlEvent());

        queries.Should().Contain("NRL 2026 07");
        queries.Should().Contain("NRL 2026");
        queries.Should().NotContain(q => q.Contains("Australian National Rugby League"));
    }
}
