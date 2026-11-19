using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #443. A German release for the Brazil race was rejected because the
/// "GERMAN" language tag counted as the place Germany. The German names
/// "Brasilien" and "Mexiko" were also unknown.
/// </summary>
public class LanguageTagLocationTests
{
    private readonly ReleaseMatchingService _matching = new(
        Mock.Of<ILogger<ReleaseMatchingService>>(),
        new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()),
        new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()));

    private readonly ReleaseMatchScorer _scorer = new();

    private const string GermanBrazilRelease = "Formel.1.2026.GP.Brasilien.Rennen.GERMAN.1080p.WEB.H264-GROUP";
    private const string GermanSaoPauloRelease = "F1.2026.Grosser.Preis.von.Sao.Paulo.Rennen.German.1080p.HDTV.x264-GROUP";
    private const string GermanMexicoRelease = "Formel.1.2026.GP.Mexiko.Rennen.GERMAN.1080p.WEB.H264-GROUP";

    [Theory]
    [InlineData(GermanBrazilRelease, "Sao Paulo Grand Prix - Race")]
    [InlineData(GermanBrazilRelease, "Brazilian Grand Prix - Race")]
    [InlineData(GermanSaoPauloRelease, "Sao Paulo Grand Prix - Race")]
    [InlineData(GermanMexicoRelease, "Mexico City Grand Prix - Race")]
    [InlineData(GermanMexicoRelease, "Mexican Grand Prix - Race")]
    public void GermanReleaseMatchesItsOwnRace(string release, string eventTitle)
    {
        var result = _matching.ValidateRelease(Release(release), Race(eventTitle));

        result.Rejections.Should().NotContain(reason => reason.Contains("Location mismatch"));
        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        result.IsMatch.Should().BeTrue($"confidence {result.Confidence}: {string.Join("; ", result.MatchReasons)}");
        result.Confidence.Should().BeGreaterThanOrEqualTo(65, string.Join("; ", result.MatchReasons));
        // Manual search approves a release that has no hard rejection and
        // reaches the minimum match score.
        _scorer.CalculateMatchScore(release, Race(eventTitle))
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Theory]
    [InlineData(GermanSaoPauloRelease, "Sao Paulo Grand Prix - Race")]
    [InlineData(GermanBrazilRelease, "Brazilian Grand Prix - Race")]
    [InlineData(GermanMexicoRelease, "Mexico City Grand Prix - Race")]
    public void RssPathAcceptsTheReportersTitles(string title, string eventTitle)
    {
        // RSS parses the title once and keeps only IsMatch results.
        var release = Release(title);
        var preParsed = _matching.ParseRelease(release.Title);

        var result = _matching.ValidateRelease(release, Race(eventTitle), preParsed: preParsed);

        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        result.IsMatch.Should().BeTrue($"confidence {result.Confidence}: {string.Join("; ", result.MatchReasons)}");
        result.MatchReasons.Should().Contain("Session type matches: Race");
        result.Confidence.Should().BeGreaterThanOrEqualTo(65, string.Join("; ", result.MatchReasons));
        preParsed.Organization.Should().Be("Formula 1");
    }

    [Fact]
    public void FormelBrasilienClearsTheThresholdWithAMargin()
    {
        // "Brasilien" names the event location, so it earns a location credit.
        var result = _matching.ValidateRelease(Release(GermanBrazilRelease), Race("Brazilian Grand Prix - Race"));

        result.MatchReasons.Should().Contain("Release names the event location");
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence + 5,
            string.Join("; ", result.MatchReasons));
    }

    [Fact]
    public void ItaliaAfterGpEarnsTheLocationCredit()
    {
        // No language tag here, so only "GP Italia" names the place.
        var result = _matching.ValidateRelease(
            Release("Formula.1.2026.GP.Italia.Gara.1080p.WEB.H264-GROUP"), Race("Italian Grand Prix - Race"));

        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain("Release names the event location");
        result.IsMatch.Should().BeTrue($"confidence {result.Confidence}: {string.Join("; ", result.MatchReasons)}");
    }

    [Theory]
    [InlineData("Formel.1.2026.GP.Brasilien.Rennen.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race", true)]
    [InlineData("Formel.1.2026.GP.Brasilien.Rennen.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Qualifying", false)]
    [InlineData("Formel.1.2026.GP.Brasilien.Qualifikation.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Qualifying", true)]
    [InlineData("Formel.1.2026.GP.Brasilien.Qualifikation.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race", false)]
    [InlineData("Formel.1.2026.GP.Brasilien.Qualifying.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Qualifying", true)]
    [InlineData("Formel.1.2026.GP.Brasilien.Sprint-Qualifikation.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Sprint Qualifying", true)]
    [InlineData("Formel.1.2026.GP.Brasilien.Sprint-Qualifikation.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Sprint", false)]
    [InlineData("Formel.1.2026.GP.Brasilien.Sprint.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Sprint", true)]
    [InlineData("Formel.1.2026.GP.Brasilien.Sprint.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race", false)]
    [InlineData("Formel.1.2026.GP.Brasilien.Freies.Training.1.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Free Practice 1", true)]
    [InlineData("Formel.1.2026.GP.Brasilien.Freies.Training.1.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race", false)]
    [InlineData("Formel.1.2026.GP.Brasilien.Training.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race", false)]
    [InlineData("Formel.1.2026.GP.Brasilien.Training.GERMAN.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Free Practice 1", false)]
    [InlineData("Formel.1.2026.GP.Japan.3.Freies.Training.GERMAN.720p.HDTV.x264-GRP", "Japanese Grand Prix - Free Practice 1", false)]
    [InlineData("Formel.1.2026.GP.Japan.3.Freies.Training.GERMAN.720p.HDTV.x264-GRP", "Japanese Grand Prix - Free Practice 3", true)]
    [InlineData("Formel.1.2026.GP.Japan.2.Training.GERMAN.720p.HDTV.x264-GRP", "Japanese Grand Prix - Free Practice 2", true)]
    [InlineData("Formula1.2026.Japan.Race.Training.Day.1080p.WEB.H264-GROUP", "Japanese Grand Prix - Race", true)]
    [InlineData("Formula1.2026.Japan.Qualifying.Training.1080p.WEB.H264-GROUP", "Japanese Grand Prix - Qualifying", true)]
    [InlineData("Formula1.2026.Japan.Qualifying.Training.1080p.WEB.H264-GROUP", "Japanese Grand Prix - Free Practice 1", false)]
    public void GermanSessionWordsPickTheRightSession(string title, string eventTitle, bool expected)
    {
        var release = Release(title);
        var result = _matching.ValidateRelease(release, Race(eventTitle), preParsed: _matching.ParseRelease(title));
        if (expected)
        {
            var score = _scorer.CalculateMatchScore(title, Race(eventTitle));
            result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
            result.IsMatch.Should().BeTrue($"confidence {result.Confidence}: {string.Join("; ", result.MatchReasons)}");
            score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
        }
        else
        {
            // The scorer compares practice as one coarse session, so only
            // the validator tells FP1 from FP3.
            result.IsHardRejection.Should().BeTrue();
            result.Rejections.Should().Contain(reason => reason.StartsWith("Session mismatch"));
        }
    }

    [Theory]
    [InlineData("Freies Training 2", "Practice 2")]
    [InlineData("Training 3", "Practice 3")]
    [InlineData("3. Freies Training", "Practice 3")]
    [InlineData("1 Training", "Practice 1")]
    [InlineData("Training", "Training")]
    [InlineData("Freies Training", "Training")]
    [InlineData("Round 3 Training", "Training")]
    [InlineData("Qualifikation", "Qualifying")]
    [InlineData("Sprint Qualifikation", "Sprint Qualifying")]
    [InlineData("Rennen", "Race")]
    public void GermanSessionWordsAreKnownInReleaseNames(string words, string session)
    {
        EventPartDetector.DetectMotorsportSessionFromFilename($"Formel.1.2026.GP.Brasilien.{words}.GERMAN.1080p")
            .Should().Be(session);
    }

    [Theory]
    [InlineData("Dakar.2026.Stage.3.Training.1080p")]
    [InlineData("Rallye.2026.Etappe.3.Training.1080p")]
    [InlineData("WRC.2026.SS.3.Training.1080p")]
    [InlineData("Rally.2026.WP.3.Training.1080p")]
    public void StageNumberIsNotAPracticeNumber(string title)
    {
        EventPartDetector.DetectGermanSession(title.ToLowerInvariant().Replace('.', ' ')).Should().Be("Training");
    }

    [Fact]
    public void FormelOneIsNotAPracticeNumber()
    {
        EventPartDetector.DetectMotorsportSessionFromFilename("Formel.1.Training.Japan.2026.GERMAN.1080p")
            .Should().Be("Training");
    }

    [Theory]
    [InlineData("Formula1.2026.Japan.Race.Training.Day.1080p", "Race")]
    [InlineData("Formula1.2026.Japan.Qualifying.Training.1080p", "Qualifying")]
    public void AnEnglishSessionWordWinsOverGerman(string title, string session)
    {
        EventPartDetector.DetectMotorsportSessionFromFilename(title).Should().Be(session);
    }

    [Fact]
    public void GermanPracticeNumberLinksALibraryFile()
    {
        // Library scan and file import read the session this way.
        EventPartDetector.DetectMotorsportSessionIdentity(
                "Formel 1 - 2026 - GP Japan - 3. Freies Training.mkv", "Formula 1", releaseTitle: true)
            .Should().Be("Practice 3");
    }

    [Fact]
    public void MotoGpBareTrainingIsNotTheFirstPractice()
    {
        var result = _matching.ValidateRelease(
            Release("MotoGP.2026.GP.Italien.Training.GERMAN.1080p.WEB.H264-GROUP"),
            MotoGp("Italian Grand Prix - Free Practice 1"));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain("Session mismatch: release is 'Training', event is 'Practice 1'");
    }

    [Theory]
    [InlineData("Japanese Grand Prix - Race")]
    [InlineData("Japanese Grand Prix - Qualifying")]
    [InlineData("Japanese Grand Prix - Sprint Qualifying")]
    [InlineData("Japanese Grand Prix - Free Practice 1")]
    [InlineData("Japanese Grand Prix - Practice 3")]
    [InlineData("Pre-Season Testing - Day 1")]
    public void GermanWordsDoNotReadEnglishEventTitles(string eventTitle)
    {
        // ReleaseEvaluator and the scorer also read event titles.
        EventPartDetector.DetectGermanSession(eventTitle.ToLowerInvariant().Replace('-', ' ')).Should().BeNull();
    }

    [Theory]
    [InlineData("Sao Paulo Grand Prix - Race", "Formula 1 2026 Round21 || Formula 1 2026 Sao Paulo || Formula 1 2026 || Formula1 2026 Round21 || Formula1 2026 Sao Paulo || Formula1 2026")]
    [InlineData("Sao Paulo Grand Prix - Qualifying", "Formula 1 2026 Round21 || Formula 1 2026 Sao Paulo || Formula 1 2026 || Formula1 2026 Round21 || Formula1 2026 Sao Paulo || Formula1 2026")]
    [InlineData("Sao Paulo Grand Prix - Sprint Qualifying", "Formula 1 2026 Round21 || Formula 1 2026 Sao Paulo || Formula 1 2026 || Formula1 2026 Round21 || Formula1 2026 Sao Paulo || Formula1 2026")]
    [InlineData("Brazilian Grand Prix", "Formula 1 2026 Round21 || Formula 1 2026 Brazilian || Formula 1 2026 Brazil || Formula 1 2026 || Formula1 2026 Round21 || Formula1 2026 Brazilian || Formula1 2026 Brazil || Formula1 2026")]
    public void SearchQueriesDoNotChange(string eventTitle, string expected)
    {
        // These values come from dev before the German session words.
        var queries = new EventQueryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<EventQueryService>.Instance);
        var evt = Race(eventTitle);
        evt.Round = "21";
        evt.Season = "2026";
        evt.SeasonNumber = 2026;

        string.Join(" || ", queries.BuildEventQueries(evt)).Should().Be(expected);
        queries.BuildQueryFromTemplate("F1 {Year} German", evt).Should().Be("F1 2026 German");
    }

    [Theory]
    [InlineData("Formel.1.2026.GP.Spanien.Rennen.GERMAN.1080p.WEB.H264-GROUP", "Spanish Grand Prix - Race")]
    [InlineData("Formel.1.2026.GP.Oesterreich.Rennen.GERMAN.1080p.WEB.H264-GROUP", "Austrian Grand Prix - Race")]
    [InlineData("Formel.1.2026.GP.Ungarn.Rennen.GERMAN.1080p.WEB.H264-GROUP", "Hungarian Grand Prix - Race")]
    [InlineData("Formel.1.2026.GP.Belgien.Rennen.GERMAN.1080p.WEB.H264-GROUP", "Belgian Grand Prix - Race")]
    [InlineData("Formel.1.2026.GP.Niederlande.Rennen.GERMAN.1080p.WEB.H264-GROUP", "Dutch Grand Prix - Race")]
    [InlineData("Formel.1.2026.GP.Grossbritannien.Rennen.GERMAN.1080p.WEB.H264-GROUP", "British Grand Prix - Race")]
    [InlineData("Formel.1.2026.GP.Kanada.Rennen.GERMAN.1080p.WEB.H264-GROUP", "Canadian Grand Prix - Race")]
    [InlineData("Formel.1.2026.GP.Aserbaidschan.Rennen.GERMAN.1080p.WEB.H264-GROUP", "Azerbaijan Grand Prix - Race")]
    [InlineData("Formel.1.2026.GP.Katar.Rennen.GERMAN.1080p.WEB.H264-GROUP", "Qatar Grand Prix - Race")]
    [InlineData("Formel.1.2026.GP.Italien.Rennen.GERMAN.1080p.WEB.H264-GROUP", "Italian Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Brasil.Course.FRENCH.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Messico.Gara.ITALIAN.1080p.WEB.H264-GROUP", "Mexico City Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Giappone.Gara.SPANISH.1080p.WEB.H264-GROUP", "Japanese Grand Prix - Race")]
    public void ForeignNameOfTheRaceIsNotAMismatch(string release, string eventTitle)
    {
        var result = _matching.ValidateRelease(Release(release), Race(eventTitle));

        result.Rejections.Should().NotContain(reason => reason.Contains("Location mismatch"));
        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        result.IsMatch.Should().BeTrue($"confidence {result.Confidence}: {string.Join("; ", result.MatchReasons)}");
        _scorer.CalculateMatchScore(release, Race(eventTitle))
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Theory]
    [InlineData("FRENCH")]
    [InlineData("ITALIAN")]
    [InlineData("SPANISH")]
    [InlineData("DUTCH")]
    [InlineData("PORTUGUESE")]
    [InlineData("HUNGARIAN")]
    [InlineData("JAPANESE")]
    [InlineData("CHINESE")]
    [InlineData("TURKISH")]
    [InlineData("German")]
    public void LanguageTagAloneIsNotAPlace(string language)
    {
        // The place word is unknown, so only the language tag is left.
        var release = $"Formula.1.2026.GP.Brazilie.Race.{language}.1080p.WEB.H264-GROUP";

        var result = _matching.ValidateRelease(Release(release), Race("Brazilian Grand Prix - Race"));

        result.Rejections.Should().NotContain(reason => reason.Contains("Location mismatch"));
        _scorer.CalculateMatchScore(release, Race("Brazilian Grand Prix - Race"))
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Theory]
    [InlineData("Formel.1.2026.Grosser.Preis.von.Deutschland.Rennen.GERMAN.1080p.WEB.H264-GROUP")]
    [InlineData("Formula.1.2026.Germany.Race.GERMAN.1080p.WEB.H264-GROUP")]
    [InlineData("Formula.1.2026.German.Grand.Prix.Race.1080p.WEB.H264-GROUP")]
    [InlineData("Formula.1.2026.German.GP.Race.1080p.WEB.H264-GROUP")]
    [InlineData("Formula.1.2026.Hockenheim.Race.ITALIAN.1080p.WEB.H264-GROUP")]
    [InlineData("Formel.1.2026.GP.Mexiko.Rennen.GERMAN.1080p.WEB.H264-GROUP")]
    [InlineData("Formula.1.2026.Spanish.GP.Race.SPANISH.1080p.WEB.H264-GROUP")]
    [InlineData("Formula.1.2026.Chinese.Sprint.1080p.WEB.H264-GROUP")]
    [InlineData("Formula.1.2026.Hungarian.Race.1080p.WEB.H264-GROUP")]
    [InlineData("Formula.1.2026.Japanese.Qualifying.1080p.WEB.H264-GROUP")]
    [InlineData("Formula1.2026.Hungarian.Sky.Sports.Race.1080p.WEB.H264-GROUP")]
    [InlineData("Formula1.2026.Spanish.F1TV.Race.1080p.WEB.H264-GROUP")]
    [InlineData("Formula1.2026.Italian.RAI.Race.1080p.WEB.H264-GROUP")]
    [InlineData("Formula1.2026.Japanese.DAZN.Qualifying.1080p.WEB.H264-GROUP")]
    [InlineData("Formula1.2026.Chinese.SKY.Sprint.1080p.WEB.H264-GROUP")]
    [InlineData("Formula1.2026.Hungarian.NF.Race.1080p.WEB.H264-GROUP")]
    [InlineData("Formula1.2026.Round03.Japanese-GP")]
    public void RealWrongPlaceIsStillRejected(string release)
    {
        var result = _matching.ValidateRelease(Release(release), Race("Brazilian Grand Prix - Race"));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Location mismatch"));
        _scorer.CalculateMatchScore(release, Race("Brazilian Grand Prix - Race")).Should().Be(0);
    }

    [Fact]
    public void GermanBrazilReleaseIsRejectedForTheGermanRace()
    {
        var result = _matching.ValidateRelease(Release(GermanBrazilRelease), Race("German Grand Prix - Race"));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Location mismatch"));
    }

    [Theory]
    [InlineData("GERMAN.1080p", 0, 6, true)]
    [InlineData("rennen german", 7, 6, true)]
    [InlineData("german grand prix", 0, 6, false)]
    [InlineData("german gp race", 0, 6, false)]
    [InlineData("germany race", 0, 7, false)]
    [InlineData("chinese sprint", 0, 7, false)]
    [InlineData("hungarian free practice 1", 0, 9, false)]
    [InlineData("italian motogp race", 0, 7, false)]
    [InlineData("rennen german web h264", 7, 6, true)]
    [InlineData("rennen german sub", 7, 6, true)]
    [InlineData("rennen german eng 1080p", 7, 6, true)]
    [InlineData("hungarian sky sports race", 0, 9, false)]
    [InlineData("spanish f1tv race", 0, 7, false)]
    [InlineData("german hdr10", 0, 6, true)]
    [InlineData("german 10bit", 0, 6, true)]
    [InlineData("german ac3d", 0, 6, true)]
    [InlineData("german dvbrip", 0, 6, true)]
    [InlineData("german ml", 0, 6, true)]
    [InlineData("german amzn", 0, 6, true)]
    [InlineData("formel 1 2026 gp brasilien rennen german", 34, 6, true)]
    [InlineData("brasilien", 0, 9, false)]
    public void LanguageTagDependsOnTheNextWord(string title, int index, int length, bool expected)
    {
        LanguageDetector.IsLanguageTag(title, index, length).Should().Be(expected);
    }

    [Fact]
    public void LanguageTagAfterTheSessionIsATag()
    {
        // "Race.Hungarian.1080p" puts the language in the tag position.
        const string release = "Formula1.2026.Brazil.Race.Hungarian.1080p.WEB.H264-GROUP";

        var result = _matching.ValidateRelease(Release(release), Race("Brazilian Grand Prix - Race"));

        result.Rejections.Should().NotContain(reason => reason.Contains("Location mismatch"));
        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
    }

    [Theory]
    [InlineData("German.HDR10.1080p")]
    [InlineData("German.10bit.1080p")]
    [InlineData("German.AC3D.1080p")]
    [InlineData("German.DVBRip.x264")]
    [InlineData("German.ML.1080p")]
    [InlineData("German.AMZN.WEB")]
    public void ReleaseWordsAfterGermanKeepItATag(string trailer)
    {
        var release = $"Formel.1.2026.GP.Brazilie.Rennen.{trailer}-GROUP";

        var result = _matching.ValidateRelease(Release(release), Race("Brazilian Grand Prix - Race"));

        result.Rejections.Should().NotContain(reason => reason.Contains("Location mismatch"));
    }

    [Theory]
    [InlineData("Formula1.2026.GP.Monaco.Gara.Sky.Sport.Italia.1080p.WEB-GRP", "Italian Grand Prix - Race")]
    [InlineData("Formula1.2026.Brazil.Race.Sky.Sport.Italia.1080p.WEB-GRP", "Italian Grand Prix - Race")]
    [InlineData("Formula1.2026.Brazil.Race.Sky.Deutschland.1080p.WEB-GRP", "German Grand Prix - Race")]
    [InlineData("Formula1.2026.Mexico.Race.DAZN.Espana.1080p.WEB-GRP", "Spanish Grand Prix - Race")]
    [InlineData("Formula1.2026.Japan.Race.ESPN.Brasil.1080p.WEB-GRP", "Brazilian Grand Prix - Race")]
    public void BroadcasterNameDoesNotProveTheRace(string release, string eventTitle)
    {
        var result = _matching.ValidateRelease(Release(release), Race(eventTitle));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Location mismatch"));
        _scorer.CalculateMatchScore(release, Race(eventTitle)).Should().Be(0);
    }

    [Theory]
    [InlineData("Formula1.2026.GP.Brasile.Gara.Sky.Sport.Italia.1080p.WEB-GRP", "Brazilian Grand Prix - Race")]
    [InlineData("Formula1.2026.Brazil.Race.Sky.Deutschland.1080p.WEB-GRP", "Brazilian Grand Prix - Race")]
    [InlineData("Formel.1.2026.GP.Brasilien.Rennen.GERMAN.Sky.Deutschland.1080p.WEB-GRP", "Brazilian Grand Prix - Race")]
    [InlineData("Formula1.2026.GP.Brasil.Corrida.Sky.Sport.Italia.1080p.WEB-GRP", "Brazilian Grand Prix - Race")]
    public void BroadcasterNameDoesNotRejectTheRightRace(string release, string eventTitle)
    {
        var result = _matching.ValidateRelease(Release(release), Race(eventTitle));

        result.Rejections.Should().NotContain(reason => reason.Contains("Location mismatch"));
        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        _scorer.CalculateMatchScore(release, Race(eventTitle))
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Theory]
    [InlineData("Formula1.2026.Round08.Gara.Sky.Sport.Italia.1080p.WEB-GRP")]
    [InlineData("Formula1.2026.Round08.Rennen.Sky.Deutschland.1080p.WEB-GRP")]
    [InlineData("Formula1.2026.Round08.Carrera.DAZN.Espana.1080p.WEB-GRP")]
    public void BroadcasterNameAloneIsNotAPlace(string release)
    {
        var evt = Race("Monaco Grand Prix - Race");
        evt.Round = "8";

        var result = _matching.ValidateRelease(Release(release), evt);

        result.Rejections.Should().NotContain(reason => reason.Contains("Location mismatch"));
        _scorer.CalculateMatchScore(release, evt).Should().BeGreaterThan(0);
    }

    [Fact]
    public void GroupAfterLanguageTagIsRemoved()
    {
        LanguageDetector.RemoveGroupAfterLanguage("Formel.1.2026.GP.Brasilien.Rennen.GERMAN-GROUP")
            .Should().Be("Formel.1.2026.GP.Brasilien.Rennen.GERMAN");
        LanguageDetector.RemoveGroupAfterLanguage("Formula.1.2026.Brazil.Race.1080p.WEB.H264-GROUP")
            .Should().Be("Formula.1.2026.Brazil.Race.1080p.WEB.H264-GROUP");
        LanguageDetector.RemoveGroupAfterLanguage("Formula1.2026.Round03.Japanese-GP")
            .Should().Be("Formula1.2026.Round03.Japanese-GP");
    }

    [Fact]
    public void GermanReleaseWithGroupRightAfterTheTagMatches()
    {
        const string release = "Formel.1.2026.GP.Brasilien.Rennen.GERMAN-GROUP";

        var result = _matching.ValidateRelease(Release(release), Race("Brazilian Grand Prix - Race"));

        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        _scorer.CalculateMatchScore(release, Race("Brazilian Grand Prix - Race"))
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Theory]
    [InlineData("Formula1.2026.Hungarian.Free.Practice.1.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Free Practice 1")]
    [InlineData("Formula1.2026.Hungarian.FP1.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Free Practice 1")]
    [InlineData("Formula1.2026.Japanese.Q1.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Qualifying")]
    [InlineData("Formula1.2026.Dutch.Qualy.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Qualifying")]
    public void NationalityBeforeASessionIsAPlace(string release, string eventTitle)
    {
        var result = _matching.ValidateRelease(Release(release), Race(eventTitle));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Location mismatch"));
        _scorer.CalculateMatchScore(release, Race(eventTitle)).Should().Be(0);
    }

    [Fact]
    public void NationalityBeforeMotoGpIsAPlace()
    {
        const string release = "MotoGP.2026.Italian.MotoGP.Race.1080p.WEB.H264-GROUP";

        var result = _matching.ValidateRelease(Release(release), MotoGp("Spain Grand Prix - Race"));

        result.IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(release, MotoGp("Spain Grand Prix - Race")).Should().Be(0);
    }

    [Theory]
    [InlineData("Formula.1.2026.GP.Bahrein.Carrera.SPANISH.1080p.WEB.H264-GROUP", "Bahrain Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Catar.Carrera.SPANISH.1080p.WEB.H264-GROUP", "Qatar Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Abu.Dabi.Carrera.SPANISH.1080p.WEB.H264-GROUP", "Abu Dhabi Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Abou.Dhabi.Course.FRENCH.1080p.WEB.H264-GROUP", "Abu Dhabi Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Azerbaiyan.Carrera.SPANISH.1080p.WEB.H264-GROUP", "Azerbaijan Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Azerbaijao.Corrida.PORTUGUESE.1080p.WEB.H264-GROUP", "Azerbaijan Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.San.Paolo.Gara.ITALIAN.1080p.WEB.H264-GROUP", "Sao Paulo Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Espanha.Corrida.PORTUGUESE.1080p.WEB.H264-GROUP", "Spanish Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Holanda.Corrida.PORTUGUESE.1080p.WEB.H264-GROUP", "Dutch Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Japao.Corrida.PORTUGUESE.1080p.WEB.H264-GROUP", "Japanese Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Gra.Bretanha.Corrida.PORTUGUESE.1080p.WEB.H264-GROUP", "British Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Cingapura.Corrida.PORTUGUESE.1080p.WEB.H264-GROUP", "Singapore Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Italia.Gara.1080p.WEB.H264-GROUP", "Italian Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Brasile.Gara.Sky.Sport.Italia.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race")]
    public void SpanishPortugueseAndItalianNamesMatchTheirOwnRace(string release, string eventTitle)
    {
        var result = _matching.ValidateRelease(Release(release), Race(eventTitle));

        result.Rejections.Should().NotContain(reason => reason.Contains("Location mismatch"));
        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        result.IsMatch.Should().BeTrue($"confidence {result.Confidence}: {string.Join("; ", result.MatchReasons)}");
        _scorer.CalculateMatchScore(release, Race(eventTitle))
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Theory]
    [InlineData("Formula.1.2026.GP.Bahrein.Carrera.SPANISH.1080p.WEB.H264-GROUP", "Australian Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Catar.Carrera.SPANISH.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Abu.Dabi.Carrera.SPANISH.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Japao.Corrida.PORTUGUESE.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Espanha.Corrida.PORTUGUESE.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Cingapura.Corrida.PORTUGUESE.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race")]
    [InlineData("Formula.1.2026.GP.Italia.Gara.1080p.WEB.H264-GROUP", "Brazilian Grand Prix - Race")]
    public void SpanishPortugueseAndItalianNamesOfAnotherRaceAreRejected(string release, string eventTitle)
    {
        var result = _matching.ValidateRelease(Release(release), Race(eventTitle));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Location mismatch"));
        _scorer.CalculateMatchScore(release, Race(eventTitle)).Should().Be(0);
    }

    [Theory]
    [InlineData("MotoGP.2026.Gran.Premio.de.Valencia.Espana.Carrera.SPANISH.1080p.WEB.H264-GROUP", "Valencia Grand Prix - Race")]
    [InlineData("MotoGP.2026.Gran.Premio.de.la.Comunitat.Valenciana.Carrera.SPANISH.1080p.WEB.H264-GROUP", "Valencia Grand Prix - Race")]
    [InlineData("MotoGP.2026.Gran.Premio.de.Espana.Jerez.Carrera.SPANISH.1080p.WEB.H264-GROUP", "Spain Grand Prix - Race")]
    [InlineData("MotoGP.2026.Gran.Premio.de.Aragon.Espana.Carrera.SPANISH.1080p.WEB.H264-GROUP", "Aragon Grand Prix - Race")]
    [InlineData("MotoGP.2026.Gran.Premio.di.San.Marino.Misano.Gara.ITALIAN.1080p.WEB.H264-GROUP", "San Marino Grand Prix - Race")]
    [InlineData("MotoGP.2026.Gran.Premio.d.Italia.Mugello.Gara.ITALIAN.1080p.WEB.H264-GROUP", "Italian Grand Prix - Race")]
    [InlineData("MotoGP.2026.Gran.Premio.de.Espana.Carrera.SPANISH.1080p.WEB.H264-GROUP", "Spain Grand Prix - Race")]
    [InlineData("MotoGP.2026.Gran.Premio.di.Spagna.Gara.ITALIAN.1080p.WEB.H264-GROUP", "Spain Grand Prix - Race")]
    [InlineData("MotoGP.2026.Gran.Premio.d.Italia.Gara.ITALIAN.1080p.WEB.H264-GROUP", "Italian Grand Prix - Race")]
    [InlineData("MotoGP.2026.Grosser.Preis.von.Deutschland.Rennen.GERMAN.1080p.WEB.H264-GROUP", "Germany Grand Prix - Race")]
    [InlineData("MotoGP.2026.Grande.Premio.do.Brasil.Corrida.PORTUGUESE.1080p.WEB.H264-GROUP", "Brazil Grand Prix - Race")]
    [InlineData("MotoGP.2026.Qatar.Race.Sky.Sport.Italia.1080p.WEB.H264-GROUP", "Qatar Grand Prix - Race")]
    public void MotoGpSpanishAndItalianNamesFindTheirOwnRound(string release, string eventTitle)
    {
        var result = _matching.ValidateRelease(Release(release), MotoGp(eventTitle));

        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain("MotoGP Grand Prix location matches");
    }

    [Fact]
    public void MotoGpValenciaReleaseIsRejectedForTheSpanishRound()
    {
        var result = _matching.ValidateRelease(
            Release("MotoGP.2026.Gran.Premio.de.Valencia.Espana.Carrera.SPANISH.1080p.WEB.H264-GROUP"),
            MotoGp("Spain Grand Prix - Race"));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Location mismatch"));
    }

    [Theory]
    [InlineData("Formel.1.2026.GP.Brasilien.Rennen.GERMAN", "Formula 1", true)]
    [InlineData("Formel.2.2026.Brasilien.Rennen.GERMAN", "Formula 1", false)]
    [InlineData("Formel.1.2026.GP.Brasilien.Rennen.GERMAN", "F1 Academy", false)]
    [InlineData("Formel.1.2026.GP.Brasilien.Rennen.GERMAN", "Formula 2", false)]
    [InlineData("Formel.E.2026.Berlin.ePrix.Rennen.GERMAN", "Formula E", true)]
    [InlineData("Formel.E.2026.Berlin.ePrix.Rennen.GERMAN", "Formula 1", false)]
    public void FormelNamesOnlyItsOwnLeague(string title, string leagueName, bool expected)
    {
        var league = new League { Name = leagueName, Sport = "Motorsport" };

        ReleaseMatchingService.TitleNamesLeague(title, league).Should().Be(expected);
    }

    [Theory]
    [InlineData("Formel.2.2026.Monaco.Rennen.GERMAN.1080p.WEB.H264-GRP", "Monaco Grand Prix - Race")]
    [InlineData("Formula.2.2026.Monaco.Race.1080p.WEB.H264-GRP", "Monaco Grand Prix - Race")]
    [InlineData("Formel2.2026.Monaco.Rennen.GERMAN.1080p.WEB.H264-GRP", "Monaco Grand Prix - Race")]
    [InlineData("Formel.3.2026.Monaco.Rennen.GERMAN.1080p.WEB.H264-GRP", "Monaco Grand Prix - Race")]
    [InlineData("Formula.3.2026.Monaco.Race.1080p.WEB.H264-GRP", "Monaco Grand Prix - Race")]
    [InlineData("Formel.E.2026.Monaco.E-Prix.Rennen.GERMAN.1080p.WEB.H264-GRP", "Monaco Grand Prix - Race")]
    [InlineData("Formula.E.2026.Monaco.E-Prix.Race.1080p.WEB.H264-GRP", "Monaco Grand Prix - Race")]
    [InlineData("FormelE.2026.Monaco.E-Prix.Rennen.GERMAN.1080p.WEB.H264-GRP", "Monaco Grand Prix - Race")]
    [InlineData("Formel.E.2026.Mexiko.City.E-Prix.Rennen.GERMAN.1080p.WEB.H264-GRP", "Mexico City Grand Prix - Race")]
    [InlineData("Formula.E.2026.Mexico.City.E-Prix.Race.1080p.WEB.H264-GRP", "Mexico City Grand Prix - Race")]
    [InlineData("Formel.1.Academy.2026.Monaco.Rennen.GERMAN.1080p.WEB.H264-GRP", "Monaco Grand Prix - Race")]
    public void OtherFormulaSeriesAreADifferentSport(string title, string eventTitle)
    {
        var release = Release(title);
        var result = _matching.ValidateRelease(release, Race(eventTitle), preParsed: _matching.ParseRelease(title));

        result.IsHardRejection.Should().BeTrue();
        result.IsMatch.Should().BeFalse();
        result.Rejections.Should().Contain(reason => reason.StartsWith("Different sport detected"));
    }

    [Theory]
    [InlineData("Formel.1.2026.Runde.5.Rennen.GERMAN.1080p.WEB.H264-GRP", "5", false)]
    [InlineData("Formel.1.2026.Runde.5.Rennen.GERMAN.1080p.WEB.H264-GRP", "3", true)]
    [InlineData("Formel.1.2026.Runde05.Rennen.GERMAN.1080p.WEB.H264-GRP", "3", true)]
    [InlineData("Formula1.2026.Round05.Race.1080p.WEB.H264-GRP", "3", true)]
    public void RundeIsARoundNumber(string title, string eventRound, bool rejected)
    {
        var evt = Race("Brazilian Grand Prix - Race");
        evt.Round = eventRound;

        var result = _matching.ValidateRelease(Release(title), evt, preParsed: _matching.ParseRelease(title));

        if (rejected)
        {
            result.IsHardRejection.Should().BeTrue();
            result.Rejections.Should().Contain(reason => reason.StartsWith("Round mismatch"));
        }
        else
        {
            result.Rejections.Should().NotContain(reason => reason.StartsWith("Round mismatch"));
        }
    }

    [Fact]
    public void SessionWordAfterTheLastDashIsNotAGroup()
    {
        var result = _matching.ValidateRelease(
            Release("Formula1.2026.Hungarian-Race"), Race("Brazilian Grand Prix - Race"));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Location mismatch"));
        ReleaseMatchingService.NormalizeTitle("Formula1.2026.Hungarian-Race").Should().EndWith("Hungarian Race");
        ReleaseMatchingService.NormalizeTitle("Formula1.2026.Brazil.Race-GROUP").Should().EndWith("Brazil Race");
    }

    [Fact]
    public void FormelNeedsAWordStart()
    {
        _matching.ParseRelease("Kinderformel1.2026.Brazil.Race.1080p").Organization.Should().NotBe("Formula 1");
        _matching.ParseRelease("Formel1.2026.Brazil.Race.1080p").Organization.Should().Be("Formula 1");
    }

    [Fact]
    public void MotoGpGermanNameMatchesTheSpanishRound()
    {
        var result = _matching.ValidateRelease(
            Release("MotoGP.2026.Grosser.Preis.von.Spanien.Rennen.GERMAN.1080p.WEB.H264-GROUP"),
            MotoGp("Spain Grand Prix - Race"));

        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        result.MatchReasons.Should().Contain("MotoGP Grand Prix location matches");
    }

    [Fact]
    public void MotoGpGermanNameOfAnotherRoundIsRejected()
    {
        var result = _matching.ValidateRelease(
            Release("MotoGP.2026.Grosser.Preis.von.Oesterreich.Rennen.GERMAN.1080p.WEB.H264-GROUP"),
            MotoGp("Spain Grand Prix - Race"));

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(reason => reason.Contains("Location mismatch"));
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "https://example.invalid/" + title,
        Indexer = "Fixture"
    };

    private static Event Race(string title) => new()
    {
        Title = title,
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 11, 8, 17, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "Formula 1", Sport = "Motorsport" }
    };

    private static Event MotoGp(string title) => new()
    {
        Title = title,
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 4, 26, 12, 0, 0, DateTimeKind.Utc),
        League = new League { Name = "MotoGP", Sport = "Motorsport" }
    };
}
