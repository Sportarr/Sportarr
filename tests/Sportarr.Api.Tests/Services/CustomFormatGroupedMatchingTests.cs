using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Coverage for issue #245: every WEB release matched every TRaSH release
/// group tier at once, English, German and French alike, inflating scores
/// past 10k. A tier format holds many non-required release group specs plus
/// two non-required source specs (WEBDL, WEBRIP). Treating all non-required
/// specs as one flat OR let the source spec alone satisfy the format, so the
/// release group list stopped meaning anything. Sonarr semantics, which the
/// TRaSH formats are written against: specs of the same type are
/// alternatives, different types are cumulative, and Required specs must
/// always match.
/// </summary>
public class CustomFormatGroupedMatchingTests
{
    private static ReleaseEvaluator Make() => new(
        Mock.Of<ILogger<ReleaseEvaluator>>(),
        new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()),
        new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>()));

    private static FormatSpecification Spec(string impl, object value, string name = "s",
        bool negate = false, bool required = false) => new()
    {
        Name = name,
        Implementation = impl,
        Negate = negate,
        Required = required,
        Fields = new Dictionary<string, object> { ["value"] = value }
    };

    /// <summary>
    /// The real TRaSH web tier shape: anchored per-group regexes plus the
    /// WEBDL (3) and WEBRIP (4) source alternatives, everything non-required.
    /// </summary>
    private static CustomFormat WebTier(int id, string name, params string[] groups)
    {
        var specs = groups
            .Select((g, i) => Spec("ReleaseGroupSpecification", $"^({g})$", $"g{i}"))
            .ToList();
        specs.Add(Spec("SourceSpecification", 3, "WEBDL"));
        specs.Add(Spec("SourceSpecification", 4, "WEBRIP"));
        return new CustomFormat { Id = id, Name = name, Specifications = specs };
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = "g",
        DownloadUrl = "http://x",
        Indexer = "t",
        Size = 4_000_000_000
    };

    private static QualityProfile Profile(params CustomFormat[] formats) => new()
    {
        Name = "p",
        FormatItems = formats.Select(f => new ProfileFormatItem { FormatId = f.Id, Score = 1700 }).ToList()
    };

    [Fact]
    public void WebReleaseFromAnUnknownGroup_MatchesNoTier()
    {
        // The reporter's case: F1TV WEB releases from groups in no tier list
        // matched WEB, German and FR tiers 01 through 03 simultaneously.
        var formats = new List<CustomFormat>
        {
            WebTier(1, "WEB Tier 01", "ABBiE", "AJP69"),
            WebTier(2, "German Web Tier 01", "CNY", "KOMET"),
            WebTier(3, "FR WEB Tier 01", "AZR", "TFA"),
        };

        var eval = Make().EvaluateRelease(
            Release("Formula1 2025 Round08 Monaco Jolyon Palmers Analysis F1TV WEB-DL 1080p h264 English-MWR"),
            Profile(formats.ToArray()), formats);

        (eval.MatchedFormats ?? new()).Should().BeEmpty(
            "a source alternative alone must not satisfy a release group tier");
    }

    [Fact]
    public void WebReleaseFromATierGroup_MatchesExactlyThatTier()
    {
        var formats = new List<CustomFormat>
        {
            WebTier(1, "WEB Tier 01", "ABBiE", "AJP69"),
            WebTier(2, "WEB Tier 02", "Kitsune", "NTb"),
        };

        var eval = Make().EvaluateRelease(
            Release("Some.Event.2025.1080p.WEB-DL.H.264-NTb"),
            Profile(formats.ToArray()), formats);

        (eval.MatchedFormats ?? new()).Select(m => m.Name)
            .Should().ContainSingle().Which.Should().Be("WEB Tier 02");
    }

    [Fact]
    public void TierGroupOnAnHdtvRelease_DoesNotMatch()
    {
        // The source group is cumulative: right release group, wrong source.
        var formats = new List<CustomFormat> { WebTier(1, "WEB Tier 01", "NTb") };

        var eval = Make().EvaluateRelease(
            Release("Some.Event.2025.1080p.HDTV.H.264-NTb"),
            Profile(formats.ToArray()), formats);

        (eval.MatchedFormats ?? new()).Should().BeEmpty();
    }

    [Fact]
    public void RequiredGateStillRejects()
    {
        var format = new CustomFormat
        {
            Id = 1,
            Name = "Gated",
            Specifications = new List<FormatSpecification>
            {
                Spec("ResolutionSpecification", 1080, "1080p", required: true),
                Spec("ReleaseTitleSpecification", "WEB", "web"),
            }
        };

        var eval = Make().EvaluateRelease(
            Release("Some.Event.2025.720p.WEB-DL.H.264-NTb"),
            Profile(format), new List<CustomFormat> { format });

        (eval.MatchedFormats ?? new()).Should().BeEmpty("the required resolution gate failed");
    }

    [Fact]
    public void GateStyleFormatWithNegatedAlternative_StillMatches()
    {
        // The SDR shape: a required gate plus same-type alternatives where one
        // is negated. Grouping must not break it.
        var format = new CustomFormat
        {
            Id = 1,
            Name = "SDR-style",
            Specifications = new List<FormatSpecification>
            {
                Spec("ReleaseTitleSpecification", @"\b2160p\b", "2160p", required: true),
                Spec("ReleaseTitleSpecification", @"\b(HDR10|HDR)\b", "HDR", negate: true),
                Spec("ReleaseTitleSpecification", @"\bSDR\b", "SDR"),
            }
        };

        var eval = Make().EvaluateRelease(
            Release("Some.Event.2025.2160p.WEB-DL.H.265-NTb"),
            Profile(format), new List<CustomFormat> { format });

        (eval.MatchedFormats ?? new()).Select(m => m.Name)
            .Should().ContainSingle().Which.Should().Be("SDR-style");
    }
}
