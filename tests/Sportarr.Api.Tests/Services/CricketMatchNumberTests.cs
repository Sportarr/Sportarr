using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Cricket releases write the match number as "M38" or as "Match 38". The
/// same two teams meet more than once a season, so a different number is a
/// different match in both forms.
/// </summary>
public class CricketMatchNumberTests
{
    private readonly ReleaseMatchScorer _scorer = new();
    private readonly ReleaseMatchingService _matcher = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    private static Event MatchThree() => new()
    {
        Title = "Mumbai Indians vs Chennai Super Kings",
        Sport = "Cricket",
        Season = "2025",
        Round = "3",
        EventDate = new DateTime(2025, 3, 23, 14, 0, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2025, 3, 23),
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = "Mumbai Indians",
        AwayTeamName = "Chennai Super Kings",
        League = new League { Name = "Indian Premier League", Sport = "Cricket" },
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://fixture.invalid/" + Uri.EscapeDataString(title),
        Indexer = "Fixture",
    };

    [Theory]
    [InlineData("IPL.2025.Match.38.Mumbai.Indians.vs.Chennai.Super.Kings.720p")]
    [InlineData("IPL 2025 Match 38 Mumbai Indians vs Chennai Super Kings 1080p")]
    [InlineData("IPL.2025.Match38.Mumbai.Indians.vs.Chennai.Super.Kings.720p")]
    [InlineData("IPL.2025.M38.Mumbai.Indians.vs.Chennai.Super.Kings.720p")]
    public void WrittenMatchNumberOfAnotherMatch_IsAConflict(string title)
    {
        CricketRugbyReleaseNamePolicy.HasIdentityConflict(title, MatchThree()).Should().BeTrue();
        _scorer.CalculateMatchScore(title, MatchThree()).Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        _matcher.ValidateRelease(Release(title), MatchThree()).IsMatch.Should().BeFalse();
    }

    [Theory]
    [InlineData("IPL.2025.Match.3.Mumbai.Indians.vs.Chennai.Super.Kings.720p")]
    [InlineData("IPL.2025.Match.03.Mumbai.Indians.vs.Chennai.Super.Kings.720p")]
    [InlineData("IPL.2025.Mumbai.Indians.vs.Chennai.Super.Kings.Full.Match.1080p")]
    [InlineData("IPL.2025.Mumbai.Indians.vs.Chennai.Super.Kings.Full.Match.720p.WEB")]
    public void SameMatchNumber_OrAQualityTagAfterMatch_IsNoConflict(string title)
    {
        CricketRugbyReleaseNamePolicy.HasIdentityConflict(title, MatchThree()).Should().BeFalse();
        _scorer.CalculateMatchScore(title, MatchThree())
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    private static Event MatchTwentyNine() => new()
    {
        Title = "Mumbai Indians vs Chennai Super Kings",
        Sport = "Cricket",
        Season = "2024",
        Round = "29",
        EventDate = new DateTime(2024, 4, 14, 14, 0, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2024, 4, 14),
        BroadcastDateVerified = true,
        HomeTeamId = 1,
        AwayTeamId = 2,
        HomeTeamName = "Mumbai Indians",
        AwayTeamName = "Chennai Super Kings",
        League = new League { Name = "Indian Premier League", Sport = "Cricket" },
    };

    // A part upload counts its parts ("Full Match 1 of 2"), not the match.
    [Theory]
    [InlineData("IPL.2024.Mumbai.Indians.vs.Chennai.Super.Kings.Full.Match.1.of.2.720p")]
    [InlineData("IPL.2024.04.14.Mumbai.Indians.vs.Chennai.Super.Kings.Full.Match.1.of.2.720p")]
    [InlineData("IPL.2024.Mumbai.Indians.vs.Chennai.Super.Kings.Full.Match.1080p")]
    public void PartCountAfterMatch_IsNotAMatchNumber(string title)
    {
        CricketRugbyReleaseNamePolicy.HasIdentityConflict(title, MatchTwentyNine()).Should().BeFalse();
        _scorer.CalculateMatchScore(title, MatchTwentyNine())
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    // A part count or a season count after the number does not hide it.
    [Theory]
    [InlineData("IPL.2024.Match.30.Part.1.Mumbai.Indians.vs.Chennai.Super.Kings.720p")]
    [InlineData("IPL.2024.Match.30.of.74.Mumbai.Indians.vs.Chennai.Super.Kings.720p")]
    public void MatchNumberBeforeAPartOrSeasonCount_StillConflicts(string title)
    {
        CricketRugbyReleaseNamePolicy.HasIdentityConflict(title, MatchTwentyNine()).Should().BeTrue();
        _scorer.CalculateMatchScore(title, MatchTwentyNine()).Should().BeLessThan(ReleaseMatchScorer.MinimumMatchScore);
        _matcher.ValidateRelease(Release(title), MatchTwentyNine()).IsMatch.Should().BeFalse();
    }

    [Theory]
    [InlineData("IPL.2025.Match.38.MI.vs.CSK", 38)]
    [InlineData("IPL 2025 M07 MI vs CSK", 7)]
    [InlineData("IPL.2025.MI.vs.CSK.Full.Match.1080p", null)]
    [InlineData("IPL.2025.MI.vs.CSK.Full.Match.1.of.2", null)]
    [InlineData("IPL.2025.MI.vs.CSK.Full.Match.2.720p", null)]
    [InlineData("IPL.2025.MI.vs.CSK.Match.2.Part.1", 2)]
    [InlineData("IPL.2024.Match.29.Part.1.MI.vs.CSK", 29)]
    [InlineData("IPL.2024.Match.29.of.74.MI.vs.CSK", 29)]
    [InlineData("IPL.2024.Match.30.of.74.MI.vs.CSK", 30)]
    [InlineData("IPL.2025.MI.vs.CSK.Match.2.of.2", null)]
    [InlineData("IPL.2025.Match.38.MI.vs.CSK.Full.Match.1.of.2", 38)]
    public void ReleaseMatchNumber_ReadsBothForms(string title, int? expected)
    {
        CricketRugbyReleaseNamePolicy.ReleaseMatchNumber(title).Should().Be(expected);
    }
}
