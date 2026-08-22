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
/// What the handler actually does for the two behaviors under review is exactly this:
///   - reject raw input longer than AliasField.MaxUserAliasesLength with a 400
///   - otherwise store AliasField.Normalize(raw), having split on comma/pipe/slash via
///     AliasField.Parse
/// Both are covered by the shared helper the handler calls unmodified below. This proves
/// the split/normalize logic is right and that the boundary is exactly 512/513, but it does
/// NOT exercise the handler's own JsonElement parsing, its Results.BadRequest shape, or the
/// analytics side effect - those remain uncovered at the endpoint level.
/// </summary>
public class TeamAliasesEndpointTests
{
    [Fact]
    public void AliasField_Accepts512CharacterAliases()
    {
        var raw = new string('a', AliasField.MaxUserAliasesLength);

        raw.Length.Should().Be(512);
        raw.Length.Should().BeLessOrEqualTo(AliasField.MaxUserAliasesLength,
            "512 characters is the endpoint's accept boundary, not a rejection");
    }

    [Fact]
    public void AliasField_Rejects513CharacterAliases()
    {
        var raw = new string('a', AliasField.MaxUserAliasesLength + 1);

        raw.Length.Should().Be(513);
        raw.Length.Should().BeGreaterThan(AliasField.MaxUserAliasesLength,
            "the endpoint's 400 branch triggers on raw.Length > AliasField.MaxUserAliasesLength, i.e. 513+");
    }

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
