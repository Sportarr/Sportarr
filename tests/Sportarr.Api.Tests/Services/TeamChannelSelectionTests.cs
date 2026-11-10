using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The DVR picks the primary channel and the backups from the team's ranked
/// list and the resolver candidates (#406).
/// </summary>
public class TeamChannelSelectionTests
{
    private static IptvChannel Channel(int id, IptvChannelStatus status = IptvChannelStatus.Unknown) =>
        new() { Id = id, Name = $"Channel {id}", StreamUrl = $"http://iptv.test/{id}", Status = status };

    private static EventChannelCandidate Guide(int id, int confidence = 95) =>
        new(id, $"Channel {id}", "Source", 0, null, confidence, TeamChannelSelection.GuideMatchSource);

    private static EventChannelCandidate Guess(int id, int confidence = 80) =>
        new(id, $"Channel {id}", "Source", 0, null, confidence, "league-mapping");

    [Fact]
    public void ThePrimaryIsTheHighestRankedTeamChannelWithAGuideMatch()
    {
        var team = new[] { Channel(1), Channel(2), Channel(3) };
        var candidates = new[] { Guide(9, 97), Guide(3), Guide(2) };

        TeamChannelSelection.PickPrimaryChannelId(team, candidates).Should().Be(2);
    }

    [Fact]
    public void AGuideMatchOffTheTeamListBeatsTeamChannelOneWhenNoTeamChannelHasOne()
    {
        var team = new[] { Channel(1), Channel(2) };
        var candidates = new[] { Guide(9), Guess(1) };

        TeamChannelSelection.PickPrimaryChannelId(team, candidates).Should().Be(9);
    }

    [Fact]
    public void WithoutAnyGuideMatchTeamChannelOneIsThePrimary()
    {
        var team = new[] { Channel(1), Channel(2) };
        var candidates = new[] { Guess(9, 90), Guess(2) };

        TeamChannelSelection.PickPrimaryChannelId(team, candidates).Should().Be(1);
    }

    [Fact]
    public void WithoutCandidatesTeamChannelOneIsThePrimary()
    {
        TeamChannelSelection.PickPrimaryChannelId(new[] { Channel(1) }, []).Should().Be(1);
    }

    [Fact]
    public void WithoutTeamChannelsTheCallerKeepsItsOwnRules()
    {
        TeamChannelSelection.PickPrimaryChannelId([], new[] { Guide(9) }).Should().BeNull();
    }

    [Fact]
    public void ADeadTeamChannelWithAGuideMatchDoesNotBecomeThePrimary()
    {
        var team = new[] { Channel(1), Channel(2, IptvChannelStatus.Offline) };
        var candidates = new[] { Guide(2) };

        TeamChannelSelection.PickPrimaryChannelId(team, candidates).Should().Be(1);
    }

    [Fact]
    public void ADeadGuideMatchOffTheTeamListDoesNotBeatTeamChannelOne()
    {
        var team = new[] { Channel(1) };
        var candidates = new[] { Guide(9) };

        TeamChannelSelection.PickPrimaryChannelId(team, candidates, new HashSet<int> { 9 }).Should().Be(1);
    }

    [Fact]
    public void ADeadTopGuideMatchGivesWayToTheNextListedChannel()
    {
        var team = new[] { Channel(1) };
        var candidates = new[] { Guide(9), Guide(8, 93) };

        TeamChannelSelection.PickPrimaryChannelId(team, candidates, new HashSet<int> { 9 }).Should().Be(8);
    }

    [Fact]
    public void TheBackupsFollowTheTeamRankThenTheResolver()
    {
        var merged = TeamChannelSelection.MergeOrder(
            new[] { Channel(1), Channel(2), Channel(3) }, new[] { Guide(9), Guess(2), Guess(8) });

        merged.Should().Equal(1, 2, 3, 9, 8);
        TeamChannelSelection.BuildFallbackIds(merged, primaryChannelId: 2).Should().Equal(1, 3, 9, 8);
    }

    [Fact]
    public void TheBackupsLeaveOutTheRedundantCopiesAndKeepTheCap()
    {
        var merged = TeamChannelSelection.MergeOrder(
            new[] { Channel(1), Channel(2), Channel(3), Channel(4) }, new[] { Guide(9), Guess(8), Guess(7) });

        TeamChannelSelection.BuildFallbackIds(merged, primaryChannelId: 1, excluded: [3])
            .Should().Equal(2, 4, 9, 8);
    }

    [Fact]
    public void DeadTeamChannelsGoBehindTheResolverCandidates()
    {
        // Three dead team channels used to fill the backup cap ahead of the
        // channel whose guide lists the event.
        var team = new[]
        {
            Channel(1, IptvChannelStatus.Offline),
            Channel(2, IptvChannelStatus.Error),
            Channel(3, IptvChannelStatus.Offline),
            Channel(4),
        };
        var merged = TeamChannelSelection.MergeOrder(team, new[] { Guide(9), Guess(2) });

        merged.Should().Equal(4, 9, 1, 2, 3);
        TeamChannelSelection.BuildFallbackIds(merged, primaryChannelId: 4).Should().Equal(9, 1, 2, 3);
    }
}
