using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class DriversConferenceReleaseTests
{
    private readonly ReleaseMatchingService _matching = new(
        Mock.Of<ILogger<ReleaseMatchingService>>(),
        new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
        new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()));

    [Theory]
    [InlineData("Formula 1 2026 Azerbaijan Grand Prix Round 15 Paddock & Drivers Conference 2160P Sky Sports F1 UHD H 265")]
    [InlineData("Formula.1.2026.Azerbaijan.Grand.Prix.Round.15.Drivers.Conference.1080p")]
    [InlineData("Formula_1_2026_Azerbaijan_Grand_Prix_Round_15_Driver's_Conference_1080p")]
    [InlineData("Formula_1_2026_Azerbaijan_Grand_Prix_Round_15_Drivers'_Conference_1080p")]
    public void ConferenceReleaseCannotFillMonitoredRace(string title)
    {
        var result = _matching.ValidateRelease(Release(title), AzerbaijanRace());

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Non-event content"));
    }

    [Fact]
    public void RaceReleaseStillMatchesMonitoredRace()
    {
        var result = _matching.ValidateRelease(
            Release("Formula 1 2026 Azerbaijan Grand Prix Round 15 Race 2160P Sky Sports F1 UHD H 265"),
            AzerbaijanRace());

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.IsHardRejection.Should().BeFalse();
    }

    [Fact]
    public async Task ManualSearchRejectsConferenceAndApprovesRace()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            multipart: false, title: "Azerbaijan Grand Prix - Race",
            sport: "Motorsport", leagueName: "Formula 1");
        rig.Event.Round = "15";
        rig.Event.EventDate = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        rig.Event.Season = "2026";
        rig.Event.SeasonNumber = 2026;
        (await rig.Db.QualityProfiles.SingleAsync()).Items.Add(
            new QualityItem { Name = "HDTV-1080p", Quality = 6, Allowed = true });
        await rig.Db.SaveChangesAsync();

        var conference = rig.Release("Formula.1.2026.Azerbaijan.Grand.Prix.Round.15.Drivers.Conference.1080p", suffix: "conference");
        var race = rig.Release("Formula.1.2026.Azerbaijan.Grand.Prix.Round.15.Race.1080p", suffix: "race");
        conference.PublishDate = race.PublishDate = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        const string query = "Formula 1 Azerbaijan Grand Prix 2026";
        var indexers = rig.Services.GetRequiredService<IndexerSearchService>();
        var fingerprint = await indexers.GetSearchSourceFingerprintAsync(true, rig.Event.League?.Tags);
        var key = SearchResultCache.RequestKey(new[] { query }, rig.Event.League?.Tags,
            10000, false, Sportarr.Api.Helpers.SportarrIdToken.Normalize(rig.Event.ExternalId), fingerprint);
        rig.Services.GetRequiredService<SearchResultCache>().Store(key, new[] { conference, race });

        using var search = await rig.Client.PostAsJsonAsync($"/api/event/{rig.Event.Id}/search", new { customQuery = query });
        var body = await search.Content.ReadAsStringAsync();
        search.IsSuccessStatusCode.Should().BeTrue(body);
        var results = JsonDocument.Parse(body).RootElement.GetProperty("results").EnumerateArray().ToList();
        results.Should().HaveCount(2);
        results.Single(item => item.GetProperty("title").GetString() == conference.Title)
            .GetProperty("approved").GetBoolean().Should().BeFalse();
        results.Single(item => item.GetProperty("title").GetString() == race.Title)
            .GetProperty("approved").GetBoolean().Should().BeTrue();
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "https://example.invalid/fixture",
        Indexer = "Fixture"
    };

    private static Event AzerbaijanRace() => new()
    {
        Title = "Azerbaijan Grand Prix - Race",
        Sport = "Motorsport",
        Round = "15",
        Monitored = true,
        EventDate = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "Formula 1", Sport = "Motorsport" }
    };
}
