using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The looping-stream check stops a recording only when a fallback channel
/// can take over. These tests use real channel rows to check which fallbacks
/// count.
/// </summary>
public class DvrUsableFallbackTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SportarrDbContext _db = null!;
    private DvrRecordingService _service = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();

        // One stream per source, so the looping recording fills its source.
        _db.IptvSources.Add(new IptvSource { Id = 1, Name = "Source", Url = "http://test", IsActive = true, MaxStreams = 1 });
        _db.IptvChannels.AddRange(
            new IptvChannel { Id = 1, SourceId = 1, Name = "Looping", StreamUrl = "http://test/1" },
            new IptvChannel { Id = 2, SourceId = 1, Name = "Backup", StreamUrl = "http://test/2", Status = IptvChannelStatus.Online });
        await _db.SaveChangesAsync();

        // The constructor needs the whole app. The fallback check uses only
        // the database and the logger.
        _service = (DvrRecordingService)RuntimeHelpers.GetUninitializedObject(typeof(DvrRecordingService));
        typeof(DvrRecordingService).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_service, _db);
        typeof(DvrRecordingService).GetField("_logger", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_service, NullLogger<DvrRecordingService>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_healthy_fallback_on_the_full_source_is_usable()
    {
        // The looping recording holds the only stream of the source. The stop
        // frees that stream, so the fallback can still take over.
        var recording = await RecordingAsync(fallbacks: "[2]");

        (await _service.HasUsableFallbackAsync(recording)).Should().BeTrue();
    }

    [Theory]
    [InlineData(IptvChannelStatus.Offline)]
    [InlineData(IptvChannelStatus.Error)]
    public async Task A_fallback_known_to_be_dead_is_not_usable(IptvChannelStatus status)
    {
        (await _db.IptvChannels.SingleAsync(c => c.Id == 2)).Status = status;
        await _db.SaveChangesAsync();
        var recording = await RecordingAsync(fallbacks: "[2]");

        (await _service.HasUsableFallbackAsync(recording)).Should().BeFalse();
    }

    [Fact]
    public async Task No_fallback_is_usable_once_the_retries_are_spent()
    {
        var recording = await RecordingAsync(fallbacks: "[2]", retries: DvrRecordingService.MaxAutoRetries);

        (await _service.HasUsableFallbackAsync(recording)).Should().BeFalse();
    }

    [Fact]
    public async Task A_fallback_on_a_source_that_another_recording_fills_is_not_usable()
    {
        _db.IptvSources.Add(new IptvSource { Id = 2, Name = "Other", Url = "http://other", IsActive = true, MaxStreams = 1 });
        _db.IptvChannels.Add(new IptvChannel { Id = 3, SourceId = 2, Name = "Other backup", StreamUrl = "http://other/3" });
        _db.IptvChannels.Add(new IptvChannel { Id = 4, SourceId = 2, Name = "Other busy", StreamUrl = "http://other/4" });
        _db.DvrRecordings.Add(new DvrRecording
        {
            Title = "Another event", ChannelId = 4, Status = DvrRecordingStatus.Recording,
            ScheduledStart = DateTime.UtcNow, ScheduledEnd = DateTime.UtcNow.AddHours(1),
        });
        await _db.SaveChangesAsync();
        var recording = await RecordingAsync(fallbacks: "[3]");

        (await _service.HasUsableFallbackAsync(recording)).Should().BeFalse();
    }

    [Fact]
    public async Task A_recording_without_fallbacks_has_none_usable()
    {
        var recording = await RecordingAsync(fallbacks: null);

        (await _service.HasUsableFallbackAsync(recording)).Should().BeFalse();
    }

    private async Task<DvrRecording> RecordingAsync(string? fallbacks, int retries = 0)
    {
        var recording = new DvrRecording
        {
            Title = "Looping event",
            ChannelId = 1,
            Status = DvrRecordingStatus.Recording,
            ScheduledStart = DateTime.UtcNow.AddMinutes(-30),
            ScheduledEnd = DateTime.UtcNow.AddHours(1),
            FallbackChannelIds = fallbacks,
            AutoRetryCount = retries,
        };
        _db.DvrRecordings.Add(recording);
        await _db.SaveChangesAsync();
        return recording;
    }
}
