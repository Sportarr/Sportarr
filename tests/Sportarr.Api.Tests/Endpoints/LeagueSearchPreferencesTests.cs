using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Validators;
using System.Text.Json;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Stage 1 of league alias query expansion: leagues gain the same
/// user-alias field teams already have, plus a saved alias ordering and a
/// per-league early-stop match score override. This covers persistence and
/// validation only - query generation happens in a later stage.
/// </summary>
public class LeagueSearchPreferencesTests
{
    private static AddLeagueRequest Request(string? userAliases = null) => new()
    {
        Name = "English Prem Rugby",
        Sport = "Rugby",
        UserAliases = userAliases
    };

    // ---- Add mapping -------------------------------------------------

    [Fact]
    public void ToLeague_NormalizesCommaSeparatedAliases()
    {
        var league = Request("Gallagher Premiership ,  Prem Rugby ").ToLeague();

        league.UserAliases.Should().Be("Gallagher Premiership, Prem Rugby");
    }

    [Theory]
    [InlineData("Gallagher Premiership | Prem Rugby")]
    [InlineData("Gallagher Premiership / Prem Rugby")]
    public void ToLeague_NormalizesPipeAndSlashSeparators(string submitted)
    {
        var league = Request(submitted).ToLeague();

        league.UserAliases.Should().Be("Gallagher Premiership, Prem Rugby");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , , ")]
    public void ToLeague_ClearsAliasesThatAreOnlyWhitespaceOrSeparators(string submitted)
    {
        var league = Request(submitted).ToLeague();

        league.UserAliases.Should().BeNull();
    }

    [Fact]
    public void ToLeague_CopiesAliasOrderAndEarlyStopOverride()
    {
        var request = Request("Prem Rugby");
        request.AliasSearchOrder = new List<LeagueAliasOrderEntry>
        {
            new() { Source = LeagueNameFormSource.UserAlias, Value = "Prem Rugby" },
            new() { Source = LeagueNameFormSource.Canonical, Value = "English Prem Rugby" }
        };
        request.SearchEarlyStopMatchScoreOverride = 85;

        var league = request.ToLeague();

        league.AliasSearchOrder.Should().HaveCount(2);
        league.AliasSearchOrder![0].Source.Should().Be(LeagueNameFormSource.UserAlias);
        league.AliasSearchOrder[0].Value.Should().Be("Prem Rugby");
        league.SearchEarlyStopMatchScoreOverride.Should().Be(85);
    }

    // ---- Response mapping --------------------------------------------

    [Fact]
    public void FromLeague_ReturnsAllThreeSearchPreferences()
    {
        var league = new League
        {
            Name = "English Prem Rugby",
            Sport = "Rugby",
            UserAliases = "Gallagher Premiership, Prem Rugby",
            AliasSearchOrder = new List<LeagueAliasOrderEntry>
            {
                new() { Source = LeagueNameFormSource.UpstreamAlias, Value = "Gallagher Premiership" }
            },
            SearchEarlyStopMatchScoreOverride = 0
        };

        var response = LeagueResponse.FromLeague(league);

        response.UserAliases.Should().Be("Gallagher Premiership, Prem Rugby");
        response.AliasSearchOrder.Should().ContainSingle()
            .Which.Source.Should().Be(LeagueNameFormSource.UpstreamAlias);
        response.SearchEarlyStopMatchScoreOverride.Should().Be(0);
    }

    [Fact]
    public void FromLeague_LeavesNeverCustomizedPreferencesNull()
    {
        var response = LeagueResponse.FromLeague(new League { Name = "NFL", Sport = "American Football" });

        response.UserAliases.Should().BeNull();
        response.AliasSearchOrder.Should().BeNull("a null alias order means the user never customized it");
        response.SearchEarlyStopMatchScoreOverride.Should().BeNull("a null override inherits the global setting");
    }

    // ---- Alias order round-trip --------------------------------------

    [Fact]
    public void AliasOrderEntry_RoundTripsSourceAndValueThroughJson()
    {
        var entries = new List<LeagueAliasOrderEntry>
        {
            new() { Source = LeagueNameFormSource.BuiltIn, Value = "EPR" },
            new() { Source = LeagueNameFormSource.UserAlias, Value = "Prem Rugby" }
        };

        var json = JsonSerializer.Serialize(entries);
        var restored = JsonSerializer.Deserialize<List<LeagueAliasOrderEntry>>(json)!;

        json.Should().Contain("\"UserAlias\"", "the source is stored by name so the enum can be reordered safely");
        restored.Should().HaveCount(2);
        restored[0].Source.Should().Be(LeagueNameFormSource.BuiltIn);
        restored[0].Value.Should().Be("EPR");
        restored[1].Source.Should().Be(LeagueNameFormSource.UserAlias);
        restored[1].Value.Should().Be("Prem Rugby");
    }

    // ---- Validation ---------------------------------------------------

    private static readonly AddLeagueRequestValidator Validator = new();

    [Fact]
    public void Validator_Rejects513CharacterAliases_WithUserAliasesKey()
    {
        var request = Request(new string('a', AliasField.MaxUserAliasesLength + 1));

        var result = Validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("UserAliases");
    }

    [Fact]
    public void Validator_Accepts512CharacterAliases()
    {
        var result = Validator.Validate(Request(new string('a', AliasField.MaxUserAliasesLength)));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(100)]
    public void Validator_AcceptsNullZeroAndPositiveEarlyStopOverrides(int? override_)
    {
        var request = Request();
        request.SearchEarlyStopMatchScoreOverride = override_;

        Validator.Validate(request).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validator_RejectsEarlyStopOverridesOutsideTheScorerClamp(int override_)
    {
        var request = Request();
        request.SearchEarlyStopMatchScoreOverride = override_;

        var result = Validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be("SearchEarlyStopMatchScoreOverride");
    }

    [Fact]
    public void Validator_RejectsMoreThan64AliasOrderEntries()
    {
        var request = Request();
        request.AliasSearchOrder = Enumerable.Range(0, 65)
            .Select(i => new LeagueAliasOrderEntry { Source = LeagueNameFormSource.UserAlias, Value = $"alias {i}" })
            .ToList();

        Validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsEmptyAliasOrderValues()
    {
        var request = Request();
        request.AliasSearchOrder = new List<LeagueAliasOrderEntry>
        {
            new() { Source = LeagueNameFormSource.UserAlias, Value = "  " }
        };

        Validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsAliasOrderValuesLongerThan256Characters()
    {
        var request = Request();
        request.AliasSearchOrder = new List<LeagueAliasOrderEntry>
        {
            new() { Source = LeagueNameFormSource.UserAlias, Value = new string('a', 257) }
        };

        Validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsUndefinedAliasOrderSource()
    {
        var request = Request();
        request.AliasSearchOrder = new List<LeagueAliasOrderEntry>
        {
            new() { Source = (LeagueNameFormSource)99, Value = "Prem Rugby" }
        };

        Validator.Validate(request).IsValid.Should().BeFalse();
    }

    // ---- POST /api/leagues path ---------------------------------------

    [Fact]
    public async Task AddLeagueAsync_RejectsOverLengthAliases_BeforeTouchingTheDatabase()
    {
        // The add endpoint deserializes manually and delegates here, so the
        // validator has to run inside the service or the POST path would
        // accept aliases the typed PUT path rejects. Null dependencies prove
        // the rejection happens before any of them is used.
        var service = new LeagueAddService(null!, null!, null!, null!, NullLogger<LeagueAddService>.Instance);
        var request = Request(new string('a', AliasField.MaxUserAliasesLength + 1));

        var result = await service.AddLeagueAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Field.Should().Be("userAliases");
        result.ErrorMessage.Should().Be($"userAliases must be {AliasField.MaxUserAliasesLength} characters or fewer");
    }

    [Fact]
    public async Task AddLeagueAsync_ReportsEveryFailure_NotJustTheFirst()
    {
        var service = new LeagueAddService(null!, null!, null!, null!, NullLogger<LeagueAddService>.Instance);
        var request = Request(new string('a', AliasField.MaxUserAliasesLength + 1));
        request.SearchEarlyStopMatchScoreOverride = 101;

        var result = await service.AddLeagueAsync(request);

        result.Errors.Select(e => e.Field).Should().BeEquivalentTo(
            new[] { "userAliases", "searchEarlyStopMatchScoreOverride" },
            "one round trip should tell the user about everything that is wrong");
        result.Field.Should().Be(result.Errors[0].Field);
        result.ErrorMessage.Should().Be(result.Errors[0].Error);
    }

    [Fact]
    public async Task AddLeagueAsync_ReportsChildRuleFailuresAgainstABindableFieldName()
    {
        var service = new LeagueAddService(null!, null!, null!, null!, NullLogger<LeagueAddService>.Instance);
        var request = Request();
        request.AliasSearchOrder = new List<LeagueAliasOrderEntry>
        {
            new() { Source = LeagueNameFormSource.UserAlias, Value = "Prem Rugby" },
            new() { Source = LeagueNameFormSource.UserAlias, Value = "" }
        };

        var result = await service.AddLeagueAsync(request);

        result.StatusCode.Should().Be(400);
        result.Errors.Should().OnlyContain(e => e.Field == "aliasSearchOrder",
            "no UI can bind an error to 'aliasSearchOrder[1].Value'");
    }

    // ---- PUT /api/leagues/{id} search preference handling --------------
    //
    // ApplyLeagueSearchPreferences is the whole of the update endpoint's new
    // behavior, factored out of the JsonElement handler so it can be driven
    // directly. The handler around it only turns a non-empty error list into
    // a 400 body; everything asserted here is what actually runs in request.

    private static League ExistingLeague() => new()
    {
        Name = "English Prem Rugby",
        Sport = "Rugby",
        UserAliases = "Old Alias",
        SearchEarlyStopMatchScoreOverride = 50
    };

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Put_NormalizesSubmittedAliases()
    {
        var league = ExistingLeague();

        var errors = LeagueEndpoints.ApplyLeagueSearchPreferences(
            Body("""{"userAliases":"Gallagher Premiership | Prem Rugby / Prem Rugby"}"""), league);

        errors.Should().BeEmpty();
        league.UserAliases.Should().Be("Gallagher Premiership, Prem Rugby");
    }

    [Fact]
    public void Put_ClearsAliasesOnExplicitNull()
    {
        var league = ExistingLeague();

        LeagueEndpoints.ApplyLeagueSearchPreferences(Body("""{"userAliases":null}"""), league).Should().BeEmpty();

        league.UserAliases.Should().BeNull();
    }

    [Fact]
    public void Put_LeavesAbsentFieldsUntouched()
    {
        var league = ExistingLeague();

        LeagueEndpoints.ApplyLeagueSearchPreferences(Body("""{"monitored":true}"""), league).Should().BeEmpty();

        league.UserAliases.Should().Be("Old Alias");
        league.SearchEarlyStopMatchScoreOverride.Should().Be(50);
    }

    [Fact]
    public void Put_RejectsOverLengthAliases_WithTheSameBodyTheTeamEndpointReturns()
    {
        var league = ExistingLeague();
        var tooLong = new string('a', AliasField.MaxUserAliasesLength + 1);

        var errors = LeagueEndpoints.ApplyLeagueSearchPreferences(
            Body($$"""{"userAliases":"{{tooLong}}"}"""), league);

        errors.Should().ContainSingle();
        errors[0].Field.Should().Be("userAliases");
        errors[0].Error.Should().Be($"userAliases must be {AliasField.MaxUserAliasesLength} characters or fewer");
        league.UserAliases.Should().Be("Old Alias", "a rejected update must not have been applied");
    }

    [Fact]
    public void Put_RejectsNonStringAliases()
    {
        var errors = LeagueEndpoints.ApplyLeagueSearchPreferences(Body("""{"userAliases":7}"""), ExistingLeague());

        errors.Should().ContainSingle().Which.Field.Should().Be("userAliases");
    }

    [Fact]
    public void Put_StoresSubmittedAliasOrder()
    {
        var league = ExistingLeague();

        var errors = LeagueEndpoints.ApplyLeagueSearchPreferences(
            Body("""{"aliasSearchOrder":[{"source":"UserAlias","value":"Prem Rugby"}]}"""), league);

        errors.Should().BeEmpty();
        league.AliasSearchOrder.Should().ContainSingle()
            .Which.Should().Be(new LeagueAliasOrderEntry { Source = LeagueNameFormSource.UserAlias, Value = "Prem Rugby" });
    }

    [Fact]
    public void Put_TreatsNullAliasOrderAsNeverCustomized_AndAnEmptyArrayAsARealOrder()
    {
        var league = ExistingLeague();
        league.AliasSearchOrder = new List<LeagueAliasOrderEntry>
        {
            new() { Source = LeagueNameFormSource.Canonical, Value = "English Prem Rugby" }
        };

        LeagueEndpoints.ApplyLeagueSearchPreferences(Body("""{"aliasSearchOrder":null}"""), league).Should().BeEmpty();
        league.AliasSearchOrder.Should().BeNull("null restores the default ordering");

        LeagueEndpoints.ApplyLeagueSearchPreferences(Body("""{"aliasSearchOrder":[]}"""), league).Should().BeEmpty();
        league.AliasSearchOrder.Should().NotBeNull("an empty array is an all-forms-removed order, not an absent one");
        league.AliasSearchOrder.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"aliasSearchOrder":"not an array"}""")]
    [InlineData("""{"aliasSearchOrder":[{"source":"NotASource","value":"Prem Rugby"}]}""")]
    public void Put_RejectsUnparseableAliasOrder(string json)
    {
        var errors = LeagueEndpoints.ApplyLeagueSearchPreferences(Body(json), ExistingLeague());

        errors.Should().ContainSingle().Which.Field.Should().Be("aliasSearchOrder");
    }

    [Fact]
    public void Put_AppliesTheSameAliasOrderRulesTheAddPathApplies()
    {
        var league = ExistingLeague();
        var tooMany = string.Join(",", Enumerable.Range(0, AddLeagueRequestValidator.MaxAliasOrderEntries + 1)
            .Select(i => $$"""{"source":"UserAlias","value":"alias {{i}}"}"""));

        var tooManyErrors = LeagueEndpoints.ApplyLeagueSearchPreferences(
            Body($$"""{"aliasSearchOrder":[{{tooMany}}]}"""), league);
        var emptyValueErrors = LeagueEndpoints.ApplyLeagueSearchPreferences(
            Body("""{"aliasSearchOrder":[{"source":"UserAlias","value":"  "}]}"""), league);
        var longValueErrors = LeagueEndpoints.ApplyLeagueSearchPreferences(
            Body($$"""{"aliasSearchOrder":[{"source":"UserAlias","value":"{{new string('a', AddLeagueRequestValidator.MaxAliasOrderValueLength + 1)}}"}]}"""), league);

        tooManyErrors.Should().ContainSingle().Which.Field.Should().Be("aliasSearchOrder");
        emptyValueErrors.Should().NotBeEmpty().And.OnlyContain(e => e.Field == "aliasSearchOrder");
        longValueErrors.Should().NotBeEmpty().And.OnlyContain(e => e.Field == "aliasSearchOrder");
        league.AliasSearchOrder.Should().BeNull("none of the rejected orders may have been applied");
    }

    [Theory]
    [InlineData("null", null)]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    public void Put_StoresValidEarlyStopOverrides(string json, int? expected)
    {
        var league = ExistingLeague();

        var errors = LeagueEndpoints.ApplyLeagueSearchPreferences(
            Body($$"""{"searchEarlyStopMatchScoreOverride":{{json}}}"""), league);

        errors.Should().BeEmpty();
        league.SearchEarlyStopMatchScoreOverride.Should().Be(expected);
    }

    [Theory]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("\"high\"")]
    public void Put_RejectsEarlyStopOverridesOutsideTheScorerClamp(string json)
    {
        var league = ExistingLeague();

        var errors = LeagueEndpoints.ApplyLeagueSearchPreferences(
            Body($$"""{"searchEarlyStopMatchScoreOverride":{{json}}}"""), league);

        errors.Should().ContainSingle().Which.Field.Should().Be("searchEarlyStopMatchScoreOverride");
        league.SearchEarlyStopMatchScoreOverride.Should().Be(50, "a rejected update must not have been applied");
    }

    [Fact]
    public void Put_AppliesNothingWhenAnySubmittedFieldFails()
    {
        var league = ExistingLeague();

        var errors = LeagueEndpoints.ApplyLeagueSearchPreferences(
            Body($$"""
            {
              "userAliases": "Prem Rugby",
              "aliasSearchOrder": [{"source":"UserAlias","value":"Prem Rugby"}],
              "searchEarlyStopMatchScoreOverride": 101
            }
            """), league);

        errors.Should().ContainSingle().Which.Field.Should().Be("searchEarlyStopMatchScoreOverride");
        league.UserAliases.Should().Be("Old Alias");
        league.AliasSearchOrder.Should().BeNull();
        league.SearchEarlyStopMatchScoreOverride.Should().Be(50);
    }
}
