using System.Text.Json;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Covers the webhook/Notifiarr payload shape, redesigned twice from
/// Notifiarr developer feedback: first to fix flat-field gaps (size only
/// in message text, no distinction between the internal eventId and an
/// external metadata id, Grab/Import field-set drift), then a full
/// restructure to mirror Sonarr's actual production webhook shape
/// (Series/Release/EpisodeFile/DeletedFiles) after confirming via Sonarr's
/// own GitHub source that this wasn't just a preference - it's the real,
/// shipped pattern integrators already build against. BuildWebhookPayload
/// and WebhookJsonOptions are internal (not private) specifically so this
/// test class can exercise the real payload-building code directly via
/// InternalsVisibleTo, rather than only through an HTTP capture.
/// </summary>
public class NotificationServiceWebhookPayloadTests
{
    private static JsonElement SerializeToJson(WebhookPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, NotificationService.WebhookJsonOptions);
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public void Grab_PutsQualityIndexerSize_UnderReleaseObject_NotTopLevel()
    {
        var data = new NotificationEventData
        {
            EventId = 42,
            EventTitle = "UFC 300",
            Indexer = "SomeIndexer",
            Quality = "1080p",
            Size = 4_500_000_000L,
            DownloadId = "abc123",
        };

        var payload = NotificationService.BuildWebhookPayload("Grabbed: UFC 300", "text with size in it", NotificationTrigger.OnGrab, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.TryGetProperty("quality", out _).Should().BeFalse("quality should live under release, not top-level");
        json.TryGetProperty("size", out _).Should().BeFalse("size should live under release, not top-level");
        json.TryGetProperty("indexer", out _).Should().BeFalse("indexer should live under release, not top-level");

        var release = json.GetProperty("release");
        release.GetProperty("quality").GetString().Should().Be("1080p");
        release.GetProperty("indexer").GetString().Should().Be("SomeIndexer");
        release.GetProperty("size").GetInt64().Should().Be(4_500_000_000L);
    }

    [Fact]
    public void EventId_And_EventExternalId_LiveUnderSeries_NotTopLevel()
    {
        var data = new NotificationEventData
        {
            EventId = 42,
            EventExternalId = "ev-848683",
            EventTitle = "UFC 300",
        };

        var payload = NotificationService.BuildWebhookPayload("title", "message", NotificationTrigger.OnEventAdded, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.TryGetProperty("eventId", out _).Should().BeFalse();
        json.TryGetProperty("eventExternalId", out _).Should().BeFalse();

        var series = json.GetProperty("series");
        series.GetProperty("id").GetInt32().Should().Be(42);
        series.GetProperty("externalId").GetString().Should().Be("ev-848683");
        series.GetProperty("title").GetString().Should().Be("UFC 300");
    }

    [Fact]
    public void TsdbId_LivesUnderSeries_WhenResolved_AndIsOmittedWhenNull()
    {
        var data = new NotificationEventData
        {
            EventId = 42,
            EventExternalId = "ev-848683",
            EventTitle = "UFC 300",
            TsdbId = "2368486",
        };

        var payload = NotificationService.BuildWebhookPayload("title", "message", NotificationTrigger.OnEventAdded, data, "Sportarr");
        var json = SerializeToJson(payload);
        json.GetProperty("series").GetProperty("tsdbId").GetString().Should().Be("2368486");

        var withoutTsdb = new NotificationEventData
        {
            EventId = 42,
            EventTitle = "UFC 300",
        };
        var payload2 = NotificationService.BuildWebhookPayload("title", "message", NotificationTrigger.OnEventAdded, withoutTsdb, "Sportarr");
        var json2 = SerializeToJson(payload2);
        json2.GetProperty("series").TryGetProperty("tsdbId", out _).Should().BeFalse("null tsdbId must be omitted, keeping old payloads byte-identical");
    }

    [Fact]
    public void EpisodeFile_CarriesQualityAndSize_NotJustPath()
    {
        var data = new NotificationEventData
        {
            EventTitle = "UFC 300",
            File = new NotificationFileData { Path = "/media/league/UFC 300/ufc300.mkv", Quality = "1080p", Size = 4_500_000_000L },
        };

        var payload = NotificationService.BuildWebhookPayload("Imported: UFC 300", "message", NotificationTrigger.OnDownload, data, "Sportarr");
        var json = SerializeToJson(payload);

        var episodeFile = json.GetProperty("episodeFile");
        episodeFile.GetProperty("path").GetString().Should().Be("/media/league/UFC 300/ufc300.mkv");
        episodeFile.GetProperty("quality").GetString().Should().Be("1080p");
        episodeFile.GetProperty("size").GetInt64().Should().Be(4_500_000_000L);
        episodeFile.GetProperty("relativePath").GetString().Should().Be("ufc300.mkv");
    }

    [Fact]
    public void SeriesPath_IsDerivedFromEpisodeFilePath_WhenNoExplicitSeriesPath()
    {
        var data = new NotificationEventData
        {
            EventTitle = "UFC 300",
            File = new NotificationFileData { Path = "/media/league/UFC 300/ufc300.mkv" },
        };

        var payload = NotificationService.BuildWebhookPayload("title", "message", NotificationTrigger.OnDownload, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.GetProperty("series").GetProperty("path").GetString().Should().Be("/media/league/UFC 300");
    }

    [Fact]
    public void Upgrade_CarriesBothEpisodeFile_And_DeletedFiles_WithFullOldFileData()
    {
        // The core ask: on an upgrade, show what replaced what - both the
        // new file's info AND the old file's path/quality/size in the same
        // payload, not just a bare path.
        var data = new NotificationEventData
        {
            EventTitle = "UFC 300",
            File = new NotificationFileData { Path = "/media/ufc300.1080p.mkv", Quality = "1080p", Size = 4_500_000_000L },
            IsUpgrade = true,
            DeletedFiles = new List<NotificationFileData>
            {
                new() { Path = "/media/ufc300.720p.mkv", Quality = "720p", Size = 2_100_000_000L }
            }
        };

        var payload = NotificationService.BuildWebhookPayload("Upgraded: UFC 300", "message", NotificationTrigger.OnUpgrade, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.GetProperty("isUpgrade").GetBoolean().Should().BeTrue();
        json.GetProperty("episodeFile").GetProperty("quality").GetString().Should().Be("1080p");

        var deletedFiles = json.GetProperty("deletedFiles");
        deletedFiles.GetArrayLength().Should().Be(1);
        var oldFile = deletedFiles[0];
        oldFile.GetProperty("path").GetString().Should().Be("/media/ufc300.720p.mkv");
        oldFile.GetProperty("quality").GetString().Should().Be("720p");
        oldFile.GetProperty("size").GetInt64().Should().Be(2_100_000_000L);
    }

    [Fact]
    public void PureDelete_UsesDeletedFiles_SameFieldAsUpgrade_WithSizeAndQuality()
    {
        // "add the sizes to the deleted payload as well as people want to
        // see recovered space" + "the deletedFile would be attached to
        // upgrades and delete so they are the same struc"
        var data = new NotificationEventData
        {
            EventTitle = "UFC 300",
            DeletedFiles = new List<NotificationFileData>
            {
                new() { Path = "/media/ufc300.mkv", Quality = "1080p", Size = 4_500_000_000L }
            }
        };

        var payload = NotificationService.BuildWebhookPayload("Deleted: UFC 300", "message", NotificationTrigger.OnEventFileDelete, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.TryGetProperty("episodeFile", out _).Should().BeFalse("a pure delete has no current file, only deletedFiles");
        var deletedFiles = json.GetProperty("deletedFiles");
        deletedFiles.GetArrayLength().Should().Be(1);
        deletedFiles[0].GetProperty("size").GetInt64().Should().Be(4_500_000_000L);
        deletedFiles[0].GetProperty("quality").GetString().Should().Be("1080p");
    }

    [Fact]
    public void BulkDelete_ListsEveryFile_NotJustOneRepresentative()
    {
        var data = new NotificationEventData
        {
            EventTitle = "UFC 300",
            DeletedFiles = new List<NotificationFileData>
            {
                new() { Path = "/media/ufc300-prelims.mkv", Quality = "1080p", Size = 1_000_000_000L },
                new() { Path = "/media/ufc300-mainCard.mkv", Quality = "1080p", Size = 4_500_000_000L },
            }
        };

        var payload = NotificationService.BuildWebhookPayload("Deleted: UFC 300", "message", NotificationTrigger.OnEventFileDelete, data, "Sportarr");
        var json = SerializeToJson(payload);

        var deletedFiles = json.GetProperty("deletedFiles");
        deletedFiles.GetArrayLength().Should().Be(2);
        (deletedFiles[0].GetProperty("size").GetInt64() + deletedFiles[1].GetProperty("size").GetInt64())
            .Should().Be(5_500_000_000L, "consumers should be able to sum recovered space across every deleted file");
    }

    [Fact]
    public void SeriesPath_FallsBackToDeletedFilesDirectory_WhenNoEpisodeFile()
    {
        var data = new NotificationEventData
        {
            EventTitle = "UFC 300",
            DeletedFiles = new List<NotificationFileData>
            {
                new() { Path = "/media/league/UFC 300/ufc300.mkv" }
            }
        };

        var payload = NotificationService.BuildWebhookPayload("title", "message", NotificationTrigger.OnEventFileDelete, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.GetProperty("series").GetProperty("path").GetString().Should().Be("/media/league/UFC 300");
    }

    [Fact]
    public void GrabAndDownload_ForTheSameRelease_ShareDownloadIdAndIndexer()
    {
        // Correlation gap Notifiarr flagged: tie a Download event back to
        // the Grab that produced it. Indexer now lives under release on
        // both (Sonarr's WebhookImportPayload carries a release too).
        var grabData = new NotificationEventData { EventId = 42, EventTitle = "UFC 300", Indexer = "SomeIndexer", DownloadId = "abc123" };
        var downloadData = new NotificationEventData
        {
            EventId = 42, EventTitle = "UFC 300", Indexer = "SomeIndexer", DownloadId = "abc123",
            File = new NotificationFileData { Path = "/media/ufc300.mkv" }
        };

        var grabJson = SerializeToJson(NotificationService.BuildWebhookPayload("Grabbed", "msg", NotificationTrigger.OnGrab, grabData, "Sportarr"));
        var downloadJson = SerializeToJson(NotificationService.BuildWebhookPayload("Imported", "msg", NotificationTrigger.OnDownload, downloadData, "Sportarr"));

        grabJson.GetProperty("series").GetProperty("id").GetInt32().Should().Be(downloadJson.GetProperty("series").GetProperty("id").GetInt32());
        grabJson.GetProperty("release").GetProperty("indexer").GetString().Should().Be(downloadJson.GetProperty("release").GetProperty("indexer").GetString());
        grabJson.GetProperty("downloadId").GetString().Should().Be(downloadJson.GetProperty("downloadId").GetString());
    }

    [Fact]
    public void IrrelevantObjects_AreOmittedFromTheWirePayload_NotSentAsNull()
    {
        var data = new NotificationEventData { EventId = 42, EventTitle = "UFC 300", Indexer = "SomeIndexer" };

        var payload = NotificationService.BuildWebhookPayload("Grabbed", "msg", NotificationTrigger.OnGrab, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.TryGetProperty("episodeFile", out _).Should().BeFalse();
        json.TryGetProperty("deletedFiles", out _).Should().BeFalse();
        json.TryGetProperty("healthType", out _).Should().BeFalse();
        json.TryGetProperty("recordingId", out _).Should().BeFalse();
    }

    [Fact]
    public void PropertyNames_AreCamelCase()
    {
        var data = new NotificationEventData { EventId = 42, EventTitle = "UFC 300", DownloadId = "abc", RecordingId = 7 };

        var payload = NotificationService.BuildWebhookPayload("title", "message", NotificationTrigger.OnGrab, data, "MyInstance");
        var json = SerializeToJson(payload);

        json.TryGetProperty("downloadId", out _).Should().BeTrue();
        json.TryGetProperty("recordingId", out _).Should().BeTrue();
        json.TryGetProperty("applicationUrl", out _).Should().BeTrue();
        json.GetProperty("instanceName").GetString().Should().Be("MyInstance");
    }

    [Fact]
    public void NoEventIdentity_NoSeriesObjectAtAll()
    {
        var data = new NotificationEventData { HealthType = "DiskSpaceLow", HealthLevel = "Warning" };

        var payload = NotificationService.BuildWebhookPayload("Health issue", "msg", NotificationTrigger.OnHealthIssue, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.TryGetProperty("series", out _).Should().BeFalse();
        json.TryGetProperty("episodeFile", out _).Should().BeFalse();
    }

    [Fact]
    public void RenameEvent_UsesSeriesPathBranch_NoEventIdRequired()
    {
        var data = new NotificationEventData { RenamedCount = 3, SeriesPath = "/media/league/UFC 300" };

        var payload = NotificationService.BuildWebhookPayload("Renamed 3 file(s)", "msg", NotificationTrigger.OnRename, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.GetProperty("series").GetProperty("path").GetString().Should().Be("/media/league/UFC 300");
        json.TryGetProperty("episodeFile", out _).Should().BeFalse();
        json.GetProperty("renamedCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public void NullData_StillProducesAValidMinimalPayload()
    {
        var payload = NotificationService.BuildWebhookPayload("Test Notification", "This is a test.", NotificationTrigger.Test, null, "Sportarr");
        var json = SerializeToJson(payload);

        json.GetProperty("eventType").GetString().Should().Be("Test");
        json.TryGetProperty("series", out _).Should().BeFalse();
    }
}
