using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Issue #358: monitored events that start in the future have no file yet, so
/// the Library card counted them as Missing. They are now counted as Upcoming.
/// </summary>
public class LeagueUpcomingCountTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static League Formula1() => new() { Id = 1, Name = "Formula 1", Sport = "Motorsport" };

    [Fact]
    public void Upcoming_EventsAreNotMissing()
    {
        // 1185 monitored, 1125 with a file, 60 without. All 60 start later.
        var response = LeagueResponse.FromLeague(Formula1(), 1185, 1185, 1125, 1125, true, upcomingCount: 60);

        response.MonitoredEventCount.Should().Be(1185);
        response.FileCount.Should().Be(1125);
        response.MissingCount.Should().Be(0);
        response.UpcomingCount.Should().Be(60);
    }

    [Fact]
    public void Missing_AndUpcoming_AddUpToMonitoredWithoutFile()
    {
        var response = LeagueResponse.FromLeague(Formula1(), 100, 100, 90, 90, true, upcomingCount: 4);

        response.MissingCount.Should().Be(6);
        response.UpcomingCount.Should().Be(4);
        (response.MissingCount + response.UpcomingCount + response.DownloadedMonitoredCount)
            .Should().Be(response.MonitoredEventCount);
    }

    [Fact]
    public void WithoutUpcoming_MissingKeepsItsOldMeaning()
    {
        var response = LeagueResponse.FromLeague(Formula1(), 10, 10, 7, 7);

        response.MissingCount.Should().Be(3);
        response.UpcomingCount.Should().Be(0);
    }

    [Fact]
    public void Progress_IgnoresUpcomingEvents()
    {
        var response = LeagueResponse.FromLeague(Formula1(), 1185, 1185, 1125, 1125, true, upcomingCount: 60);

        response.ProgressPercent.Should().Be(100);
        response.ProgressStatus.Should().Be("continuing");
    }

    [Fact]
    public void Progress_StillCountsPastEventsWithoutAFile()
    {
        var response = LeagueResponse.FromLeague(Formula1(), 10, 10, 6, 6, true, upcomingCount: 2);

        response.MissingCount.Should().Be(2);
        response.ProgressPercent.Should().Be(75);
        response.ProgressStatus.Should().Be("partial");
    }

    [Fact]
    public void OnlyUpcomingEvents_IsNotReportedAsMissingOrUnmonitored()
    {
        var response = LeagueResponse.FromLeague(Formula1(), 5, 5, 0, 0, true, upcomingCount: 5);

        response.MissingCount.Should().Be(0);
        response.ProgressStatus.Should().Be("continuing");
    }

    [Fact]
    public void UpcomingCount_CannotExceedMonitoredEventsWithoutAFile()
    {
        var response = LeagueResponse.FromLeague(Formula1(), 10, 10, 9, 9, true, upcomingCount: 5);

        response.UpcomingCount.Should().Be(1);
        response.MissingCount.Should().Be(0);
    }

    [Fact]
    public async Task Query_CountsOnlyEventsThatStartAfterNow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection)
            .Options);
        await db.Database.EnsureCreatedAsync();

        db.Leagues.Add(Formula1());
        db.Leagues.Add(new League { Id = 2, Name = "Other", Sport = "Motorsport" });
        var id = 0;
        Event Ev(int league, DateTime date, bool monitored, bool hasFile) => new()
        {
            Id = ++id,
            Title = $"Race {id}",
            Sport = "Motorsport",
            LeagueId = league,
            Season = "2026",
            EventDate = date,
            Monitored = monitored,
            HasFile = hasFile,
        };

        db.Events.AddRange(
            Ev(1, Now.AddDays(-10), true, true),      // have
            Ev(1, Now.AddDays(-5), true, false),      // missing
            Ev(1, Now.AddHours(-1), true, false),     // live or just started, missing
            Ev(1, Now, true, false),                  // starts exactly now, missing
            Ev(1, Now.AddMinutes(1), true, false),    // upcoming
            Ev(1, Now.AddDays(30), true, false),      // upcoming
            Ev(1, Now.AddDays(30), false, false),     // not monitored, not counted
            Ev(1, Now.AddDays(-3), false, true),      // unmonitored with a file
            Ev(2, Now.AddDays(7), true, false));      // other league
        await db.SaveChangesAsync();

        var stats = await LeagueEndpoints.GetLibraryStatsAsync(db, new List<int?> { 1, 2 }, Now);
        var s = stats[1];
        var response = LeagueResponse.FromLeague(
            Formula1(), s.EventCount, s.MonitoredEventCount, s.FileCount,
            s.DownloadedMonitoredCount, s.HasFutureEvents, s.UpcomingCount);

        response.EventCount.Should().Be(8);
        response.MonitoredEventCount.Should().Be(6);
        response.FileCount.Should().Be(2);
        response.UpcomingCount.Should().Be(2);
        response.MissingCount.Should().Be(3);
        stats[2].UpcomingCount.Should().Be(1);
    }
}
