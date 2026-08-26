using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

public class CustomFormatServiceTests
{
    private readonly CustomFormatService _service;

    public CustomFormatServiceTests()
    {
        var parser = new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>());
        _service = new CustomFormatService(parser);
    }

    private static CustomFormat BuildReleaseTypeFormat(string expectedType) => new()
    {
        Id = 1,
        Name = "ReleaseType-" + expectedType,
        Specifications = new List<FormatSpecification>
        {
            new FormatSpecification
            {
                Name = "Release Type",
                Implementation = "ReleaseType",
                Required = true,
                Negate = false,
                Fields = new Dictionary<string, object> { { "value", expectedType } }
            }
        }
    };

    [Theory]
    [InlineData("NFL.2024.Week.10.Chiefs.vs.Bills.1080p.WEB.H264", "SingleEvent", true)]
    [InlineData("NFL.2024.Week.10.PACK.1080p.WEB.H264", "SingleEvent", false)]
    [InlineData("NFL.2024.Week.10.PACK.1080p.WEB.H264", "Pack", true)]
    [InlineData("NFL.2024.Week.10.Chiefs.vs.Bills.1080p.WEB.H264", "Pack", false)]
    public void MatchesFormat_ReleaseTypeCondition_MatchesRenameTimeSameAsGrabTime(string title, string expectedType, bool shouldMatch)
    {
        var format = BuildReleaseTypeFormat(expectedType);

        _service.MatchesFormat(title, format).Should().Be(shouldMatch);
    }

    private static CustomFormat BuildSizeFormat(double minGb, double maxGb) => new()
    {
        Id = 2,
        Name = "Size",
        Specifications = new List<FormatSpecification>
        {
            new FormatSpecification
            {
                Name = "Size",
                Implementation = "Size",
                Required = true,
                Negate = false,
                Fields = new Dictionary<string, object> { { "min", minGb }, { "max", maxGb } }
            }
        }
    };

    [Fact]
    public void MatchesFormat_SizeCondition_UsesRealSizeInsteadOfAlwaysZero()
    {
        var format = BuildSizeFormat(minGb: 1, maxGb: 5);
        var sizeInBytes = 2L * 1024 * 1024 * 1024; // 2 GB, inside the range

        // Without a real size threaded through, this always evaluated against 0
        // bytes, so a min condition could never match.
        _service.MatchesFormat("Release.Title.1080p", format, sizeInBytes).Should().BeTrue();
        _service.MatchesFormat("Release.Title.1080p", format, sizeInBytes: 0).Should().BeFalse();
    }

    [Fact]
    public void MatchesFormat_SizeCondition_ReadsBoundsAsGigabytesLikeGrabTimeDoes()
    {
        // The grab-time evaluator has always read these bounds as gigabytes.
        // Reading them as megabytes here made the same format score one way at
        // grab time and another at rename time, off by a factor of a thousand:
        // a 2 GB release passed a "between 1 and 5" rule at grab time and
        // failed it at rename time.
        var format = BuildSizeFormat(minGb: 1, maxGb: 5);

        var twoGb = 2L * 1024 * 1024 * 1024;
        var twoHundredMb = 200L * 1024 * 1024;

        _service.MatchesFormat("Release.Title.1080p", format, twoGb).Should().BeTrue();
        _service.MatchesFormat("Release.Title.1080p", format, twoHundredMb).Should().BeFalse();
    }

    private static CustomFormat BuildIndexerFlagFormat(string flagValue) => new()
    {
        Id = 3,
        Name = "IndexerFlag",
        Specifications = new List<FormatSpecification>
        {
            new FormatSpecification
            {
                Name = "Freeleech",
                Implementation = "IndexerFlag",
                Required = true,
                Negate = false,
                Fields = new Dictionary<string, object> { { "value", flagValue } }
            }
        }
    };

    [Fact]
    public void MatchesFormat_IndexerFlagCondition_MatchesWhenFlagPresent()
    {
        var format = BuildIndexerFlagFormat("freeleech");

        _service.MatchesFormat("Release.Title.1080p", format, indexerFlags: "freeleech,internal").Should().BeTrue();
        _service.MatchesFormat("Release.Title.1080p", format, indexerFlags: "internal").Should().BeFalse();
        _service.MatchesFormat("Release.Title.1080p", format, indexerFlags: null).Should().BeFalse();
    }

    [Fact]
    public void MatchesFormat_IndexerFlagCondition_NumericIdMirrorsReleaseEvaluatorMapping()
    {
        var format = BuildIndexerFlagFormat("1"); // 1 = freeleech, mirrors ReleaseEvaluator.EvaluateIndexerFlagSpec

        _service.MatchesFormat("Release.Title.1080p", format, indexerFlags: "freeleech").Should().BeTrue();
        _service.MatchesFormat("Release.Title.1080p", format, indexerFlags: "scene").Should().BeFalse();
    }
}
