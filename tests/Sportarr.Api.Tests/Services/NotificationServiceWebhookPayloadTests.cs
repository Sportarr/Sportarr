using System.Text.Json;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Covers the webhook/Notifiarr payload shape fixes prompted by Notifiarr's
/// developer flagging real structural problems: size only ever embedded in
/// message text, no distinction between the internal eventId and an
/// external metadata id, title/eventTitle/series.title duplication with no
/// documented relationship, filePath/episodeFile.path duplication, and
/// Grab/Import carrying inconsistent field sets. BuildWebhookPayload and
/// WebhookJsonOptions are internal (not private) specifically so this test
/// class can exercise the real payload-building code directly via
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
    public void Grab_CarriesSizeAsARealNumericField_NotOnlyInMessageText()
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

        json.GetProperty("size").GetInt64().Should().Be(4_500_000_000L);
    }

    [Fact]
    public void EventId_And_EventExternalId_AreDistinctFields()
    {
        var data = new NotificationEventData
        {
            EventId = 42,
            EventExternalId = "ev-848683",
            EventTitle = "UFC 300",
        };

        var payload = NotificationService.BuildWebhookPayload("title", "message", NotificationTrigger.OnDownload, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.GetProperty("eventId").GetInt32().Should().Be(42);
        json.GetProperty("eventExternalId").GetString().Should().Be("ev-848683");
    }

    [Fact]
    public void EventExternalId_Omitted_WhenNotProvided()
    {
        var data = new NotificationEventData { EventId = 42, EventTitle = "UFC 300" };

        var payload = NotificationService.BuildWebhookPayload("title", "message", NotificationTrigger.OnDownload, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.TryGetProperty("eventExternalId", out _).Should().BeFalse();
    }

    [Fact]
    public void Title_IsTheHumanSentence_DistinctFromEventTitle()
    {
        var data = new NotificationEventData { EventTitle = "UFC 300", FilePath = "/media/ufc300.mkv" };

        var payload = NotificationService.BuildWebhookPayload("Grabbed: UFC.300.1080p.WEB-DL-GROUP", "message", NotificationTrigger.OnGrab, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.GetProperty("title").GetString().Should().Be("Grabbed: UFC.300.1080p.WEB-DL-GROUP");
        json.GetProperty("eventTitle").GetString().Should().Be("UFC 300");
        json.GetProperty("title").GetString().Should().NotBe(json.GetProperty("eventTitle").GetString());
    }

    [Fact]
    public void SeriesTitle_AlwaysMatchesEventTitle_DerivedNotIndependentlySet()
    {
        var data = new NotificationEventData { EventTitle = "UFC 300", FilePath = "/media/league/ufc300.mkv" };

        var payload = NotificationService.BuildWebhookPayload("Imported: UFC 300", "message", NotificationTrigger.OnDownload, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.GetProperty("series").GetProperty("title").GetString().Should().Be("UFC 300");
        json.GetProperty("series").GetProperty("title").GetString().Should().Be(json.GetProperty("eventTitle").GetString());
    }

    [Fact]
    public void FilePath_And_EpisodeFilePath_AreTheSameString_ByDesign()
    {
        var data = new NotificationEventData { EventTitle = "UFC 300", FilePath = "/media/league/UFC 300/ufc300.mkv" };

        var payload = NotificationService.BuildWebhookPayload("title", "message", NotificationTrigger.OnDownload, data, "Sportarr");
        var json = SerializeToJson(payload);

        var filePath = json.GetProperty("filePath").GetString();
        var episodeFilePath = json.GetProperty("episodeFile").GetProperty("path").GetString();

        filePath.Should().Be("/media/league/UFC 300/ufc300.mkv");
        filePath.Should().Be(episodeFilePath);
    }

    [Fact]
    public void SeriesPath_IsTheDirectoryOfFilePath_AndEpisodeFileRelativePathIsTheFilename()
    {
        var data = new NotificationEventData { EventTitle = "UFC 300", FilePath = "/media/league/UFC 300/ufc300.mkv" };

        var payload = NotificationService.BuildWebhookPayload("title", "message", NotificationTrigger.OnDownload, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.GetProperty("series").GetProperty("path").GetString().Should().Be("/media/league/UFC 300");
        json.GetProperty("episodeFile").GetProperty("relativePath").GetString().Should().Be("ufc300.mkv");
    }

    [Fact]
    public void Grab_CarriesLeagueAndSport_PreviouslyMissingEntirely()
    {
        var data = new NotificationEventData
        {
            EventId = 42,
            EventTitle = "UFC 300",
            League = "UFC",
            Sport = "Combat",
            Indexer = "SomeIndexer",
            DownloadId = "abc123",
        };

        var payload = NotificationService.BuildWebhookPayload("Grabbed: UFC 300", "message", NotificationTrigger.OnGrab, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.GetProperty("league").GetString().Should().Be("UFC");
        json.GetProperty("sport").GetString().Should().Be("Combat");
    }

    [Fact]
    public void Import_CarriesIndexerAndDownloadId_ToCorrelateBackToItsGrab()
    {
        var data = new NotificationEventData
        {
            EventId = 42,
            EventTitle = "UFC 300",
            FilePath = "/media/ufc300.mkv",
            Indexer = "SomeIndexer",
            DownloadId = "abc123",
            IsUpgrade = false,
        };

        var payload = NotificationService.BuildWebhookPayload("Imported: UFC 300", "message", NotificationTrigger.OnDownload, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.GetProperty("indexer").GetString().Should().Be("SomeIndexer");
        json.GetProperty("downloadId").GetString().Should().Be("abc123");
        json.GetProperty("isUpgrade").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void GrabAndImport_ForTheSameEvent_ShareEventIdIndexerAndDownloadId()
    {
        // Simulates the correlation gap Notifiarr flagged: before this fix
        // there was no way to tie a Download event back to the Grab that
        // produced it. Both payloads must now agree on the fields that
        // identify the same underlying release.
        var grabData = new NotificationEventData
        {
            EventId = 42, EventTitle = "UFC 300", Indexer = "SomeIndexer", DownloadId = "abc123", Size = 4_500_000_000L
        };
        var importData = new NotificationEventData
        {
            EventId = 42, EventTitle = "UFC 300", Indexer = "SomeIndexer", DownloadId = "abc123", FilePath = "/media/ufc300.mkv", Size = 4_500_000_000L
        };

        var grabJson = SerializeToJson(NotificationService.BuildWebhookPayload("Grabbed", "msg", NotificationTrigger.OnGrab, grabData, "Sportarr"));
        var importJson = SerializeToJson(NotificationService.BuildWebhookPayload("Imported", "msg", NotificationTrigger.OnDownload, importData, "Sportarr"));

        grabJson.GetProperty("eventId").GetInt32().Should().Be(importJson.GetProperty("eventId").GetInt32());
        grabJson.GetProperty("indexer").GetString().Should().Be(importJson.GetProperty("indexer").GetString());
        grabJson.GetProperty("downloadId").GetString().Should().Be(importJson.GetProperty("downloadId").GetString());
        grabJson.GetProperty("size").GetInt64().Should().Be(importJson.GetProperty("size").GetInt64());
    }

    [Fact]
    public void IrrelevantFields_AreOmittedFromTheWirePayload_NotSentAsNull()
    {
        // The "grouped together consistently, sent complete" ask: a Grab
        // payload shouldn't carry a wall of null health/recording/DVR
        // fields that only apply to other trigger types.
        var data = new NotificationEventData { EventId = 42, EventTitle = "UFC 300", Indexer = "SomeIndexer" };

        var payload = NotificationService.BuildWebhookPayload("Grabbed", "msg", NotificationTrigger.OnGrab, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.TryGetProperty("healthType", out _).Should().BeFalse();
        json.TryGetProperty("recordingId", out _).Should().BeFalse();
        json.TryGetProperty("previousVersion", out _).Should().BeFalse();
        json.TryGetProperty("size", out _).Should().BeFalse();
        json.TryGetProperty("filePath", out _).Should().BeFalse();
    }

    [Fact]
    public void PropertyNames_AreCamelCase()
    {
        var data = new NotificationEventData { EventId = 42, EventTitle = "UFC 300", DownloadId = "abc", RecordingId = 7 };

        var payload = NotificationService.BuildWebhookPayload("title", "message", NotificationTrigger.OnGrab, data, "MyInstance");
        var json = SerializeToJson(payload);

        json.TryGetProperty("eventId", out _).Should().BeTrue();
        json.TryGetProperty("downloadId", out _).Should().BeTrue();
        json.TryGetProperty("recordingId", out _).Should().BeTrue();
        json.TryGetProperty("applicationUrl", out _).Should().BeTrue();
        json.GetProperty("instanceName").GetString().Should().Be("MyInstance");
    }

    [Fact]
    public void NoFilePathOrSeriesPath_NoSeriesObjectAtAll()
    {
        var data = new NotificationEventData { HealthType = "DiskSpaceLow", HealthLevel = "Warning" };

        var payload = NotificationService.BuildWebhookPayload("Health issue", "msg", NotificationTrigger.OnHealthIssue, data, "Sportarr");
        var json = SerializeToJson(payload);

        json.TryGetProperty("series", out _).Should().BeFalse();
        json.TryGetProperty("episodeFile", out _).Should().BeFalse();
    }

    [Fact]
    public void RenameEvent_UsesSeriesPathBranch_NotFilePathBranch()
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
        json.TryGetProperty("eventId", out _).Should().BeFalse();
    }
}
