namespace Sportarr.Api.Models;

/// <summary>
/// Structured event data for a notification trigger, replacing the old ad
/// hoc <c>Dictionary&lt;string, object&gt;</c> metadata bag that used to be
/// built by hand at every call site with no shared shape. Every property is
/// optional; a given trigger's producer only sets the ones relevant to it.
/// See <see cref="NotificationService"/>.BuildWebhookPayload for how this
/// becomes the actual Webhook/Notifiarr JSON body - null properties are
/// omitted from the wire payload entirely rather than sent as null noise,
/// so each event type's JSON only carries the fields that actually apply
/// to it.
/// </summary>
public class NotificationEventData
{
    // Canonical event identity. EventId is Sportarr's own internal database
    // primary key - NOT a TheSportsDB id and not comparable to a
    // tvdb/tmdb/imdb id. EventExternalId is that external metadata id (the
    // hub short_id, e.g. "ev-848683") when the event has one.
    public int? EventId { get; set; }
    public string? EventExternalId { get; set; }
    public string? EventTitle { get; set; }
    public string? League { get; set; }
    public string? Sport { get; set; }

    // Release / file facts. Size is always a raw byte count (never
    // pre-rounded), present on both Grab and Download/Upgrade so it's
    // available as real data instead of only inside the human message text.
    public string? Quality { get; set; }
    public long? Size { get; set; }
    public string? Indexer { get; set; }
    public string? DownloadId { get; set; }
    public bool? IsUpgrade { get; set; }

    // Download/Upgrade/Delete carry a single file's path. Rename carries the
    // covering directory of a batch of renamed files instead (there's no
    // single file to point at), which is why this is a distinct field
    // rather than overloading FilePath.
    public string? FilePath { get; set; }
    public string? SeriesPath { get; set; }
    public int? RenamedCount { get; set; }

    // Health
    public string? HealthType { get; set; }
    public string? HealthLevel { get; set; }

    // Application update
    public string? PreviousVersion { get; set; }
    public string? NewVersion { get; set; }

    // Manual interaction required
    public string? DownloadTitle { get; set; }
    public string? DownloadClientName { get; set; }
    public int? Confidence { get; set; }
    public int? PendingCount { get; set; }

    // DVR recording
    public int? RecordingId { get; set; }
    public string? RecordingTitle { get; set; }
    public int? ChannelId { get; set; }

    /// <summary>
    /// Flattens the set properties to a dictionary for CustomScript's
    /// SPORTARR_{KEY} environment-variable passthrough, which needs a
    /// key/value walk rather than a typed object.
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>();
        if (EventId.HasValue) dict["eventId"] = EventId.Value;
        if (EventExternalId != null) dict["eventExternalId"] = EventExternalId;
        if (EventTitle != null) dict["eventTitle"] = EventTitle;
        if (League != null) dict["league"] = League;
        if (Sport != null) dict["sport"] = Sport;
        if (Quality != null) dict["quality"] = Quality;
        if (Size.HasValue) dict["size"] = Size.Value;
        if (Indexer != null) dict["indexer"] = Indexer;
        if (DownloadId != null) dict["downloadId"] = DownloadId;
        if (IsUpgrade.HasValue) dict["isUpgrade"] = IsUpgrade.Value;
        if (FilePath != null) dict["filePath"] = FilePath;
        if (SeriesPath != null) dict["seriesPath"] = SeriesPath;
        if (RenamedCount.HasValue) dict["renamedCount"] = RenamedCount.Value;
        if (HealthType != null) dict["healthType"] = HealthType;
        if (HealthLevel != null) dict["healthLevel"] = HealthLevel;
        if (PreviousVersion != null) dict["previousVersion"] = PreviousVersion;
        if (NewVersion != null) dict["newVersion"] = NewVersion;
        if (DownloadTitle != null) dict["downloadTitle"] = DownloadTitle;
        if (DownloadClientName != null) dict["client"] = DownloadClientName;
        if (Confidence.HasValue) dict["confidence"] = Confidence.Value;
        if (PendingCount.HasValue) dict["pendingCount"] = PendingCount.Value;
        if (RecordingId.HasValue) dict["recordingId"] = RecordingId.Value;
        if (RecordingTitle != null) dict["recordingTitle"] = RecordingTitle;
        if (ChannelId.HasValue) dict["channelId"] = ChannelId.Value;
        return dict;
    }
}

/// <summary>
/// The actual JSON body sent to the Webhook and Notifiarr providers (both
/// use this same shape - see NotificationService.BuildWebhookPayload).
/// Serialized with camelCase property names and nulls omitted, so a given
/// event type's payload only contains the fields that apply to it instead
/// of a full field list padded with nulls.
/// </summary>
public class WebhookPayload
{
    public required string EventType { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public string ApplicationUrl { get; set; } = "";
    public string InstanceName { get; set; } = "Sportarr";

    public int? EventId { get; set; }
    public string? EventExternalId { get; set; }
    public string? EventTitle { get; set; }
    public string? League { get; set; }
    public string? Sport { get; set; }

    public string? Quality { get; set; }
    public long? Size { get; set; }
    public string? Indexer { get; set; }
    public string? DownloadId { get; set; }
    public bool? IsUpgrade { get; set; }

    public string? FilePath { get; set; }
    public string? SeriesPath { get; set; }
    public int? RenamedCount { get; set; }

    public string? HealthType { get; set; }
    public string? HealthLevel { get; set; }
    public string? PreviousVersion { get; set; }
    public string? NewVersion { get; set; }
    public string? DownloadTitle { get; set; }
    public string? DownloadClientName { get; set; }
    public int? Confidence { get; set; }
    public int? PendingCount { get; set; }
    public int? RecordingId { get; set; }
    public string? RecordingTitle { get; set; }
    public int? ChannelId { get; set; }

    /// <summary>
    /// Sonarr-webhook-shape compatibility for path-driven consumers (e.g.
    /// Autoscan, which rescans path.Dir(path.Join(series.path,
    /// episodeFile.relativePath))). Derived directly from FilePath/
    /// SeriesPath/EventTitle above at build time - never independently
    /// set, so it can't drift from the flat fields it mirrors the way the
    /// old dictionary re-read could.
    /// </summary>
    public WebhookSeriesInfo? Series { get; set; }
    public WebhookEpisodeFileInfo? EpisodeFile { get; set; }
}

public class WebhookSeriesInfo
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
}

public class WebhookEpisodeFileInfo
{
    public string RelativePath { get; set; } = "";
    public string Path { get; set; } = "";
}
