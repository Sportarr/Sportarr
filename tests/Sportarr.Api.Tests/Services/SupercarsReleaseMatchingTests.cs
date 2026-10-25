using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #257. Supercars releases are named two ways and neither matches the
/// event titles the metadata gives us. Every title below was taken from a real
/// indexer response, not invented.
///
///   Supercars 2026 Round09 Ipswich Race 3 ...   round + race within the round
///   Supercars 2025 Race 25 Ipswich 10 08 ...    race counted across the season
///   Supercars 2024 Adelaide 500 Race 23 ...     venue event + season race
///
/// The library counts races across the season, so round 9 race 3 is race 28.
/// </summary>
public class SupercarsReleaseMatchingTests
{
    private readonly ReleaseMatchingService _matchingSvc;
    private readonly ReleaseMatchScorer _scorer = new();

    public SupercarsReleaseMatchingTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _matchingSvc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event Race(string title, string round, int raceNumber, int year, int month, int day) => new()
    {
        Id = raceNumber,
        Title = title,
        Sport = "Motorsport",
        EventDate = new DateTime(year, month, day, 4, 0, 0, DateTimeKind.Utc),
        Location = "AU",
        Round = round,
        Season = year.ToString(),
        EpisodeNumber = raceNumber,
        League = new League { Id = 1, Name = "Supercars", Sport = "Motorsport" },
    };

    // Round 9 of 2026 holds races 26, 27 and 28, so the release's "Race 3" is race 28.
    private static Event Ipswich2026Race28() =>
        Race("Century Batteries Ipswich Super 440 - Race 28", "9", 28, 2026, 8, 23);

    private static Event Ipswich2025Race25() =>
        Race("Century Batteries Ipswich Super 440 - Race 25", "8", 25, 2025, 8, 10);

    private static Event Adelaide2024Race23() =>
        Race("VAILO Adelaide 500 - Race 23", "12", 23, 2024, 11, 16);

    // Round 9 holds races 26, 27 and 28. The caller reads them from the
    // library, because "Race 3" means nothing without them.
    private static readonly int[] Round9Races = { 26, 27, 28 };

    [Fact]
    public void RoundAndRaceWithinTheRound_MatchesTheSeasonRace()
    {
        var release = Rel("Supercars 2026 Round09 Ipswich Race 3 2160p FoxSports WEB DL DD H265 English");

        var result = _matchingSvc.ValidateRelease(release, Ipswich2026Race28(), roundRaceNumbers: Round9Races);

        result.IsHardRejection.Should().BeFalse("round 9 race 3 is race 28");
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void RoundAndRaceWithinTheRound_DoesNotMatchAnotherRaceOfThatRound()
    {
        var release = Rel("Supercars 2026 Round09 Ipswich Race 1 2160p FoxSports WEB DL DD H265 English");

        var result = _matchingSvc.ValidateRelease(release, Ipswich2026Race28(), roundRaceNumbers: Round9Races);

        result.IsHardRejection.Should().BeTrue("race 1 of round 9 is race 26, a different event");
    }

    [Fact]
    public void SeasonRaceNumber_MatchesTheEventThatCarriesIt()
    {
        var release = Rel("Supercars 2025 Race 25 Ipswich 10 08 1080p EN");

        var result = _matchingSvc.ValidateRelease(release, Ipswich2025Race25());

        result.IsHardRejection.Should().BeFalse();
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void SeasonRaceNumber_DoesNotMatchANeighbouringRace()
    {
        var release = Rel("Supercars 2025 Race 24 Ipswich 09 08 1080p EN");

        _matchingSvc.ValidateRelease(release, Ipswich2025Race25())
            .IsHardRejection.Should().BeTrue("race 24 is its own event");
    }

    [Fact]
    public void AVenueEventNameAndSeasonRaceNumber_Match()
    {
        var release = Rel("Supercars 2024 Adelaide 500 Race 23 16 11 1080p EN");

        var result = _matchingSvc.ValidateRelease(release, Adelaide2024Race23());

        result.IsHardRejection.Should().BeFalse();
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void WithoutTheRoundsRacesAPerRoundReleaseIsNotRejected()
    {
        // A caller that cannot say which races the round holds leaves the
        // question open rather than rejecting a release that may well be right.
        var release = Rel("Supercars 2026 Round09 Ipswich Race 3 2160p FoxSports WEB DL DD H265 English");

        _matchingSvc.ValidateRelease(release, Ipswich2026Race28())
            .IsHardRejection.Should().BeFalse();
    }

    [Fact]
    public void AnEventNameHoldingAThousandIsNotADate()
    {
        // "Bathurst 1000 13 10" read 1000 as the year, retried as 1000-10-13,
        // and hard-rejected every Bathurst release as 374,009 days out.
        var release = Rel("Supercars 2024 Race 20 Bathurst 1000 13 10 1080p EN");
        var evt = Race("Repco Bathurst 1000 - Race 20", "9", 20, 2024, 10, 13);

        var result = _matchingSvc.ValidateRelease(release, evt);

        result.IsHardRejection.Should().BeFalse("the biggest race of the season is not from the year 1000");
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Fact]
    public void OneFileHoldingTwoRacesMatchesBoth()
    {
        var release = Rel("Supercars 2026 Races 26 and 27 Ipswich 22 08 1080p EN");

        foreach (var race in new[] { 26, 27 })
        {
            _matchingSvc.ValidateRelease(release, Race($"Century Batteries Ipswich Super 440 - Race {race}", "9", race, 2026, 8, 22))
                .IsHardRejection.Should().BeFalse($"the file holds race {race}");
        }

        _matchingSvc.ValidateRelease(release, Ipswich2026Race28())
            .IsHardRejection.Should().BeTrue("race 28 is not in that file");
    }

    [Fact]
    public void TheRaceNumberScoresTheRightEventHigher()
    {
        const string release = "Supercars 2025 Race 25 Ipswich 10 08 1080p EN";

        var right = _scorer.CalculateMatchScore(release, Ipswich2025Race25());
        var wrong = _scorer.CalculateMatchScore(release, Race(
            "Century Batteries Ipswich Super 440 - Race 24", "8", 24, 2025, 8, 9));

        right.Should().BeGreaterThan(wrong, "the race number is the only thing telling them apart");
    }

    [Fact]
    public void RoundRelativeRaceScoresOnlyItsSeasonRace()
    {
        const string release = "Supercars 2026 Round09 Ipswich Race 3 2160p FoxSports WEB DL DD H265 English";
        var wanted = Ipswich2026Race28();
        var sibling = Race("Century Batteries Ipswich Super 440 - Race 26", "9", 26, 2026, 8, 21);

        _scorer.CalculateMatchScore(release, wanted, roundRaceNumbers: Round9Races)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
        _scorer.CalculateMatchScore(release, sibling, roundRaceNumbers: Round9Races)
            .Should().Be(0);
    }

    [Fact]
    public void RoundRelativeRaceSeparatesImportCandidates()
    {
        const string release = "Supercars 2026 Round09 Ipswich Race 3 2160p FoxSports WEB DL DD H265 English";
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var parsed = parser.Parse(release);
        parsed.RoundNumber.Should().Be(9);
        var service = ImportMatchingTestHarness.Service();
        var wanted = Ipswich2026Race28();
        var sibling = Race("Century Batteries Ipswich Super 440 - Race 26", "9", 26, 2026, 8, 21);

        service.ScoreMatch(parsed.EventTitle ?? release, wanted.Title, null, wanted, parsed, Round9Races).Core
            .Should().BeGreaterThanOrEqualTo(50);
        service.ScoreMatch(parsed.EventTitle ?? release, sibling.Title, null, sibling, parsed, Round9Races).Core
            .Should().BeLessOrEqualTo(0);
    }

    [Fact]
    public void ASingleRaceRoundWithoutALocalRaceNumberIsImportable()
    {
        const string release = "Supercars 2026 Round10 Tailem Bend Race 2160p FoxSports WEB DL H265 English";
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var parsed = parser.Parse(release);
        var evt = Race("AirTouch 500 at The Bend - Race 29", "10", 29, 2026, 9, 13);

        ImportMatchingTestHarness.Service()
            .ScoreMatch(parsed.EventTitle ?? release, evt.Title, null, evt, parsed, new[] { 29 }).Core
            .Should().BeGreaterThanOrEqualTo(50);
    }

    [Theory]
    [InlineData("Supercars 2026 Round09 Ipswich Race 2160p FoxSports WEB DL H265 English")]
    [InlineData("Supercars 2026 Round09 Ipswich 2160p FoxSports WEB DL H265 English")]
    public void ARoundWithoutARaceNumberInAMultiRaceRoundIsRejectedAsAmbiguous(string release)
    {
        var evt = Ipswich2026Race28();
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var parsed = parser.Parse(release);

        _matchingSvc.ValidateRelease(Rel(release), evt, roundRaceNumbers: Round9Races)
            .IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(release, evt, roundRaceNumbers: Round9Races)
            .Should().Be(0);
        ImportMatchingTestHarness.Service()
            .ScoreMatch(parsed.EventTitle ?? release, evt.Title, null, evt, parsed, Round9Races).Core
            .Should().BeLessOrEqualTo(0);
        LibraryImportService.CalculateMatchConfidence(
                parsed.EventTitle ?? release,
                evt.Title,
                parsed.Organization,
                evt,
                parsed.EventDate,
                parsed.EventYear,
                parsed.RoundNumber,
                parsed.SeasonYearEnd,
                parsedLocation: parsed.Location,
                parsedSport: parsed.Sport,
                roundRaceNumbers: Round9Races,
                sourceTitle: release)
            .Should().Be(0);
    }
    // Issue #368. Round 12 holds races 31 and 32. The metadata names each
    // qualifying and shootout event after its race.
    private static readonly int[] Round12Races = { 31, 32 };

    private static Event GoldCoast(string title, int race) =>
        Race(title, "12", race, 2026, 10, race == 31 ? 24 : 25);

    [Theory]
    [InlineData("Supercars 2026 Round12 Gold Coast Qualifying", "Boost Mobile Gold Coast 500 Qualifying - Race 31")]
    [InlineData("Supercars 2026 Round12 Gold Coast Top 10 Shootout", "Boost Mobile Gold Coast 500 Top 10 Shootout - Race 31")]
    [InlineData("Supercars 2026 Round12 Gold Coast Practice 1", "Boost Mobile Gold Coast 500 - Practice 1")]
    public void ARoundNamedSessionReleaseMatchesItsSessionEvent(string release, string eventTitle)
    {
        var evt = GoldCoast(eventTitle, 31);

        var result = _matchingSvc.ValidateRelease(Rel(release), evt, roundRaceNumbers: Round12Races);

        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
        _scorer.CalculateMatchScore(release, evt, roundRaceNumbers: Round12Races)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Theory]
    [InlineData("Supercars 2026 Round12 Gold Coast Qualifying Race 2", "Boost Mobile Gold Coast 500 Qualifying - Race 31")]
    [InlineData("Supercars 2026 Round12 Gold Coast Top 10 Shootout Race 2", "Boost Mobile Gold Coast 500 Top 10 Shootout - Race 31")]
    public void ARoundNamedSessionReleaseWithAnotherRaceNumberDoesNotMatch(string release, string eventTitle)
    {
        var evt = GoldCoast(eventTitle, 31);

        _matchingSvc.ValidateRelease(Rel(release), evt, roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().BeTrue("the release names race 2 of the round, which is race 32");
        _scorer.CalculateMatchScore(release, evt, roundRaceNumbers: Round12Races)
            .Should().Be(0);
    }

    [Theory]
    [InlineData("Supercars 2026 Round12 Gold Coast Qualifying Race 1", "Boost Mobile Gold Coast 500 Qualifying - Race 31")]
    [InlineData("Supercars 2026 Round12 Gold Coast Top 10 Shootout Race 2", "Boost Mobile Gold Coast 500 Top 10 Shootout - Race 32")]
    public void ARoundNamedSessionReleaseWithItsRaceNumberMatches(string release, string eventTitle)
    {
        var evt = GoldCoast(eventTitle, eventTitle.EndsWith("31") ? 31 : 32);

        _matchingSvc.ValidateRelease(Rel(release), evt, roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().BeFalse();
    }

    [Theory]
    [InlineData("Supercars 2026 Round12 Gold Coast Qualifying", "Boost Mobile Gold Coast 500 Qualifying - Race 32")]
    [InlineData("Supercars 2026 Round12 Gold Coast Top 10 Shootout", "Boost Mobile Gold Coast 500 Top 10 Shootout - Race 32")]
    public void ARoundNamedSessionReleaseDoesNotFillTheLaterRaceOfTheRound(string release, string eventTitle)
    {
        var evt = GoldCoast(eventTitle, 32);

        _matchingSvc.ValidateRelease(Rel(release), evt, roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().BeTrue("a round name alone stands for the first race, so it must not also fill race 32");
        _scorer.CalculateMatchScore(release, evt, roundRaceNumbers: Round12Races)
            .Should().Be(0);
    }

    [Fact]
    public void ARoundNamedSessionReleaseKeepsItsBonusInASingleRaceRound()
    {
        var evt = Race("Repco Bathurst 1000 Qualifying - Race 20", "10", 20, 2026, 10, 10);

        _matchingSvc.ValidateRelease(Rel("Supercars 2026 Round10 Bathurst Qualifying"), evt, roundRaceNumbers: new[] { 20 })
            .IsHardRejection.Should().BeFalse();
        Sportarr.Api.Helpers.LeagueReleaseNamePolicy.EvaluateSupercarsRoundRaceIdentity(
            "Supercars 2026 Round10 Bathurst Qualifying", evt, new[] { 20 }).Should().BeTrue();
    }

    [Fact]
    public void ARoundNamedRaceReleaseWithoutANumberStaysAmbiguousInATwoRaceRound()
    {
        var evt = GoldCoast("Boost Mobile Gold Coast 500 - Race 31", 31);

        _matchingSvc.ValidateRelease(Rel("Supercars 2026 Round12 Gold Coast Race"), evt, roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void ARoundNamedQualifyingReleaseDoesNotMatchTheRaceEvent()
    {
        var evt = GoldCoast("Boost Mobile Gold Coast 500 - Race 31", 31);

        _matchingSvc.ValidateRelease(Rel("Supercars 2026 Round12 Gold Coast Qualifying"), evt, roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().BeTrue();
    }

    [Theory]
    [InlineData("Supercars 2026 Round12 Gold Coast Qualifying 2160p FoxSports WEB DL H265 English", "Boost Mobile Gold Coast 500 Qualifying - Race 31")]
    [InlineData("Supercars 2026 Round12 Gold Coast Top 10 Shootout 2160p FoxSports WEB DL H265 English", "Boost Mobile Gold Coast 500 Top 10 Shootout - Race 31")]
    public void ARoundNamedSessionFileIsImportableToItsSessionEvent(string release, string eventTitle)
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var parsed = parser.Parse(release);
        var evt = GoldCoast(eventTitle, 31);

        ImportMatchingTestHarness.Service()
            .ScoreMatch(parsed.EventTitle ?? release, evt.Title, null, evt, parsed, Round12Races).Core
            .Should().BeGreaterThanOrEqualTo(50);
    }

    // Issue #368 follow-up. One uploader names the qualifying session after the
    // race it leads into, glued to the word ("Quali1" is the qualifying for the
    // first race of the round, "Quali2" for the second).
    private const string Quali1Release =
        "Supercars.2026.Round12.Gold.Coast.Quali1.FoxSports.720p.X264.English-s88.mp4";
    private const string Quali2Release =
        "Supercars.2026.Round12.Gold.Coast.Quali2.FoxSports.720p.X264.English-s88.mp4";

    [Theory]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Quali1.FoxSports.720p.X264.English-s88.mp4")]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Quali2.FoxSports.720p.X264.English-s88.mp4")]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Qualifying1.FoxSports.720p.X264.English-s88.mp4")]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Qualy2.FoxSports.720p.X264.English-s88.mp4")]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Quali.FoxSports.720p.X264.English-s88.mp4")]
    public void AGluedQualifyingNumberIsStillQualifying(string release)
    {
        EventPartDetector.DetectMotorsportSessionIdentity(release, "Supercars", releaseTitle: true)
            .Should().Be("Qualifying");
    }

    [Theory]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Shootout1.FoxSports.720p.X264.English-s88.mp4")]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Shootout2.FoxSports.720p.X264.English-s88.mp4")]
    public void AGluedShootoutNumberIsStillTheShootout(string release)
    {
        EventPartDetector.DetectMotorsportSessionIdentity(release, "Supercars", releaseTitle: true)
            .Should().Be("Shootout");
    }

    [Theory]
    [InlineData(Quali1Release, "Boost Mobile Gold Coast 500 Qualifying - Race 31", 31)]
    [InlineData(Quali2Release, "Boost Mobile Gold Coast 500 Qualifying - Race 32", 32)]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Qualifying1.FoxSports.720p.X264.English-s88.mp4", "Boost Mobile Gold Coast 500 Qualifying - Race 31", 31)]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Qualy2.FoxSports.720p.X264.English-s88.mp4", "Boost Mobile Gold Coast 500 Qualifying - Race 32", 32)]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Shootout1.FoxSports.720p.X264.English-s88.mp4", "Boost Mobile Gold Coast 500 Top 10 Shootout - Race 31", 31)]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Shootout2.FoxSports.720p.X264.English-s88.mp4", "Boost Mobile Gold Coast 500 Top 10 Shootout - Race 32", 32)]
    public void ANumberedQualifyingReleaseMatchesTheQualifyingOfThatRace(string release, string eventTitle, int race)
    {
        var evt = GoldCoast(eventTitle, race);

        var result = _matchingSvc.ValidateRelease(Rel(release), evt, roundRaceNumbers: Round12Races);

        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
        _scorer.CalculateMatchScore(release, evt, roundRaceNumbers: Round12Races)
            .Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Theory]
    [InlineData(Quali1Release, "Boost Mobile Gold Coast 500 Qualifying - Race 32")]
    [InlineData(Quali2Release, "Boost Mobile Gold Coast 500 Qualifying - Race 31")]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Shootout2.FoxSports.720p.X264.English-s88.mp4", "Boost Mobile Gold Coast 500 Top 10 Shootout - Race 31")]
    public void ANumberedQualifyingReleaseDoesNotMatchTheOtherRacesQualifying(string release, string eventTitle)
    {
        var evt = GoldCoast(eventTitle, eventTitle.EndsWith("31") ? 31 : 32);

        _matchingSvc.ValidateRelease(Rel(release), evt, roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().BeTrue();
        _scorer.CalculateMatchScore(release, evt, roundRaceNumbers: Round12Races)
            .Should().Be(0);
    }

    [Theory]
    [InlineData("Boost Mobile Gold Coast 500 - Race 31")]
    [InlineData("Boost Mobile Gold Coast 500 Top 10 Shootout - Race 31")]
    public void ANumberedQualifyingReleaseDoesNotMatchAnotherSession(string eventTitle)
    {
        _matchingSvc.ValidateRelease(Rel(Quali1Release), GoldCoast(eventTitle, 31), roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().BeTrue();
    }

    [Theory]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Practice1.FoxSports.720p.X264.English-s88.mp4", "Boost Mobile Gold Coast 500 - Practice 1")]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Practice2.FoxSports.720p.X264.English-s88.mp4", "Boost Mobile Gold Coast 500 - Practice 2")]
    public void TheReportersPracticeReleasesStillMatch(string release, string eventTitle)
    {
        var evt = GoldCoast(eventTitle, 31);

        var result = _matchingSvc.ValidateRelease(Rel(release), evt, roundRaceNumbers: Round12Races);

        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
        result.Confidence.Should().BeGreaterThanOrEqualTo(ReleaseMatchingService.MinimumMatchConfidence);
    }

    [Theory]
    [InlineData(Quali1Release, "Boost Mobile Gold Coast 500 Qualifying - Race 31", 31, true)]
    [InlineData(Quali2Release, "Boost Mobile Gold Coast 500 Qualifying - Race 32", 32, true)]
    [InlineData(Quali1Release, "Boost Mobile Gold Coast 500 Qualifying - Race 32", 32, false)]
    [InlineData(Quali2Release, "Boost Mobile Gold Coast 500 Qualifying - Race 31", 31, false)]
    public void ANumberedQualifyingFileIsImportableToTheQualifyingOfThatRace(
        string release, string eventTitle, int race, bool importable)
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var parsed = parser.Parse(release);
        var evt = GoldCoast(eventTitle, race);

        var score = ImportMatchingTestHarness.Service()
            .ScoreMatch(parsed.EventTitle ?? release, evt.Title, null, evt, parsed, Round12Races).Core;

        if (importable) score.Should().BeGreaterThanOrEqualTo(50);
        else score.Should().BeLessOrEqualTo(0);

        var libraryScore = LibraryImportService.CalculateMatchConfidence(
            parsed.EventTitle ?? release,
            evt.Title,
            parsed.Organization,
            evt,
            parsed.EventDate,
            parsed.EventYear,
            parsed.RoundNumber,
            parsed.SeasonYearEnd,
            parsedLocation: parsed.Location,
            parsedSport: parsed.Sport,
            roundRaceNumbers: Round12Races,
            sourceTitle: release);
        if (importable) libraryScore.Should().BeGreaterThan(0);
        else libraryScore.Should().Be(0);
    }

    [Fact]
    public void ANumberedQualifyingNameInASingleRaceRoundMapsToItsOnlyRace()
    {
        var evt = Race("Repco Bathurst 1000 Qualifying - Race 20", "10", 20, 2026, 10, 10);

        Sportarr.Api.Helpers.LeagueReleaseNamePolicy.EvaluateSupercarsRoundRaceIdentity(
            "Supercars.2026.Round10.Bathurst.Quali1.FoxSports.720p.X264.English-s88.mp4", evt, new[] { 20 })
            .Should().BeTrue();
    }

    [Fact]
    public void AGluedNumberOnAnotherSeriesSessionIsLeftAlone()
    {
        // Short forms are scoped to Supercars. Other series keep their own rules.
        EventPartDetector.DetectMotorsportSessionIdentity(
                "Formula1.2026.Round20.Mexico.City.Quali1.720p.WEB.mkv", "Formula 1", releaseTitle: true)
            .Should().NotBe("Qualifying");
    }

    [Theory]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Quali.2.FoxSports.720p.X264.English-s88.mp4", "Boost Mobile Gold Coast 500 Qualifying - Race 32", 32, true)]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Quali.2.FoxSports.720p.X264.English-s88.mp4", "Boost Mobile Gold Coast 500 Qualifying - Race 31", 31, false)]
    [InlineData("Supercars 2026 Round12 Gold Coast Qualifying 2 FoxSports 720p", "Boost Mobile Gold Coast 500 Qualifying - Race 32", 32, true)]
    [InlineData("Supercars 2026 Round12 Gold Coast Qualifying 2 FoxSports 720p", "Boost Mobile Gold Coast 500 Qualifying - Race 31", 31, false)]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Quali-1.FoxSports.720p", "Boost Mobile Gold Coast 500 Qualifying - Race 31", 31, true)]
    public void ASpacedOrDottedQualifyingNumberMapsToItsRace(string release, string eventTitle, int race, bool matches)
    {
        var evt = GoldCoast(eventTitle, race);

        _matchingSvc.ValidateRelease(Rel(release), evt, roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().Be(!matches);
        (_scorer.CalculateMatchScore(release, evt, roundRaceNumbers: Round12Races) > 0)
            .Should().Be(matches);
    }

    [Theory]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Qualifying.1080p.FoxSports.WEB-DL.English", true)]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Qualifying.720p.FoxSports.WEB-DL.English", true)]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Quali.2026.FoxSports.English", true)]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Qualifying.10.FoxSports.English", true)]
    public void ABigNumberAfterQualifyingIsNotARaceNumber(string release, bool firstRace)
    {
        // Such a name stands for the first race, as a bare "Qualifying" does.
        _matchingSvc.ValidateRelease(Rel(release), GoldCoast("Boost Mobile Gold Coast 500 Qualifying - Race 31", 31), roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().Be(!firstRace);
        _matchingSvc.ValidateRelease(Rel(release), GoldCoast("Boost Mobile Gold Coast 500 Qualifying - Race 32", 32), roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().BeTrue("the second race needs its own number");
    }

    [Theory]
    [InlineData("Supercars.2026.Gold.Coast.Quali2.FoxSports.720p.X264.English-s88.mp4")]
    [InlineData("Supercars.2026.Gold.Coast.Quali.2.FoxSports.720p.X264.English-s88.mp4")]
    [InlineData("Supercars.2026.Gold.Coast.Quali1.FoxSports.720p.X264.English-s88.mp4")]
    public void ANumberedQualifyingNameWithoutARoundIsRejectedForEitherRace(string release)
    {
        foreach (var race in new[] { 31, 32 })
        {
            var evt = GoldCoast($"Boost Mobile Gold Coast 500 Qualifying - Race {race}", race);

            _matchingSvc.ValidateRelease(Rel(release), evt, roundRaceNumbers: Round12Races)
                .IsHardRejection.Should().BeTrue($"without a round the number cannot be mapped to race {race}");
            _scorer.CalculateMatchScore(release, evt, roundRaceNumbers: Round12Races).Should().Be(0);
        }
    }

    [Theory]
    [InlineData(1, 26, true)]
    [InlineData(2, 27, true)]
    [InlineData(3, 28, true)]
    [InlineData(3, 26, false)]
    [InlineData(1, 28, false)]
    public void ANumberedQualifyingNameInAThreeRaceRoundMapsInOrder(int ordinal, int race, bool matches)
    {
        var release = $"Supercars.2026.Round09.Ipswich.Quali{ordinal}.FoxSports.720p.English";
        var evt = Race($"Century Batteries Ipswich Super 440 Qualifying - Race {race}", "9", race, 2026, 8, 21);

        _matchingSvc.ValidateRelease(Rel(release), evt, roundRaceNumbers: Round9Races)
            .IsHardRejection.Should().Be(!matches);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    public void AThirdQualifyingInATwoRaceRoundMatchesNothing(int race)
    {
        var release = "Supercars.2026.Round12.Gold.Coast.Quali3.FoxSports.720p.English";
        var evt = GoldCoast($"Boost Mobile Gold Coast 500 Qualifying - Race {race}", race);

        _matchingSvc.ValidateRelease(Rel(release), evt, roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().BeTrue("the round has no third race");
    }

    [Fact]
    public void ANumberedQualifyingNameWaitsForTheRoundScheduleWhenItIsMissing()
    {
        var evt = GoldCoast("Boost Mobile Gold Coast 500 Qualifying - Race 32", 32);

        foreach (var races in new IReadOnlyList<int>?[] { null, Array.Empty<int>() })
        {
            var result = _matchingSvc.ValidateRelease(Rel(Quali2Release), evt, roundRaceNumbers: races);

            result.IsHardRejection.Should().BeFalse();
            result.Confidence.Should().BeLessThan(ReleaseMatchingService.MinimumMatchConfidence);
            result.Rejections.Should().Contain(r => r.Contains("round schedule"));
        }
    }

    [Theory]
    [InlineData("Supercars.2025.Adelaide.500.Qualifying.1.1080p.FoxSports")]
    [InlineData("Supercars.2025.Adelaide.500.Qualifying.1080p.FoxSports")]
    public void ASeasonWithoutRaceNumbersInItsEventTitleStillMatchesAQualifyingRelease(string release)
    {
        var evt = Race("VAILO Adelaide 500 Qualifying", "12", 23, 2025, 11, 15);
        evt.EpisodeNumber = 23;

        var result = _matchingSvc.ValidateRelease(Rel(release), evt);

        result.IsHardRejection.Should().BeFalse(string.Join("; ", result.Rejections));
    }

    [Theory]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Qualifying.3.Top.10.Shootout.FoxSports.720p")]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Shootout.Quali.2.FoxSports.720p")]
    [InlineData("Supercars.2026.Round12.Gold.Coast.Shootout2.FoxSports.720p")]
    public void ATitleNamingBothSessionsIsTheShootout(string release)
    {
        EventPartDetector.DetectMotorsportSessionIdentity(release, "Supercars", releaseTitle: true)
            .Should().Be("Shootout");
    }

    [Fact]
    public void AShootoutNamedWithAQualifyingWordMatchesItsShootoutOnly()
    {
        const string release = "Supercars.2026.Round12.Gold.Coast.Shootout.Quali.2.FoxSports.720p";
        var shootout = GoldCoast("Boost Mobile Gold Coast 500 Top 10 Shootout - Race 32", 32);
        var qualifying = GoldCoast("Boost Mobile Gold Coast 500 Qualifying - Race 32", 32);

        _matchingSvc.ValidateRelease(Rel(release), shootout, roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().BeFalse();
        _matchingSvc.ValidateRelease(Rel(release), qualifying, roundRaceNumbers: Round12Races)
            .IsHardRejection.Should().BeTrue();
    }
}
