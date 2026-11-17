using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The Search button runs the automatic search path with isManualSearch set.
/// That search still picks the release itself, so the match floor must hold.
/// </summary>
public class SearchButtonMatchFloorTests
{
    // The name has the competition and the date but no teams.
    // Any match played that day fits it.
    private const string LooseTitle = "FIFA.World.Cup.2020.09.01.720p.WEB-DL.H264-Loose";
    private const string ExactTitle = "FIFA.World.Cup.2020.09.01.Spain.vs.Belgium.720p.WEB-DL.H264-Exact";

    [Fact]
    public async Task ButtonSearchDoesNotGrabAReleaseBelowTheMatchFloor()
    {
        await using var rig = await CreateRigAsync();
        var loose = Release(rig, LooseTitle, "loose");
        AssertLowConfidence(rig, LooseTitle);

        var outcome = await SearchAsync(rig, manual: true, loose);

        Assert.False(outcome.Success, outcome.Message);
        Assert.Equal("Found 1 release, but none clearly match this event. Use Manual Search to pick one.", outcome.Message);
        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());

        var record = await LatestRecordAsync(rig);
        Assert.True(record.IsManual);
        Assert.Contains("none matched this event closely enough", record.Headline);
        var row = Assert.Single(record.Releases!);
        Assert.Equal(LooseTitle, row.Title);
        Assert.Equal("rejected", row.Status);
        Assert.Contains(row.Reasons, reason => reason.StartsWith(SearchHistoryService.LowConfidencePrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ButtonSearchStillGrabsAReleaseAboveTheMatchFloor()
    {
        await using var rig = await CreateRigAsync();
        var exact = Release(rig, ExactTitle, "exact");
        AssertHighConfidence(rig, ExactTitle);

        var outcome = await SearchAsync(rig, manual: true, exact);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(ExactTitle, outcome.SelectedRelease);
        Assert.Equal(1, rig.Transport.ClientAdds);
        var queued = await rig.Db.DownloadQueue.SingleAsync();
        Assert.True(queued.IsManualSearch);
        var record = await LatestRecordAsync(rig);
        Assert.True(record.IsManual);
        Assert.Equal(SearchOutcome.Grabbed, record.Outcome);
    }

    [Fact]
    public async Task ButtonSearchPicksTheConfidentReleaseOverALooseOne()
    {
        await using var rig = await CreateRigAsync();
        var loose = Release(rig, LooseTitle, "loose");
        var exact = Release(rig, ExactTitle, "exact");

        var outcome = await SearchAsync(rig, manual: true, loose, exact);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(ExactTitle, outcome.SelectedRelease);
        Assert.Equal(1, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task ButtonSearchStillSearchesAnUnmonitoredEvent()
    {
        await using var rig = await CreateRigAsync();
        rig.Event.Monitored = false;
        await rig.Db.SaveChangesAsync();

        var automatic = await SearchAsync(rig, manual: false, Release(rig, ExactTitle, "exact"));
        Assert.False(automatic.Success);
        Assert.Contains("unmonitored", automatic.Message);
        Assert.Equal(0, rig.Transport.ClientAdds);

        var button = await SearchAsync(rig, manual: true, Release(rig, ExactTitle, "exact"));

        Assert.True(button.Success, button.Message);
        Assert.Equal(1, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task AutomaticSearchStillHoldsAReleaseBelowTheMatchFloor()
    {
        await using var rig = await CreateRigAsync();

        var outcome = await SearchAsync(rig, manual: false, Release(rig, LooseTitle, "loose"));

        Assert.False(outcome.Success, outcome.Message);
        Assert.Equal("Found 1 release, but none clearly match this event. Use Manual Search to pick one.", outcome.Message);
        Assert.Equal(0, rig.Transport.ClientAdds);
        var record = await LatestRecordAsync(rig);
        Assert.False(record.IsManual);
        var row = Assert.Single(record.Releases!);
        Assert.Contains(row.Reasons, reason => reason.StartsWith(SearchHistoryService.LowConfidencePrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AutomaticSearchStillGrabsAReleaseAboveTheMatchFloor()
    {
        await using var rig = await CreateRigAsync();

        var outcome = await SearchAsync(rig, manual: false, Release(rig, ExactTitle, "exact"));

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.False((await rig.Db.DownloadQueue.SingleAsync()).IsManualSearch);
        Assert.False((await LatestRecordAsync(rig)).IsManual);
    }

    [Fact]
    public async Task MinimumAgeHoldsAutomaticSearchButNotTheButton()
    {
        await using var rig = await CreateRigAsync();
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        config.IndexerMinimumAgeMinutes = 60;
        await configService.SaveConfigAsync(config);

        var automaticRelease = Release(rig, ExactTitle, "exact");
        automaticRelease.PublishDate = DateTime.UtcNow.AddMinutes(-5);
        var automatic = await SearchAsync(rig, manual: false, automaticRelease);
        Assert.False(automatic.Success);
        Assert.Equal(0, rig.Transport.ClientAdds);
        var held = Assert.Single((await LatestRecordAsync(rig)).Releases!);
        Assert.Contains(held.Reasons, reason => reason.StartsWith(SearchHistoryService.MinimumAgePrefix, StringComparison.Ordinal));

        var buttonRelease = Release(rig, ExactTitle, "exact");
        buttonRelease.PublishDate = DateTime.UtcNow.AddMinutes(-5);
        var button = await SearchAsync(rig, manual: true, buttonRelease);

        Assert.True(button.Success, button.Message);
        Assert.Equal(1, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task ButtonPartSearchStillGrabsAConfidentFightCardPart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true, relational: true,
            title: "UFC 300");
        const string title = "UFC.300.Prelims.720p.WEB-DL.H264-Part";
        var prelims = rig.Release(title, "Prelims", suffix: "prelims");
        prelims.Size = 2_000_000_000;
        var score = rig.Services.GetRequiredService<ReleaseMatchScorer>()
            .CalculateMatchScore(title, rig.Event, requestedPart: "Prelims", enableMultiPartEpisodes: true);
        Assert.True(score >= ReleaseMatchScorer.AutoGrabMatchScore, $"score {score}");

        var outcome = await SearchAsync(rig, manual: true, "Prelims", prelims);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(title, outcome.SelectedRelease);
        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Equal("Prelims", (await rig.Db.DownloadQueue.SingleAsync()).Part);
    }

    private static Task<PartIdentityIntegrationHarness> CreateRigAsync() =>
        PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true,
            title: "Spain vs Belgium", sport: "Soccer", leagueName: "FIFA World Cup");

    private static ReleaseSearchResult Release(PartIdentityIntegrationHarness rig, string title, string suffix)
    {
        var release = rig.Release(title, suffix: suffix);
        release.Size = 2_000_000_000;
        return release;
    }

    private static void AssertLowConfidence(PartIdentityIntegrationHarness rig, string title)
    {
        var score = rig.Services.GetRequiredService<ReleaseMatchScorer>()
            .CalculateMatchScore(title, rig.Event, enableMultiPartEpisodes: false);
        Assert.InRange(score, ReleaseMatchScorer.MinimumMatchScore, ReleaseMatchScorer.AutoGrabMatchScore - 1);
    }

    private static void AssertHighConfidence(PartIdentityIntegrationHarness rig, string title)
    {
        var score = rig.Services.GetRequiredService<ReleaseMatchScorer>()
            .CalculateMatchScore(title, rig.Event, enableMultiPartEpisodes: false);
        Assert.True(score >= ReleaseMatchScorer.AutoGrabMatchScore, $"score {score}");
    }

    /// <summary>
    /// Seeds the search cache with the releases and runs one event search
    /// that also writes its search record.
    /// </summary>
    private static Task<AutomaticSearchResult> SearchAsync(PartIdentityIntegrationHarness rig, bool manual,
        params ReleaseSearchResult[] releases) => SearchAsync(rig, manual, null, releases);

    private static async Task<AutomaticSearchResult> SearchAsync(PartIdentityIntegrationHarness rig, bool manual,
        string? part, params ReleaseSearchResult[] releases)
    {
        var queries = rig.Services.GetRequiredService<EventQueryService>()
            .BuildEventQueries(rig.Event, part, rig.Event.League?.SearchQueryTemplate);
        var fingerprint = await rig.Services.GetRequiredService<IndexerSearchService>()
            .GetSearchSourceFingerprintAsync(manual, rig.Event.League?.Tags);
        var key = SearchResultCache.RequestKey(queries, rig.Event.League?.Tags ?? new List<int>(),
            100, true, Sportarr.Api.Helpers.SportarrIdToken.Normalize(rig.Event.ExternalId), fingerprint);
        rig.Services.GetRequiredService<SearchResultCache>().Store(key, releases);

        var history = ActivatorUtilities.CreateInstance<SearchHistoryService>(rig.Services);
        var search = ActivatorUtilities.CreateInstance<AutomaticSearchService>(rig.Services, history);
        return await search.SearchAndDownloadEventAsync(rig.Event.Id, null, part, manual);
    }

    private static async Task<SearchHistoryDto> LatestRecordAsync(PartIdentityIntegrationHarness rig)
    {
        var entry = await rig.Db.SearchHistory.AsNoTracking()
            .Where(s => s.EventId == rig.Event.Id)
            .OrderByDescending(s => s.Id)
            .FirstAsync();
        return SearchHistoryService.ToDto(entry, includeReleases: true);
    }
}
