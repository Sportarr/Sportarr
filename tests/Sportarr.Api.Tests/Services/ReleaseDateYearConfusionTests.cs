using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A four-digit number in an event or club name was read as the year. Every
/// title here came from a real indexer response. The library holds thousands
/// of events whose names carry such a number: clubs founded in a year
/// (Bohemians 1905, CSKA 1948, CB 1939 Canarias) and races named after a
/// distance (Bathurst 1000).
/// </summary>
public class ReleaseDateYearConfusionTests
{
    private static SportsFileNameParser Parser() => new(Mock.Of<ILogger<SportsFileNameParser>>());

    [Theory]
    // The number sits where a year would, and the real date follows it.
    [InlineData("Supercars 2025 Race 27 Bathurst 1000 12 10 1080p EN", 2025, 10, 12)]
    [InlineData("Supercars 2024 Race 20 Bathurst 1000 13 10 1080p EN", 2024, 10, 13)]
    [InlineData("Supercars 2025 Top 10 Shootout Bathurst 1000 11 10 1080p EN", 2025, 10, 11)]
    public void AnEventNumberIsNotTheYear(string title, int year, int month, int day)
    {
        var parsed = Parser().Parse(title);

        parsed.EventDate.Should().Be(new DateTime(year, month, day));
    }

    [Theory]
    // These already worked and must keep working.
    [InlineData("Friendly 2023 07 05 Panathinaikos vs CSKA 1948 Sofia 1080p 25fps BULGARiAN", 2023, 7, 5)]
    [InlineData("Football Vtora Liga 2019 04 01 CSKA 1948 Sofia vs Litex Lovech 1080p HDTV x264", 2019, 4, 1)]
    [InlineData("FIA WEC 2024 06 15 Round 04 24 Hours of Le Mans FULL RACE WEB DL 1080p50 Multi ES", 2024, 6, 15)]
    [InlineData("EFL League Two 2024 03 29 Wrexham vs Mansfield Town SKY 1080p50 EN", 2024, 3, 29)]
    public void ARealDateIsStillRead(string title, int year, int month, int day)
    {
        Parser().Parse(title).EventDate.Should().Be(new DateTime(year, month, day));
    }

    [Fact]
    public void ADayFirstPairAfterTheYearStillSwaps()
    {
        // "Indianapolis 500 2025 26 05" has no valid month 26, so the pair is
        // read the other way round. This came from a real indexer too.
        Parser().Parse("Indianapolis 500 2025 26 05 720pEN60fps Fox")
            .EventDate.Should().Be(new DateTime(2025, 5, 26));
    }

    [Fact]
    public void AClubFoundedLongAgoDoesNotBecomeADate()
    {
        // A club's founding year followed by a date pair used to win outright.
        Parser().Parse("Czech First League Bohemians 1905 12 04 vs Slavia 1080p")
            .EventDate.Should().NotBe(new DateTime(1905, 12, 4));
    }
}
