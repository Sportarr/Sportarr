using Sportarr.Api.Services;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Moq;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Tests for sport detection system in ImportListService and LibraryImportService
/// Verifies that the DeriveEventSport() method correctly identifies sports from keywords
/// </summary>
public class SportDetectionTests
{
    private readonly Mock<ILogger<ImportListService>> _mockImportLogger;
    private readonly Mock<ILogger<LibraryImportService>> _mockLibraryLogger;
    private readonly Mock<ILogger<MediaFileParser>> _mockParserLogger;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly SportarrDbContext _dbContext;
    private readonly MediaFileParser _fileParser;

    public SportDetectionTests()
    {
        _mockImportLogger = new Mock<ILogger<ImportListService>>();
        _mockLibraryLogger = new Mock<ILogger<LibraryImportService>>();
        _mockParserLogger = new Mock<ILogger<MediaFileParser>>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _fileParser = new MediaFileParser(_mockParserLogger.Object);

        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new SportarrDbContext(options);
    }

    #region Fighting Sport Tests

    [Theory]
    [InlineData("UFC", "UFC 300", "Fighting")]
    [InlineData("ufc", "UFC Fight Night", "Fighting")]
    [InlineData("UFC", "ufc 300 main card", "Fighting")]
    [InlineData("Bellator", "Bellator 300", "Fighting")]
    [InlineData("ONE Championship", "ONE Championship 100", "Fighting")]
    [InlineData("PFL", "PFL Championship", "Fighting")]
    [InlineData("Invicta", "Invicta FC 50", "Fighting")]
    [InlineData("Cage Warriors", "Cage Warriors 150", "Fighting")]
    [InlineData("LFA", "LFA 100", "Fighting")]
    [InlineData("DWCS", "Dana White's Contender Series", "Fighting")]
    [InlineData("Rizin", "Rizin 40", "Fighting")]
    [InlineData("KSW", "KSW 80", "Fighting")]
    [InlineData("Glory", "Glory Kickboxing", "Fighting")]
    [InlineData("Combate", "Combate Global", "Fighting")]
    [InlineData("", "MMA Fight Night", "Fighting")]
    [InlineData("", "Boxing Championship", "Fighting")]
    [InlineData("", "Muay Thai Event", "Fighting")]
    [InlineData("", "Kickboxing Match", "Fighting")]
    [InlineData("", "Jiu-Jitsu Tournament", "Fighting")]
    [InlineData("", "BJJ Competition", "Fighting")]
    public void DeriveEventSport_Fighting_Keywords_Should_Return_Fighting(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act - Use reflection to call private method
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport);
    }

    #endregion

    #region Soccer Sport Tests

    [Theory]
    [InlineData("", "Premier League Match", "Soccer")]
    [InlineData("", "La Liga El Clasico", "Soccer")]
    [InlineData("", "Serie A Derby", "Soccer")]
    [InlineData("", "Bundesliga Match", "Soccer")]
    [InlineData("", "Ligue 1 Game", "Soccer")]
    [InlineData("", "Champions League Final", "Soccer")]
    [InlineData("", "Europa League", "Soccer")]
    [InlineData("", "FIFA World Cup", "Soccer")]
    [InlineData("", "World Cup Qualifier", "Soccer")]
    [InlineData("", "MLS Cup", "Soccer")]
    [InlineData("", "Soccer Tournament", "Soccer")]
    [InlineData("", "Football Match", "Soccer")]
    [InlineData("", "Manchester United vs Liverpool", "Soccer")]
    [InlineData("", "Real Madrid CF vs Barcelona", "Soccer")]
    [InlineData("", "Athletic Bilbao Match", "Soccer")]
    public void DeriveEventSport_Soccer_Keywords_Should_Return_Soccer(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport);
    }

    #endregion

    #region Basketball Sport Tests

    [Theory]
    [InlineData("NBA", "Lakers vs Warriors", "Basketball")]
    [InlineData("", "NBA Finals Game 7", "Basketball")]
    [InlineData("", "WNBA Championship", "Basketball")]
    [InlineData("", "NCAA Basketball Tournament", "Basketball")]
    [InlineData("", "EuroLeague Final Four", "Basketball")]
    [InlineData("", "Basketball Championship", "Basketball")]
    [InlineData("", "FIBA World Cup", "Basketball")]
    [InlineData("", "BBL Game", "Basketball")]
    [InlineData("", "ACB Final", "Basketball")]
    public void DeriveEventSport_Basketball_Keywords_Should_Return_Basketball(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport);
    }

    #endregion

    #region American Football Sport Tests

    [Theory]
    [InlineData("NFL", "Patriots vs Chiefs", "American Football")]
    [InlineData("", "NFL Playoffs", "American Football")]
    [InlineData("", "NCAA Football Championship", "American Football")]
    [InlineData("", "College Football Playoff", "American Football")]
    [InlineData("", "Super Bowl LVIII", "American Football")]
    [InlineData("", "American Football Game", "American Football")]
    [InlineData("", "AFL Match", "American Football")]
    [InlineData("", "CFL Grey Cup", "American Football")]
    public void DeriveEventSport_AmericanFootball_Keywords_Should_Return_AmericanFootball(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport);
    }

    #endregion

    #region Baseball Sport Tests

    [Theory]
    [InlineData("MLB", "Yankees vs Red Sox", "Baseball")]
    [InlineData("", "MLB World Series", "Baseball")]
    [InlineData("", "World Series Game 7", "Baseball")]
    [InlineData("", "Baseball Championship", "Baseball")]
    [InlineData("", "NPB Japan Series", "Baseball")]
    [InlineData("", "KBO League", "Baseball")]
    public void DeriveEventSport_Baseball_Keywords_Should_Return_Baseball(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport);
    }

    #endregion

    #region Ice Hockey Sport Tests

    [Theory]
    [InlineData("NHL", "Maple Leafs vs Canadiens", "Ice Hockey")]
    [InlineData("", "NHL Stanley Cup Finals", "Ice Hockey")]
    [InlineData("", "Stanley Cup Playoffs", "Ice Hockey")]
    [InlineData("", "Hockey Championship", "Ice Hockey")]
    [InlineData("", "KHL Match", "Ice Hockey")]
    [InlineData("", "SHL Game", "Ice Hockey")]
    [InlineData("", "Liiga Finals", "Ice Hockey")]
    public void DeriveEventSport_IceHockey_Keywords_Should_Return_IceHockey(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport);
    }

    #endregion

    #region Tennis Sport Tests

    [Theory]
    [InlineData("", "Wimbledon Men's Final", "Tennis")]
    [InlineData("", "US Open Championship", "Tennis")]
    [InlineData("", "French Open Final", "Tennis")]
    [InlineData("", "Australian Open Match", "Tennis")]
    [InlineData("", "ATP Finals", "Tennis")]
    [InlineData("", "WTA Championship", "Tennis")]
    [InlineData("", "Tennis Grand Slam", "Tennis")]
    [InlineData("", "Grand Slam Final", "Tennis")]
    public void DeriveEventSport_Tennis_Keywords_Should_Return_Tennis(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport);
    }

    #endregion

    #region Golf Sport Tests

    [Theory]
    [InlineData("", "PGA Championship", "Golf")]
    [InlineData("", "The Masters Final Round", "Golf")]
    [InlineData("", "Golf Tournament", "Golf")]
    [InlineData("", "Open Championship", "Golf")]
    [InlineData("", "Ryder Cup Match", "Golf")]
    public void DeriveEventSport_Golf_Keywords_Should_Return_Golf(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport);
    }

    #endregion

    #region Motorsport Sport Tests

    [Theory]
    [InlineData("", "Formula 1 Grand Prix", "Motorsport")]
    [InlineData("", "F1 Monaco GP", "Motorsport")]
    [InlineData("", "Formula One Championship", "Motorsport")]
    [InlineData("", "NASCAR Cup Series", "Motorsport")]
    [InlineData("", "IndyCar Race", "Motorsport")]
    [InlineData("", "MotoGP Championship", "Motorsport")]
    [InlineData("", "Rally Championship", "Motorsport")]
    [InlineData("", "Grand Prix Racing", "Motorsport")]
    [InlineData("", "Racing Series", "Motorsport")]
    [InlineData("", "Motorsport Event", "Motorsport")]
    public void DeriveEventSport_Motorsport_Keywords_Should_Return_Motorsport(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport);
    }

    #endregion

    #region Rugby Sport Tests

    [Theory]
    [InlineData("", "Rugby World Cup", "Rugby")]
    [InlineData("", "Six Nations Championship", "Rugby")]
    [InlineData("", "Super Rugby Final", "Rugby")]
    [InlineData("", "NRL Grand Final", "Rugby")]
    [InlineData("", "Rugby League Match", "Rugby")]
    public void DeriveEventSport_Rugby_Keywords_Should_Return_Rugby(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport);
    }

    #endregion

    #region Cricket Sport Tests

    [Theory]
    [InlineData("", "Cricket Test Match", "Cricket")]
    [InlineData("", "Test Match Day 5", "Cricket")]
    [InlineData("", "ODI Championship", "Cricket")]
    [InlineData("", "T20 World Cup", "Cricket")]
    [InlineData("", "IPL Final", "Cricket")]
    [InlineData("", "BBL Match", "Cricket")]
    public void DeriveEventSport_Cricket_Keywords_Should_Return_Cricket(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport);
    }

    #endregion

    #region Default and Edge Cases

    [Theory]
    [InlineData("", "", "Fighting")] // Empty strings default to Fighting
    [InlineData("Unknown Organization", "Unknown Event", "Fighting")] // Unknown defaults to Fighting
    [InlineData("", "Random Event Title 2024", "Fighting")] // No keywords default to Fighting
    public void DeriveEventSport_Unknown_Keywords_Should_Default_To_Fighting(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport, "backward compatibility requires unknown events to default to Fighting");
    }

    [Theory]
    [InlineData("UFC", "", "Fighting")] // Organization only, no title
    [InlineData("", "UFC 300", "Fighting")] // Title only, no organization
    [InlineData("UFC", "UFC 300", "Fighting")] // Both organization and title
    public void DeriveEventSport_Should_Check_Both_Organization_And_Title(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport);
    }

    [Theory]
    [InlineData("ufc", "UFC 300", "Fighting")] // Mixed case
    [InlineData("UFC", "ufc 300", "Fighting")] // Mixed case
    [InlineData("PrEmIeR lEaGuE", "SOCCER MATCH", "Soccer")] // Mixed case
    public void DeriveEventSport_Should_Be_Case_Insensitive(
        string organization, string title, string expectedSport)
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Act
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(service, new object[] { organization, title }) as string;

        // Assert
        result.Should().Be(expectedSport, "sport detection should be case-insensitive");
    }

    #endregion

    #region Priority Tests (First Match Wins)

    [Fact]
    public void DeriveEventSport_Fighting_Keywords_Take_Priority()
    {
        // Arrange
        var service = new LibraryImportService(
            _dbContext,
            _mockLibraryLogger.Object,
            _fileParser);

        // Fighting keywords are checked first, so UFC should return Fighting
        var method = typeof(LibraryImportService).GetMethod(
            "DeriveEventSport",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        var result = method?.Invoke(service, new object[] { "UFC", "UFC 300" }) as string;

        // Assert
        result.Should().Be("Fighting", "Fighting keywords are checked first in priority order");
    }

    #endregion
}
