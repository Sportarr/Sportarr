using FluentValidation;
using Sportarr.Api.Models;

namespace Sportarr.Api.Validators;

public class AddEpgSourceRequestValidator : AbstractValidator<AddEpgSourceRequest>
{
    public AddEpgSourceRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("EPG source name is required.")
            .MaximumLength(100);

        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("URL is required.")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("URL must be a valid http(s) URL.");
    }
}
