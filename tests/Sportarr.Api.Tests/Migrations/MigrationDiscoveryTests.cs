using FluentAssertions;

namespace Sportarr.Api.Tests.Migrations;

/// <summary>
/// EF discovers a migration by the Migration attribute, which the scaffolder
/// writes into the .Designer.cs file beside it. A migration file without that
/// sibling is invisible: it never appears in the applied history and its
/// operations never run, with nothing at build or start time to say so.
///
/// AddBackupRestoreInfrastructure was written this way, so the RestoreReports
/// table was never created on SQLite and every restore ended in an error for
/// three months before anyone traced it. This test turns the same mistake into
/// a failing build.
///
/// The migrations already in the tree that lack a designer are listed below.
/// They stay listed rather than being fixed: giving them the attribute now
/// would make EF treat all of them as pending on every existing install, and
/// they would fail against columns that are already there. Their operations
/// were all re-emitted by later scaffolds, which is why the schema is whole.
/// Nothing should be added to this list.
/// </summary>
public class MigrationDiscoveryTests
{
    private static readonly HashSet<string> KnownUndiscovered = new(StringComparer.Ordinal)
    {
        "20260307000000_AddIndexerIdToDownloadQueue",
        "20260314220000_MakePendingImportDownloadClientIdNullable",
        "20260321000000_AddDirectoryToDownloadClient",
        "20260415000000_AddBroadcastDateToEvent",
        "20260415100000_AddPendingReleases",
        "20260415200000_AddIndexerMinimumAgeMinutes",
        "20260428000000_AddFilePathToBlocklist",
        "20260502000000_AddRootFolderIdToLeague",
        "20260502120000_DropPersistedRootFolderState",
        "20260502130000_AddRootFolderDefaults",
        "20260502140000_AddRssIndexerFields",
        "20260502150000_AddFailDownloads",
        "20260503000000_AddPreferredChannelPerLeagueIndex",
        "20260503010000_AddLeagueDvrPadding",
        "20260503020000_AddIptvOrgIdToChannel",
        "20260505000000_AddEventFileLanguagesAndIndexerFlags",
        "20260506000000_AddEventFileMissingSince",
        "20260507000000_AddLeagueAlternateName",
        "20260510000000_AddLeagueMetadataLastSyncedAt",
        "20260516220000_AddScoredMappingsAndDvrFallback",
        "20260517000000_AddBackupRestoreInfrastructure",
        "20260612000000_AddCatchupSupport",
        "20260615000000_DropMediaManagementRootFoldersColumn",
        "20260616000000_AddEventFileHistory",
    };

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Sportarr.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must be able to find the repository root");
        return dir!.FullName;
    }

    public static TheoryData<string> MigrationDirectories() => new()
    {
        Path.Combine("src", "Sportarr.Data", "Migrations"),
        Path.Combine("src", "Sportarr.Migrations.Postgres", "Migrations"),
    };

    [Theory]
    [MemberData(nameof(MigrationDirectories))]
    public void EveryMigrationHasTheDesignerFileThatMakesItDiscoverable(string relativeDirectory)
    {
        var directory = Path.Combine(RepositoryRoot(), relativeDirectory);
        Directory.Exists(directory).Should().BeTrue($"{relativeDirectory} should exist");

        var undiscovered = Directory.GetFiles(directory, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(f => !Path.GetFileName(f).Contains("ModelSnapshot", StringComparison.Ordinal))
            .Select(f => f[..^3])
            .Where(stem => !File.Exists(stem + ".Designer.cs"))
            .Select(stem => Path.GetFileName(stem))
            .Where(name => !KnownUndiscovered.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        undiscovered.Should().BeEmpty(
            "a migration with no .Designer.cs carries no Migration attribute, so EF never runs it and " +
            "the schema it describes is never created. Scaffold it with `dotnet ef migrations add` " +
            "instead of writing the file by hand.");
    }

    [Fact]
    public void TheUndiscoveredListDoesNotNameMigrationsThatAreFine()
    {
        var directory = Path.Combine(RepositoryRoot(), "src", "Sportarr.Data", "Migrations");

        var stale = KnownUndiscovered
            .Where(name => File.Exists(Path.Combine(directory, name + ".Designer.cs")) ||
                           !File.Exists(Path.Combine(directory, name + ".cs")))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        stale.Should().BeEmpty("the list should shrink only when a migration is scaffolded properly or removed");
    }
}
