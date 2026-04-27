using FluentValidation;
using Sportarr.Api.Models;

namespace Sportarr.Api.Validators;

public class MapChannelToLeaguesRequestValidator : AbstractValidator<MapChannelToLeaguesRequest>
{
    public MapChannelToLeaguesRequestValidator()
    {
        RuleFor(x => x.ChannelId).GreaterThan(0);

        RuleFor(x => x.LeagueIds)
            .NotNull()
            .Must(ids => ids == null || ids.All(id => id > 0))
            .WithMessage("All league IDs must be positive integers.");

        RuleFor(x => x.PreferredLeagueId)
            .GreaterThan(0)
            .When(x => x.PreferredLeagueId.HasValue);
    }
}
