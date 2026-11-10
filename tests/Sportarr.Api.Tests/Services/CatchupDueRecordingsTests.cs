using System.Collections.Generic;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Xunit;

namespace Sportarr.Api.Tests.Services;

public class CatchupDueRecordingsTests
{
    private static SportarrDbContext CreateDb(string databaseName)
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new SportarrDbContext(options);
    }

    [Fact]
    public async Task DueCatchupRecordingsCarryTheirLeagueForTheOutputFolder()
    {
        var databaseName = Guid.NewGuid().ToString();

        using (var db = CreateDb(databaseName))
        {
            var league = new League
            {
                Name = "Formula 1",
                Sport = "Motorsport",
            };
            db.Leagues.Add(league);
            await db.SaveChangesAsync();

            var sportsEvent = new Event
            {
                Title = "Formula 1",
                Sport = "Motorsport",
                LeagueId = league.Id,
            };
            var source = new IptvSource { Name = "Catchup", Url = "http://test/m3u", IsActive = true };
            db.IptvSources.Add(source);
            await db.SaveChangesAsync();

            var channel = new IptvChannel
            {
                SourceId = source.Id,
                Name = "Catchup Test",
                StreamUrl = "http://test/catchup",
            };
            db.Events.Add(sportsEvent);
            db.IptvChannels.Add(channel);
            await db.SaveChangesAsync();

            db.DvrRecordings.Add(new DvrRecording
            {
                Title = "Catchup Recording",
                EventId = sportsEvent.Id,
                ChannelId = channel.Id,
                ScheduledStart = DateTime.UtcNow.AddHours(-2),
                ScheduledEnd = DateTime.UtcNow.AddHours(-1),
                Status = DvrRecordingStatus.Scheduled,
                Method = DvrRecordingMethod.Catchup,
            });

            db.DvrRecordings.Add(new DvrRecording
            {
                Title = "Live Recording",
                EventId = sportsEvent.Id,
                ChannelId = channel.Id,
                ScheduledStart = DateTime.UtcNow.AddHours(-2),
                ScheduledEnd = DateTime.UtcNow.AddHours(-1),
                Status = DvrRecordingStatus.Scheduled,
                Method = DvrRecordingMethod.Live,
            });

            await db.SaveChangesAsync();
        }

        // A fresh context has nothing tracked, so only the query loads the league.
        using var readDb = CreateDb(databaseName);
        var due = await CatchupDownloadService.LoadDueCatchupRecordingsAsync(readDb, CancellationToken.None);

        due.Should().HaveCount(1);
        due.Should().NotContain(r => r.Method == DvrRecordingMethod.Live);

        var recording = due[0];
        recording.Event.Should().NotBeNull();
        recording.Event!.League.Should().NotBeNull();
        recording.Event!.League!.Name.Should().Be("Formula 1");
    }
}
