using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class FailedDownloadSearchBudgetTests
{
    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 1)]
    public async Task AutomaticSearchDoesNotQueryAfterRetryIsUnavailable(bool enabled, int retries)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await SeedFailureAsync(rig, retries);
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        config.RedownloadFailedDownloads = enabled;
        config.AutoSearchRetryBackoffMinutes = "0";
        await configService.SaveConfigAsync(config);

        var outcome = await rig.Services.GetRequiredService<AutomaticSearchService>()
            .SearchAndDownloadEventAsync(rig.Event.Id, isManualSearch: false);

        Assert.False(outcome.Success);
        Assert.Empty(rig.Transport.SourceRequests);
        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Single(await rig.Db.DownloadQueue.ToListAsync());
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 1)]
    public async Task RssDoesNotGrabWhenRetryIsUnavailable(bool enabled, int retries)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true);
        await SeedFailureAsync(rig, retries);
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        config.RedownloadFailedDownloads = enabled;
        await configService.SaveConfigAsync(config);
        var release = rig.Release("UFC.9999.2020.09.01.720p.WEB-DL.H264-RetryBudget");
        release.Size = 2_000_000_000;
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();

        var outcome = await rig.Services.GetRequiredService<RssSyncService>()
            .ProcessPushedReleaseAsync(release, CancellationToken.None);

        Assert.False(outcome.Grabbed);
        Assert.Contains(outcome.Rejections, reason => reason.Contains("retry", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Single(await rig.Db.DownloadQueue.ToListAsync());
    }

    [Fact]
    public async Task RssCarriesFailureCountWithoutChargingTheReplacementEarly()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true);
        await SeedFailureAsync(rig, 2);
        var release = rig.Release("UFC.9999.2020.09.01.720p.WEB-DL.H264-RetryBudget");
        release.Size = 2_000_000_000;
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();

        var outcome = await rig.Services.GetRequiredService<RssSyncService>()
            .ProcessPushedReleaseAsync(release, CancellationToken.None);

        Assert.True(outcome.Grabbed, string.Join("; ", outcome.Rejections));
        Assert.Equal(1, rig.Transport.ClientAdds);
        var replacement = await rig.Db.DownloadQueue.SingleAsync(q => q.Status == DownloadStatus.Queued);
        Assert.Equal(2, replacement.RetryCount);
    }

    [Fact]
    public async Task ExplicitManualSearchCanOverrideAutomaticRetryStop()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await SeedFailureAsync(rig, 3);
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        config.RedownloadFailedDownloads = false;
        await configService.SaveConfigAsync(config);
        var release = rig.Release("UFC.9999.2020.09.01.720p.WEB-DL.H264-ManualRetry");
        release.Size = 2_000_000_000;

        var outcome = await rig.AutomaticAsync(release, manual: true);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(1, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task FailedMainCardDoesNotStopPrelimsSearch()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true);
        await SeedFailureAsync(rig, 3, "Main Card");
        var mainCard = rig.Release("UFC.9999.2020.09.01.Main.Card.1080p.WEB-DL.H264-RetryBudget",
            "Main Card", suffix: "main");
        mainCard.Quality = "WEBDL-1080p";
        mainCard.Size = 4_000_000_000;
        var release = rig.Release("UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-RetryBudget", "Prelims");
        release.Size = 2_000_000_000;

        var queries = rig.Services.GetRequiredService<EventQueryService>()
            .BuildEventQueries(rig.Event, null, rig.Event.League?.SearchQueryTemplate);
        var search = rig.Services.GetRequiredService<IndexerSearchService>();
        var fingerprint = await search.GetSearchSourceFingerprintAsync(false, rig.Event.League?.Tags);
        var key = SearchResultCache.RequestKey(queries, rig.Event.League?.Tags ?? new List<int>(),
            100, true, Sportarr.Api.Helpers.SportarrIdToken.Normalize(rig.Event.ExternalId), fingerprint);
        rig.Services.GetRequiredService<SearchResultCache>().Store(key, new[] { mainCard, release });

        var outcome = await rig.Services.GetRequiredService<AutomaticSearchService>()
            .SearchAndDownloadEventAsync(rig.Event.Id, part: null, isManualSearch: false);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(release.Title, outcome.SelectedRelease);
        Assert.Equal(1, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task SuccessfulImportClearsAnOlderFailedRetryStop()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await SeedFailureAsync(rig, 3);
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id,
            Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-Imported",
            DownloadId = "imported-after-failure",
            Status = DownloadStatus.Imported,
            ImportedAt = DateTime.UtcNow,
            LastUpdate = DateTime.UtcNow,
            Quality = "WEBDL-720p"
        });
        await rig.Db.SaveChangesAsync();
        var release = rig.Release("UFC.9999.2020.09.01.1080p.WEB-DL.H264-Upgrade");
        release.Quality = "WEBDL-1080p";
        release.Size = 4_000_000_000;

        var outcome = await rig.AutomaticAsync(release);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(1, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task RssCanGrabMainCardAfterFailedWholeEvent()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true, relational: true);
        var first = rig.Release("UFC.9999.2020.09.01.720p.WEB-DL.H264-AutoFailure");
        first.Size = 2_000_000_000;
        var automatic = await rig.AutomaticAsync(first);
        Assert.True(automatic.Success, automatic.Message);
        var failed = await rig.Db.DownloadQueue.SingleAsync();
        Assert.Null(failed.Part);
        failed.Status = DownloadStatus.Failed;
        failed.RetryCount = 3;
        failed.LastUpdate = DateTime.UtcNow.AddDays(-7);
        await rig.Db.SaveChangesAsync();
        var release = rig.Release("UFC.9999.2020.09.01.720p.WEB-DL.H264-RssRetry", suffix: "rss-retry");
        release.Size = 2_000_000_000;
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();

        var outcome = await rig.Services.GetRequiredService<RssSyncService>()
            .ProcessPushedReleaseAsync(release, CancellationToken.None);

        Assert.True(outcome.Grabbed, string.Join("; ", outcome.Rejections));
        Assert.Equal(2, rig.Transport.ClientAdds);
        Assert.Equal("Main Card", (await rig.Db.DownloadQueue.SingleAsync(q => q.Status == DownloadStatus.Queued)).Part);
    }

    [Fact]
    public async Task PollingOldFailureDoesNotUndoLaterSuccessfulImport()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await SeedFailureAsync(rig, 3);
        var failed = await rig.Db.DownloadQueue.SingleAsync();
        failed.Added = DateTime.UtcNow.AddDays(-3);
        failed.LastUpdate = DateTime.UtcNow.AddMinutes(1);
        failed.FailedAt = null;
        rig.Db.Blocklist.Add(new BlocklistItem
        {
            EventId = rig.Event.Id,
            Title = failed.Title,
            Indexer = "Unknown",
            Reason = BlocklistReason.FailedDownload,
            BlockedAt = DateTime.UtcNow.AddDays(-2)
        });
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id,
            Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-Imported",
            DownloadId = "imported-after-failure-poll",
            Status = DownloadStatus.Imported,
            Added = DateTime.UtcNow.AddDays(-2),
            ImportedAt = DateTime.UtcNow.AddDays(-1),
            LastUpdate = DateTime.UtcNow.AddDays(-1),
            Quality = "WEBDL-720p"
        });
        await rig.Db.SaveChangesAsync();
        var release = rig.Release("UFC.9999.2020.09.01.1080p.WEB-DL.H264-UpgradeAfterPoll");
        release.Quality = "WEBDL-1080p";
        release.Size = 4_000_000_000;

        var outcome = await rig.AutomaticAsync(release);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(1, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task OldFailureWithoutEventBlockDoesNotUndoLaterImport()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await SeedFailureAsync(rig, 3);
        var failed = await rig.Db.DownloadQueue.SingleAsync();
        failed.Added = DateTime.UtcNow.AddDays(-3);
        failed.LastUpdate = DateTime.UtcNow.AddMinutes(1);
        failed.FailedAt = null;
        rig.Db.Blocklist.Add(new BlocklistItem
        {
            EventId = rig.Event.Id + 1,
            Title = failed.Title,
            Indexer = "Unknown",
            Reason = BlocklistReason.FailedDownload,
            BlockedAt = DateTime.UtcNow.AddDays(-2)
        });
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id,
            Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-ImportedWithoutBlock",
            DownloadId = "imported-after-shared-block",
            Status = DownloadStatus.Imported,
            Added = DateTime.UtcNow.AddDays(-2),
            ImportedAt = DateTime.UtcNow.AddDays(-1),
            LastUpdate = DateTime.UtcNow.AddDays(-1),
            Quality = "WEBDL-720p"
        });
        await rig.Db.SaveChangesAsync();
        var release = rig.Release("UFC.9999.2020.09.01.1080p.WEB-DL.H264-UpgradeWithoutBlock");
        release.Quality = "WEBDL-1080p";
        release.Size = 4_000_000_000;

        var outcome = await rig.AutomaticAsync(release);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(1, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task FailureAfterAnotherDownloadImportsStillUsesRetryStop()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await SeedFailureAsync(rig, 3);
        var failed = await rig.Db.DownloadQueue.SingleAsync();
        failed.Added = DateTime.UtcNow.AddDays(-3);
        failed.LastUpdate = DateTime.UtcNow.AddHours(-1);
        failed.FailedAt = null;
        rig.Db.Blocklist.Add(new BlocklistItem
        {
            EventId = rig.Event.Id,
            Title = failed.Title,
            Indexer = "Unknown",
            Reason = BlocklistReason.FailedDownload,
            BlockedAt = DateTime.UtcNow.AddHours(-1)
        });
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id,
            Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-Imported",
            DownloadId = "imported-before-failure",
            Status = DownloadStatus.Imported,
            Added = DateTime.UtcNow.AddDays(-2),
            ImportedAt = DateTime.UtcNow.AddDays(-1),
            LastUpdate = DateTime.UtcNow.AddDays(-1),
            Quality = "WEBDL-720p"
        });
        await rig.Db.SaveChangesAsync();

        var outcome = await rig.Services.GetRequiredService<AutomaticSearchService>()
            .SearchAndDownloadEventAsync(rig.Event.Id, isManualSearch: false);

        Assert.False(outcome.Success);
        Assert.Empty(rig.Transport.SourceRequests);
        Assert.Equal(0, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task RecordedFailureTimeControlsRetryWhenBlockBelongsToAnotherEvent()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await SeedFailureAsync(rig, 3);
        var failed = await rig.Db.DownloadQueue.SingleAsync();
        failed.Added = DateTime.UtcNow.AddDays(-3);
        failed.LastUpdate = DateTime.UtcNow.AddHours(-1);
        failed.FailedAt = DateTime.UtcNow.AddHours(-1);
        rig.Db.Blocklist.Add(new BlocklistItem
        {
            EventId = rig.Event.Id + 1,
            Title = failed.Title,
            Indexer = "Unknown",
            Reason = BlocklistReason.FailedDownload,
            BlockedAt = DateTime.UtcNow.AddDays(-2)
        });
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id,
            Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-OtherImported",
            DownloadId = "other-imported-before-failure",
            Status = DownloadStatus.Imported,
            Added = DateTime.UtcNow.AddDays(-2),
            ImportedAt = DateTime.UtcNow.AddDays(-1),
            LastUpdate = DateTime.UtcNow.AddDays(-1),
            Quality = "WEBDL-720p"
        });
        await rig.Db.SaveChangesAsync();

        var outcome = await rig.Services.GetRequiredService<AutomaticSearchService>()
            .SearchAndDownloadEventAsync(rig.Event.Id, isManualSearch: false);

        Assert.False(outcome.Success);
        Assert.Empty(rig.Transport.SourceRequests);
        Assert.Equal(0, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task LatestFailureControlsRetryWhenDownloadsFailOutOfOrder()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        config.AutoSearchRetryBackoffMinutes = "0";
        await configService.SaveConfigAsync(config);
        rig.Db.DownloadQueue.AddRange(
            new DownloadQueueItem
            {
                EventId = rig.Event.Id,
                Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-OlderJob",
                DownloadId = "older-job-later-failure",
                Status = DownloadStatus.Failed,
                RetryCount = 3,
                Added = DateTime.UtcNow.AddDays(-3),
                FailedAt = DateTime.UtcNow.AddHours(-1),
                LastUpdate = DateTime.UtcNow.AddHours(-1)
            },
            new DownloadQueueItem
            {
                EventId = rig.Event.Id,
                Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-NewerJob",
                DownloadId = "newer-job-earlier-failure",
                Status = DownloadStatus.Failed,
                RetryCount = 1,
                Added = DateTime.UtcNow.AddDays(-2),
                FailedAt = DateTime.UtcNow.AddHours(-2),
                LastUpdate = DateTime.UtcNow.AddHours(-2)
            });
        rig.Db.Blocklist.AddRange(
            new BlocklistItem
            {
                EventId = rig.Event.Id,
                Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-OlderJob",
                Indexer = "Unknown",
                Reason = BlocklistReason.FailedDownload,
                BlockedAt = DateTime.UtcNow.AddHours(-1)
            },
            new BlocklistItem
            {
                EventId = rig.Event.Id,
                Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-NewerJob",
                Indexer = "Unknown",
                Reason = BlocklistReason.FailedDownload,
                BlockedAt = DateTime.UtcNow.AddHours(-2)
            });
        await rig.Db.SaveChangesAsync();

        var outcome = await rig.Services.GetRequiredService<AutomaticSearchService>()
            .SearchAndDownloadEventAsync(rig.Event.Id, isManualSearch: false);

        Assert.False(outcome.Success);
        Assert.Empty(rig.Transport.SourceRequests);
        Assert.Equal(0, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task HeldReleaseDoesNotBypassExhaustedRetryBudget()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true);
        await SeedFailureAsync(rig, 3);
        rig.Db.PendingReleases.Add(new PendingRelease
        {
            EventId = rig.Event.Id,
            Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-Held",
            Guid = "held-retry-budget",
            DownloadUrl = "http://part-source.invalid/held.nzb",
            Indexer = "Part fixture",
            Protocol = "Usenet",
            Quality = "WEBDL-720p",
            Size = 2_000_000_000,
            PublishDate = DateTime.UtcNow.AddHours(-2),
            ReleasableAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await rig.Db.SaveChangesAsync();
        using var reaper = new PendingReleaseReaperService(rig.Services,
            NullLogger<PendingReleaseReaperService>.Instance);
        var run = typeof(PendingReleaseReaperService).GetMethod("ReapAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        await ((Task)run.Invoke(reaper, new object[] { CancellationToken.None })!)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Single(await rig.Db.DownloadQueue.ToListAsync());
        Assert.Equal(PendingReleaseStatus.Cancelled,
            (await rig.Db.PendingReleases.SingleAsync()).Status);
    }

    private static async Task SeedFailureAsync(PartIdentityIntegrationHarness rig, int retries, string? part = null)
    {
        rig.Db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = rig.Event.Id,
            Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-Previous",
            DownloadId = "failed-previous",
            Status = DownloadStatus.Failed,
            Quality = "WEBDL-720p",
            Part = part,
            RetryCount = retries,
            Added = DateTime.UtcNow.AddDays(-8),
            FailedAt = DateTime.UtcNow.AddDays(-7),
            LastUpdate = DateTime.UtcNow.AddDays(-7)
        });
        await rig.Db.SaveChangesAsync();
    }
}
