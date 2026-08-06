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

    private static CustomFormat BuildSizeFormat(long minMb, long maxMb) => new()
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
                Fields = new Dictionary<string, object> { { "min", (double)minMb }, { "max", (double)maxMb } }
            }
        }
    };

    [Fact]
    public void MatchesFormat_SizeCondition_UsesRealSizeInsteadOfAlwaysZero()
    {
        var format = BuildSizeFormat(minMb: 1000, maxMb: 5000);
        var sizeInBytes = 2000L * 1024 * 1024; // 2000 MB - inside the min/max range

        // Without a real size threaded through, this always evaluated against 0
        // bytes, so a min=1000 condition could never match.
        _service.MatchesFormat("Release.Title.1080p", format, sizeInBytes).Should().BeTrue();
        _service.MatchesFormat("Release.Title.1080p", format, sizeInBytes: 0).Should().BeFalse();
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
