using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The release naming standard (docs/RELEASE_NAMING.md): id token extraction,
/// parser integration, authoritative matching, and renamer emission.
/// </summary>
public class SportarrIdTokenTests
{
    // ---- Extraction grammar ----

    [Theory]
    [InlineData("FIFA.World.Cup.2026-07-10.QF.Spain.vs.Belgium.1080p.WEB.h264-GROUP{sportarr-ev-2336155}")]
    [InlineData("Some Release {SPORTARR-EV-2336155} 1080p")]
    [InlineData("Some.Release.{sportarr.ev.2336155}.1080p")]
    [InlineData("Some Release {sportarr ev 2336155}")]
    [InlineData("Some.Release.{ev-2336155}.1080p")]
    [InlineData("Some.Release.sportarr-ev-2336155.1080p")]
    [InlineData("Some.Release.sportarr.ev.2336155.1080p")]
    [InlineData("Some.Release.ev-2336155.1080p")]            // bare short form (v1.1)
    [InlineData("Some.Release.ev.2336155.1080p")]
    [InlineData("Some_Release_ev_2336155_1080p")]
    public void EventToken_Variants_AllExtract(string name)
    {
        SportarrIdToken.ExtractEventId(name).Should().Be("ev-2336155");
    }

    [Fact]
    public void BareToken_RealWorldReleaseName_Extracts()
    {
        SportarrIdToken.ExtractEventId(
                "Formula.1.2026.Round.12.British.Grand.Prix.Qualifying.ev-2338110.2160p.WEB-DL.h265-GROUP")
            .Should().Be("ev-2338110");
    }

    [Theory]
    [InlineData("EPL.2026-27.Round.15.Pack.1080p-GROUP{sportarr-lg-000123}")]
    [InlineData("EPL.2026-27.Round.15.Pack.lg-000123.1080p-GROUP")]          // bare short form (v1.1)
    public void LeagueToken_Extracts_ForPacks(string name)
    {
        SportarrIdToken.ExtractLeagueId(name).Should().Be("lg-000123");
    }

    [Theory]
    [InlineData("Everton vs Manchester City 2026")]          // 'ev' inside a word
    [InlineData("EPL 2017 15 01 Everton vs Man City 720p")]  // no token at all
    [InlineData("Some.Release.ev-2026.1080p")]               // bare needs 6+ digits; years are 4
    [InlineData("Some.Release.ev2338110.1080p")]             // bare needs an explicit separator
    [InlineData("Some Release ev 2338110")]                  // space separator not allowed for bare
    [InlineData("Kiev-2026 Highlights")]                     // ev- preceded by letters
    [InlineData("Kiev-233811 Highlights")]                   // still letters before 'ev', even with 6 digits
    [InlineData("Race.REV-233811.720p")]                     // 'ev' inside a longer letter run
    public void NonTokens_DoNotExtract(string name)
    {
        SportarrIdToken.ExtractEventId(name).Should().BeNull();
    }

    // ---- Structured value normalization (mkv tag values, indexer attrs) ----

    [Theory]
    [InlineData("ev-2336155", "ev-2336155")]
    [InlineData("EV-2336155", "ev-2336155")]
    [InlineData("ev.2336155", "ev-2336155")]
    [InlineData("  ev-2336155  ", "ev-2336155")]
    [InlineData("{sportarr-ev-2336155}", "ev-2336155")]
    [InlineData("sportarr-ev-2336155", "ev-2336155")]
    [InlineData("lg-000123", "lg-000123")]
    public void Normalize_AcceptsWholeValueIds(string value, string expected)
    {
        SportarrIdToken.Normalize(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2336155")]            // no prefix
    [InlineData("tt0095253")]          // imdb id
    [InlineData("ev-2336155 extra")]   // trailing junk: reject, don't fish
    [InlineData("see ev-2336155")]     // leading junk
    [InlineData("ev-123")]             // too few digits even for lenient form
    public void Normalize_RejectsNonIdValues(string? value)
    {
        SportarrIdToken.Normalize(value).Should().BeNull();
    }

    [Fact]
    public void Strip_RemovesTokens_SoDigitsCannotConfuseDateParsing()
    {
        var stripped = SportarrIdToken.Strip("Match.2026-07-10{sportarr-ev-2336155}.mkv");
        stripped.Should().NotContain("2336155");
        stripped.Should().Contain("2026-07-10");
    }

    // ---- Embedded tag key matching (ffprobe format.tags quirks) ----

    [Theory]
    [InlineData("SPORTARR", true)]
    [InlineData("sportarr", true)]           // muxer casing drift
    [InlineData("MOVIE/SPORTARR", true)]     // TargetType string prefix flattening
    [InlineData("SPORTARR-eng", true)]       // non-default TagLanguage suffix
    [InlineData("MOVIE/SPORTARR-eng", true)]
    [InlineData("SPORTARRID", false)]
    [InlineData("XSPORTARR", false)]
    [InlineData("IMDB", false)]
    public void EmbeddedTagKey_MatchesWithFfprobeFlattening(string key, bool expected)
    {
        MediaFileInspector.IsSportarrTagKey(key).Should().Be(expected);
    }

    // ---- Parser integration ----

    [Fact]
    public void Parser_SurfacesTokenIds_AndParsesRestOfNameNormally()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var result = parser.Parse("FIFA.World.Cup.2026-07-10.Quarter.Final.Spain.vs.Belgium.1080p.WEB.h264-GROUP{sportarr-ev-2336155}");

        result.SportarrEventId.Should().Be("ev-2336155");
        result.EventDate.Should().Be(new DateTime(2026, 7, 10));
    }

    [Fact]
    public void Parser_SurfacesBareTokenId_AndStripsItBeforeDateParsing()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var result = parser.Parse("FIFA.World.Cup.2026-07-10.Quarter.Final.Spain.vs.Belgium.ev-2336155.1080p.WEB.h264-GROUP");

        result.SportarrEventId.Should().Be("ev-2336155");
        result.EventDate.Should().Be(new DateTime(2026, 7, 10));
    }

    [Fact]
    public void GenericParser_StripsTokens_FromTitleAndReleaseGroup()
    {
        var parser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>());

        var bare = parser.Parse("total.nonsense.name.here.Qualifying.ev-172897.2160p.WEB-DL.h265-GROUP.mkv");
        bare.EventTitle.Should().NotContain("172897");
        bare.SportarrEventId.Should().Be("ev-172897");

        // Trailing braced token must not break $-anchored group extraction.
        var braced = parser.Parse("Spain.vs.Belgium.2026-07-10.1080p.WEB.h264-GROUP{sportarr-ev-2336155}.mkv");
        braced.EventTitle.Should().NotContain("2336155");
        braced.ReleaseGroup.Should().Be("GROUP");
    }

    // ---- Authoritative release matching ----

    private static ReleaseMatchingService Matcher()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        return new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private static Event WcEvent(string externalId = "ev-2336155") => new()
    {
        Id = 1,
        ExternalId = externalId,
        Title = "Spain vs Belgium",
        Sport = "Soccer",
        HomeTeamName = "Spain",
        AwayTeamName = "Belgium",
        EventDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
        League = new League { Id = 1, Name = "FIFA World Cup", Sport = "Soccer" },
    };

    [Fact]
    public void TokenMatchingEventId_IsAuthoritativeMatch_EvenWithGarbageTitle()
    {
        // The title text is unparseable nonsense; the token alone carries it.
        var result = Matcher().ValidateRelease(
            Rel("total.nonsense.name.here.1080p{sportarr-ev-2336155}"), WcEvent());

        result.IsHardRejection.Should().BeFalse();
        result.IsMatch.Should().BeTrue();
        result.Confidence.Should().Be(100);
    }

    [Fact]
    public void TokenForDifferentEvent_IsHardRejected_EvenWithPerfectTitle()
    {
        // Title reads exactly like the event, but the token names another event.
        var result = Matcher().ValidateRelease(
            Rel("Spain.vs.Belgium.2026-07-10.1080p{sportarr-ev-9999999}"), WcEvent());

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("different event"));
    }

    [Fact]
    public void TokenAgainstLegacyEventWithoutCanonicalId_FallsBackToFuzzy()
    {
        // Local event predates canonical ids (numeric TSDB id) - the token
        // can't be compared, so normal fuzzy matching decides. This title
        // matches the event well, so it must not be rejected.
        var result = Matcher().ValidateRelease(
            Rel("Spain.vs.Belgium.2026-07-10.1080p.WEB{sportarr-ev-2336155}"), WcEvent(externalId: "2519345"));

        result.IsHardRejection.Should().BeFalse();
    }

    [Fact]
    public void IndexerAttribute_IsAuthoritative_WhenNameHasNoToken()
    {
        // Tracker supplied sportarrid as a torznab attr; the name itself
        // carries nothing usable.
        var rel = Rel("total.nonsense.name.1080p.WEB");
        rel.SportarrEventId = "ev-2336155";

        var result = Matcher().ValidateRelease(rel, WcEvent());

        result.IsHardRejection.Should().BeFalse();
        result.IsMatch.Should().BeTrue();
        result.Confidence.Should().Be(100);
    }

    [Fact]
    public void NameToken_WinsOver_ContradictingIndexerAttribute()
    {
        var rel = Rel("Spain.vs.Belgium.2026-07-10.1080p{sportarr-ev-2336155}");
        rel.SportarrEventId = "ev-9999999"; // bad tracker metadata

        var result = Matcher().ValidateRelease(rel, WcEvent());

        result.IsMatch.Should().BeTrue();
        result.Confidence.Should().Be(100);
    }

    [Fact]
    public void IndexerAttribute_ForDifferentEvent_IsHardRejected()
    {
        var rel = Rel("Spain.vs.Belgium.2026-07-10.1080p.WEB");
        rel.SportarrEventId = "ev-9999999";

        var result = Matcher().ValidateRelease(rel, WcEvent());

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("different event"));
    }

    [Fact]
    public void PackMemberFile_WithEventToken_ResolvesInsidePack()
    {
        // Pack import matches member files to events; a tagged member skips
        // the date/team scoring entirely.
        SportarrIdToken.ExtractEventId("EPL.R15.Match3.ev-2336155.1080p.mkv").Should().Be("ev-2336155");
    }

    [Fact]
    public void LeagueToken_ForDifferentLeague_IsHardRejected()
    {
        var evt = WcEvent();
        evt.League!.ExternalId = "lg-000456";

        var result = Matcher().ValidateRelease(
            Rel("FIFA.World.Cup.2026.Pack.1080p{sportarr-lg-000123}"), evt);

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("different league"));
    }

    [Fact]
    public void LeagueToken_ForSameLeague_RecordsReason_AndStaysFuzzy()
    {
        var evt = WcEvent();
        evt.League!.ExternalId = "lg-000123";

        var result = Matcher().ValidateRelease(
            Rel("FIFA.World.Cup.2026-07-10.Spain.vs.Belgium.1080p{sportarr-lg-000123}"), evt);

        result.IsHardRejection.Should().BeFalse();
        result.MatchReasons.Should().Contain(r => r.Contains("league id token"));
    }

    // ---- Renamer emission ----

    [Fact]
    public void RenamerToken_EmitsCanonicalForm_ForHubIds()
    {
        FileNamingService.FormatSportarrIdToken("ev-2336155").Should().Be("{sportarr-ev-2336155}");
        FileNamingService.FormatSportarrIdToken("EV-2336155").Should().Be("{sportarr-ev-2336155}");
    }

    [Fact]
    public void RenamerToken_EmitsNothing_ForLegacyOrMissingIds()
    {
        FileNamingService.FormatSportarrIdToken("2519345").Should().BeEmpty();
        FileNamingService.FormatSportarrIdToken(null).Should().BeEmpty();
        FileNamingService.FormatSportarrIdToken("lg-000123").Should().BeEmpty();
    }

    [Fact]
    public void RoundTrip_RenamedFileReExtracts()
    {
        // The loop-closure property: what the renamer stamps, the parser reads.
        var emitted = FileNamingService.FormatSportarrIdToken("ev-2336155");
        SportarrIdToken.ExtractEventId($"FIFA World Cup - S2026E98 - Spain vs Belgium - 1080p {emitted}.mkv")
            .Should().Be("ev-2336155");
    }
}
