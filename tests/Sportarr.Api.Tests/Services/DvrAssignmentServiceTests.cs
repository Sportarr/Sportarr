using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class DvrAssignmentServiceTests
{
    [Fact]
    public async Task PatchChangesOnlySuppliedFieldsAndReturnsBothStates()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DvrAssignmentService(fixture.Db, NullLogger<DvrAssignmentService>.Instance);

        var result = await service.UpdateAsync(fixture.RecordingId, new DvrAssignmentPatchRequest
        {
            ExpectedChannelId = 1,
            ChannelId = 2,
            FallbackChannelIds = [3],
        });

        result.Should().NotBeNull();
        result!.Previous.ChannelId.Should().Be(1);
        result.Current.ChannelId.Should().Be(2);
        result.Current.FallbackChannelIds.Should().Equal(3);
        result.Current.Quality.Should().Be("HDTV-1080p");
        result.Current.ScheduledStart.Should().Be(fixture.Start);
    }

    [Fact]
    public async Task StaleExpectedChannelLeavesAssignmentUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DvrAssignmentService(fixture.Db, NullLogger<DvrAssignmentService>.Instance);

        var act = () => service.UpdateAsync(fixture.RecordingId, new DvrAssignmentPatchRequest
        {
            ExpectedChannelId = 2,
            ChannelId = 3,
        });

        await act.Should().ThrowAsync<DvrAssignmentConflictException>();
        var saved = await fixture.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(1);
    }

    [Theory]
    [InlineData(1, new[] { 1 })]
    [InlineData(1, new[] { 2, 2 })]
    [InlineData(1, new[] { 4 })]
    public async Task InvalidFallbackListLeavesAssignmentUnchanged(int channelId, int[] fallbackIds)
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DvrAssignmentService(fixture.Db, NullLogger<DvrAssignmentService>.Instance);

        var act = () => service.UpdateAsync(fixture.RecordingId, new DvrAssignmentPatchRequest
        {
            ChannelId = channelId,
            FallbackChannelIds = fallbackIds.ToList(),
        });

        await act.Should().ThrowAsync<ArgumentException>();
        var saved = await fixture.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(1);
        saved.FallbackChannelIds.Should().BeNull();
    }

    [Fact]
    public async Task DisabledPrimaryLeavesAssignmentUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.IptvChannels.Add(new IptvChannel
        {
            Id = 5, SourceId = 1, Name = "Disabled", StreamUrl = "http://test/5", IsEnabled = false
        });
        await fixture.Db.SaveChangesAsync();
        var service = new DvrAssignmentService(fixture.Db, NullLogger<DvrAssignmentService>.Instance);

        var act = () => service.UpdateAsync(fixture.RecordingId, new DvrAssignmentPatchRequest { ChannelId = 5 });

        await act.Should().ThrowAsync<ArgumentException>();
        (await fixture.Db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelId.Should().Be(1);
    }

    [Fact]
    public async Task NonScheduledRecordingCannotBeChanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recording = await fixture.Db.DvrRecordings.SingleAsync();
        recording.Status = DvrRecordingStatus.Recording;
        await fixture.Db.SaveChangesAsync();
        var service = new DvrAssignmentService(fixture.Db, NullLogger<DvrAssignmentService>.Instance);

        var act = () => service.UpdateAsync(fixture.RecordingId, new DvrAssignmentPatchRequest { ChannelId = 2 });

        await act.Should().ThrowAsync<DvrAssignmentConflictException>();
        (await fixture.Db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelId.Should().Be(1);
    }

    [Fact]
    public async Task InvalidTimeWindowLeavesAssignmentUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DvrAssignmentService(fixture.Db, NullLogger<DvrAssignmentService>.Instance);

        var act = () => service.UpdateAsync(fixture.RecordingId, new DvrAssignmentPatchRequest
        {
            ChannelId = 2,
            ScheduledEnd = fixture.Start,
        });

        await act.Should().ThrowAsync<ArgumentException>();
        var saved = await fixture.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.ChannelId.Should().Be(1);
        saved.ScheduledEnd.Should().Be(fixture.Start.AddHours(2));
    }

    [Fact]
    public async Task InactiveSourceCannotSupplyAnAssignedChannel()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.IptvSources.Add(new IptvSource
        {
            Id = 2, Name = "Inactive", Url = "http://inactive", IsActive = false
        });
        fixture.Db.IptvChannels.Add(new IptvChannel
        {
            Id = 6, SourceId = 2, Name = "Unavailable", StreamUrl = "http://test/6"
        });
        await fixture.Db.SaveChangesAsync();
        var service = new DvrAssignmentService(fixture.Db, NullLogger<DvrAssignmentService>.Instance);

        var act = () => service.UpdateAsync(fixture.RecordingId, new DvrAssignmentPatchRequest
        {
            ChannelId = 6,
        });

        await act.Should().ThrowAsync<ArgumentException>();
        (await fixture.Db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelId.Should().Be(1);
    }

    [Fact]
    public async Task EmptyFallbackListClearsSavedFallbacksAndAppearsInNormalResponse()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recording = await fixture.Db.DvrRecordings.SingleAsync();
        recording.FallbackChannelIds = "[2,3]";
        await fixture.Db.SaveChangesAsync();
        var service = new DvrAssignmentService(fixture.Db, NullLogger<DvrAssignmentService>.Instance);

        var result = await service.UpdateAsync(fixture.RecordingId, new DvrAssignmentPatchRequest
        {
            FallbackChannelIds = [],
        });

        result!.Previous.FallbackChannelIds.Should().Equal(2, 3);
        result.Current.FallbackChannelIds.Should().BeEmpty();
        var saved = await fixture.Db.DvrRecordings.AsNoTracking().SingleAsync();
        saved.FallbackChannelIds.Should().BeNull();
        DvrRecordingResponse.FromEntity(saved).FallbackChannelIds.Should().BeEmpty();
    }

    [Fact]
    public async Task CatchupRecordingRejectsAChannelWithoutArchiveSupport()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recording = await fixture.Db.DvrRecordings.SingleAsync();
        recording.Method = DvrRecordingMethod.Catchup;
        await fixture.Db.SaveChangesAsync();
        var service = new DvrAssignmentService(fixture.Db, NullLogger<DvrAssignmentService>.Instance);

        var act = () => service.UpdateAsync(fixture.RecordingId, new DvrAssignmentPatchRequest
        {
            ChannelId = 2,
        });

        await act.Should().ThrowAsync<ArgumentException>();
        (await fixture.Db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelId.Should().Be(1);
    }

    [Fact]
    public async Task CatchupRecordingRejectsAnXtreamChannelWithoutArchive()
    {
        await using var fixture = await Fixture.CreateAsync();
        var recording = await fixture.Db.DvrRecordings.SingleAsync();
        recording.Method = DvrRecordingMethod.Catchup;
        var source = await fixture.Db.IptvSources.SingleAsync();
        source.Type = IptvSourceType.Xtream;
        source.Username = "test";
        source.Password = "test";
        var channel = await fixture.Db.IptvChannels.SingleAsync(c => c.Id == 2);
        channel.StreamUrl = "http://test/live/test/test/2.ts";
        channel.HasArchive = false;
        await fixture.Db.SaveChangesAsync();
        var service = new DvrAssignmentService(fixture.Db, NullLogger<DvrAssignmentService>.Instance);

        var act = () => service.UpdateAsync(fixture.RecordingId, new DvrAssignmentPatchRequest
        {
            ChannelId = 2,
        });

        await act.Should().ThrowAsync<ArgumentException>();
        (await fixture.Db.DvrRecordings.AsNoTracking().SingleAsync()).ChannelId.Should().Be(1);
    }

    [Fact]
    public async Task PatchNormalizesUnspecifiedTimeToUtc()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DvrAssignmentService(fixture.Db, NullLogger<DvrAssignmentService>.Instance);

        var result = await service.UpdateAsync(fixture.RecordingId, new DvrAssignmentPatchRequest
        {
            ScheduledStart = DateTime.SpecifyKind(fixture.Start.AddMinutes(30), DateTimeKind.Unspecified),
        });

        result!.Current.ScheduledStart.Kind.Should().Be(DateTimeKind.Utc);
        result.Current.ScheduledStart.Should().Be(fixture.Start.AddMinutes(30));
    }

    [Fact]
    public async Task PastLiveWindowCannotBeAssigned()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = new DvrAssignmentService(fixture.Db, NullLogger<DvrAssignmentService>.Instance);

        var act = () => service.UpdateAsync(fixture.RecordingId, new DvrAssignmentPatchRequest
        {
            ScheduledStart = DateTime.UtcNow.AddHours(-3),
            ScheduledEnd = DateTime.UtcNow.AddHours(-2),
        });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void MalformedStoredFallbackListDoesNotBreakNormalRecordingResponse()
    {
        var response = DvrRecordingResponse.FromEntity(new DvrRecording
        {
            Title = "Test", ChannelId = 1, FallbackChannelIds = "not json",
        });

        response.FallbackChannelIds.Should().BeEmpty();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public SportarrDbContext Db { get; }
        public int RecordingId { get; }
        public DateTime Start { get; }

        private Fixture(SqliteConnection connection, SportarrDbContext db, int recordingId, DateTime start)
        {
            _connection = connection;
            Db = db;
            RecordingId = recordingId;
            Start = start;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.IptvSources.Add(new IptvSource { Id = 1, Name = "Source", Url = "http://test", IsActive = true });
            db.IptvChannels.AddRange(
                new IptvChannel { Id = 1, SourceId = 1, Name = "First", StreamUrl = "http://test/1" },
                new IptvChannel { Id = 2, SourceId = 1, Name = "Second", StreamUrl = "http://test/2" },
                new IptvChannel { Id = 3, SourceId = 1, Name = "Third", StreamUrl = "http://test/3" },
                new IptvChannel { Id = 4, SourceId = 1, Name = "Inactive", StreamUrl = "http://test/4", IsEnabled = false });
            var start = new DateTime(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc);
            var recording = new DvrRecording
            {
                Title = "Test", ChannelId = 1, Quality = "HDTV-1080p",
                ScheduledStart = start, ScheduledEnd = start.AddHours(2),
                Status = DvrRecordingStatus.Scheduled,
            };
            db.DvrRecordings.Add(recording);
            await db.SaveChangesAsync();
            return new Fixture(connection, db, recording.Id, start);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
