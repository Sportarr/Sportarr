using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class PackMemberImportBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TaggedMembersKeepTheirOwnBytesRegardlessOfSizeAndImportOrder(bool reverse)
    {
        await using var rig = await PackRig.CreateAsync();
        await rig.MemberAsync(0, reverse ? 8192 : 4096);
        await rig.MemberAsync(1, reverse ? 4096 : 8192);
        foreach (var index in reverse ? new[] { 1, 0 } : new[] { 0, 1 })
            Assert.NotNull(await rig.ImportAsync(index));
        await rig.AssertOwnedBytesAsync(0);
        await rig.AssertOwnedBytesAsync(1);
        Assert.Equal(2, await rig.Db.EventFiles.CountAsync());
        Assert.Equal(2, await rig.Db.DownloadQueue.CountAsync(q => q.Status == DownloadStatus.Imported));
    }

    [Fact]
    public async Task AbsentThirdOwnerCannotConsumeEitherCopiedMemberOrDisplaceTheirRecords()
    {
        await using var rig = await PackRig.CreateAsync();
        await rig.MemberAsync(0, 4096); await rig.MemberAsync(1, 8192);
        Assert.NotNull(await rig.ImportAsync(0)); Assert.NotNull(await rig.ImportAsync(1));
        Assert.Null(await rig.ImportAsync(2));
        await rig.AssertOwnedBytesAsync(0); await rig.AssertOwnedBytesAsync(1);
        await rig.AssertHeldAsync(2);
        Assert.Equal(2, await rig.Db.EventFiles.CountAsync());
    }

    [Theory]
    [InlineData("{sportarr-ev-9999999}")]
    [InlineData("{sportarr-ev-9900301}.{sportarr-ev-9900302}")]
    [InlineData("{sportarr-ev-9900301}.ev-9900302")]
    public async Task UnknownOrConflictingTokensCannotFallBackToMatchingTeamText(string tokens)
    {
        await using var rig = await PackRig.CreateAsync();
        await rig.MemberAsync(0, 4096, tokens: tokens);
        Assert.Null(await rig.ImportAsync(0));
        await rig.AssertHeldAsync(0);
        Assert.Empty(await rig.Db.EventFiles.ToListAsync());
    }

    [Fact]
    public async Task OneRemainingMemberForAnotherOwnerCannotBecomeTheAbsentOwnersFile()
    {
        await using var rig = await PackRig.CreateAsync();
        await rig.MemberAsync(1, 4096);
        Assert.Null(await rig.ImportAsync(0));
        await rig.AssertHeldAsync(0);
        Assert.True(File.Exists(rig.Paths[1]));
    }

    [Fact]
    public async Task TwoMembersClaimingOneOwnerRemainAmbiguousInsteadOfChoosingTheLarger()
    {
        await using var rig = await PackRig.CreateAsync();
        await rig.MemberAsync(0, 4096);
        await rig.MemberAsync(0, 8192, suffix: ".alternate");
        Assert.Null(await rig.ImportAsync(0));
        await rig.AssertHeldAsync(0);
        Assert.Equal(2, Directory.GetFiles(rig.Folder, "*.mkv").Length);
    }

    [Fact]
    public async Task UntaggedDistinctDatedTeamPairingsCanSelectTheirUniqueOwnedMembers()
    {
        await using var rig = await PackRig.CreateAsync();
        await rig.MemberAsync(0, 4096, tokens: ""); await rig.MemberAsync(1, 8192, tokens: "");
        Assert.NotNull(await rig.ImportAsync(0)); Assert.NotNull(await rig.ImportAsync(1));
        await rig.AssertOwnedBytesAsync(0); await rig.AssertOwnedBytesAsync(1);
    }

    [Fact]
    public async Task UntaggedSameDateAndPairingAcrossTwoOwnersIsNotUniqueEvidence()
    {
        await using var rig = await PackRig.CreateAsync();
        rig.Events[1].Title = rig.Events[0].Title;
        rig.Events[1].HomeTeamName = rig.Events[0].HomeTeamName;
        rig.Events[1].AwayTeamName = rig.Events[0].AwayTeamName;
        rig.Events[1].EventDate = rig.Events[0].EventDate;
        await rig.Db.SaveChangesAsync();
        await rig.MemberAsync(0, 4096, tokens: "");
        Assert.Null(await rig.ImportAsync(0)); await rig.AssertHeldAsync(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task UntaggedDateAndLeagueWithoutParticipantMetadataCannotIdentifyAnOwner(string? empty)
    {
        await using var rig = await PackRig.CreateAsync();
        foreach (var item in rig.Events) { item.Title = "Week 1"; item.HomeTeamName = empty; item.AwayTeamName = empty; }
        await rig.Db.SaveChangesAsync();
        await rig.MemberAsync(0, 4096, tokens: "");
        Assert.Null(await rig.ImportAsync(0)); await rig.AssertHeldAsync(0);
    }

    [Fact]
    public async Task OrdinaryNonPackWithTwoUnclearVideosWaitsForAChoice()
    {
        await using var rig = await PackRig.CreateAsync();
        rig.Owners[0].IsPack = false; rig.Owners[0].PackGroupId = null;
        await rig.Db.SaveChangesAsync();
        await rig.MemberAsync(0, 4096); await rig.MemberAsync(1, 8192);
        var history = await rig.ImportAsync(0); Assert.Null(history);
        Assert.Equal(DownloadStatus.ImportWarning, rig.Owners[0].Status);
        Assert.Equal(ManualQueueImportPolicy.AmbiguousVideoWarning, rig.Owners[0].ErrorMessage);
    }

    [Fact]
    public async Task MovingOneMemberCannotDeleteTheReleaseDirectoryWithUncoveredSiblings()
    {
        await using var rig = await PackRig.CreateAsync();
        rig.Settings.CopyFiles = false;
        var client = await rig.Db.DownloadClients.SingleAsync(); client.RemoveCompletedDownloads = true;
        await rig.Db.SaveChangesAsync();
        await rig.MemberAsync(0, 8192); await rig.MemberAsync(1, 4096);
        Assert.NotNull(await rig.ImportAsync(0, PostImportMode.Move));
        Assert.True(Directory.Exists(rig.Folder));
        Assert.True(File.Exists(rig.Paths[1]));
        Assert.Equal(rig.Bytes[1], await File.ReadAllBytesAsync(rig.Paths[1]));
        await rig.AssertOwnedBytesAsync(0); await rig.AssertHeldAsync(2, requireWarning: false);
    }

    [Fact]
    public async Task SingleKnownTaggedMemberStillImportsThroughTheActualPipeline()
    {
        await using var rig = await PackRig.CreateAsync();
        await rig.MemberAsync(0, 4096);
        Assert.NotNull(await rig.ImportAsync(0));
        await rig.AssertOwnedBytesAsync(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForeignDestinationProtectionIsPackOnlyAndPreservesOrdinaryUpsert(bool isPack)
    {
        await using var rig = await PackRig.CreateAsync();
        rig.Owners[0].IsPack = isPack;
        if (!isPack) rig.Owners[0].PackGroupId = null;
        await rig.MemberAsync(0, 4096);
        var library = (await rig.Db.RootFolders.SingleAsync()).Path;
        var destination = Path.Combine(library, Path.GetFileName(rig.Paths[0]));
        var existingBytes = Enumerable.Repeat((byte)90, 8192).ToArray();
        await File.WriteAllBytesAsync(destination, existingBytes);
        var foreign = new EventFile { EventId = rig.Events[1].Id, FilePath = destination,
            Size = existingBytes.Length, Quality = "WEBDL-720p", Exists = true };
        rig.Db.EventFiles.Add(foreign);
        rig.Events[1].HasFile = true; rig.Events[1].FilePath = destination;
        await rig.Db.SaveChangesAsync();
        var history = await rig.ImportAsync(0);
        if (isPack)
        {
            Assert.Null(history); await rig.AssertHeldAsync(0);
            Assert.Equal(existingBytes, await File.ReadAllBytesAsync(destination));
            Assert.Equal(rig.Events[1].Id, (await rig.Db.EventFiles.SingleAsync()).EventId);
            Assert.True(rig.Events[1].HasFile);
        }
        else
        {
            Assert.NotNull(history); await rig.AssertOwnedBytesAsync(0);
            Assert.False(await rig.Db.EventFiles.AnyAsync(f => f.EventId == rig.Events[1].Id));
            Assert.False(rig.Events[1].HasFile);
        }
    }

    private sealed class PackRig : IAsyncDisposable
    {
        private readonly PartIdentityIntegrationHarness _base;
        public Sportarr.Api.Data.SportarrDbContext Db => _base.Db;
        public MediaManagementSettings Settings => _base.Settings;
        public Event[] Events { get; private set; } = null!;
        public DownloadQueueItem[] Owners { get; private set; } = null!;
        public string Folder { get; private set; } = null!;
        public Dictionary<int, string> Paths { get; } = new();
        public Dictionary<int, byte[]> Bytes { get; } = new();
        private const string ReleaseTitle = "NFL.2020.Week1.720p.WEB-DL.H264-PackBoundary";
        private PackRig(PartIdentityIntegrationHarness basis) { _base = basis; }

        public static async Task<PackRig> CreateAsync()
        {
            var basis = await PartIdentityIntegrationHarness.CreateAsync(rename: false, multipart: false,
                title: "Team A vs Team B", sport: "American Football", leagueName: "NFL");
            var rig = new PackRig(basis);
            var league = basis.Event.League!;
            rig.Events = new[] { basis.Event,
                new Event { Title = "Team C vs Team D", Sport = "American Football", LeagueId = league.Id, League = league },
                new Event { Title = "Team E vs Team F", Sport = "American Football", LeagueId = league.Id, League = league } };
            for (var i = 0; i < 3; i++)
            {
                var item = rig.Events[i]; item.ExternalId = "ev-990030" + (i + 1);
                item.HomeTeamName = "Team " + (char)('A' + 2 * i); item.AwayTeamName = "Team " + (char)('B' + 2 * i);
                item.EventDate = new DateTime(2020, 9, i + 1, 20, 0, 0, DateTimeKind.Utc);
                item.Season = "2020"; item.SeasonNumber = 2020; item.EpisodeNumber = i + 1;
                item.Monitored = true; item.QualityProfileId = league.QualityProfileId;
                if (i > 0) rig.Db.Events.Add(item);
            }
            await rig.Db.SaveChangesAsync();
            var client = await rig.Db.DownloadClients.SingleAsync();
            var group = Guid.NewGuid();
            rig.Owners = rig.Events.Select(item => new DownloadQueueItem { EventId = item.Id, Event = item,
                DownloadClientId = client.Id, DownloadClient = client, DownloadId = "owned-pack-job",
                Title = ReleaseTitle, IsPack = true, PackGroupId = group, Quality = "WEBDL-720p",
                Protocol = "Usenet", Status = DownloadStatus.Completed, CompletedAt = DateTime.UtcNow }).ToArray();
            rig.Db.DownloadQueue.AddRange(rig.Owners); await rig.Db.SaveChangesAsync();
            var library = (await rig.Db.RootFolders.SingleAsync()).Path;
            rig.Folder = Path.Combine(Path.GetDirectoryName(library)!, "incoming", ReleaseTitle);
            Directory.CreateDirectory(rig.Folder); return rig;
        }

        public async Task MemberAsync(int index, int length, string? tokens = null, string suffix = "")
        {
            var item = Events[index]; tokens ??= "{sportarr-" + item.ExternalId + "}";
            var name = "NFL." + item.EventDate.ToString("yyyy.MM.dd") + "." + item.Title.Replace(' ', '.')
                + ".720p.WEB-DL.H264." + tokens + suffix + ".mkv";
            var path = Path.Combine(Folder, name);
            var bytes = Enumerable.Repeat((byte)(65 + index), length).ToArray();
            await File.WriteAllBytesAsync(path, bytes); Paths[index] = path; Bytes[index] = bytes;
        }

        public Task<ImportHistory> ImportAsync(int index, PostImportMode mode = PostImportMode.Copy) =>
            _base.Services.GetRequiredService<FileImportService>()
                .ImportDownloadAsync(Owners[index], Folder, mode).WaitAsync(TimeSpan.FromSeconds(20));

        public async Task AssertOwnedBytesAsync(int index)
        {
            var file = await Db.EventFiles.SingleAsync(f => f.EventId == Events[index].Id);
            Assert.Equal(Bytes[index], await File.ReadAllBytesAsync(file.FilePath));
            Assert.Equal(DownloadStatus.Imported, Owners[index].Status);
            Assert.True(Events[index].HasFile); Assert.Equal(file.FilePath, Events[index].FilePath);
        }

        public async Task AssertHeldAsync(int index, bool requireWarning = true)
        {
            Assert.NotEqual(DownloadStatus.Imported, Owners[index].Status);
            Assert.False(Events[index].HasFile);
            Assert.False(await Db.EventFiles.AnyAsync(f => f.EventId == Events[index].Id));
            if (requireWarning) Assert.False(string.IsNullOrWhiteSpace(Owners[index].ErrorMessage));
        }

        public ValueTask DisposeAsync() => _base.DisposeAsync();
    }
}
