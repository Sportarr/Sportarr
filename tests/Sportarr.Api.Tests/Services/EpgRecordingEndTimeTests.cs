using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// When the guide and the sports API disagree about the start, the recording
/// covers both ends. Taking the guide's end alone cut the recording short
/// whenever the guide listed a pre-show under the event's own title.
/// </summary>
public class EpgRecordingEndTimeTests
{
    private static SportarrDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SportarrDbContext(options);
    }

    private static readonly DateTime ApiStart = new(2026, 8, 24, 19, 0, 0, DateTimeKind.Utc);

    private static Event Fixture() => new()
    {
        Id = 1,
        Title = "Chicago Cubs vs St Louis Cardinals",
        Sport = "Baseball",
        EventDate = ApiStart,
    };

    private static IptvChannel Channel() => new()
    {
        Id = 1,
        SourceId = 1,
        Name = "Marquee",
        StreamUrl = "http://test/1",
        TvgId = "marquee.us",
    };

    private static EpgProgram Program(DateTime start, DateTime end) => new()
    {
        Id = 1,
        EpgSourceId = 1,
        ChannelId = "marquee.us",
        Title = "Chicago Cubs vs St Louis Cardinals",
        StartTime = start,
        EndTime = end,
    };

    private static EpgSchedulingService CreateService(SportarrDbContext db) =>
        new(Mock.Of<ILogger<EpgSchedulingService>>(), db);

    [Fact]
    public async Task A_guide_entry_starting_early_and_ending_early_does_not_cut_the_event_short()
    {
        using var db = CreateDb();
        // The guide lists a pre-show shape: 45 minutes early, over before the
        // event's own three hours are.
        db.EpgPrograms.Add(Program(ApiStart.AddMinutes(-45), ApiStart.AddMinutes(75)));
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetOptimizedRecordingTimesAsync(
            Fixture(), Channel(), prePaddingMinutes: 0, postPaddingMinutes: 0);

        result.UsedEpgData.Should().BeTrue();
        var apiEnd = ApiStart.AddHours(EpgSchedulingService.DefaultDurationHours);
        result.OptimizedEndTime.Should().BeOnOrAfter(apiEnd,
            "the recording must run to the event's own end, not the pre-show's");
    }

    [Fact]
    public async Task A_guide_entry_agreeing_on_the_start_keeps_the_guide_end()
    {
        using var db = CreateDb();
        // Same start, guide says the broadcast runs 3.5 hours. The guide is
        // the better source when the two agree on when things begin.
        var guideEnd = ApiStart.AddMinutes(210);
        db.EpgPrograms.Add(Program(ApiStart, guideEnd));
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetOptimizedRecordingTimesAsync(
            Fixture(), Channel(), prePaddingMinutes: 0, postPaddingMinutes: 0);

        result.UsedEpgData.Should().BeTrue();
        result.OptimizedEndTime.Should().Be(guideEnd);
    }

    [Fact]
    public async Task No_guide_data_falls_back_to_the_default_duration()
    {
        using var db = CreateDb();

        var result = await CreateService(db).GetOptimizedRecordingTimesAsync(
            Fixture(), Channel(), prePaddingMinutes: 0, postPaddingMinutes: 0);

        result.UsedEpgData.Should().BeFalse();
        result.OriginalEndTime.Should().Be(ApiStart.AddHours(EpgSchedulingService.DefaultDurationHours));
    }
}
