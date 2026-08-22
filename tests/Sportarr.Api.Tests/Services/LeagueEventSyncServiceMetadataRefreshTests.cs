using System.Reflection;
using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The weekly upstream metadata refresh owns the hub-sourced fields
/// (AlternateName, artwork, description) and nothing else. The league search
/// preferences are local: a user alias, a customized alias order, and an
/// early-stop override must all survive every refresh, or a league the user
/// tuned would silently revert on the next sync.
/// </summary>
public class LeagueEventSyncServiceMetadataRefreshTests
{
    private static void ApplyUpstreamMetadata(League league, League fullDetails)
    {
        var method = typeof(LeagueEventSyncService)
            .GetMethod("ApplyUpstreamMetadata", BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Invoke(null, new object?[] { league, fullDetails });
    }

    [Fact]
    public void Refresh_UpdatesUpstreamAlternateName_AndLeavesLocalSearchPreferencesUntouched()
    {
        var order = new List<LeagueAliasOrderEntry>
        {
            new() { Source = LeagueNameFormSource.UserAlias, Value = "Prem Rugby" },
            new() { Source = LeagueNameFormSource.Canonical, Value = "English Prem Rugby" }
        };
        var league = new League
        {
            Name = "English Prem Rugby",
            Sport = "Rugby",
            AlternateName = "Premiership Rugby",
            UserAliases = "Prem Rugby, EPR",
            AliasSearchOrder = order,
            SearchEarlyStopMatchScoreOverride = 0
        };

        ApplyUpstreamMetadata(league, new League
        {
            Name = "English Prem Rugby",
            Sport = "Rugby",
            AlternateName = "Gallagher Premiership Rugby",
            UserAliases = "hub should never write this",
            AliasSearchOrder = new List<LeagueAliasOrderEntry>
            {
                new() { Source = LeagueNameFormSource.UpstreamAlias, Value = "hub order" }
            },
            SearchEarlyStopMatchScoreOverride = 95
        });

        league.AlternateName.Should().Be("Gallagher Premiership Rugby", "the alternate name is upstream-owned");
        league.UserAliases.Should().Be("Prem Rugby, EPR");
        league.AliasSearchOrder.Should().BeSameAs(order);
        league.SearchEarlyStopMatchScoreOverride.Should().Be(0, "zero disables early stopping and is not 'unset'");
    }
}
