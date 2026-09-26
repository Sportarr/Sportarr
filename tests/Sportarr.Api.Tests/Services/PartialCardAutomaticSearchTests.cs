using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PartialCardAutomaticSearchTests
{
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
