using Sportarr.Api.Helpers;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

public class Nfl244ReproTests
{
    [Fact]
    public void NflPostseason_BypassesTeamFilter()
    {
        // The real NFL 2025 hub data: rounds 1-18 regular season, 500
        // preseason, then 160/125/150/200 postseason codes.
        var rounds = new List<string?>();
        for (var wk = 1; wk <= 18; wk++) for (var g = 0; g < 15; g++) rounds.Add(wk.ToString());
        for (var g = 0; g < 49; g++) rounds.Add("500");
        rounds.AddRange(new[] { "160", "160", "160", "160", "160", "160", "125", "125", "125", "125", "150", "150", "200" });

        var sizes = SpecialEventClassifier.ComputeCupStageSizes(rounds);

        SpecialEventClassifier.BypassesTeamFilter("200", "New England Patriots vs Seattle Seahawks", true, false, false, sizes)
            .Should().BeTrue("round 200 is the Super Bowl and MonitorFinals is on");
        SpecialEventClassifier.BypassesTeamFilter("160", "Denver Broncos vs Buffalo Bills", false, true, false, sizes)
            .Should().BeTrue("round 160 is a wildcard game and MonitorPlayoffs is on");
        SpecialEventClassifier.BypassesTeamFilter("7", "Some vs Game", true, true, false, sizes)
            .Should().BeFalse("a regular season week is neither");
    }
}
