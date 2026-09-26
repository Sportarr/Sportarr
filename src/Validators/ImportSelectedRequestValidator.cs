using FluentValidation;
using Sportarr.Api.Services;

namespace Sportarr.Api.Validators;

public sealed class ImportSelectedRequestValidator : AbstractValidator<ImportSelectedRequest>
{
    public ImportSelectedRequestValidator()
    {
        RuleFor(request => request.RelativePath)
            .NotEmpty()
            .MaximumLength(4096);
    }
}
