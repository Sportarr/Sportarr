using Sportarr.Api.Services;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Sportarr.Api.Tests.Services;

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
            ReleaseGroup = "SPORTARR"
        };

        // Act
        var result = _service.BuildFileName(format, tokens, ".mkv");

        // Assert
        result.Should().Be("UFC 300 [SPORTARR].mkv");
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
            League = new League { Name = "UFC", Sport = "Fighting" },
            EventDate = new DateTime(2024, 4, 13),
            Sport = "Fighting"
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
            League = new League { Name = "UFC", Sport = "Fighting" },
            EventDate = new DateTime(2024, 4, 13),
            Sport = "Fighting"
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
            League = new League { Name = "UFC", Sport = "Fighting" },
            EventDate = new DateTime(2024, 1, 1),
            Sport = "Fighting"
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
            League = new League { Name = "UFC", Sport = "Fighting" },
            EventDate = new DateTime(2024, 1, 1),
            Sport = "Fighting"
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
            ReleaseGroup = "SPORTARR"
        };

        // Act
        var result = _service.BuildFileName(format, tokens, ".mkv");

        // Assert
        result.Should().Be("UFC 300 - 2024-04-13 - 1080p BluRay x265 - SPORTARR.mkv");
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
            League = new League { Name = "UFC", Sport = "Fighting" },
            EventDate = new DateTime(2024, 4, 13),
            Sport = "Fighting"
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

    // Issue #170: {Part Name} renders the human part label ("Prelims",
    // "Main Card") instead of the opaque pt1/pt2, with the separator embedded
    // (same convention as {Part}) so single-part files render cleanly.
    [Fact]
    public void BuildFileName_PartNameToken_RendersHumanLabel()
    {
        var format = "{Series} - {Season}{Episode} - {Event Title}{Part Name} - {Quality Full}";
        var tokens = new FileNamingTokens
        {
            Series = "UFC",
            Season = "2026",
            Episode = "24",
            EventTitle = "UFC Fight Night 279 Kape vs Horiguchi",
            PartName = " - Prelims",
            QualityFull = "WEBDL-1080p"
        };

        var result = _service.BuildFileName(format, tokens, ".mkv");

        result.Should().Be("UFC - S2026E24 - UFC Fight Night 279 Kape vs Horiguchi - Prelims - WEBDL-1080p.mkv");
    }

    [Fact]
    public void BuildFileName_PartNameToken_EmptyForSinglePartFiles()
    {
        var format = "{Series} - {Season}{Episode} - {Event Title}{Part Name} - {Quality Full}";
        var tokens = new FileNamingTokens
        {
            Series = "UFC",
            Season = "2026",
            Episode = "24",
            EventTitle = "UFC Fight Night 279 Kape vs Horiguchi",
            PartName = string.Empty,
            QualityFull = "WEBDL-1080p"
        };

        var result = _service.BuildFileName(format, tokens, ".mkv");

        result.Should().Be("UFC - S2026E24 - UFC Fight Night 279 Kape vs Horiguchi - WEBDL-1080p.mkv");
    }

    [Fact]
    public void BuildFileName_PartAndPartName_CanCoexist()
    {
        // Users can keep pt{N} for plugin compatibility AND add the label.
        var format = "{Event Title}{Part}{Part Name}";
        var tokens = new FileNamingTokens
        {
            EventTitle = "UFC 300",
            Part = " - pt2",
            PartName = " - Main Card"
        };

        var result = _service.BuildFileName(format, tokens, ".mkv");

        result.Should().Be("UFC 300 - pt2 - Main Card.mkv");
    }

    [Fact]
    public void GetAvailableFileTokens_IncludesPartName()
    {
        _service.GetAvailableFileTokens().Should().Contain("{Part Name}");
    }

    private static Event MotorsportEvent(string title) => new()
    {
        Title = title,
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 5, 24, 13, 0, 0, DateTimeKind.Utc),
        EpisodeNumber = 41,
        Season = "2026"
    };

    private static MediaManagementSettings FolderSettings(string? eventFolderFormat = null) => new()
    {
        CreateLeagueFolders = true,
        LeagueFolderFormat = "{League}",
        CreateSeasonFolders = true,
        SeasonFolderFormat = "Season {Season}",
        CreateEventFolders = true,
        EventFolderFormat = eventFolderFormat ?? "{Event Title} ({Year}-{Month}-{Day}) E{Episode}"
    };

    [Fact]
    public void BuildFolderPath_HonorsConfiguredEventFolderFormat()
    {
        var path = _service.BuildFolderPath(FolderSettings("{Event Weekend Title}"), MotorsportEvent("Monaco Grand Prix Qualifying"));

        path.Should().EndWith($"{System.IO.Path.DirectorySeparatorChar}Monaco Grand Prix");
    }

    [Fact]
    public void BuildFolderPath_WeekendTitleGroupsAllSessionsIntoOneFolder()
    {
        var settings = FolderSettings("{Event Weekend Title}");
        var sessions = new[]
        {
            "Monaco Grand Prix Practice 1",
            "Monaco Grand Prix Practice 3",
            "Monaco Grand Prix Sprint Qualifying",
            "Monaco Grand Prix Qualifying",
            "Monaco Grand Prix - Qualifying",
            "Monaco Grand Prix Race",
            "Monaco Grand Prix"
        };

        var paths = sessions.Select(s => _service.BuildFolderPath(settings, MotorsportEvent(s))).Distinct().ToList();

        paths.Should().HaveCount(1, "every session of the weekend should share one folder");
        paths[0].Should().EndWith("Monaco Grand Prix");
    }

    [Fact]
    public void BuildFolderPath_BlankFormatFallsBackToHistoricalDefault()
    {
        var path = _service.BuildFolderPath(FolderSettings(""), MotorsportEvent("Monaco Grand Prix Race"));

        path.Should().EndWith("Monaco Grand Prix Race (2026-05-24) E41");
    }

    [Theory]
    [InlineData("Mexico City Grand Prix Practice 3", "Mexico City Grand Prix")]
    [InlineData("MXGP of Portugal Race 2", "MXGP of Portugal")]
    [InlineData("MXGP of Portugal Qualifying Race", "MXGP of Portugal")]
    [InlineData("Australian Grand Prix Sprint Qualifying", "Australian Grand Prix")]
    [InlineData("Bahrain Testing 1 Day 2", "Bahrain")]
    [InlineData("Arsenal vs Chelsea", "Arsenal vs Chelsea")]
    [InlineData("UFC Fight Night: Volkov vs Aspinall", "UFC Fight Night: Volkov vs Aspinall")]
    [InlineData("Race", "Race")] // stripping must never produce an empty title
    public void GetMotorsportWeekendTitle_StripsOnlyTrailingSessionDesignators(string title, string expected)
    {
        EventPartDetector.GetMotorsportWeekendTitle(title).Should().Be(expected);
    }

    [Fact]
    public void BuildFileName_SportarrIdToken_SurvivesIntoFilename()
    {
        // The rendered token is itself a braced literal; the unknown-token
        // cleanup must not eat it (it did before the single-pass rewrite).
        var format = "{Series} - {Season}{Episode} - {Event Title} - {Quality Full} {Sportarr Id}";
        var tokens = new FileNamingTokens
        {
            Series = "Formula 1",
            Season = "2026",
            Episode = "12",
            EventTitle = "British Grand Prix",
            QualityFull = "WEBDL-1080p",
            SportarrId = "ev-2338110"
        };

        var result = _service.BuildFileName(format, tokens, ".mkv");

        result.Should().Be("Formula 1 - S2026E12 - British Grand Prix - WEBDL-1080p {sportarr-ev-2338110}.mkv");
    }

    [Fact]
    public void BuildFileName_SportarrIdToken_LegacyEventLeavesCleanName()
    {
        // Legacy rows have no ev- id; the token renders empty and the
        // trailing space it would leave must be trimmed away.
        var format = "{Event Title} - {Quality Full} {Sportarr Id}";
        var tokens = new FileNamingTokens
        {
            EventTitle = "UFC 300",
            QualityFull = "Bluray-1080p",
            SportarrId = "123456"
        };

        var result = _service.BuildFileName(format, tokens, ".mkv");

        result.Should().Be("UFC 300 - Bluray-1080p.mkv");
    }

    [Fact]
    public void BuildFileName_UnknownTokens_AreStillRemoved()
    {
        var format = "{Event Title} {Bogus Token} - {Quality}";
        var tokens = new FileNamingTokens
        {
            EventTitle = "UFC 300",
            Quality = "1080p"
        };

        var result = _service.BuildFileName(format, tokens, ".mkv");

        result.Should().Be("UFC 300 - 1080p.mkv");
    }
}
