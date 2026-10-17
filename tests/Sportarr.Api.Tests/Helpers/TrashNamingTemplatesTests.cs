using FluentAssertions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Helpers;

public class TrashNamingTemplatesTests
{
    [Theory]
    [InlineData("date-based")]
    [InlineData("sports-league")]
    [InlineData("minimal")]
    public void UserFacingPresets_IncludeEpisodeNumbersAndEventId(string presetName)
    {
        var format = TrashNamingTemplates.GetFileNamingPreset(presetName, enableMultiPartEpisodes: false);

        format.Should().Contain("{Season}{Episode}");
        format.Should().Contain("{Sportarr Id}");
    }
}
