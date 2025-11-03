using Fightarr.Api.Services;
using Fightarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Fightarr.Api.Tests.Services;

public class ReleaseEvaluatorTests
{
    private readonly ReleaseEvaluator _evaluator;
    private readonly Mock<ILogger<ReleaseEvaluator>> _mockLogger;

    public ReleaseEvaluatorTests()
    {
        _mockLogger = new Mock<ILogger<ReleaseEvaluator>>();
        _evaluator = new ReleaseEvaluator(_mockLogger.Object);
    }

    [Theory]
    [InlineData("UFC.300.2024.2160p.WEB-DL.x265", "2160p", 1000)]
    [InlineData("Fight.Night.1080p.BluRay.x264", "1080p", 800)]
    [InlineData("Event.720p.HDTV.x264", "720p", 600)]
    [InlineData("Card.480p.WEBRip.x265", "480p", 400)]
    [InlineData("Fight.360p.HDTV", "360p", 200)]
    public void EvaluateRelease_ShouldDetectQualityAndScore(string title, string expectedQuality, int expectedScore)
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = title,
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 1024 // 1GB
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, null);

        // Assert
        evaluation.Quality.Should().Be(expectedQuality);
        evaluation.QualityScore.Should().Be(expectedScore);
        evaluation.Approved.Should().BeTrue(); // Manual search always approves
    }

    [Theory]
    [InlineData("UFC.300.BluRay.x264", "BluRay", 100)]
    [InlineData("Fight.WEB-DL.H264", "WEB-DL", 90)]
    [InlineData("Event.WEBRip.x265", "WEBRip", 85)]
    [InlineData("Card.HDTV.x264", "HDTV", 70)]
    [InlineData("Fight.DVDRip.XviD", "DVDRip", 60)]
    public void EvaluateRelease_ShouldDetectSourceQuality(string title, string expectedQuality, int expectedScore)
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = title,
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 500
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, null);

        // Assert
        evaluation.Quality.Should().Be(expectedQuality);
        evaluation.QualityScore.Should().Be(expectedScore);
    }

    [Fact]
    public void EvaluateRelease_ShouldRejectWhenQualityNotInProfile()
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = "UFC.300.1080p.WEB-DL.x264",
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 1024
        };

        var profile = new QualityProfile
        {
            Name = "HD Only",
            Items = new List<QualityItem>
            {
                new QualityItem { Name = "720p", Quality = 1, Allowed = true },
                new QualityItem { Name = "1080p", Quality = 2, Allowed = false }
            }
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, profile);

        // Assert
        evaluation.Rejections.Should().Contain(r => r.Contains("not wanted in quality profile"));
        evaluation.Approved.Should().BeTrue(); // Still approved for manual search
    }

    [Fact]
    public void EvaluateRelease_ShouldWarnWhenSizeExceedsMaximum()
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = "UFC.300.1080p.WEB-DL.x264",
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = (long)(5.5 * 1024 * 1024 * 1024) // 5.5GB
        };

        var profile = new QualityProfile
        {
            Name = "Normal",
            MaxSize = 5000 // 5000 MB = 5GB
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, profile);

        // Assert
        evaluation.Rejections.Should().Contain(r => r.Contains("exceeds maximum"));
    }

    [Fact]
    public void EvaluateRelease_ShouldWarnWhenSizeBelowMinimum()
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = "UFC.300.1080p.WEB-DL.x264",
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 500 * 1024 * 1024 // 500MB
        };

        var profile = new QualityProfile
        {
            Name = "High Quality Only",
            MinSize = 1000 // 1000 MB = 1GB
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, profile);

        // Assert
        evaluation.Rejections.Should().Contain(r => r.Contains("below minimum"));
    }

    [Fact]
    public void EvaluateRelease_ShouldWarnWhenNoSeeders()
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = "UFC.300.1080p.WEB-DL.x264",
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 1024,
            Seeders = 0
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, null);

        // Assert
        evaluation.Rejections.Should().Contain(r => r.Contains("No seeders"));
    }

    [Fact]
    public void EvaluateRelease_ShouldMatchCustomFormats()
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = "UFC.300.1080p.WEB-DL.x265.HDR",
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 1024
        };

        var customFormats = new List<CustomFormat>
        {
            new CustomFormat
            {
                Id = 1,
                Name = "x265",
                Specifications = new List<FormatSpecification>
                {
                    new FormatSpecification
                    {
                        Name = "x265 codec",
                        Implementation = "ReleaseTitle",
                        Required = false,
                        Negate = false,
                        Fields = new Dictionary<string, object> { { "value", "x265|HEVC" } }
                    }
                }
            },
            new CustomFormat
            {
                Id = 2,
                Name = "HDR",
                Specifications = new List<FormatSpecification>
                {
                    new FormatSpecification
                    {
                        Name = "HDR",
                        Implementation = "ReleaseTitle",
                        Required = false,
                        Negate = false,
                        Fields = new Dictionary<string, object> { { "value", "HDR|HDR10" } }
                    }
                }
            }
        };

        var profile = new QualityProfile
        {
            Name = "Test Profile",
            FormatItems = new List<ProfileFormatItem>
            {
                new ProfileFormatItem { FormatId = 1, Score = 50 },
                new ProfileFormatItem { FormatId = 2, Score = 30 }
            }
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, profile, customFormats);

        // Assert
        evaluation.MatchedFormats.Should().HaveCount(2);
        evaluation.MatchedFormats.Should().Contain(f => f.Name == "x265" && f.Score == 50);
        evaluation.MatchedFormats.Should().Contain(f => f.Name == "HDR" && f.Score == 30);
        evaluation.CustomFormatScore.Should().Be(80);
        evaluation.TotalScore.Should().Be(evaluation.QualityScore + 80);
    }

    [Fact]
    public void EvaluateRelease_ShouldNotMatchNegatedSpecification()
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = "UFC.300.1080p.WEB-DL.x264.CAM",
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 1024
        };

        var customFormats = new List<CustomFormat>
        {
            new CustomFormat
            {
                Id = 1,
                Name = "No CAM",
                Specifications = new List<FormatSpecification>
                {
                    new FormatSpecification
                    {
                        Name = "Not CAM",
                        Implementation = "ReleaseTitle",
                        Required = false,
                        Negate = true, // Should NOT match CAM
                        Fields = new Dictionary<string, object> { { "value", "CAM|TS|TELESYNC" } }
                    }
                }
            }
        };

        var profile = new QualityProfile
        {
            Name = "Test Profile",
            FormatItems = new List<ProfileFormatItem>
            {
                new ProfileFormatItem { FormatId = 1, Score = 100 }
            }
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, profile, customFormats);

        // Assert
        // Should NOT match because title contains CAM and negate=true
        evaluation.MatchedFormats.Should().BeEmpty();
        evaluation.CustomFormatScore.Should().Be(0);
    }

    [Fact]
    public void EvaluateRelease_ShouldRequireAllRequiredSpecifications()
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = "UFC.300.1080p.WEB-DL.x264", // Missing HDR
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 1024
        };

        var customFormats = new List<CustomFormat>
        {
            new CustomFormat
            {
                Id = 1,
                Name = "1080p HDR Required",
                Specifications = new List<FormatSpecification>
                {
                    new FormatSpecification
                    {
                        Name = "1080p",
                        Implementation = "ReleaseTitle",
                        Required = true, // MUST match
                        Negate = false,
                        Fields = new Dictionary<string, object> { { "value", "1080p" } }
                    },
                    new FormatSpecification
                    {
                        Name = "HDR",
                        Implementation = "ReleaseTitle",
                        Required = true, // MUST match
                        Negate = false,
                        Fields = new Dictionary<string, object> { { "value", "HDR" } }
                    }
                }
            }
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, null, customFormats);

        // Assert
        // Should NOT match because HDR is required but missing
        evaluation.MatchedFormats.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateRelease_ShouldWarnWhenCustomFormatScoreBelowMinimum()
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = "UFC.300.1080p.WEB-DL.x264",
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 1024
        };

        var profile = new QualityProfile
        {
            Name = "High Standards",
            MinFormatScore = 100 // Requires at least 100 custom format score
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, profile);

        // Assert
        evaluation.CustomFormatScore.Should().Be(0);
        evaluation.Rejections.Should().Contain(r => r.Contains("Custom format score") && r.Contains("below minimum"));
    }

    [Fact]
    public void EvaluateRelease_ShouldHandleEmptyProfile()
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = "UFC.300.1080p.WEB-DL.x264",
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 1024
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, null);

        // Assert
        evaluation.Should().NotBeNull();
        evaluation.Approved.Should().BeTrue();
        evaluation.QualityScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EvaluateRelease_ShouldHandleUnknownQuality()
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = "UFC.300.Some.Random.Release",
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 1024
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, null);

        // Assert
        evaluation.Quality.Should().Be("Unknown");
        evaluation.QualityScore.Should().Be(0);
        evaluation.Approved.Should().BeTrue();
    }

    [Fact]
    public void EvaluateRelease_ShouldCalculateTotalScore()
    {
        // Arrange
        var release = new ReleaseSearchResult
        {
            Title = "UFC.300.1080p.BluRay.x265",
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 1024
        };

        var customFormats = new List<CustomFormat>
        {
            new CustomFormat
            {
                Id = 1,
                Name = "x265",
                Specifications = new List<FormatSpecification>
                {
                    new FormatSpecification
                    {
                        Name = "x265",
                        Implementation = "ReleaseTitle",
                        Required = false,
                        Negate = false,
                        Fields = new Dictionary<string, object> { { "value", "x265" } }
                    }
                }
            }
        };

        var profile = new QualityProfile
        {
            Name = "Test",
            FormatItems = new List<ProfileFormatItem>
            {
                new ProfileFormatItem { FormatId = 1, Score = 50 }
            }
        };

        // Act
        var evaluation = _evaluator.EvaluateRelease(release, profile, customFormats);

        // Assert
        evaluation.QualityScore.Should().Be(800); // 1080p
        evaluation.CustomFormatScore.Should().Be(50);
        evaluation.TotalScore.Should().Be(850);
    }
}
