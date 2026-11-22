using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #445. The hub can rename a season, for example "2024" to
/// "2024-2025". The local events keep the old season until a sync fetches
/// the new one. If that fetch fails, the old events look stale. A sync must
/// not delete events or their files on data it did not get.
/// </summary>
public class StaleSeasonCleanupGuardTests
{
    // Each test starts with no remembered empty lookups.
    public StaleSeasonCleanupGuardTests() => LeagueEventSyncService.ForgetEmptyLookups();

    private const string LeagueExternalId = "lg-stale445";

    public enum Failure { TooManyRequests, ServerError, Timeout, NetworkError }

    private sealed record WireEvent(string Id, string Title, DateTime Date, string? TsdbId = null);

    private static async Task<(PartIdentityIntegrationHarness Rig, int LeagueId)> RigAsync()
    {
        var rig = await PartIdentityIntegrationHarness.CreateAsync(title: "Fixture Grand Prix", sport: "Motorsport",
            leagueName: "Stale Season Fixture", relational: true, multipart: false);
        var league = await rig.Db.Leagues.SingleAsync(l => l.Id == rig.Event.LeagueId);
        league.ExternalId = LeagueExternalId;
        league.SportFormat = "EventSport";
        league.MonitoredParts = null;
        league.MonitorType = MonitorType.All;
        // A fresh stamp skips the league metadata lookup.
        league.LastUpdate = DateTime.UtcNow;
        // The harness event plays no part here.
        rig.Db.Events.Remove(rig.Event);
        await rig.Db.SaveChangesAsync();
        return (rig, league.Id);
    }

    private static async Task<Event> AddLocalAsync(PartIdentityIntegrationHarness rig, int leagueId, string season,
        WireEvent wire, bool withFile = false)
    {
        var evt = new Event
        {
            ExternalId = wire.Id, Title = wire.Title, Sport = "Motorsport", LeagueId = leagueId,
            EventDate = wire.Date, Season = season, SeasonNumber = SeasonNumberParser.Parse(season), TsdbId = wire.TsdbId,
            Status = "Completed", Monitored = true,
        };
        if (withFile)
        {
            var root = (await rig.Db.RootFolders.FirstAsync()).Path;
            var path = Path.Combine(root, wire.Id + ".mkv");
            await File.WriteAllTextAsync(path, wire.Id);
            evt.HasFile = true;
            evt.Files.Add(new EventFile { FilePath = path, Exists = true, Size = 1, Quality = "WEBDL-720p" });
        }
        rig.Db.Events.Add(evt);
        await rig.Db.SaveChangesAsync();
        return evt;
    }

    private static LeagueEventSyncService Sync(PartIdentityIntegrationHarness rig, FakeHub hub)
    {
        var api = new SportarrApiClient(new HttpClient(hub), NullLogger<SportarrApiClient>.Instance,
            rig.Services.GetRequiredService<IConfiguration>(), rig.Services.GetRequiredService<ConfigService>(),
            new MemoryCache(new MemoryCacheOptions()));
        var rename = ActivatorUtilities.CreateInstance<FileRenameService>(rig.Services, api);
        var stream = ActivatorUtilities.CreateInstance<EventStreamService>(rig.Services);
        return ActivatorUtilities.CreateInstance<LeagueEventSyncService>(rig.Services, api, rename, stream);
    }

    private static readonly WireEvent RaceA = new("ev-race-a", "Race A", new DateTime(2024, 11, 3, 14, 0, 0, DateTimeKind.Utc));
    private static readonly WireEvent RaceB = new("ev-race-b", "Race B", new DateTime(2024, 11, 17, 14, 0, 0, DateTimeKind.Utc));
    private static readonly WireEvent RaceC = new("ev-race-c", "Race C", new DateTime(2025, 1, 12, 14, 0, 0, DateTimeKind.Utc));

    private static readonly WireEvent[] NextSeason =
    {
        new("ev-next-1", "Next 1", new DateTime(2025, 10, 5, 14, 0, 0, DateTimeKind.Utc)),
        new("ev-next-2", "Next 2", new DateTime(2025, 10, 19, 14, 0, 0, DateTimeKind.Utc)),
        new("ev-next-3", "Next 3", new DateTime(2025, 11, 2, 14, 0, 0, DateTimeKind.Utc)),
        new("ev-next-4", "Next 4", new DateTime(2025, 11, 16, 14, 0, 0, DateTimeKind.Utc)),
        new("ev-next-5", "Next 5", new DateTime(2025, 12, 7, 14, 0, 0, DateTimeKind.Utc)),
    };

    // Local state before the hub renamed "2024" to "2024-2025". Three events
    // sit under the old name, so they stay under the half-league guard.
    private static async Task<(PartIdentityIntegrationHarness Rig, int LeagueId, Event Kept)> RenamedSeasonRigAsync()
    {
        var (rig, leagueId) = await RigAsync();
        var kept = await AddLocalAsync(rig, leagueId, "2024", RaceA, withFile: true);
        await AddLocalAsync(rig, leagueId, "2024", RaceB);
        await AddLocalAsync(rig, leagueId, "2024", RaceC);
        foreach (var wire in NextSeason)
            await AddLocalAsync(rig, leagueId, "2025-2026", wire);
        return (rig, leagueId, kept);
    }

    [Theory]
    [InlineData(Failure.TooManyRequests)]
    [InlineData(Failure.ServerError)]
    [InlineData(Failure.Timeout)]
    [InlineData(Failure.NetworkError)]
    public async Task FailedFetchOfTheRenamedSeason_DeletesNoEventsOrFiles(Failure failure)
    {
        var (rig, leagueId, kept) = await RenamedSeasonRigAsync();
        await using var _ = rig;
        var fileId = kept.Files.Single().Id;
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Fail("2024-2025", failure);
        hub.Serve("2025-2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        var oldSeason = await rig.Db.Events.AsNoTracking().Where(e => e.LeagueId == leagueId && e.Season == "2024")
            .Select(e => e.ExternalId).ToListAsync();
        oldSeason.Should().BeEquivalentTo(new[] { RaceA.Id, RaceB.Id, RaceC.Id },
            "one failed season fetch is no proof that these events are gone");
        (await rig.Db.EventFiles.AsNoTracking().SingleAsync(f => f.Id == fileId)).EventId.Should().Be(kept.Id);
    }

    [Fact]
    public async Task FullSuccessfulSync_StillRemovesTrulyStaleAndCancelledEvents()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var gone = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-gone", "Gone", new DateTime(1999, 5, 1, 12, 0, 0, DateTimeKind.Utc)));
        var goneWithFile = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-gone-file", "Gone With File", new DateTime(1999, 5, 8, 12, 0, 0, DateTimeKind.Utc)), withFile: true);
        var cancelled = await AddLocalAsync(rig, leagueId, "2025-2026",
            new WireEvent("ev-cancelled", "Cancelled", new DateTime(2025, 12, 21, 14, 0, 0, DateTimeKind.Utc)));
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC });
        hub.Serve("2025-2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(2);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == gone.Id)).Should().BeFalse("its season left the hub");
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == cancelled.Id)).Should().BeFalse("the hub dropped it from its season");
        var kept = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.Id == goneWithFile.Id);
        kept.Season.Should().Be("1999", "the hub cannot find it, so it stays where it was with its file");
        kept.Files.Should().ContainSingle();
        hub.LookedUp.Should().Equal(goneWithFile.ExternalId);
        (await rig.Db.Events.AsNoTracking().CountAsync(e => e.LeagueId == leagueId)).Should().Be(9);
    }

    // A stale event with files that the hub now holds in a listed season
    // moves there. The sync asks the hub by id.
    [Fact]
    public async Task StaleEventWithFiles_MovesToTheSeasonTheHubNamesForIt()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var wire = new WireEvent("ev-late", "Late Race", new DateTime(1999, 5, 1, 12, 0, 0, DateTimeKind.Utc));
        var stale = await AddLocalAsync(rig, leagueId, "1999", wire, withFile: true);
        var fileId = stale.Files.Single().Id;
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC });
        hub.Serve("2025-2026", NextSeason);
        hub.Locate(wire, "2024-2025");

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        var row = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.Id == stale.Id);
        row.Season.Should().Be("2024-2025");
        row.Files.Select(f => f.Id).Should().Equal(fileId);
    }

    // The hub answers the lookup under a new id, and the new season already
    // made a row with that id. The files move to that row, so the next sync
    // cannot remove them with the old row.
    [Fact]
    public async Task StaleEventWithFiles_UnderANewHubId_HandsItsFilesToTheNewRow()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var oldWire = new WireEvent("ev-old-id", "Late Race", new DateTime(2024, 12, 1, 12, 0, 0, DateTimeKind.Utc), TsdbId: "445001");
        // The season schedule omits the TheSportsDB id, so the season fetch
        // makes a new row. Only the lookup can join the two rows.
        var newWire = oldWire with { Id = "ev-new-id", Title = "Late Race Renamed", TsdbId = null };
        var stale = await AddLocalAsync(rig, leagueId, "1999", oldWire, withFile: true);
        var fileId = stale.Files.Single().Id;
        stale.FilePath = stale.Files.Single().FilePath;
        stale.FileSize = 1234;
        stale.Quality = "WEBDL-720p";
        var queued = new DownloadQueueItem { EventId = stale.Id, Title = "Late.Race.720p", DownloadId = "dl-445" };
        var grab = new GrabHistory { EventId = stale.Id, Title = "Late.Race.720p", Indexer = "Fixture",
            DownloadUrl = "http://fixture.invalid/445.nzb", Guid = "guid-445", Protocol = "Usenet" };
        var import = new ImportHistory { EventId = stale.Id, SourcePath = "/in/445.mkv", DestinationPath = stale.FilePath!,
            Quality = "WEBDL-720p" };
        rig.Db.AddRange(queued, grab, import);
        await rig.Db.SaveChangesAsync();
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC, newWire });
        hub.Serve("2025-2026", NextSeason);
        // The real hub answers a deleted short id with an empty list. The
        // TheSportsDB id leads to the new event.
        hub.Locate(oldWire.TsdbId!, newWire with { TsdbId = oldWire.TsdbId }, "2024-2025");

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == stale.Id)).Should().BeFalse();
        var row = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.ExternalId == newWire.Id);
        row.Season.Should().Be("2024-2025");
        row.Files.Select(f => f.Id).Should().Equal(fileId);
        row.HasFile.Should().BeTrue();
        row.FilePath.Should().Be(stale.FilePath);
        row.FileSize.Should().Be(1234);
        row.Quality.Should().Be("WEBDL-720p");
        (await rig.Db.DownloadQueue.AsNoTracking().SingleAsync(q => q.Id == queued.Id)).EventId.Should().Be(row.Id);
        (await rig.Db.GrabHistory.AsNoTracking().SingleAsync(g => g.Id == grab.Id)).EventId.Should().Be(row.Id);
        (await rig.Db.ImportHistories.AsNoTracking().SingleAsync(h => h.Id == import.Id)).EventId.Should().Be(row.Id);
        hub.LookedUp.Should().Equal(oldWire.Id, oldWire.TsdbId);

        // The next sync finds nothing to remove.
        var again = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);
        again.RemovedCount.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.EventFiles.AsNoTracking().SingleAsync(f => f.Id == fileId)).EventId.Should().Be(row.Id);
    }

    [Fact]
    public async Task SyncWithRemovalsHeld_RemovesNothing()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var gone = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-gone", "Gone", new DateTime(1999, 5, 1, 12, 0, 0, DateTimeKind.Utc)));
        var cancelled = await AddLocalAsync(rig, leagueId, "2025-2026",
            new WireEvent("ev-cancelled", "Cancelled", new DateTime(2025, 12, 21, 14, 0, 0, DateTimeKind.Utc)));
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC });
        hub.Serve("2025-2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true, allowRemovals: false);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().CountAsync(e => e.Id == gone.Id || e.Id == cancelled.Id)).Should().Be(2);
        (await rig.Db.Events.AsNoTracking().CountAsync(e => e.Season == "2024-2025")).Should().Be(3, "updates still apply");
    }

    // The feed sequence for a renamed past season: the hub deletes "2019",
    // creates "2019-2020", then updates each event to the new season. The
    // walk skips past seasons, so only the targeted sync moves the events.
    private static async Task<(PartIdentityIntegrationHarness Rig, int LeagueId, Event[] Local, FakeHub Hub)> FeedRigAsync()
    {
        var (rig, leagueId) = await RigAsync();
        var a = new WireEvent("ev-past-a", "Past A", new DateTime(2019, 11, 3, 14, 0, 0, DateTimeKind.Utc));
        var b = new WireEvent("ev-past-b", "Past B", new DateTime(2019, 11, 17, 14, 0, 0, DateTimeKind.Utc));
        var c = new WireEvent("ev-past-c", "Past C", new DateTime(2020, 1, 12, 14, 0, 0, DateTimeKind.Utc));
        var local = new[]
        {
            await AddLocalAsync(rig, leagueId, "2019", a, withFile: true),
            await AddLocalAsync(rig, leagueId, "2019", b, withFile: true),
            await AddLocalAsync(rig, leagueId, "2019", c),
        };
        var current = DateTime.UtcNow.Year.ToString();
        var currentEvents = NextSeason.Select((e, i) => e with { Date = new DateTime(DateTime.UtcNow.Year, 3, 1 + i, 14, 0, 0, DateTimeKind.Utc) }).ToArray();
        foreach (var wire in currentEvents)
            await AddLocalAsync(rig, leagueId, current, wire);

        if (!await rig.Db.AppSettings.AnyAsync())
            rig.Db.AppSettings.Add(new AppSettings());
        await rig.Db.SaveChangesAsync();
        (await rig.Db.AppSettings.FirstAsync()).HubChangesCursor = 50;
        await rig.Db.SaveChangesAsync();

        var hub = new FakeHub(new[] { "2019-2020", current });
        hub.Serve(current, currentEvents);
        hub.Feed(
            new { seq = 51, entity = "season", id = "sn-2019", league = LeagueExternalId, season = "2019", change = "deleted" },
            new { seq = 52, entity = "season", id = "sn-2019-2020", league = LeagueExternalId, season = "2019-2020", change = "created" },
            new { seq = 53, entity = "event", id = a.Id, league = LeagueExternalId, season = "2019-2020", change = "updated" },
            new { seq = 54, entity = "event", id = b.Id, league = LeagueExternalId, season = "2019-2020", change = "updated" },
            new { seq = 55, entity = "event", id = c.Id, league = LeagueExternalId, season = "2019-2020", change = "updated" });
        hub.Serve("2019-2020", new[] { a, b, c });
        return (rig, leagueId, local, hub);
    }

    private static HubChangesPollerService Poller(PartIdentityIntegrationHarness rig, FakeHub hub)
    {
        var api = new SportarrApiClient(new HttpClient(hub), NullLogger<SportarrApiClient>.Instance,
            rig.Services.GetRequiredService<IConfiguration>(), rig.Services.GetRequiredService<ConfigService>(),
            new MemoryCache(new MemoryCacheOptions()));
        var rename = ActivatorUtilities.CreateInstance<FileRenameService>(rig.Services, api);
        var stream = ActivatorUtilities.CreateInstance<EventStreamService>(rig.Services);
        var sync = ActivatorUtilities.CreateInstance<LeagueEventSyncService>(rig.Services, api, rename, stream);
        var services = new ServiceCollection()
            .AddSingleton(rig.Db)
            .AddSingleton(api)
            .AddSingleton(sync)
            .BuildServiceProvider();
        return new HubChangesPollerService(services, NullLogger<HubChangesPollerService>.Instance);
    }

    [Fact]
    public async Task SeasonRenameFeed_TargetedSyncSucceeds_MovesEventsAndKeepsFiles()
    {
        var (rig, leagueId, local, hub) = await FeedRigAsync();
        await using var _ = rig;
        var fileIds = local.SelectMany(e => e.Files).Select(f => f.Id).ToList();

        await Poller(rig, hub).PollNowAsync(CancellationToken.None);

        rig.Db.ChangeTracker.Clear();
        var rows = await rig.Db.Events.AsNoTracking().Include(e => e.Files)
            .Where(e => local.Select(l => l.Id).Contains(e.Id)).ToListAsync();
        rows.Should().HaveCount(3, "no event was deleted");
        rows.Should().OnlyContain(e => e.Season == "2019-2020");
        rows.SelectMany(e => e.Files).Select(f => f.Id).Should().BeEquivalentTo(fileIds);
        (await rig.Db.Events.AsNoTracking().CountAsync(e => e.LeagueId == leagueId && e.Season == "2019")).Should().Be(0);
        (await rig.Db.AppSettings.AsNoTracking().FirstAsync()).HubChangesPendingRefresh
            .Should().BeNullOrEmpty("the whole order was applied");
    }

    [Theory]
    [InlineData(Failure.TooManyRequests)]
    [InlineData(Failure.ServerError)]
    public async Task SeasonRenameFeed_TargetedSyncFails_DeletesNothing(Failure failure)
    {
        var (rig, leagueId, local, hub) = await FeedRigAsync();
        await using var _ = rig;
        var fileIds = local.SelectMany(e => e.Files).Select(f => f.Id).ToList();
        hub.Fail("2019-2020", failure);

        await Poller(rig, hub).PollNowAsync(CancellationToken.None);

        rig.Db.ChangeTracker.Clear();
        var rows = await rig.Db.Events.AsNoTracking().Include(e => e.Files)
            .Where(e => local.Select(l => l.Id).Contains(e.Id)).ToListAsync();
        rows.Should().HaveCount(3, "a failed targeted sync is no proof that the events are gone");
        rows.Should().OnlyContain(e => e.Season == "2019");
        rows.SelectMany(e => e.Files).Select(f => f.Id).Should().BeEquivalentTo(fileIds);
        (await rig.Db.EventFiles.AsNoTracking().CountAsync()).Should().Be(fileIds.Count);
        (await rig.Db.AppSettings.AsNoTracking().FirstAsync()).HubChangesPendingRefresh
            .Should().Contain(LeagueExternalId, "the order waits for the next cycle");
        (await rig.Db.AppSettings.AsNoTracking().FirstAsync()).HubChangesPendingRefresh
            .Should().Contain("2019-2020", "the targeted sync must run again before a walk removes anything");
    }

    [Fact]
    public async Task EventRekeyedIntoTheRenamedSeason_KeepsItsRowAndFiles()
    {
        var (rig, leagueId, kept) = await RenamedSeasonRigAsync();
        await using var _ = rig;
        var fileId = kept.Files.Single().Id;
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC });
        hub.Serve("2025-2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        var row = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.ExternalId == RaceA.Id);
        row.Id.Should().Be(kept.Id, "the event updates in place");
        row.Season.Should().Be("2024-2025");
        row.Files.Select(f => f.Id).Should().Equal(fileId);
        (await rig.Db.Events.AsNoTracking().CountAsync(e => e.LeagueId == leagueId && e.Season == "2024")).Should().Be(0);
    }

    // A scheduled refresh fetches only the current and future seasons. It
    // removes the stale events that hold nothing. A stale event with files
    // waits for a sync that fetches every listed season and asks the hub.
    [Fact]
    public async Task OptimizedSync_RemovesFilelessStaleEvents_AndLeavesTheOthersToAFullSync()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var current = DateTime.UtcNow.Year.ToString();
        var fileless = await AddLocalAsync(rig, leagueId, "2009",
            new WireEvent("ev-merged", "Merged", new DateTime(2009, 6, 1, 12, 0, 0, DateTimeKind.Utc)));
        var withFile = await AddLocalAsync(rig, leagueId, "2009",
            new WireEvent("ev-merged-file", "Merged With File", new DateTime(2009, 6, 8, 12, 0, 0, DateTimeKind.Utc)), withFile: true);
        var currentEvents = NextSeason.Select((e, i) => e with { Date = new DateTime(DateTime.UtcNow.Year, 3, 1 + i, 14, 0, 0, DateTimeKind.Utc) }).ToArray();
        foreach (var wire in currentEvents)
            await AddLocalAsync(rig, leagueId, current, wire);
        var hub = new FakeHub(new[] { "2010", current });
        hub.Serve("2010", new[] { new WireEvent("ev-2010", "Old Race", new DateTime(2010, 6, 1, 12, 0, 0, DateTimeKind.Utc)) });
        hub.Serve(current, currentEvents);

        var optimized = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId);

        optimized.Success.Should().BeTrue(optimized.Message);
        optimized.RemovedCount.Should().Be(1);
        hub.LookedUp.Should().BeEmpty("only a full sync asks the hub about stale events with files");
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == fileless.Id)).Should().BeFalse();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == withFile.Id)).Should().BeTrue();

        var full = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        full.Success.Should().BeTrue(full.Message);
        full.RemovedCount.Should().Be(0);
        hub.LookedUp.Should().Equal(withFile.ExternalId);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.Id == withFile.Id))
            .Files.Should().ContainSingle("the hub does not have it, so it stays with its file");
    }

    // Issue #445 review. The hub moved an event with a file to an older
    // listed season. A daily refresh does not fetch that season. The event
    // and its file must survive and end under the older season.
    [Fact]
    public async Task DailySync_EventMovedToAnUnfetchedOlderSeason_KeepsItsFilesAndMoves()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var year = DateTime.UtcNow.Year;
        var current = year.ToString();
        var older = (year - 2).ToString();
        var moved = new WireEvent("ev-moved-back", "Moved Back", new DateTime(year - 2, 6, 1, 12, 0, 0, DateTimeKind.Utc));
        var local = await AddLocalAsync(rig, leagueId, current, moved, withFile: true);
        var fileId = local.Files.Single().Id;
        var currentEvents = NextSeason.Select((e, i) => e with { Date = new DateTime(year, 3, 1 + i, 14, 0, 0, DateTimeKind.Utc) }).ToArray();
        foreach (var wire in currentEvents)
            await AddLocalAsync(rig, leagueId, current, wire);
        var hub = new FakeHub(new[] { older, current });
        hub.Serve(current, currentEvents);
        hub.Serve(older, new[] { moved });
        hub.Locate(moved, older);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        var row = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.Id == local.Id);
        row.Season.Should().Be(older);
        row.Files.Select(f => f.Id).Should().Equal(fileId);
    }

    // The season list is newest first, so the newer season is processed
    // first. Its pass must leave the event for the older season to adopt.
    [Fact]
    public async Task FullSync_EventMovedToAnOlderListedSeason_KeepsItsRowAndFiles()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var moved = await AddLocalAsync(rig, leagueId, "2026", RaceA, withFile: true);
        await AddLocalAsync(rig, leagueId, "2026", RaceB);
        await AddLocalAsync(rig, leagueId, "2025", RaceC);
        var fileId = moved.Files.Single().Id;
        var hub = new FakeHub(new[] { "2025", "2026" });
        hub.Serve("2026", new[] { RaceB });
        hub.Serve("2025", new[] { RaceA, RaceC });

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(0);
        hub.LookedUp.Should().BeEmpty("the older season's fetch moved the row");
        rig.Db.ChangeTracker.Clear();
        var row = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.Id == moved.Id);
        row.Season.Should().Be("2025");
        row.Files.Select(f => f.Id).Should().Equal(fileId);
    }

    // One old season fails every time. It must not stop the removal of a
    // cancelled event in seasons whose years it does not cover.
    [Fact]
    public async Task PermanentFailureOfAnOldSeason_StillRemovesCancelledEventsElsewhere()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var cancelled = await AddLocalAsync(rig, leagueId, "2025-2026",
            new WireEvent("ev-cancelled", "Cancelled", new DateTime(2025, 12, 21, 14, 0, 0, DateTimeKind.Utc)));
        foreach (var wire in NextSeason)
            await AddLocalAsync(rig, leagueId, "2025-2026", wire);
        var oldRace = new WireEvent("ev-2010", "Old Race", new DateTime(2010, 6, 1, 12, 0, 0, DateTimeKind.Utc));
        var old = await AddLocalAsync(rig, leagueId, "2010", oldRace);
        var hub = new FakeHub(new[] { "2010", "2025-2026" });
        hub.Fail("2010", Failure.ServerError);
        hub.Serve("2025-2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(1);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == cancelled.Id)).Should().BeFalse();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == old.Id)).Should().BeTrue("its own season failed");
    }

    // An old placeholder season that answers empty, holds no local events
    // and does not overlap the stale season must not block the cleanup.
    [Fact]
    public async Task EmptyOldPlaceholderSeason_DoesNotBlockTheStaleCleanup()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var fileless = await AddLocalAsync(rig, leagueId, "2015",
            new WireEvent("ev-2015", "Stale 2015", new DateTime(2015, 6, 1, 12, 0, 0, DateTimeKind.Utc)));
        var withFile = await AddLocalAsync(rig, leagueId, "2015",
            new WireEvent("ev-2015-file", "Stale 2015 File", new DateTime(2015, 6, 8, 12, 0, 0, DateTimeKind.Utc)), withFile: true);
        var hub = new FakeHub(new[] { "2010", "2024-2025", "2025-2026" });
        hub.Serve("2010", Array.Empty<WireEvent>());
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC });
        hub.Serve("2025-2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(1);
        hub.LookedUp.Should().Equal(new[] { withFile.ExternalId }, "the pass counts as full, so it asks the hub");
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == fileless.Id)).Should().BeFalse();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == withFile.Id)).Should().BeTrue();
    }

    // A 429 on a lookup stops the lookups and reaches the caller, so the
    // poller can stop its own hub requests. Nothing is removed.
    [Fact]
    public async Task RateLimitedLookup_StopsTheLookupsAndReachesTheCaller()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var first = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-first", "First", new DateTime(1999, 5, 1, 12, 0, 0, DateTimeKind.Utc)), withFile: true);
        var second = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-second", "Second", new DateTime(1999, 5, 8, 12, 0, 0, DateTimeKind.Utc)), withFile: true);
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC });
        hub.Serve("2025-2026", NextSeason);
        hub.FailLookup(first.ExternalId!, HttpStatusCode.TooManyRequests);
        var stamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        (await rig.Db.Leagues.SingleAsync(l => l.Id == leagueId)).LastUpdate = stamp;
        await rig.Db.SaveChangesAsync();

        var act = () => Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        hub.LookedUp.Should().Equal(first.ExternalId);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().CountAsync(e => e.Id == first.Id || e.Id == second.Id)).Should().Be(2);
        (await rig.Db.Events.AsNoTracking().CountAsync(e => e.Season == "2024-2025")).Should().Be(3, "the pass before the 429 is saved");
        (await rig.Db.Leagues.AsNoTracking().SingleAsync(l => l.Id == leagueId)).LastUpdate.Should().BeAfter(stamp, "the sync saved its work before the 429");
    }

    // A download in the queue protects a stale event the same way a file
    // does. The sync asks the hub and keeps the row.
    [Fact]
    public async Task StaleEventWithOnlyAQueuedDownload_IsNotRemoved()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var queuedOnly = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-queued", "Queued", new DateTime(1999, 5, 1, 12, 0, 0, DateTimeKind.Utc)));
        rig.Db.DownloadQueue.Add(new DownloadQueueItem { EventId = queuedOnly.Id, Title = "Queued.720p", DownloadId = "dl-queued" });
        await rig.Db.SaveChangesAsync();
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC });
        hub.Serve("2025-2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(0);
        hub.LookedUp.Should().Equal(queuedOnly.ExternalId);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == queuedOnly.Id)).Should().BeTrue();
    }

    private static async Task<DvrRecording> AddRecordingAsync(PartIdentityIntegrationHarness rig, Event evt, DvrRecordingStatus status)
    {
        var source = new IptvSource { Name = "Fixture", Url = "https://iptv.invalid/list" };
        var channel = new IptvChannel { Source = source, Name = "Fixture", StreamUrl = "https://iptv.invalid/stream" };
        var recording = new DvrRecording
        {
            EventId = evt.Id, Channel = channel, Title = evt.Title, Status = status,
            ScheduledStart = evt.EventDate, ScheduledEnd = evt.EventDate.AddHours(3),
            OriginalScheduledEnd = evt.EventDate.AddHours(3),
        };
        rig.Db.DvrRecordings.Add(recording);
        await rig.Db.SaveChangesAsync();
        return recording;
    }

    // The hub deletes a season and makes it again. The event comes back
    // with a new short id, a new title and a new day. Its TheSportsDB id
    // stays. The files and the recording must end on one row, with no copy.
    [Fact]
    public async Task DeleteThenRecreate_WithTitleDrift_KeepsOneRowWithItsFilesAndRecording()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var old = new WireEvent("ev-before", "Round 4 Race", new DateTime(2019, 6, 1, 12, 0, 0, DateTimeKind.Utc), TsdbId: "990004");
        var local = await AddLocalAsync(rig, leagueId, "2019", old, withFile: true);
        var fileId = local.Files.Single().Id;
        var recording = await AddRecordingAsync(rig, local, DvrRecordingStatus.Scheduled);
        foreach (var wire in NextSeason)
            await AddLocalAsync(rig, leagueId, "2025-2026", wire);
        var recreated = new WireEvent("ev-after", "Round 4 Grand Prix", new DateTime(2019, 6, 2, 13, 0, 0, DateTimeKind.Utc), TsdbId: "990004");
        var hub = new FakeHub(new[] { "2019-2020", "2025-2026" });
        hub.Serve("2019-2020", new[] { recreated });
        hub.Serve("2025-2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        var rows = await rig.Db.Events.AsNoTracking().Include(e => e.Files)
            .Where(e => e.TsdbId == "990004" || e.ExternalId == "ev-after" || e.ExternalId == "ev-before").ToListAsync();
        rows.Should().ContainSingle("the event must not be copied");
        rows[0].Id.Should().Be(local.Id);
        rows[0].ExternalId.Should().Be("ev-after");
        rows[0].Season.Should().Be("2019-2020");
        rows[0].Files.Select(f => f.Id).Should().Equal(fileId);
        var rec = await rig.Db.DvrRecordings.AsNoTracking().SingleAsync(r => r.Id == recording.Id);
        rec.EventId.Should().Be(local.Id);
        rec.ScheduledStart.Should().Be(recreated.Date, "the recording follows the new time");
        (rec.ScheduledEnd - rec.ScheduledStart).Should().Be(TimeSpan.FromHours(3));
    }

    // A season that covers this year can drop off the hub list for a
    // moment. A daily sync must not remove its events.
    [Fact]
    public async Task CurrentSeasonMissingFromTheList_KeepsItsEvents()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var year = DateTime.UtcNow.Year;
        var current = year.ToString();
        var dropped = $"{year - 1}-{year}";
        var kept = await AddLocalAsync(rig, leagueId, dropped,
            new WireEvent("ev-dropped", "Dropped", new DateTime(year, 1, 5, 12, 0, 0, DateTimeKind.Utc)));
        var past = await AddLocalAsync(rig, leagueId, "2009",
            new WireEvent("ev-past", "Past", new DateTime(2009, 1, 5, 12, 0, 0, DateTimeKind.Utc)));
        var currentEvents = NextSeason.Select((e, i) => e with { Date = new DateTime(year, 3, 1 + i, 14, 0, 0, DateTimeKind.Utc) }).ToArray();
        foreach (var wire in currentEvents)
            await AddLocalAsync(rig, leagueId, current, wire);
        var hub = new FakeHub(new[] { current });
        hub.Serve(current, currentEvents);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(1);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == kept.Id)).Should().BeTrue();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == past.Id)).Should().BeFalse("past stale seasons keep the rule");
    }

    // A legacy duplicate holds two parts. Its migrated twin holds one of
    // them. The twin gets the part it lacks, and no file row is deleted.
    [Fact]
    public async Task FossilTwin_WithFiles_KeepsEveryFileRow()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var date = new DateTime(2025, 10, 5, 14, 0, 0, DateTimeKind.Utc);
        var twin = await AddLocalAsync(rig, leagueId, "2025-2026", new WireEvent("ev-twin", "Card", date, TsdbId: "770001"));
        var fossil = await AddLocalAsync(rig, leagueId, "2025-2026", new WireEvent("770001", "Card", date));
        var root = (await rig.Db.RootFolders.FirstAsync()).Path;
        EventFile Part(int number, string name) => new()
        {
            FilePath = Path.Combine(root, $"card-{number}-{Guid.NewGuid():N}.mkv"), Exists = true, Size = 1,
            Quality = "WEBDL-720p", PartNumber = number, PartName = name,
        };
        twin.Files.Add(Part(2, "Main Card"));
        twin.HasFile = true;
        fossil.Files.Add(Part(1, "Prelims"));
        fossil.Files.Add(Part(2, "Main Card"));
        fossil.HasFile = true;
        foreach (var wire in NextSeason)
            await AddLocalAsync(rig, leagueId, "2025-2026", wire);
        await rig.Db.SaveChangesAsync();
        var fileIds = await rig.Db.EventFiles.Select(f => f.Id).ToListAsync();
        var hub = new FakeHub(new[] { "2025-2026" });
        hub.Serve("2025-2026", NextSeason.Append(new WireEvent("ev-twin", "Card", date, TsdbId: "770001")).ToArray());

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.EventFiles.AsNoTracking().Select(f => f.Id).ToListAsync()).Should().BeEquivalentTo(fileIds);
        var twinParts = await rig.Db.EventFiles.AsNoTracking().Where(f => f.EventId == twin.Id).Select(f => f.PartNumber).ToListAsync();
        twinParts.Should().BeEquivalentTo(new int?[] { 1, 2 });
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == fossil.Id)).Should().BeTrue("it still holds a part the twin has");
    }

    // A manual monitor choice, grab history and an active recording each
    // protect a file-less event. A finished recording does not.
    [Fact]
    public async Task ManualChoiceHistoryAndActiveRecording_ProtectAFilelessEvent()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var manual = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-manual", "Manual", new DateTime(1999, 5, 1, 12, 0, 0, DateTimeKind.Utc)));
        manual.ManuallyMonitored = true;
        var grabbed = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-grabbed", "Grabbed", new DateTime(1999, 5, 2, 12, 0, 0, DateTimeKind.Utc)));
        rig.Db.GrabHistory.Add(new GrabHistory { EventId = grabbed.Id, Title = "Grabbed.720p", Indexer = "Fixture",
            DownloadUrl = "http://fixture.invalid/g.nzb", Guid = "guid-g", Protocol = "Usenet" });
        var recorded = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-recorded", "Recorded", new DateTime(1999, 5, 3, 12, 0, 0, DateTimeKind.Utc)));
        await AddRecordingAsync(rig, recorded, DvrRecordingStatus.Scheduled);
        var finished = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-finished", "Finished", new DateTime(1999, 5, 4, 12, 0, 0, DateTimeKind.Utc)));
        await AddRecordingAsync(rig, finished, DvrRecordingStatus.Failed);
        await rig.Db.SaveChangesAsync();
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC });
        hub.Serve("2025-2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(1);
        hub.LookedUp.Should().BeEquivalentTo(new[] { manual.ExternalId, grabbed.ExternalId, recorded.ExternalId });
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().CountAsync(e => e.Id == manual.Id || e.Id == grabbed.Id || e.Id == recorded.Id))
            .Should().Be(3);
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == finished.Id)).Should().BeFalse("a failed recording does not protect it");
    }

    // Probe 1. A row protected only by a queued download hands its records
    // to the hub's new row. The new row must not look downloaded.
    [Fact]
    public async Task HandoffWithoutFiles_DoesNotMarkTheNewRowAsDownloaded()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var oldWire = new WireEvent("ev-q-old", "Queued Race", new DateTime(2024, 12, 1, 12, 0, 0, DateTimeKind.Utc), TsdbId: "445101");
        var newWire = oldWire with { Id = "ev-q-new", Title = "Queued Race Renamed", TsdbId = null };
        var stale = await AddLocalAsync(rig, leagueId, "1999", oldWire);
        var queued = new DownloadQueueItem { EventId = stale.Id, Title = "Queued.720p", DownloadId = "dl-q", Status = DownloadStatus.Downloading };
        rig.Db.DownloadQueue.Add(queued);
        await rig.Db.SaveChangesAsync();
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC, newWire });
        hub.Serve("2025-2026", NextSeason);
        hub.Locate(oldWire.TsdbId!, newWire with { TsdbId = oldWire.TsdbId }, "2024-2025");

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        var row = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.ExternalId == newWire.Id);
        row.HasFile.Should().BeFalse("no file moved, so the row must still be searched");
        row.Files.Should().BeEmpty();
        row.FilePath.Should().BeNull();
        (await rig.Db.DownloadQueue.AsNoTracking().SingleAsync(q => q.Id == queued.Id)).EventId.Should().Be(row.Id);
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == stale.Id)).Should().BeFalse();
    }

    // Probe 3. The hub renames the current season and gives its events new
    // ids with no TheSportsDB id. The old rows must move in place, with no
    // second monitored copy.
    [Fact]
    public async Task RenamedCurrentSeason_WithNewIds_MovesTheOldRowsInPlace()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var year = DateTime.UtcNow.Year;
        var oldName = year.ToString();
        var newName = $"{year}-{year + 1}";
        var oldWires = NextSeason.Select((e, i) => e with { Id = $"ev-old-{i}", Date = new DateTime(year, 3, 1 + i, 14, 0, 0, DateTimeKind.Utc) }).ToArray();
        var local = new List<Event>();
        for (var i = 0; i < oldWires.Length; i++)
            local.Add(await AddLocalAsync(rig, leagueId, oldName, oldWires[i], withFile: i == 0));
        var fileId = local[0].Files.Single().Id;
        var newWires = oldWires.Select((e, i) => e with { Id = $"ev-new-{i}" }).ToArray();
        var hub = new FakeHub(new[] { newName });
        hub.Serve(newName, newWires);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        var rows = await rig.Db.Events.AsNoTracking().Include(e => e.Files).Where(e => e.LeagueId == leagueId).ToListAsync();
        rows.Should().HaveCount(oldWires.Length, "no second copy may stay");
        rows.Select(e => e.Id).Should().BeEquivalentTo(local.Select(e => e.Id));
        rows.Should().OnlyContain(e => e.Season == newName && e.ExternalId!.StartsWith("ev-new-"));
        rows.Single(e => e.Id == local[0].Id).Files.Select(f => f.Id).Should().Equal(fileId);
    }

    // Probe 4. A download that waits for its import, one with an import
    // warning, and a delayed grab each protect a file-less event.
    [Fact]
    public async Task CompletedDownload_ImportWarning_AndPendingRelease_ProtectAFilelessEvent()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var completed = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-completed", "Completed", new DateTime(1999, 5, 1, 12, 0, 0, DateTimeKind.Utc)));
        var warned = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-warned", "Warned", new DateTime(1999, 5, 2, 12, 0, 0, DateTimeKind.Utc)));
        var delayed = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-delayed", "Delayed", new DateTime(1999, 5, 3, 12, 0, 0, DateTimeKind.Utc)));
        var imported = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-imported", "Imported", new DateTime(1999, 5, 4, 12, 0, 0, DateTimeKind.Utc)));
        rig.Db.DownloadQueue.AddRange(
            new DownloadQueueItem { EventId = completed.Id, Title = "c", DownloadId = "dl-c", Status = DownloadStatus.Completed },
            new DownloadQueueItem { EventId = warned.Id, Title = "w", DownloadId = "dl-w", Status = DownloadStatus.ImportWarning },
            new DownloadQueueItem { EventId = imported.Id, Title = "i", DownloadId = "dl-i", Status = DownloadStatus.Imported });
        rig.Db.PendingReleases.Add(new PendingRelease { EventId = delayed.Id, Title = "d", Guid = "g-d", DownloadUrl = "http://fixture.invalid/d.nzb" });
        await rig.Db.SaveChangesAsync();
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC });
        hub.Serve("2025-2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(1);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().CountAsync(e => e.Id == completed.Id || e.Id == warned.Id || e.Id == delayed.Id))
            .Should().Be(3);
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == imported.Id)).Should().BeFalse("an imported download does not protect it");
        (await rig.Db.PendingReleases.AsNoTracking().CountAsync()).Should().Be(1);
    }

    // The hub answered a lookup with no event. The next sync must not ask
    // about the same id again within the day.
    [Fact]
    public async Task EmptyLookup_IsNotRepeatedOnTheNextSync()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var gone = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-gone-file", "Gone", new DateTime(1999, 5, 8, 12, 0, 0, DateTimeKind.Utc)), withFile: true);
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC });
        hub.Serve("2025-2026", NextSeason);

        await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);
        await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        hub.LookedUp.Should().Equal(gone.ExternalId);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == gone.Id)).Should().BeTrue();
    }

    // A 429 in the walk's lookups leaves the order done, because the walk
    // saved its work. The rest of the cycle stops, so the next league waits.
    [Fact]
    public async Task PollerWalkRateLimitedInLookups_FinishesTheOrderAndStopsTheCycle()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var year = DateTime.UtcNow.Year;
        var current = year.ToString();
        var currentEvents = NextSeason.Select((e, i) => e with { Date = new DateTime(year, 3, 1 + i, 14, 0, 0, DateTimeKind.Utc) }).ToArray();
        foreach (var wire in currentEvents)
            await AddLocalAsync(rig, leagueId, current, wire);
        var orphan = await AddLocalAsync(rig, leagueId, current,
            new WireEvent("ev-orphan", "Orphan", new DateTime(year, 4, 1, 14, 0, 0, DateTimeKind.Utc)), withFile: true);
        if (!await rig.Db.AppSettings.AnyAsync())
            rig.Db.AppSettings.Add(new AppSettings());
        await rig.Db.SaveChangesAsync();
        (await rig.Db.AppSettings.FirstAsync()).HubChangesCursor = 50;
        // A carried order runs first, so this league is refreshed before
        // the second one.
        var carried = new Dictionary<string, HubChangesRefreshOrder>(StringComparer.OrdinalIgnoreCase);
        HubChangesRefreshPlan.Merge(carried, LeagueExternalId, Array.Empty<string>(), smartWalk: true);
        (await rig.Db.AppSettings.FirstAsync()).HubChangesPendingRefresh = HubChangesRefreshPlan.Save(carried);
        await rig.Db.SaveChangesAsync();
        var hub = new FakeHub(new[] { current });
        hub.Serve(current, currentEvents);
        hub.FailLookup(orphan.ExternalId!, HttpStatusCode.TooManyRequests);
        var league = await rig.Db.Leagues.SingleAsync(l => l.Id == leagueId);
        rig.Db.Leagues.Add(new League { Name = "Second League", Sport = "Motorsport", ExternalId = "lg-second",
            Monitored = true, RootFolderId = league.RootFolderId, QualityProfileId = league.QualityProfileId,
            LastUpdate = DateTime.UtcNow });
        await rig.Db.SaveChangesAsync();
        hub.Feed(
            new { seq = 51, entity = "season", id = "sn-old", league = LeagueExternalId, season = "2009", change = "deleted" },
            new { seq = 52, entity = "season", id = "sn-other", league = "lg-second", season = "2009", change = "deleted" });

        await Poller(rig, hub).PollNowAsync(CancellationToken.None);

        hub.LookedUp.Should().Equal(orphan.ExternalId);
        hub.PathLog.Should().NotContain(p => p.Contains("lg-second"), "the 429 stops the rest of the cycle");
        rig.Db.ChangeTracker.Clear();
        var pending = (await rig.Db.AppSettings.AsNoTracking().FirstAsync()).HubChangesPendingRefresh;
        pending.Should().NotContain(LeagueExternalId, "the walk saved its work, so its order is done");
        pending.Should().Contain("lg-second", "the next league waits for the next cycle");
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == orphan.Id)).Should().BeTrue();
    }

    // Probe 5. Two files with the same part number are the same part, even
    // when their names differ. The twin keeps one, the duplicate keeps the
    // other, and a part with no number moves by its name.
    [Fact]
    public async Task FossilTwin_ComparesPartsByNumberFirst()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var date = new DateTime(2025, 10, 5, 14, 0, 0, DateTimeKind.Utc);
        var twin = await AddLocalAsync(rig, leagueId, "2025-2026", new WireEvent("ev-twin5", "Card", date, TsdbId: "770005"));
        var fossil = await AddLocalAsync(rig, leagueId, "2025-2026", new WireEvent("770005", "Card", date));
        var root = (await rig.Db.RootFolders.FirstAsync()).Path;
        EventFile Part(int? number, string name) => new()
        {
            FilePath = Path.Combine(root, $"card5-{Guid.NewGuid():N}.mkv"), Exists = true, Size = 1,
            Quality = "WEBDL-720p", PartNumber = number, PartName = name,
        };
        twin.Files.Add(Part(1, "Prelims"));
        twin.HasFile = true;
        fossil.Files.Add(Part(1, "Early Prelims"));
        fossil.Files.Add(Part(null, "Main Card"));
        fossil.HasFile = true;
        fossil.ManuallyMonitored = true;
        fossil.Monitored = true;
        fossil.MonitoredParts = "Prelims";
        foreach (var wire in NextSeason)
            await AddLocalAsync(rig, leagueId, "2025-2026", wire);
        await rig.Db.SaveChangesAsync();
        var fileIds = await rig.Db.EventFiles.Select(f => f.Id).ToListAsync();
        var hub = new FakeHub(new[] { "2025-2026" });
        hub.Serve("2025-2026", NextSeason.Append(new WireEvent("ev-twin5", "Card", date, TsdbId: "770005")).ToArray());

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.EventFiles.AsNoTracking().Select(f => f.Id).ToListAsync()).Should().BeEquivalentTo(fileIds);
        var twinFiles = await rig.Db.EventFiles.AsNoTracking().Where(f => f.EventId == twin.Id).ToListAsync();
        twinFiles.Select(f => f.PartName).Should().BeEquivalentTo(new[] { "Prelims", "Main Card" });
        (await rig.Db.EventFiles.AsNoTracking().SingleAsync(f => f.EventId == fossil.Id)).PartName.Should().Be("Early Prelims");
        var keptFossil = await rig.Db.Events.AsNoTracking().SingleAsync(e => e.Id == fossil.Id);
        keptFossil.Monitored.Should().BeFalse("the kept duplicate stops searching");
        var twinRow = await rig.Db.Events.AsNoTracking().SingleAsync(e => e.Id == twin.Id);
        twinRow.ManuallyMonitored.Should().BeTrue("the user's choice moves to the twin");
        twinRow.Monitored.Should().BeTrue();
        twinRow.MonitoredParts.Should().BeNull("the twin was already monitored, so its parts are not narrowed");
    }

    // Two local rows share one TheSportsDB id. The row with the file wins.
    [Fact]
    public async Task TsdbIdMatch_PrefersTheRowWithFiles()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var date = new DateTime(2019, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var bare = await AddLocalAsync(rig, leagueId, "2019", new WireEvent("ev-bare", "Race Bare", date, TsdbId: "880001"));
        var withFile = await AddLocalAsync(rig, leagueId, "2019", new WireEvent("ev-file", "Race File", date.AddHours(1), TsdbId: "880001"), withFile: true);
        foreach (var wire in NextSeason)
            await AddLocalAsync(rig, leagueId, "2025-2026", wire);
        var hub = new FakeHub(new[] { "2019-2020", "2025-2026" });
        hub.Serve("2019-2020", new[] { new WireEvent("ev-new88", "Race", date.AddDays(1), TsdbId: "880001") });
        hub.Serve("2025-2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().SingleAsync(e => e.ExternalId == "ev-new88")).Id.Should().Be(withFile.Id);
        bare.Id.Should().NotBe(withFile.Id);
    }

    // A row in the season being processed and a copy in a stale season
    // share date and title. The row of this season wins, as before.
    [Fact]
    public async Task DateTitleMatch_PrefersTheRowOfTheProcessedSeason()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var date = new DateTime(2025, 10, 5, 14, 0, 0, DateTimeKind.Utc);
        var inSeason = await AddLocalAsync(rig, leagueId, "2025-2026", new WireEvent("ev-a-old", "Clash", date), withFile: true);
        var inStale = await AddLocalAsync(rig, leagueId, "1999", new WireEvent("ev-b-old", "Clash", date));
        foreach (var wire in NextSeason)
            await AddLocalAsync(rig, leagueId, "2025-2026", wire);
        var hub = new FakeHub(new[] { "2025-2026" });
        hub.Serve("2025-2026", NextSeason.Append(new WireEvent("ev-a-new", "Clash", date)).ToArray());

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().SingleAsync(e => e.ExternalId == "ev-a-new")).Id.Should().Be(inSeason.Id);
        (await rig.Db.Events.AsNoTracking().CountAsync(e => e.Title == "Clash" && e.Season == "2025-2026")).Should().Be(1);
        inStale.Id.Should().NotBe(inSeason.Id);
    }

    // A guessed future season is fetched but not listed. Its own rows must
    // count once in the date and title match.
    [Fact]
    public async Task GuessedFutureSeason_RowIsAdoptedInPlace()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var year = DateTime.UtcNow.Year;
        var current = year.ToString();
        var future = (year + 1).ToString();
        var currentEvents = NextSeason.Select((e, i) => e with { Date = new DateTime(year, 3, 1 + i, 14, 0, 0, DateTimeKind.Utc) }).ToArray();
        foreach (var wire in currentEvents)
            await AddLocalAsync(rig, leagueId, current, wire);
        var date = new DateTime(year + 1, 3, 1, 14, 0, 0, DateTimeKind.Utc);
        var local = await AddLocalAsync(rig, leagueId, future, new WireEvent("ev-fut-old", "Opener", date), withFile: true);
        var hub = new FakeHub(new[] { current });
        hub.Serve(current, currentEvents);
        hub.Serve(future, new[] { new WireEvent("ev-fut-new", "Opener", date) });

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        var rows = await rig.Db.Events.AsNoTracking().Where(e => e.Title == "Opener").ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(local.Id);
        rows[0].ExternalId.Should().Be("ev-fut-new");
    }

    // Probe A. A doubleheader: Game 1 matches by id and Game 2 is new, both
    // with one title on one day. A season the hub no longer lists holds an
    // old copy of Game 1 with a file. Game 2 must not take that copy.
    [Fact]
    public async Task Doubleheader_SecondGameDoesNotTakeTheFirstGamesStaleCopy()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var day = new DateTime(2025, 10, 5, 13, 0, 0, DateTimeKind.Utc);
        var game1 = new WireEvent("ev-g1", "Home vs Away", day);
        var game2 = new WireEvent("ev-g2", "Home vs Away", day.AddHours(4));
        var game1Row = await AddLocalAsync(rig, leagueId, "2025-2026", game1);
        foreach (var wire in NextSeason)
            await AddLocalAsync(rig, leagueId, "2025-2026", wire);
        var staleCopy = await AddLocalAsync(rig, leagueId, "2025", new WireEvent("ev-g1-old", "Home vs Away", day.AddHours(1)), withFile: true);
        var fileId = staleCopy.Files.Single().Id;
        var hub = new FakeHub(new[] { "2025-2026" });
        hub.Serve("2025-2026", NextSeason.Append(game1).Append(game2).ToArray());

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        var g2 = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.ExternalId == "ev-g2");
        g2.Id.Should().NotBe(staleCopy.Id, "Game 2 gets a new row");
        g2.HasFile.Should().BeFalse();
        g2.Files.Should().BeEmpty();
        (await rig.Db.Events.AsNoTracking().SingleAsync(e => e.ExternalId == "ev-g1")).Id.Should().Be(game1Row.Id);
        var copy = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.Id == staleCopy.Id);
        copy.ExternalId.Should().Be("ev-g1-old");
        copy.Season.Should().Be("2025");
        copy.Files.Select(f => f.Id).Should().Equal(fileId);
    }

    // A fossil duplicate and its twin with chosen monitor states. Returns
    // the twin after a full sync.
    private static async Task<Event> FossilMonitorCaseAsync(bool twinHasFiles, bool twinManual, bool twinMonitored,
        bool fossilMonitored)
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var date = new DateTime(2025, 10, 5, 14, 0, 0, DateTimeKind.Utc);
        var twin = await AddLocalAsync(rig, leagueId, "2025-2026", new WireEvent("ev-twin-m", "Card", date, TsdbId: "770009"),
            withFile: twinHasFiles);
        twin.ManuallyMonitored = twinManual;
        twin.Monitored = twinMonitored;
        var fossil = await AddLocalAsync(rig, leagueId, "2025-2026", new WireEvent("770009", "Card", date), withFile: true);
        fossil.ManuallyMonitored = true;
        fossil.Monitored = fossilMonitored;
        foreach (var wire in NextSeason)
            await AddLocalAsync(rig, leagueId, "2025-2026", wire);
        await rig.Db.SaveChangesAsync();
        var hub = new FakeHub(new[] { "2025-2026" });
        hub.Serve("2025-2026", NextSeason.Append(new WireEvent("ev-twin-m", "Card", date, TsdbId: "770009")).ToArray());

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        return await rig.Db.Events.AsNoTracking().SingleAsync(e => e.Id == twin.Id);
    }

    // The user unmonitored the duplicate by hand. That choice must not stop
    // the twin the sync monitors, whether or not the twin has files.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FossilHandUnmonitor_DoesNotUnmonitorTheTwin(bool twinHasFiles)
    {
        var twin = await FossilMonitorCaseAsync(twinHasFiles, twinManual: false, twinMonitored: true, fossilMonitored: false);

        twin.Monitored.Should().BeTrue("a hand-made unmonitor on the duplicate never lowers the twin");
        twin.ManuallyMonitored.Should().BeFalse();
    }

    // Both rows carry a choice made by hand. The twin's choice wins.
    [Fact]
    public async Task FossilHandMonitor_DoesNotOverrideTheTwinsOwnChoice()
    {
        var twin = await FossilMonitorCaseAsync(twinHasFiles: true, twinManual: true, twinMonitored: false, fossilMonitored: true);

        twin.ManuallyMonitored.Should().BeTrue();
        twin.Monitored.Should().BeFalse("the twin's own choice wins");
    }

    // ProbeR. Both games of a doubleheader are new, and a stale copy with a
    // file shares their date and title. Neither game may take it.
    [Fact]
    public async Task Doubleheader_BothGamesNew_DoNotTakeTheStaleCopy()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var day = new DateTime(2025, 10, 5, 13, 0, 0, DateTimeKind.Utc);
        foreach (var wire in NextSeason)
            await AddLocalAsync(rig, leagueId, "2025-2026", wire);
        var staleCopy = await AddLocalAsync(rig, leagueId, "2025", new WireEvent("ev-old-dh", "Home vs Away", day), withFile: true);
        var fileId = staleCopy.Files.Single().Id;
        var hub = new FakeHub(new[] { "2025-2026" });
        hub.Serve("2025-2026", NextSeason
            .Append(new WireEvent("ev-r1", "Home vs Away", day))
            .Append(new WireEvent("ev-r2", "Home vs Away", day.AddHours(4))).ToArray());

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        var games = await rig.Db.Events.AsNoTracking().Include(e => e.Files)
            .Where(e => e.ExternalId == "ev-r1" || e.ExternalId == "ev-r2").ToListAsync();
        games.Should().HaveCount(2);
        games.Should().OnlyContain(g => g.Id != staleCopy.Id && !g.HasFile && g.Files.Count == 0);
        var copy = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.Id == staleCopy.Id);
        copy.ExternalId.Should().Be("ev-old-dh");
        copy.Files.Select(f => f.Id).Should().Equal(fileId);
    }

    // ProbeL. The hub moved Game 1 to the next day. Game 2 keeps the old
    // date and title, which Game 1's row of this season still carries.
    // Game 2 must not take Game 1's stale copy.
    [Fact]
    public async Task Doubleheader_FirstGameMovedADay_SecondGameDoesNotTakeTheStaleCopy()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var day = new DateTime(2025, 10, 5, 13, 0, 0, DateTimeKind.Utc);
        var game1Row = await AddLocalAsync(rig, leagueId, "2025-2026", new WireEvent("ev-l1", "Home vs Away", day));
        foreach (var wire in NextSeason)
            await AddLocalAsync(rig, leagueId, "2025-2026", wire);
        var staleCopy = await AddLocalAsync(rig, leagueId, "2025", new WireEvent("ev-l1-old", "Home vs Away", day.AddHours(1)), withFile: true);
        var fileId = staleCopy.Files.Single().Id;
        var hub = new FakeHub(new[] { "2025-2026" });
        hub.Serve("2025-2026", NextSeason
            .Append(new WireEvent("ev-l1", "Home vs Away", day.AddDays(1)))
            .Append(new WireEvent("ev-l2", "Home vs Away", day.AddHours(4))).ToArray());

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        rig.Db.ChangeTracker.Clear();
        var g1 = await rig.Db.Events.AsNoTracking().SingleAsync(e => e.ExternalId == "ev-l1");
        g1.Id.Should().Be(game1Row.Id);
        g1.EventDate.Date.Should().Be(day.AddDays(1).Date);
        var g2 = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.ExternalId == "ev-l2");
        g2.Id.Should().NotBe(staleCopy.Id);
        g2.Files.Should().BeEmpty();
        var copy = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.Id == staleCopy.Id);
        copy.ExternalId.Should().Be("ev-l1-old");
        copy.Files.Select(f => f.Id).Should().Equal(fileId);
    }

    [Fact]
    public async Task FailedLookup_StopsTheOtherLookups()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var first = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-first", "First", new DateTime(1999, 5, 1, 12, 0, 0, DateTimeKind.Utc)), withFile: true);
        var second = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-second", "Second", new DateTime(1999, 5, 8, 12, 0, 0, DateTimeKind.Utc)), withFile: true);
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC });
        hub.Serve("2025-2026", NextSeason);
        hub.FailLookup(first.ExternalId!, HttpStatusCode.InternalServerError);
        hub.Locate(new WireEvent(second.ExternalId!, "Second", second.EventDate), "2024-2025");

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        hub.LookedUp.Should().Equal(first.ExternalId);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().SingleAsync(e => e.Id == second.Id)).Season.Should().Be("1999", "its lookup waits for a later sync");
    }

    [Fact]
    public void LookupWindow_RotatesSoEveryEventGetsATurn()
    {
        var items = Enumerable.Range(0, 120).ToList();

        LeagueEventSyncService.PickRotatingWindow(items, 50, 0).Should().Equal(Enumerable.Range(0, 50));
        LeagueEventSyncService.PickRotatingWindow(items, 50, 1).Should().Equal(Enumerable.Range(50, 50));
        LeagueEventSyncService.PickRotatingWindow(items, 50, 2).Should().Equal(Enumerable.Range(100, 20).Concat(Enumerable.Range(0, 30)));
        Enumerable.Range(0, 3).SelectMany(d => LeagueEventSyncService.PickRotatingWindow(items, 50, d))
            .Distinct().Should().HaveCount(120);
        LeagueEventSyncService.PickRotatingWindow(items.Take(10).ToList(), 50, 7).Should().HaveCount(10);
    }

    [Fact]
    public void TargetedSyncWithAFailedEvent_HoldsRemovalsButDoesNotKeepTheOrder()
    {
        var failedEvent = new LeagueEventSyncResult { Success = true, FailedCount = 1 };
        HubChangesRefreshPlan.JudgeTargetedSync(failedEvent).Should().Be((false, true));

        var skipped = new LeagueEventSyncResult { Success = true };
        skipped.SkippedSeasons.Add("2019-2020");
        HubChangesRefreshPlan.JudgeTargetedSync(skipped).Should().Be((true, true));

        HubChangesRefreshPlan.JudgeTargetedSync(new LeagueEventSyncResult { Success = false }).Should().Be((true, true));
        HubChangesRefreshPlan.JudgeTargetedSync(new LeagueEventSyncResult { Success = true }).Should().Be((false, false));
    }

    [Theory]
    [InlineData("2024", "2024-2025", true)]
    [InlineData("2024-2025", "2025", true)]
    [InlineData("2010", "2015", false)]
    [InlineData("2015", "2024-2025", false)]
    [InlineData("Season One", "2015", true)]
    public void SeasonsOverlap_ComparesYearRanges(string a, string b, bool expected) =>
        LeagueEventSyncService.SeasonsOverlap(a, b).Should().Be(expected);

    // The hub lists a season ahead of time with no events. It starts after
    // every stale season, so it cannot hold the renamed events.
    [Fact]
    public async Task EmptyLaterListedSeason_DoesNotStopTheStaleCleanup()
    {
        var (rig, leagueId, _) = await RenamedSeasonRigAsync();
        await using var __ = rig;
        var gone = await AddLocalAsync(rig, leagueId, "1999",
            new WireEvent("ev-gone", "Gone", new DateTime(1999, 5, 1, 12, 0, 0, DateTimeKind.Utc)));
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026", "2026-2027" });
        hub.Serve("2024-2025", new[] { RaceA, RaceB, RaceC });
        hub.Serve("2025-2026", NextSeason);
        hub.Serve("2026-2027", Array.Empty<WireEvent>());

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(1);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().AnyAsync(e => e.Id == gone.Id)).Should().BeFalse();
    }

    // The renamed season answers with an empty list for a moment. Its
    // events can still be the ones under the old name, so they stay.
    [Fact]
    public async Task RenamedSeasonAnsweringEmpty_DeletesNoEventsOrFiles()
    {
        var (rig, leagueId, kept) = await RenamedSeasonRigAsync();
        await using var _ = rig;
        var fileId = kept.Files.Single().Id;
        var hub = new FakeHub(new[] { "2024-2025", "2025-2026" });
        hub.Serve("2024-2025", Array.Empty<WireEvent>());
        hub.Serve("2025-2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().CountAsync(e => e.LeagueId == leagueId && e.Season == "2024")).Should().Be(3);
        (await rig.Db.EventFiles.AsNoTracking().SingleAsync(f => f.Id == fileId)).EventId.Should().Be(kept.Id);
    }

    // The hub renamed "2024-2025" to "2025", and "2025" answers empty for a
    // moment. It starts in the year the stale season ends, so it can hold
    // the renamed events.
    [Fact]
    public async Task EndYearRenameAnsweringEmpty_DeletesNoEventsOrFiles()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var kept = await AddLocalAsync(rig, leagueId, "2024-2025", RaceA, withFile: true);
        await AddLocalAsync(rig, leagueId, "2024-2025", RaceB);
        await AddLocalAsync(rig, leagueId, "2024-2025", RaceC);
        foreach (var wire in NextSeason)
            await AddLocalAsync(rig, leagueId, "2026", wire);
        var fileId = kept.Files.Single().Id;
        var hub = new FakeHub(new[] { "2025", "2026" });
        hub.Serve("2025", Array.Empty<WireEvent>());
        hub.Serve("2026", NextSeason);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Events.AsNoTracking().CountAsync(e => e.LeagueId == leagueId && e.Season == "2024-2025")).Should().Be(3);
        (await rig.Db.EventFiles.AsNoTracking().SingleAsync(f => f.Id == fileId)).EventId.Should().Be(kept.Id);
    }

    [Theory]
    [InlineData("2024", 2024)]
    [InlineData("2024-2025", 2025)]
    [InlineData("2024/25", 2025)]
    [InlineData("2025-2027", 2027)]
    [InlineData("1999-00", 2000)]
    [InlineData("2024-2025 Playoffs", 2025)]
    [InlineData("2024 Round 3", 2024)]
    [InlineData("2024 Week 10", 2024)]
    [InlineData("2024-25 Series 2", 2025)]
    [InlineData("Round 3", null)]
    [InlineData("Series 2", null)]
    [InlineData("Week 10", null)]
    [InlineData("Season One", null)]
    [InlineData(null, null)]
    public void SeasonEndYear_ReadsTheLastYear(string? season, int? expected) =>
        LeagueEventSyncService.SeasonEndYear(season).Should().Be(expected);

    // Both seasons are listed, and the old one comes first. The old season's
    // own pass must not delete the event before the new season adopts it.
    [Fact]
    public async Task EventMovedToALaterListedSeason_KeepsItsRowAndFiles()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var moved = await AddLocalAsync(rig, leagueId, "2025", RaceA, withFile: true);
        await AddLocalAsync(rig, leagueId, "2025", RaceB);
        await AddLocalAsync(rig, leagueId, "2026", RaceC);
        var fileId = moved.Files.Single().Id;
        var hub = new FakeHub(new[] { "2025", "2026" });
        hub.Serve("2025", new[] { RaceB });
        hub.Serve("2026", new[] { RaceA, RaceC });

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        var row = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.ExternalId == RaceA.Id);
        row.Id.Should().Be(moved.Id);
        row.Season.Should().Be("2026");
        row.Files.Select(f => f.Id).Should().Equal(fileId);
    }

    // The event moved to a season whose fetch failed. The old season's
    // pass cannot tell a move from a cancellation, so it must wait.
    [Fact]
    public async Task EventMovedToAFailedSeason_IsNotDeletedByTheOldSeasonPass()
    {
        var (rig, leagueId) = await RigAsync();
        await using var _ = rig;
        var moved = await AddLocalAsync(rig, leagueId, "2025", RaceA, withFile: true);
        await AddLocalAsync(rig, leagueId, "2025", RaceB);
        await AddLocalAsync(rig, leagueId, "2026", RaceC);
        var fileId = moved.Files.Single().Id;
        var hub = new FakeHub(new[] { "2025", "2026" });
        hub.Serve("2025", new[] { RaceB });
        hub.Fail("2026", Failure.TooManyRequests);

        var result = await Sync(rig, hub).SyncLeagueEventsAsync(leagueId, fullHistoricalSync: true);

        result.Success.Should().BeTrue(result.Message);
        result.RemovedCount.Should().Be(0);
        rig.Db.ChangeTracker.Clear();
        var row = await rig.Db.Events.AsNoTracking().Include(e => e.Files).SingleAsync(e => e.Id == moved.Id);
        row.Season.Should().Be("2025");
        row.Files.Select(f => f.Id).Should().Equal(fileId);
    }

    // Answers the season list and the schedules it is given. Everything
    // else is a 404, like a hub route that has no data.
    private sealed class FakeHub : HttpMessageHandler
    {
        private readonly string[] _seasons;
        private readonly Dictionary<string, WireEvent[]> _schedules = new();
        private readonly Dictionary<string, Failure> _failures = new();

        // The real hub lists seasons newest first.
        public FakeHub(string[] seasons) =>
            _seasons = seasons.OrderByDescending(s => SeasonNumberParser.Parse(s) ?? 0).ThenByDescending(s => s, StringComparer.Ordinal).ToArray();

        public void Serve(string season, WireEvent[] events) => _schedules[season] = events;

        public void Fail(string season, Failure failure) => _failures[season] = failure;

        // Records the changes feed returns once, then an empty page.
        private object[]? _feed;
        public void Feed(params object[] records) => _feed = records;

        // Where the event lookup finds an event. A missing id gets an empty list.
        private readonly Dictionary<string, (WireEvent Event, string Season)> _lookups = new();
        public void Locate(WireEvent e, string season) => _lookups[e.Id] = (e, season);
        public void Locate(string id, WireEvent answer, string season) => _lookups[id] = (answer, season);
        public List<string> LookedUp { get; } = new();

        // Every path the fake hub was asked for.
        public System.Collections.Concurrent.ConcurrentQueue<string> PathLog { get; } = new();
        private ConcurrentPaths Paths => new(PathLog);
        private readonly struct ConcurrentPaths
        {
            private readonly System.Collections.Concurrent.ConcurrentQueue<string> _log;
            public ConcurrentPaths(System.Collections.Concurrent.ConcurrentQueue<string> log) => _log = log;
            public void Add(string path) => _log.Enqueue(path);
        }

        // A lookup that fails with this status.
        private readonly Dictionary<string, HttpStatusCode> _lookupFailures = new();
        public void FailLookup(string id, HttpStatusCode status) => _lookupFailures[id] = status;

        private static object Wire(WireEvent e, string season) => new Dictionary<string, object?>
        {
            ["idEvent"] = e.Id, ["strEvent"] = e.Title, ["strSport"] = "Motorsport", ["idLeague"] = LeagueExternalId,
            ["strSeason"] = season, ["strTimestamp"] = e.Date.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["dateEvent"] = e.Date.ToString("yyyy-MM-dd"), ["strStatus"] = "Match Finished",
            ["tsdbId"] = e.TsdbId,
        };

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path.EndsWith($"/list/seasons/{LeagueExternalId}", StringComparison.Ordinal))
                return Task.FromResult(Json(new { list = _seasons.Select(s => new { strSeason = s }).ToArray() }));

            if (path.EndsWith("/changes", StringComparison.Ordinal))
            {
                var records = _feed ?? Array.Empty<object>();
                _feed = null;
                return Task.FromResult(Json(new { changes = records, next = 100 + records.Length, more = false, resync = false }));
            }

            const string lookup = "/lookup/event/";
            var lookupAt = path.IndexOf(lookup, StringComparison.Ordinal);
            if (lookupAt >= 0)
            {
                var id = Uri.UnescapeDataString(path[(lookupAt + lookup.Length)..]);
                LookedUp.Add(id);
                if (_lookupFailures.TryGetValue(id, out var status))
                    return Task.FromResult(new HttpResponseMessage(status));
                if (_lookups.TryGetValue(id, out var found))
                    return Task.FromResult(Json(new { data = new[] { Wire(found.Event, found.Season) } }));
                // The real hub answers a missing event with an empty list.
                return Task.FromResult(Json(new { data = Array.Empty<object>() }));
            }

            var prefix = $"/schedule/league/{LeagueExternalId}/";
            var at = path.IndexOf(prefix, StringComparison.Ordinal);
            if (at >= 0)
            {
                var season = Uri.UnescapeDataString(path[(at + prefix.Length)..]);
                if (_failures.TryGetValue(season, out var failure))
                {
                    return failure switch
                    {
                        Failure.TooManyRequests => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)),
                        Failure.ServerError => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
                        Failure.Timeout => throw new TaskCanceledException("The request timed out"),
                        _ => throw new HttpRequestException("Connection reset"),
                    };
                }
                // The hub answers a season it does not know with an empty
                // schedule, not a 404.
                var events = _schedules.TryGetValue(season, out var served) ? served : Array.Empty<WireEvent>();
                return Task.FromResult(Json(new { data = new { schedule = events.Select(e => Wire(e, season)).ToArray() } }));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
