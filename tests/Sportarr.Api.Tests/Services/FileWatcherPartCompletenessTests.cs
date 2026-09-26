using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class FileWatcherPartCompletenessTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DeletingSelectedFileKeepsSurvivorAndMonitoring(bool fighting, bool complete)
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new SportarrDbContext(options);
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(new ImportFileSuppressionService());
        services.AddSingleton(new ConfigService(configuration, Mock.Of<ILogger<ConfigService>>()));
        using var provider = services.BuildServiceProvider();

        var deletedPath = "/isolated/deleted-" + Guid.NewGuid().ToString("N") + ".mkv";
        var survivorPath = "/isolated/survivor-" + Guid.NewGuid().ToString("N") + ".mkv";
        var league = new League { Id = 1, Name = fighting ? "UFC" : "NFL",
            Sport = fighting ? "Fighting" : "American Football" };
        var evt = new Event
        {
            Id = 1,
            League = league,
            LeagueId = league.Id,
            Title = fighting ? "UFC Fight Night 999" : "HOU vs BUF",
            Sport = league.Sport,
            EventDate = new DateTime(2026, 9, 13, 17, 0, 0, DateTimeKind.Utc),
            Monitored = true,
            HasFile = true,
            FilePath = deletedPath
        };
        evt.Files.Add(new EventFile { Id = 1, Event = evt, EventId = evt.Id,
            FilePath = deletedPath, Exists = true, PartNumber = fighting ? 2 : null });
        evt.Files.Add(new EventFile { Id = 2, Event = evt, EventId = evt.Id,
            FilePath = survivorPath, Exists = true, PartNumber = fighting ? 1 : null });
        db.AddRange(league, evt, new MediaManagementSettings { UnmonitorDeletedEvents = true });
        await db.SaveChangesAsync();

        var watcher = new FileWatcherService(provider, Mock.Of<ILogger<FileWatcherService>>());
        var method = typeof(FileWatcherService).GetMethod("HandleDeletedFileAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(watcher, new object[] { deletedPath })!;

        db.ChangeTracker.Clear();
        var persisted = await db.Events.SingleAsync();
        persisted.HasFile.Should().Be(complete);
        persisted.Monitored.Should().BeTrue();
        persisted.FilePath.Should().Be(survivorPath);
        (await db.EventFiles.SingleAsync(file => file.Id == 1)).Exists.Should().BeFalse();
    }
}
