using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PartIdentityPendingContractV2Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateDvrChoiceCanReplaceAnIndexerFileRegardlessOfQuality(bool legacyMultipart)
    {
        const string mediaRoot = "/data/e2e-lib/late-dvr-choice";
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: legacyMultipart, mediaRoot: mediaRoot);
        var directory = NewDirectory(mediaRoot);
        try
        {
            var heldPath = Path.Combine(directory, "held.1080p.WEB-DL.mkv");
            var recordingPath = Path.Combine(directory, "recording.720p.DVR.mkv");
            await File.WriteAllBytesAsync(heldPath, Enumerable.Repeat((byte)'h', 4096).ToArray());
            await File.WriteAllBytesAsync(recordingPath, Enumerable.Repeat((byte)'r', 4096).ToArray());
            rig.Db.EventFiles.Add(new EventFile
            {
                EventId = rig.Event.Id, FilePath = heldPath, Quality = "WEBDL-1080p", Exists = true,
                PartName = legacyMultipart ? "Prelims" : null, PartNumber = null
            });
            var source = new IptvSource { Name = "Fixture", Url = "https://iptv.invalid/list" };
            var channel = new IptvChannel { Source = source, Name = "Fixture", StreamUrl = "https://iptv.invalid/stream" };
            var recording = new DvrRecording
            {
                EventId = rig.Event.Id, Channel = channel, Title = rig.Event.Title,
                Status = DvrRecordingStatus.Completed, OutputPath = recordingPath,
                PartName = legacyMultipart ? "Prelims" : null,
                Quality = "HDTV-720p", QualityScore = 10,
                ScheduledStart = DateTime.UtcNow.AddHours(-2), ScheduledEnd = DateTime.UtcNow.AddHours(-1)
            };
            rig.Db.DvrRecordings.Add(recording);
            await rig.Db.SaveChangesAsync();
            var pending = new PendingImport
            {
                DownloadId = $"dvr-{recording.Id}", Title = Path.GetFileName(recordingPath),
                FilePath = recordingPath, Size = 4096, Quality = recording.Quality,
                SuggestedEventId = rig.Event.Id, SuggestedPart = recording.PartName, Protocol = "DVR"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            pending.SuggestedPart = legacyMultipart ? "Main Card" : "Prelims";
            await rig.Db.SaveChangesAsync();
            using var wrongPart = await app.GetTestClient().PostAsync($"/api/pending-imports/{pending.Id}/accept", null);
            wrongPart.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            File.Exists(heldPath).Should().BeTrue();
            pending.SuggestedPart = recording.PartName;
            var held = await rig.Db.EventFiles.SingleAsync();
            held.IsIptvRecording = true;
            await rig.Db.SaveChangesAsync();
            using var staleChoice = await app.GetTestClient().PostAsync($"/api/pending-imports/{pending.Id}/accept", null);
            staleChoice.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            File.Exists(heldPath).Should().BeTrue();
            held.IsIptvRecording = false;
            await rig.Db.SaveChangesAsync();
            using var response = await app.GetTestClient().PostAsync($"/api/pending-imports/{pending.Id}/accept", null);

            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var linked = await rig.Db.EventFiles.SingleAsync();
            linked.IsIptvRecording.Should().BeTrue();
            linked.Quality.Should().Be("HDTV-720p");
            (await File.ReadAllBytesAsync(linked.FilePath)).Should().OnlyContain(value => value == (byte)'r');
            File.Exists(heldPath).Should().BeFalse("the selected recording replaces the old library file");
            recording.Status.Should().Be(DvrRecordingStatus.Imported);
            pending.Status.Should().Be(PendingImportStatus.Completed);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task KeepingIndexerFileResolvesLateDvrChoiceWithoutDeletingRecording()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var directory = NewDirectory();
        try
        {
            var recordingPath = Path.Combine(directory, "recording.720p.DVR.ts");
            var indexerPath = Path.Combine(directory, "download.1080p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(recordingPath, new byte[4096]);
            await File.WriteAllBytesAsync(indexerPath, new byte[4096]);
            rig.Db.EventFiles.Add(new EventFile
            {
                EventId = rig.Event.Id, FilePath = indexerPath, Quality = "WEBDL-1080p", Exists = true
            });
            var source = new IptvSource { Name = "Fixture", Url = "https://iptv.invalid/list" };
            var channel = new IptvChannel { Source = source, Name = "Fixture", StreamUrl = "https://iptv.invalid/stream" };
            var recording = new DvrRecording
            {
                EventId = rig.Event.Id, Channel = channel, Title = rig.Event.Title,
                Status = DvrRecordingStatus.Completed, OutputPath = recordingPath,
                Quality = "HDTV-720p", ScheduledStart = DateTime.UtcNow.AddHours(-2),
                ScheduledEnd = DateTime.UtcNow.AddHours(-1)
            };
            rig.Db.DvrRecordings.Add(recording);
            await rig.Db.SaveChangesAsync();
            var pending = new PendingImport
            {
                DownloadId = $"dvr-{recording.Id}", Title = Path.GetFileName(recordingPath),
                FilePath = recordingPath, Size = 4096, SuggestedEventId = rig.Event.Id, Protocol = "DVR"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var deleteResponse = await app.GetTestClient().DeleteAsync(
                $"/api/pending-imports/{pending.Id}?deleteFile=true");
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            File.Exists(recordingPath).Should().BeTrue();
            using var response = await app.GetTestClient().PostAsync(
                $"/api/pending-imports/{pending.Id}/reject?deleteFile=false", null);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            File.Exists(recordingPath).Should().BeTrue();
            recording.Status.ToString().Should().Be("Ignored");
            (await rig.Db.PendingImports.CountAsync()).Should().Be(0);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MissingIndexerChoiceKeepsCaptureUntilItReplacesTheMissingRow()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var directory = NewDirectory("/data/e2e-lib/issue348");
        try
        {
            var missingPath = Path.Combine(directory, "missing.1080p.WEB-DL.mkv");
            var recordingPath = Path.Combine(directory, "recording.720p.DVR.ts");
            await File.WriteAllBytesAsync(recordingPath, new byte[4096]);
            rig.Db.EventFiles.Add(new EventFile
            {
                EventId = rig.Event.Id, FilePath = missingPath, Quality = "WEBDL-1080p", Exists = false
            });
            var recording = new DvrRecording
            {
                EventId = rig.Event.Id,
                Channel = new IptvChannel
                {
                    Source = new IptvSource { Name = "Fixture", Url = "https://iptv.invalid/list" },
                    Name = "Fixture", StreamUrl = "https://iptv.invalid/stream"
                },
                Title = rig.Event.Title, Status = DvrRecordingStatus.Completed,
                OutputPath = recordingPath, Quality = "HDTV-720p",
                ScheduledStart = DateTime.UtcNow.AddHours(-2),
                ScheduledEnd = DateTime.UtcNow.AddHours(-1)
            };
            rig.Db.DvrRecordings.Add(recording);
            await rig.Db.SaveChangesAsync();
            var pending = new PendingImport
            {
                DownloadId = $"dvr-{recording.Id}", Title = "Recording", FilePath = recordingPath,
                SuggestedEventId = rig.Event.Id, Protocol = "DVR"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var reject = await app.GetTestClient().PostAsync(
                $"/api/pending-imports/{pending.Id}/reject", null);
            reject.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            recording.Status.Should().Be(DvrRecordingStatus.Completed);
            File.Exists(recordingPath).Should().BeTrue();

            using var accept = await app.GetTestClient().PostAsync(
                $"/api/pending-imports/{pending.Id}/accept", null);
            accept.IsSuccessStatusCode.Should().BeTrue(await accept.Content.ReadAsStringAsync());
            var linked = await rig.Db.EventFiles.SingleAsync();
            linked.Exists.Should().BeTrue();
            linked.IsIptvRecording.Should().BeTrue();
            linked.FilePath.Should().NotBe(recordingPath);
            File.Exists(linked.FilePath).Should().BeTrue();
            recording.Status.Should().Be(DvrRecordingStatus.Imported);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAcceptSettlesPathAlreadyTrackedForTheSameEvent()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, new byte[4096]);
            rig.Db.EventFiles.Add(new EventFile
            {
                EventId = rig.Event.Id, FilePath = path, Quality = "WEBDL-720p", Exists = false
            });
            var pending = new PendingImport
            {
                DownloadId = "disk-already-tracked", Title = Path.GetFileName(path),
                FilePath = path, Size = 4096, Quality = "WEBDL-720p",
                SuggestedEventId = rig.Event.Id
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var response = await app.GetTestClient()
                .PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                    new { metadataOverrides = new { releaseGroup = "corrected" } });

            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var linked = await rig.Db.EventFiles.SingleAsync();
            linked.Exists.Should().BeTrue();
            linked.ReleaseGroup.Should().Be("corrected");
            rig.Event.HasFile.Should().BeTrue();
            rig.Event.FilePath.Should().Be(path);
            rig.Event.FileSize.Should().Be(4096);
            rig.Event.Quality.Should().Be("WEBDL-720p");
            pending.Status.Should().Be(PendingImportStatus.Completed);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAcceptRefusesLowerQualityWithoutCreatingSecondFile()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var directory = NewDirectory();
        try
        {
            var heldPath = Path.Combine(directory, "held.1080p.WEB-DL.mkv");
            var pendingPath = Path.Combine(directory, "new.720p.HDTV.mkv");
            await File.WriteAllBytesAsync(heldPath, new byte[4096]);
            await File.WriteAllBytesAsync(pendingPath, new byte[4096]);
            rig.Db.EventFiles.Add(new EventFile
            {
                EventId = rig.Event.Id, FilePath = heldPath, Quality = "WEBDL-1080p", Exists = true
            });
            var pending = new PendingImport
            {
                DownloadId = "disk-lower-quality", Title = Path.GetFileName(pendingPath),
                FilePath = pendingPath, Size = 4096, Quality = "HDTV-720p",
                SuggestedEventId = rig.Event.Id
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var response = await app.GetTestClient()
                .PostAsync($"/api/pending-imports/{pending.Id}/accept", null);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await rig.Db.EventFiles.SingleAsync()).FilePath.Should().Be(heldPath);
            pending.Status.Should().Be(PendingImportStatus.Pending);
            File.Exists(heldPath).Should().BeTrue();
            File.Exists(pendingPath).Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAcceptLinksCompletedDvrCaptureAndSettlesRecording()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.1080p.ts");
            await File.WriteAllBytesAsync(path, new byte[4096]);
            var source = new IptvSource { Name = "Fixture", Url = "https://iptv.invalid/list" };
            var channel = new IptvChannel
            {
                Source = source, Name = "Fixture FHD", StreamUrl = "https://iptv.invalid/stream"
            };
            var recording = new DvrRecording
            {
                EventId = rig.Event.Id, Channel = channel, Title = rig.Event.Title,
                Status = DvrRecordingStatus.Completed, OutputPath = path,
                Quality = "HDTV-1080p", QualityScore = 510, VideoCodec = "H264",
                ScheduledStart = DateTime.UtcNow.AddHours(-2),
                ScheduledEnd = DateTime.UtcNow.AddHours(-1)
            };
            var pending = new PendingImport
            {
                DownloadId = "disk-dvr-capture", Title = Path.GetFileName(path),
                FilePath = path, Size = 4096, Quality = "1080p",
                SuggestedEventId = rig.Event.Id
            };
            rig.Db.DvrRecordings.Add(recording);
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var response = await app.GetTestClient()
                .PostAsync($"/api/pending-imports/{pending.Id}/accept", null);

            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var linked = await rig.Db.EventFiles.SingleAsync();
            linked.FilePath.Should().Be(path);
            linked.Quality.Should().Be("HDTV-1080p");
            linked.QualityScore.Should().Be(510);
            linked.Codec.Should().Be("H264");
            linked.Source.Should().Be("IPTV");
            linked.IsIptvRecording.Should().BeTrue();
            recording.Status.Should().Be(DvrRecordingStatus.Imported);
            recording.ImportedAt.Should().NotBeNull();
            pending.Status.Should().Be(PendingImportStatus.Completed);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAcceptReplacesLowerQualityTrackedPart()
    {
        const string mediaRoot = "/data/e2e-lib/issue348";
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            multipart: false, writeMetadata: true, mediaRoot: mediaRoot);
        var directory = NewDirectory(mediaRoot);
        try
        {
            var heldPath = Path.Combine(directory, "held.720p.HDTV.mkv");
            var incomingDirectory = Path.Combine(directory, "incoming");
            Directory.CreateDirectory(incomingDirectory);
            var pendingPath = Path.Combine(incomingDirectory, "new.1080p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(heldPath, Enumerable.Repeat((byte)'h', 4096).ToArray());
            await File.WriteAllBytesAsync(pendingPath, Enumerable.Repeat((byte)'n', 4096).ToArray());
            var oldNfo = Path.ChangeExtension(heldPath, ".nfo");
            var oldThumb = Path.ChangeExtension(heldPath, null) + "-thumb.jpg";
            await File.WriteAllTextAsync(oldNfo, "old metadata");
            await File.WriteAllBytesAsync(oldThumb, [1, 2, 3]);
            rig.Db.EventFiles.Add(new EventFile
            {
                EventId = rig.Event.Id, FilePath = heldPath, Quality = "HDTV-720p", Exists = true
            });
            var pending = new PendingImport
            {
                DownloadId = "disk-higher-quality", Title = Path.GetFileName(pendingPath),
                FilePath = pendingPath, Size = 4096, Quality = "WEBDL-1080p",
                SuggestedEventId = rig.Event.Id
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var response = await app.GetTestClient()
                .PostAsync($"/api/pending-imports/{pending.Id}/accept", null);

            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            pending.Status.Should().Be(PendingImportStatus.Completed);
            var linked = await rig.Db.EventFiles.SingleAsync();
            linked.Quality.Should().Be("WEBDL-1080p");
            (await File.ReadAllBytesAsync(linked.FilePath)).Should().OnlyContain(value => value == (byte)'n');
            File.Exists(oldNfo).Should().BeFalse();
            File.Exists(oldThumb).Should().BeFalse();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingUpgradeFailureRestoresTheTrackedVideo()
    {
        const string mediaRoot = "/data/e2e-lib/issue348";
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            multipart: false, mediaRoot: mediaRoot);
        var directory = NewDirectory(mediaRoot);
        try
        {
            var heldPath = Path.Combine(directory, "held.720p.HDTV.mkv");
            var incomingDirectory = Path.Combine(directory, "incoming");
            Directory.CreateDirectory(incomingDirectory);
            var pendingPath = Path.Combine(incomingDirectory, "new.1080p.WEB-DL.mkv");
            var heldBytes = Enumerable.Repeat((byte)'h', 4096).ToArray();
            await File.WriteAllBytesAsync(heldPath, heldBytes);
            await File.WriteAllBytesAsync(pendingPath, Enumerable.Repeat((byte)'n', 4096).ToArray());
            var blockedRoot = Path.Combine(directory, "blocked-root");
            await File.WriteAllTextAsync(blockedRoot, "not a directory");
            (await rig.Db.RootFolders.SingleAsync()).Path = blockedRoot;
            rig.Db.EventFiles.Add(new EventFile
            {
                EventId = rig.Event.Id, FilePath = heldPath, Quality = "HDTV-720p", Exists = true
            });
            var pending = new PendingImport
            {
                DownloadId = "disk-failed-upgrade", Title = Path.GetFileName(pendingPath),
                FilePath = pendingPath, Size = 4096, Quality = "WEBDL-1080p",
                SuggestedEventId = rig.Event.Id
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var response = await app.GetTestClient()
                .PostAsync($"/api/pending-imports/{pending.Id}/accept", null);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            pending.Status.Should().Be(PendingImportStatus.Pending);
            (await rig.Db.EventFiles.SingleAsync()).FilePath.Should().Be(heldPath);
            (await File.ReadAllBytesAsync(heldPath)).Should().Equal(heldBytes);
            File.Exists(pendingPath).Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("copy", true)]
    [InlineData("auto", false)]
    public async Task PendingFolderImport_ListsVideosAndImportsOnlyTheChosenRace(
        string importMode, bool copyFiles)
    {
        const string mediaRoot = "/data/e2e-lib/issue330";
        Directory.CreateDirectory(mediaRoot);
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            rename: false, multipart: false, title: "South Point 400", sport: "Motorsport",
            leagueName: "NASCAR Cup Series", mediaRoot: mediaRoot);
        rig.Settings.CopyFiles = copyFiles;
        await rig.Db.SaveChangesAsync();
        var directory = NewDirectory(mediaRoot);
        try
        {
            var race = Path.Combine(directory, "South.Point.400.Race.720p.mkv");
            var qualifying = Path.Combine(directory, "South.Point.400.Qualifying.720p.mkv");
            var raceBytes = Enumerable.Repeat((byte)'r', 4096).ToArray();
            var qualifyingBytes = Enumerable.Repeat((byte)'q', 4096).ToArray();
            await File.WriteAllBytesAsync(race, raceBytes);
            await File.WriteAllBytesAsync(qualifying, qualifyingBytes);
            var pending = new PendingImport
            {
                DownloadId = "nascar-weekend-pack", Title = "NCS.2026.Round31.Weekend.Pack",
                FilePath = directory, Size = 8192, Quality = "WEBDL-720p",
                Protocol = "Torrent", TorrentInfoHash = new string('a', 40),
                SuggestedEventId = rig.Event.Id, Status = PendingImportStatus.Pending
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var candidates = await client.GetAsync($"/api/pending-imports/{pending.Id}/video-candidates");
            candidates.IsSuccessStatusCode.Should().BeTrue(await candidates.Content.ReadAsStringAsync());
            var listed = await candidates.Content.ReadFromJsonAsync<List<ImportVideoCandidate>>();
            listed.Should().NotBeNull();
            listed!.Select(file => file.RelativePath).Should().BeEquivalentTo(new[]
            {
                "South.Point.400.Race.720p.mkv", "South.Point.400.Qualifying.720p.mkv"
            });

            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { importMode, selectedRelativePath = "South.Point.400.Race.720p.mkv" });
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            pending.Status.Should().Be(PendingImportStatus.Completed);
            var imported = await rig.Db.EventFiles.SingleAsync();
            (await File.ReadAllBytesAsync(imported.FilePath)).Should().Equal(raceBytes);
            (await File.ReadAllBytesAsync(race)).Should().Equal(raceBytes);
            (await File.ReadAllBytesAsync(qualifying)).Should().Equal(qualifyingBytes);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(null, "copy")]
    [InlineData("../other-file.mkv", "copy")]
    [InlineData("..\\other-file.mkv", "copy")]
    [InlineData("/etc/passwd", "copy")]
    [InlineData("missing.mkv", "copy")]
    [InlineData("South.Point.400.Race.720p.mkv", "move")]
    public async Task PendingFolderImport_RejectsUnsafeChoicesWithoutMovingSiblings(
        string? selectedRelativePath, string importMode)
    {
        const string mediaRoot = "/data/e2e-lib/issue330";
        Directory.CreateDirectory(mediaRoot);
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(
            rename: false, multipart: false, title: "South Point 400", sport: "Motorsport",
            leagueName: "NASCAR Cup Series", mediaRoot: mediaRoot);
        var directory = NewDirectory(mediaRoot);
        try
        {
            var race = Path.Combine(directory, "South.Point.400.Race.720p.mkv");
            var qualifying = Path.Combine(directory, "South.Point.400.Qualifying.720p.mkv");
            await File.WriteAllBytesAsync(race, Enumerable.Repeat((byte)'r', 4096).ToArray());
            await File.WriteAllBytesAsync(qualifying, Enumerable.Repeat((byte)'q', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "nascar-weekend-pack", Title = "NCS.2026.Round31.Weekend.Pack",
                FilePath = directory, Size = 8192, Quality = "WEBDL-720p",
                Protocol = "Torrent", TorrentInfoHash = new string('a', 40),
                SuggestedEventId = rig.Event.Id, Status = PendingImportStatus.Pending
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { importMode, selectedRelativePath });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            pending.Status.Should().Be(PendingImportStatus.Pending);
            File.Exists(race).Should().BeTrue();
            File.Exists(qualifying).Should().BeTrue();
            (await rig.Db.EventFiles.CountAsync()).Should().Be(0);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefusedPendingImport_CannotCompleteOrEditSurvivingFile(bool fullCollision)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        if (fullCollision) { rig.Settings.StandardFileFormat = "same-name"; await rig.Db.SaveChangesAsync(); }
        var held = fullCollision
            ? await PartIdentityContractV2Tests.CreateFull(rig)
            : await rig.ImportAsync("UFC.9999.Prelims", "held-prelims.1080p.WEB-DL.mkv", "Prelims");
        held.Quality = "WEBDL-1080p"; held.ReleaseGroup = "held-group"; rig.Event.Quality = held.Quality;
        await rig.Db.SaveChangesAsync();
        var bytes = await File.ReadAllBytesAsync(held.FilePath);
        var directory = NewDirectory();
        try
        {
            var pending = await AddPending(rig, directory);
            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { importMode = "copy", metadataOverrides = new { releaseGroup = "must-not-touch-survivor" } });
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
            pending.Status.Should().Be(PendingImportStatus.Pending); pending.ResolvedAt.Should().BeNull();
            pending.ErrorMessage.Should().NotBeNullOrWhiteSpace();
            var survivor = await rig.Db.EventFiles.SingleAsync(); survivor.Id.Should().Be(held.Id);
            survivor.ReleaseGroup.Should().Be("held-group"); survivor.Quality.Should().Be("WEBDL-1080p");
            (await File.ReadAllBytesAsync(held.FilePath)).Should().Equal(bytes);
            rig.Event.FilePath.Should().Be(held.FilePath); rig.Event.Quality.Should().Be("WEBDL-1080p");
            File.Exists(pending.FilePath).Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AcceptedDistinctPendingPart_UpdatesOnlyItsOwnFileAndPreservesFullSummary()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var full = await PartIdentityContractV2Tests.CreateFull(rig);
        full.ReleaseGroup = "full-group"; await rig.Db.SaveChangesAsync();
        var bytes = await File.ReadAllBytesAsync(full.FilePath);
        var directory = NewDirectory();
        try
        {
            var pending = await AddPending(rig, directory);
            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { importMode = "copy", metadataOverrides = new { releaseGroup = "new-part-group" } });
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(body);
            pending.Status.Should().Be(PendingImportStatus.Completed); pending.ResolvedAt.Should().NotBeNull();
            var files = await rig.Db.EventFiles.ToListAsync(); files.Should().HaveCount(2);
            var part = files.Single(f => f.Id != full.Id);
            part.PartName.Should().Be("Prelims"); part.PartNumber.Should().Be(2);
            part.ReleaseGroup.Should().Be("new-part-group"); full.ReleaseGroup.Should().Be("full-group");
            (await File.ReadAllBytesAsync(full.FilePath)).Should().Equal(bytes);
            rig.Event.FilePath.Should().Be(full.FilePath); rig.Event.Quality.Should().Be("WEBDL-1080p");
            rig.Event.HasFile.Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("known-length")]
    [InlineData("unknown-length")]
    [InlineData("empty-body")]
    public async Task PendingAccept_ReadsPresentJsonAndPreservesEmptyBodyCompatibility(string bodyMode)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory();
        try
        {
            var pending = await AddPending(rig, directory);
            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            var payload = new { importMode = "copy", metadataOverrides = new { releaseGroup = "body-control-group" } };
            using HttpContent content = bodyMode switch
            {
                "known-length" => new StringContent(System.Text.Json.JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8, "application/json"),
                "unknown-length" => JsonContent.Create(payload),
                _ => new ByteArrayContent(Array.Empty<byte>())
            };
            if (bodyMode == "known-length") content.Headers.ContentLength.Should().BeGreaterThan(0);
            else if (bodyMode == "unknown-length") content.Headers.ContentLength.Should().BeNull();
            else content.Headers.ContentLength.Should().Be(0);
            using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", content);
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(body);
            pending.Status.Should().Be(PendingImportStatus.Completed);
            var file = await rig.Db.EventFiles.SingleAsync();
            file.PartName.Should().Be("Prelims"); file.PartNumber.Should().Be(2);
            if (bodyMode == "empty-body") file.ReleaseGroup.Should().BeNull();
            else file.ReleaseGroup.Should().Be("body-control-group");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAccept_PreservesTheSelectedPartInFileAndHistory()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'d', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "disk-selected-part",
                Title = Path.GetFileNameWithoutExtension(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                Protocol = "Usenet",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Main Card"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);
            var body = await response.Content.ReadAsStringAsync();

            response.IsSuccessStatusCode.Should().BeTrue(body);
            (await rig.Db.EventFiles.SingleAsync()).PartName.Should().Be("Main Card");
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be("Main Card");
            rig.Event.HasFile.Should().BeFalse();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAccept_StoresTheCustomFormatScoreTheFileNameEarns()
    {
        var mediaRoot = Environment.GetEnvironmentVariable("SPORTARR_PR340_E2E_ROOT");
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(mediaRoot: mediaRoot);
        var directory = NewDirectory(mediaRoot);
        try
        {
            var format = new CustomFormat { Name = "WEB-DL", Specifications = new List<FormatSpecification>
            {
                new() { Name = "Title", Implementation = "ReleaseTitle", Required = true,
                    Fields = new Dictionary<string, object> { { "value", "WEB-DL" } } },
            } };
            rig.Db.CustomFormats.Add(format);
            await rig.Db.SaveChangesAsync();
            var profile = await rig.Db.QualityProfiles.SingleAsync();
            profile.FormatItems.Add(new ProfileFormatItem { FormatId = format.Id, Score = 25 });
            await rig.Db.SaveChangesAsync();
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'s', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "disk-format-score",
                Title = Path.GetFileNameWithoutExtension(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Main Card"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);
            var body = await response.Content.ReadAsStringAsync();

            response.IsSuccessStatusCode.Should().BeTrue(body);
            (await rig.Db.EventFiles.SingleAsync()).CustomFormatScore.Should().Be(25,
                "a stored score of 0 makes every scored release look like an upgrade over this file");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAccept_InfersPartForExistingPendingRowWithoutAStoredPart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'m', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "disk-existing-row",
                Title = Path.GetFileName(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                SuggestedEventId = rig.Event.Id
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var imported = await rig.Db.EventFiles.SingleAsync();
            imported.PartName.Should().Be("Main Card");
            imported.PartNumber.Should().Be(3);
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be("Main Card");
            rig.Event.HasFile.Should().BeFalse();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAccept_ExplicitFullEventOverridesPartInFilename()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'f', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "disk-explicit-full",
                Title = Path.GetFileName(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Full Event"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var imported = await rig.Db.EventFiles.SingleAsync();
            imported.PartName.Should().BeNull();
            imported.PartNumber.Should().BeNull();
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().BeNull();
            rig.Event.HasFile.Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiskPendingAccept_UsesEditedPartForHistoryAndCompletion()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Event.MonitoredParts = "Main Card";
        await rig.Db.SaveChangesAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'e', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "disk-edited-part",
                Title = Path.GetFileName(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Main Card"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { metadataOverrides = new { partName = "Prelims", partNumber = 2 } });
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var imported = await rig.Db.EventFiles.SingleAsync();
            imported.PartName.Should().Be("Prelims");
            imported.PartNumber.Should().Be(2);
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be("Prelims");
            rig.Event.HasFile.Should().BeFalse();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("Prelims")]
    [InlineData(null)]
    public async Task ClientPendingAccept_UsesEditedPartForHistoryAndCompletion(string? partName)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Event.MonitoredParts = "Main Card";
        await rig.Db.SaveChangesAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'c', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "client-edited-part",
                Title = Path.GetFileName(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                Protocol = "Usenet",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Main Card"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { importMode = "copy", metadataOverrides = new { partName, partNumber = 2 } });
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var imported = await rig.Db.EventFiles.SingleAsync();
            imported.PartName.Should().Be("Prelims");
            imported.PartNumber.Should().Be(2);
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be("Prelims");
            rig.Event.HasFile.Should().BeFalse();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(null, 2, "Prelims", 2, false)]
    [InlineData("Full Event", null, null, null, true)]
    public async Task DiskPendingAccept_NormalizesPartEditsBeforeCompleteness(
        string? partName, int? partNumber, string? expectedName, int? expectedNumber, bool expectedComplete)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        rig.Event.MonitoredParts = "Main Card";
        await rig.Db.SaveChangesAsync();
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'n', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "disk-normalized-part",
                Title = Path.GetFileName(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Main Card"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { metadataOverrides = new { partName, partNumber } });
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var imported = await rig.Db.EventFiles.SingleAsync();
            imported.PartName.Should().Be(expectedName);
            imported.PartNumber.Should().Be(expectedNumber);
            (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be(expectedName);
            rig.Event.HasFile.Should().Be(expectedComplete);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ClientPendingAccept_EditedPartUsesItsOwnUpgradeSlot()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var heldMain = await rig.ImportAsync("UFC.9999.Main.Card.720p.WEB-DL",
            "held-main.720p.WEB-DL.mkv", "Main Card");
        var heldBytes = await File.ReadAllBytesAsync(heldMain.FilePath);
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'u', 4096).ToArray());
            var pending = new PendingImport
            {
                DownloadId = "client-part-slot",
                Title = Path.GetFileName(path),
                FilePath = path,
                Size = 4096,
                Quality = "WEBDL-720p",
                Protocol = "Usenet",
                SuggestedEventId = rig.Event.Id,
                SuggestedPart = "Main Card"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var client = app.GetTestClient();
            using var response = await client.PostAsJsonAsync($"/api/pending-imports/{pending.Id}/accept",
                new { importMode = "copy", metadataOverrides = new { partName = "Prelims", partNumber = 2 } });
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            var files = await rig.Db.EventFiles.ToListAsync();
            files.Should().HaveCount(2);
            var prelims = files.Single(file => file.Id != heldMain.Id);
            prelims.PartName.Should().Be("Prelims");
            prelims.PartNumber.Should().Be(2);
            Path.GetFileName(prelims.FilePath).Should().Contain("Prelims");
            (await File.ReadAllBytesAsync(heldMain.FilePath)).Should().Equal(heldBytes);
            (await rig.Db.ImportHistories.OrderBy(history => history.Id).LastAsync()).Part.Should().Be("Prelims");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LeagueScan_PersistsTheSuggestedPartForPendingAcceptance()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var root = (await rig.Db.RootFolders.SingleAsync()).Path;
        var folder = Path.Combine(root, "UFC");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "UFC.9999.2020.09.01.Main.Card.720p.WEB-DL.mkv");
        await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'m', 4096).ToArray());

        await using var app = await CreatePendingHost(rig, includeLeagueScan: true);
        using var client = app.GetTestClient();
        using var scanResponse = await client.PostAsJsonAsync($"/api/leagues/{rig.Event.LeagueId}/scan", new { });
        var scanBody = await scanResponse.Content.ReadAsStringAsync();
        scanResponse.IsSuccessStatusCode.Should().BeTrue(scanBody);
        using var scan = System.Text.Json.JsonDocument.Parse(scanBody);
        var files = scan.RootElement.GetProperty("files");
        files.GetArrayLength().Should().Be(1);
        files[0].GetProperty("suggestedEventId").GetInt32().Should().Be(rig.Event.Id);
        files[0].GetProperty("part").GetString().Should().Be("Main Card");

        var pending = await rig.Db.PendingImports.SingleAsync();
        pending.SuggestedEventId.Should().Be(rig.Event.Id);
        pending.SuggestedPart.Should().Be("Main Card");
        using var acceptResponse = await client.PostAsync($"/api/pending-imports/{pending.Id}/accept", null);
        acceptResponse.IsSuccessStatusCode.Should().BeTrue(await acceptResponse.Content.ReadAsStringAsync());
        var imported = await rig.Db.EventFiles.SingleAsync();
        imported.PartName.Should().Be("Main Card");
        imported.PartNumber.Should().Be(3);
        (await rig.Db.ImportHistories.SingleAsync()).Part.Should().Be("Main Card");
        rig.Event.HasFile.Should().BeFalse();
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("remove-from-client")]
    public async Task PendingRemoval_PreservesTheSelectedEventAndPartInBlocklist(string action)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var pending = new PendingImport
        {
            DownloadId = "pending-removal-part",
            Title = "UFC.9999.Prelims.720p.WEB-DL",
            FilePath = "/data/e2e-lib/downloads/UFC.9999.Prelims.720p.WEB-DL.mkv",
            Size = 4096,
            Quality = "WEBDL-720p",
            Protocol = "Usenet",
            SuggestedEventId = rig.Event.Id,
            SuggestedPart = "Prelims"
        };
        rig.Db.PendingImports.Add(pending);
        await rig.Db.SaveChangesAsync();

        await using var app = await CreatePendingHost(rig);
        using var client = app.GetTestClient();
        using var response = await client.PostAsync($"/api/pending-imports/{pending.Id}/{action}", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var blocked = await rig.Db.Blocklist.SingleAsync();
        blocked.EventId.Should().Be(rig.Event.Id);
        blocked.Part.Should().Be("Prelims");
    }

    [Fact]
    public async Task DvrChoiceCannotBeRemovedWithoutADecision()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var recording = new DvrRecording
        {
            EventId = rig.Event.Id,
            Channel = new IptvChannel
            {
                Source = new IptvSource { Name = "Fixture", Url = "https://iptv.invalid/list" },
                Name = "Fixture",
                StreamUrl = "https://iptv.invalid/stream"
            },
            Title = rig.Event.Title,
            Status = DvrRecordingStatus.Completed,
            OutputPath = "/data/e2e-lib/recording.ts"
        };
        rig.Db.DvrRecordings.Add(recording);
        await rig.Db.SaveChangesAsync();
        var pending = new PendingImport
        {
            DownloadId = $"dvr-{recording.Id}",
            Title = "Recording",
            FilePath = recording.OutputPath,
            SuggestedEventId = rig.Event.Id,
            Protocol = "DVR"
        };
        rig.Db.PendingImports.Add(pending);
        await rig.Db.SaveChangesAsync();

        await using var app = await CreatePendingHost(rig);
        using var response = await app.GetTestClient().PostAsync(
            $"/api/pending-imports/{pending.Id}/remove-from-client", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rig.Db.PendingImports.CountAsync()).Should().Be(1);
        (await rig.Db.Blocklist.CountAsync()).Should().Be(0);
        recording.Status.Should().Be(DvrRecordingStatus.Completed);
    }

    [Fact]
    public async Task DvrRetentionKeepsRecordingWhileManualChoiceIsOpen()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory("/data/e2e-lib/issue348");
        try
        {
            var source = new IptvSource { Name = "Fixture", Url = "https://iptv.invalid/list" };
            var channel = new IptvChannel
            {
                Source = source, Name = "Fixture", StreamUrl = "https://iptv.invalid/stream"
            };
            var heldPath = Path.Combine(directory, "awaiting-choice.ts");
            var missingBlockedPath = Path.Combine(directory, "waiting-for-missing-file.ts");
            var stalePath = Path.Combine(directory, "expired.ts");
            await File.WriteAllBytesAsync(heldPath, [1, 2, 3]);
            await File.WriteAllBytesAsync(missingBlockedPath, [7, 8, 9]);
            await File.WriteAllBytesAsync(stalePath, [4, 5, 6]);
            var oldEnd = DateTime.UtcNow.AddDays(-3);
            var otherEvent = new Event
            {
                LeagueId = rig.Event.LeagueId, Title = "Other fixture",
                Sport = rig.Event.Sport, ExternalId = "ev-998348",
                EventDate = oldEnd
            };
            rig.Db.Events.Add(otherEvent);
            var held = new DvrRecording
            {
                EventId = rig.Event.Id, Channel = channel, Title = "Awaiting choice",
                Status = DvrRecordingStatus.Completed, OutputPath = heldPath,
                ScheduledStart = oldEnd.AddHours(-1), ScheduledEnd = oldEnd
            };
            var stale = new DvrRecording
            {
                EventId = rig.Event.Id, Channel = channel, Title = "Expired",
                Status = DvrRecordingStatus.Ignored, OutputPath = stalePath,
                ScheduledStart = oldEnd.AddHours(-1), ScheduledEnd = oldEnd
            };
            var missingBlocked = new DvrRecording
            {
                Event = otherEvent, Channel = channel, Title = "Missing file grace",
                Status = DvrRecordingStatus.Completed, OutputPath = missingBlockedPath,
                ScheduledStart = oldEnd.AddHours(-1), ScheduledEnd = oldEnd
            };
            rig.Db.DvrRecordings.AddRange(held, stale, missingBlocked);
            await rig.Db.SaveChangesAsync();
            rig.Db.EventFiles.Add(new EventFile
            {
                EventId = otherEvent.Id,
                FilePath = Path.Combine(directory, "missing-indexer.mkv"),
                Exists = false, MissingSince = DateTime.UtcNow
            });
            rig.Db.PendingImports.Add(new PendingImport
            {
                DownloadId = $"dvr-{held.Id}", Title = "Awaiting choice",
                FilePath = heldPath, SuggestedEventId = rig.Event.Id, Protocol = "DVR"
            });
            await rig.Db.SaveChangesAsync();

            var service = new HousekeepingService(rig.Services, NullLogger<HousekeepingService>.Instance);
            await service.PruneDvrRecordingsAsync(rig.Db,
                new Config { DvrRecordingRetentionDays = 1 }, CancellationToken.None);

            (await rig.Db.DvrRecordings.Select(recording => recording.Id).ToListAsync())
                .Should().BeEquivalentTo([held.Id, missingBlocked.Id]);
            File.Exists(heldPath).Should().BeTrue();
            File.Exists(missingBlockedPath).Should().BeTrue();
            File.Exists(stalePath).Should().BeFalse();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DvrChoiceRefusesAmbiguousTrackedFilesWithoutChangingThem()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false);
        var directory = NewDirectory("/data/e2e-lib/issue348");
        try
        {
            var firstPath = Path.Combine(directory, "first.1080p.WEB-DL.mkv");
            var secondPath = Path.Combine(directory, "second.1080p.WEB-DL.mkv");
            var recordingPath = Path.Combine(directory, "recording.720p.DVR.ts");
            foreach (var path in new[] { firstPath, secondPath, recordingPath })
                await File.WriteAllBytesAsync(path, [1, 2, 3]);
            rig.Db.EventFiles.AddRange(
                new EventFile { EventId = rig.Event.Id, FilePath = firstPath, Quality = "WEBDL-1080p", Exists = true },
                new EventFile { EventId = rig.Event.Id, FilePath = secondPath, Quality = "WEBDL-1080p", Exists = true });
            var recording = new DvrRecording
            {
                EventId = rig.Event.Id,
                Channel = new IptvChannel
                {
                    Source = new IptvSource { Name = "Fixture", Url = "https://iptv.invalid/list" },
                    Name = "Fixture", StreamUrl = "https://iptv.invalid/stream"
                },
                Title = rig.Event.Title, Status = DvrRecordingStatus.Completed,
                OutputPath = recordingPath, Quality = "HDTV-720p",
                ScheduledStart = DateTime.UtcNow.AddHours(-2),
                ScheduledEnd = DateTime.UtcNow.AddHours(-1)
            };
            rig.Db.DvrRecordings.Add(recording);
            await rig.Db.SaveChangesAsync();
            var pending = new PendingImport
            {
                DownloadId = $"dvr-{recording.Id}", Title = "Recording",
                FilePath = recordingPath, SuggestedEventId = rig.Event.Id,
                Protocol = "DVR"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var response = await app.GetTestClient().PostAsync(
                $"/api/pending-imports/{pending.Id}/accept", null);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Multiple tracked files");
            (await rig.Db.EventFiles.CountAsync()).Should().Be(2);
            File.Exists(firstPath).Should().BeTrue();
            File.Exists(secondPath).Should().BeTrue();
            File.Exists(recordingPath).Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FullEventPendingImportDoesNotDiscardLegacyPartFile()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory("/data/e2e-lib/issue348");
        try
        {
            var heldPath = Path.Combine(directory, "main-card.720p.WEB-DL.mkv");
            var incomingDirectory = Path.Combine(directory, "incoming");
            Directory.CreateDirectory(incomingDirectory);
            var pendingPath = Path.Combine(incomingDirectory, "full-event.1080p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(heldPath, [1, 2, 3]);
            await File.WriteAllBytesAsync(pendingPath, [4, 5, 6]);
            rig.Db.EventFiles.Add(new EventFile
            {
                EventId = rig.Event.Id, FilePath = heldPath, Quality = "WEBDL-720p",
                PartName = "Main Card", PartNumber = null, Exists = true
            });
            var pending = new PendingImport
            {
                DownloadId = "disk-full-over-legacy-part", Title = "Full Event",
                FilePath = pendingPath, Quality = "WEBDL-1080p", Size = 3,
                SuggestedEventId = rig.Event.Id, SuggestedPart = "Full Event"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var response = await app.GetTestClient().PostAsync(
                $"/api/pending-imports/{pending.Id}/accept", null);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("part files");
            (await rig.Db.EventFiles.SingleAsync()).FilePath.Should().Be(heldPath);
            File.Exists(heldPath).Should().BeTrue();
            File.Exists(pendingPath).Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FullEventPendingImportCanCoexistWithNumberedPartFile()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory("/data/e2e-lib/issue348");
        try
        {
            var partPath = Path.Combine(directory, "prelims.720p.WEB-DL.mkv");
            var fullPath = Path.Combine(directory, "full-event.1080p.WEB-DL.mkv");
            await File.WriteAllBytesAsync(partPath, [1, 2, 3]);
            await File.WriteAllBytesAsync(fullPath, [4, 5, 6]);
            rig.Db.EventFiles.Add(new EventFile
            {
                EventId = rig.Event.Id, FilePath = partPath, Quality = "WEBDL-720p",
                PartName = "Prelims", PartNumber = 2, Exists = true
            });
            var pending = new PendingImport
            {
                DownloadId = "disk-full-next-to-part", Title = "Full Event",
                FilePath = fullPath, Quality = "WEBDL-1080p", Size = 3,
                SuggestedEventId = rig.Event.Id, SuggestedPart = "Full Event"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var response = await app.GetTestClient().PostAsync(
                $"/api/pending-imports/{pending.Id}/accept", null);
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            (await rig.Db.EventFiles.CountAsync()).Should().Be(2);
            File.Exists(partPath).Should().BeTrue();
            File.Exists(fullPath).Should().BeTrue();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FullEventDvrChoiceCanReplaceFullFileBesideLegacyPart()
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync();
        var directory = NewDirectory("/data/e2e-lib/issue348");
        try
        {
            var partPath = Path.Combine(directory, "main-card.mkv");
            var heldPath = Path.Combine(directory, "held-full.mkv");
            var recordingPath = Path.Combine(directory, "recording.720p.DVR.ts");
            await File.WriteAllBytesAsync(partPath, [1, 2, 3]);
            await File.WriteAllBytesAsync(heldPath, [4, 5, 6]);
            await File.WriteAllBytesAsync(recordingPath, [7, 8, 9]);
            rig.Db.EventFiles.AddRange(
                new EventFile
                {
                    EventId = rig.Event.Id, FilePath = partPath, Quality = "WEBDL-720p",
                    PartName = "Main Card", PartNumber = null, Exists = true
                },
                new EventFile
                {
                    EventId = rig.Event.Id, FilePath = heldPath, Quality = "WEBDL-1080p", Exists = true
                },
                new EventFile
                {
                    EventId = rig.Event.Id, FilePath = Path.Combine(directory, "old-missing.mkv"),
                    Quality = "WEBDL-720p", Exists = false
                });
            var recording = new DvrRecording
            {
                EventId = rig.Event.Id,
                Channel = new IptvChannel
                {
                    Source = new IptvSource { Name = "Fixture", Url = "https://iptv.invalid/list" },
                    Name = "Fixture", StreamUrl = "https://iptv.invalid/stream"
                },
                Title = rig.Event.Title, Status = DvrRecordingStatus.Completed,
                OutputPath = recordingPath, Quality = "HDTV-720p",
                ScheduledStart = DateTime.UtcNow.AddHours(-2),
                ScheduledEnd = DateTime.UtcNow.AddHours(-1)
            };
            rig.Db.DvrRecordings.Add(recording);
            await rig.Db.SaveChangesAsync();
            var pending = new PendingImport
            {
                DownloadId = $"dvr-{recording.Id}", Title = "Recording",
                FilePath = recordingPath, SuggestedEventId = rig.Event.Id, Protocol = "DVR"
            };
            rig.Db.PendingImports.Add(pending);
            await rig.Db.SaveChangesAsync();

            await using var app = await CreatePendingHost(rig);
            using var response = await app.GetTestClient().PostAsync(
                $"/api/pending-imports/{pending.Id}/accept", null);
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            (await rig.Db.EventFiles.CountAsync()).Should().Be(3);
            File.Exists(partPath).Should().BeTrue();
            File.Exists(heldPath).Should().BeFalse();
            recording.Status.Should().Be(DvrRecordingStatus.Imported);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterMissingIndexerRowDoesNotDiscardTheOnlyRecording(bool markedAsExisting)
    {
        await using var rig = await PartIdentityIntegrationHarness.CreateAsync(multipart: false,
            mediaRoot: "/data/e2e-lib/issue348");
        var directory = NewDirectory("/data/e2e-lib/issue348");
        try
        {
            var recordingPath = Path.Combine(directory, "recording.720p.DVR.ts");
            await File.WriteAllBytesAsync(recordingPath, new byte[4096]);
            var configService = rig.Services.GetRequiredService<ConfigService>();
            var config = await configService.GetConfigAsync();
            config.DvrReplaceRecordingsWithIndexerReleases = true;
            await configService.SaveConfigAsync(config);
            var recording = new DvrRecording
            {
                EventId = rig.Event.Id, Event = rig.Event,
                Channel = new IptvChannel
                {
                    Source = new IptvSource { Name = "Fixture", Url = "https://iptv.invalid/list" },
                    Name = "Fixture", StreamUrl = "https://iptv.invalid/stream"
                },
                Title = rig.Event.Title, Status = DvrRecordingStatus.Completed,
                OutputPath = recordingPath, Quality = "HDTV-720p",
                ScheduledStart = DateTime.UtcNow.AddHours(-2),
                ScheduledEnd = DateTime.UtcNow.AddHours(-1)
            };
            rig.Db.DvrRecordings.Add(recording);
            rig.Db.EventFiles.Add(new EventFile
            {
                EventId = rig.Event.Id, FilePath = Path.Combine(directory, "missing.mkv"),
                Quality = "WEBDL-1080p", Exists = markedAsExisting, Added = DateTime.UtcNow
            });
            await rig.Db.SaveChangesAsync();
            var service = new EventDvrService(NullLogger<EventDvrService>.Instance, rig.Db,
                null!, null!, null!,
                new FFmpegRecorderService(NullLogger<FFmpegRecorderService>.Instance, configService, null!),
                null!, null!, null!, configService,
                rig.Services.GetRequiredService<LibraryImportService>());

            (await service.ImportCompletedRecordingAsync(recording.Id)).Should().BeTrue();
            recording.Status.Should().Be(DvrRecordingStatus.Imported);
            var linked = await rig.Db.EventFiles.SingleAsync(file => file.Exists);
            linked.IsIptvRecording.Should().BeTrue();
            File.Exists(linked.FilePath).Should().BeTrue();
            File.Exists(recordingPath).Should().BeFalse();
            (await rig.Db.EventFiles.CountAsync()).Should().Be(1);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string NewDirectory(string? mediaRoot = null)
    {
        if (mediaRoot != null && !Path.GetFullPath(mediaRoot).StartsWith("/data/e2e-lib/", StringComparison.Ordinal))
            throw new InvalidOperationException("The test media root must be under /data/e2e-lib.");
        var path = Path.Combine(mediaRoot ?? Path.GetTempPath(),
            "sportarr-pending-part-v2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }

    private static async Task<PendingImport> AddPending(PartIdentityIntegrationHarness rig, string directory)
    {
        var path = Path.Combine(directory, "UFC.9999.2020.09.01.Prelims.720p.WEB-DL.mkv");
        await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'p', 4096).ToArray());
        var pending = new PendingImport { DownloadId = "pending-part-v2", Title = Path.GetFileNameWithoutExtension(path),
            FilePath = path, Size = 4096, Quality = "WEBDL-720p", Protocol = "Usenet", SuggestedEventId = rig.Event.Id };
        rig.Db.PendingImports.Add(pending); await rig.Db.SaveChangesAsync(); return pending;
    }

    private static async Task<WebApplication> CreatePendingHost(PartIdentityIntegrationHarness rig, bool includeLeagueScan = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(rig.Db);
        builder.Services.AddSingleton(rig.Services.GetRequiredService<FileImportService>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<LibraryImportService>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<ConfigService>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<EventPartDetector>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<DownloadClientService>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<PackImportService>());
        builder.Services.AddSingleton(rig.Services.GetRequiredService<CustomFormatService>());
        // The mapped sibling routes must resolve as services and must never run here.
        builder.Services.AddSingleton<QueueRemovalService>(_ => throw new InvalidOperationException("Unexpected queue removal route"));
        builder.Services.AddSingleton<ImportMatchingService>(_ => includeLeagueScan
            ? new ImportMatchingService(rig.Db,
                rig.Services.GetRequiredService<MediaFileParser>(),
                rig.Services.GetRequiredService<SportsFileNameParser>(),
                rig.Services.GetRequiredService<EventPartDetector>(),
                rig.Services.GetRequiredService<ILogger<ImportMatchingService>>())
            : throw new InvalidOperationException("Unexpected matching route"));
        if (includeLeagueScan)
        {
            builder.Services.AddSingleton(rig.Services.GetRequiredService<FileNamingService>());
            builder.Services.AddSingleton(rig.Services.GetRequiredService<SportarrApiClient>());
            builder.Services.AddSingleton(rig.Services.GetRequiredService<EventQueryService>());
            builder.Services.AddSingleton<TaskService>(_ => throw new InvalidOperationException("Unexpected task route"));
            builder.Services.AddSingleton<EventStreamService>(_ => throw new InvalidOperationException("Unexpected event stream route"));
            builder.Services.AddSingleton<FileRenameService>(_ => throw new InvalidOperationException("Unexpected rename route"));
            builder.Services.AddSingleton<LeagueAddService>(_ => throw new InvalidOperationException("Unexpected league add route"));
            builder.Services.AddSingleton<LeagueEventSyncService>(_ => throw new InvalidOperationException("Unexpected league sync route"));
            builder.Services.AddSingleton<LeagueMoveService>(_ => throw new InvalidOperationException("Unexpected league move route"));
        }
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
        var app = builder.Build();
        app.MapQueueAndImportEndpoints();
        if (includeLeagueScan) app.MapLeagueEndpoints();
        await app.StartAsync();
        return app;
    }
}
