using FluentAssertions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A league search template keyword is compared with the release title after
/// both get the same separators.
/// </summary>
public class RssTemplateKeywordTests
{
    // The reporter's four F1 templates.
    private const string F1Templates =
        "Formula.1 {Year} 2160p SS\n" +
        "Formula.1 {Year} R{Round} {EventTitle} SS\n" +
        "F1 {Year} R{Round} {EventTitle} SS\n" +
        "Formula.1 {Year} R{Round} SS";

    [Theory]
    [InlineData("Formula.1.2026x104.Mexico.Race.MULTi.2160p.SS")]
    [InlineData("Formula.1.2026x104.Mexico.Race.F1TV.2160p.SS")]
    public void DottedTemplateKeywordMatchesTheNormalizedTitle(string releaseTitle)
    {
        var keywords = RssSyncService.ExtractRequiredKeywordsFromTemplate("Formula.1 {Year} 2160p SS");

        RssSyncService.ReleaseSatisfiesTemplateKeywords(releaseTitle, keywords).Should().BeTrue();
    }

    [Fact]
    public void MultiReleaseSatisfiesAFormulaOneTemplateAndNotOnlyTheF1Line()
    {
        const string multi = "Formula.1.2026x104.Mexico.Race.MULTi.2160p.SS";
        var keywordSets = SearchTemplateList.Parse(F1Templates)
            .Select(RssSyncService.ExtractRequiredKeywordsFromTemplate)
            .ToList();

        keywordSets.Count(set => RssSyncService.ReleaseSatisfiesTemplateKeywords(multi, set))
            .Should().BeGreaterThan(0);
        keywordSets.Where(set => set.Contains("Formula.1"))
            .Should().OnlyContain(set => RssSyncService.ReleaseSatisfiesTemplateKeywords(multi, set));
    }

    [Theory]
    [InlineData("Formula-1", "Formula_1_2026_Mexico_Race")]
    [InlineData("Formula_1", "Formula-1-2026-Mexico-Race")]
    public void AnySeparatorInTheKeywordMatchesAnySeparatorInTheTitle(string keyword, string releaseTitle)
    {
        RssSyncService.ReleaseSatisfiesTemplateKeywords(releaseTitle, new List<string> { keyword })
            .Should().BeTrue();
    }

    [Fact]
    public void DottedKeywordAlsoMatchesTheJoinedSpelling()
    {
        RssSyncService.ReleaseSatisfiesTemplateKeywords("Formula1.2026x104.Mexico.Race.MULTi.2160p.SS",
            new List<string> { "Formula.1", "SS" }).Should().BeTrue();
    }

    [Theory]
    [InlineData("F1", "Formula.1.2026.Half.1.Race.1080p")]
    [InlineData("NBA", "N.B.A.2026.Finals.Game.1.720p")]
    public void PlainKeywordDoesNotMatchAcrossSeparators(string keyword, string releaseTitle)
    {
        RssSyncService.ReleaseSatisfiesTemplateKeywords(releaseTitle, new List<string> { keyword })
            .Should().BeFalse();
    }

    [Fact]
    public void SeparatorOnlyKeywordIsIgnored()
    {
        RssSyncService.ReleaseSatisfiesTemplateKeywords("Formula.1.2026x104.Mexico.Race.1080p.WEB",
            new List<string> { "--", "Formula.1" }).Should().BeTrue();
        RssSyncService.ReleaseSatisfiesTemplateKeywords("Formula.1.2026x104.Mexico.Race.1080p.WEB",
            new List<string> { "--", "SS" }).Should().BeFalse();
    }

    [Fact]
    public void MissingKeywordStillRejectsTheRelease()
    {
        var keywords = RssSyncService.ExtractRequiredKeywordsFromTemplate("Formula.1 {Year} 2160p SS");

        RssSyncService.ReleaseSatisfiesTemplateKeywords("Formula.1.2026x104.Mexico.Race.1080p.WEB", keywords)
            .Should().BeFalse();
    }
}
