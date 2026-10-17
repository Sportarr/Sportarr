using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class MotoGpFrenchIdentityTests
{
    private readonly ReleaseMatchScorer _scorer = new();
    private readonly ReleaseMatchingService _matcher = new(
        Mock.Of<ILogger<ReleaseMatchingService>>(),
        new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
        new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()));

    private static Event MotoGpEvent(string title, string location) => new()
    {
        Id = 310,
        Title = title,
        Sport = "Motorsport",
        Season = "2026",
        EventDate = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc),
        Location = location,
        League = new League { Id = 310, Name = "MotoGP", Sport = "Motorsport" }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test"
    };

    [Fact]
    public void CzechRaceIsRejectedForAustriaRaceInBothSearchMatchers()
    {
        const string title = "MotoGP.Grand.Prix.De.Tchequie.2026.Course.VFF.1080p.WEB.EAC3.x264-PiXeL";
        var evt = MotoGpEvent("Austria GP - Race", "Red Bull Ring");

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Fact]
    public void AustrianPracticeIsRejectedForAustriaRaceInBothSearchMatchers()
    {
        const string title = "MotoGP.Grand.Prix.D.Autriche.2026.Essais.Libres.1.VFF.1080p.WEB.EAC3.x264-PiXeL";
        var evt = MotoGpEvent("Austria GP - Race", "Red Bull Ring");

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Fact]
    public void AustrianRaceWithPodiumMatchesTheAustriaRaceInBothSearchMatchers()
    {
        const string title = "MotoGP.Grand.Prix.D.Autriche.2026.Course+Podium.VFF.1080p.WEB.EAC3.x264-PiXeL";
        var evt = MotoGpEvent("Austria GP - Race", "Red Bull Ring");

        _matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void LocationlessYearOnlyRaceIsRejectedForAustriaRaceInBothSearchMatchers()
    {
        const string title = "MotoGP.La.Course.2026.VFF.1080p.WEBRip.AAC.2.0.x264-DN55";
        var evt = MotoGpEvent("Austria GP - Race", "Red Bull Ring");

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Theory]
    [InlineData("Austria", "Autriche")]
    [InlineData("San Marino", "Saint.Marin")]
    [InlineData("Aragon", "Aragon")]
    [InlineData("Great Britain", "Grande.Bretagne")]
    [InlineData("Germany", "Allemagne")]
    [InlineData("Netherlands", "Pays.Bas")]
    [InlineData("Czechia", "Tchequie")]
    [InlineData("Hungary", "Hongrie")]
    [InlineData("Catalonia", "Catalogne")]
    [InlineData("France", "France")]
    [InlineData("Spain", "Espagne")]
    [InlineData("Americas", "Ameriques")]
    [InlineData("Brazil", "Bresil")]
    [InlineData("Italy", "Italie")]
    [InlineData("Thailand", "Thailande")]
    [InlineData("Portugal", "Portugal")]
    [InlineData("Valencia", "Valence")]
    [InlineData("Qatar", "Qatar")]
    [InlineData("Japan", "Japon")]
    [InlineData("Australia", "Australie")]
    [InlineData("Malaysia", "Malaisie")]
    [InlineData("Indonesia", "Indonesie")]
    [InlineData("Argentina", "Argentine")]
    public void FrenchGrandPrixNamesMatchTheirOwnEvent(string eventPlace, string frenchPlace)
    {
        var title = $"MotoGP.Grand.Prix.De.{frenchPlace}.2026.Course.VFF.1080p.WEB.EAC3.x264-PiXeL";
        var evt = MotoGpEvent($"{eventPlace} GP - Race", eventPlace);

        _matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("San Marino GP - Race", "Misano", "Italie")]
    [InlineData("Aragon GP - Race", "Motorland", "Espagne")]
    [InlineData("Catalonia GP - Race", "Barcelona", "Espagne")]
    public void DifferentGrandPrixInTheSameCountryIsRejected(string eventTitle, string venue, string releasePlace)
    {
        var title = $"MotoGP.Grand.Prix.D.{releasePlace}.2026.Course.VFF.1080p.WEB.EAC3.x264-PiXeL";
        var evt = MotoGpEvent(eventTitle, venue);

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Fact]
    public void LocationlessReleaseWithMatchingRoundKeepsItsRoundIdentity()
    {
        const string title = "MotoGP.2026.Round14.Course.VFF.1080p.WEB.EAC3.x264-PiXeL";
        var evt = MotoGpEvent("San Marino GP - Race", "Misano");
        evt.Round = "14";

        _matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void LocationlessReleaseWithMatchingDateKeepsItsDateIdentity()
    {
        const string title = "MotoGP.La.Course.GP.06.09.2026.VFF.1080p.WEBRip.AAC.x264-DN55";
        var evt = MotoGpEvent("San Marino GP - Race", "Misano");

        _matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void FrenchAudioTagDoesNotTurnAPlaceFreeReleaseIntoTheFranceGrandPrix()
    {
        const string title = "[FRENCH] MotoGP.La.Course.2026.VFF.1080p.WEBRip.AAC.x264-DN55";
        var evt = MotoGpEvent("France GP - Race", "Le Mans");

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Theory]
    [InlineData("Essais")]
    [InlineData("Essais.Qualificatifs")]
    [InlineData("La.Grille")]
    [InlineData("Warm.Up")]
    [InlineData("Course.Sprint")]
    public void NonRaceFrenchSessionIsRejectedForTheRace(string session)
    {
        var title = $"MotoGP.Grand.Prix.D.Autriche.2026.{session}.VFF.1080p.WEB.EAC3.x264-PiXeL";
        var evt = MotoGpEvent("Austria GP - Race", "Red Bull Ring");

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Fact]
    public void FrenchSprintMatchesOnlyTheSprintAtItsOwnGrandPrix()
    {
        const string title = "MotoGP.Grand.Prix.D.Autriche.2026.Course.Sprint.VFF.1080p.WEB.EAC3.x264-PiXeL";
        var evt = MotoGpEvent("Austria - Sprint Race", "Red Bull Ring");

        _matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);

        var otherGp = MotoGpEvent("Czechia - Sprint Race", "Brno");
        _matcher.ValidateRelease(Release(title), otherGp).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, otherGp).Should().Be(0);
    }

    [Fact]
    public void ExactSportarrIdStillWinsOverTheFrenchGrandPrixName()
    {
        const string title = "MotoGP.Grand.Prix.De.Tchequie.2026.Course.{sportarr-ev-0000310}.VFF.1080p.WEB.EAC3.x264-PiXeL";
        var evt = MotoGpEvent("Austria GP - Race", "Red Bull Ring");
        evt.ExternalId = "ev-0000310";

        _matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(100);
    }

    [Fact]
    public void LocationlessYearOnlyRaceIsRejectedForValencia()
    {
        const string title = "MotoGP.La.Course.2026.VFF.1080p.WEBRip.AAC.x264-DN55";
        var evt = MotoGpEvent("Valencia GP - Race", "Ricardo Tormo");

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Fact]
    public void GridShowIsNotAcceptedForQualifying()
    {
        const string title = "MotoGP.Grand.Prix.D.Autriche.2026.La.Grille.VFF.1080p.WEB.EAC3.x264-PiXeL";
        var evt = MotoGpEvent("Austria GP - Qualifying", "Red Bull Ring");

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Fact]
    public void FutureGrandPrixNeedsMoreThanYearAndSession()
    {
        const string title = "MotoGP.La.Course.2026.VFF.1080p.WEBRip.AAC.x264-DN55";
        var evt = MotoGpEvent("New Zealand GP - Race", "Hampton Downs");

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Fact]
    public void FutureRaceWithoutGpInTheEventTitleNeedsLocation()
    {
        const string title = "MotoGP.La.Course.2026.VFF.1080p.WEBRip.AAC.x264-DN55";
        var evt = MotoGpEvent("New Zealand - Race", "Hampton Downs");

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Fact]
    public void FutureGrandPrixCanMatchItsNamedLocation()
    {
        const string title = "MotoGP.New.Zealand.GP.2026.Race.1080p.WEB-DL";
        var evt = MotoGpEvent("New Zealand GP - Race", "Hampton Downs");

        _matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Theory]
    [InlineData("Shakedown Test 1", "Shakedown.Test.1")]
    [InlineData("Shakedown Test 2", "Shakedown.Test.2")]
    [InlineData("Shakedown Test 3", "Shakedown.Test.3")]
    [InlineData("Test 1", "Test.1")]
    [InlineData("Test 2", "Test.2")]
    [InlineData("Test 3", "Test.3")]
    public void GrandPrixLocationGuardDoesNotCoverPreseasonTests(string session, string releaseSession)
    {
        var evt = MotoGpEvent($"Sepang - {session}", "Sepang");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 4;
        var releaseTitle = $"MotoGP.2026.{releaseSession}.1080p.WEB-DL";

        MotoGpGrandPrixIdentity.Evaluate(releaseTitle, evt, null, null)
            .Should().Be(MotoGpGrandPrixMatch.NotApplicable);
    }

    [Theory]
    [InlineData("Practice 1", "FP1", "FP2")]
    [InlineData("Practice 2", "FP2", "FP3")]
    [InlineData("Practice 3", "FP3", "Race")]
    [InlineData("Warm Up", "Warm.Up", "Race")]
    [InlineData("Sprint", "Sprint", "Race")]
    [InlineData("Qualifying 1", "Q1", "Q2")]
    [InlineData("Qualifying 2", "Q2", "Q1")]
    [InlineData("Qualifying", "Qualifying", "Race")]
    [InlineData("Race", "Race", "Sprint")]
    public void GrandPrixWeekendSessionsKeepVenueAndSessionIdentity(
        string eventSession, string matchingSession, string otherSession)
    {
        var evt = MotoGpEvent($"Austria GP - {eventSession}", "Red Bull Ring");
        var matchingTitle = $"MotoGP.2026.Austria.{matchingSession}.1080p.WEB-DL";
        var wrongSessionTitle = $"MotoGP.2026.Austria.{otherSession}.1080p.WEB-DL";
        var missingPlaceTitle = $"MotoGP.2026.{matchingSession}.1080p.WEB-DL";

        var matchingResult = _matcher.ValidateRelease(Release(matchingTitle), evt);
        matchingResult.IsMatch.Should().BeTrue(
            $"confidence {matchingResult.Confidence}; rejections {string.Join("; ", matchingResult.Rejections)}; reasons {string.Join("; ", matchingResult.MatchReasons)}");
        _scorer.CalculateMatchScore(matchingTitle, evt).Should()
            .BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);

        _matcher.ValidateRelease(Release(wrongSessionTitle), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(wrongSessionTitle, evt).Should().Be(0);

        _matcher.ValidateRelease(Release(missingPlaceTitle), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(missingPlaceTitle, evt).Should().Be(0);
    }

    [Theory]
    [InlineData(2025, 1)]
    [InlineData(2026, 88)]
    [InlineData(2027, 250)]
    public void SeasonEpisodeNumbersAloneNeverIdentifyAGrandPrix(int year, int episode)
    {
        var evt = MotoGpEvent("Austria GP - Race", "Red Bull Ring");
        evt.Season = year.ToString();
        evt.SeasonNumber = year;
        evt.EpisodeNumber = episode;
        evt.EventDate = new DateTime(year, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        var title = $"MotoGP.S{year}E{episode:D2}.Race.1080p.WEB-DL";

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Theory]
    [InlineData(2025, 2, 41)]
    [InlineData(2026, 88, 89)]
    [InlineData(2027, 250, 8)]
    public void NamedGrandPrixStillMatchesWhenIndexerEpisodeNumberDiffers(
        int year, int sportarrEpisode, int indexerEpisode)
    {
        var evt = MotoGpEvent("Austria GP - Race", "Red Bull Ring");
        evt.Season = year.ToString();
        evt.SeasonNumber = year;
        evt.EpisodeNumber = sportarrEpisode;
        evt.EventDate = new DateTime(year, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        var title = $"MotoGP.S{year}E{indexerEpisode:D2}.Austria.Race.1080p.WEB-DL";

        _matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should()
            .BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }

    [Fact]
    public void WrongSeasonIsRejectedEvenWithTheRightGrandPrixAndSession()
    {
        const string title = "MotoGP.S2025E88.Austria.Race.1080p.WEB-DL";
        var evt = MotoGpEvent("Austria GP - Race", "Red Bull Ring");
        evt.SeasonNumber = 2026;
        evt.EpisodeNumber = 88;

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Fact]
    public void ScheduledWorldSupersportWarmUpIsNotTreatedAsAShow()
    {
        var evt = MotoGpEvent("Australian Round Warm Up 2", "Phillip Island");
        evt.League = new League { Id = 311, Name = "WorldSSP", Sport = "Motorsport" };
        const string title = "WorldSSP.2026.Australian.Round.Warm.Up.2.1080p.WEB-DL";

        _matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
    }

    [Fact]
    public void WeekendWarmUpShowCannotFillTheScheduledMotoGpWarmUp()
    {
        var evt = MotoGpEvent("Austria GP - Warm Up", "Red Bull Ring");
        const string title = "MotoGP.2026.Austria.Weekend.Warm.Up.Show.1080p.WEB-DL";

        _matcher.ValidateRelease(Release(title), evt).Rejections.Should()
            .Contain(reason => reason == "Non-event content detected: Warm-up Show");
    }

    [Fact]
    public void FormulaOneWarmUpShowRemainsNonEventContent()
    {
        var evt = MotoGpEvent("Austrian Grand Prix - Race", "Red Bull Ring");
        evt.League = new League { Id = 312, Name = "Formula 1", Sport = "Motorsport" };
        const string title = "Formula1.2026.Austria.Weekend.Warm.Up.1080p.WEB-DL";

        _matcher.ValidateRelease(Release(title), evt).Rejections.Should()
            .Contain(reason => reason == "Non-event content detected: Warm-up Show");
    }

    [Theory]
    [InlineData("Road.Course")]
    [InlineData("Road..Course")]
    [InlineData("Road.-.Course")]
    public void RoadCourseQualifyingIsNotReadAsTheFrenchRaceSession(string venue)
    {
        var title = $"IndyCar.2026.Indianapolis.{venue}.Qualifying.1080p.WEB-DL";

        EventPartDetector.DetectMotorsportSessionFromFilename(title, "IndyCar Series")
            .Should().Be("Qualifying");
    }

    [Fact]
    public void UsaBroadcasterTagDoesNotIdentifyTheAmericasGrandPrix()
    {
        const string title = "MotoGP.2026.Race.USA.Network.1080p.WEB-DL";
        var evt = MotoGpEvent("Americas GP - Race", "Circuit of the Americas");

        _matcher.ValidateRelease(Release(title), evt).IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should().Be(0);
    }

    [Fact]
    public void UsaGrandPrixNameStillIdentifiesTheAmericasRound()
    {
        const string title = "MotoGP.2026.USA.GP.Race.1080p.WEB-DL";
        var evt = MotoGpEvent("Americas GP - Race", "Circuit of the Americas");

        _matcher.ValidateRelease(Release(title), evt).IsMatch.Should().BeTrue();
        _scorer.CalculateMatchScore(title, evt).Should()
            .BeGreaterThanOrEqualTo(ReleaseMatchScorer.AutoGrabMatchScore);
    }
}
