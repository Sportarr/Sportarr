using FluentAssertions;
using Sportarr.Api.Models;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The anime release-version tags were tested as plain substrings, and "av1"
/// contains "v1". The AV1 unwanted format was thrown out of every
/// sport-relevant sync, so AV1 releases carried no negative score and were
/// grabbed by profiles written to reject them.
/// </summary>
public class TrashRelevanceTests
{
    [Theory]
    [InlineData("av1")]
    [InlineData("AV1.json")]
    [InlineData("av1-unwanted")]
    public void The_av1_format_is_relevant_for_sports(string filename)
    {
        TrashCategories.IsRelevantForSports(filename).Should().BeTrue();
    }

    [Theory]
    [InlineData("anime-bd-tier-01-top-tier")]
    [InlineData("release-v2")]
    [InlineData("v1")]
    [InlineData("fansub-tier-01")]
    public void Anime_formats_stay_out(string filename)
    {
        TrashCategories.IsRelevantForSports(filename).Should().BeFalse();
    }
}
