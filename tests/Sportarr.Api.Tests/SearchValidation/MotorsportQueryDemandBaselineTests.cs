using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class MotorsportQueryDemandBaselineTests(ITestOutputHelper output)
{
    private const string Probe = "Silverstone Grand Prix Practice 1";
    private const string ProbeOnlyTarget = "2026.07.03.Formula.1.Round.6.Silverstone.Grand.Prix.Practice.1.1080p.WEB-DL.H264-GROUP";
    private const string BroadTarget = "Formula.1.2026.07.03.Round.6.Silverstone.Grand.Prix.Practice.1.1080p.WEB-DL.H264-GROUP";
    private const string WrongRoundSpaced = "Formula.1.2026.Round06.Silverstone.Grand.Prix.Practice.2.2026.07.03.1080p.WEB-DL.H264-GROUP";
    private static readonly string[] Legacy =
    {
        "Formula 1 2026 Round06", "Formula 1 2026 Silverstone", "Formula 1 2026",
        "Formula1 2026 Round06", "Formula1 2026 Silverstone", "Formula1 2026"
    };

    [Fact]
    public async Task NonemptyWrongLegacyOffersStillPermitOneNeededMetadataProbe()
    {
        await VerifyProbeOnlyTargetWithExplicitTemplateAsync();
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, "phrase");
        rig.AddRelease(WrongRoundSpaced, suppliedEventId: null, guid: "offer-wrong-round-spaced");
        rig.AddRelease(ProbeOnlyTarget, suppliedEventId: null, guid: "offer-correct-probe");
        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(Legacy.Append(Probe));
            rig.Transport.Searches.Where(search => search.Query != Probe).SelectMany(search => search.Guids)
                .Distinct().Should().Equal("offer-wrong-round-spaced");
            rig.Transport.Searches.Where(search => search.Query == Probe).SelectMany(search => search.Guids)
                .Should().Equal("offer-correct-probe");
            await SelectedAndRefusedAsync(rig, result, ProbeOnlyTarget, "offer-correct-probe", 2);
        }
    }

    [Fact]
    public async Task BroadTargetTraversesTheFullPlanWithoutAnUnneededProbe()
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, "phrase");
        rig.AddRelease(WrongRoundSpaced, suppliedEventId: null, guid: "offer-wrong-round-spaced");
        rig.AddRelease(BroadTarget, suppliedEventId: null, guid: "offer-correct-broad");
        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(Legacy);
            rig.Transport.Searches.Should().NotContain(search => search.Query == Probe);
            rig.Transport.Searches.Where(search => search.Query == Legacy[2]).SelectMany(search => search.Guids)
                .Should().Equal("offer-wrong-round-spaced", "offer-correct-broad");
            await SelectedAndRefusedAsync(rig, result, BroadTarget, "offer-correct-broad", 2);
        }
    }

    [Fact]
    public async Task ExhaustedNonemptyLegacyPlanPermitsOnlyOneNeededMetadataProbe()
    {
        await VerifyProbeOnlyTargetWithExplicitTemplateAsync();
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, "phrase");
        var wrongGuids = new[] { "offer-wrong-round-spaced", "offer-wrong-location-spaced", "offer-wrong-round-compact", "offer-wrong-location-compact" };
        rig.AddRelease(WrongRoundSpaced, suppliedEventId: null, guid: wrongGuids[0]);
        rig.AddRelease("Formula.1.2026.Silverstone.Grand.Prix.Practice.2.2026.07.03.Round.6.1080p.WEB-DL.H264-GROUP",
            suppliedEventId: null, guid: wrongGuids[1]);
        rig.AddRelease("Formula1.2026.Round06.Silverstone.Grand.Prix.Practice.2.2026.07.03.1080p.WEB-DL.H264-GROUP",
            suppliedEventId: null, guid: wrongGuids[2]);
        rig.AddRelease("Formula1.2026.Silverstone.Grand.Prix.Practice.2.2026.07.03.Round.6.1080p.WEB-DL.H264-GROUP",
            suppliedEventId: null, guid: wrongGuids[3]);
        rig.AddRelease(ProbeOnlyTarget, suppliedEventId: null, guid: "offer-correct-probe");
        var result = await rig.AutomaticAsync();

        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(Legacy.Append(Probe));
            var oldSearches = rig.Transport.Searches.Where(search => search.Query != Probe).ToArray();
            oldSearches.Should().HaveCount(6);
            oldSearches.Should().OnlyContain(search => search.Guids.Length > 0);
            oldSearches.SelectMany(search => search.Guids).Distinct().Should().BeEquivalentTo(wrongGuids);
            rig.Transport.Searches.Where(search => search.Query == Probe).SelectMany(search => search.Guids)
                .Should().Equal("offer-correct-probe");
            await SelectedAndRefusedAsync(rig, result, ProbeOnlyTarget, "offer-correct-probe", 5);
        }
    }

    private async Task VerifyProbeOnlyTargetWithExplicitTemplateAsync()
    {
        output.WriteLine("Probe-only catalogue prerequisite through the current explicit-template route");
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, "phrase");
        rig.Event.League!.SearchQueryTemplate = "{EventTitle}";
        await rig.Db.SaveChangesAsync();
        rig.AddRelease(ProbeOnlyTarget, suppliedEventId: null, guid: "offer-correct-probe");
        var result = await rig.AutomaticAsync();
        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(Probe);
            rig.Transport.Searches.SelectMany(search => search.Guids).Should().Equal("offer-correct-probe");
            await SelectedAndRefusedAsync(rig, result, ProbeOnlyTarget, "offer-correct-probe", 1);
        }
    }

    private static async Task SelectedAndRefusedAsync(MotorsportQueryHttpHarness rig, AutomaticSearchResult result,
        string title, string guid, int expectedParsedOffers)
    {
        rig.Transport.Releases.Should().OnlyContain(release => release.SportarrEventId == null);
        rig.Transport.Searches.SelectMany(search => search.SuppliedEventIds).Should().OnlyContain(id => id == null);
        result.ReleasesFound.Should().Be(expectedParsedOffers);
        result.SelectedRelease.Should().Be(title);
        rig.Transport.DescriptorGuids.Should().Equal(guid);
        rig.Transport.DescriptorAttempts.Should().Be(1);
        result.Success.Should().BeFalse();
        (await rig.Db.DownloadQueue.ToListAsync()).Should().BeEmpty();
    }
}
