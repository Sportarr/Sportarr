using FluentAssertions;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class ManualQueueImportTests
{
    [Fact]
    public async Task QueueEndpointRejectsIncompletePreferenceWarningWithoutTouchingCurrentFile()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var existing = await rig.ImportAsync("UFC.9999.2160p.WEB-DL", "existing.2160p.WEB-DL.mkv");
        var original = await File.ReadAllBytesAsync(existing.FilePath);
        var row = new DownloadQueueItem
        {
            EventId = rig.Event.Id,
            Title = "UFC.9999.1080p.HDTV-DARKSPORT",
            DownloadId = "unfinished-manual-choice",
            DownloadClientId = (await rig.Db.DownloadClients.SingleAsync()).Id,
            Status = DownloadStatus.ImportWarning,
            Progress = 99,
            ErrorMessage = "Not an upgrade for the existing file. Existing quality: WEBDL-2160p. New quality: HDTV-1080p.",
            Quality = "HDTV-1080p"
        };
        rig.Db.DownloadQueue.Add(row);
        await rig.Db.SaveChangesAsync();

        using var response = await rig.Client.PostAsync($"/api/queue/{row.Id}/import-anyway", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        row.Status.Should().Be(DownloadStatus.ImportWarning);
        (await File.ReadAllBytesAsync(existing.FilePath)).Should().Equal(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueueEndpointImportsCompletedPreferenceWarningAfterExplicitRequest(bool historyExpired)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var existing = await rig.ImportAsync("UFC.9999.2160p.WEB-DL", "existing.2160p.WEB-DL.mkv");
        existing.Quality = "WEBDL-2160p";
        existing.CustomFormatScore = 560;
        await rig.Db.SaveChangesAsync();
        var oldPath = existing.FilePath;
        var folder = Path.Combine(Path.GetTempPath(), "sportarr-manual-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "UFC.9999.1080p.HDTV.mkv");
        await File.WriteAllBytesAsync(source, Enumerable.Repeat((byte)'r', 4096).ToArray());
        try
        {
            var row = new DownloadQueueItem
            {
                EventId = rig.Event.Id,
                Title = "UFC.9999.1080p.HDTV-DARKSPORT",
                DownloadId = "completed-manual-route",
                DownloadClientId = (await rig.Db.DownloadClients.SingleAsync()).Id,
                Status = DownloadStatus.ImportWarning,
                Progress = 100,
                ErrorMessage = "Not an upgrade for the existing file. Existing quality: WEBDL-2160p. New quality: HDTV-1080p.",
                Quality = "HDTV-1080p",
                CustomFormatScore = 2500,
                Protocol = "Usenet",
                OutputPath = historyExpired ? folder : null
            };
            rig.Db.DownloadQueue.Add(row);
            await rig.Db.SaveChangesAsync();
            rig.Transport.CompletedDownloadId = row.DownloadId;
            rig.Transport.CompletedDownloadPath = historyExpired ? null : folder;

            using var response = await rig.Client.PostAsync($"/api/queue/{row.Id}/import-anyway", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            row.Status.Should().Be(DownloadStatus.Imported);
            var selected = await rig.Db.EventFiles.SingleAsync();
            selected.Quality.Should().Be("HDTV-1080p");
            selected.CustomFormatScore.Should().Be(2500);
            (await File.ReadAllBytesAsync(selected.FilePath)).Should().OnlyContain(value => value == (byte)'r');
            if (!string.Equals(oldPath, selected.FilePath, StringComparison.OrdinalIgnoreCase))
                File.Exists(oldPath).Should().BeFalse();
            (await rig.Db.EventFileHistory.SingleAsync()).Type.Should().Be(EventFileHistoryType.ReplacedManually);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task OnlyOneRequestCanClaimTheSameWarning()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        var row = new DownloadQueueItem
        {
            EventId = rig.Event.Id,
            Title = "UFC.9999.1080p.HDTV-DARKSPORT",
            DownloadId = "claim-once",
            DownloadClientId = (await rig.Db.DownloadClients.SingleAsync()).Id,
            Status = DownloadStatus.ImportWarning,
            Progress = 100,
            ErrorMessage = "Not an upgrade for the existing file. Existing quality: WEBDL-2160p. New quality: HDTV-1080p."
        };
        rig.Db.DownloadQueue.Add(row);
        await rig.Db.SaveChangesAsync();

        var first = await ManualQueueImportPolicy.TryClaimAsync(rig.Db, row.Id);
        var second = await ManualQueueImportPolicy.TryClaimAsync(rig.Db, row.Id);

        first.Should().BeTrue();
        second.Should().BeFalse();
        (await rig.Db.DownloadQueue.AsNoTracking().SingleAsync()).Status.Should().Be(DownloadStatus.Importing);
    }

    [Fact]
    public async Task ExplicitImportReplacesLowerRankedCompletedDownloadWithoutChangingAutomaticRule()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var existing = await rig.ImportAsync("UFC.9999.2160p.WEB-DL", "existing.2160p.WEB-DL.mkv");
        existing.Quality = "WEBDL-2160p";
        existing.CustomFormatScore = 560;
        rig.Event.Quality = existing.Quality;
        await rig.Db.SaveChangesAsync();
        var oldPath = existing.FilePath;
        var folder = Path.Combine(Path.GetTempPath(), "sportarr-manual-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "UFC.9999.1080p.HDTV.mkv");
        var replacement = Enumerable.Repeat((byte)'n', 4096).ToArray();
        await File.WriteAllBytesAsync(source, replacement);
        try
        {
            var row = new DownloadQueueItem
            {
                EventId = rig.Event.Id,
                Title = "UFC.9999.1080p.HDTV-DARKSPORT",
                DownloadId = "completed-manual-choice",
                Status = DownloadStatus.Completed,
                Progress = 100,
                Quality = "HDTV-1080p",
                CustomFormatScore = 2500,
                Protocol = "Usenet"
            };
            rig.Db.DownloadQueue.Add(row);
            await rig.Db.SaveChangesAsync();
            var importer = rig.Services.GetRequiredService<FileImportService>();

            var automatic = await importer.ImportDownloadAsync(row, source, PostImportMode.Copy);

            automatic.Should().BeNull();
            row.Status.Should().Be(DownloadStatus.ImportWarning);
            File.Exists(oldPath).Should().BeTrue();

            var manual = await importer.ImportDownloadAsync(row, source, PostImportMode.Copy, allowPreferenceOverride: true);

            manual.Should().NotBeNull();
            row.Status.Should().Be(DownloadStatus.Imported);
            var selected = await rig.Db.EventFiles.SingleAsync();
            selected.Id.Should().NotBe(existing.Id);
            selected.Quality.Should().Be("HDTV-1080p");
            selected.CustomFormatScore.Should().Be(2500);
            (await File.ReadAllBytesAsync(selected.FilePath)).Should().Equal(replacement);
            File.Exists(source).Should().BeTrue();
            var removal = await rig.Db.EventFileHistory.SingleAsync();
            removal.Reason.Should().Be("Manually replaced with HDTV-1080p");
            removal.Type.Should().Be(EventFileHistoryType.ReplacedManually);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
