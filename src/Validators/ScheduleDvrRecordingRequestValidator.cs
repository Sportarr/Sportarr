using FluentValidation;
using Sportarr.Api.Models;

namespace Sportarr.Api.Validators;

public class ScheduleDvrRecordingRequestValidator : AbstractValidator<ScheduleDvrRecordingRequest>
{
    public ScheduleDvrRecordingRequestValidator()
    {
        RuleFor(x => x.ChannelId).GreaterThan(0);

        RuleFor(x => x.Title).MaximumLength(500);

        RuleFor(x => x.ScheduledStart)
            .LessThan(x => x.ScheduledEnd)
            .WithMessage("Recording start time must be before end time.");

        RuleFor(x => x.PrePadding)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(120)
            .WithMessage("Pre-padding must be between 0 and 120 minutes.");

        RuleFor(x => x.PostPadding)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(180)
            .WithMessage("Post-padding must be between 0 and 180 minutes.");
    }
}
