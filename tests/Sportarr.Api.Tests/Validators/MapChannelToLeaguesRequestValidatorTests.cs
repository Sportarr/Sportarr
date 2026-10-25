using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Tests.Validators;

public class MapChannelToLeaguesRequestValidatorTests
{
    private readonly MapChannelToLeaguesRequestValidator _validator = new();

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(-1, false)]
    [InlineData(101, false)]
    public void AcceptsAnOptionalConfidenceFromZeroToOneHundred(int? confidence, bool expected)
    {
        var request = new MapChannelToLeaguesRequest
        {
            ChannelId = 1,
            LeagueIds = new List<int> { 1 },
            Confidence = confidence,
        };

        _validator.Validate(request).IsValid.Should().Be(expected);
    }
}
