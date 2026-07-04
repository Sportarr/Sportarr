using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using FluentAssertions;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Coverage for issue #185: the metadata agent endpoints returned
/// poster_url = null for every season, so media servers fell back to the
/// league poster on all season tiles even when TheSportsDB's season art
/// archive had a dedicated poster for the season. ResolveSeasonPoster picks
/// the season's own art when synced (exact label match first, start-year
/// match second - the events table and the poster archive don't always agree
/// on "2023" vs "2023-2024" labels) and falls back to the league poster.
/// </summary>
public class SeasonPosterResolutionTests
{
    private static SeasonPoster Poster(string season, string url) => new()
    {
        LeagueId = 1,
        Season = season,
        PosterUrl = url,
    };

    private const string LeaguePoster = "https://img.test/league-poster.jpg";

    [Fact]
    public void ExactSeasonLabelMatch_ReturnsSeasonArt()
    {
        var posters = new List<SeasonPoster>
        {
            Poster("2025", "https://img.test/2025.jpg"),
            Poster("2026", "https://img.test/2026.jpg"),
        };

        var result = MetadataAgentEndpoints.ResolveSeasonPoster(posters, "2026", LeaguePoster);

        result.Should().Be("https://img.test/2026.jpg");
    }

    [Fact]
    public void SingleYearLabel_MatchesDualYearArchiveEntryByStartYear()
    {
        // Events synced with label "2023" while the poster archive stores
        // the dual-year form - the season-list normalizer merges single-year
        // labels into dual-year ones, so both forms exist in the wild.
        var posters = new List<SeasonPoster> { Poster("2023-2024", "https://img.test/2324.jpg") };

        var result = MetadataAgentEndpoints.ResolveSeasonPoster(posters, "2023", LeaguePoster);

        result.Should().Be("https://img.test/2324.jpg");
    }

    [Fact]
    public void DualYearLabel_MatchesSingleYearArchiveEntryByStartYear()
    {
        var posters = new List<SeasonPoster> { Poster("2023", "https://img.test/2023.jpg") };

        var result = MetadataAgentEndpoints.ResolveSeasonPoster(posters, "2023-2024", LeaguePoster);

        result.Should().Be("https://img.test/2023.jpg");
    }

    [Fact]
    public void StartYearMatch_MustNotMatchADifferentSeason()
    {
        // A 2024-2025 archive entry must not serve as art for the 2023 season.
        var posters = new List<SeasonPoster> { Poster("2024-2025", "https://img.test/2425.jpg") };

        var result = MetadataAgentEndpoints.ResolveSeasonPoster(posters, "2023", LeaguePoster);

        result.Should().Be(LeaguePoster);
    }

    [Fact]
    public void NoSeasonArt_FallsBackToLeaguePoster()
    {
        var result = MetadataAgentEndpoints.ResolveSeasonPoster(new List<SeasonPoster>(), "2026", LeaguePoster);

        result.Should().Be(LeaguePoster);
    }

    [Fact]
    public void NoSeasonLabel_FallsBackToLeaguePoster()
    {
        var posters = new List<SeasonPoster> { Poster("2026", "https://img.test/2026.jpg") };

        var result = MetadataAgentEndpoints.ResolveSeasonPoster(posters, null, LeaguePoster);

        result.Should().Be(LeaguePoster);
    }

    [Fact]
    public void NoArtAndNoLeaguePoster_ReturnsNull()
    {
        var result = MetadataAgentEndpoints.ResolveSeasonPoster(new List<SeasonPoster>(), "2026", null);

        result.Should().BeNull();
    }
}
