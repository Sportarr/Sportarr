using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #441. One release family writes a season-wide session index after
/// the year ("Formula.1.2026x109.Brazil.Race"). The year reader accepted one
/// or two digits only, so a three-digit index left the name with no year and
/// RSS sync dropped it. The index is not a championship round. The location
/// and the session come from the words after it.
/// </summary>
public class FormulaOneSessionIndexTests
{
    private delegate Event? FindMatch(ReleaseSearchResult release, List<Event> events,
        ReleaseMatchingService matcher, ReleaseMatchScorer scorer, EventPartDetector partDetector, bool multiPart,
        IReadOnlyDictionary<int, int?> earlyLimits,
        IReadOnlyCollection<League> knownLeagues, IReadOnlyCollection<Event> datePeers,
        IReadOnlyDictionary<(int? LeagueId, string? Season, string? Round), List<int>> roundRaceNumbersByRound,
        IReadOnlyDictionary<(int? LeagueId, string? Season), NascarVenueMatchContext> nascarVenues);

    private static readonly League FormulaOne = new() { Id = 1, Name = "Formula 1", Sport = "Motorsport" };

    private readonly SportsFileNameParser _parser = new(NullLogger<SportsFileNameParser>.Instance);
    private readonly ReleaseMatchingService _matching = new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    // Every name from the issue and its follow-up comment.
    [Theory]
    [InlineData("Formula.1.2026x109.Brazil.Race.MULTi.2160p.SS", "Brazil", "Race")]
    [InlineData("Formula.1.2026x109.Brazil.Race.F1TV.2160p.SS", "Brazil", "Race")]
    [InlineData("Formula.1.2026x109.Brazil.Race.MULTi.720p.SS", "Brazil", "Race")]
    [InlineData("Formula.1.2026x109.Brazil.Race.MULTi.1080p.SS", "Brazil", "Race")]
    [InlineData("Formula.1.2026x109.Brazil.Race.F1TV.720p.SS", "Brazil", "Race")]
    [InlineData("Formula.1.2026x109.Brazil.Race.F1TV.1080p.SS", "Brazil", "Race")]
    [InlineData("Formula.1.2026x94.Singapore.Race.MULTi.2160p.SS", "Singapore", "Race")]
    [InlineData("Formula.1.2026x98.United.States.Qualifying.MULTi.2160p.SS", "United States", "Qualifying")]
    [InlineData("Formula.1.2026x99.United.States.Race.MULTi.2160p.SS", "United States", "Race")]
    [InlineData("Formula.1.2026x103.Mexico.Qualifying.MULTi.2160p.SS", "Mexico", "Qualifying")]
    [InlineData("Formula.1.2026x104.Mexico.Race.F1TV.2160p.SS", "Mexico", "Race")]
    [InlineData("Formula.1.2026x108.Brazil.Qualifying.F1TV.1080p.SS", "Brazil", "Qualifying")]
    [InlineData("Formula.1.2026x79.R14.SpanishGP.Race.MULTi.4K.HLG.2160p.SS", "Spanish", "Race")]
    [InlineData("Formula 1 2026x20 Brazil Race SkyF1HD 1080p", "Brazil", "Race")]
    [InlineData("Formula.1.2026x18.USA.Race.SkyF1HD.SD", "USA", "Race")]
    [InlineData("Formula_1_2026x109_Brazil_Race_MULTi_2160p_SS", "Brazil", "Race")]
    [InlineData("Formula.1.2026x02.China.Sprint.Qualifying.SkyF1HD.1080p", "China", "Sprint Qualifying")]
    [InlineData("Formula.One.2026x109.Brazil.Race.MULTi.2160p.SS", "Brazil", "Race")]
    public void SessionIndexName_YieldsYearLocationAndSession(string name, string location, string session)
    {
        var result = _parser.Parse(name);

        result.EventYear.Should().Be(2026);
        result.Organization.Should().Be("Formula 1");
        result.Location.Should().Be(location);
        result.Session.Should().Be(session);
        // The index is not a round, and the parser does not turn it into one.
        result.RoundNumber.Should().BeNull();
    }

    // A codec tag ends the place, so it does not join the location.
    [Theory]
    [InlineData("Formula.1.2026x109.Brazil.HEVC.x265-GROUP")]
    [InlineData("Formula.1.2026x109.Brazil.h.264.AAC-GROUP")]
    [InlineData("Formula.1.2026x109.Brazil.English.1080p")]
    public void CodecOrLanguageTag_EndsThePlace(string name)
    {
        _parser.Parse(name).Location.Should().Be("Brazil");
    }

    // A "Preview Show" is not a race show, and the historic Pre-Qualifying
    // session keeps its qualifying reading.
    [Theory]
    [InlineData("Formula 1 1989 Brazil Pre-Qualifying 1080p WEB", "Qualifying")]
    [InlineData("Brazil Preview Show Race", "Race")]
    public void ShowCheck_LeavesOtherNamesAlone(string name, string session)
    {
        SportsFileNameParser.DetectMotorsportSession(name).Should().Be(session);
    }

    // A four-digit number after the x is a resolution, not an index.
    [Theory]
    [InlineData("Formula.1.2026x1080p.China.Race-GROUP")]
    [InlineData("Formula.1.2026x1080.China.Race-GROUP")]
    public void FourDigitsAfterTheX_IsNotAnIndex(string name)
    {
        _parser.Parse(name).EventYear.Should().BeNull();
    }

    // The issue log shows the location "Brazil Post Race Show" and the
    // session "Race". That line comes from a different release. The Sky race
    // name holds no "Post Race Show". The show is not the race, so its
    // session is not Race.
    [Fact]
    public void SkyRaceName_DoesNotReadAsThePostRaceShow()
    {
        var result = _parser.Parse("Formula 1 2026x20 Brazil Race SkyF1HD 1080p");

        result.Location.Should().Be("Brazil");
        result.Session.Should().Be("Race");
    }

    [Theory]
    [InlineData("Formula 1 2026 Brazil Post Race Show 1080p WEB")]
    [InlineData("Formula.1.2026x20.Brazil.Post.Race.Show.SkyF1HD.1080p")]
    [InlineData("Formula 1 2026 Brazil Pre Race Show 1080p WEB")]
    public void RaceShow_HasNoRaceSession(string name)
    {
        _parser.Parse(name).Session.Should().BeNull();
    }

    [Theory]
    [InlineData("Formula 1 2026 Brazil Post Race Show 1080p WEB")]
    [InlineData("Formula.1.2026x20.Brazil.Post.Race.Show.SkyF1HD.1080p")]
    public void RaceShow_DoesNotMatchTheRace(string name)
    {
        var result = _matching.ValidateRelease(Release(name), SaoPaulo(11, "Race"));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Theory]
    [InlineData("Formula.1.2026x109.Brazil.Race.MULTi.2160p.SS")]
    [InlineData("Formula.1.2026x20.Brazil.Race.SkyF1HD.1080p")]
    public void BrazilRace_MatchesTheSaoPauloRaceWithoutARoundClaim(string name)
    {
        var result = _matching.ValidateRelease(Release(name), SaoPaulo(11, "Race"));

        result.IsMatch.Should().BeTrue(string.Join("; ", result.Rejections));
        result.IsHardRejection.Should().BeFalse();
        result.MatchReasons.Should().Contain("Year matches (2026)");
        result.Rejections.Should().NotContain(reason => reason.Contains("Round mismatch"));
        result.MatchReasons.Should().NotContain(reason => reason.StartsWith("Round number matches"));
    }

    [Theory]
    [InlineData("Qualifying")]
    [InlineData("Sprint")]
    [InlineData("Sprint Qualifying")]
    public void BrazilRace_DoesNotMatchAnotherSession(string session)
    {
        var result = _matching.ValidateRelease(
            Release("Formula.1.2026x109.Brazil.Race.MULTi.2160p.SS"), SaoPaulo(12, session));

        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void BrazilRace_DoesNotMatchTheMexicoRace()
    {
        var result = _matching.ValidateRelease(
            Release("Formula.1.2026x109.Brazil.Race.MULTi.2160p.SS"), MexicoRace());

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    // The explicit R14 token still names the round, beside the index.
    [Fact]
    public void ExplicitRoundBesideTheIndex_StillDecidesTheRound()
    {
        var right = _matching.ValidateRelease(
            Release("Formula.1.2026x79.R14.SpanishGP.Race.MULTi.4K.HLG.2160p.SS"), Spanish("14"));
        var wrong = _matching.ValidateRelease(
            Release("Formula.1.2026x79.R14.SpanishGP.Race.MULTi.4K.HLG.2160p.SS"), Spanish("15"));

        right.IsMatch.Should().BeTrue(string.Join("; ", right.Rejections));
        right.MatchReasons.Should().Contain("Round number matches: Round 14");
        wrong.IsMatch.Should().BeFalse();
        wrong.Rejections.Should().Contain(reason => reason.Contains("Round mismatch"));
    }

    // Outside Formula 1 a short number after the x is a round. No season
    // has 100 rounds, so a three-digit number is never read as one.
    [Fact]
    public void ThreeDigitIndex_IsNeverARoundInAnotherSeries()
    {
        var evt = new Event
        {
            Id = 30,
            Title = "London E-Prix - Race",
            Sport = "Motorsport",
            EventDate = new DateTime(2026, 7, 26, 14, 0, 0, DateTimeKind.Utc),
            Round = "16",
            League = new League { Id = 3, Name = "Formula E", Sport = "Motorsport" }
        };

        var result = _matching.ValidateRelease(Release("Formula.E.2026x109.London.Race.1080p.WEB"), evt);

        result.Rejections.Should().NotContain(reason => reason.Contains("Round mismatch"));
        result.MatchReasons.Should().NotContain(reason => reason.StartsWith("Round number matches"));
    }

    [Theory]
    [InlineData("Formula.1.2026x109.Brazil.Race.MULTi.2160p.SS", 11)]
    [InlineData("Formula.1.2026x109.Brazil.Race.F1TV.1080p.SS", 11)]
    [InlineData("Formula.1.2026x108.Brazil.Qualifying.F1TV.1080p.SS", 12)]
    [InlineData("Formula 1 2026x20 Brazil Race SkyF1HD 1080p", 11)]
    [InlineData("Formula.1.2026x104.Mexico.Race.F1TV.2160p.SS", 20)]
    public void RssSync_PicksTheNamedSession(string name, int expectedId)
    {
        var events = new List<Event>
        {
            SaoPaulo(11, "Race"),
            SaoPaulo(12, "Qualifying"),
            SaoPaulo(13, "Sprint"),
            SaoPaulo(14, "Sprint Qualifying"),
            MexicoRace(),
        };
        using var services = new ServiceCollection().BuildServiceProvider();
        using var rss = new RssSyncService(services, NullLogger<RssSyncService>.Instance);
        var findMatch = typeof(RssSyncService).GetMethod(
            "FindMatchingEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<FindMatch>(rss);

        var result = findMatch(
            Release(name),
            events,
            _matching,
            new ReleaseMatchScorer(),
            new EventPartDetector(NullLogger<EventPartDetector>.Instance),
            true,
            new Dictionary<int, int?>(),
            new[] { FormulaOne },
            events,
            new Dictionary<(int? LeagueId, string? Season, string? Round), List<int>>(),
            new Dictionary<(int? LeagueId, string? Season), NascarVenueMatchContext>());

        result.Should().NotBeNull();
        result!.Id.Should().Be(expectedId);
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "https://example.invalid/" + title,
        Indexer = "Fixture"
    };

    private static Event SaoPaulo(int id, string session) => new()
    {
        Id = id,
        Title = $"São Paulo Grand Prix - {session}",
        Sport = "Motorsport",
        LeagueId = FormulaOne.Id,
        League = FormulaOne,
        Season = "2026",
        Round = "20",
        EventDate = session switch
        {
            "Race" => new DateTime(2026, 11, 8, 17, 0, 0, DateTimeKind.Utc),
            "Qualifying" => new DateTime(2026, 11, 7, 18, 0, 0, DateTimeKind.Utc),
            "Sprint" => new DateTime(2026, 11, 7, 14, 0, 0, DateTimeKind.Utc),
            _ => new DateTime(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc),
        },
        Monitored = true
    };

    private static Event MexicoRace() => new()
    {
        Id = 20,
        Title = "Mexico City Grand Prix - Race",
        Sport = "Motorsport",
        LeagueId = FormulaOne.Id,
        League = FormulaOne,
        Season = "2026",
        Round = "19",
        EventDate = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc),
        Monitored = true
    };

    private static Event Spanish(string round) => new()
    {
        Id = 40,
        Title = "Spanish Grand Prix - Race",
        Sport = "Motorsport",
        LeagueId = FormulaOne.Id,
        League = FormulaOne,
        Season = "2026",
        Round = round,
        EventDate = new DateTime(2026, 9, 13, 13, 0, 0, DateTimeKind.Utc),
        Monitored = true
    };
}
