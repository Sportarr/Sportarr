using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Each automatic writer of grab history records how the grab was made.
/// </summary>
public class GrabHistorySourceTests
{
    private const string ExactTitle = "FIFA.World.Cup.2020.09.01.Spain.vs.Belgium.720p.WEB-DL.H264-Exact";

    [Fact]
    public async Task AnRssGrabRecordsRssWithNoMatchScore()
    {
        await using var rig = await CreateRssRigAsync();

        await rig.Services.GetRequiredService<RssSyncService>().SyncNowAsync(CancellationToken.None);

        Assert.Equal(1, rig.Transport.ClientAdds);
        var grab = await rig.Db.GrabHistory.AsNoTracking().SingleAsync();
        Assert.Equal(GrabSource.Rss, grab.GrabSource);
        Assert.Null(grab.MatchScoreAtGrab);
        Assert.False(grab.IdentityRejectedAtGrab);
        Assert.False(grab.CrowdHintShown);
    }

    [Fact]
    public async Task AnAutomaticGrabRecordsAutomaticWithTheWinnersScore()
    {
        await using var rig = await CreateSearchRigAsync();

        var outcome = await SearchAsync(rig, manual: false);

        Assert.True(outcome.Success, outcome.Message);
        var grab = await rig.Db.GrabHistory.AsNoTracking().SingleAsync();
        Assert.Equal(GrabSource.Automatic, grab.GrabSource);
        Assert.Equal(await WinnerScoreAsync(rig), grab.MatchScoreAtGrab);
        Assert.True(grab.MatchScoreAtGrab >= ReleaseMatchScorer.AutoGrabMatchScore, $"score {grab.MatchScoreAtGrab}");
        Assert.False(grab.IdentityRejectedAtGrab);
    }

    [Fact]
    public async Task TheSearchButtonRecordsAUserStartedAutomaticGrab()
    {
        await using var rig = await CreateSearchRigAsync();

        var outcome = await SearchAsync(rig, manual: true);

        Assert.True(outcome.Success, outcome.Message);
        var grab = await rig.Db.GrabHistory.AsNoTracking().SingleAsync();
        Assert.Equal(GrabSource.UserStartedAutomatic, grab.GrabSource);
        Assert.Equal(await WinnerScoreAsync(rig), grab.MatchScoreAtGrab);
        Assert.NotNull(grab.MatchScoreAtGrab);
        Assert.False(grab.IdentityRejectedAtGrab);
    }

    private static Task<PartIdentityIntegrationHarness> CreateSearchRigAsync() =>
        PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true,
            title: "Spain vs Belgium", sport: "Soccer", leagueName: "FIFA World Cup");

    /// <summary>
    /// Seeds the search cache and runs one event search that also writes its
    /// search record, so the test can read the winner's score.
    /// </summary>
    private static async Task<AutomaticSearchResult> SearchAsync(PartIdentityIntegrationHarness rig, bool manual)
    {
        var release = rig.Release(ExactTitle, suffix: "exact");
        release.Size = 2_000_000_000;
        var queries = rig.Services.GetRequiredService<EventQueryService>()
            .BuildEventQueries(rig.Event, null, rig.Event.League?.SearchQueryTemplate);
        var fingerprint = await rig.Services.GetRequiredService<IndexerSearchService>()
            .GetSearchSourceFingerprintAsync(manual, rig.Event.League?.Tags);
        var key = SearchResultCache.RequestKey(queries, rig.Event.League?.Tags ?? new List<int>(),
            100, true, Sportarr.Api.Helpers.SportarrIdToken.Normalize(rig.Event.ExternalId), fingerprint);
        rig.Services.GetRequiredService<SearchResultCache>().Store(key, new[] { release });

        var history = ActivatorUtilities.CreateInstance<SearchHistoryService>(rig.Services);
        var search = ActivatorUtilities.CreateInstance<AutomaticSearchService>(rig.Services, history);
        return await search.SearchAndDownloadEventAsync(rig.Event.Id, null, null, manual);
    }

    private static async Task<int> WinnerScoreAsync(PartIdentityIntegrationHarness rig)
    {
        var entry = await rig.Db.SearchHistory.AsNoTracking()
            .Where(s => s.EventId == rig.Event.Id)
            .OrderByDescending(s => s.Id)
            .FirstAsync();
        var row = Assert.Single(SearchHistoryService.ToDto(entry, includeReleases: true).Releases!);
        Assert.Equal(ExactTitle, row.Title);
        return row.MatchScore;
    }

    private static async Task<PartIdentityIntegrationHarness> CreateRssRigAsync()
    {
        var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false,
            title: "Alpha vs Beta", sport: "American Football", leagueName: "NFL", relational: true);
        rig.Event.HomeTeamName = "Alpha";
        rig.Event.AwayTeamName = "Beta";
        var indexer = await rig.Db.Indexers.SingleAsync();
        indexer.EnableRss = true;
        indexer.ApiPath = "";
        await rig.Db.SaveChangesAsync();

        XNamespace ns = "http://www.newznab.com/DTD/2010/feeds/attributes/";
        const long size = 2_000_000_000L;
        rig.Transport.RssResponse = new XDocument(new XElement("rss", new XAttribute("version", "2.0"),
            new XElement("channel", new XElement("item",
                new XElement("title", "NFL.2020.09.01.Alpha.vs.Beta.720p.WEB-DL.H264-Fixture"),
                new XElement("guid", "rss-source"),
                new XElement("pubDate", DateTime.UtcNow.AddHours(-2).ToString("R")),
                new XElement("enclosure",
                    new XAttribute("url", "http://part-source.invalid/rss-source.nzb"),
                    new XAttribute("length", size),
                    new XAttribute("type", "application/x-nzb")),
                new XElement(ns + "attr", new XAttribute("name", "size"),
                    new XAttribute("value", size)))))).ToString();
        return rig;
    }
}
