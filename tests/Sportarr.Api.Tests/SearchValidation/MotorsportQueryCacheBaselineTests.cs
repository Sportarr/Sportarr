using System.Diagnostics;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class MotorsportQueryCacheBaselineTests(ITestOutputHelper output)
{
    private const string Probe = "Silverstone Grand Prix Practice 1";
    private const string LegacyTitle = "Formula.1.2026.Silverstone.Grand.Prix.Practice.1.2026.07.03.Round.6.1080p.WEB-DL.H264-GROUP";
    private const string ProbeTitle = "2026.07.03.Formula.1.Round.6.Silverstone.Grand.Prix.Practice.1.2160p.WEB-DL.H264-GROUP";
    private static readonly string[] Legacy =
    {
        "Formula 1 2026 Round06", "Formula 1 2026 Silverstone", "Formula 1 2026",
        "Formula1 2026 Round06", "Formula1 2026 Silverstone", "Formula1 2026"
    };

    [Fact]
    public async Task RepeatedEligibleEmptySearchReusesItsFetchedNegativeProbe()
    {
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, "phrase");
        var elapsed = Stopwatch.StartNew();
        var first = await rig.AutomaticAsync();
        var searchesAfterFirst = rig.Transport.Searches;
        var arrivalsAfterFirst = rig.Transport.Arrivals;
        var second = await rig.AutomaticAsync();
        var measuredElapsed = elapsed.Elapsed;
        output.WriteLine($"Negative cache sequence elapsed milliseconds: {measuredElapsed.TotalMilliseconds:F0}");

        using (new AssertionScope())
        {
            measuredElapsed.Should().BeLessThan(TimeSpan.FromSeconds(45));
            searchesAfterFirst.Select(search => search.Query).Should().Equal(Legacy.Append(Probe));
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(Legacy.Append(Probe));
            rig.Transport.Searches.Count(search => search.Query == Probe).Should().Be(1);
            rig.Transport.Searches.SelectMany(search => search.Guids).Should().BeEmpty();
            rig.Transport.Arrivals.Should().Be(arrivalsAfterFirst);
            rig.Transport.CapsRequests.Should().Be(1);
            foreach (var result in new[] { first, second })
            {
                result.ReleasesFound.Should().Be(0);
                result.SelectedRelease.Should().BeNullOrEmpty();
                result.Success.Should().BeFalse();
            }
            rig.Transport.DescriptorGuids.Should().BeEmpty();
            rig.Transport.DescriptorAttempts.Should().Be(0);
            (await rig.Db.DownloadQueue.ToListAsync()).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ChangedProfileFetchesTheSkippedProbeThenReusesItsRawResults()
    {
        await VerifyUhdTargetThroughCurrentTemplateAsync();
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, "phrase");
        var uhdProfileId = await AddUhdProfileAsync(rig);
        AddCatalogue(rig);
        var elapsed = Stopwatch.StartNew();
        var first = await rig.AutomaticAsync();
        var firstSearches = rig.Transport.Searches;
        var firstDescriptors = rig.Transport.DescriptorGuids;
        var second = await rig.AutomaticAsync(uhdProfileId);
        var secondSearches = rig.Transport.Searches;
        var secondDescriptors = rig.Transport.DescriptorGuids;
        var arrivalsAfterSecond = rig.Transport.Arrivals;
        var third = await rig.AutomaticAsync(uhdProfileId);
        var measuredElapsed = elapsed.Elapsed;
        output.WriteLine($"Profile cache sequence elapsed milliseconds: {measuredElapsed.TotalMilliseconds:F0}");

        using (new AssertionScope())
        {
            measuredElapsed.Should().BeLessThan(TimeSpan.FromSeconds(300));
            firstSearches.Select(search => search.Query).Should().Equal(Legacy);
            firstSearches.Should().NotContain(search => search.Query == Probe);
            firstSearches.SelectMany(search => search.Guids).Distinct().Should().Equal("offer-legacy-hd");
            first.ReleasesFound.Should().Be(1);
            first.SelectedRelease.Should().Be(LegacyTitle);
            firstDescriptors.Should().Equal("offer-legacy-hd");

            secondSearches.Select(search => search.Query).Should().Equal(Legacy.Append(Probe));
            secondSearches.Where(search => search.Query == Probe).SelectMany(search => search.Guids)
                .Should().Equal("offer-legacy-hd", "offer-probe-uhd");
            secondSearches.SelectMany(search => search.SuppliedEventIds).Should().OnlyContain(id => id == null);
            second.ReleasesFound.Should().Be(2);
            second.SelectedRelease.Should().Be(ProbeTitle);
            secondDescriptors.Should().Equal("offer-legacy-hd", "offer-probe-uhd");

            rig.Transport.Searches.Select(search => search.Query).Should().Equal(secondSearches.Select(search => search.Query));
            rig.Transport.Searches.Count(search => search.Query == Probe).Should().Be(1);
            third.ReleasesFound.Should().Be(2);
            third.SelectedRelease.Should().Be(ProbeTitle);
            rig.Transport.Arrivals.Should().Be(arrivalsAfterSecond + 1);
            rig.Transport.CapsRequests.Should().Be(1);
            rig.Transport.DescriptorGuids.Should().Equal("offer-legacy-hd", "offer-probe-uhd", "offer-probe-uhd");
            rig.Transport.DescriptorAttempts.Should().Be(3);
            foreach (var result in new[] { first, second, third }) result.Success.Should().BeFalse();
            (await rig.Db.DownloadQueue.ToListAsync()).Should().BeEmpty();
        }
    }

    private async Task VerifyUhdTargetThroughCurrentTemplateAsync()
    {
        output.WriteLine("UHD catalogue prerequisite through the current explicit-template route");
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, "phrase");
        var uhdProfileId = await AddUhdProfileAsync(rig);
        rig.Event.League!.SearchQueryTemplate = "{EventTitle}";
        await rig.Db.SaveChangesAsync();
        AddCatalogue(rig);
        var result = await rig.AutomaticAsync(uhdProfileId);
        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(Probe);
            rig.Transport.Searches.SelectMany(search => search.Guids).Should().Equal("offer-legacy-hd", "offer-probe-uhd");
            rig.Transport.Searches.SelectMany(search => search.SuppliedEventIds).Should().OnlyContain(id => id == null);
            result.ReleasesFound.Should().Be(2);
            result.SelectedRelease.Should().Be(ProbeTitle);
            rig.Transport.DescriptorGuids.Should().Equal("offer-probe-uhd");
            rig.Transport.DescriptorAttempts.Should().Be(1);
            result.Success.Should().BeFalse();
            (await rig.Db.DownloadQueue.ToListAsync()).Should().BeEmpty();
        }
    }

    private static void AddCatalogue(MotorsportQueryHttpHarness rig)
    {
        rig.AddRelease(LegacyTitle, suppliedEventId: null, guid: "offer-legacy-hd");
        rig.AddRelease(ProbeTitle, suppliedEventId: null, guid: "offer-probe-uhd");
    }

    private static async Task<int> AddUhdProfileAsync(MotorsportQueryHttpHarness rig)
    {
        var profile = new QualityProfile
        {
            Name = "Query fixture UHD only",
            Items = new List<QualityItem> { new() { Name = "WEBDL-2160p", Quality = 19, Allowed = true } }
        };
        rig.Db.QualityProfiles.Add(profile);
        await rig.Db.SaveChangesAsync();
        return profile.Id;
    }
}
