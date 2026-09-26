using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

public class TrashQualityProfileOrderTests
{
    [Fact]
    public async Task ImportedProfileUsesBestFirstOrderForGrabsAndDisplay()
    {
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        using var client = new HttpClient(new ProfileTransport());
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("TrashGuides")).Returns(client);
        using var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        var service = new TrashGuideSyncService(db, factory.Object,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);

        var result = await service.CreateProfileFromTemplateAsync("sample-profile");

        result.success.Should().BeTrue(result.error);
        var profile = await db.QualityProfiles.SingleAsync();
        profile.Items.Select(item => item.Name).Should().Equal("WEB 1080p", "SDTV", "Unknown");
        QualityProfileRanker.Compare(profile, "WEBDL-1080p", "SDTV").Should().BePositive();
        profile.CutoffQuality.Should().Be(2);
    }

    [Fact]
    public async Task ImportedFirstGroupCutoffResolvesToThatGroup()
    {
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var profileJson = """{"trash_id":"sample-profile","name":"Sample","cutoff":"WEB 480p","items":[{"name":"WEB 480p","allowed":true,"items":["WEBDL-480p","WEBRip-480p"]},{"name":"WEB 1080p","allowed":true,"items":["WEBDL-1080p","WEBRip-1080p"]}]}""";
        using var client = new HttpClient(new ProfileTransport(profileJson));
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("TrashGuides")).Returns(client);
        using var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        var service = new TrashGuideSyncService(db, factory.Object,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);

        var result = await service.CreateProfileFromTemplateAsync("sample-profile");

        result.success.Should().BeTrue(result.error);
        var profile = await db.QualityProfiles.SingleAsync();
        profile.Items.Select(item => item.Name).Should().Equal("WEB 1080p", "WEB 480p");
        profile.CutoffQuality.Should().Be(0);
        QualityProfileRanker.GetCutoffRank(profile, 0)
            .Should().Be(QualityProfileRanker.GetRank(profile, "WEBDL-480p"));
    }

    private sealed class ProfileTransport(string? profileJson = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.RequestUri?.Host switch
            {
                "api.github.com" => """[{"name":"profile.json"}]""",
                "raw.githubusercontent.com" => profileJson ?? """{"trash_id":"sample-profile","name":"Sample","cutoff":"WEB 1080p","items":[{"name":"Unknown","allowed":false},{"name":"SDTV","allowed":true},{"name":"WEB 1080p","allowed":true,"items":["WEBDL-1080p","WEBRip-1080p"]}]}""",
                _ => throw new InvalidOperationException("Unexpected profile source")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
}
