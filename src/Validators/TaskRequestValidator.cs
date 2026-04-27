using FluentValidation;
using Sportarr.Api.Models;

namespace Sportarr.Api.Validators;

public class TaskRequestValidator : AbstractValidator<TaskRequest>
{
    public TaskRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Task name is required.")
            .MaximumLength(200);

        RuleFor(x => x.CommandName)
            .NotEmpty().WithMessage("CommandName is required.")
            .MaximumLength(200);

        RuleFor(x => x.Priority)
            .InclusiveBetween(0, 10).When(x => x.Priority.HasValue);
    }
}
