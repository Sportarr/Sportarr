using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PartIdentityIntegrationTests
{
    private const string MainRelease = "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.H264-PARTFIXTURE";
    private const string PrelimsRelease = "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-PARTFIXTURE";

    [Theory]
    [InlineData(null, "Main Card")]
    [InlineData("Prelims", "Prelims")]
    [InlineData("Full Event", "Full Event")]
    public async Task ManualGrab_PersistsOneAcquiredIdentityInQueueAndHistory(string? requested, string expected)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        await rig.GrabAsync(rig.Release(MainRelease, requested));
        rig.Transport.ClientAdds.Should().Be(1);
        (await rig.Db.DownloadQueue.SingleAsync()).Part.Should().Be(expected);
        (await rig.Db.GrabHistory.SingleAsync()).PartName.Should().Be(expected);
        rig.Transport.UnexpectedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExactGoldenRelease_GrabAndImportKeepQueueHistoryAndFileAligned()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        const string title = "UFC.9999.2026.09.01.Main.Card.720p.WEB-DL.H264-SEARCHFIXTURE";
        rig.Event.EventDate = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc);
        await rig.Db.SaveChangesAsync();
        await rig.GrabAsync(rig.Release(title));
        var queue = await rig.Db.DownloadQueue.SingleAsync();
        queue.Part.Should().Be("Main Card");
        (await rig.Db.GrabHistory.SingleAsync()).PartName.Should().Be(queue.Part);
        var file = await rig.ImportAsync(queue.Title, title + ".mkv", queue.Part, acquiredQueue: queue);
        file.PartName.Should().Be(queue.Part); file.PartNumber.Should().Be(3);
        (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be(queue.Part);
        Path.GetFileName(file.FilePath).Should().Contain("pt3").And.Contain("Main Card");
        queue.Status.Should().Be(DownloadStatus.Imported);
    }

    [Fact]
    public async Task ManualPack_ParentSelectionOnlyAppliesToSelectedChild()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var sibling = new Event { Title = "UFC 9998", Sport = "Fighting", LeagueId = rig.Event.LeagueId,
            Monitored = true, EventDate = rig.Event.EventDate.AddDays(-7), Season = "2020", EpisodeNumber = 2 };
        rig.Db.Events.Add(sibling); await rig.Db.SaveChangesAsync();
        await rig.GrabAsync(rig.Release("UFC.2020.Season.Pack.Main.Card.720p.WEB-DL", "Main Card", isPack: true));
        var rows = await rig.Db.DownloadQueue.OrderBy(q => q.EventId).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(q => q.IsPack);
        rows.Single(q => q.EventId == rig.Event.Id).Part.Should().Be("Main Card");
        rows.Single(q => q.EventId == sibling.Id).Part.Should().BeNull();
        (await rig.Db.GrabHistory.SingleAsync()).PartName.Should().Be("Main Card");
        rig.Transport.ClientAdds.Should().Be(1);
        var selectedQueue = rows.Single(q => q.EventId == rig.Event.Id);
        var siblingQueue = rows.Single(q => q.EventId == sibling.Id);
        var selected = await rig.ImportAsync(selectedQueue.Title, PrelimsRelease + ".mkv", selectedQueue.Part,
            isPack: true, acquiredQueue: selectedQueue);
        var other = await rig.ImportAsync(siblingQueue.Title, "UFC.9998.2020.08.25.Prelims.720p.WEB-DL.mkv", siblingQueue.Part,
            isPack: true, acquiredQueue: siblingQueue);
        selected.PartName.Should().Be("Main Card"); selected.PartNumber.Should().Be(3);
        other.PartName.Should().Be("Prelims"); other.PartNumber.Should().Be(2);
    }

    [Fact]
    public async Task ManualPack_IncidentalTitleDoesNotStampAnyChild()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Db.Events.Add(new Event { Title = "UFC 9998", Sport = "Fighting", LeagueId = rig.Event.LeagueId,
            Monitored = true, EventDate = rig.Event.EventDate.AddDays(-7), Season = "2020", EpisodeNumber = 2 });
        await rig.Db.SaveChangesAsync();
        await rig.GrabAsync(rig.Release("UFC.2020.Season.Pack.Main.Card.720p.WEB-DL", isPack: true));
        (await rig.Db.DownloadQueue.ToListAsync()).Should().HaveCount(2).And.OnlyContain(q => q.Part == null);
        (await rig.Db.GrabHistory.SingleAsync()).PartName.Should().BeNull();
    }

    [Fact]
    public async Task AutomaticNullPart_StoresSelectedReleaseIdentity()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var result = await rig.AutomaticAsync(rig.Release(MainRelease));
        result.Success.Should().BeTrue(result.Message);
        rig.Transport.ClientAdds.Should().Be(1);
        (await rig.Db.DownloadQueue.SingleAsync()).Part.Should().Be("Main Card");
        (await rig.Db.GrabHistory.SingleAsync()).PartName.Should().Be("Main Card");
    }

    [Fact]
    public async Task AutomaticNullPart_RechecksSelectedPartBeforeSecondClientAdd()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Db.DownloadQueue.Add(new DownloadQueueItem { EventId = rig.Event.Id, Title = "A different active release",
            DownloadId = "already-active", Part = "Main Card", Status = DownloadStatus.Downloading,
            Quality = "WEBDL-720p", LastUpdate = DateTime.UtcNow });
        await rig.Db.SaveChangesAsync();
        var result = await rig.AutomaticAsync(rig.Release(MainRelease, suffix: "different-url-and-guid"));
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("download");
        rig.Transport.ClientAdds.Should().Be(0);
        (await rig.Db.DownloadQueue.CountAsync()).Should().Be(1);
        (await rig.Db.GrabHistory.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AutomaticNullPart_DifferentSelectedPartRemainsEligible()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Db.DownloadQueue.Add(new DownloadQueueItem { EventId = rig.Event.Id, Title = MainRelease,
            DownloadId = "already-main", Part = "Main Card", Status = DownloadStatus.Downloading, LastUpdate = DateTime.UtcNow });
        await rig.Db.SaveChangesAsync();
        var result = await rig.AutomaticAsync(rig.Release(PrelimsRelease, suffix: "prelims"));
        result.Success.Should().BeTrue(result.Message);
        rig.Transport.ClientAdds.Should().Be(1);
        (await rig.Db.DownloadQueue.ToListAsync()).Select(q => q.Part).Should().BeEquivalentTo("Main Card", "Prelims");
    }

    [Fact]
    public async Task ManualAutomaticSearchAcceptsAewZeroHourForRequestedCountdown()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            title: "Forbidden Door", sport: "Wrestling", leagueName: "AEW");
        var result = await rig.AutomaticAsync(
            rig.Release("AEW.Forbidden.Door.2020.Zero.Hour.720p.WEB-DL.H264-Fixture"),
            requestedPart: "Countdown",
            manual: true);

        result.Success.Should().BeTrue(result.Message);
        rig.Transport.ClientAdds.Should().Be(1);
        (await rig.Db.DownloadQueue.SingleAsync()).Part.Should().Be("Countdown");
    }

    [Fact]
    public async Task ManualAutomaticRoute_PreservesIntentionalActiveDownloadOverride()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Db.DownloadQueue.Add(new DownloadQueueItem { EventId = rig.Event.Id, Title = "Different release",
            DownloadId = "already-main", Part = "Main Card", Status = DownloadStatus.Downloading, LastUpdate = DateTime.UtcNow });
        await rig.Db.SaveChangesAsync();
        var result = await rig.AutomaticAsync(rig.Release(MainRelease), requestedPart: "Main Card", manual: true);
        result.Success.Should().BeTrue(result.Message);
        rig.Transport.ClientAdds.Should().Be(1);
    }

    [Theory]
    [InlineData(true, MainRelease, MainRelease + ".mkv")]
    [InlineData(false, MainRelease, MainRelease + ".mkv")]
    [InlineData(true, "UFC.9999.2020.09.01.Main.Card", "opaque.720p.WEB-DL.mkv")]
    [InlineData(true, "Legacy generic title", "UFC_9999_2020_09_01_Main_Card_720p_WEB-DL.mkv")]
    [InlineData(true, "Legacy generic title", "UFC.9999.Main.Card.2020.09.01.720p.WEB-DL.mkv")]
    public async Task Import_UsesOriginalIdentityForStoredPartAndDestination(bool rename, string title, string basename)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(rename);
        DownloadQueueItem? acquired = null;
        if (basename == "opaque.720p.WEB-DL.mkv")
        {
            await rig.GrabAsync(rig.Release(title));
            acquired = await rig.Db.DownloadQueue.SingleAsync();
            acquired.Part.Should().Be("Main Card");
            (await rig.Db.GrabHistory.SingleAsync()).PartName.Should().Be("Main Card");
        }
        var file = await rig.ImportAsync(title, basename, acquiredQueue: acquired);
        file.PartName.Should().Be("Main Card"); file.PartNumber.Should().Be(3);
        if (rename) Path.GetFileName(file.FilePath).Should().Contain("pt3").And.Contain("Main Card");
        else Path.GetFileName(file.FilePath).Should().Be(basename);
        File.Exists(file.FilePath).Should().BeTrue();
        rig.Event.HasFile.Should().BeFalse("Prelims is still monitored and missing");
    }

    [Theory]
    [InlineData("Prelims", "Prelims", 2)]
    [InlineData("Main Card", "Main Card", 3)]
    public async Task Import_StoredAcquiredIdentityWinsOverConflictingBasename(string stored, string expected, int number)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var rawLabel = stored == "Main Card" ? "Prelims" : "Main.Card";
        var file = await rig.ImportAsync("Original opaque release", $"UFC.9999.{rawLabel}.2020.09.01.720p.WEB-DL.mkv", stored);
        file.PartName.Should().Be(expected); file.PartNumber.Should().Be(number);
        Path.GetFileName(file.FilePath).Should().Contain("pt" + number).And.Contain(expected);
    }

    [Fact]
    public async Task AcquisitionThenImport_InferredStoredValueRemainsAuthoritative()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        await rig.GrabAsync(rig.Release(MainRelease));
        var acquired = await rig.Db.DownloadQueue.SingleAsync();
        acquired.Part.Should().Be("Main Card");
        // Stored identity does not establish who supplied that identity.
        var file = await rig.ImportAsync(acquired.Title, "UFC.9999.Prelims.2020.09.01.720p.WEB-DL.mkv", acquired.Part, acquiredQueue: acquired);
        file.PartName.Should().Be("Main Card"); file.PartNumber.Should().Be(3);
    }

    [Theory]
    [InlineData("Full Event")]
    [InlineData("Unknown selected part")]
    public async Task Import_NonsegmentOverrideDoesNotFallThroughToFilename(string stored)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var file = await rig.ImportAsync(MainRelease, "UFC.9999.Main.Card.2020.09.01.720p.WEB-DL.mkv", stored);
        file.PartName.Should().BeNull(); file.PartNumber.Should().BeNull();
        Path.GetFileName(file.FilePath).Should().NotContain("pt0").And.NotContain("pt3");
    }

    [Theory]
    [InlineData("UFC.9999.2020.09.01.720p.WEB-DL")]
    [InlineData("UFC.9999.2020.09.01.Full.Event.Main.Card.720p.WEB-DL")]
    [InlineData("UFC.9999.2020.09.01.Complete.Card.720p.WEB-DL")]
    [InlineData("UFC.9999.2020.09.01.PPV.720p.WEB-DL")]
    [InlineData("UFC.9999.2020.09.01.Main.Card.and.Prelims.720p.WEB-DL")]
    public async Task Import_UnlabelledOrAmbiguousIdentityPreservesExistingNullBehavior(string title)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var file = await rig.ImportAsync(title, title + ".mkv");
        file.PartName.Should().BeNull(); file.PartNumber.Should().BeNull();
        Path.GetFileName(file.FilePath).Should().NotContain("pt");
        rig.Event.HasFile.Should().BeTrue("the existing null-coverage convention remains in this bounded change");
    }

    [Fact]
    public async Task Import_MainAndPrelimsOccupyDistinctSlotsAndCompleteOnlyTogether()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var main = await rig.ImportAsync(MainRelease, MainRelease + ".mkv");
        main.PartNumber.Should().Be(3); rig.Event.HasFile.Should().BeFalse();
        var mainBytes = await File.ReadAllBytesAsync(main.FilePath);
        var prelims = await rig.ImportAsync(PrelimsRelease, PrelimsRelease + ".mkv");
        prelims.PartNumber.Should().Be(2);
        (await rig.Db.EventFiles.ToListAsync()).Should().HaveCount(2);
        prelims.FilePath.Should().NotBe(main.FilePath);
        (await File.ReadAllBytesAsync(main.FilePath)).Should().Equal(mainBytes);
        rig.Event.HasFile.Should().BeTrue();
    }

    [Fact]
    public async Task Import_OnlyMainMonitoredCompletesAfterMainImport()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Event.MonitoredParts = "Main Card"; await rig.Db.SaveChangesAsync();
        var file = await rig.ImportAsync(MainRelease, MainRelease + ".mkv");
        file.PartNumber.Should().Be(3); rig.Event.HasFile.Should().BeTrue();
    }

    [Theory]
    [InlineData("UFC Fight Night: Fixture", "Fighting", "UFC", "Main.Card", "Main Card", 2)]
    [InlineData("WWE WrestleMania 40", "Wrestling", "WWE", "Main.Card", "Main Show", 2)]
    [InlineData("AEW All In", "Wrestling", "AEW", "Main.Card", "Main Show", 2)]
    [InlineData("UFC 9999", "Fighting", "UFC", "Early.Prelims", "Early Prelims", 1)]
    [InlineData("WWE Monday Night Raw", "Wrestling", "WWE", "Main.Card", null, null)]
    [InlineData("Dana White's Contender Series", "Fighting", "UFC", "Main.Card", null, null)]
    [InlineData("ONE Friday Fights 99", "Combat", "ONE Championship", "Main.Card", null, null)]
    [InlineData("Fixture vs Main Card", "Basketball", "NBA", "Main.Card", null, null)]
    [InlineData("Fixture Grand Prix Qualifying", "Motorsport", "Formula 1", "Main.Card", null, null)]
    [InlineData("Fixture Grand Prix Race", "Motorsport", "MotoGP", "Main.Card", null, null)]
    public async Task Import_UsesActualEventAndLeagueSegmentDefinitions(string eventTitle, string sport, string league,
        string label, string? expected, int? number)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(title: eventTitle, sport: sport, leagueName: league);
        var release = league + ".2020.09.01." + label + ".720p.WEB-DL";
        var file = await rig.ImportAsync(release, release + ".mkv");
        file.EventId.Should().Be(rig.Event.Id); file.PartName.Should().Be(expected); file.PartNumber.Should().Be(number);
        if (number.HasValue) Path.GetFileName(file.FilePath).Should().Contain("pt" + number);
        else Path.GetFileName(file.FilePath).Should().NotContain("pt");
    }

    [Fact]
    public async Task Import_DisabledMultipartDoesNotAssignPart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var file = await rig.ImportAsync(MainRelease, MainRelease + ".mkv", "Main Card");
        file.PartName.Should().BeNull(); file.PartNumber.Should().BeNull();
        Path.GetFileName(file.FilePath).Should().NotContain("pt");
    }

    [Theory]
    [InlineData(null, "Prelims", 2)]
    [InlineData("Main Card", "Main Card", 3)]
    public async Task Import_PackUsesChildBasenameUnlessChildHasStoredOverride(string? childOverride, string expected, int number)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var file = await rig.ImportAsync("UFC.2020.Season.Pack.Main.Card", PrelimsRelease + ".mkv", childOverride, isPack: true);
        file.PartName.Should().Be(expected); file.PartNumber.Should().Be(number);
        Path.GetFileName(file.FilePath).Should().Contain("pt" + number);
    }
}
