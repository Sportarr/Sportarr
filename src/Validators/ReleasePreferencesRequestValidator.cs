using FluentValidation;
using Sportarr.Api.Endpoints;

namespace Sportarr.Api.Validators;

public sealed class ReleasePreferencesRequestValidator : AbstractValidator<ReleasePreferencesRequest>
{
    public ReleasePreferencesRequestValidator()
    {
        RuleFor(request => request.Mode).Must(mode => mode is "standard" or "recommended");
    }
}
