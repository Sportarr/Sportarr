using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Services;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class MotorsportQueryMinimumAgeTests(ITestOutputHelper output)
{
    private const string Probe = "Silverstone Grand Prix Practice 1";
    private const string OldProbeTitle = "2026.07.03.Formula.1.Round.6.Silverstone.Grand.Prix.Practice.1.1080p.WEB-DL.H264-GROUP";
    private static readonly string[] Legacy =
    {
        "Formula 1 2026 Round06", "Formula 1 2026 Silverstone", "Formula 1 2026",
        "Formula1 2026 Round06", "Formula1 2026 Silverstone", "Formula1 2026"
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MinimumAgeHoldSuppressesProbeOnlyForAnOtherwiseValidLegacyCandidate(bool wrongSession)
    {
        var legacyTitle = $"Formula.1.2026.Round06.Silverstone.Grand.Prix.Practice.{(wrongSession ? 2 : 1)}.2026.07.03.1080p.WEB-DL.H264-GROUP";
        await VerifyOldLegacyIdentityAsync(legacyTitle, wrongSession);
        await VerifyOldProbeAsync();
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, "phrase");
        await SetMinimumAgeAsync(rig);
        rig.AddRelease(legacyTitle, null, "offer-young-legacy");
        rig.Transport.Releases.Single().PublishDate = DateTime.UtcNow.AddMinutes(-5);
        rig.AddRelease(OldProbeTitle, null, "offer-old-probe");
        rig.Transport.Releases.Single(row => row.Guid == "offer-old-probe").PublishDate = DateTime.UtcNow.AddDays(-2);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            WrongSession = wrongSession, MinimumAgeMinutes = 60,
            Catalogue = rig.Transport.Releases.Select(row => new { row.Guid, row.Title, row.PublishDate, row.SportarrEventId })
        }));
        var result = await rig.AutomaticAsync();
        using (new AssertionScope())
        {
            rig.Transport.Searches.Select(search => search.Query).Should().Equal(
                wrongSession ? Legacy.Append(Probe) : Legacy);
            rig.Transport.Searches.Where(search => search.Query != Probe).SelectMany(search => search.Guids)
                .Distinct().Should().Equal("offer-young-legacy");
            rig.Transport.DescriptorGuids.Should().NotContain("offer-young-legacy");
            if (wrongSession)
            {
                rig.Transport.Searches.Where(search => search.Query == Probe).SelectMany(search => search.Guids)
                    .Should().Equal("offer-old-probe");
                result.SelectedRelease.Should().Be(OldProbeTitle);
                result.ReleasesFound.Should().Be(2);
                rig.Transport.DescriptorGuids.Should().Equal("offer-old-probe");
                rig.Transport.DescriptorAttempts.Should().Be(1);
            }
            else
            {
                rig.Transport.Searches.Should().NotContain(search => search.Query == Probe);
                result.SelectedRelease.Should().BeNull();
                result.ReleasesFound.Should().Be(1);
                rig.Transport.DescriptorAttempts.Should().Be(0);
            }
            result.Success.Should().BeFalse();
            (await rig.Db.DownloadQueue.CountAsync()).Should().Be(0);
            rig.Transport.Violations.Should().BeEmpty();
        }
    }

    private async Task VerifyOldLegacyIdentityAsync(string title, bool wrongSession)
    {
        output.WriteLine("Old legacy release identity prerequisite through an explicit template with the same minimum-age setting");
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, "phrase");
        await SetMinimumAgeAsync(rig);
        rig.Event.League!.SearchQueryTemplate = Legacy[0];
        await rig.Db.SaveChangesAsync();
        rig.AddRelease(title, null, "offer-old-legacy-prerequisite");
        rig.Transport.Releases.Single().PublishDate = DateTime.UtcNow.AddDays(-2);
        var fixtureRow = rig.Transport.Releases.Single();
        var score = rig.Services.GetRequiredService<ReleaseMatchScorer>().CalculateMatchScore(fixtureRow.Title, rig.Event);
        var match = rig.Services.GetRequiredService<ReleaseMatchingService>().ValidateRelease(fixtureRow, rig.Event, null, false);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { IdentityPrerequisiteScore = score, match.IsHardRejection, match.Rejections }));
        score.Should().BeGreaterThanOrEqualTo(50);
        match.IsHardRejection.Should().Be(wrongSession);
        if (wrongSession) match.Rejections.Should().Contain(reason => reason.StartsWith("Session mismatch:"));
        var result = await rig.AutomaticAsync();
        rig.Transport.Searches.Select(search => search.Query).Should().Equal(Legacy[0]);
        rig.Transport.Searches.SelectMany(search => search.Guids).Should().Equal("offer-old-legacy-prerequisite");
        result.ReleasesFound.Should().Be(1);
        result.Success.Should().BeFalse();
        if (wrongSession)
        {
            result.SelectedRelease.Should().BeNull();
            rig.Transport.DescriptorAttempts.Should().Be(0);
        }
        else
        {
            result.SelectedRelease.Should().Be(title);
            rig.Transport.DescriptorGuids.Should().Equal("offer-old-legacy-prerequisite");
        }
        (await rig.Db.DownloadQueue.CountAsync()).Should().Be(0);
        rig.Transport.Violations.Should().BeEmpty();
    }

    private async Task VerifyOldProbeAsync()
    {
        output.WriteLine("Old metadata-only target prerequisite through an explicit template with the same minimum-age setting");
        await using var rig = await MotorsportQueryHttpHarness.CreateAsync(output, "phrase");
        await SetMinimumAgeAsync(rig);
        rig.Event.League!.SearchQueryTemplate = Probe;
        await rig.Db.SaveChangesAsync();
        rig.AddRelease(OldProbeTitle, null, "offer-old-probe-prerequisite");
        rig.Transport.Releases.Single().PublishDate = DateTime.UtcNow.AddDays(-2);
        var result = await rig.AutomaticAsync();
        rig.Transport.Searches.Select(search => search.Query).Should().Equal(Probe);
        rig.Transport.Searches.SelectMany(search => search.Guids).Should().Equal("offer-old-probe-prerequisite");
        result.SelectedRelease.Should().Be(OldProbeTitle);
        result.Success.Should().BeFalse();
        rig.Transport.DescriptorGuids.Should().Equal("offer-old-probe-prerequisite");
        (await rig.Db.DownloadQueue.CountAsync()).Should().Be(0);
        rig.Transport.Violations.Should().BeEmpty();
    }

    private static async Task SetMinimumAgeAsync(MotorsportQueryHttpHarness rig)
    {
        var service = rig.Services.GetRequiredService<ConfigService>();
        var config = await service.GetConfigAsync();
        config.IndexerMinimumAgeMinutes = 60;
        await service.SaveConfigAsync(config);
    }
}
