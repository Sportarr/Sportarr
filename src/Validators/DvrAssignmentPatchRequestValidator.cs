using FluentValidation;
using Sportarr.Api.Services;

namespace Sportarr.Api.Validators;

public sealed class DvrAssignmentPatchRequestValidator : AbstractValidator<DvrAssignmentPatchRequest>
{
    public DvrAssignmentPatchRequestValidator()
    {
        RuleFor(x => x.ExpectedChannelId).GreaterThan(0).When(x => x.ExpectedChannelId.HasValue);
        RuleFor(x => x.ChannelId).GreaterThan(0).When(x => x.ChannelId.HasValue);
        RuleFor(x => x.FallbackChannelIds)
            .Must(ids => ids == null || ids.All(id => id > 0))
            .WithMessage("Fallback channel IDs must be positive.");
        RuleFor(x => x.Quality).MaximumLength(100);
    }
}
