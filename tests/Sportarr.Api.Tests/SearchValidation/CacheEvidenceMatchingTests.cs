using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.SearchValidation;

public class CacheEvidenceMatchingTests
{
    [Fact]
    public void SuppliedEventIdentityStillMatchesWhenTheTitleCannotIdentifyTheEvent()
    {
        var cold = CacheEvidenceFixtures.Release("total.nonsense.name.1080p.WEB-DL");
        cold.SportarrEventId = "ev-2336155";
        var matcher = CacheEvidenceFixtures.Matcher();
        var evt = CacheEvidenceFixtures.Event();
        var before = matcher.ValidateRelease(cold, evt);
        Assert.True(before.IsMatch);
        Assert.Equal(100, before.Confidence);

        var after = matcher.ValidateRelease(CacheEvidenceFixtures.RoundTrip(cold), evt);

        Assert.True(after.IsMatch);
        Assert.False(after.IsHardRejection);
        Assert.Equal(before.Confidence, after.Confidence);
        after.MatchReasons.Should().Equal(before.MatchReasons);
    }

    [Fact]
    public void ConflictingSuppliedEventIdentityStillRejectsAnOtherwiseMatchingTitle()
    {
        var cold = CacheEvidenceFixtures.Release();
        cold.SportarrEventId = "ev-9999999";
        var matcher = CacheEvidenceFixtures.Matcher();
        var evt = CacheEvidenceFixtures.Event();
        var before = matcher.ValidateRelease(cold, evt);
        Assert.True(before.IsHardRejection);
        before.Rejections.Should().Contain(reason => reason.Contains("different event"));

        var after = matcher.ValidateRelease(CacheEvidenceFixtures.RoundTrip(cold), evt);

        Assert.True(after.IsHardRejection);
        after.Rejections.Should().Equal(before.Rejections);
    }

    [Fact]
    public void ConflictingSuppliedLeagueIdentityStillRejectsThePack()
    {
        var cold = CacheEvidenceFixtures.Release("FIFA.World.Cup.2026.Pack.1080p.WEB-DL.H264-GROUP");
        cold.SportarrLeagueId = "lg-000456";
        var matcher = CacheEvidenceFixtures.Matcher();
        var evt = CacheEvidenceFixtures.Event();
        var before = matcher.ValidateRelease(cold, evt);
        Assert.True(before.IsHardRejection);
        before.Rejections.Should().Contain(reason => reason.Contains("different league"));

        var after = matcher.ValidateRelease(CacheEvidenceFixtures.RoundTrip(cold), evt);

        Assert.True(after.IsHardRejection);
        after.Rejections.Should().Equal(before.Rejections);
    }

    [Fact]
    public void MatchingSuppliedLeagueIdentityRetainsItsReasonWithoutClaimingAnExactEvent()
    {
        var cold = CacheEvidenceFixtures.Release();
        cold.SportarrLeagueId = "lg-000123";
        var matcher = CacheEvidenceFixtures.Matcher();
        var evt = CacheEvidenceFixtures.Event();
        var before = matcher.ValidateRelease(cold, evt);
        before.MatchReasons.Should().Contain(reason => reason.Contains("league id token match"));

        var after = matcher.ValidateRelease(CacheEvidenceFixtures.RoundTrip(cold), evt);

        after.MatchReasons.Should().Equal(before.MatchReasons);
        Assert.Equal(before.Confidence, after.Confidence);
        Assert.Equal(before.IsHardRejection, after.IsHardRejection);
    }

    [Fact]
    public void InTitleEventIdentityStillWinsOverConflictingSuppliedIdentity()
    {
        var cold = CacheEvidenceFixtures.Release("Spain.vs.Belgium.2026-07-10.1080p.WEB-DL{sportarr-ev-2336155}");
        cold.SportarrEventId = "ev-9999999";
        var matcher = CacheEvidenceFixtures.Matcher();
        var evt = CacheEvidenceFixtures.Event();
        foreach (var release in new[] { cold, CacheEvidenceFixtures.RoundTrip(cold) })
        {
            var result = matcher.ValidateRelease(release, evt);
            Assert.True(result.IsMatch);
            Assert.False(result.IsHardRejection);
            Assert.Equal(100, result.Confidence);
        }
    }

    [Fact]
    public void SourceSpecificEarlyReleaseLimitStillAppliesAfterCaching()
    {
        var evt = CacheEvidenceFixtures.Event();
        var cold = CacheEvidenceFixtures.Release();
        cold.PublishDate = evt.EventDate.AddDays(-2);
        var limits = new Dictionary<int, int?> { [7] = 1 };
        var matcher = CacheEvidenceFixtures.Matcher();
        var before = matcher.ValidateRelease(cold, evt,
            earlyReleaseLimitDays: ReleaseMatchingService.ResolveEarlyReleaseLimit(cold, limits));
        Assert.True(before.IsHardRejection);
        before.Rejections.Should().Contain(reason => reason.Contains("early-release limit"));
        var warm = CacheEvidenceFixtures.RoundTrip(cold);

        var after = matcher.ValidateRelease(warm, evt,
            earlyReleaseLimitDays: ReleaseMatchingService.ResolveEarlyReleaseLimit(warm, limits));

        Assert.True(after.IsHardRejection);
        after.Rejections.Should().Equal(before.Rejections);
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(8, true)]
    [InlineData(7, false)]
    public void DefaultEarlyReleaseLimitRejectsOnlyReleasesOlderThanOneWeek(
        int daysBeforeEvent, bool shouldReject)
    {
        var evt = CacheEvidenceFixtures.Event();
        var release = CacheEvidenceFixtures.Release();
        release.PublishDate = evt.EventDate.AddDays(-daysBeforeEvent);

        var result = CacheEvidenceFixtures.Matcher().ValidateRelease(release, evt);

        result.IsHardRejection.Should().Be(shouldReject);
        if (shouldReject)
            result.Rejections.Should().Contain(reason => reason.Contains("early-release limit"));
    }

    [Fact]
    public void IndexerLimitCannotPermitAReleaseOlderThanOneWeek()
    {
        var evt = CacheEvidenceFixtures.Event();
        var release = CacheEvidenceFixtures.Release();
        release.PublishDate = evt.EventDate.AddDays(-8);

        var result = CacheEvidenceFixtures.Matcher().ValidateRelease(
            release, evt, earlyReleaseLimitDays: 30);

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("early-release limit"));
    }
}
