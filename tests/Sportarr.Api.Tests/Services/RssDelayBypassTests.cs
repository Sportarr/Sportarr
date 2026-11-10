using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class RssDelayBypassTests
{
    [Fact]
    public async Task RssGrabsTheHighestAllowedQualityWithoutWaiting()
    {
        await using var rig = await CreateRigAsync();
        SetFeed(rig, DateTime.UtcNow.AddMinutes(-5), "1080p");

        await rig.Services.GetRequiredService<RssSyncService>().SyncNowAsync(CancellationToken.None);

        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Empty(await rig.Db.PendingReleases.ToListAsync());
    }

    [Fact]
    public async Task RssStillHoldsAQualityBelowTheHighestAllowed()
    {
        await using var rig = await CreateRigAsync();
        SetFeed(rig, DateTime.UtcNow.AddMinutes(-5), "720p");

        await rig.Services.GetRequiredService<RssSyncService>().SyncNowAsync(CancellationToken.None);

        Assert.Equal(0, rig.Transport.ClientAdds);
        Assert.Equal(PendingReleaseStatus.Pending, (await rig.Db.PendingReleases.SingleAsync()).Status);
    }

    [Fact]
    public async Task HeldReleaseReasonNamesTheDate()
    {
        await using var rig = await CreateRigAsync();
        var published = DateTime.UtcNow.AddMinutes(-5);

        using var response = await rig.Client.PostAsJsonAsync("/api/release/push", new
        {
            title = "NFL.2020.09.01.Alpha.vs.Beta.720p.WEB-DL.H264-Fixture",
            downloadUrl = "http://part-source.invalid/720p.nzb",
            protocol = "usenet",
            indexer = "Part fixture",
            size = 2_000_000_000L,
            publishDate = published
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        var releasableAt = (await rig.Db.PendingReleases.SingleAsync()).ReleasableAt;
        using var result = JsonDocument.Parse(body);
        var reasons = result.RootElement[0].GetProperty("rejections").EnumerateArray()
            .Select(reason => reason.GetString()).ToList();
        Assert.Contains($"Held by delay profile until {releasableAt:yyyy-MM-dd HH:mm} UTC", reasons);
        Assert.Matches(new Regex(@"until \d{4}-\d{2}-\d{2} \d{2}:\d{2} UTC"), string.Join("|", reasons));
    }

    private static async Task<PartIdentityIntegrationHarness> CreateRigAsync()
    {
        var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false,
            title: "Alpha vs Beta", sport: "American Football", leagueName: "NFL", relational: true);
        rig.Event.HomeTeamName = "Alpha";
        rig.Event.AwayTeamName = "Beta";
        var indexer = await rig.Db.Indexers.SingleAsync();
        indexer.EnableRss = true;
        indexer.ApiPath = "";

        // WEBDL-1080p becomes the best quality the profile allows.
        var profile = await rig.Db.QualityProfiles.SingleAsync(p => p.Id == rig.Event.QualityProfileId);
        profile.Items = profile.Items
            .Select(item => new QualityItem
            {
                Name = item.Name,
                Quality = item.Quality,
                Allowed = item.Name != "WEBDL-2160p"
            })
            .ToList();
        rig.Db.DelayProfiles.Add(new DelayProfile
        {
            Order = 1,
            UsenetDelay = 60,
            PreferredProtocol = "Usenet",
            BypassIfHighestQuality = true
        });
        await rig.Db.SaveChangesAsync();
        return rig;
    }

    private static void SetFeed(PartIdentityIntegrationHarness rig, DateTime published, string quality)
    {
        XNamespace ns = "http://www.newznab.com/DTD/2010/feeds/attributes/";
        var size = quality == "1080p" ? 4_000_000_000L : 2_000_000_000L;
        rig.Transport.RssResponse = new XDocument(new XElement("rss", new XAttribute("version", "2.0"),
            new XElement("channel", new XElement("item",
                new XElement("title", $"NFL.2020.09.01.Alpha.vs.Beta.{quality}.WEB-DL.H264-Fixture"),
                new XElement("guid", $"rss-{quality}"),
                new XElement("pubDate", published.ToString("R")),
                new XElement("enclosure",
                    new XAttribute("url", $"http://part-source.invalid/{quality}.nzb"),
                    new XAttribute("length", size),
                    new XAttribute("type", "application/x-nzb")),
                new XElement(ns + "attr", new XAttribute("name", "size"),
                    new XAttribute("value", size)))))).ToString();
    }
}
