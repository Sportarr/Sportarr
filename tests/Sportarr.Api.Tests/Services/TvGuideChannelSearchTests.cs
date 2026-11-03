using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Services.Interfaces;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #405. The guide had no channel search, so a user with the
/// "USA - FOX" group open scrolled through every affiliate. The guide also
/// loads only the first 500 channels. The search filters on the server
/// before paging, so it finds channels past that limit.
/// </summary>
public class TvGuideChannelSearchTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SportarrDbContext _db;
    private readonly EpgService _service;

    public TvGuideChannelSearchTests()
    {
        _connection.Open();
        _db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        // The guide query never parses XMLTV.
        _service = new EpgService(NullLogger<EpgService>.Instance, _db, null!, Mock.Of<INotificationService>());

        _db.IptvSources.Add(new IptvSource { Id = 1, Name = "Provider", Url = "http://test", IsActive = true });
        var channels = new List<IptvChannel>();
        for (var i = 1; i <= 600; i++)
        {
            channels.Add(new IptvChannel
            {
                Id = i, SourceId = 1, Name = $"US: FOX {i} Affiliate", StreamUrl = $"http://test/{i}",
                ChannelNumber = i, Group = "USA - FOX",
            });
        }
        channels.Add(new IptvChannel
        {
            Id = 601, SourceId = 1, Name = "US: FOX 5 WNYW New York", StreamUrl = "http://test/601",
            ChannelNumber = 9005, Group = "USA - FOX",
        });
        _db.IptvChannels.AddRange(channels);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Task<TvGuideResponse> GuideAsync(string? search) => _service.GetTvGuideAsync(
        DateTime.UtcNow, DateTime.UtcNow.AddHours(6), group: "USA - FOX", limit: 500, offset: 0, search: search);

    [Fact]
    public async Task ASearchByNameFindsAChannelPastTheFirst500()
    {
        var guide = await GuideAsync("wnyw");

        guide.TotalChannels.Should().Be(1);
        guide.Channels.Select(c => c.Id).Should().Equal(601);
    }

    [Fact]
    public async Task ASearchByNumberFindsTheChannelWithThatNumber()
    {
        var guide = await GuideAsync("9005");

        guide.Channels.Select(c => c.Id).Should().Equal(601);
    }

    [Fact]
    public async Task ANumberAlsoMatchesNamesThatContainIt()
    {
        var guide = await GuideAsync("550");

        guide.Channels.Select(c => c.Id).Should().Equal(550);
    }

    [Fact]
    public async Task AnEmptySearchKeepsTheFullGuide()
    {
        var guide = await GuideAsync("   ");

        guide.TotalChannels.Should().Be(601);
        guide.Channels.Should().HaveCount(500);
    }
}
