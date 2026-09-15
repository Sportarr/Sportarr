using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

public sealed class MotorsportQueryBaselineTests(ITestOutputHelper output)
{
    private const string SilverstoneProbe = "Silverstone Grand Prix Practice 1";
    private const string MonacoProbe = "Monaco Grand Prix Qualifying";
    private const string SilverstoneRelease = "Formula.1.2026.07.03.Round.6.Silverstone.Grand.Prix.Practice.1.1080p.WEB-DL.H264-GROUP";
    private static readonly string[] SilverstoneLegacy =
    {
        "Formula 1 2026 Round06", "Formula 1 2026 Silverstone", "Formula 1 2026",
        "Formula1 2026 Round06", "Formula1 2026 Silverstone", "Formula1 2026"
    };
    private static readonly string[] MonacoLegacy =
    {
        "Formula 1 2026 Round08", "Formula 1 2026 Monaco", "Formula 1 2026",
        "Formula1 2026 Round08", "Formula1 2026 Monaco", "Formula1 2026"
    };

    [Theory]
    [InlineData("and")]
    [InlineData("phrase")]
    [InlineData("ordered")]
    public async Task DefaultPlanRetrievesThroughTheDeclaredSourceSemantics(string mode)
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, mode);
        rig.AddRelease(SilverstoneRelease);
        var result = await rig.AutomaticAsync();
        var expected = SilverstoneLegacy;

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(expected);
            rig.Transport.Searches.Should().NotContain(search => search.Query == SilverstoneProbe);
            rig.Transport.Searches.SelectMany(search => search.Guids).Distinct().Should().Equal("offer-target");
            await SelectedAndRefusedAsync(rig, result, SilverstoneRelease);
        }
    }

    [Fact]
    public async Task SuccessfulLocationQueryStillSuppressesUnneededMetadataProbe()
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output);
        await rig.ConfigureAsync(MonacoProbe);
        var title = rig.AddRelease("Formula.1.2026.Monaco.Qualifying.Grand.Prix.2026.05.23.Round.8.1080p.WEB-DL.H264-GROUP");
        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(
                MonacoLegacy);
            rig.Transport.Searches.Should().NotContain(search => search.Query == MonacoProbe);
            rig.Transport.Searches.Where(search => search.Query == MonacoLegacy[1]).SelectMany(search => search.Guids).Should().Equal("offer-target");
            await SelectedAndRefusedAsync(rig, result, title);
        }
    }

    [Fact]
    public async Task BroadDefaultQueryRetrievesTheReleaseWithoutMetadataProbe()
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output);
        await rig.ConfigureAsync(MonacoProbe);
        var title = rig.AddRelease("Formula.1.2026.05.23.Round.8.Monaco.Grand.Prix.Qualifying.1080p.WEB-DL.H264-GROUP");
        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(MonacoLegacy);
            rig.Transport.Searches.Should().NotContain(search => search.Query == MonacoProbe);
            rig.Transport.Searches.Where(search => search.Query == MonacoLegacy[2]).SelectMany(search => search.Guids).Should().Equal("offer-target");
            await SelectedAndRefusedAsync(rig, result, title);
        }
    }

    [Fact]
    public async Task AllEmptySearchRunsTheCompletePlanAndOneMetadataProbe()
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output);
        await rig.ConfigureAsync(MonacoProbe);
        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(MonacoLegacy.Append(MonacoProbe));
            await EmptyWithoutTransferAsync(rig, result);
        }
    }

    [Fact]
    public async Task GenericSessionOnlyMetadataRunsFullPlanWithoutAnUnusableProbe()
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output);
        await rig.ConfigureAsync("Practice 1");
        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal("Formula 1 2026 Round08", "Formula 1 2026", "Formula1 2026 Round08", "Formula1 2026");
            await EmptyWithoutTransferAsync(rig, result);
        }
    }

    [Fact]
    public async Task ExplicitTemplatesKeepTheirOrderAndAttemptTheThirdLine()
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output);
        rig.Event.League!.SearchQueryTemplate = "unpublished alpha\nunpublished beta\n{EventTitle}";
        await rig.Db.SaveChangesAsync();
        rig.AddRelease(SilverstoneRelease);
        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal("unpublished alpha", "unpublished beta", SilverstoneProbe);
            await SelectedAndRefusedAsync(rig, result, SilverstoneRelease);
        }
    }

    [Fact]
    public async Task MeetingWithoutSessionStillHasOneUsableMetadataProbe()
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output);
        await rig.ConfigureAsync("Monaco Grand Prix");
        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(MonacoLegacy.Append("Monaco Grand Prix"));
            await EmptyWithoutTransferAsync(rig, result);
        }
    }

    [Fact]
    public async Task BasketballDefaultQueriesRemainUnchanged()
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output);
        rig.Event.Title = "Celtics vs Lakers";
        rig.Event.Sport = "Basketball";
        rig.Event.League!.Name = "NBA";
        rig.Event.League.Sport = "Basketball";
        await rig.Db.SaveChangesAsync();
        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal("NBA 2026 07", "NBA 2026");
            await EmptyWithoutTransferAsync(rig, result);
        }
    }

    [Fact]
    public async Task BroadPhraseQueryWithoutSuppliedIdMustPassTheActualTitleMatcher()
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, "phrase");
        rig.AddRelease(SilverstoneRelease, suppliedEventId: null);
        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Releases.Should().ContainSingle().Which.SportarrEventId.Should().BeNull();
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(SilverstoneLegacy);
            rig.Transport.Searches.Where(search => search.Query == SilverstoneLegacy[2]).SelectMany(search => search.Guids).Should().Equal("offer-target");
            rig.Transport.Searches.Where(search => search.Query == SilverstoneLegacy[2]).SelectMany(search => search.SuppliedEventIds).Should().Equal(new string?[] { null });
            rig.Transport.Searches.Should().NotContain(search => search.Query == SilverstoneProbe);
            await SelectedAndRefusedAsync(rig, result, SilverstoneRelease);
        }
    }

    [Fact]
    public async Task NascarEventBeyondTheBroadResultCeilingUsesOneSpecificFallback()
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output);
        rig.Event.Title = "Daytona 500";
        rig.Event.Round = "1";
        rig.Event.EventDate = new DateTime(2026, 2, 15, 12, 0, 0, DateTimeKind.Utc);
        rig.Event.BroadcastDate = rig.Event.EventDate;
        rig.Event.League!.Name = "NASCAR Cup Series";
        await rig.Db.SaveChangesAsync();

        for (var number = 1; number <= 100; number++)
        {
            rig.AddRelease(
                $"NASCAR Cup Series 2026 Other Race {number:D3} 1080p WEB H264",
                suppliedEventId: null,
                guid: $"broad-offer-{number:D3}");
        }

        const string target = "NASCAR Cup Series 2026 Daytona 500 1080p WEB H264";
        rig.AddRelease(target, suppliedEventId: null, guid: "offer-target-after-ceiling");

        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(
                "NASCAR Cup Series 2026",
                "NASCAR Cup Series 2026 Daytona 500");
            result.ReleasesFound.Should().Be(101);
            await SelectedAndRefusedAsync(rig, result, target);
        }
    }

    [Fact]
    public async Task WecRoundReleaseBeyondTheBroadResultCeilingUsesOneSpecificFallback()
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output);
        rig.Event.Title = "6 Hours of Spa Francorchamps Qualifying - Hypercar";
        rig.Event.Round = "2";
        rig.Event.League!.Name = "WEC";
        await rig.Db.SaveChangesAsync();

        for (var number = 1; number <= 100; number++)
        {
            rig.AddRelease(
                $"WEC 2026 Round03 Other Session {number:D3} 1080p WEB H264",
                suppliedEventId: null,
                guid: $"wec-broad-offer-{number:D3}");
        }

        const string target = "WEC 2026 Round02 Belgium Qualifying STAN WEB DL 1080p H264 English MWR";
        rig.AddRelease(target, suppliedEventId: null, guid: "wec-target-after-ceiling");

        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(
                "WEC 2026",
                "WEC 2026 Round02");
            result.ReleasesFound.Should().Be(101);
            await SelectedAndRefusedAsync(rig, result, target);
        }
    }

    private static async Task SelectedAndRefusedAsync(MotorsportQueryHttpHarness rig, AutomaticSearchResult result, string title)
    {
        result.SelectedRelease.Should().Be(title);
        result.Success.Should().BeFalse();
        rig.Transport.DescriptorAttempts.Should().Be(1);
        (await rig.Db.DownloadQueue.ToListAsync()).Should().BeEmpty();
    }

    private static async Task EmptyWithoutTransferAsync(MotorsportQueryHttpHarness rig, AutomaticSearchResult result)
    {
        result.Success.Should().BeFalse();
        result.SelectedRelease.Should().BeNullOrEmpty();
        result.Message.Should().Contain("No releases found");
        rig.Transport.DescriptorAttempts.Should().Be(0);
        (await rig.Db.DownloadQueue.ToListAsync()).Should().BeEmpty();
    }
}
