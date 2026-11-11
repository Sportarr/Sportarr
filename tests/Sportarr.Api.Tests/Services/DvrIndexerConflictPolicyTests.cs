using System.Net;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A recording that finishes for a part a downloaded file already holds.
/// Covers source precedence, the KeepBetter policy, ties and recording
/// retention of open choices (issue 381).
/// </summary>
[Collection(DvrChoiceStateCollection.Name)]
public class DvrIndexerConflictPolicyTests : IDisposable
{
    private readonly List<string> _directories = new();

    public void Dispose()
    {
        foreach (var directory in _directories.Where(Directory.Exists))
            Directory.Delete(directory, true);
    }

    [Fact]
    public async Task SourcePrecedenceIgnoresTheRecordingWithoutAChoice()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await ConfigureAsync(rig, config => config.DvrReplaceRecordingsWithIndexerReleases = true);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-2160p");

        (await CreateService(rig).ImportCompletedRecordingAsync(recording.Id)).Should().BeFalse();

        (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
        recording.Status.Should().Be(DvrRecordingStatus.Ignored);
        recording.ErrorMessage.Should().Be(SourcePrecedence.IptvRecordingRejection);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task SourcePrecedenceStillAsksWhenTheDownloadedFileIsMissing()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await ConfigureAsync(rig, config => config.DvrReplaceRecordingsWithIndexerReleases = true);
        var (recording, _, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p");
        File.Delete(downloadPath);

        (await CreateService(rig).ImportCompletedRecordingAsync(recording.Id)).Should().BeFalse();

        var choice = await rig.Db.PendingImports.SingleAsync();
        choice.DownloadId.Should().Be($"dvr-{recording.Id}");
        choice.ErrorMessage.Should().Contain("missing from disk");
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
    }

    [Fact]
    public async Task AskPolicyIsTheDefaultAndOpensAChoice()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var config = await rig.Services.GetRequiredService<ConfigService>().GetConfigAsync();
        config.DvrIndexerConflictPolicy.Should().Be(DvrIndexerChoice.AskPolicy);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-2160p");

        (await CreateService(rig).ImportCompletedRecordingAsync(recording.Id)).Should().BeFalse();

        (await rig.Db.PendingImports.SingleAsync()).ErrorMessage.Should().Contain("Choose which file to keep");
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task KeepBetterImportsABetterRecordingAndRecyclesTheDownload()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var recycleBin = Path.Combine(Path.GetTempPath(), "sportarr-dvr381-recycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(recycleBin);
        try
        {
            await ConfigureAsync(rig, config =>
            {
                config.DvrIndexerConflictPolicy = DvrIndexerChoice.KeepBetterPolicy;
                config.RecycleBin = recycleBin;
            });
            var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-2160p");

            (await CreateService(rig).ImportCompletedRecordingAsync(recording.Id)).Should().BeTrue();

            (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
            recording.Status.Should().Be(DvrRecordingStatus.Imported);
            var held = await rig.Db.EventFiles.SingleAsync(file => file.Exists);
            held.IsIptvRecording.Should().BeTrue();
            File.Exists(held.FilePath).Should().BeTrue();
            File.Exists(recordingPath).Should().BeFalse();
            File.Exists(downloadPath).Should().BeFalse();
            Directory.GetFiles(recycleBin).Should().ContainSingle(path =>
                path.EndsWith("_" + Path.GetFileName(downloadPath), StringComparison.Ordinal));
        }
        finally { Directory.Delete(recycleBin, true); }
    }

    [Theory]
    [InlineData("WEBDL-720p")]
    [InlineData("WEBDL-1080p")]
    public async Task KeepBetterKeepsTheDownloadOverAWorseOrEqualRecording(string recordingQuality)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await ConfigureAsync(rig, config => config.DvrIndexerConflictPolicy = DvrIndexerChoice.KeepBetterPolicy);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, recordingQuality);

        (await CreateService(rig).ImportCompletedRecordingAsync(recording.Id)).Should().BeFalse();

        (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
        recording.Status.Should().Be(DvrRecordingStatus.Ignored);
        recording.ErrorMessage.Should().NotBeNullOrEmpty();
        (await rig.Db.EventFiles.SingleAsync()).FilePath.Should().Be(downloadPath);
        File.Exists(downloadPath).Should().BeTrue();
        File.Exists(recordingPath).Should().BeTrue();
    }

    [Fact]
    public async Task KeepBetterAsksWhenTheDownloadedFileIsMissing()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await ConfigureAsync(rig, config => config.DvrIndexerConflictPolicy = DvrIndexerChoice.KeepBetterPolicy);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p");
        File.Delete(downloadPath);

        (await CreateService(rig).ImportCompletedRecordingAsync(recording.Id)).Should().BeFalse();

        (await rig.Db.PendingImports.SingleAsync()).ErrorMessage.Should().Contain("missing from disk");
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
    }

    [Fact]
    public async Task AnEqualRecordingKeepsALaterDownload()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-1080p",
            downloadedAfterRecording: true);

        (await CreateService(rig).ImportCompletedRecordingAsync(recording.Id)).Should().BeFalse();

        (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
        recording.Status.Should().Be(DvrRecordingStatus.Ignored);
        recording.ErrorMessage.Should().Be(LibraryImportService.EqualCopyRejection);
        (await rig.Db.EventFiles.SingleAsync()).FilePath.Should().Be(downloadPath);
        File.Exists(downloadPath).Should().BeTrue();
        File.Exists(recordingPath).Should().BeTrue();
    }

    [Fact]
    public async Task ABetterRecordingStillReplacesALaterDownload()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-2160p",
            downloadedAfterRecording: true);

        (await CreateService(rig).ImportCompletedRecordingAsync(recording.Id)).Should().BeTrue();

        recording.Status.Should().Be(DvrRecordingStatus.Imported);
        File.Exists(recordingPath).Should().BeFalse();
        File.Exists(downloadPath).Should().BeFalse();
        (await rig.Db.EventFiles.SingleAsync(file => file.Exists)).IsIptvRecording.Should().BeTrue();
    }

    [Fact]
    public async Task RejectEndpointKeepsTheDownloadThroughTheSharedHelper()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p");
        var choice = await AddChoiceAsync(rig, recording);

        await using var app = await PartIdentityPendingContractV2Tests.CreatePendingHost(rig);
        using var response = await app.GetTestClient().PostAsync(
            $"/api/pending-imports/{choice.Id}/reject?deleteFile=false", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        recording.Status.Should().Be(DvrRecordingStatus.Ignored);
        (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
        (await rig.Db.Blocklist.SingleAsync()).FilePath.Should().Be(recordingPath);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task RejectEndpointRefusesAChoiceThatIsBeingImported()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, _) = await AddConflictAsync(rig, "WEBDL-720p");
        var choice = await AddChoiceAsync(rig, recording);
        choice.Status = PendingImportStatus.Importing;
        await rig.Db.SaveChangesAsync();

        await using var app = await PartIdentityPendingContractV2Tests.CreatePendingHost(rig);
        using (DvrIndexerChoice.TryStartImport(choice.Id))
        {
            using var response = await app.GetTestClient().PostAsync(
                $"/api/pending-imports/{choice.Id}/reject?deleteFile=false", null);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            recording.Status.Should().Be(DvrRecordingStatus.Completed);
            (await rig.Db.PendingImports.CountAsync()).Should().Be(1);
            File.Exists(recordingPath).Should().BeTrue();
        }
    }

    [Fact]
    public async Task RejectEndpointResolvesAChoiceLeftImportingByAStop()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p");
        var choice = await AddChoiceAsync(rig, recording);
        choice.Status = PendingImportStatus.Importing;
        await rig.Db.SaveChangesAsync();

        await using var app = await PartIdentityPendingContractV2Tests.CreatePendingHost(rig);
        using var response = await app.GetTestClient().PostAsync(
            $"/api/pending-imports/{choice.Id}/reject?deleteFile=false", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        recording.Status.Should().Be(DvrRecordingStatus.Ignored);
        (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task StartupReopensAChoiceLeftImporting()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, _, _) = await AddConflictAsync(rig, "WEBDL-720p");
        var stuck = await AddChoiceAsync(rig, recording);
        stuck.Status = PendingImportStatus.Importing;
        await rig.Db.SaveChangesAsync();

        (await DvrIndexerChoice.ReopenInterruptedChoicesAsync(rig.Db)).Should().Be(1);

        stuck.Status.Should().Be(PendingImportStatus.Pending);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectEndpointRefusesAResolvedImport(bool dvrChoice)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p");
        var choice = await AddChoiceAsync(rig, recording);
        if (!dvrChoice)
            choice.DownloadId = "disk-" + Guid.NewGuid().ToString("N");
        choice.Status = PendingImportStatus.Completed;
        await rig.Db.SaveChangesAsync();

        await using var app = await PartIdentityPendingContractV2Tests.CreatePendingHost(rig);
        using var response = await app.GetTestClient().PostAsync(
            $"/api/pending-imports/{choice.Id}/reject", null);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await rig.Db.Blocklist.CountAsync()).Should().Be(0);
        (await rig.Db.PendingImports.CountAsync()).Should().Be(1);
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionResolvesAnExpiredChoiceAndDeletesTheRecording()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p", endedDaysAgo: 3);
        await AddChoiceAsync(rig, recording);

        await PruneAsync(rig, new Config { DvrRecordingRetentionDays = 1 });

        (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
        (await rig.Db.DvrRecordings.CountAsync()).Should().Be(0);
        (await rig.Db.Blocklist.CountAsync()).Should().Be(0);
        File.Exists(recordingPath).Should().BeFalse();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionByKeepLastResolvesAnOverflowChoice()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p", endedDaysAgo: 3);
        await AddChoiceAsync(rig, recording);
        var newerPath = Path.Combine(DirectoryOf(downloadPath), "newer.DVR.ts");
        await File.WriteAllBytesAsync(newerPath, new byte[16]);
        rig.Db.DvrRecordings.Add(new DvrRecording
        {
            EventId = rig.Event.Id, Channel = recording.Channel, Title = "Newer",
            Status = DvrRecordingStatus.Ignored, OutputPath = newerPath,
            ScheduledStart = DateTime.UtcNow.AddHours(-2), ScheduledEnd = DateTime.UtcNow.AddHours(-1)
        });
        await rig.Db.SaveChangesAsync();

        await PruneAsync(rig, new Config { DvrKeepLastRecordingsPerLeague = 1 });

        (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
        (await rig.Db.DvrRecordings.SingleAsync()).Title.Should().Be("Newer");
        File.Exists(recordingPath).Should().BeFalse();
        File.Exists(newerPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionKeepsAnExpiredChoiceWhenTheDownloadedFileIsMissing()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p", endedDaysAgo: 3);
        await AddChoiceAsync(rig, recording);
        File.Delete(downloadPath);

        await PruneAsync(rig, new Config { DvrRecordingRetentionDays = 1 });

        (await rig.Db.PendingImports.CountAsync()).Should().Be(1);
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        (await rig.Db.DvrRecordings.CountAsync()).Should().Be(1);
        File.Exists(recordingPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionKeepsAChoiceThatIsNotPastTheCutoff()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, _) = await AddConflictAsync(rig, "WEBDL-720p");
        await AddChoiceAsync(rig, recording);

        await PruneAsync(rig, new Config { DvrRecordingRetentionDays = 1 });

        (await rig.Db.PendingImports.CountAsync()).Should().Be(1);
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionKeepsAChoiceThatIsBeingImported()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, _) = await AddConflictAsync(rig, "WEBDL-720p", endedDaysAgo: 3);
        var choice = await AddChoiceAsync(rig, recording);
        choice.Status = PendingImportStatus.Importing;
        await rig.Db.SaveChangesAsync();

        using (DvrIndexerChoice.TryStartImport(choice.Id))
            await PruneAsync(rig, new Config { DvrRecordingRetentionDays = 1 });

        (await rig.Db.PendingImports.CountAsync()).Should().Be(1);
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionResolvesAChoiceLeftImportingByAStop()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p", endedDaysAgo: 3);
        var choice = await AddChoiceAsync(rig, recording);
        choice.Status = PendingImportStatus.Importing;
        await rig.Db.SaveChangesAsync();

        await PruneAsync(rig, new Config { DvrRecordingRetentionDays = 1 });

        (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
        (await rig.Db.DvrRecordings.CountAsync()).Should().Be(0);
        File.Exists(recordingPath).Should().BeFalse();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionKeepsAnIgnoredRecordingWhenTheDownloadIsGone()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p", endedDaysAgo: 3);
        recording.Status = DvrRecordingStatus.Ignored;
        recording.ErrorMessage = SourcePrecedence.IptvRecordingRejection;
        await rig.Db.SaveChangesAsync();
        File.Delete(downloadPath);

        await PruneAsync(rig, new Config { DvrRecordingRetentionDays = 1 });

        (await rig.Db.DvrRecordings.SingleAsync()).Id.Should().Be(recording.Id);
        File.Exists(recordingPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionPrunesAnIgnoredRecordingWhenTheDownloadRowIsRemoved()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p", endedDaysAgo: 3);
        recording.Status = DvrRecordingStatus.Ignored;
        rig.Db.EventFiles.Remove(await rig.Db.EventFiles.SingleAsync());
        await rig.Db.SaveChangesAsync();
        File.Delete(downloadPath);

        await PruneAsync(rig, new Config { DvrRecordingRetentionDays = 1 });

        (await rig.Db.DvrRecordings.CountAsync()).Should().Be(0);
        File.Exists(recordingPath).Should().BeFalse();
    }

    [Fact]
    public async Task KeepBetterOpensAChoiceWhenTheImportFails()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await ConfigureAsync(rig, config => config.DvrIndexerConflictPolicy = DvrIndexerChoice.KeepBetterPolicy);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-2160p");
        // A file where the library folder should be makes the transfer fail.
        var root = (await rig.Db.RootFolders.SingleAsync()).Path;
        Directory.Delete(root, true);
        await File.WriteAllBytesAsync(root, new byte[1]);

        (await CreateService(rig).ImportCompletedRecordingAsync(recording.Id)).Should().BeFalse();

        (await rig.Db.PendingImports.SingleAsync()).DownloadId.Should().Be($"dvr-{recording.Id}");
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task AcceptRefusesASecondAcceptOfTheSameChoice()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p");
        var choice = await AddChoiceAsync(rig, recording);

        await using var app = await PartIdentityPendingContractV2Tests.CreatePendingHost(rig);
        using (DvrIndexerChoice.TryStartImport(choice.Id))
        {
            using var response = await app.GetTestClient().PostAsync($"/api/pending-imports/{choice.Id}/accept", null);
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        choice.Status.Should().Be(PendingImportStatus.Pending);
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task AcceptAnswersNotFoundWhenRetentionResolvedTheChoiceFirst()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p");
        var choice = await AddChoiceAsync(rig, recording);
        await using var app = await PartIdentityPendingContractV2Tests.CreatePendingHost(rig);
        // The endpoint uses rig.Db. This thread uses its own context on the same store.
        var factory = rig.Services.GetRequiredService<IDbContextFactory<SportarrDbContext>>();
        await using var retentionDb = await factory.CreateDbContextAsync();

        Task<HttpResponseMessage> accept;
        await DvrIndexerChoice.EnterGateAsync();
        try
        {
            accept = app.GetTestClient().PostAsync($"/api/pending-imports/{choice.Id}/accept", null);
            // The accept has loaded the row and now waits for the gate.
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DvrIndexerChoice.GateWaiters == 0)
            {
                DateTime.UtcNow.Should().BeBefore(deadline, "the accept must reach the gate");
                await Task.Delay(10);
            }
            var row = await retentionDb.PendingImports.SingleAsync(p => p.Id == choice.Id);
            (await DvrIndexerChoice.KeepDownloadAsync(retentionDb, row, blocklist: false))
                .Should().Be(DvrIndexerChoice.Outcome.Resolved);
        }
        finally
        {
            DvrIndexerChoice.Gate.Release();
        }

        using var response = await accept;
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        DvrIndexerChoice.IsImporting(choice.Id).Should().BeFalse();
        await using var checkDb = await factory.CreateDbContextAsync();
        (await checkDb.DvrRecordings.SingleAsync(r => r.Id == recording.Id)).Status
            .Should().Be(DvrRecordingStatus.Ignored);
        (await checkDb.PendingImports.CountAsync()).Should().Be(0);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task AcceptRefusesAResolvedChoice()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p");
        var choice = await AddChoiceAsync(rig, recording);
        choice.Status = PendingImportStatus.Completed;
        await rig.Db.SaveChangesAsync();

        await using var app = await PartIdentityPendingContractV2Tests.CreatePendingHost(rig);
        using var response = await app.GetTestClient().PostAsync($"/api/pending-imports/{choice.Id}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        choice.Status.Should().Be(PendingImportStatus.Completed);
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionSkipsAClaimedRecording()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, _) = await AddConflictAsync(rig, "WEBDL-720p", endedDaysAgo: 3);
        recording.Status = DvrRecordingStatus.Ignored;
        await rig.Db.SaveChangesAsync();

        using (DvrIndexerChoice.TryClaimRecording(recording.Id))
            await PruneAsync(rig, new Config { DvrRecordingRetentionDays = 1 });

        (await rig.Db.DvrRecordings.SingleAsync()).Id.Should().Be(recording.Id);
        File.Exists(recordingPath).Should().BeTrue();
        using var after = DvrIndexerChoice.TryClaimRecording(recording.Id);
        after.Should().NotBeNull("retention releases its own claims");
    }

    [Fact]
    public async Task SweepLeavesAClaimedRecordingAlone()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p");

        using (DvrIndexerChoice.TryClaimRecording(recording.Id))
            (await CreateService(rig).ImportCompletedRecordingAsync(recording.Id)).Should().BeFalse();

        (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task ASecondSweepDuringAKeepBetterImportOpensNoChoice()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        await ConfigureAsync(rig, config => config.DvrIndexerConflictPolicy = DvrIndexerChoice.KeepBetterPolicy);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-2160p");
        // The first sweep holds the claim and has staged the download aside.
        var staged = downloadPath + ".staged";
        File.Move(downloadPath, staged);

        using (DvrIndexerChoice.TryClaimRecording(recording.Id))
            (await CreateService(rig).ImportCompletedRecordingAsync(recording.Id)).Should().BeFalse();

        (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(staged).Should().BeTrue();
    }

    [Fact]
    public async Task KeepDownloadIsBusyWhenAnAcceptFinishesDuringTheRowRead()
    {
        var interceptor = new AcceptFinishesOnRowRead();
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false,
            interceptors: [interceptor]);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p");
        var choice = await AddChoiceAsync(rig, recording);
        // An accept runs. It finishes and drops its claim while the row is read.
        var acceptClaim = DvrIndexerChoice.TryStartImport(choice.Id)!;
        interceptor.Arm(acceptClaim.Dispose);

        DvrIndexerChoice.Outcome outcome;
        await DvrIndexerChoice.EnterGateAsync();
        try
        {
            outcome = await DvrIndexerChoice.KeepDownloadAsync(rig.Db, choice, blocklist: false);
        }
        finally
        {
            DvrIndexerChoice.Gate.Release();
            acceptClaim.Dispose();
        }

        outcome.Should().Be(DvrIndexerChoice.Outcome.Busy);
        (await rig.Db.PendingImports.CountAsync()).Should().Be(1);
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionDeletesNothingWhenAnAcceptFinishesDuringTheRowRead()
    {
        var interceptor = new AcceptFinishesOnRowRead();
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false,
            interceptors: [interceptor]);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p", endedDaysAgo: 3);
        var choice = await AddChoiceAsync(rig, recording);
        var acceptClaim = DvrIndexerChoice.TryStartImport(choice.Id)!;
        interceptor.Arm(acceptClaim.Dispose);

        try
        {
            await PruneAsync(rig, new Config { DvrRecordingRetentionDays = 1 });
        }
        finally
        {
            acceptClaim.Dispose();
        }

        (await rig.Db.PendingImports.CountAsync()).Should().Be(1);
        (await rig.Db.DvrRecordings.CountAsync()).Should().Be(1);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task ManualImportAnswersConflictForAClaimedRecording()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, _) = await AddConflictAsync(rig, "WEBDL-720p");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(rig.Services.GetRequiredService<ConfigService>());
        builder.Services.AddValidatorsFromAssemblyContaining<ScheduleDvrRecordingRequestValidator>();
        builder.Services.AddSingleton(CreateService(rig));
        foreach (var type in new[]
        {
            typeof(DvrRecordingService), typeof(DvrQualityScoreCalculator), typeof(SportarrDbContext),
            typeof(IptvOrgSyncService), typeof(EventChannelResolverService), typeof(DvrAutoSchedulerService),
            typeof(DvrChannelReresolveService), typeof(FFmpegRecorderService), typeof(DiskSpaceService)
        })
        {
            builder.Services.AddScoped(type, _ => throw new InvalidOperationException("The import must not resolve unrelated services."));
        }
        await using var app = builder.Build();
        app.MapDvrEndpoints();
        await app.StartAsync();

        using (DvrIndexerChoice.TryClaimRecording(recording.Id))
        {
            using var response = await app.GetTestClient().PostAsync($"/api/dvr/recordings/{recording.Id}/import", null);
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionPrunesARecordingWhenAReplacementIsOnDisk()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p", endedDaysAgo: 3);
        recording.Status = DvrRecordingStatus.Ignored;
        // The old download is gone. A replacement under another name holds the part.
        File.Delete(downloadPath);
        var replacement = Path.Combine(Path.GetDirectoryName(downloadPath)!, "Fixture.Replacement.mkv");
        await File.WriteAllBytesAsync(replacement, new byte[16]);
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id, FilePath = replacement, Quality = "WEBDL-1080p", Exists = true
        });
        await rig.Db.SaveChangesAsync();

        await PruneAsync(rig, new Config { DvrRecordingRetentionDays = 1 });

        (await rig.Db.DvrRecordings.CountAsync()).Should().Be(0);
        File.Exists(recordingPath).Should().BeFalse();
        File.Exists(replacement).Should().BeTrue();
    }

    [Fact]
    public async Task AcceptReleasesItsClaimAfterASuccessfulImport()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, _, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p");
        var choice = await AddChoiceAsync(rig, recording);

        await using var app = await PartIdentityPendingContractV2Tests.CreatePendingHost(rig);
        using var response = await app.GetTestClient().PostAsync($"/api/pending-imports/{choice.Id}/accept", null);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        DvrIndexerChoice.IsImporting(choice.Id).Should().BeFalse();
        recording.Status.Should().Be(DvrRecordingStatus.Imported);
        File.Exists(downloadPath).Should().BeFalse();
    }

    [Fact]
    public async Task AcceptReleasesItsClaimAfterAFailedImport()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p");
        var choice = await AddChoiceAsync(rig, recording);
        // The choice names a part the recording does not cover, so the accept fails.
        choice.SuggestedPart = "Prelims";
        await rig.Db.SaveChangesAsync();

        await using var app = await PartIdentityPendingContractV2Tests.CreatePendingHost(rig);
        using var response = await app.GetTestClient().PostAsync($"/api/pending-imports/{choice.Id}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        DvrIndexerChoice.IsImporting(choice.Id).Should().BeFalse();
        choice.Status.Should().Be(PendingImportStatus.Pending);
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionPrunesAnIgnoredRecordingWhileTheDownloadIsOnDisk()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-720p", endedDaysAgo: 3);
        recording.Status = DvrRecordingStatus.Ignored;
        await rig.Db.SaveChangesAsync();

        await PruneAsync(rig, new Config { DvrRecordingRetentionDays = 1 });

        (await rig.Db.DvrRecordings.CountAsync()).Should().Be(0);
        File.Exists(recordingPath).Should().BeFalse();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task AnEqualRecordingWithAnImportModeSettlesAgainstALaterDownload()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-1080p",
            downloadedAfterRecording: true);
        recording.ImportMode = "move";
        await rig.Db.SaveChangesAsync();
        var service = CreateService(rig);

        (await service.ImportCompletedRecordingAsync(recording.Id)).Should().BeFalse();

        recording.Status.Should().Be(DvrRecordingStatus.Ignored);
        recording.ErrorMessage.Should().Be(LibraryImportService.EqualCopyRejection);
        (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
        (await rig.Db.EventFiles.SingleAsync()).FilePath.Should().Be(downloadPath);
        File.Exists(recordingPath).Should().BeTrue();
    }

    [Fact]
    public async Task KeepBetterAsksWhenNoHeldFileMatchesThePartNumber()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true);
        await ConfigureAsync(rig, config => config.DvrIndexerConflictPolicy = DvrIndexerChoice.KeepBetterPolicy);
        var (recording, recordingPath, downloadPath) = await AddConflictAsync(rig, "WEBDL-2160p");
        // An older row names the part but has no part number.
        var download = await rig.Db.EventFiles.SingleAsync();
        download.PartName = "Prelims";
        download.PartNumber = null;
        recording.PartName = "Prelims";
        await rig.Db.SaveChangesAsync();

        (await CreateService(rig).ImportCompletedRecordingAsync(recording.Id)).Should().BeFalse();

        (await rig.Db.PendingImports.SingleAsync()).DownloadId.Should().Be($"dvr-{recording.Id}");
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
        (await rig.Db.EventFiles.SingleAsync()).FilePath.Should().Be(downloadPath);
        File.Exists(recordingPath).Should().BeTrue();
        File.Exists(downloadPath).Should().BeTrue();
    }

    [Fact]
    public async Task RetentionResolvesEachPartOnItsOwn()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true);
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-dvr381-parts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var prelimsDownload = Path.Combine(directory, "download.Prelims.1080p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(prelimsDownload, new byte[16]);
            var ended = DateTime.UtcNow.AddDays(-3);
            rig.Db.EventFiles.AddRange(
                new EventFile
                {
                    EventId = rig.Event.Id, FilePath = prelimsDownload, Quality = "WEBDL-1080p",
                    PartName = "Prelims", Exists = true, Added = ended.AddMinutes(-10)
                },
                new EventFile
                {
                    EventId = rig.Event.Id, FilePath = Path.Combine(directory, "download.Main.Card.mkv"),
                    Quality = "WEBDL-1080p", PartName = "Main Card", Exists = false, Added = ended.AddMinutes(-10)
                });
            var channel = NewChannel();
            var prelims = NewRecording(rig, channel, Path.Combine(directory, "prelims.DVR.ts"), "WEBDL-720p", ended, "Prelims");
            var mainCard = NewRecording(rig, channel, Path.Combine(directory, "main.DVR.ts"), "WEBDL-720p", ended, "Main Card");
            await File.WriteAllBytesAsync(prelims.OutputPath!, new byte[16]);
            await File.WriteAllBytesAsync(mainCard.OutputPath!, new byte[16]);
            rig.Db.DvrRecordings.AddRange(prelims, mainCard);
            await rig.Db.SaveChangesAsync();
            await AddChoiceAsync(rig, prelims);
            var mainChoice = await AddChoiceAsync(rig, mainCard);

            await PruneAsync(rig, new Config { DvrRecordingRetentionDays = 1 });

            (await rig.Db.PendingImports.SingleAsync()).Id.Should().Be(mainChoice.Id);
            (await rig.Db.DvrRecordings.SingleAsync()).Id.Should().Be(mainCard.Id);
            mainCard.Status.Should().Be(DvrRecordingStatus.Completed);
            File.Exists(prelims.OutputPath!).Should().BeFalse();
            File.Exists(mainCard.OutputPath!).Should().BeTrue();
            File.Exists(prelimsDownload).Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SourcePrecedenceIgnoresOnlyThePartWithADownload()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: true);
        await ConfigureAsync(rig, config => config.DvrReplaceRecordingsWithIndexerReleases = true);
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-dvr381-parts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var ended = DateTime.UtcNow.AddHours(-1);
            var prelimsDownload = Path.Combine(directory, "download.Prelims.1080p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(prelimsDownload, new byte[16]);
            rig.Db.EventFiles.AddRange(
                new EventFile
                {
                    EventId = rig.Event.Id, FilePath = prelimsDownload, Quality = "WEBDL-1080p",
                    PartName = "Prelims", Exists = true, Added = ended.AddMinutes(-10)
                },
                new EventFile
                {
                    EventId = rig.Event.Id, FilePath = Path.Combine(directory, "download.Main.Card.mkv"),
                    Quality = "WEBDL-1080p", PartName = "Main Card", Exists = false, Added = ended.AddMinutes(-10)
                });
            var channel = NewChannel();
            var prelims = NewRecording(rig, channel, Path.Combine(directory, "prelims.DVR.ts"), "WEBDL-720p", ended, "Prelims");
            var mainCard = NewRecording(rig, channel, Path.Combine(directory, "main.DVR.ts"), "WEBDL-720p", ended, "Main Card");
            await File.WriteAllBytesAsync(prelims.OutputPath!, new byte[16]);
            await File.WriteAllBytesAsync(mainCard.OutputPath!, new byte[16]);
            rig.Db.DvrRecordings.AddRange(prelims, mainCard);
            await rig.Db.SaveChangesAsync();
            var service = CreateService(rig);

            (await service.ImportCompletedRecordingAsync(prelims.Id)).Should().BeFalse();
            (await service.ImportCompletedRecordingAsync(mainCard.Id)).Should().BeFalse();

            prelims.Status.Should().Be(DvrRecordingStatus.Ignored);
            mainCard.Status.Should().Be(DvrRecordingStatus.Completed);
            (await rig.Db.PendingImports.SingleAsync()).DownloadId.Should().Be($"dvr-{mainCard.Id}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task ConfigureAsync(PartIdentityIntegrationHarness rig, Action<Config> change)
    {
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        change(config);
        await configService.SaveConfigAsync(config);
    }

    private async Task<(DvrRecording Recording, string RecordingPath, string DownloadPath)> AddConflictAsync(
        PartIdentityIntegrationHarness rig, string recordingQuality, bool downloadedAfterRecording = false,
        int endedDaysAgo = 0)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-dvr381-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _directories.Add(directory);
        // The DVR folder and the download sit apart, as they do in a real library.
        Directory.CreateDirectory(Path.Combine(directory, "dvr"));
        Directory.CreateDirectory(Path.Combine(directory, "download"));
        var recordingPath = Path.Combine(directory, "dvr", "Fixture.DVR.ts");
        var downloadPath = Path.Combine(directory, "download", "Fixture.WEBDL-1080p.mkv");
        await File.WriteAllBytesAsync(recordingPath, Enumerable.Repeat((byte)'r', 4096).ToArray());
        await File.WriteAllBytesAsync(downloadPath, Enumerable.Repeat((byte)'d', 4096).ToArray());
        var ended = endedDaysAgo > 0 ? DateTime.UtcNow.AddDays(-endedDaysAgo) : DateTime.UtcNow.AddHours(-1);
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id, FilePath = downloadPath, Quality = "WEBDL-1080p", Exists = true,
            Size = 4096, Added = downloadedAfterRecording ? ended.AddMinutes(5) : ended.AddMinutes(-10)
        });
        var recording = NewRecording(rig, NewChannel(), recordingPath, recordingQuality, ended, partName: null);
        rig.Db.DvrRecordings.Add(recording);
        await rig.Db.SaveChangesAsync();
        return (recording, recordingPath, downloadPath);
    }

    private static IptvChannel NewChannel() => new()
    {
        Source = new IptvSource { Name = "Fixture", Url = "https://iptv.invalid/list" },
        Name = "Fixture", StreamUrl = "https://iptv.invalid/stream"
    };

    private static DvrRecording NewRecording(PartIdentityIntegrationHarness rig, IptvChannel channel, string path,
        string quality, DateTime ended, string? partName) => new()
    {
        // The claim set is shared by all tests, so the id must not repeat.
        Id = Random.Shared.Next(1_000_000, int.MaxValue),
        EventId = rig.Event.Id, Event = rig.Event, Channel = channel, Title = rig.Event.Title,
        Status = DvrRecordingStatus.Completed, OutputPath = path, Quality = quality, PartName = partName,
        ScheduledStart = ended.AddHours(-1), ScheduledEnd = ended
    };

    private static async Task<PendingImport> AddChoiceAsync(PartIdentityIntegrationHarness rig, DvrRecording recording)
    {
        var choice = new PendingImport
        {
            // The import claim set is shared by all tests, so the id must not repeat.
            Id = Random.Shared.Next(1_000_000, int.MaxValue),
            DownloadId = $"dvr-{recording.Id}", Title = Path.GetFileName(recording.OutputPath!),
            FilePath = recording.OutputPath!, Size = 4096, Quality = recording.Quality,
            SuggestedEventId = rig.Event.Id, SuggestedPart = recording.PartName, Protocol = "DVR"
        };
        rig.Db.PendingImports.Add(choice);
        await rig.Db.SaveChangesAsync();
        return choice;
    }

    private static Task PruneAsync(PartIdentityIntegrationHarness rig, Config config) =>
        new HousekeepingService(rig.Services, NullLogger<HousekeepingService>.Instance)
            .PruneDvrRecordingsAsync(rig.Db, config, CancellationToken.None);

    private static string DirectoryOf(string path) => Path.GetDirectoryName(path)!;

    /// <summary>
    /// Runs an action once, when a pending import row is read without
    /// tracking. The tests use it to finish an accept inside that read.
    /// </summary>
    private sealed class AcceptFinishesOnRowRead : IMaterializationInterceptor
    {
        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
        {
            if (entity is PendingImport && materializationData.QueryTrackingBehavior != QueryTrackingBehavior.TrackAll)
                Interlocked.Exchange(ref _pending, null)?.Invoke();
            return entity;
        }

        private Action? _pending;

        public void Arm(Action action) => _pending = action;
    }

    private static EventDvrService CreateService(PartIdentityIntegrationHarness rig)
    {
        var configService = rig.Services.GetRequiredService<ConfigService>();
        return new EventDvrService(NullLogger<EventDvrService>.Instance, rig.Db,
            null!, null!, null!,
            new FFmpegRecorderService(NullLogger<FFmpegRecorderService>.Instance, configService, null!),
            null!, null!, null!, configService,
            rig.Services.GetRequiredService<LibraryImportService>());
    }
}
