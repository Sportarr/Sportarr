namespace Sportarr.Api.Models;

/// <summary>
/// One file's path/quality/size, reused for both "the file this event is
/// about" (EpisodeFile) and "file(s) that no longer exist as of this
/// event" (DeletedFiles) - mirrors Sonarr's WebhookEpisodeFile, which is
/// reused the same way across their Import and Delete payloads.
/// </summary>
public class NotificationFileData
{
    public required string Path { get; set; }
    public string? Quality { get; set; }
    public long? Size { get; set; }
}

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
    // Canonical event identity -> WebhookPayload.Series. EventId is
    // Sportarr's own internal database primary key - NOT a TheSportsDB id
    // and not comparable to a tvdb/tmdb/imdb id. EventExternalId is that
    // external metadata id (the hub short_id, e.g. "ev-848683") when the
    // event has one.
    public int? EventId { get; set; }
    public string? EventExternalId { get; set; }
    // TheSportsDB numeric id, resolved once at dispatch time. Lets
    // consumers cross-reference the source directly.
    public string? TsdbId { get; set; }
    public string? EventTitle { get; set; }
    public string? League { get; set; }
    public string? Sport { get; set; }

    // Rename only: the covering directory of a batch of renamed files -
    // there's no single file to point at, so this feeds Series.Path
    // directly instead of going through File/DeletedFiles.
    public string? SeriesPath { get; set; }
    public int? RenamedCount { get; set; }

    // Grab -> WebhookPayload.Release. Quality/Size here are the release's
    // OWN claim (a torrent/nzb title's stated quality and advertised
    // size), not a probed file - kept as separate concepts from File
    // below, same distinction Sonarr's WebhookRelease vs WebhookEpisodeFile
    // makes.
    public string? Indexer { get; set; }
    public string? DownloadId { get; set; }
    public string? Quality { get; set; }
    public long? Size { get; set; }

    // Download/Upgrade: the file this event is actually about (the newly
    // imported file) -> WebhookPayload.EpisodeFile.
    public NotificationFileData? File { get; set; }
    public bool? IsUpgrade { get; set; }

    // Upgrade's replaced file(s), or a pure delete's removed file(s) ->
    // WebhookPayload.DeletedFiles. Deliberately the SAME field for both
    // cases (rather than a separate single-file concept for delete) so
    // consumers handle "stuff that stopped existing" one way everywhere.
    public List<NotificationFileData>? DeletedFiles { get; set; }

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
    /// key/value walk rather than a typed object. Nested file objects are
    /// flattened with an index suffix (SPORTARR_DELETEDFILES_0_PATH, ...)
    /// since env vars can't carry structure.
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>();
        if (EventId.HasValue) dict["eventId"] = EventId.Value;
        if (EventExternalId != null) dict["eventExternalId"] = EventExternalId;
        // Webhook consumers were handed this and custom scripts were not, so a
        // script had no reliable way to cross-reference the event against the
        // source everyone else was using.
        if (TsdbId != null) dict["tsdbId"] = TsdbId;
        if (EventTitle != null) dict["eventTitle"] = EventTitle;
        if (League != null) dict["league"] = League;
        if (Sport != null) dict["sport"] = Sport;
        if (SeriesPath != null) dict["seriesPath"] = SeriesPath;
        if (RenamedCount.HasValue) dict["renamedCount"] = RenamedCount.Value;
        if (Indexer != null) dict["indexer"] = Indexer;
        if (DownloadId != null) dict["downloadId"] = DownloadId;
        if (Quality != null) dict["quality"] = Quality;
        if (Size.HasValue) dict["size"] = Size.Value;
        if (IsUpgrade.HasValue) dict["isUpgrade"] = IsUpgrade.Value;
        if (File != null)
        {
            dict["filePath"] = File.Path;
            if (File.Quality != null) dict["fileQuality"] = File.Quality;
            if (File.Size.HasValue) dict["fileSize"] = File.Size.Value;
        }
        if (DeletedFiles != null)
        {
            for (var i = 0; i < DeletedFiles.Count; i++)
            {
                dict[$"deletedFiles_{i}_path"] = DeletedFiles[i].Path;
                if (DeletedFiles[i].Quality != null) dict[$"deletedFiles_{i}_quality"] = DeletedFiles[i].Quality!;
                if (DeletedFiles[i].Size.HasValue) dict[$"deletedFiles_{i}_size"] = DeletedFiles[i].Size!.Value;
            }
        }
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
/// Mirrors Sonarr's real webhook payload shapes (WebhookSeries/
/// WebhookRelease/WebhookEpisodeFile/DeletedFiles) rather than Sportarr's
/// own earlier flat-field design, per direct feedback from an integration
/// partner who builds against both. Serialized with camelCase property
/// names and nulls omitted, so a given trigger's payload only contains the
/// objects that apply to it.
/// </summary>
public class WebhookPayload
{
    public required string EventType { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public string ApplicationUrl { get; set; } = "";
    public string InstanceName { get; set; } = "Sportarr";

    /// <summary>Event/league/sport identity - present on every event-scoped trigger.</summary>
    public WebhookSeriesInfo? Series { get; set; }

    /// <summary>Grab only: the release that was grabbed.</summary>
    public WebhookReleaseInfo? Release { get; set; }

    /// <summary>Download/Upgrade only: the newly imported file.</summary>
    public WebhookFileInfo? EpisodeFile { get; set; }

    /// <summary>
    /// Upgrade's replaced file(s), or a pure delete's removed file(s) -
    /// same field either way so consumers handle "this stopped existing"
    /// one way regardless of which trigger produced it.
    /// </summary>
    public List<WebhookFileInfo>? DeletedFiles { get; set; }

    public string? DownloadId { get; set; }
    public bool? IsUpgrade { get; set; }

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
}

/// <summary>Mirrors Sonarr's WebhookSeries - every identity field for the event nested in one place.</summary>
public class WebhookSeriesInfo
{
    public int? Id { get; set; }
    public string? ExternalId { get; set; }
    public string? TsdbId { get; set; }
    public string Title { get; set; } = "";
    public string? Path { get; set; }
    public string? League { get; set; }
    public string? Sport { get; set; }
}

/// <summary>Mirrors Sonarr's WebhookRelease - the grabbed release's own claimed facts, not a probed file.</summary>
public class WebhookReleaseInfo
{
    public string? Quality { get; set; }
    public string? Indexer { get; set; }
    public long? Size { get; set; }
}

/// <summary>Mirrors Sonarr's WebhookEpisodeFile - reused for both EpisodeFile and each entry in DeletedFiles.</summary>
public class WebhookFileInfo
{
    public string RelativePath { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Quality { get; set; }
    public long? Size { get; set; }
}
