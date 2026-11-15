using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services.Import;
using Xunit;

namespace Sportarr.Api.Tests.Services;

public class LibraryFolderResolverTests
{
    [Theory]
    [InlineData("Season 2024", 2024, null)]
    [InlineData("Season 2024-2025", 2024, 2025)]
    [InlineData("Season 2024-25", 2024, 2025)]
    [InlineData("2024", 2024, null)]
    [InlineData("2024-25", 2024, 2025)]
    [InlineData("season 1999-00", 1999, 2000)]
    public void ParseSeasonFolder_ReadsYearForms(string name, int start, int? end)
        => LibraryFolderResolver.ParseSeasonFolder(name).Should().Be(new SeasonFolder(start, end));

    [Theory]
    [InlineData("Season 0")]
    [InlineData("Specials")]
    [InlineData("Extras")]
    [InlineData("Season 1")]
    [InlineData("Season 2024-2023")]
    [InlineData("UFC 300 Pereira vs Hill (2024-04-13) E13")]
    public void ParseSeasonFolder_IgnoresSpecials(string name)
        => LibraryFolderResolver.ParseSeasonFolder(name).Should().BeNull();

    [Fact]
    public void MapToLeagueSeason_PrefersExactYear()
        => LibraryFolderResolver.MapToLeagueSeason(new(2024, null), new[] { "2024", "2024-2025" }).Should().Be("2024");

    [Fact]
    public void MapToLeagueSeason_FallsBackToTheSpanStartingThatYear()
        => LibraryFolderResolver.MapToLeagueSeason(new(2024, null), new[] { "2023-2024", "2024-2025" }).Should().Be("2024-2025");

    [Fact]
    public void MapToLeagueSeason_SpanMatchesIdenticalSpanOnly()
        => LibraryFolderResolver.MapToLeagueSeason(new(2024, 2025), new[] { "2024", "2024-2025" }).Should().Be("2024-2025");

    [Fact]
    public void MapToLeagueSeason_SpanDoesNotTakeTheStartYear()
        => LibraryFolderResolver.MapToLeagueSeason(new(2024, 2025), new[] { "2024", "2025" }).Should().BeNull();

    [Fact]
    public void MapToLeagueSeason_NoFit_IsNull()
        => LibraryFolderResolver.MapToLeagueSeason(new(2019, null), new[] { "2024" }).Should().BeNull();

    [Fact]
    public void ResolveLeague_UsesAlternates()
    {
        var ufc = new League { Name = "UFC", Sport = "Combat", AlternateName = "Ultimate Fighting Championship" };
        LibraryFolderResolver.ResolveLeague("Ultimate Fighting Championship", new[] { ufc }).Should().BeSameAs(ufc);
    }

    [Fact]
    public void ResolveLeague_SiblingSeriesStayApart()
    {
        var f1 = new League { Name = "Formula 1", Sport = "Motorsport" };
        LibraryFolderResolver.ResolveLeague("Formula 2", new[] { f1 }).Should().BeNull();
    }

    [Fact]
    public void ResolveLeague_ExactName()
    {
        var f1 = new League { Name = "Formula 1", Sport = "Motorsport" };
        var fe = new League { Name = "Formula E", Sport = "Motorsport" };
        LibraryFolderResolver.ResolveLeague("Formula 1", new[] { fe, f1 }).Should().BeSameAs(f1);
    }

    [Fact]
    public void ResolveLeague_FolderNamePartOfALongerLeagueName_IsNull()
    {
        var antigua = new League { Name = "Antigua and Barbuda Premier League", Sport = "Soccer" };
        LibraryFolderResolver.ResolveLeague("Premier League", new[] { antigua }).Should().BeNull();
    }

    [Fact]
    public void ResolveLeague_WordsOfALeagueName_IsNull()
    {
        var f1 = new League { Name = "Formula 1", Sport = "Motorsport" };
        LibraryFolderResolver.ResolveLeague("Formula", new[] { f1 }).Should().BeNull();
    }

    [Fact]
    public void ResolveLeague_ExactNameIgnoresALongerLeagueName()
    {
        var nba = new League { Name = "NBA", Sport = "Basketball" };
        var gLeague = new League { Name = "NBA G League", Sport = "Basketball" };
        LibraryFolderResolver.ResolveLeague("NBA", new[] { gLeague, nba }).Should().BeSameAs(nba);
    }

    [Fact]
    public void ResolveLeague_TwoExactMatches_IsNull()
    {
        var a = new League { Name = "Super League", Sport = "Rugby" };
        var b = new League { Name = "Super League", Sport = "Soccer" };
        LibraryFolderResolver.ResolveLeague("Super League", new[] { a, b }).Should().BeNull();
    }

    [Fact]
    public void SplitPath_SeasonFolderPresent()
        => LibraryFolderResolver.SplitPath("/lib", "/lib/Formula 1/Season 2024/x.mkv").Should().Be(("Formula 1", "Season 2024"));

    [Fact]
    public void SplitPath_NoSeasonFolder()
        => LibraryFolderResolver.SplitPath("/lib", "/lib/Formula 1/x.mkv").Should().Be(("Formula 1", (string?)null));

    [Fact]
    public void SplitPath_EventFolderInsideTheSeason()
        => LibraryFolderResolver.SplitPath("/lib", "/lib/UFC/Season 2024/UFC 300 Pereira vs Hill/x.mkv")
            .Should().Be(("UFC", "Season 2024"));

    [Fact]
    public void SplitPath_EventFolderWithoutASeason()
        => LibraryFolderResolver.SplitPath("/lib", "/lib/UFC/UFC 300 Pereira vs Hill/x.mkv")
            .Should().Be(("UFC", (string?)null));

    [Fact]
    public void SplitPath_GroupFolderAboveTheLeague()
        => LibraryFolderResolver.SplitPath("/lib", "/lib/Motorsport/Formula 1/Season 2024/x.mkv")
            .Should().Be(("Formula 1", "Season 2024"));

    [Fact]
    public void SplitPath_FileDirectlyUnderTheRoot()
        => LibraryFolderResolver.SplitPath("/lib/UFC", "/lib/UFC/x.mkv").Should().Be(("UFC", (string?)null));

    [Fact]
    public void LeagueFolderPath_IsTheFolderThatSplitPathNames()
    {
        LibraryFolderResolver.LeagueFolderPath("/lib", "/lib/Motorsport/Formula 1/Season 2024/Event/x.mkv")
            .Should().Be(Path.Combine("/lib", "Motorsport", "Formula 1"));
        LibraryFolderResolver.LeagueFolderPath("/lib/UFC/", "/lib/UFC/Season 2024/x.mkv").Should().Be("/lib/UFC");
        LibraryFolderResolver.LeagueFolderPath("/lib/UFC", "/lib/UFC/x.mkv").Should().Be("/lib/UFC");
    }

    [Fact]
    public void SplitPath_SeasonFolderDirectlyUnderTheRoot()
        => LibraryFolderResolver.SplitPath("/lib/UFC", "/lib/UFC/Season 2024/x.mkv").Should().Be(("UFC", "Season 2024"));

    private static readonly League[] RootLeagues =
    {
        new() { Name = "Formula 1", Sport = "Motorsport" },
        new() { Name = "F1 Academy", Sport = "Motorsport" },
        new() { Name = "UFC", Sport = "Fighting" },
    };

    [Theory]
    [InlineData("/lib/Formula 1/x.mkv")]
    [InlineData("/lib/Formula 1/Season 2024/x.mkv")]
    [InlineData("/lib/Formula 1/Bahrain Grand Prix/x.mkv")]
    public void RootLeagueApplies_ToTheRootsOwnFolders(string file)
        => LibraryFolderResolver.RootLeagueApplies("/lib/Formula 1", file, RootLeagues).Should().BeTrue();

    [Theory]
    [InlineData("/lib/Formula 1/F1 Academy/Season 2024/x.mkv")]
    [InlineData("/lib/Formula 1/UFC/Season 2024/x.mkv")]
    [InlineData("/lib/Formula 1/UFC/x.mkv")]
    public void RootLeagueApplies_NotToTheFolderOfAnotherLeague(string file)
        => LibraryFolderResolver.RootLeagueApplies("/lib/Formula 1", file, RootLeagues).Should().BeFalse();

    [Theory]
    [InlineData("Season 2024", false)]
    [InlineData("UFC", false)]
    [InlineData("NBA", true)]
    public void IsUnlinkedFolder_SkipsSeasonAndLeagueFolders(string name, bool unlinked)
        => LibraryFolderResolver.IsUnlinkedFolder(name, RootLeagues).Should().Be(unlinked);
}
