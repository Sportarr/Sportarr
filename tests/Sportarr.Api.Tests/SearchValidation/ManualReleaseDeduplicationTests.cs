using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.SearchValidation;

public sealed class ManualReleaseDeduplicationTests
{
    [Fact]
    public void SameForumTopicWithDifferentSearchHighlightsAppearsOnce()
    {
        var deduplicator = new ManualReleaseDeduplicator();
        var first = Release("guid-1", "https://720pier.ru/viewtopic.php?t=76757&hilit=NHL+2026");
        var second = Release("guid-2", "https://720pier.ru/viewtopic.php?t=76757&hilit=NHL+2026+09");

        Assert.True(deduplicator.TryAdd(first));
        Assert.False(deduplicator.TryAdd(second));
    }

    [Fact]
    public void DistinctTorrentHashesOnOneTopicRemainSeparate()
    {
        var deduplicator = new ManualReleaseDeduplicator();
        var first = Release("guid-1", "https://720pier.ru/viewtopic.php?t=76757&hilit=NHL+2026");
        var second = Release("guid-2", "https://720pier.ru/viewtopic.php?t=76757&hilit=NHL+2026+09");
        first.TorrentInfoHash = "AAAA";
        second.TorrentInfoHash = "BBBB";

        Assert.True(deduplicator.TryAdd(first));
        Assert.True(deduplicator.TryAdd(second));
    }

    [Fact]
    public void SameTopicWithDifferentTitleSizeOrIndexerRemainsSeparate()
    {
        var deduplicator = new ManualReleaseDeduplicator();
        var first = Release("guid-1", "https://720pier.ru/viewtopic.php?t=76757");
        var otherTitle = Release("guid-2", "https://720pier.ru/viewtopic.php?t=76757");
        otherTitle.Title = "NHL other game";
        var otherSize = Release("guid-3", "https://720pier.ru/viewtopic.php?t=76757");
        otherSize.Size++;
        var otherIndexer = Release("guid-4", "https://720pier.ru/viewtopic.php?t=76757");
        otherIndexer.Indexer = "Other";

        Assert.True(deduplicator.TryAdd(first));
        Assert.True(deduplicator.TryAdd(otherTitle));
        Assert.True(deduplicator.TryAdd(otherSize));
        Assert.True(deduplicator.TryAdd(otherIndexer));
    }

    [Fact]
    public void EmptyGuidAndUnrelatedUrlsKeepTheirExistingBehavior()
    {
        var deduplicator = new ManualReleaseDeduplicator();
        var first = Release("", "https://example.com/torrent/1");
        var second = Release("", "https://example.com/torrent/1");

        Assert.True(deduplicator.TryAdd(first));
        Assert.True(deduplicator.TryAdd(second));
    }

    private static ReleaseSearchResult Release(string guid, string infoUrl) => new()
    {
        Title = "NHL 2026 09 30 Vegas vs Los Angeles",
        Guid = guid,
        DownloadUrl = "https://example.com/download",
        InfoUrl = infoUrl,
        Indexer = "720Pier",
        Size = 7_350_000_000
    };
}
