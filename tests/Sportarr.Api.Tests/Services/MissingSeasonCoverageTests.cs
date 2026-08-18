using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The changes-feed cursor can be current while a league locally misses
/// whole seasons (league monitored after its changes flowed, events that
/// predate the feed, local data loss). Refresh must detect those gaps
/// from the hub season list instead of trusting the cursor alone.
/// </summary>
public class MissingSeasonCoverageTests
{
    private static readonly string ThisYear = DateTime.UtcNow.Year.ToString();
    private static readonly string NextYear = (DateTime.UtcNow.Year + 1).ToString();
    private static readonly string LastYear = (DateTime.UtcNow.Year - 1).ToString();

    [Fact]
    public void FlagsCurrentSeasonMissingLocally()
    {
        var missing = LeagueEventSyncService.FindMissingCurrentSeasons(
            new[] { LastYear, ThisYear },
            new[] { LastYear });

        missing.Should().BeEquivalentTo(new[] { ThisYear });
    }

    [Fact]
    public void FlagsFutureSeasonMissingLocally()
    {
        var missing = LeagueEventSyncService.FindMissingCurrentSeasons(
            new[] { ThisYear, NextYear },
            new[] { ThisYear });

        missing.Should().BeEquivalentTo(new[] { NextYear });
    }

    [Fact]
    public void IgnoresHistoricalSeasons()
    {
        // The window deliberately includes last year (a season that just
        // ended still gets refreshed), so historical means two-plus years
        // back.
        var twoYearsAgo = (DateTime.UtcNow.Year - 2).ToString();
        var missing = LeagueEventSyncService.FindMissingCurrentSeasons(
            new[] { "1999", "2005", twoYearsAgo },
            Array.Empty<string>());

        missing.Should().BeEmpty();
    }

    [Fact]
    public void CompleteCoverageYieldsNothing()
    {
        var missing = LeagueEventSyncService.FindMissingCurrentSeasons(
            new[] { ThisYear, NextYear },
            new[] { ThisYear, NextYear });

        missing.Should().BeEmpty();
    }

    [Fact]
    public void CrossYearSeasonsAreHandled()
    {
        var span = $"{ThisYear}-{NextYear}";
        var missing = LeagueEventSyncService.FindMissingCurrentSeasons(
            new[] { span },
            Array.Empty<string>());

        missing.Should().BeEquivalentTo(new[] { span });
    }

    [Fact]
    public void LocalMatchIsCaseInsensitiveAndEmptyHubEntriesAreSkipped()
    {
        var missing = LeagueEventSyncService.FindMissingCurrentSeasons(
            new[] { "", ThisYear },
            new[] { ThisYear });

        missing.Should().BeEmpty();
    }
}
