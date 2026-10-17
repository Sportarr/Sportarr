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

public class FormulaOneYearXIdentityTests
{
    private readonly ReleaseMatchingService _matching = new(
        Mock.Of<ILogger<ReleaseMatchingService>>(),
        new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
        new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()));

    [Theory]
    [InlineData("Formula.1.2026x89.Bahrain.Race.F1TV.1080p.SS")]
    [InlineData("Formula_1_2026x89_Bahrain_Race_F1TV_1080p_SS")]
    public void BahrainReleaseMatchesBahrainRaceDespiteUploaderNumber(string title)
    {
        var result = _matching.ValidateRelease(Release(title), Race("Bahrain in Malaysia Grand Prix - Race", "16"));

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.IsHardRejection.Should().BeFalse();
        result.Rejections.Should().NotContain(reason => reason.Contains("Round mismatch"));
    }

    [Fact]
    public void BahrainReleaseDoesNotMatchSingaporeRace()
    {
        var result = _matching.ValidateRelease(
            Release("Formula.1.2026x89.Bahrain.Race.F1TV.1080p.SS"),
            Race("Singapore Grand Prix - Race", "89"));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Location mismatch"));
    }

    [Fact]
    public void MalaysiaReleaseDoesNotMatchBahrainRaceHeldInMalaysia()
    {
        var result = _matching.ValidateRelease(
            Release("Formula.1.2026x89.Malaysia.Race.F1TV.1080p.SS"),
            Race("Bahrain in Malaysia Grand Prix - Race", "16"));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Location mismatch"));
    }

    [Fact]
    public void CoincidentalUploaderNumberDoesNotEarnChampionshipRoundBonus()
    {
        var result = _matching.ValidateRelease(
            Release("Formula.1.2026x16.Bahrain.Race.F1TV.1080p.SS"),
            Race("Bahrain in Malaysia Grand Prix - Race", "16"));

        result.MatchReasons.Should().NotContain(reason => reason.StartsWith("Round number matches"));
    }

    [Fact]
    public void ExplicitRoundStillRejectsWrongChampionshipRound()
    {
        var result = _matching.ValidateRelease(
            Release("Formula.1.2025x26.Round.04.BahrainGP.Race.F1.Live.1080p.SS"),
            Race("Bahrain Grand Prix - Race", "5", 2025));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Round mismatch"));
    }

    [Theory]
    [InlineData("Chinese Grand Prix - Race", "2")]
    [InlineData("Bahrain in Malaysia Grand Prix - Race", "16")]
    public void BareUploaderNumberAndRaceDoNotIdentifyAGrandPrix(string eventTitle, string round)
    {
        var result = _matching.ValidateRelease(
            Release("Formula.1.2026x02.Race.1080p"), Race(eventTitle, round));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Insufficient evidence"));
    }

    [Theory]
    [InlineData("Formula_1_2026x89_Rd04_Bahrain_Race_F1TV_1080p_SS")]
    [InlineData("Formula_1_2026x89_R04_Bahrain_Race_F1TV_1080p_SS")]
    [InlineData("Formula_1_2026x89_Round_04_Bahrain_Race_F1TV_1080p_SS")]
    public void UnderscoreDelimitedExplicitRoundRejectsWrongRound(string title)
    {
        var result = _matching.ValidateRelease(Release(title), Race("Bahrain Grand Prix - Race", "5"));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Round mismatch"));
    }

    [Fact]
    public void ChineseGrandPrixAliasIdentifiesEventWithoutTrustingUploaderNumber()
    {
        var result = _matching.ValidateRelease(
            Release("Formula_1_2026x02_China_Race_SkyF1HD_1080p"),
            Race("Chinese Grand Prix - Race", "2"));

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().NotContain(reason => reason.StartsWith("Round number matches"));
    }

    [Fact]
    public async Task ManualSearchApprovesBahrainAndGrabKeepsItsEventInQueue()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            multipart: false, title: "Bahrain in Malaysia Grand Prix - Race",
            sport: "Motorsport", leagueName: "Formula 1");
        rig.Event.Round = "16";
        rig.Event.EventDate = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        rig.Event.Season = "2026";
        rig.Event.SeasonNumber = 2026;
        (await rig.Db.QualityProfiles.SingleAsync()).Items.Add(
            new QualityItem { Name = "HDTV-1080p", Quality = 6, Allowed = true });
        rig.Db.Events.Add(new Event
        {
            Title = "Singapore Grand Prix - Race",
            Sport = "Motorsport",
            LeagueId = rig.Event.LeagueId,
            QualityProfileId = rig.Event.QualityProfileId,
            EventDate = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc),
            Round = "17",
            Monitored = true
        });
        await rig.Db.SaveChangesAsync();
        var release = rig.Release("Formula.1.2026x89.Bahrain.Race.F1TV.1080p.SS");
        release.PublishDate = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        const string query = "Formula 1 2026x89 Bahrain";
        var indexers = rig.Services.GetRequiredService<IndexerSearchService>();
        var fingerprint = await indexers.GetSearchSourceFingerprintAsync(true, rig.Event.League?.Tags);
        var key = SearchResultCache.RequestKey(new[] { query }, rig.Event.League?.Tags,
            10000, false, Sportarr.Api.Helpers.SportarrIdToken.Normalize(rig.Event.ExternalId), fingerprint);
        rig.Services.GetRequiredService<SearchResultCache>().Store(key, new[] { release });

        using var search = await rig.Client.PostAsJsonAsync($"/api/event/{rig.Event.Id}/search", new { customQuery = query });
        var body = await search.Content.ReadAsStringAsync();
        search.IsSuccessStatusCode.Should().BeTrue(body);
        var results = JsonDocument.Parse(body).RootElement.GetProperty("results");
        results.GetArrayLength().Should().Be(1);
        results[0].GetProperty("approved").GetBoolean().Should().BeTrue(results[0].ToString());

        await rig.GrabAsync(release);

        (await rig.Db.DownloadQueue.SingleAsync()).EventId.Should().Be(rig.Event.Id);
        (await rig.Db.GrabHistory.SingleAsync()).EventId.Should().Be(rig.Event.Id);
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "https://example.invalid/" + title,
        Indexer = "Fixture"
    };

    private static Event Race(string title, string round, int year = 2026) => new()
    {
        Title = title,
        Sport = "Motorsport",
        EventDate = new DateTime(year, 10, 1, 12, 0, 0, DateTimeKind.Utc),
        Round = round,
        League = new League { Name = "Formula 1", Sport = "Motorsport" }
    };
}
