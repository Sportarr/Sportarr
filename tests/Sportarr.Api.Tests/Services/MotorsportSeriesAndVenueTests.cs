using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Two motorsport matching faults that each let the wrong thing through or
/// kept the right thing out.
/// </summary>
public class MotorsportSeriesAndVenueTests
{
    private readonly ReleaseMatchingService _svc;

    public MotorsportSeriesAndVenueTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _svc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event MotorsportEvent(string leagueName, string title) => new()
    {
        Id = 1,
        Title = title,
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = leagueName, Sport = "Motorsport" },
    };

    [Fact]
    public void An_f1_academy_release_is_not_rejected_as_formula_1()
    {
        // The bare F1 pattern used to fire on the very same "F1" the more
        // specific F1 Academy pattern had already claimed for the event.
        var evt = MotorsportEvent("F1 Academy", "Miami Grand Prix");
        var release = Rel("F1.Academy.2026.Round.05.Miami.Race.1080p.WEB.h264");

        var result = _svc.ValidateRelease(release, evt, null, false);

        result.Rejections.Should().NotContain(r => r.Contains("Formula1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_bare_f1_token_does_not_reject_an_f1_academy_release()
    {
        // "F1 Academy" contains "F1", so the broad F1 identifier counts as this
        // event's own series wherever it appears in the name. A trailing bare
        // token is that same series again, not a Formula 1 bundle riding along.
        var evt = MotorsportEvent("F1 Academy", "Miami Grand Prix");
        var release = Rel("F1.Academy.2026.Round.05.Miami.Race.1080p.WEB.h264.Plus.F1");

        var result = _svc.ValidateRelease(release, evt, null, false);

        result.Rejections.Should().NotContain(r => r.Contains("Different sport detected"));
    }

    [Fact]
    public void A_plain_f1_academy_release_naming_its_round_twice_is_still_accepted()
    {
        // The same token appearing more than once is that token again, not a
        // second series, so repeated mentions must not start rejecting.
        var evt = MotorsportEvent("F1 Academy", "Miami Grand Prix");
        var release = Rel("F1.Academy.2026.Miami.F1.Academy.Race.1080p.WEB.h264");

        var result = _svc.ValidateRelease(release, evt, null, false);

        result.Rejections.Should().NotContain(r => r.Contains("Formula1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_release_for_another_race_in_the_same_country_is_rejected()
    {
        // Miami and Las Vegas are separate Grands Prix that share a country.
        // Inheriting siblings through that shared parent let one pass
        // validation for the other.
        var evt = MotorsportEvent("Formula 1", "Miami Grand Prix");
        var release = Rel("Formula1.2026.Las.Vegas.Grand.Prix.Race.1080p.WEB.h264");

        var result = _svc.ValidateRelease(release, evt, null, false);

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Location mismatch"));
    }

    [Fact]
    public void The_circuit_name_still_matches_its_own_country_event()
    {
        // A release naming the circuit is compatible with an event named after
        // the country. That direction has to keep working.
        var evt = MotorsportEvent("Formula 1", "British Grand Prix");
        var release = Rel("Formula1.2026.Silverstone.Race.1080p.WEB.h264");

        var result = _svc.ValidateRelease(release, evt, null, false);

        result.Rejections.Should().NotContain(r => r.Contains("Location mismatch"));
    }
}
