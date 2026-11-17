using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A search the user starts still picks the release itself. It must not
/// pick a release that the importer will refuse as a replacement.
/// </summary>
public class UserSearchUpgradeGuardTests
{
    private const string HeldTitle = "UFC.9999.2020.09.01.PROPER.1080p.WEB-DL.H264-Held";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchDoesNotGrabAnOlderRevisionOfTheHeldFile(bool manual)
    {
        await using var rig = await CreateRigWithHeldFileAsync();
        const string plain = "UFC.9999.2020.09.01.1080p.WEB-DL.H264-Plain";

        // The importer refuses this pair. The held file is a proper.
        var import = ImportUpgradeRule.Evaluate("WEBDL-1080p", -100, HeldTitle,
            "WEBDL-1080p", 0, plain, "preferAndUpgrade", await ProfileAsync(rig));
        Assert.False(import.IsUpgrade);
        Assert.Equal(ImportUpgradeRule.RevisionRejection, import.Rejection);

        var release = rig.Release(plain, suffix: "plain");
        release.Quality = "WEBDL-1080p";
        release.Size = 4_000_000_000;

        var outcome = await rig.AutomaticAsync(release, manual: manual);

        Assert.False(outcome.Success, outcome.Message);
        Assert.Contains("an older revision of the existing file", outcome.Message);
        Assert.Equal(0, rig.Transport.ClientAdds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchDoesNotGrabALowerQualityThanTheHeldFile(bool manual)
    {
        await using var rig = await CreateRigWithHeldFileAsync();
        const string lower = "UFC.9999.2020.09.01.PROPER.720p.WEB-DL.H264-Lower";

        var import = ImportUpgradeRule.Evaluate("WEBDL-1080p", -100, HeldTitle,
            "WEBDL-720p", 0, lower, "preferAndUpgrade", await ProfileAsync(rig));
        Assert.False(import.IsUpgrade);

        var release = rig.Release(lower, suffix: "lower");
        release.Size = 2_000_000_000;

        var outcome = await rig.AutomaticAsync(release, manual: manual);

        Assert.False(outcome.Success, outcome.Message);
        Assert.StartsWith("Existing file (WEBDL-1080p) is already good enough.", outcome.Message);
        Assert.Equal(0, rig.Transport.ClientAdds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchStillGrabsANewerRevisionOfTheHeldFile(bool manual)
    {
        await using var rig = await CreateRigWithHeldFileAsync();
        var release = rig.Release("UFC.9999.2020.09.01.REPACK.1080p.WEB-DL.H264-Newer", suffix: "newer");
        release.Quality = "WEBDL-1080p";
        release.Size = 4_000_000_000;

        var outcome = await rig.AutomaticAsync(release, manual: manual);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(1, rig.Transport.ClientAdds);
    }

    /// <summary>
    /// Holds a 1080p proper with a low custom format score. A plain release
    /// with a better score then looks like a format upgrade at the same quality.
    /// </summary>
    private static async Task<PartIdentityIntegrationHarness> CreateRigWithHeldFileAsync()
    {
        var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        rig.Event.HasFile = true;
        rig.Event.FilePath = "/data/e2e-lib/UFC 9999.mkv";
        rig.Event.Quality = "WEBDL-1080p";
        rig.Db.EventFiles.Add(new EventFile
        {
            EventId = rig.Event.Id,
            FilePath = "/data/e2e-lib/UFC 9999.mkv",
            Size = 4_000_000_000,
            Quality = "WEBDL-1080p",
            CustomFormatScore = -100,
            OriginalTitle = HeldTitle,
            ReleaseTitle = HeldTitle,
            Exists = true
        });
        await rig.Db.SaveChangesAsync();
        return rig;
    }

    private static Task<QualityProfile> ProfileAsync(PartIdentityIntegrationHarness rig) =>
        rig.Db.QualityProfiles.SingleAsync(p => p.Id == rig.Event.QualityProfileId);
}
