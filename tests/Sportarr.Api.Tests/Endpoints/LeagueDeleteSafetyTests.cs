using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Deleting a league with its files used to infer which folder to remove by
/// walking two directories up from an event file, assuming the layout
/// {Root}/{League}/Season X/file. Season folders are optional, so with them
/// off the walk landed on the root folder and the recursive delete took
/// every other league with it. A target is now only deleted when it sits
/// inside that league's own root.
/// </summary>
public class LeagueDeleteSafetyTests
{
    private const string LeagueRoot = "/data/media/sports";
    private static readonly string[] AllRoots = { "/data/media/sports", "/mnt/other" };

    [Theory]
    [InlineData("/data/media/sports/UFC")]
    [InlineData("/data/media/sports/Formula 1")]
    [InlineData("/data/media/sports/UFC/")]
    public void ALeaguesOwnFolder_IsDeletable(string target)
    {
        LeagueEndpoints.IsSafeLeagueFolderTarget(target, LeagueRoot, AllRoots).Should().BeTrue();
    }

    [Theory]
    [InlineData("/data/media/sports")]   // the root itself, the original bug
    [InlineData("/data/media/sports/")]  // same with a trailing separator
    [InlineData("/data/media")]          // an ancestor of the root
    [InlineData("/data")]
    [InlineData("/")]
    public void TheRootAndItsAncestors_AreNeverDeleted(string target)
    {
        LeagueEndpoints.IsSafeLeagueFolderTarget(target, LeagueRoot, AllRoots).Should().BeFalse();
    }

    [Theory]
    [InlineData("/mnt/other")]                   // another configured root
    [InlineData("/mnt/other/NFL")]               // inside a different root
    [InlineData("/data/media/sports/../escape")] // traversal out of the root
    [InlineData("/etc")]                         // unrelated absolute path
    public void AnythingOutsideThisLeaguesRoot_IsNeverDeleted(string target)
    {
        LeagueEndpoints.IsSafeLeagueFolderTarget(target, LeagueRoot, AllRoots).Should().BeFalse();
    }

    [Theory]
    [InlineData("", LeagueRoot)]
    [InlineData("   ", LeagueRoot)]
    [InlineData("/data/media/sports/UFC", null)]
    [InlineData("/data/media/sports/UFC", "")]
    public void MissingInput_IsNeverDeleted(string target, string? root)
    {
        LeagueEndpoints.IsSafeLeagueFolderTarget(target, root, AllRoots).Should().BeFalse();
    }

    [Fact]
    public void WithLeagueFoldersOff_NoFolderIsResolvedToDelete()
    {
        // The delete path only queues a folder when this returns a name, so
        // an install without league folders removes files and nothing else.
        var naming = new FileNamingService(NullLogger<FileNamingService>.Instance);
        var settings = new MediaManagementSettings { CreateLeagueFolders = false, LeagueFolderFormat = "{Series}" };

        naming.BuildLeagueFolderName(settings, new League { Name = "UFC", Sport = "Fighting" })
            .Should().BeEmpty();
    }
}
