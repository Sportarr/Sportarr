using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #437: Unmap keeps a row as an exclusion, and the user had no way to
/// see or undo it.
/// </summary>
public class LeagueChannelExclusionTests
{
    private const int League = 1;
    private const int Mapped = 1;
    private const int Blocked = 2;
    private const int AlsoBlocked = 3;

    private static SportarrDbContext CreateDb() => new(new DbContextOptionsBuilder<SportarrDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IptvSourceService Svc(SportarrDbContext db)
    {
        var http = Mock.Of<IHttpClientFactory>();
        return new IptvSourceService(
            Mock.Of<ILogger<IptvSourceService>>(),
            db,
            new M3uParserService(Mock.Of<ILogger<M3uParserService>>(), http),
            new XtreamCodesClient(Mock.Of<ILogger<XtreamCodesClient>>(), http),
            http,
            Mock.Of<IServiceScopeFactory>());
    }

    private static ChannelAutoMappingService Mapper(SportarrDbContext db) =>
        new(NullLogger<ChannelAutoMappingService>.Instance, db);

    private static ChannelLeagueMapping Exclusion(int channelId) => new()
    {
        ChannelId = channelId, LeagueId = League, IsManual = true, Confidence = 0,
        Priority = ChannelLeagueMapping.ExcludedPriority,
    };

    // The event lists "Harbor Sports" as its broadcaster. That evidence is
    // enough for the auto-mapper to map the "Harbor Sports" channel.
    private static async Task SeedAsync(SportarrDbContext db)
    {
        db.Leagues.Add(new League { Id = League, Name = "Fixture Circuit", Sport = "Athletics" });
        db.IptvSources.Add(new IptvSource { Id = 1, Name = "Fixture", Url = "https://iptv.invalid/list", IsActive = true });
        db.IptvChannels.AddRange(
            new IptvChannel { Id = Mapped, SourceId = 1, Name = "Atlas One", IsEnabled = true, StreamUrl = "https://iptv.invalid/1" },
            new IptvChannel { Id = Blocked, SourceId = 1, Name = "Harbor Sports", Country = "NZ", IsEnabled = true, StreamUrl = "https://iptv.invalid/2" },
            new IptvChannel { Id = AlsoBlocked, SourceId = 1, Name = "Coast Sport", IsEnabled = true, StreamUrl = "https://iptv.invalid/3" });
        db.Events.Add(new Event { Id = 1, LeagueId = League, Title = "Fixture meet", Sport = "Athletics",
            EventDate = DateTime.UtcNow.AddDays(1), Broadcast = "Harbor Sports" });
        db.ChannelLeagueMappings.AddRange(
            new ChannelLeagueMapping { ChannelId = Mapped, LeagueId = League, IsManual = true, Confidence = 100 },
            Exclusion(Blocked),
            Exclusion(AlsoBlocked));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ListsOnlyTheExcludedChannelsWithTheirSource()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        var rows = await Svc(db).GetLeagueExclusionsAsync(League);

        rows.Select(m => m.ChannelId).Should().BeEquivalentTo([Blocked, AlsoBlocked]);
        rows.Should().OnlyContain(m => m.IsExcluded);
        rows.Single(m => m.ChannelId == Blocked).Channel!.Source!.Name.Should().Be("Fixture");
        (await Svc(db).GetLeagueExclusionsAsync(99)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnExcludedChannelIsNotMappedAgainByTheAutoMapper()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        await Mapper(db).AutoMapAllChannelsAsync();

        var row = await db.ChannelLeagueMappings.AsNoTracking().SingleAsync(m => m.ChannelId == Blocked);
        row.IsExcluded.Should().BeTrue("the broadcaster evidence must not undo an exclusion");
        (await Mapper(db).GetChannelsForLeagueByQualityAsync(League))
            .Select(c => c.Channel.Id).Should().NotContain(Blocked);
    }

    [Fact]
    public async Task RemovingAnExclusionDeletesTheRowAndLeavesTheChannelUnmapped()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        (await Svc(db).RemoveLeagueExclusionAsync(League, Blocked)).Should().BeTrue();

        (await db.ChannelLeagueMappings.AnyAsync(m => m.ChannelId == Blocked)).Should().BeFalse();
        (await Svc(db).GetLeagueExclusionsAsync(League)).Select(m => m.ChannelId).Should().Equal(AlsoBlocked);
        (await Svc(db).GetChannelsForLeagueAsync(League)).Select(c => c.Id).Should().Equal(Mapped);
    }

    [Fact]
    public async Task TheAutoMapperCanMapTheChannelAfterTheExclusionIsRemoved()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        await Svc(db).RemoveLeagueExclusionAsync(League, Blocked);
        db.ChangeTracker.Clear();

        await Mapper(db).AutoMapAllChannelsAsync();

        var row = await db.ChannelLeagueMappings.AsNoTracking().SingleAsync(m => m.ChannelId == Blocked);
        row.IsExcluded.Should().BeFalse();
        row.IsManual.Should().BeFalse("the auto-mapper made this mapping");
    }

    [Fact]
    public async Task RemovingAnExclusionLeavesARealMappingAlone()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        (await Svc(db).RemoveLeagueExclusionAsync(League, Mapped)).Should().BeFalse();
        (await Svc(db).RemoveLeagueExclusionAsync(League, 99)).Should().BeFalse();

        (await db.ChannelLeagueMappings.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task MapAgainRestoresAManualMapping()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        await IptvEndpoints.SetMappingManualAsync(Blocked, League, true, db, NullLogger.Instance);
        db.ChangeTracker.Clear();

        var row = await db.ChannelLeagueMappings.AsNoTracking().SingleAsync(m => m.ChannelId == Blocked);
        row.IsExcluded.Should().BeFalse();
        row.IsManual.Should().BeTrue();
        row.Confidence.Should().Be(95);
        (await Svc(db).GetLeagueExclusionsAsync(League)).Select(m => m.ChannelId).Should().Equal(AlsoBlocked);
        (await Svc(db).GetChannelsForLeagueAsync(League)).Select(c => c.Id).Should().BeEquivalentTo([Mapped, Blocked]);

        // The manual lock keeps the auto-mapper away from the restored row.
        await Mapper(db).AutoMapAllChannelsAsync();
        (await db.ChannelLeagueMappings.AsNoTracking().SingleAsync(m => m.ChannelId == Blocked))
            .Should().BeEquivalentTo(new { IsManual = true, Confidence = 95 });
    }

    [Fact]
    public async Task TheCoverageReportCountsTheExcludedChannels()
    {
        // The report groups in SQL, so it runs on SQLite, not the in-memory provider.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        await SeedAsync(db);
        db.Leagues.Add(new League { Id = 2, Name = "Other Circuit", Sport = "Athletics" });
        await db.SaveChangesAsync();

        var result = await IptvEndpoints.CoverageReportAsync(db, 14);

        var json = JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value);
        var leagues = json.GetProperty("leagues").EnumerateArray()
            .ToDictionary(l => l.GetProperty("leagueId").GetInt32(), l => l.GetProperty("channels"));
        leagues[League].GetProperty("excluded").GetInt32().Should().Be(2);
        leagues[League].GetProperty("total").GetInt32().Should().Be(1, "exclusions are not mapped channels");
        leagues[2].GetProperty("excluded").GetInt32().Should().Be(0);
    }
}
