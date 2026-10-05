using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class MotorsportQueryIdentityBaselineTests(ITestOutputHelper output)
{
    private const string SilverstoneMetadata = "Silverstone Grand Prix Practice 1";
    private const string SilverstoneRelease = "Formula.1.2026.07.03.Round.6.Silverstone.Grand.Prix.Practice.1.1080p.WEB-DL.H264-GROUP";

    [Fact]
    public async Task RetrievedWrongYearCannotBeSelectedByTheMetadataTitleTemplate()
    {
        await VerifyGuardAsync(new Catalogue("year", "phrase", SilverstoneMetadata, SilverstoneRelease,
            "Formula.1.2025.07.03.Round.6.Silverstone.Grand.Prix.Practice.1.1080p.WEB-DL.H264-GROUP"));
    }

    [Fact]
    public async Task RetrievedWrongPracticeOrdinalCannotBeSelectedByTheMetadataTitleTemplate()
    {
        await VerifyGuardAsync(new Catalogue("session", "and", SilverstoneMetadata, SilverstoneRelease,
            "Formula.1.2026.07.03.Round.6.Silverstone.Grand.Prix.Practice.2.1080p.WEB-DL.H264-GROUP"));
    }

    [Fact]
    public async Task RetrievedWrongCircuitCannotBeSelectedByTheMetadataTitleTemplate()
    {
        await VerifyGuardAsync(new Catalogue("meeting", "phrase", "Spanish Grand Prix Practice 1",
            "Formula.1.2026.07.03.Round.6.Barcelona.Spanish.Grand.Prix.Practice.1.1080p.WEB-DL.H264-GROUP",
            "Formula.1.2026.07.03.Round.6.Madrid.Spanish.Grand.Prix.Practice.1.1080p.WEB-DL.H264-GROUP",
            Venue: "Barcelona"));
    }

    [Fact]
    public async Task RetrievedContradictoryIdCannotBeSelectedDespiteAnIdenticalTitle()
    {
        await VerifyGuardAsync(new Catalogue("id", "phrase", SilverstoneMetadata,
            SilverstoneRelease, SilverstoneRelease, WrongSuppliedId: "ev-2336999"));
    }

    private async Task VerifyGuardAsync(Catalogue catalogue)
    {
        var together = await ObserveAsync(catalogue, includeCorrect: true);
        var wrongAlone = await ObserveAsync(catalogue, includeCorrect: false);
        var correctGuid = "offer-correct-" + catalogue.Label;
        var wrongGuid = "offer-wrong-" + catalogue.Label;

        using (new AssertionScope())
        {
            together.Searches.Select(search => search.Query).Should().Equal(catalogue.MetadataTitle);
            together.Searches.SelectMany(search => search.Guids).Should().Equal(wrongGuid, correctGuid);
            together.Searches.SelectMany(search => search.SuppliedEventIds).Should().Equal(new string?[] { catalogue.WrongSuppliedId, null });
            together.Result.ReleasesFound.Should().Be(2);
            together.Result.SelectedRelease.Should().Be(catalogue.CorrectTitle);
            together.DescriptorGuids.Should().Equal(correctGuid);
            together.DescriptorAttempts.Should().Be(1);
            together.Result.Success.Should().BeFalse();
            together.QueueRows.Should().Be(0);

            wrongAlone.Searches.Select(search => search.Query).Should().Equal(catalogue.MetadataTitle);
            wrongAlone.Searches.SelectMany(search => search.Guids).Should().Equal(wrongGuid);
            wrongAlone.Searches.SelectMany(search => search.SuppliedEventIds).Should().Equal(new string?[] { catalogue.WrongSuppliedId });
            wrongAlone.Result.ReleasesFound.Should().Be(1);
            wrongAlone.Result.SelectedRelease.Should().BeNullOrEmpty();
            wrongAlone.DescriptorGuids.Should().BeEmpty();
            wrongAlone.DescriptorAttempts.Should().Be(0);
            wrongAlone.Result.Success.Should().BeFalse();
            wrongAlone.QueueRows.Should().Be(0);
        }
    }

    private async Task<Observation> ObserveAsync(Catalogue catalogue, bool includeCorrect)
    {
        output.WriteLine($"Catalogue {catalogue.Label}; phase {(includeCorrect ? "together" : "wrong-alone")}");
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, catalogue.Mode);
        rig.Event.Title = catalogue.MetadataTitle;
        rig.Event.Venue = catalogue.Venue;
        rig.Event.Location = catalogue.Venue;
        rig.Event.League!.SearchQueryTemplate = "{EventTitle}";
        await rig.Db.SaveChangesAsync();
        rig.AddRelease(catalogue.WrongTitle, suppliedEventId: catalogue.WrongSuppliedId, guid: "offer-wrong-" + catalogue.Label);
        if (includeCorrect)
            rig.AddRelease(catalogue.CorrectTitle, suppliedEventId: null, guid: "offer-correct-" + catalogue.Label);

        var result = await rig.AutomaticAsync();
        return new Observation(result, rig.Transport.Searches, rig.Transport.DescriptorGuids,
            rig.Transport.DescriptorAttempts, await rig.Db.DownloadQueue.CountAsync());
    }

    private sealed record Catalogue(string Label, string Mode, string MetadataTitle, string CorrectTitle,
        string WrongTitle, string? Venue = null, string? WrongSuppliedId = null);

    private sealed record Observation(AutomaticSearchResult Result, MotorsportQueryHttpHarness.Source.QueryEvidence[] Searches,
        string[] DescriptorGuids, int DescriptorAttempts, int QueueRows);
}
