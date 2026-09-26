using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public sealed class SearchQueueLeaguePartTests
{
    [Fact]
    public async Task RejectsCardPartForSinglePartLeagueBeforeQueuing()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .Options;
        const int eventId = 183;
        await using (var seed = new SportarrDbContext(options))
        {
            var league = new League { Id = 27, Name = "ACA", Sport = "Fighting" };
            seed.Leagues.Add(league);
            seed.Events.Add(new Event
            {
                Id = eventId,
                LeagueId = league.Id,
                Title = "ACA 183",
                Sport = "Fighting",
                EventDate = new DateTime(2026, 7, 12),
                Monitored = true
            });
            await seed.SaveChangesAsync();
        }

        await using var searchDb = new SportarrDbContext(options);
        using var services = new ServiceCollection().AddSingleton(searchDb).BuildServiceProvider();
        var queue = new SearchQueueService(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SearchQueueService>.Instance);

        var item = await queue.QueueSearchAsync(eventId, "Main Card");

        item.Status.Should().Be(SearchQueueStatus.Failed);
        (await searchDb.Tasks.CountAsync()).Should().Be(0);
    }
}
