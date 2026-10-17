using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PartialCardAutomaticSearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchSelectsSameQualityScoreUpgradeAfterQualityCutoff(bool manual)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        const string currentTitle = "UFC.9999.2020.09.01.1080p.WEB-DL.H264-PARTFIXTURE";
        var currentFile = await rig.ImportAsync(currentTitle, currentTitle + ".mkv");
        rig.Event.HasFile.Should().BeTrue();
        currentFile.Quality = "WEBDL-1080p";
        currentFile.CustomFormatScore = 2000;
        var profile = await rig.Db.QualityProfiles.SingleAsync();
        var preferredFeed = new CustomFormat
        {
            Name = "Preferred feed",
            Specifications = [new FormatSpecification
            {
                Name = "Release title",
                Implementation = "ReleaseTitle",
                Required = true,
                Fields = new Dictionary<string, object> { ["value"] = "ALTGROUP" },
            }],
        };
        rig.Db.CustomFormats.Add(preferredFeed);
        await rig.Db.SaveChangesAsync();
        profile.CutoffQuality = 15;
        profile.CutoffFormatScore = 3000;
        profile.FormatItems = [new ProfileFormatItem { FormatId = preferredFeed.Id, Score = 3500 }];
        await rig.Db.SaveChangesAsync();

        var higherQuality = rig.Release("UFC.9999.2020.09.01.2160p.WEB-DL.H264-PARTFIXTURE", suffix: "higher-quality");
        higherQuality.Quality = "WEBDL-2160p";
        higherQuality.CustomFormatScore = 3500;
        var higherScore = rig.Release("UFC.9999.2020.09.01.1080p.WEB-DL.H264-ALTGROUP", suffix: "higher-score");
        higherScore.Quality = "WEBDL-1080p";
        higherScore.CustomFormatScore = 3500;
        var queryService = rig.Services.GetRequiredService<EventQueryService>();
        var indexerSearch = rig.Services.GetRequiredService<IndexerSearchService>();
        var queries = queryService.BuildEventQueries(rig.Event, null, rig.Event.League?.SearchQueryTemplate);
        var fingerprint = await indexerSearch.GetSearchSourceFingerprintAsync(manual, rig.Event.League?.Tags);
        var key = SearchResultCache.RequestKey(queries, rig.Event.League?.Tags ?? new List<int>(),
            100, true, Sportarr.Api.Helpers.SportarrIdToken.Normalize(rig.Event.ExternalId), fingerprint);
        rig.Services.GetRequiredService<SearchResultCache>().Store(key, new[] { higherQuality, higherScore });

        var result = await rig.Services.GetRequiredService<AutomaticSearchService>()
            .SearchAndDownloadEventAsync(rig.Event.Id, isManualSearch: manual);

        result.SelectedRelease.Should().Be(higherScore.Title);
        result.Success.Should().BeTrue(result.Message);
        rig.Transport.ClientAdds.Should().Be(1);
    }

    [Theory]
    [InlineData("UFC.9999.2020.09.01.PPV.REPACK.720p.WEB-DL.H264-PARTFIXTURE")]
    [InlineData("UFC.9999.2020.09.01.PPV.REPACK.720p.WEB-DL.H264-OTHERGROUP")]
    public async Task PartlessSearch_DoesNotRegrabHeldMainCardRepack(string candidateTitle)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        const string postShow = "UFC.9999.2020.09.01.Post.Show.720p.WEB-DL.H264-PARTFIXTURE";
        const string prelims = "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-PARTFIXTURE";
        const string mainCardRepack = "UFC.9999.2020.09.01.PPV.REPACK.720p.WEB-DL.H264-PARTFIXTURE";

        var postFile = await rig.ImportAsync(postShow, postShow + ".mkv");
        var prelimsFile = await rig.ImportAsync(prelims, prelims + ".mkv");
        var mainFile = await rig.ImportAsync(mainCardRepack, mainCardRepack + ".mkv", storedPart: "Main Card");
        mainFile.PartName.Should().Be("Main Card");
        postFile.CustomFormatScore = 1810;
        prelimsFile.CustomFormatScore = 1810;
        mainFile.CustomFormatScore = 55;
        await rig.Db.SaveChangesAsync();

        var result = await rig.AutomaticAsync(rig.Release(candidateTitle, suffix: "repeat"));

        result.Success.Should().BeFalse();
        rig.Transport.ClientAdds.Should().Be(0);
        if (candidateTitle == mainCardRepack)
            result.Message.Should().Be("Only releases already held for this event were found");
    }

    [Fact]
    public async Task PartlessSearch_DoesNotRegrabMainCardWhilePrelimsAreMissing()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        const string mainCardRepack = "UFC.9999.2020.09.01.PPV.REPACK.720p.WEB-DL.H264-PARTFIXTURE";
        await rig.ImportAsync(mainCardRepack, mainCardRepack + ".mkv", storedPart: "Main Card");
        rig.Event.HasFile.Should().BeFalse();

        var result = await rig.AutomaticAsync(rig.Release(
            "UFC.9999.2020.09.01.PPV.REPACK.720p.WEB-DL.H264-OTHERGROUP", suffix: "repost"));

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Only releases for already downloaded parts were found");
        rig.Transport.ClientAdds.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartlessSearch_UsesPartNumberAfterHeldFileNameEdit(bool cardComplete)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        const string postShow = "UFC.9999.2020.09.01.Post.Show.720p.WEB-DL.H264-PARTFIXTURE";
        const string prelims = "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-PARTFIXTURE";
        const string mainCardRepack = "UFC.9999.2020.09.01.PPV.REPACK.720p.WEB-DL.H264-PARTFIXTURE";
        if (cardComplete)
        {
            await rig.ImportAsync(postShow, postShow + ".mkv");
            await rig.ImportAsync(prelims, prelims + ".mkv");
        }
        var mainFile = await rig.ImportAsync(mainCardRepack, mainCardRepack + ".mkv", storedPart: "Main Card");
        mainFile.PartNumber.Should().Be(3);
        mainFile.PartName = "Main Show";
        await rig.Db.SaveChangesAsync();
        rig.Event.HasFile.Should().Be(cardComplete);

        var result = await rig.AutomaticAsync(rig.Release(
            "UFC.9999.2020.09.01.PPV.REPACK.720p.WEB-DL.H264-OTHERGROUP", suffix: "edited-repost"));

        result.Success.Should().BeFalse();
        rig.Transport.ClientAdds.Should().Be(0);
    }

    [Fact]
    public async Task PartlessSearch_QueuesUndercardAsPrelims()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        const string mainCard = "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.H264-PARTFIXTURE";
        await rig.ImportAsync(mainCard, mainCard + ".mkv");

        var result = await rig.AutomaticAsync(rig.Release(
            "UFC.9999.2020.09.01.Undercard.720p.WEB-DL.H264-PARTFIXTURE", suffix: "undercard"));

        result.Success.Should().BeTrue(result.Message);
        (await rig.Db.DownloadQueue.Where(q => q.Status != DownloadStatus.Imported)
            .SingleAsync()).Part.Should().Be("Prelims");
    }

    [Fact]
    public async Task PartlessSearch_DoesNotQueuePpvForUnmonitoredMainCard()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Event.MonitoredParts = "Prelims";
        await rig.Db.SaveChangesAsync();

        var result = await rig.AutomaticAsync(rig.Release(
            "UFC.9999.2020.09.01.PPV.720p.WEB-DL.H264-PARTFIXTURE", suffix: "ppv-main"));

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Only unmonitored parts available");
        rig.Transport.ClientAdds.Should().Be(0);
    }

    [Fact]
    public async Task PartlessSearch_DoesNotQueueAmbiguousUnmonitoredPrelims()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Event.MonitoredParts = "Main Card";
        await rig.Db.SaveChangesAsync();

        var result = await rig.AutomaticAsync(rig.Release(
            "UFC.9999.2020.09.01.Prelims.and.Main.Card.720p.WEB-DL.H264-PARTFIXTURE",
            suffix: "ambiguous"));

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Only unmonitored parts available");
        rig.Transport.ClientAdds.Should().Be(0);
    }

    [Fact]
    public async Task PartlessSearch_KeepsBareWholeEventEligibleAcrossPartResolution()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        const string mainCard = "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.H264-PARTFIXTURE";
        await rig.ImportAsync(mainCard, mainCard + ".mkv");
        var profile = await rig.Db.QualityProfiles.SingleAsync();
        profile.CutoffQuality = 5;
        profile.CutoffFormatScore = 3000;
        await rig.Db.SaveChangesAsync();
        var wholeEvent = rig.Release(
            "UFC.9999.2020.09.01.1080p.WEB-DL.H264-PARTFIXTURE", suffix: "whole-event");
        wholeEvent.Quality = "WEBDL-1080p";

        var result = await rig.AutomaticAsync(wholeEvent);

        result.Success.Should().BeTrue(result.Message);
        (await rig.Db.DownloadQueue.Where(q => q.Status != DownloadStatus.Imported)
            .SingleAsync()).Part.Should().BeNull();
    }

    [Fact]
    public async Task PartlessAutomaticSearchSelectsMissingPartFromSharedResults()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        const string mainTitle = "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.H264-PARTFIXTURE";
        const string prelimsTitle = "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-PARTFIXTURE";
        await rig.ImportAsync(mainTitle, mainTitle + ".mkv");
        rig.Event.HasFile.Should().BeFalse();
        var profile = await rig.Db.QualityProfiles.SingleAsync();
        profile.UpgradesAllowed = false;
        await rig.Db.SaveChangesAsync();

        var betterMain = rig.Release(
            "UFC.9999.2020.09.01.Main.Card.1080p.WEB-DL.H264-PARTFIXTURE", suffix: "main-upgrade");
        betterMain.Quality = "WEBDL-1080p";
        var prelims = rig.Release(prelimsTitle, suffix: "prelims");
        var queryService = rig.Services.GetRequiredService<EventQueryService>();
        var indexerSearch = rig.Services.GetRequiredService<IndexerSearchService>();
        var queries = queryService.BuildEventQueries(rig.Event, null, rig.Event.League?.SearchQueryTemplate);
        var fingerprint = await indexerSearch.GetSearchSourceFingerprintAsync(false, rig.Event.League?.Tags);
        var key = SearchResultCache.RequestKey(queries, rig.Event.League?.Tags ?? new List<int>(),
            100, true, Sportarr.Api.Helpers.SportarrIdToken.Normalize(rig.Event.ExternalId), fingerprint);
        rig.Services.GetRequiredService<SearchResultCache>().Store(key, new[] { betterMain, prelims });

        var result = await rig.Services.GetRequiredService<AutomaticSearchService>()
            .SearchAndDownloadEventAsync(rig.Event.Id, part: null, isManualSearch: false);

        result.Success.Should().BeTrue(result.Message);
        rig.Transport.ClientAdds.Should().Be(1);
        (await rig.Db.DownloadQueue.Where(q => q.Status != DownloadStatus.Imported)
            .SingleAsync()).Part.Should().Be("Prelims");
        rig.Transport.SourceRequests.Should().ContainSingle()
            .Which.AbsolutePath.Should().Be("/prelims.nzb");
    }

    [Fact]
    public async Task PartlessAutomaticSearchSelectsCompatibleMissingPart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        const string mainTitle = "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.H264-PARTFIXTURE";
        await rig.ImportAsync(mainTitle, mainTitle + ".mkv");
        rig.Event.HasFile.Should().BeFalse();

        var incompatible = rig.Release(
            "UFC.9999.2020.09.01.Prelims.1080p.WEB-DL.H264-PARTFIXTURE", suffix: "prelims-1080");
        incompatible.Quality = "WEBDL-1080p";
        var compatible = rig.Release(
            "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-PARTFIXTURE", suffix: "prelims-720");
        var queryService = rig.Services.GetRequiredService<EventQueryService>();
        var indexerSearch = rig.Services.GetRequiredService<IndexerSearchService>();
        var queries = queryService.BuildEventQueries(rig.Event, null, rig.Event.League?.SearchQueryTemplate);
        var fingerprint = await indexerSearch.GetSearchSourceFingerprintAsync(false, rig.Event.League?.Tags);
        var key = SearchResultCache.RequestKey(queries, rig.Event.League?.Tags ?? new List<int>(),
            100, true, Sportarr.Api.Helpers.SportarrIdToken.Normalize(rig.Event.ExternalId), fingerprint);
        rig.Services.GetRequiredService<SearchResultCache>().Store(key, new[] { incompatible, compatible });

        var result = await rig.Services.GetRequiredService<AutomaticSearchService>()
            .SearchAndDownloadEventAsync(rig.Event.Id, part: null, isManualSearch: false);

        result.Success.Should().BeTrue(result.Message);
        result.SelectedRelease.Should().Be(compatible.Title);
        (await rig.Db.DownloadQueue.Where(q => q.Status != DownloadStatus.Imported)
            .SingleAsync()).Part.Should().Be("Prelims");
        rig.Transport.SourceRequests.Should().ContainSingle()
            .Which.AbsolutePath.Should().Be("/prelims-720.nzb");
    }
}
