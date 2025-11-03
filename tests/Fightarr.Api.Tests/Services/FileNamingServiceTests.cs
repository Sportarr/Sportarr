using Fightarr.Api.Services;
using Fightarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Fightarr.Api.Tests.Services;

public class FileNamingServiceTests
{
    private readonly FileNamingService _service;
    private readonly Mock<ILogger<FileNamingService>> _mockLogger;

    public FileNamingServiceTests()
    {
        _mockLogger = new Mock<ILogger<FileNamingService>>();
        _service = new FileNamingService(_mockLogger.Object);
    }

    [Fact]
    public void BuildFileName_ShouldReplaceBasicTokens()
    {
        // Arrange
        var format = "{Event Title} - {Quality}";
        var tokens = new FileNamingTokens
        {
            EventTitle = "UFC 300",
            Quality = "1080p"
        };

        // Act
        var result = _service.BuildFileName(format, tokens, ".mkv");

        // Assert
        result.Should().Be("UFC 300 - 1080p.mkv");
    }

    [Fact]
    public void BuildFileName_ShouldHandleAirDateTokens()
    {
        // Arrange
        var format = "{Event Title} {Air Date Year}-{Air Date Month}-{Air Date Day}";
        var tokens = new FileNamingTokens
        {
            EventTitle = "UFC 300",
            AirDate = new DateTime(2024, 4, 13)
        };

        // Act
        var result = _service.BuildFileName(format, tokens, ".mkv");

        // Assert
        result.Should().Be("UFC 300 2024-04-13.mkv");
    }

    [Fact]
    public void BuildFileName_ShouldHandleQualityFullToken()
    {
        // Arrange
        var format = "{Event Title} {Quality Full}";
        var tokens = new FileNamingTokens
        {
            EventTitle = "UFC 300",
            QualityFull = "1080p BluRay x265"
        };

        // Act
        var result = _service.BuildFileName(format, tokens, ".mkv");

        // Assert
        result.Should().Be("UFC 300 1080p BluRay x265.mkv");
    }

    [Fact]
    public void BuildFileName_ShouldHandleReleaseGroup()
    {
        // Arrange
        var format = "{Event Title} [{Release Group}]";
        var tokens = new FileNamingTokens
        {
            EventTitle = "UFC 300",
            ReleaseGroup = "FIGHTARR"
        };

        // Act
        var result = _service.BuildFileName(format, tokens, ".mkv");

        // Assert
        result.Should().Be("UFC 300 [FIGHTARR].mkv");
    }

    [Fact]
    public void BuildFileName_ShouldRemoveUnreplacedTokens()
    {
        // Arrange
        var format = "{Event Title} - {Missing Token} - {Quality}";
        var tokens = new FileNamingTokens
        {
            EventTitle = "UFC 300",
            Quality = "1080p"
        };

        // Act
        var result = _service.BuildFileName(format, tokens, ".mkv");

        // Assert
        result.Should().Be("UFC 300 - 1080p.mkv"); // Missing token removed, extra spaces cleaned
    }

    [Fact]
    public void BuildFileName_ShouldAddDotToExtension()
    {
        // Arrange
        var tokens = new FileNamingTokens { EventTitle = "UFC 300" };

        // Act
        var result = _service.BuildFileName("{Event Title}", tokens, "mkv");

        // Assert
        result.Should().Be("UFC 300.mkv");
    }

    [Fact]
    public void BuildFileName_ShouldNotAddDoubleDotToExtension()
    {
        // Arrange
        var tokens = new FileNamingTokens { EventTitle = "UFC 300" };

        // Act
        var result = _service.BuildFileName("{Event Title}", tokens, ".mkv");

        // Assert
        result.Should().Be("UFC 300.mkv");
    }

    [Fact]
    public void BuildFolderName_ShouldReplaceEventTokens()
    {
        // Arrange
        var format = "{Event Title} ({Year})";
        var eventInfo = new Event
        {
            Title = "UFC 300",
            Organization = "UFC",
            EventDate = new DateTime(2024, 4, 13)
        };

        // Act
        var result = _service.BuildFolderName(format, eventInfo);

        // Assert
        result.Should().Be("UFC 300 (2024)");
    }

    [Fact]
    public void BuildFolderName_ShouldHandleEventCleanTitle()
    {
        // Arrange
        var format = "{Event CleanTitle}";
        var eventInfo = new Event
        {
            Title = "UFC 300: Main Event!",
            Organization = "UFC",
            EventDate = new DateTime(2024, 4, 13)
        };

        // Act
        var result = _service.BuildFolderName(format, eventInfo);

        // Assert
        result.Should().Be("ufc300mainevent");
    }

    [Fact]
    public void BuildFolderName_ShouldMoveArticleToEnd()
    {
        // Arrange
        var format = "{Event Title The}";
        var eventInfo = new Event
        {
            Title = "The Ultimate Fighter",
            Organization = "UFC",
            EventDate = new DateTime(2024, 1, 1)
        };

        // Act
        var result = _service.BuildFolderName(format, eventInfo);

        // Assert
        result.Should().Be("Ultimate Fighter, The");
    }

    [Theory]
    [InlineData(":", " ")]
    [InlineData("*", " ")]
    [InlineData("?", " ")]
    [InlineData("\"", " ")]
    [InlineData("<", " ")]
    [InlineData(">", " ")]
    [InlineData("|", " ")]
    public void CleanFileName_ShouldReplaceInvalidCharacters(string invalidChar, string replacement)
    {
        // Arrange
        var filename = $"UFC{invalidChar}300";

        // Act
        var result = _service.CleanFileName(filename);

        // Assert
        result.Should().Be($"UFC{replacement}300");
    }

    [Fact]
    public void CleanFileName_ShouldCleanMultipleSpaces()
    {
        // Arrange
        var filename = "UFC    300     Main    Card";

        // Act
        var result = _service.CleanFileName(filename);

        // Assert
        result.Should().Be("UFC 300 Main Card");
    }

    [Fact]
    public void CleanFileName_ShouldTrimSpacesAndDots()
    {
        // Arrange
        var filename = "  UFC 300  ...  ";

        // Act
        var result = _service.CleanFileName(filename);

        // Assert
        result.Should().Be("UFC 300");
    }

    [Fact]
    public void CleanFileName_ShouldHandleEmptyString()
    {
        // Act
        var result = _service.CleanFileName("");

        // Assert
        result.Should().Be("");
    }

    [Fact]
    public void CleanFileName_ShouldHandleNull()
    {
        // Act
        var result = _service.CleanFileName(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CleanPath_ShouldReplaceInvalidPathCharacters()
    {
        // Arrange - Test with null character which is invalid in paths
        var path = "C:\\Users\0Test\\Files";

        // Act
        var result = _service.CleanPath(path);

        // Assert
        result.Should().NotContain("\0");
        result.Should().Contain("_");
    }

    [Fact]
    public void GetAvailableFileTokens_ShouldReturnAllFileTokens()
    {
        // Act
        var tokens = _service.GetAvailableFileTokens();

        // Assert
        tokens.Should().Contain("{Event Title}");
        tokens.Should().Contain("{Event Title The}");
        tokens.Should().Contain("{Event CleanTitle}");
        tokens.Should().Contain("{Air Date}");
        tokens.Should().Contain("{Quality}");
        tokens.Should().Contain("{Quality Full}");
        tokens.Should().Contain("{Release Group}");
        tokens.Should().Contain("{Original Title}");
        tokens.Should().Contain("{Original Filename}");
    }

    [Fact]
    public void GetAvailableFolderTokens_ShouldReturnAllFolderTokens()
    {
        // Act
        var tokens = _service.GetAvailableFolderTokens();

        // Assert
        tokens.Should().Contain("{Event Title}");
        tokens.Should().Contain("{Event Title The}");
        tokens.Should().Contain("{Event CleanTitle}");
        tokens.Should().Contain("{Event Id}");
        tokens.Should().Contain("{Year}");
    }

    [Theory]
    [InlineData("The Ultimate Fighter", "Ultimate Fighter, The")]
    [InlineData("A New Beginning", "New Beginning, A")]
    [InlineData("An Event", "Event, An")]
    [InlineData("UFC 300", "UFC 300")] // No article
    public void BuildFolderName_ShouldHandleArticleMovement(string title, string expected)
    {
        // Arrange
        var format = "{Event Title The}";
        var eventInfo = new Event
        {
            Title = title,
            Organization = "UFC",
            EventDate = new DateTime(2024, 1, 1)
        };

        // Act
        var result = _service.BuildFolderName(format, eventInfo);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void BuildFileName_ShouldHandleComplexFormat()
    {
        // Arrange
        var format = "{Event Title} - {Air Date} - {Quality Full} - {Release Group}";
        var tokens = new FileNamingTokens
        {
            EventTitle = "UFC 300",
            AirDate = new DateTime(2024, 4, 13),
            QualityFull = "1080p BluRay x265",
            ReleaseGroup = "FIGHTARR"
        };

        // Act
        var result = _service.BuildFileName(format, tokens, ".mkv");

        // Assert
        result.Should().Be("UFC 300 - 2024-04-13 - 1080p BluRay x265 - FIGHTARR.mkv");
    }

    [Fact]
    public void BuildFileName_ShouldCleanInvalidCharactersInReplacedTokens()
    {
        // Arrange
        var format = "{Event Title}";
        var tokens = new FileNamingTokens
        {
            EventTitle = "UFC 300: Main Event?" // Contains invalid characters
        };

        // Act
        var result = _service.BuildFileName(format, tokens, ".mkv");

        // Assert
        result.Should().NotContain(":");
        result.Should().NotContain("?");
        result.Should().Be("UFC 300 Main Event.mkv"); // Invalid chars cleaned up
    }

    [Fact]
    public void BuildFileName_ShouldHandleCaseInsensitiveTokens()
    {
        // Arrange
        var format = "{event title} - {QUALITY}";
        var tokens = new FileNamingTokens
        {
            EventTitle = "UFC 300",
            Quality = "1080p"
        };

        // Act
        var result = _service.BuildFileName(format, tokens, ".mkv");

        // Assert
        result.Should().Be("UFC 300 - 1080p.mkv");
    }

    [Fact]
    public void BuildFolderName_ShouldHandleEventId()
    {
        // Arrange
        var format = "{Event Title} - {Event Id}";
        var eventInfo = new Event
        {
            Id = 123,
            Title = "UFC 300",
            Organization = "UFC",
            EventDate = new DateTime(2024, 4, 13)
        };

        // Act
        var result = _service.BuildFolderName(format, eventInfo);

        // Assert
        result.Should().Be("UFC 300 - 123");
    }

    [Fact]
    public void BuildFileName_ShouldHandleOriginalTitle()
    {
        // Arrange
        var format = "{Original Title}";
        var tokens = new FileNamingTokens
        {
            OriginalTitle = "UFC.300.1080p.WEB-DL.x264-GROUP"
        };

        // Act
        var result = _service.BuildFileName(format, tokens, ".mkv");

        // Assert
        result.Should().Be("UFC.300.1080p.WEB-DL.x264-GROUP.mkv");
    }

    [Fact]
    public void BuildFileName_ShouldHandleOriginalFilename()
    {
        // Arrange
        var format = "{Original Filename}";
        var tokens = new FileNamingTokens
        {
            OriginalFilename = "original_file_name"
        };

        // Act
        var result = _service.BuildFileName(format, tokens, ".mkv");

        // Assert
        result.Should().Be("original_file_name.mkv");
    }
}
