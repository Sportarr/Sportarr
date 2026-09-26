using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PartIdentityPendingContractV2Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefusedPendingImport_CannotCompleteOrEditSurvivingFile(bool fullCollision)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        if (fullCollision) { rig.Settings.StandardFileFormat = "same-name"; await rig.Db.SaveChangesAsync(); }
        var held = fullCollision
            ? await PartIdentityContractV2Tests.CreateFull(rig)
            : await rig.ImportAsync("UFC.9999.Prelims", "held-prelims.1080p.WEB-DL.mkv", "Prelims");
        held.Quality = "WEBDL-1080p"; held.ReleaseGroup = "held-group"; rig.Event.Quality = held.Quality;
        await rig.Db.SaveChangesAsync();
        var bytes = await File.ReadAllBytesAsync(held.FilePath);
        var directory = NewDirectory();
        try
        {
            var pending = await AddPending(rig, directory);
            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { importMode = "copy", metadataOverrides = new { releaseGroup = "must-not-touch-survivor" } });
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
            pending.Status.Should().Be(PendingImportStatus.Pending); pending.ResolvedAt.Should().BeNull();
            pending.ErrorMessage.Should().NotBeNullOrWhiteSpace();
            var survivor = await rig.Db.EventFiles.SingleAsync(); survivor.Id.Should().Be(held.Id);
            survivor.ReleaseGroup.Should().Be("held-group"); survivor.Quality.Should().Be("WEBDL-1080p");
            (await File.ReadAllBytesAsync(held.FilePath)).Should().Equal(bytes);
            rig.Event.FilePath.Should().Be(held.FilePath); rig.Event.Quality.Should().Be("WEBDL-1080p");
            File.Exists(pending.FilePath).Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AcceptedDistinctPendingPart_UpdatesOnlyItsOwnFileAndPreservesFullSummary()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var full = await PartIdentityContractV2Tests.CreateFull(rig);
        full.ReleaseGroup = "full-group"; await rig.Db.SaveChangesAsync();
        var bytes = await File.ReadAllBytesAsync(full.FilePath);
        var directory = NewDirectory();
        try
        {
            var pending = await AddPending(rig, directory);
            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { importMode = "copy", metadataOverrides = new { releaseGroup = "new-part-group" } });
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(body);
            pending.Status.Should().Be(PendingImportStatus.Completed); pending.ResolvedAt.Should().NotBeNull();
            var files = await rig.Db.EventFiles.ToListAsync(); files.Should().HaveCount(2);
            var part = files.Single(f => f.Id != full.Id);
            part.PartName.Should().Be("Prelims"); part.PartNumber.Should().Be(2);
            part.ReleaseGroup.Should().Be("new-part-group"); full.ReleaseGroup.Should().Be("full-group");
            (await File.ReadAllBytesAsync(full.FilePath)).Should().Equal(bytes);
            rig.Event.FilePath.Should().Be(full.FilePath); rig.Event.Quality.Should().Be("WEBDL-1080p");
            rig.Event.HasFile.Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("known-length")]
    [InlineData("unknown-length")]
    [InlineData("empty-body")]
    public async Task PendingAccept_ReadsPresentJsonAndPreservesEmptyBodyCompatibility(string bodyMode)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory();
        try
        {
            var pending = await AddPending(rig, directory);
            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            var payload = new { importMode = "copy", metadataOverrides = new { releaseGroup = "body-control-group" } };
            using HttpContent content = bodyMode switch
            {
                "known-length" => new StringContent(System.Text.Json.JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8, "application/json"),
                "unknown-length" => JsonContent.Create(payload),
                _ => new ByteArrayContent(Array.Empty<byte>())
            };
            if (bodyMode == "known-length") content.Headers.ContentLength.Should().BeGreaterThan(0);
            else if (bodyMode == "unknown-length") content.Headers.ContentLength.Should().BeNull();
            else content.Headers.ContentLength.Should().Be(0);
            using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", content);
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(body);
            pending.Status.Should().Be(PendingImportStatus.Completed);
            var file = await rig.Db.EventFiles.SingleAsync();
            file.PartName.Should().Be("Prelims"); file.PartNumber.Should().Be(2);
            if (bodyMode == "empty-body") file.ReleaseGroup.Should().BeNull();
            else file.ReleaseGroup.Should().Be("body-control-group");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAccept_PreservesTheSelectedPartInFileAndHistory()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'d', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "disk-selected-part",
                Title = Path.GetFileNameWithoutExtension(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                Protocol = "Usenet",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Main Card"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);
            var body = await response.Content.ReadAsStringAsync();

            response.IsSuccessStatusCode.Should().BeTrue(body);
            (await rig.Db.EventFiles.SingleAsync()).PartName.Should().Be("Main Card");
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be("Main Card");
            rig.Event.HasFile.Should().BeFalse();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAccept_InfersPartForExistingPendingRowWithoutAStoredPart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'m', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "disk-existing-row",
                Title = Path.GetFileName(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                SuggestedEventId = rig.Event.Id
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var imported = await rig.Db.EventFiles.SingleAsync();
            imported.PartName.Should().Be("Main Card");
            imported.PartNumber.Should().Be(3);
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be("Main Card");
            rig.Event.HasFile.Should().BeFalse();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAccept_ExplicitFullEventOverridesPartInFilename()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'f', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "disk-explicit-full",
                Title = Path.GetFileName(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Full Event"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var imported = await rig.Db.EventFiles.SingleAsync();
            imported.PartName.Should().BeNull();
            imported.PartNumber.Should().BeNull();
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().BeNull();
            rig.Event.HasFile.Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAccept_UsesEditedPartForHistoryAndCompletion()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Event.MonitoredParts = "Main Card";
        await rig.Db.SaveChangesAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'e', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "disk-edited-part",
                Title = Path.GetFileName(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Main Card"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { metadataOverrides = new { partName = "Prelims", partNumber = 2 } });
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var imported = await rig.Db.EventFiles.SingleAsync();
            imported.PartName.Should().Be("Prelims");
            imported.PartNumber.Should().Be(2);
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be("Prelims");
            rig.Event.HasFile.Should().BeFalse();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("Prelims")]
    [InlineData(null)]
    public async Task ClientPendingAccept_UsesEditedPartForHistoryAndCompletion(string? partName)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Event.MonitoredParts = "Main Card";
        await rig.Db.SaveChangesAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'c', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "client-edited-part",
                Title = Path.GetFileName(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                Protocol = "Usenet",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Main Card"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { importMode = "copy", metadataOverrides = new { partName, partNumber = 2 } });
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var imported = await rig.Db.EventFiles.SingleAsync();
            imported.PartName.Should().Be("Prelims");
            imported.PartNumber.Should().Be(2);
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be("Prelims");
            rig.Event.HasFile.Should().BeFalse();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(null, 2, "Prelims", 2, false)]
    [InlineData("Full Event", null, null, null, true)]
    public async Task DiskPendingAccept_NormalizesPartEditsBeforeCompleteness(
        string? partName, int? partNumber, string? expectedName, int? expectedNumber, bool expectedComplete)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Event.MonitoredParts = "Main Card";
        await rig.Db.SaveChangesAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'n', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "disk-normalized-part",
                Title = Path.GetFileName(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Main Card"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { metadataOverrides = new { partName, partNumber } });
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var imported = await rig.Db.EventFiles.SingleAsync();
            imported.PartName.Should().Be(expectedName);
            imported.PartNumber.Should().Be(expectedNumber);
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be(expectedName);
            rig.Event.HasFile.Should().Be(expectedComplete);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ClientPendingAccept_EditedPartUsesItsOwnUpgradeSlot()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var heldMain = await rig.ImportAsync("UFC.9999.Main.Card.720p.WEB-DL",
            "held-main.720p.WEB-DL.mkv", "Main Card");
        var heldBytes = await File.ReadAllBytesAsync(heldMain.FilePath);
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'u', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "client-part-slot",
                Title = Path.GetFileName(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                Protocol = "Usenet",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Main Card"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { importMode = "copy", metadataOverrides = new { partName = "Prelims", partNumber = 2 } });
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var files = await rig.Db.EventFiles.ToListAsync();
            files.Should().HaveCount(2);
            var prelims = files.Single(file => file.Id != heldMain.Id);
            prelims.PartName.Should().Be("Prelims");
            prelims.PartNumber.Should().Be(2);
            Path.GetFileName(prelims.FilePath).Should().Contain("Prelims");
            (await File.ReadAllBytesAsync(heldMain.FilePath)).Should().Equal(heldBytes);
            (await rig.Db.ImportHistories.OrderBy(history => history.Id).LastAsync()).Part.Should().Be("Prelims");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LeagueScan_PersistsTheSuggestedPartForPendingAcceptance()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var root = (await rig.Db.RootFolders.SingleAsync()).Path;
        var folder = Path.Combine(root, "UFC");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
        await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'m', 4096).ToArray());

        await using var app = await CreatePendingHost(rig, includeLeagueScan: true);
        using var client = app.GetTestClient();
        using var scanResponse = await client.PostAsJsonAsync($"/api/leagues/{rig.Event.LeagueId}/scan", new { });
        var scanBody = await scanResponse.Content.ReadAsStringAsync();
        scanResponse.IsSuccessStatusCode.Should().BeTrue(scanBody);
        using var scan = System.Text.Json.JsonDocument.Parse(scanBody);
        var files = scan.RootElement.GetProperty("files");
        files.GetArrayLength().Should().Be(1);
        files[0].GetProperty("suggestedEventId").GetInt32().Should().Be(rig.Event.Id);
        files[0].GetProperty("part").GetString().Should().Be("Main Card");

        var pending = await rig.Db.PendingImports.SingleAsync();
        pending.SuggestedEventId.Should().Be(rig.Event.Id);
        pending.SuggestedPart.Should().Be("Main Card");
        using var acceptResponse = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);
        acceptResponse.IsSuccessStatusCode.Should().BeTrue(await acceptResponse.Content.ReadAsStringAsync());
        var imported = await rig.Db.EventFiles.SingleAsync();
        imported.PartName.Should().Be("Main Card");
        imported.PartNumber.Should().Be(3);
        (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be("Main Card");
        rig.Event.HasFile.Should().BeFalse();
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("remove-from-client")]
    public async Task PendingRemoval_PreservesTheSelectedEventAndPartInBlocklist(string action)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var pending = new PendingImport
        {
            DownloadId = "pending-removal-part",
            Title = "UFC.9999.Prelims.720p.WEB-DL",
            FilePath = "/data/e2e-lib/downloads/UFC.9999.Prelims.720p.WEB-DL.mkv",
            Size = 4096,
            Quality = "WEBDL-720p",
            Protocol = "Usenet",
            SuggestedEventId = rig.Event.Id,
            SuggestedPart = "Prelims"
        };
        rig.Db.PendingImports.Add(pending);
        await rig.Db.SaveChangesAsync();

        await using var app = await CreatePendingHost(rig);
        using var client = app.GetTestClient();
        using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/{action}", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var blocked = await rig.Db.Blocklist.SingleAsync();
        blocked.EventId.Should().Be(rig.Event.Id);
        blocked.Part.Should().Be("Prelims");
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sportarr-pending-part-v2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }

    private static async Task<PendingImport> AddPending(PartIdentityIntegrationHarness rig, string directory)
    {
        var path = Path.Combine(directory, "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.mkv");
        await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'p', 4096).ToArray());
        var pending = new PendingImport { DownloadId = "pending-part-v2", Title = Path.GetFileNameWithoutExtension(path),
            FilePath = path, Size = 4096, Quality = "WEBDL-720p", Protocol = "Usenet", SuggestedEventId = rig.Event.Id };
        rig.Db.PendingImports.Add(pending); await rig.Db.SaveChangesAsync(); return pending;
    }

    private static async Task<WebApplication> CreatePendingHost(PartIdentityIntegrationHarness rig, bool includeLeagueScan = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(rig.Db);
        builder.Services.AddSingleton(rig.Services.GetRequiredService<FileImportService>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<ConfigService>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<EventPartDetector>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<DownloadClientService>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<PackImportService>());
        // The mapped sibling routes must resolve as services and must never run here.
        builder.Services.AddSingleton<QueueRemovalService>(_ => throw new InvalidOperationException("Unexpected queue removal route"));
        builder.Services.AddSingleton<ImportMatchingService>(_ => includeLeagueScan
            ? new ImportMatchingService(rig.Db,
                rig.Services.GetRequiredService<MediaFileParser>(),
                rig.Services.GetRequiredService<SportsFileNameParser>(),
                rig.Services.GetRequiredService<EventPartDetector>(),
                rig.Services.GetRequiredService<ILogger<ImportMatchingService>>())
            : throw new InvalidOperationException("Unexpected matching route"));
        if (includeLeagueScan)
        {
            builder.Services.AddSingleton(rig.Services.GetRequiredService<FileNamingService>());
            builder.Services.AddSingleton(rig.Services.GetRequiredService<SportarrApiClient>());
            builder.Services.AddSingleton(rig.Services.GetRequiredService<EventQueryService>());
            builder.Services.AddSingleton<TaskService>(_ => throw new InvalidOperationException("Unexpected task route"));
            builder.Services.AddSingleton<EventStreamService>(_ => throw new InvalidOperationException("Unexpected event stream route"));
            builder.Services.AddSingleton<FileRenameService>(_ => throw new InvalidOperationException("Unexpected rename route"));
            builder.Services.AddSingleton<LeagueAddService>(_ => throw new InvalidOperationException("Unexpected league add route"));
            builder.Services.AddSingleton<LeagueEventSyncService>(_ => throw new InvalidOperationException("Unexpected league sync route"));
            builder.Services.AddSingleton<LeagueMoveService>(_ => throw new InvalidOperationException("Unexpected league move route"));
        }
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
        var app = builder.Build();
        app.MapQueueAndImportEndpoints();
        if (includeLeagueScan) app.MapLeagueEndpoints();
        await app.StartAsync();
        return app;
    }
}
