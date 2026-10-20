using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class RssHeldDownloadTests
{
    private const string Title = "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.H264-PARTFIXTURE";

    private static async Task<(bool Grab, string Reason, string? ReleasePart)> DecideAsync(
        PartIdentityIntegrationHarness rig, ReleaseSearchResult release)
    {
        var method = typeof(RssSyncService).GetMethod("ShouldGrabReleaseAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<(bool Grab, string Reason, string? ReleasePart)>)method.Invoke(
            rig.Services.GetRequiredService<RssSyncService>(), new object[]
            {
                rig.Db, rig.Event, release,
                await rig.Services.GetRequiredService<ConfigService>().GetConfigAsync(),
                await rig.Db.QualityProfiles.SingleAsync(profile => profile.Id == rig.Event.QualityProfileId),
                rig.Services.GetRequiredService<EventPartDetector>(),
                rig.Services.GetRequiredService<DelayProfileService>(),
                rig.Services.GetRequiredService<DownloadClientService>(),
                rig.Services.GetRequiredService<ReleaseProfileService>(),
                new List<ReleaseProfile>(),
                CancellationToken.None
            })!;
        return await task;
    }

    private static async Task HoldAsync(PartIdentityIntegrationHarness rig, ReleaseSearchResult release, DownloadStatus status, string? part)
    {
        var client = await rig.Db.DownloadClients.FirstAsync();
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id,
            Title = release.Title,
            DownloadId = "held-job",
            DownloadClientId = client.Id,
            Protocol = release.Protocol,
            Quality = release.Quality,
            CustomFormatScore = release.CustomFormatScore,
            Part = part,
            Status = status
        });
        await rig.Db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(DownloadStatus.Warning)]
    [InlineData(DownloadStatus.Paused)]
    [InlineData(DownloadStatus.Downloading)]
    public async Task AnEqualReleaseIsNotGrabbedWhileTheFirstGrabIsStillInTheClient(DownloadStatus status)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var first = rig.Release(Title, suffix: "tracker-a");
        var part = (await DecideAsync(rig, first)).ReleasePart;
        await HoldAsync(rig, first, status, part);
        var second = rig.Release(Title, suffix: "tracker-b");

        var decision = await DecideAsync(rig, second);

        decision.Grab.Should().BeFalse();
        decision.Reason.Should().Be("Better or equal release already queued");
        rig.Transport.ClientAdds.Should().Be(0);
    }

    [Fact]
    public async Task AFinishedDownloadWaitingToImportAlsoBlocksAnEqualRelease()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var first = rig.Release(Title, suffix: "tracker-a");
        var part = (await DecideAsync(rig, first)).ReleasePart;
        await HoldAsync(rig, first, DownloadStatus.ImportPending, part);
        var second = rig.Release(Title, suffix: "tracker-b");

        var decision = await DecideAsync(rig, second);

        decision.Grab.Should().BeFalse();
        decision.Reason.Should().StartWith("Already importing");
    }

    [Theory]
    [InlineData(DownloadStatus.Warning)]
    [InlineData(DownloadStatus.Paused)]
    public async Task AGrabStuckForMoreThanADayNoLongerBlocksAnEqualRelease(DownloadStatus status)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var first = rig.Release(Title, suffix: "tracker-a");
        var part = (await DecideAsync(rig, first)).ReleasePart;
        await HoldAsync(rig, first, status, part);
        var held = await rig.Db.DownloadQueue.SingleAsync();
        held.Added = DateTime.UtcNow.AddHours(-25);
        await rig.Db.SaveChangesAsync();
        var second = rig.Release(Title, suffix: "tracker-b");

        var decision = await DecideAsync(rig, second);

        decision.Reason.Should().NotBe("Better or equal release already queued");
    }
}

