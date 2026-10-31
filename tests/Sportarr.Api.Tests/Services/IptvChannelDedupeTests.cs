using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

public class IptvChannelDedupeTests
{
    private static IptvChannel CreateChannel(string name, string streamUrl, string group, int sourceId)
    {
        return new IptvChannel
        {
            Name = name,
            StreamUrl = streamUrl,
            Group = group,
            SourceId = sourceId
        };
    }

    [Fact]
    public void DistinctByStreamUrl_SameStreamUrl_KeepsFirstChannel()
    {
        var channels = new List<IptvChannel>
        {
            CreateChannel("CBS - KTAB", "http://p/live/u/p/101.ts", "US Locals", 1),
            CreateChannel("CBS - KTAB", "http://p/live/u/p/101.ts", "CBS", 2)
        };

        var result = IptvSourceService.DistinctByStreamUrl(channels);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("CBS - KTAB");
        result[0].Group.Should().Be("US Locals");
        result[0].StreamUrl.Should().Be("http://p/live/u/p/101.ts");
    }

    [Fact]
    public void DistinctByStreamUrl_DifferentStreamUrls_KeepsAllInOrder()
    {
        var channels = new List<IptvChannel>
        {
            CreateChannel("First", "http://p/live/1.ts", "Group 1", 1),
            CreateChannel("Second", "http://p/live/2.ts", "Group 2", 2),
            CreateChannel("Third", "http://p/live/3.ts", "Group 3", 3)
        };

        var result = IptvSourceService.DistinctByStreamUrl(channels);

        result.Should().HaveCount(3);
        result[0].Name.Should().Be("First");
        result[1].Name.Should().Be("Second");
        result[2].Name.Should().Be("Third");
    }

    [Fact]
    public void DistinctByStreamUrl_UrlsDifferingOnlyByCase_KeepsBoth()
    {
        var channels = new List<IptvChannel>
        {
            CreateChannel("Lower", "http://p/live/u/p/101.ts", "Lower", 1),
            CreateChannel("Upper", "http://p/live/u/p/101.TS", "Upper", 2)
        };

        var result = IptvSourceService.DistinctByStreamUrl(channels);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Lower");
        result[1].Name.Should().Be("Upper");
    }

    [Fact]
    public void DistinctByStreamUrl_EmptyStreamUrls_KeepsBoth()
    {
        var channels = new List<IptvChannel>
        {
            CreateChannel("Empty 1", string.Empty, "Group 1", 1),
            CreateChannel("Empty 2", string.Empty, "Group 2", 2)
        };

        var result = IptvSourceService.DistinctByStreamUrl(channels);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Empty 1");
        result[1].Name.Should().Be("Empty 2");
    }

    private static async Task<(SqliteConnection Connection, SportarrDbContext Db, IptvSourceService Service, IptvSource Source)> RigAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var source = new IptvSource { Id = 1, Name = "Provider", Url = "http://test", IsActive = true };
        db.IptvSources.Add(source);
        db.Leagues.Add(new League { Id = 1, Name = "NHL", Sport = "Ice Hockey" });
        await db.SaveChangesAsync();
        var http = Mock.Of<IHttpClientFactory>();
        var service = new IptvSourceService(
            Mock.Of<ILogger<IptvSourceService>>(),
            db,
            new M3uParserService(Mock.Of<ILogger<M3uParserService>>(), http),
            new XtreamCodesClient(Mock.Of<ILogger<XtreamCodesClient>>(), http),
            http,
            Mock.Of<IServiceScopeFactory>());
        return (connection, db, service, source);
    }

    [Fact]
    public async Task AStoredDuplicateHealsAndTheMappedCopyKeepsTheStream()
    {
        // Two rows were stored for one stream by an older sync. The user
        // mapped a league to the second one. After the sync, that copy stays
        // enabled and the unused copy is removed.
        var (connection, db, service, source) = await RigAsync();
        await using var _ = connection;
        await using var __ = db;
        const string url = "http://p/live/u/p/101.ts";
        db.IptvChannels.AddRange(
            new IptvChannel { Id = 10, SourceId = 1, Name = "CBS - KTAB", StreamUrl = url },
            new IptvChannel { Id = 11, SourceId = 1, Name = "CBS - KTAB", StreamUrl = url });
        db.ChannelLeagueMappings.Add(new ChannelLeagueMapping { ChannelId = 11, LeagueId = 1 });
        await db.SaveChangesAsync();

        var incoming = IptvSourceService.DistinctByStreamUrl(new List<IptvChannel>
        {
            new() { SourceId = 1, Name = "CBS - KTAB", StreamUrl = url, Group = "US Locals" },
            new() { SourceId = 1, Name = "CBS - KTAB", StreamUrl = url, Group = "CBS" },
        });
        await service.UpsertChannelsAsync(source, incoming);
        await db.SaveChangesAsync();

        var rows = await db.IptvChannels.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(11);
        rows[0].IsEnabled.Should().BeTrue();
    }
}
