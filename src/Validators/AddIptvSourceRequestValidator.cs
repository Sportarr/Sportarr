using FluentValidation;
using Sportarr.Api.Models;

namespace Sportarr.Api.Validators;

public class AddIptvSourceRequestValidator : AbstractValidator<AddIptvSourceRequest>
{
    public AddIptvSourceRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Source name is required.")
            .MaximumLength(100);

        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("URL is required.")
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("URL must be a valid http(s) URL.");

        RuleFor(x => x.Type).IsInEnum();

        RuleFor(x => x.MaxStreams)
            .GreaterThan(0)
            .LessThanOrEqualTo(100);

        RuleFor(x => x.Username).MaximumLength(256);
        RuleFor(x => x.Password).MaximumLength(512);
        RuleFor(x => x.UserAgent).MaximumLength(512);
    }
}
