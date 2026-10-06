using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Source precedence end to end: an indexer release replaces an IPTV
/// recording, per part, only when DvrReplaceRecordingsWithIndexerReleases
/// is on.
/// </summary>
public class IptvSourcePrecedenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PushedReleaseOfAnExcludedQualityReplacesAnIptvRecordingOnlyWithTheSettingOn(bool replaceRecordings)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true);
        await SetReplaceRecordingsAsync(rig, replaceRecordings);
        await ExcludeQualityAsync(rig, "WEBDL-720p");
        await HoldAsync(rig, part: null, quality: "WEBDL-1080p", isIptvRecording: true);

        var outcome = await PushAsync(rig, "UFC.9999.2020.09.01.720p.WEB-DL.H264-Fixture");

        Assert.Equal(replaceRecordings, outcome.Grabbed);
        Assert.Equal(replaceRecordings ? 1 : 0, rig.Transport.ClientAdds);
        if (!replaceRecordings)
            Assert.NotEmpty(outcome.Rejections);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedDownloadReplacesAHigherQualityIptvRecordingOnlyWithTheSettingOn(bool replaceRecordings)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true);
        await SetReplaceRecordingsAsync(rig, replaceRecordings);
        await HoldAsync(rig, part: null, quality: "WEBDL-1080p", isIptvRecording: true);

        var history = await ImportAsync(rig, "UFC.9999.2020.09.01.720p.WEB-DL.H264-Fixture");

        var file = await rig.Db.EventFiles.AsNoTracking().SingleAsync(f => f.EventId == rig.Event.Id && f.Exists);
        if (replaceRecordings)
        {
            Assert.Equal(ImportDecision.Approved, history?.Decision);
            Assert.Equal("WEBDL-720p", file.Quality);
            Assert.False(file.IsIptvRecording);
        }
        else
        {
            Assert.Null(history);
            Assert.Equal("WEBDL-1080p", file.Quality);
            Assert.True(file.IsIptvRecording);
        }
    }

    [Theory]
    [InlineData("Prelims", true)]
    [InlineData("Main.Card", false)]
    public async Task PushedReleaseReplacesARecordedPartButNotAnIndexerFileOnAnotherPart(string partToken, bool grabbed)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        await SetReplaceRecordingsAsync(rig, true);
        await ExcludeQualityAsync(rig, "WEBDL-720p");
        // The indexer file goes in first so a lookup that ignored the part
        // would find it for the Prelims release.
        await HoldAsync(rig, part: "Main Card", quality: "WEBDL-1080p", isIptvRecording: false);
        await HoldAsync(rig, part: "Prelims", quality: "WEBDL-1080p", isIptvRecording: true);

        var outcome = await PushAsync(rig, $"UFC.9999.2020.09.01.{partToken}.720p.WEB-DL.H264-Fixture");

        Assert.Equal(grabbed, outcome.Grabbed);
        Assert.Equal(grabbed ? 1 : 0, rig.Transport.ClientAdds);
        if (grabbed)
            Assert.Equal("Prelims", (await rig.Db.DownloadQueue.SingleAsync()).Part);
    }

    private static async Task SetReplaceRecordingsAsync(PartIdentityIntegrationHarness rig, bool on)
    {
        var configService = rig.Services.GetRequiredService<ConfigService>();
        var config = await configService.GetConfigAsync();
        config.DvrReplaceRecordingsWithIndexerReleases = on;
        await configService.SaveConfigAsync(config);
    }

    private static async Task ExcludeQualityAsync(PartIdentityIntegrationHarness rig, string quality)
    {
        var profile = await rig.Db.QualityProfiles.SingleAsync(p => p.Id == rig.Event.QualityProfileId);
        profile.Items = profile.Items.Select(item => new QualityItem
        {
            Name = item.Name, Quality = item.Quality, Allowed = item.Name != quality,
        }).ToList();
        await rig.Db.SaveChangesAsync();
    }

    private static async Task<EventFile> HoldAsync(PartIdentityIntegrationHarness rig, string? part, string quality, bool isIptvRecording)
    {
        var root = await rig.Db.RootFolders.Select(r => r.Path).SingleAsync();
        var path = Path.Combine(root, $"UFC 9999 {part ?? "Event"} {quality}.mkv");
        await File.WriteAllBytesAsync(path, new byte[4096]);
        var file = new EventFile
        {
            EventId = rig.Event.Id, PartName = part, PartNumber = part switch { "Main Card" => 3, "Prelims" => 2, _ => null },
            FilePath = path, Size = 4096, Quality = quality, IsIptvRecording = isIptvRecording, Exists = true,
        };
        rig.Event.HasFile = true;
        rig.Db.EventFiles.Add(file);
        await rig.Db.SaveChangesAsync();
        return file;
    }

    private static async Task<PushedReleaseOutcome> PushAsync(PartIdentityIntegrationHarness rig, string title)
    {
        var release = rig.Release(title);
        release.Size = 2_000_000_000;
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();
        return await rig.Services.GetRequiredService<RssSyncService>()
            .ProcessPushedReleaseAsync(release, CancellationToken.None);
    }

    private static async Task<ImportHistory?> ImportAsync(PartIdentityIntegrationHarness rig, string title)
    {
        var folder = Path.Combine(Path.GetDirectoryName(await rig.Db.RootFolders.Select(r => r.Path).SingleAsync())!,
            "incoming", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, title + ".mkv");
        await File.WriteAllBytesAsync(source, new byte[4096]);
        var download = new DownloadQueueItem
        {
            EventId = rig.Event.Id, Title = title, DownloadId = Guid.NewGuid().ToString("N"),
            Quality = "WEBDL-720p", Protocol = "Usenet", Status = DownloadStatus.Completed,
        };
        rig.Db.DownloadQueue.Add(download);
        await rig.Db.SaveChangesAsync();
        return await rig.Services.GetRequiredService<FileImportService>().ImportDownloadAsync(download, source, PostImportMode.Copy);
    }
}
