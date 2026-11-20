using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Tests.Services;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// A release picked by hand stores its source, the score the app computes
/// again at the grab, and the identity verdict.
/// </summary>
public class InteractiveGrabVerdictTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ALowScoringPickStoresTheComputedScore()
    {
        await using var rig = await ReleaseVerdictServiceTests.SoccerRigAsync();
        var release = await SearchedReleaseAsync(rig, ReleaseVerdictServiceTests.SoccerLoose, rig.Event.EventDate.AddHours(4));

        await rig.GrabAsync(release, ManualSearch);

        var grab = await rig.Db.GrabHistory.AsNoTracking().SingleAsync();
        Assert.Equal(GrabSource.InteractivePick, grab.GrabSource);
        Assert.Equal(release.MatchScore, grab.MatchScoreAtGrab);
        Assert.InRange(grab.MatchScoreAtGrab!.Value, ReleaseMatchScorer.MinimumMatchScore, ReleaseMatchScorer.AutoGrabMatchScore - 1);
        Assert.False(grab.IdentityRejectedAtGrab);
    }

    [Fact]
    public async Task APostedScoreIsNotTrusted()
    {
        await using var rig = await ReleaseVerdictServiceTests.SoccerRigAsync();
        var release = await SearchedReleaseAsync(rig, ReleaseVerdictServiceTests.SoccerLoose, rig.Event.EventDate.AddHours(4));
        var computed = release.MatchScore;
        release.MatchScore = 90;

        await rig.GrabAsync(release, ManualSearch);

        var grab = await rig.Db.GrabHistory.AsNoTracking().SingleAsync();
        Assert.Equal(computed, grab.MatchScoreAtGrab);
        Assert.True(grab.MatchScoreAtGrab < ReleaseMatchScorer.AutoGrabMatchScore, $"score {grab.MatchScoreAtGrab}");
    }

    [Fact]
    public async Task AHardRejectedPickStoresTheRejection()
    {
        await using var rig = await ReleaseVerdictServiceTests.SoccerRigAsync();
        const string title = "FIFA.World.Cup.2020.09.01.Spain.vs.Belgium.720p.WEB-DL.H264-Early";
        var release = await SearchedReleaseAsync(rig, title, rig.Event.EventDate.AddDays(-8));

        await rig.GrabAsync(release, ManualSearch);

        var grab = await rig.Db.GrabHistory.AsNoTracking().SingleAsync();
        Assert.Equal(GrabSource.InteractivePick, grab.GrabSource);
        Assert.True(grab.IdentityRejectedAtGrab);
        Assert.NotNull(grab.MatchScoreAtGrab);
    }

    [Fact]
    public async Task AManualSearchPackStoresThePickWithNoScore()
    {
        await using var rig = await ReleaseVerdictServiceTests.SoccerRigAsync();
        var release = rig.Release("FIFA.World.Cup.2020.720p.WEB-DL.H264-Pack", isPack: true, suffix: "pack");

        await rig.GrabAsync(release, ManualSearch);

        var grab = await rig.Db.GrabHistory.AsNoTracking().SingleAsync();
        Assert.Equal(GrabSource.InteractivePick, grab.GrabSource);
        Assert.Null(grab.MatchScoreAtGrab);
        Assert.False(grab.IdentityRejectedAtGrab);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ASeasonSearchGrabIsNotAPickByHand(bool seasonPack)
    {
        await using var rig = await ReleaseVerdictServiceTests.SoccerRigAsync();
        var title = seasonPack ? "FIFA.World.Cup.2020.720p.WEB-DL.H264-Pack" : ReleaseVerdictServiceTests.SoccerLoose;
        var release = rig.Release(title, suffix: "season");

        using var response = await rig.Client.PostAsJsonAsync("/api/release/grab", SeasonSearchBody(rig, release, seasonPack));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var grab = await rig.Db.GrabHistory.AsNoTracking().SingleAsync();
        Assert.Equal(GrabSource.SeasonSearch, grab.GrabSource);
        Assert.Null(grab.MatchScoreAtGrab);
        Assert.False(grab.IdentityRejectedAtGrab);
    }

    [Fact]
    public async Task AGrabWithNoMarkerStoresAnUnknownSource()
    {
        await using var rig = await ReleaseVerdictServiceTests.SoccerRigAsync();
        var release = await SearchedReleaseAsync(rig, ReleaseVerdictServiceTests.SoccerLoose, rig.Event.EventDate.AddHours(4));

        await rig.GrabAsync(release);

        var grab = await rig.Db.GrabHistory.AsNoTracking().SingleAsync();
        Assert.Equal(GrabSource.Unknown, grab.GrabSource);
        Assert.Null(grab.MatchScoreAtGrab);
        Assert.False(grab.IdentityRejectedAtGrab);
    }

    private const string ManualSearch = "manualSearch";

    /// <summary>The body that the season search list posts. It carries no part, indexer id or event id token.</summary>
    private static object SeasonSearchBody(PartIdentityIntegrationHarness rig, ReleaseSearchResult release, bool seasonPack) => new
    {
        title = release.Title,
        guid = release.Guid,
        downloadUrl = release.DownloadUrl,
        indexer = release.Indexer,
        protocol = release.Protocol,
        size = release.Size,
        quality = release.Quality,
        source = release.Source,
        codec = release.Codec,
        language = release.Language,
        seeders = release.Seeders,
        leechers = release.Leechers,
        publishDate = release.PublishDate,
        score = release.Score,
        qualityScore = release.QualityScore,
        torrentInfoHash = release.TorrentInfoHash,
        eventId = rig.Event.Id,
        matchedEventIds = seasonPack ? new[] { rig.Event.Id } : null,
        isSeasonPack = seasonPack,
        overrideBlocklist = false,
        pickedFrom = "seasonSearch",
    };

    [Fact]
    public async Task AFailedVerdictStillGrabsAndStoresNoScore()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true,
            title: "Spain vs Belgium", sport: "Soccer", leagueName: "FIFA World Cup",
            configureServices: services => services.AddSingleton<ReleaseVerdictService>(sp => new ThrowingVerdicts(sp)));
        var release = rig.Release(ReleaseVerdictServiceTests.SoccerLoose, suffix: "throw");

        await rig.GrabAsync(release, ManualSearch);

        Assert.Equal(1, rig.Transport.ClientAdds);
        var grab = await rig.Db.GrabHistory.AsNoTracking().SingleAsync();
        Assert.Equal(GrabSource.InteractivePick, grab.GrabSource);
        Assert.Null(grab.MatchScoreAtGrab);
        Assert.False(grab.IdentityRejectedAtGrab);
    }

    /// <summary>Runs the manual search and returns the row as the grab dialog posts it.</summary>
    private static async Task<ReleaseSearchResult> SearchedReleaseAsync(PartIdentityIntegrationHarness rig, string title, DateTime published)
    {
        ReleaseVerdictServiceTests.SetFeed(rig, (title, published));
        var row = await ReleaseVerdictServiceTests.SearchRowAsync(rig, title);
        return row.Deserialize<ReleaseSearchResult>(Web)!;
    }

    private sealed class ThrowingVerdicts : ReleaseVerdictService
    {
        public ThrowingVerdicts(IServiceProvider services) : base(
            services.GetRequiredService<SportarrDbContext>(),
            services.GetRequiredService<ConfigService>(),
            services.GetRequiredService<ReleaseMatchingService>(),
            services.GetRequiredService<ReleaseMatchScorer>())
        {
        }

        public override Task<ReleaseVerdict> ComputeAsync(ReleaseSearchResult release, int eventId, string? part,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The verdict test forces a failure.");
    }
}
