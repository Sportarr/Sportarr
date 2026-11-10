using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

public class DelayProfileQualityOrderTests
{
    [Fact]
    public void ProfileOrderWinsBeforeCustomFormatScore()
    {
        using var db = Db();
        var service = Service(db);
        var profile = Profile(
            Item("HDTV-1080p", 6),
            Item("WEBDL-2160p", 19));
        var preferred = Release("Event.1080p.HDTV", "HDTV-1080p", 0);
        var other = Release("Event.2160p.WEB-DL", "WEBDL-2160p", 5000);

        service.SelectBestReleaseWithDelayProfile(
                [other, preferred], new DelayProfile(), profile, "preferAndUpgrade")
            .Should().BeSameAs(preferred);
    }

    [Fact]
    public void GroupedQualitiesUseCustomFormatScore()
    {
        using var db = Db();
        var service = Service(db);
        var profile = Profile(Group("Preferred", Item("HDTV-1080p", 6), Item("WEBDL-2160p", 19)));
        var existingScore = Release("Event.1080p.HDTV", "HDTV-1080p", 2000);
        var lowerScore = Release("Event.2160p.WEB-DL", "WEBDL-2160p", 560);

        service.SelectBestReleaseWithDelayProfile(
                [lowerScore, existingScore], new DelayProfile(), profile, "preferAndUpgrade")
            .Should().BeSameAs(existingScore);
    }

    [Fact]
    public void HighestQualityBypassUsesTheBestAllowedQualityInTheProfile()
    {
        using var db = Db();
        var service = Service(db);
        var profile = Profile(Item("WEBDL-1080p", 15), Item("WEBDL-720p", 5));
        var delay = new DelayProfile { UsenetDelay = 60, PreferredProtocol = "Usenet", BypassIfHighestQuality = true };
        var best = Release("Event.1080p.WEB-DL", "WEBDL-1080p", 0);

        service.SelectBestReleaseWithDelayProfile([best], delay, profile).Should().BeSameAs(best);
    }

    [Fact]
    public void HighestQualityBypassIgnoresTheBestReleaseFoundWhenTheProfileAllowsBetter()
    {
        using var db = Db();
        var service = Service(db);
        var profile = Profile(Item("WEBDL-1080p", 15), Item("WEBDL-720p", 5));
        var delay = new DelayProfile { UsenetDelay = 60, PreferredProtocol = "Usenet", BypassIfHighestQuality = true };
        var onlyRelease = Release("Event.720p.WEB-DL", "WEBDL-720p", 0);

        service.SelectBestReleaseWithDelayProfile([onlyRelease], delay, profile)
            .Should().BeNull("a 1080p release can still arrive during the delay");
    }

    [Fact]
    public void HighestQualityBypassNeedsThePreferredProtocol()
    {
        using var db = Db();
        var service = Service(db);
        var profile = Profile(Item("WEBDL-1080p", 15), Item("WEBDL-720p", 5));
        var delay = new DelayProfile
        {
            UsenetDelay = 60, TorrentDelay = 60, PreferredProtocol = "Usenet", BypassIfHighestQuality = true
        };
        var torrent = Release("Event.1080p.WEB-DL", "WEBDL-1080p", 0);
        torrent.Protocol = "Torrent";

        service.SelectBestReleaseWithDelayProfile([torrent], delay, profile).Should().BeNull();
    }

    [Fact]
    public void HighestQualityBypassSkipsDisallowedQualitiesAtTheTop()
    {
        using var db = Db();
        var service = Service(db);
        var top = Item("WEBDL-2160p", 19);
        top.Allowed = false;
        var profile = Profile(top, Item("WEBDL-1080p", 15), Item("WEBDL-720p", 5));
        var delay = new DelayProfile { UsenetDelay = 60, PreferredProtocol = "Usenet", BypassIfHighestQuality = true };

        var hd = Release("Event.1080p.WEB-DL", "WEBDL-1080p", 0);
        var sd = Release("Event.720p.WEB-DL", "WEBDL-720p", 0);

        service.SelectBestReleaseWithDelayProfile([hd], delay, profile).Should().BeSameAs(hd);
        service.SelectBestReleaseWithDelayProfile([sd], delay, profile).Should().BeNull();
    }

    private static SportarrDbContext Db() => new(
        new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static DelayProfileService Service(SportarrDbContext db)
    {
        var evaluator = new ReleaseEvaluator(
            Mock.Of<ILogger<ReleaseEvaluator>>(),
            new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()),
            new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>()));
        return new DelayProfileService(db, evaluator, Mock.Of<ILogger<DelayProfileService>>());
    }

    private static ReleaseSearchResult Release(string title, string quality, int customFormatScore) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "https://example.invalid/download",
        Indexer = "TestIndexer",
        Protocol = "Usenet",
        Quality = quality,
        CustomFormatScore = customFormatScore,
        PublishDate = DateTime.UtcNow,
        Size = 1024,
    };

    private static QualityProfile Profile(params QualityItem[] items) => new()
    {
        Name = "Test",
        Items = items.ToList(),
    };

    private static QualityItem Item(string name, int quality) => new()
    {
        Name = name,
        Quality = quality,
        Allowed = true,
    };

    private static QualityItem Group(string name, params QualityItem[] items) => new()
    {
        Name = name,
        Quality = 0,
        Allowed = true,
        Items = items.ToList(),
    };
}
