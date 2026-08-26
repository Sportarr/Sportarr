using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Several team patterns end in a group like (?:[A-Za-z]+[\.\-\s]*)+?, whose
/// inner part can match one run of letters many different ways. A title that
/// never satisfies the lookahead after it makes the engine try every division
/// of that run, which grows exponentially. Titles come from indexers, and
/// Parse runs on every one inside the RSS loop, so one crafted title could
/// hold a worker for as long as it liked.
/// </summary>
public class SportsParserBacktrackingTests
{
    private static SportsFileNameParser Parser() =>
        new(Mock.Of<ILogger<SportsFileNameParser>>());

    [Theory]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(40)]
    public void AHostileTitleReturnsQuickly(int runLength)
    {
        // A long unbroken run of letters where the lookahead can never be
        // satisfied: no resolution token, no recognised source tag.
        var hostile = "NFL.2026.Week.5.Patriots.vs." + new string('a', runLength) + "!";

        var stopwatch = Stopwatch.StartNew();
        var result = Parser().Parse(hostile);
        stopwatch.Stop();

        result.Should().NotBeNull();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
            "a title that defeats a pattern must not hold the worker; the pattern timeout bounds it");
    }

    [Fact]
    public void AnOrdinaryTitleStillParses()
    {
        var result = Parser().Parse("NFL.2026.Week.5.Patriots.vs.Jets.1080p.WEB.h264-RIG");

        result.Should().NotBeNull();
        result.Organization.Should().Be("NFL");
    }
}
