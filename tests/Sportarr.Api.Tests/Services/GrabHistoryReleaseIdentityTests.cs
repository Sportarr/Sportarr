using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class GrabHistoryReleaseIdentityTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string Title = "Formula1.2026.Round14.Spain.Madrid.Qualifying.SKY.F1TV.WEB-DL.1080p.H264.English-MWR";

    [Fact]
    public void SameUsenetTitleAcrossIndexersKeepsTheHighestRetryCount()
    {
        var first = Grab("NZBgeek", "first", 3, Now.AddHours(-12));
        var later = Grab("DrunkenSlug", "second", 1, Now.AddHours(-1));
        var release = Release("DrunkenSlug", "third");

        var prior = GrabHistoryReleaseIdentity.FindPrior(new[] { first, later }, release);

        prior.Should().NotBeNull();
        prior!.History.Should().BeSameAs(later);
        prior.RegrabCount.Should().Be(3);
        GrabHistoryChurnGuard.Evaluate(prior.History, Now, prior.RegrabCount)
            .Should().Be(GrabHistoryChurnGuard.Decision.BlockCapReached);
    }

    [Fact]
    public void DistinctReleaseTitleDoesNotInheritTheCap()
    {
        var prior = Grab("NZBgeek", "first", 3, Now.AddHours(-12));
        var release = Release("DrunkenSlug", "second");
        release.Title = "Formula1.2026.Round14.Spain.Madrid.Race.F1TV.WEB-DL.1080p.H264.English-MWR";

        GrabHistoryReleaseIdentity.FindPrior(new[] { prior }, release).Should().BeNull();
    }

    [Fact]
    public void TorrentTitleFromAnotherIndexerDoesNotShareHistory()
    {
        var prior = Grab("Tracker A", "first", 3, Now.AddHours(-12));
        prior.Protocol = "Torrent";
        var release = Release("Tracker B", "second");
        release.Protocol = "Torrent";

        GrabHistoryReleaseIdentity.FindPrior(new[] { prior }, release).Should().BeNull();
    }

    [Fact]
    public void SuccessfulImportAfterOldFailedGrabsDoesNotKeepTheCap()
    {
        var capped = Grab("NZBgeek", "first", 3, Now.AddHours(-12));
        var imported = Grab("DrunkenSlug", "second", 0, Now.AddMinutes(-10));
        imported.WasImported = true;

        GrabHistoryReleaseIdentity.FindPrior(new[] { capped, imported }, Release("DrunkenSlug", "third"))
            .Should().Match<GrabHistoryReleaseIdentity.PriorGrab>(prior => prior.History == imported);
    }

    [Fact]
    public void SuccessfulImportStartsANewRetryWindowAfterLaterFailure()
    {
        var oldCapped = Grab("NZBgeek", "first", 3, Now.AddDays(-2));
        var imported = Grab("DrunkenSlug", "second", 0, Now.AddDays(-1));
        imported.WasImported = true;
        var newFailure = Grab("NZBfinder", "third", 0, Now.AddHours(-8));

        GrabHistoryReleaseIdentity.FindPrior(
                new[] { oldCapped, imported, newFailure }, Release("NZBfinder", "fourth"))
            .Should().Match<GrabHistoryReleaseIdentity.PriorGrab>(prior =>
                prior.History == newFailure && prior.RegrabCount == 0);
    }

    [Fact]
    public void NewestFailedGrabSetsCooldownWhileOlderGrabKeepsRetryCount()
    {
        var oldRetry = Grab("NZBgeek", "first", 1, Now.AddHours(-8));
        var latest = Grab("DrunkenSlug", "second", 0, Now.AddHours(-1));

        var prior = GrabHistoryReleaseIdentity.FindPrior(
            new[] { oldRetry, latest }, Release("NZBfinder", "third"));

        prior.Should().NotBeNull();
        prior!.History.Should().BeSameAs(latest);
        prior.RegrabCount.Should().Be(1);
        GrabHistoryChurnGuard.Evaluate(prior.History, Now, prior.RegrabCount)
            .Should().Be(GrabHistoryChurnGuard.Decision.BlockCooldown);
    }

    private static GrabHistory Grab(string indexer, string suffix, int count, DateTime grabbedAt) => new()
    {
        Title = Title,
        Indexer = indexer,
        Protocol = "Usenet",
        Guid = "guid-" + suffix,
        DownloadUrl = "https://example.invalid/" + suffix,
        RegrabCount = count,
        GrabbedAt = grabbedAt,
        LastRegrabAttempt = grabbedAt
    };

    private static ReleaseSearchResult Release(string indexer, string suffix) => new()
    {
        Title = Title,
        Indexer = indexer,
        Protocol = "Usenet",
        Guid = "guid-" + suffix,
        DownloadUrl = "https://example.invalid/" + suffix
    };
}
