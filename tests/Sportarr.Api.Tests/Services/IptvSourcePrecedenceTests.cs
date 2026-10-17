using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Helpers;
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
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            multipart: false, relational: true, mediaRoot: Environment.GetEnvironmentVariable("SPORTARR_E2E_MEDIA_ROOT"));
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

    [Fact]
    public async Task SourcePrecedenceDoesNotLiftWrongEventOrSizeRejections()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true);
        await SetReplaceRecordingsAsync(rig, true);
        await ExcludeQualityAsync(rig, "WEBDL-720p");
        await HoldAsync(rig, part: null, quality: "WEBDL-1080p", isIptvRecording: true);

        var wrongEvent = await PushAsync(rig, "UFC.8888.2020.09.01.720p.WEB-DL.H264-Fixture");
        Assert.False(wrongEvent.Grabbed);

        var tooSmall = rig.Release("UFC.9999.2020.09.01.720p.WEB-DL.H264-Fixture");
        tooSmall.Size = 1024;
        tooSmall.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();
        var sizeOutcome = await rig.Services.GetRequiredService<RssSyncService>()
            .ProcessPushedReleaseAsync(tooSmall, CancellationToken.None);

        Assert.False(sizeOutcome.Grabbed);
        Assert.Contains(sizeOutcome.Rejections, reason => reason.Contains("Size", StringComparison.OrdinalIgnoreCase));

        rig.Db.Blocklist.Add(new BlocklistItem
        {
            Title = "UFC.9999.2020.09.01.720p.WEB-DL.H264-Fixture",
            Indexer = "Part fixture", Protocol = "Usenet", Reason = BlocklistReason.ManualBlock
        });
        await rig.Db.SaveChangesAsync();
        var blocked = await PushAsync(rig, "UFC.9999.2020.09.01.720p.WEB-DL.H264-Fixture");
        Assert.False(blocked.Grabbed);
        Assert.Equal(0, rig.Transport.ClientAdds);
    }

    [Fact]
    public async Task WholeEventIndexerFilePreventsProfileLiftForRecordedPart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        await SetReplaceRecordingsAsync(rig, true);
        await ExcludeQualityAsync(rig, "WEBDL-720p");
        await HoldAsync(rig, part: null, quality: "WEBDL-1080p", isIptvRecording: false);
        await HoldAsync(rig, part: "Prelims", quality: "WEBDL-1080p", isIptvRecording: true);
        var title = "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-Fixture";

        var pushed = await PushAsync(rig, title);
        var release = rig.Release(title);
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();
        var automatic = await rig.AutomaticAsync(release);

        Assert.False(pushed.Grabbed);
        Assert.False(automatic.Success);
        Assert.Equal(0, rig.Transport.ClientAdds);
    }

    [Fact]
    public void RecordingSourceSurvivesRescanButNotDownloadClientReplacement()
    {
        Assert.True(SourcePrecedence.PreserveRecordingOnRescan(false, true, false));
        Assert.False(SourcePrecedence.PreserveRecordingOnRescan(false, true, true));
        Assert.True(SourcePrecedence.PreserveRecordingOnRescan(true, false, true));
    }

    [Fact]
    public void LateRecordingChoiceRequiresTheSamePart()
    {
        var files = new[]
        {
            new EventFile { FilePath = "/fixture/whole.mkv", Exists = true, PartName = null, IsIptvRecording = false },
            new EventFile { FilePath = "/fixture/prelims.ts", Exists = true, PartName = "Prelims", IsIptvRecording = true }
        };

        Assert.False(SourcePrecedence.RecordingConflictsWithIndexerFile(
            new[] { files[0] }, "Prelims"));
        Assert.True(SourcePrecedence.RecordingConflictsWithIndexerFile(files, null));
        Assert.True(SourcePrecedence.RecordingConflictsWithIndexerFile(
            new[] { files[0] }, "Full Event"));
        Assert.False(SourcePrecedence.RecordingConflictsWithIndexerFile(
            new[] { new EventFile { FilePath = "/fixture/prelims.ts", Exists = true, PartName = "Prelims", IsIptvRecording = true } }, "Prelims"));
    }

    [Fact]
    public void LegacyRecordingMarkersDoNotCountAsIndexerFiles()
    {
        var files = new[]
        {
            new EventFile { FilePath = "/fixture/old.720p.DVR.ts", Quality = "HDTV-720p", Exists = true, Source = "IPTV" },
            new EventFile { FilePath = "/fixture/other.720p.DVR.ts", Quality = "HDTV-720p", Exists = true }
        };

        Assert.False(SourcePrecedence.RecordingConflictsWithIndexerFile(files, null));
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

    [Theory]
    [InlineData("Prelims", true)]
    [InlineData("Main.Card", false)]
    public async Task SearchWithoutAPartLiftsTheProfileOnlyForTheRecordedPart(string partToken, bool grabbed)
    {
        // The backlog searches multi-part events with no part. Main Card
        // holds nothing, so an excluded quality must not be grabbed for it.
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        await SetReplaceRecordingsAsync(rig, true);
        await ExcludeQualityAsync(rig, "WEBDL-720p");
        await HoldAsync(rig, part: "Prelims", quality: "WEBDL-1080p", isIptvRecording: true);
        var release = rig.Release($"UFC.9999.2020.09.01.{partToken}.720p.WEB-DL.H264-Fixture");
        release.Size = 2_000_000_000;
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();

        var result = await rig.AutomaticAsync(release);

        Assert.True(grabbed == result.Success, result.Message);
        Assert.Equal(grabbed ? 1 : 0, rig.Transport.ClientAdds);
    }

    [Theory]
    [InlineData("Prelims", null)]
    [InlineData("Preliminary Card", null)]
    [InlineData("Preliminary Card", "Prelims")]
    public async Task IncompleteEventSearchReplacesItsRecordedPartWithoutComparingThePartToItself(
        string storedPartName, string? requestedPart)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        await SetReplaceRecordingsAsync(rig, true);
        await ExcludeQualityAsync(rig, "WEBDL-720p");
        var held = await HoldAsync(rig, part: "Prelims", quality: "WEBDL-1080p", isIptvRecording: true);
        held.PartName = storedPartName;
        rig.Event.HasFile = false;
        await rig.Db.SaveChangesAsync();

        var release = rig.Release("UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-Fixture");
        release.Size = 2_000_000_000;
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();

        var result = await rig.AutomaticAsync(release, requestedPart);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, rig.Transport.ClientAdds);
        Assert.Equal("Prelims", (await rig.Db.DownloadQueue.SingleAsync()).Part);
    }

    [Fact]
    public async Task IncompleteEventSearchChecksEverySurvivingPartBeforeAddingARelease()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(relational: true);
        await HoldAsync(rig, part: "Post Show", quality: "WEBDL-720p", isIptvRecording: false);
        await HoldAsync(rig, part: "Main Card", quality: "WEBDL-1080p", isIptvRecording: false);
        rig.Event.HasFile = false;
        await rig.Db.SaveChangesAsync();

        var release = rig.Release("UFC.9999.2020.09.01.Prelims.720p.WEB-DL.H264-Fixture");
        release.Size = 2_000_000_000;
        release.IndexerId = await rig.Db.Indexers.Select(x => x.Id).SingleAsync();

        var result = await rig.AutomaticAsync(release);

        Assert.False(result.Success);
        Assert.Equal(0, rig.Transport.ClientAdds);
    }

    [Theory]
    [InlineData(true, true, false, 1)]
    [InlineData(false, true, false, 0)]
    [InlineData(false, false, true, 0)]
    public async Task SonarrCutoffListCountsRecordingsWithoutChangingDisabledBehaviour(
        bool replaceRecordings, bool isIptvRecording, bool excludeHeldQuality, int expected)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false, relational: true);
        await SetReplaceRecordingsAsync(rig, replaceRecordings);
        var profile = await rig.Db.QualityProfiles.SingleAsync(p => p.Id == rig.Event.QualityProfileId);
        profile.UpgradesAllowed = true;
        profile.CutoffQuality = 15;
        await rig.Db.SaveChangesAsync();
        if (excludeHeldQuality)
            await ExcludeQualityAsync(rig, "WEBDL-2160p");
        // WEBDL-2160p sits above the WEBDL-1080p cutoff when it is allowed.
        await HoldAsync(rig, part: null, quality: "WEBDL-2160p", isIptvRecording: isIptvRecording);

        var body = await rig.Client.GetFromJsonAsync<JsonElement>("/api/v3/wanted/cutoff");

        Assert.Equal(expected, body.GetProperty("totalRecords").GetInt32());
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
