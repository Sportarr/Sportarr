using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public class CachePolicyEndpointTests
{
    [Theory]
    [InlineData("ignored", "global")]
    [InlineData("preferred", "global")]
    [InlineData("ignored", "source")]
    [InlineData("preferred", "source")]
    [InlineData("ignored", "other")]
    [InlineData("preferred", "other")]
    public async Task ManualPolicySurvivesARealColdThenWarmSearch(string rule, string scope)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        await rig.AddProfileAsync(rule, scope);
        rig.Transport.Results = _ => new[] { rig.Release() };
        var cold = Assert.Single(await rig.ManualAsync());
        Assert.Single(rig.Transport.Searches);
        Assert.Equal(scope == "other" || rule != "ignored", cold.Approved);
        Assert.Equal(scope != "other" && rule == "preferred" ? 100 : 0, cold.CustomFormatScore);
        if (scope != "other" && rule == "ignored")
            Assert.Contains(cold.Rejections, reason => reason.Contains("ignored", StringComparison.OrdinalIgnoreCase));

        var warm = Assert.Single(await rig.ManualAsync());

        Assert.Single(rig.Transport.Searches);
        Assert.Equal(cold.Approved, warm.Approved);
        Assert.Equal(cold.CustomFormatScore, warm.CustomFormatScore);
        Assert.Equal(cold.Score, warm.Score);
        Assert.Equal(cold.Rejections, warm.Rejections);
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(1, true)]
    public async Task ManualRetentionSurvivesCacheReuse(int ageDays, bool expectedApproved)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        await rig.RetentionAsync(10);
        rig.Transport.Results = _ => new[] { rig.Release(ageDays: ageDays) };
        var cold = Assert.Single(await rig.ManualAsync());
        Assert.Equal(expectedApproved, cold.Approved);
        if (!expectedApproved) Assert.Contains(cold.Rejections, reason => reason.Contains("retention:"));

        var warm = Assert.Single(await rig.ManualAsync());

        Assert.Single(rig.Transport.Searches);
        Assert.Equal(expectedApproved, warm.Approved);
        if (!expectedApproved) Assert.Contains(warm.Rejections, reason => reason.Contains("retention:"));
    }

    [Theory]
    [InlineData("ignored")]
    [InlineData("preferred")]
    [InlineData("retention")]
    public async Task OneCacheHitDoesNotErasePolicyFromTheFreshQuery(string rule)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        if (rule == "retention") await rig.RetentionAsync(10);
        else await rig.AddProfileAsync(rule, "global");
        rig.Transport.Results = query => new[] { rig.Release(query["q"], rule == "retention" ? 30 : 1) };
        var first = Assert.Single(await rig.ManualAsync());
        Assert.Single(rig.Transport.Searches);
        rig.Event.League!.SearchQueryTemplate = "cache-query\nfresh-query";
        await rig.Db.SaveChangesAsync();

        var mixed = await rig.ManualAsync();

        Assert.Equal(2, rig.Transport.Searches.Count);
        Assert.Equal(new[] { "cache-query", "fresh-query" }, rig.Transport.Searches.Select(query => query["q"]));
        Assert.Equal(2, mixed.Count);
        var fresh = Assert.Single(mixed.Where(row => row.Guid == "fresh-query"));
        var warm = Assert.Single(mixed.Where(row => row.Guid == "cache-query"));
        foreach (var row in new[] { fresh, warm })
        {
            Assert.Equal(first.Approved, row.Approved);
            Assert.Equal(first.CustomFormatScore, row.CustomFormatScore);
            Assert.Equal(first.Rejections, row.Rejections);
        }
    }

    [Theory]
    [InlineData("format")]
    [InlineData("size")]
    public async Task ManualEvaluationPreservesAnExplicitCachedPackInput(string gate)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        if (gate == "format") rig.Profile.MinFormatScore = 100;
        else rig.Db.QualityDefinitions.Add(new QualityDefinition { Quality = 3, Title = "WEBDL-1080p", MaxSize = 1 });
        await rig.Db.SaveChangesAsync();
        var raw = rig.Release();
        raw.IsPack = true;
        var expected = rig.Services.GetRequiredService<ReleaseEvaluator>().EvaluateRelease(raw, rig.Profile,
            new List<CustomFormat>(), await rig.Db.QualityDefinitions.ToListAsync(), sport: "Soccer", isPack: true);
        Assert.True(expected.Approved);
        // This fixture supplies a pack flag. Ordinary Torznab parsing does not set it.
        var sourceFingerprint = await rig.Services.GetRequiredService<IndexerSearchService>()
            .GetSearchSourceFingerprintAsync(true, rig.Event.League!.Tags);
        rig.Services.GetRequiredService<SearchResultCache>().Store(
            SearchResultCache.RequestKey(new[] { "cache-query" }, rig.Event.League!.Tags,
                10000, false, Sportarr.Api.Helpers.SportarrIdToken.Normalize(rig.Event.ExternalId), sourceFingerprint), new[] { raw });

        var warm = Assert.Single(await rig.ManualAsync());

        Assert.Empty(rig.Transport.Searches);
        Assert.True(warm.IsPack);
        Assert.Equal(expected.Approved, warm.Approved);
        Assert.Equal(expected.CustomFormatScore, warm.CustomFormatScore);
        Assert.Equal(expected.Rejections, warm.Rejections);
    }

    [Fact]
    public async Task AutomaticRetentionRejectsTheSameOldReleaseWhenRawCacheIsWarm()
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        await rig.RetentionAsync(10);
        rig.Transport.Results = _ => new[] { rig.Release(ageDays: 30) };
        var cold = await rig.AutomaticAsync();
        Assert.Equal(1, cold.ReleasesFound);
        Assert.Null(cold.SelectedRelease);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
        Assert.Equal(0, rig.Transport.DescriptorAttempts);

        var warm = await rig.AutomaticAsync();

        Assert.Single(rig.Transport.Searches);
        Assert.Equal(1, warm.ReleasesFound);
        Assert.Null(warm.SelectedRelease);
        Assert.Equal(0, rig.Transport.DescriptorAttempts);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
    }

    [Fact]
    public async Task ManualSearchHandlesDuplicateTorrentHashesWithDifferentCase()
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        const string upperHash = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
        var release = rig.Release();
        release.TorrentInfoHash = upperHash;
        rig.Db.Blocklist.AddRange(
            new BlocklistItem { Title = "First blocked copy", TorrentInfoHash = upperHash, Message = "First" },
            new BlocklistItem { Title = "Second blocked copy", TorrentInfoHash = upperHash.ToLowerInvariant(), Message = "Second" });
        await rig.Db.SaveChangesAsync();
        rig.Transport.Results = _ => new[] { release };

        var result = Assert.Single(await rig.ManualAsync());

        Assert.True(result.IsBlocklisted);
        Assert.Contains("Release is blocklisted", result.Rejections);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NbaSearchUsesOneDatedFallbackWhenNicknameQueryHasNoValidCandidate(bool automatic)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        await ConfigureNbaAsync(rig);
        var wrong = NbaRelease(rig, "wrong-date",
            "NBA.2026.03.01.Oklahoma.City.Thunder.vs.Boston.Celtics.1080p.WEB-DL.H264-GROUP");
        var correct = NbaRelease(rig, "abbreviation-only",
            "NBA.2026.06.13.OKC.vs.BOS.1080p.WEB-DL.H264-GROUP");
        rig.Transport.Results = query => query["q"] switch
        {
            "NBA Thunder Celtics" => new[] { wrong },
            "NBA 2026 06 13" => new[] { correct },
            _ => Array.Empty<ReleaseSearchResult>()
        };

        if (automatic)
        {
            var result = await rig.AutomaticAsync();
            Assert.Equal(correct.Title, result.SelectedRelease);
        }
        else
        {
            var results = await rig.ManualAsync();
            Assert.Contains(results, result => result.Guid == correct.Guid && result.Approved);
        }

        Assert.Equal(new[] { "NBA Thunder Celtics", "NBA 2026 06 13" },
            rig.Transport.Searches.Select(query => query["q"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NbaSearchStopsAfterNicknameQueryWhenItFindsAValidCandidate(bool automatic)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        await ConfigureNbaAsync(rig);
        var primary = NbaRelease(rig, "nickname-title",
            "NBA.2026.06.13.Oklahoma.City.Thunder.vs.Boston.Celtics.1080p.WEB-DL.H264-GROUP");
        rig.Transport.Results = query => query["q"] == "NBA Thunder Celtics"
            ? new[] { primary }
            : Array.Empty<ReleaseSearchResult>();

        if (automatic)
        {
            var result = await rig.AutomaticAsync();
            Assert.Equal(primary.Title, result.SelectedRelease);
        }
        else
        {
            var result = Assert.Single(await rig.ManualAsync());
            Assert.True(result.Approved, string.Join("; ", result.Rejections));
        }

        Assert.Equal(new[] { "NBA Thunder Celtics" },
            rig.Transport.Searches.Select(query => query["q"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NbaSearchDoesNotProbeAgainWhenThePrimaryHasTheRightEventButQualityRejectsIt(bool automatic)
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        await ConfigureNbaAsync(rig);
        rig.Profile.Items.Single().Allowed = false;
        await rig.Db.SaveChangesAsync();
        var primary = NbaRelease(rig, "quality-rejected",
            "NBA.2026.06.13.Oklahoma.City.Thunder.vs.Boston.Celtics.1080p.WEB-DL.H264-GROUP");
        rig.Transport.Results = query => query["q"] == "NBA Thunder Celtics"
            ? new[] { primary }
            : Array.Empty<ReleaseSearchResult>();

        if (automatic)
        {
            var result = await rig.AutomaticAsync();
            Assert.False(result.Success);
        }
        else
        {
            var result = Assert.Single(await rig.ManualAsync());
            Assert.False(result.Approved);
        }

        Assert.Equal(new[] { "NBA Thunder Celtics" },
            rig.Transport.Searches.Select(query => query["q"]));
    }

    [Fact]
    public async Task NbaMinimumAgeCannotReenableProbeAfterAQualityRejectedCorrectCandidate()
    {
        await using var rig = await CachePolicyHttpHarness.CreateAsync();
        await ConfigureNbaAsync(rig);
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        config.IndexerMinimumAgeMinutes = 60;
        await configService.SaveConfigAsync(config);
        rig.Event.ExternalId = "ev-2336155";
        await rig.Db.SaveChangesAsync();
        var qualityRejected = NbaRelease(rig, "quality-rejected",
            "NBA.2026.06.13.Oklahoma.City.Thunder.vs.Boston.Celtics.720p.WEB-DL.H264-GROUP");
        var youngWrongEvent = NbaRelease(rig, "young-wrong-event",
            "NBA.2026.06.13.Oklahoma.City.Thunder.vs.Boston.Celtics.1080p.WEB-DL.H264-GROUP");
        qualityRejected.Size = 4_294_967_296;
        youngWrongEvent.Size = 4_294_967_296;
        youngWrongEvent.PublishDate = rig.Now;
        youngWrongEvent.SportarrEventId = "ev-999999";
        var scorer = rig.Services.GetRequiredService<ReleaseMatchScorer>();
        var matcher = rig.Services.GetRequiredService<ReleaseMatchingService>();
        Assert.True(scorer.CalculateMatchScore(youngWrongEvent.Title, rig.Event) >= ReleaseMatchScorer.AutoGrabMatchScore);
        Assert.True(matcher.ValidateRelease(youngWrongEvent, rig.Event, null, false).IsHardRejection);
        rig.Transport.Results = query => query["q"] == "NBA Thunder Celtics"
            ? new[] { qualityRejected, youngWrongEvent }
            : Array.Empty<ReleaseSearchResult>();

        var result = await rig.AutomaticAsync();

        Assert.False(result.Success);
        Assert.Equal(new[] { "NBA Thunder Celtics" },
            rig.Transport.Searches.Select(query => query["q"]));
    }

    private static async Task ConfigureNbaAsync(CachePolicyHttpHarness rig)
    {
        rig.Event.League!.Name = "NBA";
        rig.Event.League.Sport = "Basketball";
        rig.Event.League.SearchQueryTemplate = null;
        rig.Event.Title = "Oklahoma City Thunder vs Boston Celtics";
        rig.Event.Sport = "Basketball";
        rig.Event.ExternalId = "";
        rig.Event.EventDate = new DateTime(2026, 6, 14, 1, 0, 0, DateTimeKind.Utc);
        rig.Event.BroadcastDate = new DateTime(2026, 6, 13);
        rig.Event.HomeTeamId = 30;
        rig.Event.AwayTeamId = 40;
        rig.Event.HomeTeamName = "Oklahoma City Thunder";
        rig.Event.AwayTeamName = "Boston Celtics";
        await rig.Db.SaveChangesAsync();
    }

    private static ReleaseSearchResult NbaRelease(CachePolicyHttpHarness rig, string guid, string title) => new()
    {
        Title = title,
        Guid = guid,
        DownloadUrl = "http://" + rig.Transport.Host + "/payload/" + guid,
        Indexer = rig.Indexer.Name,
        IndexerId = rig.Indexer.Id,
        Protocol = "Torrent",
        Seeders = 20,
        PublishDate = rig.Now.AddDays(-1),
        Size = 1_073_741_824
    };
}
