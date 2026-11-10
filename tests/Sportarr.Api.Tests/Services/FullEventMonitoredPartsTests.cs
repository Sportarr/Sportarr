using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// An event whose monitored parts are only "Full Event" behaves as if the
/// multi-part setting were off. Other events keep their parts.
/// </summary>
// One parking import in flight pauses every scan return, so these run one at a time.
[Collection("FileParking")]
public class FullEventMonitoredPartsTests
{
    private const string UnlabelledRelease = "UFC.9999.2020.09.01.720p.WEB-DL.H264-PARTFIXTURE";
    private const string PrelimsRelease = "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-PARTFIXTURE";
    private const string MainCardRelease = "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.H264-PARTFIXTURE";

    private static async Task<PartIdentityIntegrationHarness> FullEventRigAsync(bool relational = false)
    {
        var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: relational);
        rig.Event.League!.MonitoredParts = "Full Event";
        await rig.Db.SaveChangesAsync();
        return rig;
    }

    [Theory]
    [InlineData("Full Event", null, true)]
    [InlineData(" full event ", null, true)]
    [InlineData(null, "Full Event", true)]
    [InlineData(null, null, false)]
    [InlineData("", "Full Event", false)]
    [InlineData("Main Card", "Full Event", false)]
    [InlineData("Full Event,Main Card", null, false)]
    public void IsFullEventOnly_ReadsTheEffectiveValue(string? eventParts, string? leagueParts, bool expected)
    {
        EventPartDetector.IsFullEventOnly(eventParts, leagueParts).Should().Be(expected);
        EventPartDetector.UsesParts(true, eventParts, leagueParts).Should().Be(!expected);
        EventPartDetector.UsesParts(false, eventParts, leagueParts).Should().BeFalse();
    }

    [Fact]
    public void UsesParts_ForAnEvent_FallsBackToItsLeague()
    {
        var league = new League { Name = "UFC", Sport = "MMA", MonitoredParts = "Full Event" };
        var evt = new Event { Title = "UFC 300", Sport = "MMA", League = league };

        EventPartDetector.UsesParts(true, evt).Should().BeFalse();
        EventPartDetector.UsesParts(true, evt, new League { Name = "UFC", Sport = "MMA" }).Should().BeTrue();
        evt.MonitoredParts = "Main Card";
        EventPartDetector.UsesParts(true, evt).Should().BeTrue();
    }

    [Theory]
    [InlineData("Full Event", new int[] { }, false)]
    [InlineData("Full Event", new[] { 0 }, true)]
    [InlineData("Full Event", new[] { 3 }, true)]
    [InlineData(null, new[] { 3 }, false)]
    public void Completeness_ForAFullEventEvent_NeedsOneFileOfAnyKind(string? leagueParts, int[] present, bool expected)
    {
        // As with the setting off, any file counts. A normal event still needs every part.
        var parts = present.Select(p => p == 0 ? (int?)null : p).ToArray();
        EventPartDetector.AreAllMonitoredPartsPresent("MMA", "UFC 300", "UFC", null, leagueParts, parts, true)
            .Should().Be(expected);
    }

    [Fact]
    public void PartStatuses_AreEmptyForAFullEventEvent()
    {
        var league = new League { Name = "UFC", Sport = "MMA", MonitoredParts = "Full Event" };
        var evt = new Event { Title = "UFC 300", Sport = "MMA", League = league, Monitored = true };

        EventResponse.FromEvent(evt, true, filesLoaded: true).PartStatuses.Should().BeEmpty();

        evt.MonitoredParts = "Main Card";
        EventResponse.FromEvent(evt, true, filesLoaded: true).PartStatuses.Should().NotBeEmpty();
    }

    [Fact]
    public void PartStatuses_AreEmptyForAFullEventCombatEvent()
    {
        // "Combat" is the hub name for the fighting sport.
        var league = new League { Name = "UFC", Sport = "Combat", MonitoredParts = "Full Event" };
        var evt = new Event { Title = "UFC 300", Sport = "Combat", League = league, Monitored = true };

        EventResponse.FromEvent(evt, true, filesLoaded: true).PartStatuses.Should().NotBeNull().And.BeEmpty();

        league.MonitoredParts = null;
        EventResponse.FromEvent(evt, true, filesLoaded: true).PartStatuses.Should().NotBeEmpty();
    }

    [Fact]
    public void PartStatuses_StayWhenTheSettingIsOff()
    {
        var league = new League { Name = "UFC", Sport = "MMA", MonitoredParts = "Full Event" };
        var evt = new Event { Title = "UFC 300", Sport = "MMA", League = league, Monitored = true };

        EventResponse.FromEvent(evt, false, filesLoaded: true).PartStatuses.Should().NotBeEmpty();
    }

    [Fact]
    public void EventTypePartNames_AreTheEventsOwnParts()
    {
        var aew = new League { Name = "AEW", Sport = "Combat", MonitoredParts = "Full Event" };
        var wrestleDream = new Event { Title = "WrestleDream", Sport = "Combat", League = aew, Monitored = true };
        var ufc = new League { Name = "UFC", Sport = "MMA", MonitoredParts = "Full Event" };
        var contender = new Event { Title = "Dana White's Contender Series 2024 Week 1", Sport = "MMA", League = ufc };

        EventResponse.FromEvent(wrestleDream, true, filesLoaded: true).EventTypePartNames
            .Should().Equal("Countdown", "Main Show");
        EventResponse.FromEvent(contender, true, filesLoaded: true).EventTypePartNames.Should().BeEmpty();
    }

    [Theory]
    [InlineData("UFC 300", true)]
    [InlineData("Dana White's Contender Series 2024 Week 1", false)]
    public void EventTypeHasParts_TellsTheUiWhetherPartsExist(string title, bool expected)
    {
        var league = new League { Name = "UFC", Sport = "MMA", MonitoredParts = "Full Event" };
        var evt = new Event { Title = title, Sport = "MMA", League = league, Monitored = true };

        EventResponse.FromEvent(evt, true, filesLoaded: true).EventTypeHasParts.Should().Be(expected);
    }

    [Fact]
    public async Task Rss_GrabsAnUnlabelledReleaseAsTheWholeEvent()
    {
        await using var rig = await FullEventRigAsync(relational: true);

        var decision = await DecideAsync(rig, rig.Release(UnlabelledRelease));

        decision.Grab.Should().BeTrue(decision.Reason);
        decision.ReleasePart.Should().BeNull();
    }

    [Fact]
    public async Task Rss_RejectsAPartReleaseForAFullEventEvent()
    {
        await using var rig = await FullEventRigAsync(relational: true);

        var decision = await DecideAsync(rig, rig.Release(PrelimsRelease));

        decision.Grab.Should().BeFalse();
        decision.Reason.Should().Be("Part file 'Prelims' (multi-part disabled)");
    }

    [Fact]
    public async Task AutomaticSearch_RejectsAPartReleaseForAFullEventEvent()
    {
        await using var rig = await FullEventRigAsync();

        var result = await rig.AutomaticAsync(rig.Release(PrelimsRelease));

        // The search logs the reason only. The manual search test checks the reason text.
        result.Success.Should().BeFalse();
        result.Message.Should().Be("No approved releases found. 1 releases were rejected.");
        rig.Transport.ClientAdds.Should().Be(0);
    }

    [Fact]
    public async Task ManualSearch_RejectsAPartReleaseForAFullEventEvent()
    {
        await using var rig = await FullEventRigAsync();

        var results = await ManualSearchAsync(rig, null, PrelimsRelease, UnlabelledRelease);

        results[PrelimsRelease].Approved.Should().BeFalse();
        results[PrelimsRelease].Rejections.Should().StartWith(
            "Multi-part disabled: rejecting part file 'Prelims' (only full event files accepted)");
        results[UnlabelledRelease].Approved.Should().BeTrue(results[UnlabelledRelease].Rejections);
    }

    [Fact]
    public async Task ManualSearch_ForANormalEventPart_StillApprovesThatPart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();

        var results = await ManualSearchAsync(rig, "Prelims", PrelimsRelease, MainCardRelease);

        results[PrelimsRelease].Approved.Should().BeTrue(results[PrelimsRelease].Rejections);
        results[MainCardRelease].Approved.Should().BeFalse();
    }

    [Fact]
    public async Task Rss_ForANormalEvent_StillGrabsTheReleasePart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);

        var decision = await DecideAsync(rig, rig.Release(PrelimsRelease));

        decision.Grab.Should().BeTrue(decision.Reason);
        decision.ReleasePart.Should().Be("Prelims");
    }

    [Fact]
    public async Task AutomaticSearch_GrabsTheWholeEventWithNoPart()
    {
        await using var rig = await FullEventRigAsync();

        var result = await rig.AutomaticAsync(rig.Release(UnlabelledRelease), requestedPart: "Prelims");

        result.Success.Should().BeTrue(result.Message);
        (await rig.Db.DownloadQueue.SingleAsync()).Part.Should().BeNull();
    }

    [Fact]
    public async Task BackgroundSearch_SearchesAFullEventEventOnceWithNoPart()
    {
        await using var rig = await FullEventRigAsync();

        await PrimeAutomaticCacheAsync(rig, rig.Release(UnlabelledRelease));

        var results = await rig.Services.GetRequiredService<AutomaticSearchService>().SearchAllMonitoredEventsAsync();

        // One whole-event search. Before, no part matched "Full Event", so nothing was searched.
        results.Should().ContainSingle().Which.Success.Should().BeTrue();
        (await rig.Db.DownloadQueue.SingleAsync()).Part.Should().BeNull();
    }

    [Fact]
    public async Task ManualGrab_DropsAPartLabelForAFullEventEvent()
    {
        await using var rig = await FullEventRigAsync();

        await rig.GrabAsync(rig.Release(PrelimsRelease, "Prelims"));

        (await rig.Db.DownloadQueue.SingleAsync()).Part.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Prelims")]
    public async Task Import_FilesAFullEventEventWithNoPartToken(string? storedPart)
    {
        await using var rig = await FullEventRigAsync();

        var file = await rig.ImportAsync(PrelimsRelease, PrelimsRelease + ".mkv", storedPart);

        file.PartName.Should().BeNull();
        file.PartNumber.Should().BeNull();
        Path.GetFileName(file.FilePath).Should().Be("UFC 9999.mkv");
        (await rig.Db.Events.SingleAsync(e => e.Id == rig.Event.Id)).HasFile.Should().BeTrue();
    }

    [Fact]
    public async Task Import_StillNamesAPartForANormalEvent()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();

        var file = await rig.ImportAsync(PrelimsRelease, PrelimsRelease + ".mkv");

        file.PartName.Should().Be("Prelims");
        Path.GetFileName(file.FilePath).Should().Contain(" - pt2");
    }

    [Fact]
    public async Task FullEventRowGrab_OfAPartRelease_KeepsThePart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();

        await GrabFromFullEventRowAsync(rig, rig.Release(PrelimsRelease));

        (await rig.Db.DownloadQueue.SingleAsync()).Part.Should().Be("Prelims");
    }

    [Fact]
    public async Task FullEventRowGrab_OfAnUnlabelledRelease_FilesTheWholeEvent()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();

        // The search gives an unlabelled release the main part. The row overrides it.
        await GrabFromFullEventRowAsync(rig, rig.Release(UnlabelledRelease, "Main Card"));
        var queue = await rig.Db.DownloadQueue.SingleAsync();
        queue.Part.Should().Be("Full Event");
        var file = await rig.ImportAsync(queue.Title, UnlabelledRelease + ".mkv", queue.Part, acquiredQueue: queue);

        file.PartName.Should().BeNull();
        Path.GetFileName(file.FilePath).Should().NotContain(" - pt");
    }

    [Theory]
    [InlineData("UFC.9999.2020.09.01.Prelims.Main.Card.720p.WEB-DL.H264-PARTFIXTURE")]
    [InlineData("UFC.9999.2020.09.01.Countdown.720p.WEB-DL.H264-PARTFIXTURE")]
    public async Task FullEventRowGrab_OfAnUnclearTitle_KeepsNoPart(string title)
    {
        // Two parts, or a label this event does not have. Import decides later.
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();

        await GrabFromFullEventRowAsync(rig, rig.Release(title));

        (await rig.Db.DownloadQueue.SingleAsync()).Part.Should().BeNull();
    }

    [Fact]
    public async Task FullEventRowGrab_OnAFullEventEvent_IsIgnored()
    {
        await using var rig = await FullEventRigAsync();

        await GrabFromFullEventRowAsync(rig, rig.Release(UnlabelledRelease));

        (await rig.Db.DownloadQueue.SingleAsync()).Part.Should().BeNull();
    }

    [Fact]
    public async Task FullEventRowGrab_OfAPack_IsIgnored()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Db.Events.Add(new Event { Title = "UFC 9998", Sport = "Fighting", LeagueId = rig.Event.LeagueId,
            Monitored = true, EventDate = rig.Event.EventDate.AddDays(-7), Season = "2020", EpisodeNumber = 2 });
        await rig.Db.SaveChangesAsync();

        await GrabFromFullEventRowAsync(rig, rig.Release("UFC.2020.Season.Pack.720p.WEB-DL", isPack: true));

        (await rig.Db.DownloadQueue.ToListAsync()).Should().HaveCount(2).And.OnlyContain(q => q.Part == null);
    }

    [Fact]
    public async Task FullEventRowAuto_RejectsAPartRelease()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();

        var result = await FullEventAutoAsync(rig, rig.Release(PrelimsRelease));

        result.Success.Should().BeFalse();
        result.Message.Should().Be("No approved releases found. 1 releases were rejected.");
        rig.Transport.ClientAdds.Should().Be(0);
    }

    [Fact]
    public async Task FullEventRowAuto_FilesAnUnlabelledReleaseAsTheWholeEvent()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();

        var result = await FullEventAutoAsync(rig, rig.Release(UnlabelledRelease));

        result.Success.Should().BeTrue(result.Message);
        var queue = await rig.Db.DownloadQueue.SingleAsync();
        queue.Part.Should().Be("Full Event");
        var file = await rig.ImportAsync(queue.Title, UnlabelledRelease + ".mkv", queue.Part, acquiredQueue: queue);
        file.PartName.Should().BeNull();
        Path.GetFileName(file.FilePath).Should().NotContain(" - pt");
    }

    [Fact]
    public async Task PartAuto_ForANormalEvent_StillGrabsThatPart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();

        var result = await rig.AutomaticAsync(rig.Release(PrelimsRelease), requestedPart: "Prelims", manual: true);

        result.Success.Should().BeTrue(result.Message);
        (await rig.Db.DownloadQueue.SingleAsync()).Part.Should().Be("Prelims");
    }

    [Fact]
    public async Task FullEventRowAuto_WithOnlyPartFilesHeld_GrabsTheWholeRelease()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        await HoldAtCutoffAsync(rig, ("Prelims", 2), ("Main Card", 3));
        // The event fields point at a part file, as after a part import.
        rig.Event.FilePath = "/fixture/held-Prelims.mkv";
        rig.Event.Quality = "WEBDL-1080p";
        await rig.Db.SaveChangesAsync();

        var result = await FullEventAutoAsync(rig, rig.Release(UnlabelledRelease));

        // A part file is not the whole event, so it does not block the whole release.
        result.Success.Should().BeTrue(result.Message);
        (await rig.Db.DownloadQueue.SingleAsync()).Part.Should().Be("Full Event");
    }

    [Fact]
    public async Task FullEventRowAuto_WithAWholeFileAtCutoff_Refuses()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        await HoldAtCutoffAsync(rig, ("Prelims", 2), (null, null));

        var result = await FullEventAutoAsync(rig, rig.Release(UnlabelledRelease));

        result.Success.Should().BeFalse();
        result.Message.Should().Be(
            "Cutoff met - existing file (WEBDL-1080p) meets quality profile requirements. No upgrade needed.");
        rig.Transport.ClientAdds.Should().Be(0);
    }

    [Fact]
    public async Task WholeEventImport_NextToPartFiles_KeepsEveryPartFile()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var prelims = await rig.ImportAsync(PrelimsRelease, PrelimsRelease + ".mkv");
        var main = await rig.ImportAsync(MainCardRelease, MainCardRelease + ".mkv");

        var whole = await rig.ImportAsync(UnlabelledRelease, UnlabelledRelease + ".mkv", "Full Event");

        whole.PartName.Should().BeNull();
        var held = await rig.Db.EventFiles.Where(f => f.EventId == rig.Event.Id && f.Exists).ToListAsync();
        held.Select(f => f.Id).Should().BeEquivalentTo(new[] { prelims.Id, main.Id, whole.Id });
        File.Exists(prelims.FilePath).Should().BeTrue();
        File.Exists(main.FilePath).Should().BeTrue();
        var evt = await rig.Db.Events.SingleAsync(e => e.Id == rig.Event.Id);
        evt.FilePath.Should().Be(whole.FilePath);
        evt.HasFile.Should().BeTrue();
    }

    [Fact]
    public async Task WholeEventImport_OnAFullEventEvent_KeepsEveryPartFile()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var prelims = await rig.ImportAsync(PrelimsRelease, PrelimsRelease + ".mkv");
        var main = await rig.ImportAsync(MainCardRelease, MainCardRelease + ".mkv");
        rig.Event.League!.MonitoredParts = "Full Event";
        await rig.Db.SaveChangesAsync();

        var whole = await rig.ImportAsync(UnlabelledRelease, UnlabelledRelease + ".mkv");

        whole.PartName.Should().BeNull();
        var held = await rig.Db.EventFiles.Where(f => f.EventId == rig.Event.Id && f.Exists).ToListAsync();
        held.Select(f => f.Id).Should().BeEquivalentTo(new[] { prelims.Id, main.Id, whole.Id });
        held.Single(f => f.Id == prelims.Id).PartName.Should().Be("Prelims");
        held.Single(f => f.Id == main.Id).PartName.Should().Be("Main Card");
        File.Exists(prelims.FilePath).Should().BeTrue();
        File.Exists(main.FilePath).Should().BeTrue();
    }

    [Fact]
    public async Task WholeEventImport_ReplacesAFileNamedFullEvent()
    {
        // The file editor can store the name "Full Event". That file is the whole event.
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var prelims = await rig.ImportAsync(PrelimsRelease, PrelimsRelease + ".mkv");
        var first = await rig.ImportAsync(UnlabelledRelease, UnlabelledRelease + ".mkv", "Full Event");
        // Another name, so the new file does not land on the same path.
        var renamedPath = Path.Combine(Path.GetDirectoryName(first.FilePath)!, "UFC 9999 old.mkv");
        File.Move(first.FilePath, renamedPath);
        first.FilePath = renamedPath;
        first.PartName = "Full Event";
        await rig.Db.SaveChangesAsync();

        var second = await rig.ImportAsync(UnlabelledRelease + ".PROPER", UnlabelledRelease + ".PROPER.mkv", "Full Event",
            acquiredQueue: new DownloadQueueItem { EventId = rig.Event.Id, Title = UnlabelledRelease + ".PROPER",
                DownloadId = "proper", Part = "Full Event", Quality = "WEBDL-1080p", Protocol = "Usenet",
                Status = DownloadStatus.Completed });

        var held = await rig.Db.EventFiles.Where(f => f.EventId == rig.Event.Id && f.Exists).ToListAsync();
        held.Select(f => f.Id).Should().BeEquivalentTo(new[] { prelims.Id, second.Id });
        File.Exists(renamedPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("Prelims,Main Card")]
    [InlineData(null)]
    [InlineData("")]
    public void FullEvent_RoundTrip_RestoresTheEarlierValue(string? earlier)
    {
        var on = EventPartDetector.ChangeMonitoredParts(earlier, null, "Full Event");
        on.MonitoredParts.Should().Be("Full Event");
        on.PartsBeforeFullEvent.Should().Be(earlier ?? EventPartDetector.AllPartsMarker);

        var off = EventPartDetector.ChangeMonitoredParts(on.MonitoredParts, on.PartsBeforeFullEvent, "Early Prelims", true);

        off.MonitoredParts.Should().Be(earlier);
        off.PartsBeforeFullEvent.Should().BeNull();
    }

    [Fact]
    public void FullEvent_Off_WithNothingKept_UsesTheRequestedValue()
    {
        var off = EventPartDetector.ChangeMonitoredParts("Full Event", null, "Main Card", true);

        off.MonitoredParts.Should().Be("Main Card");
        off.PartsBeforeFullEvent.Should().BeNull();
    }

    [Fact]
    public void EventPartsRequest_RoundTrip_RestoresTheCustomSet()
    {
        var evt = new Event { Title = "UFC 300", Sport = "MMA", MonitoredParts = "Prelims,Main Card" };

        Sportarr.Api.Endpoints.EventEndpoints.ApplyMonitoredParts(evt, Json("\"Full Event\""), Json("{}"));
        evt.MonitoredParts.Should().Be("Full Event");
        Sportarr.Api.Endpoints.EventEndpoints.ApplyMonitoredParts(
            evt, Json("null"), Json("{\"restorePrevious\":true}"));

        evt.MonitoredParts.Should().Be("Prelims,Main Card");
        evt.PartsBeforeFullEvent.Should().BeNull();
    }

    [Fact]
    public void MonitoringOffAndOn_KeepsTheEarlierParts()
    {
        var evt = new Event { Title = "UFC 300", Sport = "MMA", MonitoredParts = "Prelims,Main Card" };
        Sportarr.Api.Endpoints.EventEndpoints.ApplyMonitoredParts(evt, Json("\"Full Event\""), Json("{}"));

        // The event list sends the league's parts with each monitoring toggle.
        Sportarr.Api.Endpoints.EventEndpoints.ApplyMonitoredParts(
            evt, Json("null"), Json("{\"monitored\":false,\"monitoredParts\":null}"));
        evt.PartsBeforeFullEvent.Should().Be("Prelims,Main Card");
        Sportarr.Api.Endpoints.EventEndpoints.ApplyMonitoredParts(
            evt, Json("\"Full Event\""), Json("{\"monitored\":true,\"monitoredParts\":\"Full Event\"}"));
        evt.PartsBeforeFullEvent.Should().Be("Prelims,Main Card");

        Sportarr.Api.Endpoints.EventEndpoints.ApplyMonitoredParts(
            evt, Json("null"), Json("{\"restorePrevious\":true}"));
        evt.MonitoredParts.Should().Be("Prelims,Main Card");
    }

    [Fact]
    public void LeagueCascade_RoundTrip_RestoresEachEventsOwnParts()
    {
        var league = new League { Name = "UFC", Sport = "MMA", MonitoredParts = "Main Card,Prelims" };
        var events = new[]
        {
            new Event { Title = "UFC 300", Sport = "MMA", MonitoredParts = "Prelims,Main Card" },
            new Event { Title = "UFC 301", Sport = "MMA", MonitoredParts = null },
            new Event { Title = "UFC 302", Sport = "MMA", MonitoredParts = "Main Card" },
        };

        Sportarr.Api.Endpoints.LeagueEndpoints.ApplyLeagueMonitoredParts(league, "Full Event", false).Should().BeFalse();
        foreach (var evt in events)
            Sportarr.Api.Endpoints.LeagueEndpoints.CascadeMonitoredParts(league, evt, false);
        events.Should().OnlyContain(e => e.MonitoredParts == "Full Event");

        // The league editor sends the kept parts again, in its own order.
        var restored = Sportarr.Api.Endpoints.LeagueEndpoints.ApplyLeagueMonitoredParts(league, "Prelims,Main Card", false);
        foreach (var evt in events)
            Sportarr.Api.Endpoints.LeagueEndpoints.CascadeMonitoredParts(league, evt, restored);

        restored.Should().BeTrue();
        league.MonitoredParts.Should().Be("Main Card,Prelims");
        league.PartsBeforeFullEvent.Should().BeNull();
        events.Select(e => e.MonitoredParts).Should().Equal("Prelims,Main Card", null, "Main Card");
        events.Should().OnlyContain(e => e.PartsBeforeFullEvent == null);
    }

    [Fact]
    public async Task FullEvent_RoundTrip_LeavesFilesUntouched()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Event.MonitoredParts = "Prelims,Main Card";
        await rig.Db.SaveChangesAsync();
        await rig.ImportAsync(PrelimsRelease, PrelimsRelease + ".mkv");
        await rig.ImportAsync(MainCardRelease, MainCardRelease + ".mkv");
        var before = await HeldPathsAsync(rig);

        Sportarr.Api.Endpoints.EventEndpoints.ApplyMonitoredParts(rig.Event, Json("\"Full Event\""), Json("{}"));
        await rig.Db.SaveChangesAsync();
        var whole = await rig.ImportAsync(UnlabelledRelease, UnlabelledRelease + ".mkv");
        var parkedWhole = FileParking.ParkedPathFor(whole.FilePath);
        // The whole import parks the part files. Nothing is deleted.
        (await rig.Db.EventFiles.CountAsync(f => f.EventId == rig.Event.Id && f.Exists && f.IsParked)).Should().Be(2);

        // The parts endpoint restores the value, then moves the files back.
        Sportarr.Api.Endpoints.EventEndpoints.ApplyMonitoredParts(
            rig.Event, Json("null"), Json("{\"restorePrevious\":true}"));
        await rig.Db.Entry(rig.Event).Collection(e => e.Files).LoadAsync();
        FileParking.Reconcile(rig.Event, rig.Event.League, true,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await rig.Db.SaveChangesAsync();

        rig.Event.MonitoredParts.Should().Be("Prelims,Main Card");
        (await HeldPathsAsync(rig)).Should().BeEquivalentTo(before.Append(parkedWhole));
        before.Should().OnlyContain(path => File.Exists(path));
        File.Exists(parkedWhole).Should().BeTrue();
    }

    private static async Task<List<string>> HeldPathsAsync(PartIdentityIntegrationHarness rig) =>
        await rig.Db.EventFiles.Where(f => f.EventId == rig.Event.Id && f.Exists).Select(f => f.FilePath!).ToListAsync();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    // Held files at WEBDL-1080p, the profile cutoff. A null part is a whole-event file.
    private static async Task HoldAtCutoffAsync(PartIdentityIntegrationHarness rig, params (string? Name, int? Number)[] parts)
    {
        var profile = await rig.Db.QualityProfiles.SingleAsync();
        profile.CutoffQuality = 15;
        foreach (var (name, number) in parts)
        {
            rig.Db.EventFiles.Add(new EventFile
            {
                EventId = rig.Event.Id, FilePath = $"/fixture/held-{name ?? "whole"}.mkv", Size = 1_000_000_000,
                Quality = "WEBDL-1080p", PartName = name, PartNumber = number, Exists = true
            });
        }
        rig.Event.HasFile = true;
        await rig.Db.SaveChangesAsync();
    }

    // The Full Event row runs the search with the part "Full Event", as the task queue does.
    private static async Task<AutomaticSearchResult> FullEventAutoAsync(
        PartIdentityIntegrationHarness rig, ReleaseSearchResult release)
    {
        await PrimeAutomaticCacheAsync(rig, release, manual: true);
        return await rig.Services.GetRequiredService<AutomaticSearchService>()
            .SearchAndDownloadEventAsync(rig.Event.Id, null, "Full Event", isManualSearch: true);
    }

    private static async Task GrabFromFullEventRowAsync(PartIdentityIntegrationHarness rig, ReleaseSearchResult release)
    {
        var payload = System.Text.Json.JsonSerializer.SerializeToNode(release)!.AsObject();
        payload["eventId"] = rig.Event.Id;
        payload["wholeEvent"] = true;
        using var response = await rig.Client.PostAsJsonAsync("/api/release/grab", payload);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
    }

    private static async Task PrimeAutomaticCacheAsync(
        PartIdentityIntegrationHarness rig, ReleaseSearchResult release, bool manual = false)
    {
        var queries = rig.Services.GetRequiredService<EventQueryService>()
            .BuildEventQueries(rig.Event, null, rig.Event.League?.SearchQueryTemplate);
        var search = rig.Services.GetRequiredService<IndexerSearchService>();
        var fingerprint = await search.GetSearchSourceFingerprintAsync(manual, rig.Event.League?.Tags);
        var key = SearchResultCache.RequestKey(queries, rig.Event.League?.Tags ?? new List<int>(),
            100, true, Sportarr.Api.Helpers.SportarrIdToken.Normalize(rig.Event.ExternalId), fingerprint);
        rig.Services.GetRequiredService<SearchResultCache>().Store(key, new[] { release });
    }

    private static async Task<Dictionary<string, (bool Approved, string Rejections)>> ManualSearchAsync(
        PartIdentityIntegrationHarness rig, string? part, params string[] titles)
    {
        const string query = "UFC 9999";
        var releases = titles.Select((title, index) => rig.Release(title, suffix: "manual-" + index)).ToArray();
        var search = rig.Services.GetRequiredService<IndexerSearchService>();
        var fingerprint = await search.GetSearchSourceFingerprintAsync(true, rig.Event.League?.Tags);
        var key = SearchResultCache.RequestKey(new[] { query }, rig.Event.League?.Tags,
            10000, false, Sportarr.Api.Helpers.SportarrIdToken.Normalize(rig.Event.ExternalId), fingerprint);
        rig.Services.GetRequiredService<SearchResultCache>().Store(key, releases);

        using var response = await rig.Client.PostAsJsonAsync($"/api/event/{rig.Event.Id}/search",
            new { customQuery = query, part });
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return JsonDocument.Parse(body).RootElement.GetProperty("results").EnumerateArray()
            .ToDictionary(item => item.GetProperty("title").GetString()!, item => (
                item.GetProperty("approved").GetBoolean(),
                string.Join(" | ", item.GetProperty("rejections").EnumerateArray().Select(r => r.GetString()))));
    }

    private static async Task<(bool Grab, string Reason, string? ReleasePart)> DecideAsync(
        PartIdentityIntegrationHarness rig, ReleaseSearchResult release)
    {
        var method = typeof(RssSyncService).GetMethod("ShouldGrabReleaseAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<(bool Grab, string Reason, string? ReleasePart)>)method.Invoke(
            rig.Services.GetRequiredService<RssSyncService>(), new object[]
            {
                rig.Db, rig.Event, release,
                await rig.Services.GetRequiredService<ConfigService>().GetConfigAsync(),
                await rig.Db.QualityProfiles.SingleAsync(profile => profile.Id == rig.Event.QualityProfileId),
                rig.Services.GetRequiredService<EventPartDetector>(),
                rig.Services.GetRequiredService<DelayProfileService>(),
                rig.Services.GetRequiredService<DownloadClientService>(),
                rig.Services.GetRequiredService<ReleaseProfileService>(),
                new List<ReleaseProfile>(),
                CancellationToken.None
            })!;
        return await task;
    }
}
