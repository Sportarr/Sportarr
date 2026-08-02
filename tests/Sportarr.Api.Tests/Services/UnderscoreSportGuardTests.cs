using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Regression: the cross-series guard was blind to underscore-separated release titles.
///
/// Every pattern in SportIdentifiers is anchored with \b, and DetectDifferentSport matches them
/// against the RAW release title. Underscore is a word character, so \b never fires beside one:
/// `\bformula[\.\-\s]*e\b` does not match "…__Formula_E_2026_Round_15_Tokyo_Race…", and the guard
/// returns "no foreign series detected".
///
/// That is not hypothetical. Indexers that repackage NZBs emit exactly this shape, and such a
/// release reached an F1 event in production:
///
///   Formula1__NZBSPLIT__&lt;hash&gt;__NZBSPLIT__Formula_E_2026_Round_15_Tokyo_Race_(26_July_2026)_English
///
/// The league-identity gate does not catch it either — the outer NZB name literally begins with the
/// token "Formula1", which satisfies TitleNamesLeague's compact-alias check, so the release is
/// vouched for as Formula 1 while its actual content is Formula E. The sport guard is the only
/// layer that can reject it, and it was the layer that could not see the name.
/// </summary>
public class UnderscoreSportGuardTests
{
    private readonly ReleaseMatchingService _svc;

    public UnderscoreSportGuardTests()
    {
        var parser = new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>());
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _svc = new ReleaseMatchingService(Mock.Of<ILogger<ReleaseMatchingService>>(), parser, partDetector);
    }

    private static Event F1Event(string title, DateTime date) => new()
    {
        Id = 1,
        Title = title,
        Sport = "Motorsport",
        Round = "15",
        EventDate = date,
        League = new League { Id = 2, Name = "Formula 1", Sport = "Motorsport" },
    };

    private static ReleaseSearchResult Rel(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
    };

    private const string NzbSplitFormulaE =
        "Formula1__NZBSPLIT__90ce468f5436a74139a3173a168fe91e__NZBSPLIT__Formula_E_2026_Round_15_Tokyo_Race_(26_July_2026)_English_f1gp.xyz";

    [Fact]
    public void UnderscoreSeparatedFormulaE_IsRejectedForAnF1Event()
    {
        var result = _svc.ValidateRelease(
            Rel(NzbSplitFormulaE),
            F1Event("Australian Grand Prix - Race", new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc)));

        result.IsMatch.Should().BeFalse("a Formula E round is not a Formula 1 event");
    }

    [Theory]
    [InlineData("Moto_GP_2026_Round15_Japan_Race_1080p_WEB")]
    [InlineData("Formula_2_2026_Round15_Monza_Feature_Race_1080p")]
    [InlineData("Formula_E_2026_Round_15_Tokyo_Race_English")]
    [InlineData("W_Series_2026_Round_15_Race_1080p")]
    public void OtherUnderscoreSeparatedSeries_AreRejectedForAnF1Event(string title)
    {
        var result = _svc.ValidateRelease(
            Rel(title),
            F1Event("Japanese Grand Prix - Race", new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc)));

        result.IsMatch.Should().BeFalse();
    }

    [Theory]
    // Genuine F1 releases must still match — including the underscore-separated F1TV naming,
    // which is the exact shape the fix makes visible to the guard.
    [InlineData("Formula1.2026.Round15.Japan.Race.F1LIVE.F1TV.WEB-DL.1080p.h264.English-MWR")]
    [InlineData("Formula_1_2026_R15_JapanGP_Race_26_07_1080pEN50fps_F1TV")]
    [InlineData("Formula1.2026.Japanese.Grand.Prix.1080p.WEB.h264-BILLIE")]
    public void GenuineF1Releases_StillMatch(string title)
    {
        var result = _svc.ValidateRelease(
            Rel(title),
            F1Event("Japanese Grand Prix - Race", new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc)));

        result.IsHardRejection.Should().BeFalse($"'{title}' is a genuine Formula 1 release");
    }
}
