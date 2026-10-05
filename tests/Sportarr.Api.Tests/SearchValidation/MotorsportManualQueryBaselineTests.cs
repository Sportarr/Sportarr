using System.Diagnostics;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class MotorsportManualQueryBaselineTests(ITestOutputHelper output)
{
    private const string SilverstoneProbe = "Silverstone Grand Prix Practice 1";
    private const string ProbeOnlyTitle = "2026.07.03.Formula.1.Round.6.Silverstone.Grand.Prix.Practice.1.1080p.WEB-DL.H264-GROUP";
    private static string[] Legacy(string meeting = "Silverstone", string round = "06") => new[]
    {
        $"Formula 1 2026 Round{round}", $"Formula 1 2026 {meeting}", "Formula 1 2026",
        $"Formula1 2026 Round{round}", $"Formula1 2026 {meeting}", "Formula1 2026"
    };

    [Theory]
    [InlineData("Silverstone")]
    [InlineData("Monaco")]
    public async Task DefaultManualKeepsAllSixLegacyQueriesAndReusesWarmResults(string meeting)
    {
        var probe = meeting == "Silverstone" ? SilverstoneProbe : "Monaco Grand Prix Qualifying";
        var title = meeting == "Silverstone" ? ProbeOnlyTitle : "2026.05.23.Formula.1.Round.8.Monaco.Grand.Prix.Qualifying.1080p.WEB-DL.H264-GROUP";
        await ProveExplicitQueryAsync(meeting, probe, title);
        await using var rig = await CreateMeetingAsync(meeting);
        rig.AddRelease(title, suppliedEventId: null);
        var legacy = Legacy(meeting, meeting == "Silverstone" ? "06" : "08");
        rig.Services.GetRequiredService<EventQueryService>().BuildEventQueries(rig.Event).Should().Equal(legacy);
        var watch = Stopwatch.StartNew();
        var first = await rig.ManualAsync();
        var firstArrivals = rig.Transport.Arrivals;
        var repeated = await rig.ManualAsync();
        output.WriteLine($"Manual coverage and warm repeat elapsed milliseconds: {watch.ElapsedMilliseconds}");
        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(row => row.Query).Should().Equal(legacy);
            rig.Transport.Searches.Where(row => row.Query != probe).SelectMany(row => row.Guids).Should().BeEmpty();
            rig.Transport.Searches.Should().NotContain(row => row.Query == probe);
            first.Should().BeEmpty();
            repeated.Should().BeEmpty();
            rig.Transport.Arrivals.Should().Be(firstArrivals);
            watch.ElapsedMilliseconds.Should().BeLessThan(45_000);
            await NoTransferAsync(rig);
        }
    }

    [Fact]
    public async Task ExistingApprovedLegacyOfferKeepsItsQualityWithoutAddingMetadataRequests()
    {
        await ProveExplicitQueryAsync("Silverstone", SilverstoneProbe, ProbeOnlyTitle);
        await using var rig = await CreateMeetingAsync("Silverstone");
        rig.AddRelease("Formula.1.2026.Silverstone.Grand.Prix.Practice.1.2026.07.03.Round.6.1080p.WEB-DL.H264-GROUP", null, "offer-legacy");
        rig.AddRelease(ProbeOnlyTitle, null, "offer-probe");
        var result = await rig.ManualAsync();
        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(row => row.Query).Should().Equal(Legacy());
            rig.Transport.Searches.Where(row => row.Query != SilverstoneProbe).SelectMany(row => row.Guids).Distinct().Should().Equal("offer-legacy");
            rig.Transport.Searches.Should().NotContain(row => row.Query == SilverstoneProbe);
            Approved(result, "offer-legacy");
            result.Should().NotContain(row => row.Guid == "offer-probe");
            result.Single(row => row.Guid == "offer-legacy").Quality.Should().Be("WEBDL-1080p");
            await NoTransferAsync(rig);
        }
    }

    [Fact]
    public async Task ExplicitUserQueryReceivesNoAdditionalProbeAndReusesWarmResults()
    {
        await using var rig = await CreateMeetingAsync("Silverstone");
        rig.AddRelease(ProbeOnlyTitle, suppliedEventId: null);
        await UnchangedAsync(rig, new[] { "Silverstone Grand Prix" }, "Silverstone Grand Prix", "offer-target");
    }

    [Fact]
    public async Task NascarManualSearchScoresBristolButRejectsAnotherSeasonVenue()
    {
        await using var rig = await MotorsportManualQueryHttpHarness.CreateAsync(output, "phrase");
        rig.Event.League!.Name = "NASCAR Cup Series";
        rig.Event.Title = "Bass Pro Shops Night - Race";
        rig.Event.Round = "29";
        rig.Event.Venue = "Bristol Motor Speedway";
        rig.Event.Location = "United States";
        rig.Event.EventDate = new DateTime(2026, 9, 19, 23, 30, 0, DateTimeKind.Utc);
        rig.Db.Events.Add(new Event
        {
            Title = "Coca-Cola 600 - Race", Sport = "Motorsport", Season = "2026", Round = "12",
            EventDate = new DateTime(2026, 5, 24, 22, 0, 0, DateTimeKind.Utc),
            Venue = "Charlotte Motor Speedway", Location = "United States", League = rig.Event.League
        });
        await rig.Db.SaveChangesAsync();
        rig.AddRelease("NASCAR Cup Series 2026 Round29 Bristol Motor Speedway TN Race 1080p WEB-DL H264", suppliedEventId: null, guid: "bristol");
        rig.AddRelease("NASCAR Cup Series 2026 Round29 Charlotte Race 1080p WEB-DL H264", suppliedEventId: null, guid: "charlotte");

        var results = await rig.ManualAsync("NASCAR Cup Series 2026 Round29");

        results.Single(row => row.Guid == "bristol").MatchScore
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
        results.Single(row => row.Guid == "charlotte").MatchScore.Should().Be(0);
    }

    [Fact]
    public async Task NascarManualSearchKeepsExactIdDespiteWrongVenueText()
    {
        await using var rig = await MotorsportManualQueryHttpHarness.CreateAsync(output, "phrase");
        rig.Event.League!.Name = "NASCAR Cup Series";
        rig.Event.Title = "Bass Pro Shops Night - Race";
        rig.Event.Round = "29";
        rig.Event.Venue = "Bristol Motor Speedway";
        rig.Event.Location = "United States";
        rig.Event.EventDate = new DateTime(2026, 9, 19, 23, 30, 0, DateTimeKind.Utc);
        rig.Db.Events.Add(new Event
        {
            Title = "Coca-Cola 600 - Race", Sport = "Motorsport", Season = "2026", Round = "12",
            EventDate = new DateTime(2026, 5, 24, 22, 0, 0, DateTimeKind.Utc),
            Venue = "Charlotte Motor Speedway", Location = "United States", League = rig.Event.League
        });
        await rig.Db.SaveChangesAsync();
        rig.AddRelease("NASCAR Cup Series 2026 Round29 Charlotte Race 1080p WEB-DL H264", guid: "exact-id");

        var results = await rig.ManualAsync("NASCAR Cup Series 2026 Round29");

        results.Single(row => row.Guid == "exact-id").MatchScore.Should().Be(100);
    }

    [Fact]
    public async Task ExplicitTemplatesKeepTheirExactOrderAndReuseWarmResults()
    {
        await using var rig = await CreateMeetingAsync("Silverstone");
        rig.Event.League!.SearchQueryTemplate = "unpublished alpha\nSilverstone Grand Prix";
        await rig.Db.SaveChangesAsync();
        rig.AddRelease(ProbeOnlyTitle, suppliedEventId: null);
        await UnchangedAsync(rig, new[] { "unpublished alpha", "Silverstone Grand Prix" }, null, "offer-target");
    }

    [Fact]
    public async Task GenericSessionMetadataReceivesNoAdditionalProbe()
    {
        await using var rig = await CreateMeetingAsync("Silverstone");
        await rig.ConfigureAsync("Practice 1");
        await UnchangedAsync(rig, new[] { "Formula 1 2026 Round08", "Formula 1 2026", "Formula1 2026 Round08", "Formula1 2026" });
    }

    [Fact]
    public async Task NonMotorsportDefaultReceivesNoAdditionalProbe()
    {
        await using var rig = await CreateMeetingAsync("Silverstone");
        rig.Event.Title = "Celtics vs Lakers";
        rig.Event.Sport = "Basketball";
        rig.Event.League!.Name = "NBA";
        rig.Event.League.Sport = "Basketball";
        await rig.Db.SaveChangesAsync();
        await UnchangedAsync(rig, new[] { "NBA 2026 07", "NBA 2026" });
    }

    [Fact]
    public async Task EmptyDefaultManualPlanIsFetchedOnceWithinNegativeCacheLifetime()
    {
        await using var rig = await CreateMeetingAsync("Silverstone");
        await UnchangedAsync(rig, Legacy());
    }

    [Fact]
    public async Task ConcurrentManualRequestsShareOneIndexerQuery()
    {
        await using var rig = await CreateMeetingAsync("Silverstone");
        rig.Transport.SearchDelayMs = 150;
        const string query = "Formula 1 2026 Silverstone";

        var results = await rig.ConcurrentManualAsync(query);

        results.Should().HaveCount(2);
        rig.Transport.Searches.Select(search => search.Query).Should().Equal(query);
    }

    private async Task UnchangedAsync(MotorsportManualQueryHttpHarness rig, string[] expected, string? query = null, string? approvedGuid = null)
    {
        var watch = Stopwatch.StartNew();
        var first = await rig.ManualAsync(query);
        var arrivals = rig.Transport.Arrivals;
        var repeat = await rig.ManualAsync(query);
        output.WriteLine($"Manual unchanged or empty warm-repeat elapsed milliseconds: {watch.ElapsedMilliseconds}");
        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(row => row.Query).Should().Equal(expected);
            rig.Transport.Arrivals.Should().Be(arrivals);
            watch.ElapsedMilliseconds.Should().BeLessThan(45_000);
            if (approvedGuid == null) { first.Should().BeEmpty(); repeat.Should().BeEmpty(); }
            else { Approved(first, approvedGuid); Approved(repeat, approvedGuid); }
            await NoTransferAsync(rig);
        }
    }

    private async Task ProveExplicitQueryAsync(string meeting, string query, string title)
    {
        output.WriteLine("Positive prerequisite through the current explicit manual query endpoint");
        await using var rig = await CreateMeetingAsync(meeting);
        rig.AddRelease(title, suppliedEventId: null);
        var result = await rig.ManualAsync(query);
        rig.Transport.Searches.Select(row => row.Query).Should().Equal(query);
        rig.Transport.Searches.SelectMany(row => row.Guids).Should().Equal("offer-target");
        Approved(result, "offer-target");
        await NoTransferAsync(rig);
    }

    private async Task<MotorsportManualQueryHttpHarness> CreateMeetingAsync(string meeting)
    {
        var rig = await MotorsportManualQueryHttpHarness.CreateAsync(output, "phrase");
        if (meeting == "Monaco") await rig.ConfigureAsync("Monaco Grand Prix Qualifying");
        return rig;
    }

    private static void Approved(ReleaseSearchResult[] rows, string guid)
    {
        var matching = rows.Where(row => row.Guid == guid).ToArray();
        matching.Should().ContainSingle();
        matching.Should().OnlyContain(row => row.Approved && row.Rejections.Count == 0);
    }

    private static async Task NoTransferAsync(MotorsportManualQueryHttpHarness rig)
    {
        rig.Transport.DescriptorAttempts.Should().Be(0);
        rig.Transport.CapsRequests.Should().Be(1);
        rig.Transport.Arrivals.Should().Be(rig.Transport.Searches.Length + 1);
        (await rig.Db.DownloadQueue.CountAsync()).Should().Be(0);
        rig.Transport.Violations.Should().BeEmpty();
    }
}
