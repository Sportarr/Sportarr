using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A release picked by hand reports one release choice when its file
/// imports for the first time. The import never waits on the report and
/// never fails because of it.
/// </summary>
public class ImportReportsReleaseChoiceTests
{
    private const string Title = "FIFA.World.Cup.2020.09.01.Spain.vs.Belgium.720p.WEB-DL.H264-Exact";
    private const string Basename = Title + ".mkv";

    [Fact]
    public async Task ALowPickReportsOneChoiceAfterTheSave()
    {
        var recorder = new Recorder();
        await using var rig = await RigAsync(recorder);
        await SeedGrabAsync(rig, Title, GrabSource.InteractivePick, 30);

        await rig.ImportAsync(Title, Basename);

        var choice = Assert.Single(recorder.Choices);
        Assert.NotNull(choice);
        Assert.Equal("low", choice!.Verdict);
        Assert.Equal(30, choice.MatchScore);
        Assert.Equal(FileStemNormalizer.Normalize(Title), choice.TitleNorm);
        Assert.Equal("fifa world cup 2020 09 01 spain vs belgium", choice.TitleNorm);
        Assert.Equal(Title, choice.TitleDisplay);
        Assert.Equal("ev-0012345", choice.EventId);
        Assert.Equal("lg-000142", choice.LeagueId);
        Assert.Equal("newznab", choice.IndexerFamily);
        Assert.Equal("usenet", choice.Protocol);
        // The row was saved as imported before the report.
        Assert.True(recorder.SawImportedRow);
    }

    [Fact]
    public async Task TheSameFileImportedAgainReportsNothingMore()
    {
        var recorder = new Recorder();
        await using var rig = await RigAsync(recorder);
        await SeedGrabAsync(rig, Title, GrabSource.InteractivePick, 30);
        await rig.ImportAsync(Title, Basename);

        await ImportAgainAsync(rig, Title, Basename);

        Assert.Single(recorder.Choices);
    }

    [Theory]
    [InlineData(GrabSource.Automatic)]
    [InlineData(GrabSource.UserStartedAutomatic)]
    [InlineData(GrabSource.Rss)]
    [InlineData(GrabSource.Unknown)]
    [InlineData(GrabSource.SeasonSearch)]
    public async Task AGrabTheAppPickedReportsNothing(GrabSource source)
    {
        var recorder = new Recorder();
        await using var rig = await RigAsync(recorder);
        await SeedGrabAsync(rig, Title, source, 30);

        await rig.ImportAsync(Title, Basename);

        await AssertImportedButNotReportedAsync(rig, recorder);
    }

    [Fact]
    public async Task APackDownloadReportsNothing()
    {
        var recorder = new Recorder();
        await using var rig = await RigAsync(recorder);
        await SeedGrabAsync(rig, Title, GrabSource.InteractivePick, 30);

        await rig.ImportAsync(Title, Basename, isPack: true);

        await AssertImportedButNotReportedAsync(rig, recorder);
    }

    [Fact]
    public async Task AReleaseTheAppMatchedWellReportsNothing()
    {
        var recorder = new Recorder();
        await using var rig = await RigAsync(recorder);
        await SeedGrabAsync(rig, Title, GrabSource.InteractivePick, 70);

        await rig.ImportAsync(Title, Basename);

        await AssertImportedButNotReportedAsync(rig, recorder);
    }

    [Theory]
    [InlineData("FIFA.World.Cup.Spain.vs.Belgium.720p.WEB-DL.H264-NoYear")]
    [InlineData("FIFA World Cup 2020/21 Spain vs Belgium 720p WEB-DL H264-Slash")]
    [InlineData("[www.example.org] FIFA.World.Cup.2020.09.01.Spain.vs.Belgium.720p.WEB-DL.H264-Site")]
    [InlineData("FIFA.World.Cup.2020.09.01.Spain.vs.Belgium.720p.WEB-DL.H264-Id {sportarr-ev-312923}")]
    public async Task ATitleThatCouldNameTheSetupReportsNothing(string title)
    {
        var recorder = new Recorder();
        await using var rig = await RigAsync(recorder);
        await SeedGrabAsync(rig, title, GrabSource.InteractivePick, 30);

        await rig.ImportAsync(title, Basename);

        await AssertImportedButNotReportedAsync(rig, recorder);
    }

    [Fact]
    public async Task AnEventWithNoHubIdReportsNothing()
    {
        var recorder = new Recorder();
        await using var rig = await RigAsync(recorder);
        rig.Event.ExternalId = null;
        await rig.Db.SaveChangesAsync();
        await SeedGrabAsync(rig, Title, GrabSource.InteractivePick, 30);

        await rig.ImportAsync(Title, Basename);

        await AssertImportedButNotReportedAsync(rig, recorder);
    }

    [Fact]
    public async Task AFailedSaveReportsNothingAndTheImportStillFails()
    {
        var recorder = new Recorder();
        var failing = new FailImportedGrabSave();
        await using var rig = await RigAsync(recorder, failing);
        await SeedGrabAsync(rig, Title, GrabSource.InteractivePick, 30);
        failing.Armed = true;

        var outcome = await TryImportAsync(rig, Title, Basename);

        Assert.True(failing.Fired);
        Assert.True(outcome.Threw || outcome.History?.Decision != ImportDecision.Approved,
            $"decision {outcome.History?.Decision}");
        Assert.Empty(recorder.Choices);
    }

    [Fact]
    public async Task ARetryInTheSameContextAfterAFailedSaveStillReports()
    {
        var recorder = new Recorder();
        var failing = new FailImportedGrabSave();
        await using var rig = await RigAsync(recorder, failing);
        await SeedGrabAsync(rig, Title, GrabSource.InteractivePick, 30);
        failing.Armed = true;
        await TryImportAsync(rig, Title, Basename);
        Assert.True(failing.Fired);
        failing.Armed = false;

        var retry = await TryImportAsync(rig, Title, Basename);

        Assert.False(retry.Threw);
        Assert.Equal(ImportDecision.Approved, retry.History?.Decision);
        var choice = Assert.Single(recorder.Choices);
        Assert.Equal("low", choice!.Verdict);
    }

    [Fact]
    public async Task AThrowingReporterDoesNotChangeTheImport()
    {
        var recorder = new Recorder { Throw = true };
        await using var rig = await RigAsync(recorder);
        await SeedGrabAsync(rig, Title, GrabSource.InteractivePick, 30);

        var file = await rig.ImportAsync(Title, Basename);

        Assert.True(file.Exists);
        Assert.Equal(1, recorder.Calls);
        Assert.True((await rig.Db.GrabHistory.AsNoTracking().SingleAsync()).WasImported);
    }

    [Fact]
    public async Task AGrabByHandThenItsImportSendsOneChoice()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true,
            title: "Spain vs Belgium", sport: "Soccer", leagueName: "FIFA World Cup");
        await SetHubIdsAsync(rig);
        await ReleaseVerdictServiceTests.PrepareIndexerAsync(rig);
        ReleaseVerdictServiceTests.SetFeed(rig, (ReleaseVerdictServiceTests.SoccerLoose, rig.Event.EventDate.AddHours(4)));
        var row = await ReleaseVerdictServiceTests.SearchRowAsync(rig, ReleaseVerdictServiceTests.SoccerLoose);
        var release = row.Deserialize<ReleaseSearchResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await rig.GrabAsync(release, "manualSearch");
        var queued = await rig.Db.DownloadQueue.SingleAsync();
        queued.Status = DownloadStatus.Completed;
        await rig.Db.SaveChangesAsync();

        await rig.ImportAsync(ReleaseVerdictServiceTests.SoccerLoose, ReleaseVerdictServiceTests.SoccerLoose + ".mkv",
            acquiredQueue: queued);

        var body = await SingleSubmitAsync(rig);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(new[] { "appVersion", "choices", "installId" },
            root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        var choice = Assert.Single(root.GetProperty("choices").EnumerateArray());
        Assert.Equal(new[] { "eventId", "hintShown", "indexerFamily", "kind", "leagueId", "matchScore", "protocol",
                "season", "titleDisplay", "titleNorm", "verdict" },
            choice.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal("low", choice.GetProperty("verdict").GetString());
        Assert.Equal(ReleaseVerdictServiceTests.SoccerLoose, choice.GetProperty("titleDisplay").GetString());
        Assert.Equal(row.GetProperty("matchScore").GetInt32(), choice.GetProperty("matchScore").GetInt32());
        Assert.Equal("newznab", choice.GetProperty("indexerFamily").GetString());
    }

    private static async Task<string> SingleSubmitAsync(PartIdentityIntegrationHarness rig)
    {
        // The send is detached from the import. Wait a bounded time for it.
        for (var i = 0; i < 100 && rig.Transport.ReleaseChoiceBodies.IsEmpty; i++)
            await Task.Delay(100);
        var body = Assert.Single(rig.Transport.ReleaseChoiceBodies);
        // A second send would come from the same detached path.
        await Task.Delay(300);
        Assert.Single(rig.Transport.ReleaseChoiceBodies);
        return body;
    }

    private static async Task<PartIdentityIntegrationHarness> RigAsync(Recorder recorder, IInterceptor? interceptor = null)
    {
        var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true,
            title: "Spain vs Belgium", sport: "Soccer", leagueName: "FIFA World Cup",
            interceptors: interceptor == null ? null : new[] { interceptor },
            configureServices: services => services.AddSingleton<ReleaseChoiceReporter>(sp =>
            {
                recorder.Contexts = sp.GetRequiredService<IDbContextFactory<Sportarr.Api.Data.SportarrDbContext>>();
                return recorder;
            }));
        await SetHubIdsAsync(rig);
        return rig;
    }

    private static async Task SetHubIdsAsync(PartIdentityIntegrationHarness rig)
    {
        rig.Event.ExternalId = "ev-0012345";
        rig.Event.League!.ExternalId = "lg-000142";
        await rig.Db.SaveChangesAsync();
    }

    private static async Task SeedGrabAsync(PartIdentityIntegrationHarness rig, string title, GrabSource source, int? score)
    {
        var indexer = await rig.Db.Indexers.SingleAsync();
        rig.Db.GrabHistory.Add(new GrabHistory
        {
            EventId = rig.Event.Id, Title = title, Indexer = indexer.Name, IndexerId = indexer.Id,
            DownloadUrl = "http://part-source.invalid/seed.nzb", Guid = "seed", Protocol = "Usenet",
            GrabSource = source, MatchScoreAtGrab = score, GrabbedAt = DateTime.UtcNow.AddHours(-1),
        });
        await rig.Db.SaveChangesAsync();
    }

    private static async Task AssertImportedButNotReportedAsync(PartIdentityIntegrationHarness rig, Recorder recorder)
    {
        // The import reached the grab row, so it passed the hook.
        Assert.True((await rig.Db.GrabHistory.AsNoTracking().SingleAsync()).WasImported);
        Assert.Equal(0, recorder.Calls);
    }

    private static async Task ImportAgainAsync(PartIdentityIntegrationHarness rig, string title, string basename)
    {
        await TryImportAsync(rig, title, basename);
    }

    private static async Task<(bool Threw, ImportHistory? History)> TryImportAsync(
        PartIdentityIntegrationHarness rig, string title, string basename)
    {
        var folder = Path.Combine(Path.GetTempPath(), "sportarr-release-choice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, basename);
            await File.WriteAllBytesAsync(source, Encoding.UTF8.GetBytes(new string('y', 4096)));
            var row = new DownloadQueueItem { EventId = rig.Event.Id, Title = title, DownloadId = Guid.NewGuid().ToString("N"),
                Quality = "WEBDL-720p", Protocol = "Usenet", Status = DownloadStatus.Completed };
            rig.Db.DownloadQueue.Add(row);
            await rig.Db.SaveChangesAsync();
            try
            {
                var history = await rig.Services.GetRequiredService<FileImportService>()
                    .ImportDownloadAsync(row, source, PostImportMode.Copy);
                return (false, history);
            }
            catch (Exception)
            {
                return (true, null);
            }
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>Records each report. It holds no import state.</summary>
    private sealed class Recorder : ReleaseChoiceReporter
    {
        public Recorder() : base(null!, null!, NullLogger<ReleaseChoiceReporter>.Instance) { }

        public IDbContextFactory<Sportarr.Api.Data.SportarrDbContext>? Contexts { get; set; }
        public bool Throw { get; init; }
        public int Calls { get; private set; }
        public bool SawImportedRow { get; private set; }
        public List<ReleaseChoice?> Choices { get; } = new();

        public override Task Report(ReleaseChoice? choice)
        {
            Calls++;
            Choices.Add(choice);
            if (choice != null && Contexts != null)
            {
                // A fresh context sees only saved rows.
                using var fresh = Contexts.CreateDbContext();
                SawImportedRow = fresh.GrabHistory.Any(g => g.WasImported);
            }
            if (Throw)
                throw new InvalidOperationException("The reporter test forces a failure.");
            return Task.CompletedTask;
        }
    }

    /// <summary>Fails the save that marks a grab row imported.</summary>
    private sealed class FailImportedGrabSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<GrabHistory>()
                    .Any(e => e.State == EntityState.Modified && e.Entity.WasImported))
            {
                Fired = true;
                throw new InvalidOperationException("The import test forces the save to fail.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
