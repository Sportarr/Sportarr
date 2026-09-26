using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class RssPackAcquisitionTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("UFC.9999.Main.Card.COMPLETE.BLURAY", false, null, null, false)]
    [InlineData("F1.2026.Round10.Race.COMPLETE", false, null, null, false)]
    [InlineData("F1.2026.Round10.Race.PACK.COMPLETE", false, null, null, true)]
    [InlineData("F1.2026.FULL.SEASON.Race.COMPLETE", false, null, null, true)]
    [InlineData("NFL.Week1.Pack.{sportarr-ev-9900301}", false, "", "", false)]
    [InlineData("unknown.{sportarr-lg-9900300}", false, " ", "", true)]
    [InlineData("unknown", false, null, null, false)]
    [InlineData("unknown", true, null, null, true)]
    [InlineData("F1.2026.Round10.Race.1080p", false, null, null, false)]
    [InlineData("F1.2026.Round10.Qualifying.1080p", false, null, null, false)]
    [InlineData("NFL.2026.Week10.Alpha.vs.Beta", false, null, null, false)]
    [InlineData("NFL.2026.Week10.Pack", false, null, null, true)]
    [InlineData("NFL.2026.Week10.Pack", false, null, "ev-9900301", false)]
    [InlineData("unknown", false, "lg-9900300", null, true)]
    [InlineData("NFL.2026.Week10.Pack.{sportarr-ev-9900301}", false, null, null, false)]
    [InlineData("unknown.{sportarr-lg-9900300}", false, null, null, true)]
    public void ImportIntentUsesReleaseEvidence(string title, bool explicitPack, string? leagueId, string? eventId, bool expected)
    {
        Assert.Equal(expected, PackImportBoundary.IsPackRelease(title, explicitPack, leagueId, eventId));
    }

    [Fact]
    public void CompleteSessionExceptionDoesNotChangeCustomFormatClassification()
    {
        const string title = "UFC.9999.Main.Card.COMPLETE.BLURAY";
        Assert.Equal(Sportarr.Api.Helpers.ReleaseType.Pack, Sportarr.Api.Helpers.ReleaseTypeDetector.Detect(title));
        Assert.False(PackImportBoundary.IsPackRelease(title));
    }

    [Theory]
    [InlineData(null, "NFL.2020.Week1.Pack", true)]
    [InlineData(null, "F1.2020.Round1.Race", false)]
    [InlineData(false, "NFL.2020.Week1.Pack", false)]
    [InlineData(true, "Alpha Beta", true)]
    public async Task DelayedPromotionPreservesRecordedIntentAndClassifiesLegacyRows(bool? stored, string title, bool expected)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        rig.Db.PendingReleases.Add(new PendingRelease {
            EventId = rig.Event.Id, Title = title, Guid = "retained-pack-intent",
            DownloadUrl = "http://part-source.invalid/retained.nzb", Indexer = "Part fixture", Protocol = "Usenet",
            Quality = "WEBDL-720p", QualityScore = 300, Score = 300, IsPack = stored,
            PublishDate = DateTime.UtcNow.AddHours(-2), ReleasableAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await rig.Db.SaveChangesAsync();
        rig.Db.ChangeTracker.Clear();
        using var reaper = new PendingReleaseReaperService(rig.Services, NullLogger<PendingReleaseReaperService>.Instance);
        var method = typeof(PendingReleaseReaperService).GetMethod("ReapAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Task)method.Invoke(reaper, new object[] { CancellationToken.None })!).WaitAsync(TimeSpan.FromSeconds(20));
        rig.Db.ChangeTracker.Clear();
        var queued = await rig.Db.DownloadQueue.SingleAsync();
        Assert.Equal(expected, queued.IsPack);
        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Equal(PendingReleaseStatus.Released, (await rig.Db.PendingReleases.SingleAsync()).Status);
        Assert.Empty(rig.Transport.UnexpectedRequests);
    }

    [Fact]
    public async Task ExpiredRssHoldWaitsForTheBestPendingRelease()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            multipart: false, title: "Alpha vs Beta", sport: "American Football",
            leagueName: "NFL", relational: true);
        rig.Event.HomeTeamName = "Alpha";
        rig.Event.AwayTeamName = "Beta";
        var indexer = await rig.Db.Indexers.SingleAsync();
        indexer.EnableRss = true;
        indexer.ApiPath = "";
        rig.Db.DelayProfiles.Add(new DelayProfile { Order = 1, UsenetDelay = 60 });

        const string baseTitle = "NFL.2020.09.01.Alpha.vs.Beta";
        var published = DateTime.UtcNow.AddHours(-2);
        foreach (var (quality, rank) in new[] { ("720p", 5), ("1080p", 15) })
        {
            rig.Db.PendingReleases.Add(new PendingRelease
            {
                EventId = rig.Event.Id,
                Title = $"{baseTitle}.{quality}.WEB-DL.H264-Fixture",
                Guid = $"held-{quality}",
                DownloadUrl = $"http://part-source.invalid/{quality}.nzb",
                Indexer = indexer.Name,
                IndexerId = indexer.Id,
                Protocol = "Usenet",
                Size = DelayedNflSize(quality),
                Quality = $"WEBDL-{quality}",
                QualityScore = rank,
                Score = rank,
                PublishDate = published,
                ReleasableAt = published.AddHours(1)
            });
        }
        await rig.Db.SaveChangesAsync();

        XNamespace ns = "http://www.newznab.com/DTD/2010/feeds/attributes/";
        rig.Transport.RssResponse = new XDocument(new XElement("rss", new XAttribute("version", "2.0"),
            new XElement("channel", new[] { "720p", "1080p" }.Select(quality =>
                new XElement("item",
                    new XElement("title", $"{baseTitle}.{quality}.WEB-DL.H264-Fixture"),
                    new XElement("guid", $"held-{quality}"),
                    new XElement("pubDate", published.ToString("R")),
                    new XElement("enclosure",
                        new XAttribute("url", $"http://part-source.invalid/{quality}.nzb"),
                        new XAttribute("length", DelayedNflSize(quality)),
                        new XAttribute("type", "application/x-nzb")),
                    new XElement(ns + "attr", new XAttribute("name", "size"),
                        new XAttribute("value", DelayedNflSize(quality)))))))).ToString();

        await rig.Services.GetRequiredService<RssSyncService>().SyncNowAsync(CancellationToken.None);

        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());

        using var reaper = new PendingReleaseReaperService(rig.Services,
            NullLogger<PendingReleaseReaperService>.Instance);
        var method = typeof(PendingReleaseReaperService).GetMethod("ReapAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Task)method.Invoke(reaper, new object[] { CancellationToken.None })!)
            .WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(1, rig.Transport.ClientAdds);
        var queued = await rig.Db.DownloadQueue.SingleAsync();
        Assert.Contains("1080p", queued.Title);
        Assert.Equal(PendingReleaseStatus.Released,
            (await rig.Db.PendingReleases.SingleAsync(p => p.Guid == "held-1080p")).Status);
        Assert.Equal(PendingReleaseStatus.Cancelled,
            (await rig.Db.PendingReleases.SingleAsync(p => p.Guid == "held-720p")).Status);
        Assert.Empty(rig.Transport.UnexpectedRequests);
    }

    [Fact]
    public async Task OlderRssCandidateCannotBypassABetterActiveHold()
    {
        await using var rig = await CreateDelayedNflRssRigAsync();
        var indexer = await rig.Db.Indexers.SingleAsync();
        AddDelayedNflCandidate(rig, indexer, "1080p", "held-high",
            DateTime.UtcNow.AddMinutes(-30), DateTime.UtcNow.AddMinutes(30));
        await rig.Db.SaveChangesAsync();
        SetDelayedNflRssFeed(rig, DateTime.UtcNow.AddHours(-2), ("720p", "new-low"));

        await rig.Services.GetRequiredService<RssSyncService>().SyncNowAsync(CancellationToken.None);

        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Equal(2, await rig.Db.PendingReleases.CountAsync(p => p.Status == PendingReleaseStatus.Pending));
        await RunDelayedNflReaperAsync(rig);
        Assert.Equal(0, rig.Transport.ClientAdds);

        var high = await rig.Db.PendingReleases.SingleAsync(p => p.Guid == "held-high");
        high.ReleasableAt = DateTime.UtcNow.AddMinutes(-1);
        await rig.Db.SaveChangesAsync();
        await RunDelayedNflReaperAsync(rig);

        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Contains("1080p", (await rig.Db.DownloadQueue.SingleAsync()).Title);
        Assert.Equal(PendingReleaseStatus.Cancelled,
            (await rig.Db.PendingReleases.SingleAsync(p => p.Guid == "new-low")).Status);
    }

    [Fact]
    public async Task GuidlessRssCandidateUsesItsUrlWithinAnActiveHold()
    {
        await using var rig = await CreateDelayedNflRssRigAsync();
        var indexer = await rig.Db.Indexers.SingleAsync();
        AddDelayedNflCandidate(rig, indexer, "720p", "",
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddMinutes(-1));
        await rig.Db.SaveChangesAsync();
        SetDelayedNflRssFeed(rig, DateTime.UtcNow.AddHours(-2), ("1080p", null));

        await rig.Services.GetRequiredService<RssSyncService>().SyncNowAsync(CancellationToken.None);

        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Equal(2, await rig.Db.PendingReleases.CountAsync(p => p.Status == PendingReleaseStatus.Pending));
        await RunDelayedNflReaperAsync(rig);

        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Contains("1080p", (await rig.Db.DownloadQueue.SingleAsync()).Title);
    }

    [Fact]
    public async Task RssUpgradeAfterFileImportSurvivesAnEarlierHold()
    {
        await using var rig = await CreateDelayedNflRssRigAsync();
        var indexer = await rig.Db.Indexers.SingleAsync();
        AddDelayedNflCandidate(rig, indexer, "720p", "held-low",
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddMinutes(-1));
        rig.Event.HasFile = true;
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id,
            FilePath = "/fixture/library/alpha-beta-720p.mkv",
            Quality = "WEBDL-720p",
            QualityScore = 5,
            Exists = true
        });
        await rig.Db.SaveChangesAsync();
        SetDelayedNflRssFeed(rig, DateTime.UtcNow.AddHours(-2), ("1080p", "new-high"));

        await rig.Services.GetRequiredService<RssSyncService>().SyncNowAsync(CancellationToken.None);

        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Equal(2, await rig.Db.PendingReleases.CountAsync(p => p.Status == PendingReleaseStatus.Pending));
        await RunDelayedNflReaperAsync(rig);

        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Contains("1080p", (await rig.Db.DownloadQueue.SingleAsync()).Title);
        Assert.Equal(PendingReleaseStatus.Released,
            (await rig.Db.PendingReleases.SingleAsync(p => p.Guid == "new-high")).Status);
        Assert.Equal(PendingReleaseStatus.Cancelled,
            (await rig.Db.PendingReleases.SingleAsync(p => p.Guid == "held-low")).Status);
    }

    [Fact]
    public async Task HeldReleaseDoesNotReplaceAnEqualExistingFile()
    {
        await using var rig = await CreateDelayedNflRssRigAsync();
        var indexer = await rig.Db.Indexers.SingleAsync();
        AddDelayedNflCandidate(rig, indexer, "720p", "held-equal",
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddMinutes(-1));
        rig.Event.HasFile = true;
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id,
            FilePath = "/fixture/library/alpha-beta-720p.mkv",
            Quality = "WEBDL-720p",
            QualityScore = 5,
            Exists = true
        });
        await rig.Db.SaveChangesAsync();

        await RunDelayedNflReaperAsync(rig);

        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Equal(PendingReleaseStatus.Cancelled,
            (await rig.Db.PendingReleases.SingleAsync()).Status);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
    }

    [Fact]
    public async Task CosmeticHoldDoesNotDiscardARealUpgrade()
    {
        await using var rig = await CreateDelayedNflRssRigAsync();
        var indexer = await rig.Db.Indexers.SingleAsync();
        const string existingTitle = "NFL.2020.09.01.Alpha.vs.Beta.1080p.WEB-DL.H264-Fixture";
        rig.Event.HasFile = true;
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id,
            FilePath = "/fixture/library/alpha-beta-1080p.mkv",
            Quality = "WEBDL-1080p",
            QualityScore = 15,
            OriginalTitle = existingTitle,
            Exists = true
        });
        var readyAt = DateTime.UtcNow.AddMinutes(-1);
        AddDelayedNflCandidate(rig, indexer, "1080p", "branded",
            DateTime.UtcNow.AddHours(-2), readyAt);
        var branded = rig.Db.PendingReleases.Local.Single(p => p.Guid == "branded");
        branded.Title = "Sky Sports " + existingTitle;
        branded.CustomFormatScore = 100;
        AddDelayedNflCandidate(rig, indexer, "1080p", "other-encode",
            DateTime.UtcNow.AddHours(-2), readyAt);
        var upgrade = rig.Db.PendingReleases.Local.Single(p => p.Guid == "other-encode");
        upgrade.Title = "NFL.2020.09.01.Alpha.vs.Beta.1080p.WEB-DL.H264-Alt";
        upgrade.DownloadUrl = "http://part-source.invalid/alt.nzb";
        upgrade.CustomFormatScore = 50;
        await rig.Db.SaveChangesAsync();
        var existingFile = await rig.Db.EventFiles.SingleAsync();
        var profile = await rig.Db.QualityProfiles.SingleAsync(p => p.Id == rig.Event.QualityProfileId);
        var config = await rig.Services.GetRequiredService<ConfigService>().GetConfigAsync();
        Assert.True(RssSyncService.TitlesDifferOnlyByBroadcasterBranding(existingTitle, branded.Title),
            $"Existing: {existingTitle}; branded: {branded.Title}; other: {upgrade.Title}");
        Assert.NotNull(Sportarr.Api.Helpers.ExistingFileUpgradeGate.RefusalReason(
            existingFile, branded.Title, branded.Quality, branded.CustomFormatScore, profile, config));
        Assert.Null(Sportarr.Api.Helpers.ExistingFileUpgradeGate.RefusalReason(
            existingFile, upgrade.Title, upgrade.Quality, upgrade.CustomFormatScore, profile, config));

        await RunDelayedNflReaperAsync(rig);

        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Contains("H264-Alt", (await rig.Db.DownloadQueue.SingleAsync()).Title);
        Assert.Equal(PendingReleaseStatus.Cancelled,
            (await rig.Db.PendingReleases.SingleAsync(p => p.Guid == "branded")).Status);
        Assert.Equal(PendingReleaseStatus.Released,
            (await rig.Db.PendingReleases.SingleAsync(p => p.Guid == "other-encode")).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingHoldRemainsOwnedAfterDelaySettingChanges(bool bypass)
    {
        await using var rig = await CreateDelayedNflRssRigAsync();
        var indexer = await rig.Db.Indexers.SingleAsync();
        var now = DateTime.UtcNow;
        AddDelayedNflCandidate(rig, indexer, "720p", "held-low", now.AddHours(-2), now.AddMinutes(-1));
        AddDelayedNflCandidate(rig, indexer, "1080p", "held-high", now.AddHours(-2), now.AddMinutes(-1));
        var profile = await rig.Db.DelayProfiles.SingleAsync();
        if (bypass)
        {
            profile.BypassIfAboveCustomFormatScore = true;
            profile.MinimumCustomFormatScore = 0;
        }
        else
        {
            profile.UsenetDelay = 0;
        }
        await rig.Db.SaveChangesAsync();
        SetDelayedNflRssFeed(rig, now.AddHours(-2), ("720p", "held-low"));

        await rig.Services.GetRequiredService<RssSyncService>().SyncNowAsync(CancellationToken.None);

        Assert.Equal(0, rig.Transport.ClientAdds);
        await RunDelayedNflReaperAsync(rig);
        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Contains("1080p", (await rig.Db.DownloadQueue.SingleAsync()).Title);
    }

    private static async Task<PartIdentityIntegrationHarness> CreateDelayedNflRssRigAsync()
    {
        var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false,
            title: "Alpha vs Beta", sport: "American Football", leagueName: "NFL", relational: true);
        rig.Event.HomeTeamName = "Alpha";
        rig.Event.AwayTeamName = "Beta";
        var indexer = await rig.Db.Indexers.SingleAsync();
        indexer.EnableRss = true;
        indexer.ApiPath = "";
        rig.Db.DelayProfiles.Add(new DelayProfile { Order = 1, UsenetDelay = 60 });
        await rig.Db.SaveChangesAsync();
        return rig;
    }

    private static void AddDelayedNflCandidate(PartIdentityIntegrationHarness rig, Indexer indexer,
        string quality, string guid, DateTime published, DateTime releasableAt)
    {
        var rank = quality == "1080p" ? 15 : 5;
        rig.Db.PendingReleases.Add(new PendingRelease
        {
            EventId = rig.Event.Id,
            Title = $"NFL.2020.09.01.Alpha.vs.Beta.{quality}.WEB-DL.H264-Fixture",
            Guid = guid,
            DownloadUrl = $"http://part-source.invalid/{quality}.nzb",
            Indexer = indexer.Name,
            IndexerId = indexer.Id,
            Protocol = "Usenet",
            Size = DelayedNflSize(quality),
            Quality = $"WEBDL-{quality}",
            QualityScore = rank,
            Score = rank,
            PublishDate = published,
            ReleasableAt = releasableAt
        });
    }

    private static void SetDelayedNflRssFeed(PartIdentityIntegrationHarness rig, DateTime published,
        params (string Quality, string? Guid)[] releases)
    {
        XNamespace ns = "http://www.newznab.com/DTD/2010/feeds/attributes/";
        rig.Transport.RssResponse = new XDocument(new XElement("rss", new XAttribute("version", "2.0"),
            new XElement("channel", releases.Select(release => new XElement("item",
                new XElement("title", $"NFL.2020.09.01.Alpha.vs.Beta.{release.Quality}.WEB-DL.H264-Fixture"),
                release.Guid == null ? null : new XElement("guid", release.Guid),
                new XElement("pubDate", published.ToString("R")),
                new XElement("enclosure",
                    new XAttribute("url", $"http://part-source.invalid/{release.Quality}.nzb"),
                    new XAttribute("length", DelayedNflSize(release.Quality)),
                    new XAttribute("type", "application/x-nzb")),
                new XElement(ns + "attr", new XAttribute("name", "size"),
                    new XAttribute("value", DelayedNflSize(release.Quality)))))))).ToString();
    }

    private static long DelayedNflSize(string quality) => quality == "1080p" ? 4_000_000_000L : 2_000_000_000L;

    private static async Task RunDelayedNflReaperAsync(PartIdentityIntegrationHarness rig)
    {
        using var reaper = new PendingReleaseReaperService(rig.Services,
            NullLogger<PendingReleaseReaperService>.Instance);
        var method = typeof(PendingReleaseReaperService).GetMethod("ReapAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Task)method.Invoke(reaper, new object[] { CancellationToken.None })!)
            .WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Theory]
    [InlineData("rss", false, false)]
    [InlineData("/api/release/push", false, false)]
    [InlineData("/api/v3/release/push", false, false)]
    [InlineData("rss", true, false)]
    [InlineData("/api/release/push", true, false)]
    [InlineData("/api/v3/release/push", true, false)]
    [InlineData("rss", false, true)]
    [InlineData("rss", true, true)]
    [InlineData("/api/release/push", false, true)]
    [InlineData("/api/release/push", true, true)]
    [InlineData("/api/v3/release/push", false, true)]
    [InlineData("/api/v3/release/push", true, true)]
    public async Task AcquisitionPreservesTheIntendedMember(string route, bool delayed, bool completeCard)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(rename: false, multipart: completeCard,
            title: completeCard ? "UFC 9999" : "Alpha vs Beta", sport: completeCard ? "Fighting" : "American Football",
            leagueName: completeCard ? "UFC" : "NFL", relational: true);
        if (completeCard)
        {
            var profile = await rig.Db.QualityProfiles.SingleAsync(p => p.Id == rig.Event.QualityProfileId);
            profile.Items.Add(new QualityItem { Name = "Bluray-1080p", Quality = 7, Allowed = true });
        }
        else
        {
            var definition = await rig.Db.QualityDefinitions.SingleAsync(x => x.Title == "WEBDL-720p");
            definition.MinSize = 0;
            definition.MaxSize = 1;
        }
        rig.Event.ExternalId = "ev-9900301";
        rig.Event.HomeTeamName = "Alpha";
        rig.Event.AwayTeamName = "Beta";
        rig.Event.Round = "1";
        var indexer = await rig.Db.Indexers.SingleAsync();
        indexer.EnableRss = true;
        indexer.ApiPath = "";
        await rig.Db.SaveChangesAsync();
        var title = completeCard ? "UFC.9999.2020.09.01.Main.Card.COMPLETE.BLURAY" :
            "NFL.2020.09.01.Week1.Pack.Alpha.Beta.720p.WEB-DL.H264-Fixture";
        if (delayed)
        {
            rig.Db.DelayProfiles.Add(new DelayProfile { Order = 1, UsenetDelay = 120, PreferredProtocol = "Usenet" });
            await rig.Db.SaveChangesAsync();
        }
        var published = DateTime.UtcNow.AddHours(-1);
        var releaseSize = completeCard ? 10_000_000_000L : 4_000_000_000L;
        if (route == "rss")
        {
            XNamespace ns = "http://www.newznab.com/DTD/2010/feeds/attributes/";
            rig.Transport.RssResponse = new XDocument(new XElement("rss", new XAttribute("version", "2.0"),
                new XElement("channel", new XElement("item", new XElement("title", title),
                    new XElement("guid", "rss-pack-one"), new XElement("pubDate", published.ToString("R")),
                    new XElement("enclosure", new XAttribute("url", "http://part-source.invalid/one.nzb"),
                        new XAttribute("length", releaseSize), new XAttribute("type", "application/x-nzb")),
                    new XElement(ns + "attr", new XAttribute("name", "size"), new XAttribute("value", releaseSize)))))).ToString();
            await rig.Services.GetRequiredService<RssSyncService>().SyncNowAsync(CancellationToken.None);
        }
        else
        {
            using var response = await rig.Client.PostAsJsonAsync(route, new {
                title, downloadUrl = "http://part-source.invalid/one.nzb", protocol = "usenet",
                indexer = "Part fixture", size = releaseSize, publishDate = published
            });
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            using var result = JsonDocument.Parse(body);
            Assert.Equal(!delayed, result.RootElement[0].GetProperty("approved").GetBoolean());
            if (delayed) Assert.True(result.RootElement[0].GetProperty("temporarilyRejected").GetBoolean(), body);
        }
        if (delayed)
        {
            Assert.Equal(0, rig.Transport.ClientAdds);
            var pending = await rig.Db.PendingReleases.SingleAsync();
            Assert.Equal(!completeCard, pending.IsPack);
            pending.ReleasableAt = DateTime.UtcNow.AddMinutes(-1);
            await rig.Db.SaveChangesAsync();
            using var reaper = new PendingReleaseReaperService(rig.Services, NullLogger<PendingReleaseReaperService>.Instance);
            var method = typeof(PendingReleaseReaperService).GetMethod("ReapAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await ((Task)method.Invoke(reaper, new object[] { CancellationToken.None })!).WaitAsync(TimeSpan.FromSeconds(20));
        }
        Assert.Equal(1, rig.Transport.ClientAdds);
        rig.Db.ChangeTracker.Clear();
        var queued = await rig.Db.DownloadQueue.Include(x => x.Event).Include(x => x.DownloadClient).SingleAsync();
        var root = (await rig.Db.RootFolders.SingleAsync()).Path;
        var folder = Path.Combine(Path.GetDirectoryName(root)!, "incoming", title);
        Directory.CreateDirectory(folder);
        var owned = Path.Combine(folder, completeCard ? "opaque.1080p.BLURAY.mkv" : "NFL.2020.09.01.Alpha.vs.Beta.{sportarr-ev-9900301}.720p.WEB-DL.mkv");
        var foreign = Path.Combine(folder, "NFL.2020.09.01.Gamma.vs.Delta.{sportarr-ev-9900302}.720p.WEB-DL.mkv");
        var bytes = Enumerable.Repeat((byte)65, 4096).ToArray();
        await File.WriteAllBytesAsync(owned, bytes);
        if (!completeCard) await File.WriteAllBytesAsync(foreign, Enumerable.Repeat((byte)66, 8192).ToArray());
        queued.Status = DownloadStatus.Completed;
        queued.Progress = 100;
        await rig.Db.SaveChangesAsync();
        var history = await rig.Services.GetRequiredService<FileImportService>()
            .ImportDownloadAsync(queued, folder, PostImportMode.Copy);
        output.WriteLine(JsonSerializer.Serialize(new { route, delayed, completeCard, queued.IsPack, source = history?.SourcePath,
            queued.Status, queued.ErrorMessage, rig.Transport.ClientAdds }));
        Assert.NotNull(history);
        Assert.Equal(owned, history.SourcePath);
        var imported = await rig.Db.EventFiles.SingleAsync();
        Assert.Equal(rig.Event.Id, imported.EventId);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(imported.FilePath));
        if (!completeCard) Assert.True(File.Exists(foreign));
        Assert.Equal(!completeCard, queued.IsPack);
        Assert.Empty(rig.Transport.UnexpectedRequests);
    }

    [Fact]
    public async Task PushedExplicitPackSkipsTheSingleEventSizeCeiling()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            rename: false, title: "Alpha vs Beta", sport: "American Football",
            leagueName: "NFL", relational: true);
        rig.Event.ExternalId = "ev-9900301";
        rig.Event.HomeTeamName = "Alpha";
        rig.Event.AwayTeamName = "Beta";
        var definition = await rig.Db.QualityDefinitions.SingleAsync(x => x.Title == "WEBDL-720p");
        definition.MinSize = 0;
        definition.MaxSize = 1;
        await rig.Db.SaveChangesAsync();

        var release = rig.Release("NFL.2020.09.01.Alpha.vs.Beta.720p.WEB-DL.H264-Fixture", isPack: true);
        release.Size = 4_000_000_000L;
        release.SportarrEventId = rig.Event.ExternalId;
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();

        var outcome = await rig.Services.GetRequiredService<RssSyncService>()
            .ProcessPushedReleaseAsync(release, CancellationToken.None);

        Assert.True(outcome.Grabbed);
        Assert.Empty(outcome.Rejections);
        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.True((await rig.Db.DownloadQueue.SingleAsync()).IsPack);
    }

    [Fact]
    public async Task PushedAewZeroHourPersistsAsCountdown()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            rename: false,
            multipart: true,
            title: "Forbidden Door",
            sport: "Wrestling",
            leagueName: "AEW",
            relational: true);
        rig.Event.MonitoredParts = "Countdown";
        rig.Event.League!.MonitoredParts = "Countdown";
        await rig.Db.SaveChangesAsync();
        var release = rig.Release("AEW.Forbidden.Door.2020.Zero.Hour.720p.WEB-DL.H264-Fixture");
        release.Size = 2_000_000_000;
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();

        var outcome = await rig.Services.GetRequiredService<RssSyncService>()
            .ProcessPushedReleaseAsync(release, CancellationToken.None);

        Assert.True(outcome.Grabbed, string.Join("; ", outcome.Rejections));
        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Equal("Countdown", (await rig.Db.DownloadQueue.SingleAsync()).Part);
        Assert.Equal("Countdown", (await rig.Db.GrabHistory.SingleAsync()).PartName);
    }

    [Fact]
    public async Task HeldPartUpgradeDoesNotSearchOtherParts()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        rig.Event.HasFile = true;
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id,
            PartName = "Prelims",
            FilePath = "/fixture/library/ufc-9999-prelims-720p.mkv",
            Quality = "WEBDL-720p",
            Exists = true
        });
        rig.Db.DelayProfiles.Add(new DelayProfile { Order = 1, UsenetDelay = 60 });
        await rig.Db.SaveChangesAsync();
        rig.Transport.RssResponse = "<rss><channel /></rss>";

        var release = rig.Release("UFC.9999.2020.09.01.Main.Card.1080p.WEB-DL.H264-Fixture");
        release.Size = 4_000_000_000;
        release.PublishDate = DateTime.UtcNow.AddMinutes(-1);
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();

        var outcome = await rig.Services.GetRequiredService<RssSyncService>()
            .ProcessPushedReleaseAsync(release, CancellationToken.None);
        await Task.Delay(250);

        Assert.False(outcome.Grabbed);
        Assert.True(outcome.Pending, string.Join("; ", outcome.Rejections));
        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Empty(rig.Transport.SourceRequests);
    }

    [Fact]
    public async Task GrabbedPartUpgradeSearchesLowerQualityPart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        rig.Event.HasFile = true;
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id,
            PartName = "Prelims",
            FilePath = "/fixture/library/ufc-9999-prelims-720p.mkv",
            Quality = "WEBDL-720p",
            Exists = true
        });
        await rig.Db.SaveChangesAsync();

        var release = rig.Release("UFC.9999.2020.09.01.Main.Card.1080p.WEB-DL.H264-Fixture");
        release.Size = 4_000_000_000;
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();

        var outcome = await rig.Services.GetRequiredService<RssSyncService>()
            .ProcessPushedReleaseAsync(release, CancellationToken.None);
        for (var attempt = 0; attempt < 20 && !rig.Transport.SourceRequests.Any(uri => uri.Query.Contains("t=search")); attempt++)
            await Task.Delay(100);

        Assert.True(outcome.Grabbed, string.Join("; ", outcome.Rejections));
        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Contains(rig.Transport.SourceRequests, uri => uri.Query.Contains("t=search"));
        Assert.Equal("Main Card", (await rig.Db.DownloadQueue.SingleAsync()).Part);
    }

    [Fact]
    public async Task PromotedPartUpgradeSearchesLowerQualityPartAfterGrab()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        rig.Event.HasFile = true;
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id,
            PartName = "Prelims",
            FilePath = "/fixture/library/ufc-9999-prelims-720p.mkv",
            Quality = "WEBDL-720p",
            Exists = true
        });
        var indexer = await rig.Db.Indexers.SingleAsync();
        rig.Db.PendingReleases.Add(new PendingRelease
        {
            EventId = rig.Event.Id,
            Title = "UFC.9999.2020.09.01.Main.Card.2160p.WEB-DL.H264-Fixture",
            Guid = "promoted-main",
            DownloadUrl = "http://part-source.invalid/promoted-main.nzb",
            Indexer = indexer.Name,
            IndexerId = indexer.Id,
            Protocol = "Usenet",
            Size = 4_000_000_000,
            Quality = "WEBDL-2160p",
            Part = "Main Card",
            PublishDate = DateTime.UtcNow.AddHours(-2),
            ReleasableAt = DateTime.UtcNow.AddMinutes(-1),
            Status = PendingReleaseStatus.Pending
        });
        await rig.Db.SaveChangesAsync();
        rig.Transport.RssResponse = "<rss><channel /></rss>";

        await RunDelayedNflReaperAsync(rig);
        for (var attempt = 0; attempt < 20 && !rig.Transport.SourceRequests.Any(uri => uri.Query.Contains("t=search")); attempt++)
            await Task.Delay(100);

        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Equal("Main Card", (await rig.Db.DownloadQueue.SingleAsync()).Part);
        Assert.Contains(rig.Transport.SourceRequests, uri => uri.Query.Contains("t=search"));
    }

    [Fact]
    public async Task PushedInferredPackStillEnforcesMinimumFormatScore()
    {
        await using var rig = await CreatePackPolicyRigAsync();
        var profile = await rig.Db.QualityProfiles.SingleAsync(x => x.Id == rig.Event.QualityProfileId);
        profile.MinFormatScore = 1;
        await rig.Db.SaveChangesAsync();

        var outcome = await PushInferredPackAsync(rig);

        Assert.False(outcome.Grabbed);
        Assert.Contains(outcome.Rejections, x => x.Contains("Custom format score 0 is below minimum 1"));
        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
    }

    [Fact]
    public async Task PushedInferredPackStillEnforcesNegativeFormatScore()
    {
        await using var rig = await CreatePackPolicyRigAsync();
        var profile = await rig.Db.QualityProfiles.SingleAsync(x => x.Id == rig.Event.QualityProfileId);
        profile.MinFormatScore = -9999;
        var format = new CustomFormat
        {
            Name = "No-RlsGroup",
            Specifications =
            [
                new FormatSpecification
                {
                    Name = "Missing group",
                    Implementation = "ReleaseGroupSpecification",
                    Negate = true,
                    Fields = new Dictionary<string, object> { ["value"] = "." }
                }
            ]
        };
        rig.Db.CustomFormats.Add(format);
        await rig.Db.SaveChangesAsync();
        profile.FormatItems = [new ProfileFormatItem { FormatId = format.Id, Score = -10000 }];
        await rig.Db.SaveChangesAsync();

        var outcome = await PushInferredPackAsync(rig);

        Assert.False(outcome.Grabbed);
        Assert.Contains(outcome.Rejections, x => x.Contains("Custom format score -10000 is below minimum -9999"));
        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Empty(await rig.Db.DownloadQueue.ToListAsync());
    }

    private static async Task<PartIdentityIntegrationHarness> CreatePackPolicyRigAsync()
    {
        var rig = await PartIdentityIntegrationHarness.CreateAsync(
            rename: false, title: "Alpha vs Beta", sport: "American Football",
            leagueName: "NFL", relational: true);
        rig.Event.ExternalId = "ev-9900301";
        rig.Event.HomeTeamName = "Alpha";
        rig.Event.AwayTeamName = "Beta";
        var definition = await rig.Db.QualityDefinitions.SingleAsync(x => x.Title == "WEBDL-720p");
        definition.MinSize = 0;
        definition.MaxSize = 1;
        await rig.Db.SaveChangesAsync();
        return rig;
    }

    private static async Task<PushedReleaseOutcome> PushInferredPackAsync(PartIdentityIntegrationHarness rig)
    {
        var release = rig.Release("NFL.2020.09.01.Week1.Pack.Alpha.Beta.720p.WEB-DL.H264");
        release.Size = 4_000_000_000L;
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();
        return await rig.Services.GetRequiredService<RssSyncService>()
            .ProcessPushedReleaseAsync(release, CancellationToken.None);
    }
}
