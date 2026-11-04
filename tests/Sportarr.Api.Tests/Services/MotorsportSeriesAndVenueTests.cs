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

    [Theory]
    [InlineData("Miami Grand Prix - Race", "Sky Sports _ Formula1_2026_United_States_Grand_Prix_UNCUT_1080p_AHDTV_x264-DARKSPORT")]
    [InlineData("Miami Grand Prix - Race", "Formula1.2026.United.States.Grand.Prix.UNCUT.1080p.AHDTV.x264-DARKSPORT")]
    [InlineData("Miami Grand Prix - Race", "Formula1.2026.Round18.USA.COTA.Race.SKY.F1TV.WEB-DL.1080p.H264.English-MWR")]
    [InlineData("Las Vegas Grand Prix - Race", "Formula1.2026.United.States.Grand.Prix.UNCUT.1080p.AHDTV.x264-DARKSPORT")]
    [InlineData("Emilia Romagna Grand Prix - Race", "Formula1.2026.Italian.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("Italian Grand Prix - Race", "Formula1.2026.Imola.Race.1080p.WEB.h264")]
    public void The_country_race_is_not_taken_for_another_race_in_that_country(string eventTitle, string releaseTitle)
    {
        // Issue #419. The United States Grand Prix release was grabbed for the
        // Miami race every backlog pass, because Miami implied its country.
        var evt = MotorsportEvent("Formula 1", eventTitle);
        var release = Rel(releaseTitle);

        var result = _svc.ValidateRelease(release, evt, null, false);

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Location mismatch"));
    }

    [Theory]
    [InlineData("United States Grand Prix - Race", "Formula1.2026.USA.Miami.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("United States Grand Prix - Race", "Formula1.2026.United.States.Miami.Race.1080p.WEB.h264")]
    [InlineData("United States Grand Prix - Race", "Formula1.2026.USA.Las.Vegas.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("United States Grand Prix - Race", "Formula1.2026.American.Vegas.Race.1080p.WEB.h264")]
    [InlineData("Italian Grand Prix - Race", "Formula1.2026.Italy.Imola.Race.1080p.WEB.h264")]
    [InlineData("Italian Grand Prix - Race", "Formula1.2026.Italian.Emilia.Romagna.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("Miami Grand Prix - Race", "Formula1.2026.USA.Las.Vegas.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("Las Vegas Grand Prix - Race", "Formula1.2026.USA.Miami.Grand.Prix.Race.1080p.WEB.h264")]
    public void The_country_race_rejects_another_race_in_that_country_even_with_the_country_named(string eventTitle, string releaseTitle)
    {
        // Issue #419, reverse direction. "USA Miami" names the country, but
        // it is the Miami race, not the Austin race.
        var evt = MotorsportEvent("Formula 1", eventTitle);
        var release = Rel(releaseTitle);

        var result = _svc.ValidateRelease(release, evt, null, false);

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Location mismatch"));
    }

    [Theory]
    [InlineData("Miami Grand Prix - Race", "Formula1.2026.USA.Miami.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("Miami Grand Prix - Race", "Formula1.2026.United.States.Miami.Race.1080p.WEB.h264")]
    [InlineData("Las Vegas Grand Prix - Race", "Formula1.2026.United.States.Las.Vegas.Race.1080p.WEB.h264")]
    [InlineData("United States Grand Prix - Race", "Formula1.2026.USA.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("United States Grand Prix - Race", "Formula1.2026.USA.Austin.Race.1080p.WEB.h264")]
    [InlineData("United States Grand Prix - Race", "Formula1.2026.United.States.COTA.Race.1080p.WEB.h264")]
    [InlineData("Italian Grand Prix - Race", "Formula1.2026.Italy.Monza.Race.1080p.WEB.h264")]
    [InlineData("Italian Grand Prix - Race", "Formula1.2026.Italian.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("Emilia Romagna Grand Prix - Race", "Formula1.2026.Italian.Emilia.Romagna.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("Miami Grand Prix - Race", "Formula1.2026.Miami.Grand.Prix.UNCUT.1080p.AHDTV.x264-DARKSPORT")]
    [InlineData("Las Vegas Grand Prix - Race", "Formula1.2026.USA.Las.Vegas.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("United States Grand Prix - Race", "Formula1.2026.Round18.USA.COTA.Race.SKY.F1TV.WEB-DL.1080p.H264.English-MWR")]
    [InlineData("United States Grand Prix - Race", "Formula1.2026.United.States.Grand.Prix.UNCUT.1080p.AHDTV.x264-DARKSPORT")]
    [InlineData("Emilia Romagna Grand Prix - Race", "Formula1.2026.Italy.Imola.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("Italian Grand Prix - Race", "Formula1.2026.Monza.Race.1080p.WEB.h264")]
    public void A_release_that_names_the_race_still_matches(string eventTitle, string releaseTitle)
    {
        var evt = MotorsportEvent("Formula 1", eventTitle);
        var release = Rel(releaseTitle);

        var result = _svc.ValidateRelease(release, evt, null, false);

        result.Rejections.Should().NotContain(r => r.Contains("Location mismatch"));
    }

    [Theory]
    [InlineData("Australian Grand Prix - Race")]
    [InlineData("Austrian Grand Prix - Race")]
    [InlineData("Russian Grand Prix - Race")]
    public void A_country_with_us_inside_its_name_is_not_the_united_states(string eventTitle)
    {
        // "US" is inside "Australian", "Austrian" and "Russian". These events
        // took a United States Grand Prix release as their own.
        var evt = MotorsportEvent("Formula 1", eventTitle);
        var release = Rel("Formula1.2026.United.States.Grand.Prix.Race.1080p.WEB.h264");

        var result = _svc.ValidateRelease(release, evt, null, false);

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Location mismatch"));
    }

    [Theory]
    [InlineData("NASCAR Xfinity Series", "Focused Health 302", "NASCAR.Xfinity.Series.2026.Round05.Focused.Health.302.Las.Vegas.USA.720p.WEB.h264")]
    [InlineData("NASCAR Cup Series", "Busch Light Clash", "NASCAR.Cup.Series.2026.Busch.Light.Clash.Las.Vegas.USA.720p.WEB.h264")]
    [InlineData("IndyCar Series", "Grand Prix of Houston", "IndyCar.2026.Grand.Prix.of.Houston.Las.Vegas.USA.720p.WEB.h264")]
    public void An_event_with_us_inside_a_word_is_not_a_united_states_event(string leagueName, string eventTitle, string releaseTitle)
    {
        // "US" is inside "Focused", "Busch" and "Houston". These events became
        // United States events and rejected a release that names Las Vegas.
        var evt = MotorsportEvent(leagueName, eventTitle);
        var release = Rel(releaseTitle);

        var result = _svc.ValidateRelease(release, evt, null, false);

        result.IsHardRejection.Should().BeFalse();
        result.Rejections.Should().NotContain(r => r.Contains("Location mismatch"));
    }

    [Theory]
    [InlineData("United States Grand Prix - Race", "Formula1.2026.Mexico.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("US Grand Prix - Race", "Formula1.2026.Mexico.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("USA Grand Prix - Race", "Formula1.2026.Mexico.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("US Grand Prix - Race", "Formula1.2026.USA.Miami.Grand.Prix.Race.1080p.WEB.h264")]
    public void A_real_united_states_event_still_rejects_another_race(string eventTitle, string releaseTitle)
    {
        var evt = MotorsportEvent("Formula 1", eventTitle);
        var release = Rel(releaseTitle);

        var result = _svc.ValidateRelease(release, evt, null, false);

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Location mismatch"));
    }

    [Theory]
    [InlineData("US Grand Prix - Race", "Formula1.2026.USA.Austin.Race.1080p.WEB.h264")]
    [InlineData("USA Grand Prix - Race", "Formula1.2026.United.States.Grand.Prix.Race.1080p.WEB.h264")]
    [InlineData("United States Grand Prix - Race", "Formula1.2005.Indianapolis.Race.720p.WEB.h264")]
    public void A_real_united_states_event_still_takes_its_own_release(string eventTitle, string releaseTitle)
    {
        // "India" is inside "Indianapolis", the old home of this race.
        var evt = MotorsportEvent("Formula 1", eventTitle);
        var release = Rel(releaseTitle);

        var result = _svc.ValidateRelease(release, evt, null, false);

        result.IsHardRejection.Should().BeFalse();
        result.Rejections.Should().NotContain(r => r.Contains("Location mismatch"));
    }

    [Fact]
    public void An_americas_event_is_still_a_united_states_event()
    {
        // Whole-word matching does not find "America" inside "Americas".
        var evt = MotorsportEvent("IndyCar Series", "Grand Prix of the Americas");
        var release = Rel("IndyCar.2026.Grand.Prix.of.Mexico.720p.WEB.h264");

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
