using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

public sealed class DvrAssignmentPatchRequest
{
    public int? ExpectedChannelId { get; set; }
    public int? ChannelId { get; set; }
    public List<int>? FallbackChannelIds { get; set; }
    public DateTime? ScheduledStart { get; set; }
    public DateTime? ScheduledEnd { get; set; }
    public string? Quality { get; set; }
}

public sealed record DvrAssignmentSnapshot(
    int ChannelId,
    IReadOnlyList<int> FallbackChannelIds,
    DateTime ScheduledStart,
    DateTime ScheduledEnd,
    string? Quality);

public sealed record DvrAssignmentChangeResponse(
    int RecordingId,
    DvrAssignmentSnapshot Previous,
    DvrAssignmentSnapshot Current);

public sealed class DvrAssignmentConflictException(string message) : Exception(message);

public sealed class DvrAssignmentService(
    SportarrDbContext db,
    ILogger<DvrAssignmentService> logger)
{
    public async Task<DvrAssignmentChangeResponse?> UpdateAsync(
        int id, DvrAssignmentPatchRequest request, CancellationToken cancellationToken = default)
    {
        var recording = await db.DvrRecordings.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (recording == null)
            return null;
        if (recording.Status != DvrRecordingStatus.Scheduled)
            throw new DvrAssignmentConflictException("Only scheduled recordings can be reassigned.");
        if (request.ExpectedChannelId.HasValue && request.ExpectedChannelId != recording.ChannelId)
            throw new DvrAssignmentConflictException("The recording channel changed. Refresh and try again.");

        var previousFallbacks = ParseFallbacks(recording.FallbackChannelIds);
        var previous = Snapshot(recording, previousFallbacks);
        var channelId = request.ChannelId ?? recording.ChannelId;
        var fallbackIds = request.FallbackChannelIds ?? previousFallbacks;
        var start = NormalizeToUtc(request.ScheduledStart ?? recording.ScheduledStart);
        var end = NormalizeToUtc(request.ScheduledEnd ?? recording.ScheduledEnd);
        var quality = request.Quality ?? recording.Quality;

        if (start >= end)
            throw new ArgumentException("Recording start time must be before end time.");
        if (recording.Method != DvrRecordingMethod.Catchup &&
            end.AddMinutes(recording.PostPadding) <= DateTime.UtcNow)
            throw new ArgumentException("The scheduled window is already in the past.");
        if (fallbackIds.Contains(channelId))
            throw new ArgumentException("The primary channel cannot also be a fallback.");
        if (fallbackIds.Count != fallbackIds.Distinct().Count())
            throw new ArgumentException("Fallback channels must be unique.");

        var channelIds = fallbackIds.Prepend(channelId).Distinct().ToList();
        var channels = await db.IptvChannels.AsNoTracking()
            .Include(channel => channel.Source)
            .Where(channel => channelIds.Contains(channel.Id)
                && channel.IsEnabled
                && channel.StreamUrl != ""
                && channel.Source != null
                && channel.Source.IsActive)
            .ToListAsync(cancellationToken);
        if (channels.Count != channelIds.Count)
            throw new ArgumentException("Every assigned channel must exist, be enabled, and have an active source.");
        if (recording.Method == DvrRecordingMethod.Catchup && channels.Any(channel =>
                !channel.HasArchive ||
                channel.Source!.Type != IptvSourceType.Xtream ||
                string.IsNullOrWhiteSpace(channel.Source.Username) ||
                string.IsNullOrWhiteSpace(channel.Source.Password) ||
                !XtreamCodesClient.TryParseStreamId(channel.StreamUrl, out _)))
            throw new ArgumentException("Catchup assignments require Xtream channels with credentials and valid stream IDs.");

        var fallbackJson = fallbackIds.Count == 0 ? null : JsonSerializer.Serialize(fallbackIds);
        var updatedAt = DateTime.UtcNow;
        var changed = await db.DvrRecordings
            .Where(r => r.Id == id
                && r.Status == DvrRecordingStatus.Scheduled
                && r.ChannelId == recording.ChannelId
                && r.FallbackChannelIds == recording.FallbackChannelIds
                && r.ScheduledStart == recording.ScheduledStart
                && r.ScheduledEnd == recording.ScheduledEnd
                && r.Quality == recording.Quality)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.ChannelId, channelId)
                .SetProperty(r => r.FallbackChannelIds, fallbackJson)
                .SetProperty(r => r.ScheduledStart, start)
                .SetProperty(r => r.ScheduledEnd, end)
                .SetProperty(r => r.Quality, quality)
                .SetProperty(r => r.LastUpdated, updatedAt), cancellationToken);
        if (changed != 1)
            throw new DvrAssignmentConflictException("The recording changed. Refresh and try again.");

        logger.LogInformation("[DVR] Updated assignment for recording {Id}: channel {ChannelId}", id, channelId);
        return new DvrAssignmentChangeResponse(id, previous,
            new DvrAssignmentSnapshot(channelId, fallbackIds, start, end, quality));
    }

    private static DvrAssignmentSnapshot Snapshot(DvrRecording recording, List<int> fallbackIds) =>
        new(recording.ChannelId, fallbackIds, recording.ScheduledStart,
            recording.ScheduledEnd, recording.Quality);

    private static DateTime NormalizeToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public static List<int> ParseFallbacks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? [];
        }
        catch (JsonException)
        {
            throw new DvrAssignmentConflictException("The saved fallback channel list is invalid.");
        }
    }
}
