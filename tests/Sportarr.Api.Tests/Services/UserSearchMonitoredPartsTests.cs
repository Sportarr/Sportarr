using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The Wanted page Search sends no part. On a fight card that search must
/// pick only parts the user monitors and parts the user does not have yet.
/// </summary>
public class UserSearchMonitoredPartsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchWithNoPartSkipsAnUnmonitoredPart(bool manual)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true);
        rig.Event.MonitoredParts = "Main Card";
        await rig.Db.SaveChangesAsync();
        var earlyPrelims = rig.Release("UFC.9999.2020.09.01.Early.Prelims.720p.WEB-DL.H264-Early", suffix: "early");
        earlyPrelims.Size = 2_000_000_000;

        var outcome = await rig.AutomaticAsync(earlyPrelims, manual: manual);

        Assert.False(outcome.Success, outcome.Message);
        Assert.Contains("Only unmonitored parts available", outcome.Message);
        Assert.Equal(0, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task SearchThatNamesAPartStillSearchesAnUnmonitoredPart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true);
        rig.Event.MonitoredParts = "Main Card";
        await rig.Db.SaveChangesAsync();
        var earlyPrelims = rig.Release("UFC.9999.2020.09.01.Early.Prelims.720p.WEB-DL.H264-Early",
            "Early Prelims", suffix: "early");
        earlyPrelims.Size = 2_000_000_000;

        var outcome = await rig.AutomaticAsync(earlyPrelims, "Early Prelims", manual: true);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Equal("Early Prelims", (await rig.Db.DownloadQueue.SingleAsync()).Part);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchWithNoPartFillsAMissingPartBeforeReplacingAPresentOne(bool manual)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true);
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id,
            FilePath = "/data/e2e-lib/UFC 9999 - Main Card.mkv",
            Size = 2_000_000_000,
            Quality = "WEBDL-720p",
            Source = "WEBDL",
            Codec = "H264",
            PartName = "Main Card",
            PartNumber = 3,
            ReleaseTitle = "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.H264-Held",
            Exists = true
        });
        await rig.Db.SaveChangesAsync();
        var mainCard = rig.Release("UFC.9999.2020.09.01.Main.Card.1080p.WEB-DL.H264-Better", suffix: "main");
        mainCard.Quality = "WEBDL-1080p";
        mainCard.Size = 4_000_000_000;
        var prelims = rig.Release("UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-Missing", suffix: "prelims");
        prelims.Size = 2_000_000_000;

        var outcome = await rig.AutomaticAsync(new[] { mainCard, prelims }, manual: manual);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(prelims.Title, outcome.SelectedRelease);
        Assert.Equal("Prelims", (await rig.Db.DownloadQueue.SingleAsync()).Part);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchWithNoPartDoesNotGrabTheReleaseAlreadyHeld(bool manual)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true);
        const string held = "UFC.9999.2020.09.01.720p.WEB-DL.H264-Held";
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id,
            FilePath = "/data/e2e-lib/UFC 9999 - Prelims.mkv",
            Size = 2_000_000_000,
            Quality = "WEBDL-720p",
            Source = "WEBDL",
            Codec = "H264",
            PartName = "Prelims",
            PartNumber = 2,
            ReleaseTitle = held,
            OriginalTitle = held,
            Exists = true
        });
        await rig.Db.SaveChangesAsync();
        var again = rig.Release(held, suffix: "again");
        again.Size = 2_000_000_000;

        var outcome = await rig.AutomaticAsync(again, manual: manual);

        Assert.False(outcome.Success, outcome.Message);
        Assert.Equal(0, rig.Transport.ClientAdds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchWithNoPartDoesNotFillAMissingPartWithAMismatchedResolution(bool manual)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true);
        AddHeldMainCard1080p(rig);
        await rig.Db.SaveChangesAsync();
        var prelims = rig.Release("UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-Mismatch", suffix: "prelims");
        prelims.Size = 2_000_000_000;

        var outcome = await rig.AutomaticAsync(prelims, manual: manual);

        Assert.False(outcome.Success, outcome.Message);
        Assert.Equal("No releases found matching existing parts", outcome.Message);
        Assert.Equal(0, rig.Transport.ClientAdds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchWithNoPartFillsAMissingPartThatMatchesTheHeldParts(bool manual)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true);
        AddHeldMainCard1080p(rig);
        await rig.Db.SaveChangesAsync();
        var prelims = rig.Release("UFC.9999.2020.09.01.Prelims.1080p.WEB-DL.H264-Match", suffix: "prelims");
        prelims.Quality = "WEBDL-1080p";
        prelims.Size = 4_000_000_000;

        var outcome = await rig.AutomaticAsync(prelims, manual: manual);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal("Prelims", (await rig.Db.DownloadQueue.SingleAsync()).Part);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task SearchThatNamesAPartCanUpgradeItWhileAnotherPartStaysLower(bool manual, bool grabbed)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true);
        rig.Db.EventFiles.AddRange(
            HeldPart("Main Card", 3, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.H264-Held"),
            HeldPart("Prelims", 2, "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-Held"));
        rig.Event.HasFile = true;
        await rig.Db.SaveChangesAsync();
        var prelims = rig.Release("UFC.9999.2020.09.01.Prelims.1080p.WEB-DL.H264-Upgrade", "Prelims", suffix: "prelims");
        prelims.Quality = "WEBDL-1080p";
        prelims.Size = 4_000_000_000;

        var outcome = await rig.AutomaticAsync(prelims, "Prelims", manual: manual);

        // A user search on one part is an explicit upgrade of that part.
        // An automatic search keeps the parts matched.
        Assert.Equal(grabbed, outcome.Success);
        Assert.Equal(grabbed ? 1 : 0, rig.Transport.ClientAdds);
        if (!grabbed) Assert.Equal("No releases found matching existing parts", outcome.Message);

        EventFile HeldPart(string name, int number, string title) => new()
        {
            EventId = rig.Event.Id,
            FilePath = $"/data/e2e-lib/UFC 9999 - {name}.mkv",
            Size = 2_000_000_000,
            Quality = "WEBDL-720p",
            Source = "WEBDL",
            Codec = "H264",
            PartName = name,
            PartNumber = number,
            ReleaseTitle = title,
            OriginalTitle = title,
            Exists = true
        };
    }

    private static void AddHeldMainCard1080p(PartIdentityIntegrationHarness rig) =>
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id,
            FilePath = "/data/e2e-lib/UFC 9999 - Main Card.mkv",
            Size = 4_000_000_000,
            Quality = "WEBDL-1080p",
            Source = "WEBDL",
            Codec = "H264",
            PartName = "Main Card",
            PartNumber = 3,
            ReleaseTitle = "UFC.9999.2020.09.01.Main.Card.1080p.WEB-DL.H264-Held",
            Exists = true
        });
}

