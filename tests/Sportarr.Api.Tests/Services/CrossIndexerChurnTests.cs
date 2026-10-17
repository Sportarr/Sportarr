using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class CrossIndexerChurnTests
{
    private const string Title = "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.H264-PARTFIXTURE";

    [Fact]
    public async Task AutomaticSearchSkipsCappedReleaseFromAnotherIndexer()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var first = rig.Release(Title, suffix: "indexer-a");
        first.Indexer = "Indexer A";
        rig.Db.GrabHistory.Add(new GrabHistory
        {
            EventId = rig.Event.Id,
            Title = Title,
            Indexer = first.Indexer,
            Protocol = first.Protocol,
            Guid = first.Guid,
            DownloadUrl = first.DownloadUrl,
            RegrabCount = GrabHistoryChurnGuard.MaxAutomaticRegrabs,
            GrabbedAt = DateTime.UtcNow.AddDays(-1),
            LastRegrabAttempt = DateTime.UtcNow.AddHours(-8)
        });
        await rig.Db.SaveChangesAsync();
        var second = rig.Release(Title, suffix: "indexer-b");
        second.Indexer = "Indexer B";

        var result = await rig.AutomaticAsync(second);

        result.Success.Should().BeFalse();
        rig.Transport.ClientAdds.Should().Be(0);
    }

    [Fact]
    public async Task ManualSearchMayGrabCappedReleaseFromAnotherIndexer()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var first = rig.Release(Title, suffix: "indexer-a");
        first.Indexer = "Indexer A";
        rig.Db.GrabHistory.Add(new GrabHistory
        {
            EventId = rig.Event.Id,
            Title = Title,
            Indexer = first.Indexer,
            Protocol = first.Protocol,
            Guid = first.Guid,
            DownloadUrl = first.DownloadUrl,
            RegrabCount = GrabHistoryChurnGuard.MaxAutomaticRegrabs,
            GrabbedAt = DateTime.UtcNow.AddDays(-1),
            LastRegrabAttempt = DateTime.UtcNow.AddHours(-8)
        });
        await rig.Db.SaveChangesAsync();
        var second = rig.Release(Title, suffix: "indexer-b");
        second.Indexer = "Part fixture";

        var result = await rig.AutomaticAsync(second, manual: true);

        result.Success.Should().BeTrue(result.Message);
        rig.Transport.ClientAdds.Should().Be(1);
    }

    [Fact]
    public async Task RssSkipsCappedReleaseFromAnotherIndexer()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var first = rig.Release(Title, suffix: "indexer-a");
        first.Indexer = "Indexer A";
        rig.Db.GrabHistory.Add(new GrabHistory
        {
            EventId = rig.Event.Id,
            Title = Title,
            Indexer = first.Indexer,
            Protocol = first.Protocol,
            Guid = first.Guid,
            DownloadUrl = first.DownloadUrl,
            RegrabCount = GrabHistoryChurnGuard.MaxAutomaticRegrabs,
            GrabbedAt = DateTime.UtcNow.AddDays(-1),
            LastRegrabAttempt = DateTime.UtcNow.AddHours(-8)
        });
        await rig.Db.SaveChangesAsync();
        var second = rig.Release(Title, suffix: "indexer-b");
        second.Indexer = "Indexer B";
        var method = typeof(RssSyncService).GetMethod("ShouldGrabReleaseAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<(bool Grab, string Reason, string? ReleasePart)>)method.Invoke(
            rig.Services.GetRequiredService<RssSyncService>(), new object[]
            {
                rig.Db, rig.Event, second,
                await rig.Services.GetRequiredService<ConfigService>().GetConfigAsync(),
                await rig.Db.QualityProfiles.SingleAsync(profile => profile.Id == rig.Event.QualityProfileId),
                rig.Services.GetRequiredService<EventPartDetector>(),
                rig.Services.GetRequiredService<DelayProfileService>(),
                rig.Services.GetRequiredService<DownloadClientService>(),
                rig.Services.GetRequiredService<ReleaseProfileService>(),
                new List<ReleaseProfile>(),
                CancellationToken.None
            })!;

        var decision = await task;

        decision.Grab.Should().BeFalse();
        decision.Reason.Should().Contain("Anti-churn");
        rig.Transport.ClientAdds.Should().Be(0);
    }

    [Fact]
    public async Task AutomaticRetryCarriesTheCountAcrossIndexers()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var first = rig.Release(Title, suffix: "indexer-a");
        first.Indexer = "Indexer A";
        rig.Db.GrabHistory.Add(new GrabHistory
        {
            EventId = rig.Event.Id,
            Title = Title,
            Indexer = first.Indexer,
            Protocol = first.Protocol,
            Guid = first.Guid,
            DownloadUrl = first.DownloadUrl,
            RegrabCount = 1,
            GrabbedAt = DateTime.UtcNow.AddDays(-1),
            LastRegrabAttempt = DateTime.UtcNow.AddHours(-8)
        });
        await rig.Db.SaveChangesAsync();
        var second = rig.Release(Title, suffix: "indexer-b");

        var result = await rig.AutomaticAsync(second);

        result.Success.Should().BeTrue(result.Message);
        rig.Transport.ClientAdds.Should().Be(1);
        (await rig.Db.GrabHistory.OrderByDescending(grab => grab.Id).FirstAsync())
            .RegrabCount.Should().Be(2);
    }

    [Fact]
    public async Task RssRecordingCarriesTheCountAcrossIndexers()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var first = rig.Release(Title, suffix: "indexer-a");
        first.Indexer = "Indexer A";
        rig.Db.GrabHistory.Add(new GrabHistory
        {
            EventId = rig.Event.Id,
            Title = Title,
            Indexer = first.Indexer,
            Protocol = first.Protocol,
            Guid = first.Guid,
            DownloadUrl = first.DownloadUrl,
            RegrabCount = 1,
            GrabbedAt = DateTime.UtcNow.AddDays(-1),
            LastRegrabAttempt = DateTime.UtcNow.AddHours(-8)
        });
        await rig.Db.SaveChangesAsync();
        var second = rig.Release(Title, suffix: "indexer-b");
        var method = typeof(RssSyncService).GetMethod("GrabReleaseAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<bool>)method.Invoke(rig.Services.GetRequiredService<RssSyncService>(),
            new object?[]
            {
                rig.Db, rig.Event, second,
                rig.Services.GetRequiredService<DownloadClientService>(),
                rig.Services.GetRequiredService<NotificationService>(),
                "Main Card", CancellationToken.None
            })!;

        (await task).Should().BeTrue();
        rig.Transport.ClientAdds.Should().Be(1);
        (await rig.Db.GrabHistory.OrderByDescending(grab => grab.Id).FirstAsync())
            .RegrabCount.Should().Be(1);
    }
}
