using FluentAssertions;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Final-review finding on feat/league-alias-foundations: PUT /api/teams/{id}/aliases
/// (src/Endpoints/FollowedTeamsAndTeamsEndpoints.cs) gained a length cap and pipe/slash
/// alias splitting during Stage 1 with no test coverage.
///
/// The handler is an inline lambda passed straight to app.MapPut - unlike LeagueEndpoints,
/// nothing in Stage 1 extracted its alias-handling body into a named internal static method,
/// so there is no seam to invoke directly the way LeagueSearchPreferencesTests drives
/// LeagueEndpoints.ApplyLeagueSearchPreferences. Standing up the real endpoint would require
/// either a WebApplicationFactory-hosted app (touching startup wiring, config file I/O, and
/// the real SportarrApiClient/analytics side effects the handler fires) or extracting a new
/// helper - both out of scope for a test-only fix. Per instructions, that extraction was not
/// done here.
///
/// This file covers only the shared AliasField parsing/normalization the handler
/// delegates to for its split-and-store path (the pipe/slash bug fix and the
/// canonical comma-and-space storage form). It does NOT cover the handler's own
/// JsonElement parsing, its 400 response shape/length-cap rejection, or the
/// analytics side effect - the handler is an inline lambda with no extracted seam,
/// so those remain uncovered at the endpoint level. See the report for why no
/// extraction was made to close that gap.
/// </summary>
public class TeamAliasesEndpointTests
{
    [Theory]
    [InlineData("Man Utd | MUFC")]
    [InlineData("Man Utd / MUFC")]
    public void Normalize_SplitsPipeAndSlashSeparatedInputIntoMultipleAliases(string submitted)
    {
        // This is the live bug Stage 1 fixed: before routing through AliasField, the
        // endpoint stored "Man Utd | MUFC" as a single alias instead of two.
        var parsed = AliasField.Parse(submitted);

        parsed.Should().HaveCount(2);
        parsed.Should().BeEquivalentTo(new[] { "Man Utd", "MUFC" });
    }

    [Theory]
    [InlineData("Man Utd | MUFC")]
    [InlineData("Man Utd / MUFC")]
    [InlineData("Man Utd, MUFC")]
    public void Normalize_StoresTheCanonicalCommaAndSpaceForm(string submitted)
    {
        // The endpoint persists team.UserAliases = AliasField.Normalize(rawAliases)
        // regardless of which separator the user typed.
        var normalized = AliasField.Normalize(submitted);

        normalized.Should().Be("Man Utd, MUFC");
    }

    [Fact]
    public void Normalize_DedupesCaseInsensitively()
    {
        var normalized = AliasField.Normalize("Man Utd, MAN UTD, man utd");

        normalized.Should().Be("Man Utd");
    }
}
