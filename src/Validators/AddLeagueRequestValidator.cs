using FluentValidation;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Validators;

/// <summary>
/// One field-specific rejection, shaped to serialize as the
/// { field, error } body the team alias endpoint already returns. Field is
/// the camelCase name of a top-level request field the UI can bind to -
/// never an indexed child path like "aliasSearchOrder[0].Value".
/// </summary>
/// <param name="Field">camelCase top-level request field name.</param>
/// <param name="Error">Human-readable message for that field.</param>
public record LeagueFieldError(string Field, string Error);

/// <summary>
/// Turns FluentValidation results into the { field, error } shape both
/// league write paths return, so the POST service and the JsonElement PUT
/// handler cannot drift in how they report the same validator's failures.
/// </summary>
public static class LeagueValidationErrors
{
    /// <summary>
    /// Every failure, in validator order, with child-rule property paths
    /// collapsed onto the top-level field they belong to. FluentValidation
    /// reports a RuleForEach child failure as "AliasSearchOrder[0].Value";
    /// no UI can bind an error to that, so it becomes "aliasSearchOrder".
    /// </summary>
    public static List<LeagueFieldError> Map(FluentValidation.Results.ValidationResult result) =>
        result.Errors
            .Select(failure => new LeagueFieldError(ToRootFieldName(failure.PropertyName), failure.ErrorMessage))
            .ToList();

    private static string ToRootFieldName(string propertyName)
    {
        var cut = propertyName.IndexOfAny(['[', '.']);
        var root = cut < 0 ? propertyName : propertyName[..cut];
        return root.Length == 0 ? root : char.ToLowerInvariant(root[0]) + root[1..];
    }
}

/// <summary>
/// Validates the league search preferences carried on AddLeagueRequest.
/// Invoked explicitly by LeagueAddService (POST /api/leagues deserializes
/// the body by hand, so no endpoint filter runs) and mirrored by the
/// JsonElement PUT /api/leagues/{id} handler.
/// </summary>
public class AddLeagueRequestValidator : AbstractValidator<AddLeagueRequest>
{
    /// <summary>Most name forms a league can hold; well past any real league's alias count.</summary>
    public const int MaxAliasOrderEntries = 64;

    /// <summary>Longest single name form in a saved alias order.</summary>
    public const int MaxAliasOrderValueLength = 256;

    /// <summary>Match score bounds, matching ReleaseMatchScorer's clamp.</summary>
    public const int MinEarlyStopMatchScore = 0;
    public const int MaxEarlyStopMatchScore = 100;

    public AddLeagueRequestValidator()
    {
        RuleFor(request => request.UserAliases)
            .MaximumLength(AliasField.MaxUserAliasesLength)
            // Same wording the team alias endpoint returns - the two paths are
            // the same feature and their error text must not diverge.
            .WithMessage($"userAliases must be {AliasField.MaxUserAliasesLength} characters or fewer")
            .When(request => request.UserAliases is not null);

        RuleFor(request => request.AliasSearchOrder!)
            .Must(order => order.Count <= MaxAliasOrderEntries)
            .WithMessage($"aliasSearchOrder must hold at most {MaxAliasOrderEntries} entries")
            .When(request => request.AliasSearchOrder is not null);

        RuleForEach(request => request.AliasSearchOrder!)
            .ChildRules(entry =>
            {
                entry.RuleFor(e => e.Source).IsInEnum();
                entry.RuleFor(e => e.Value)
                    .NotEmpty()
                    .MaximumLength(MaxAliasOrderValueLength);
            })
            .When(request => request.AliasSearchOrder is not null);

        RuleFor(request => request.SearchEarlyStopMatchScoreOverride!.Value)
            .InclusiveBetween(MinEarlyStopMatchScore, MaxEarlyStopMatchScore)
            .OverridePropertyName(nameof(AddLeagueRequest.SearchEarlyStopMatchScoreOverride))
            .When(request => request.SearchEarlyStopMatchScoreOverride is not null);
    }
}
