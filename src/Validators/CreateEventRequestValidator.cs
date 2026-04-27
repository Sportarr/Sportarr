using FluentValidation;
using Sportarr.Api.Models;

namespace Sportarr.Api.Validators;

public class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Event title is required.")
            .MaximumLength(500);

        RuleFor(x => x.Sport)
            .NotEmpty().WithMessage("Sport is required.")
            .MaximumLength(100);

        RuleFor(x => x.LeagueId)
            .GreaterThan(0).WithMessage("LeagueId must reference a real league.");

        RuleFor(x => x.ExternalId)
            .MaximumLength(64);

        RuleFor(x => x.Venue).MaximumLength(500);
        RuleFor(x => x.Location).MaximumLength(500);
        RuleFor(x => x.Broadcast).MaximumLength(500);
    }
}
