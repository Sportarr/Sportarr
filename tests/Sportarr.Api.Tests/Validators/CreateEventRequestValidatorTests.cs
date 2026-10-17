using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Tests.Validators;

public class CreateEventRequestValidatorTests
{
    private readonly CreateEventRequestValidator _validator = new();

    [Theory]
    [InlineData(null, true)]
    [InlineData(2026, true)]
    [InlineData(-1, false)]
    public void AcceptsOnlyNonnegativeOptionalSeasonNumber(int? season, bool expected)
    {
        var request = new CreateEventRequest
        {
            Title = "Race", Sport = "Motorsport", EventDate = DateTime.UtcNow,
            SeasonNumber = season
        };

        _validator.Validate(request).IsValid.Should().Be(expected);
    }
}
