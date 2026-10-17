using System.Xml.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Local Kodi NFO/thumb writer. Covers the two regressions this class exists
/// to prevent: writing an &lt;episodeguide&gt; tag (confirmed to corrupt
/// Kodi's local-only scrape - see MetadataWriterService's class comment) and
/// silently doing nothing when a provider is enabled but no episode number
/// has been assigned yet.
/// </summary>
public class MetadataWriterServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SportarrDbContext _db;
    private readonly MetadataWriterService _service;

    public MetadataWriterServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sportarr-metadata-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);

        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new SportarrDbContext(options);

        var httpClientFactory = new Mock<IHttpClientFactory>();
        _service = new MetadataWriterService(_db, httpClientFactory.Object, Mock.Of<ILogger<MetadataWriterService>>());
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private async Task<MetadataProvider> AddEnabledKodiProviderAsync()
    {
        var provider = new MetadataProvider
        {
            Name = "Kodi/XBMC",
            Type = MetadataType.Kodi,
            Enabled = true,
            EventNfo = true,
            EventImages = false
        };
        _db.MetadataProviders.Add(provider);
        await _db.SaveChangesAsync();
        return provider;
    }

    private (Event Event, EventFile File) MakeEventAndFile(string fileName)
    {
        var videoPath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(videoPath, "video");

        var league = new League { Name = "UFC", Sport = "Fighting", ExternalId = "lg-004463" };
        var evt = new Event
        {
            Title = "UFC 317 - Main Card",
            Sport = "Fighting",
            ExternalId = "ev-2338110",
            League = league,
            SeasonNumber = 2026,
            EpisodeNumber = 12,
            EventDate = new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc),
        };
        var file = new EventFile { EventId = evt.Id, FilePath = videoPath };
        return (evt, file);
    }

    [Fact]
    public async Task WriteEventMetadataAsync_NoEnabledProvider_WritesNothing()
    {
        var (evt, file) = MakeEventAndFile("no-provider.mkv");

        await _service.WriteEventMetadataAsync(evt, file, evt.League);

        File.Exists(Path.ChangeExtension(file.FilePath, ".nfo")).Should().BeFalse();
    }

    [Fact]
    public async Task WriteEventMetadataAsync_NoEpisodeNumber_WritesNothing()
    {
        await AddEnabledKodiProviderAsync();
        var (evt, file) = MakeEventAndFile("no-episode.mkv");
        evt.EpisodeNumber = null;

        await _service.WriteEventMetadataAsync(evt, file, evt.League);

        File.Exists(Path.ChangeExtension(file.FilePath, ".nfo")).Should().BeFalse();
    }

    [Fact]
    public async Task WriteEventMetadataAsync_WritesNfoAtVideoBasename_NeverIncludingEpisodeGuide()
    {
        await AddEnabledKodiProviderAsync();
        var (evt, file) = MakeEventAndFile("UFC 317 - Main Card.mkv");

        await _service.WriteEventMetadataAsync(evt, file, evt.League);

        var nfoPath = Path.ChangeExtension(file.FilePath, ".nfo");
        File.Exists(nfoPath).Should().BeTrue();

        var doc = XDocument.Load(nfoPath);
        doc.Root!.Name.LocalName.Should().Be("episodedetails");
        doc.Root.Element("title")!.Value.Should().Be(evt.Title);
        doc.Root.Element("season")!.Value.Should().Be("2026");
        doc.Root.Element("episode")!.Value.Should().Be("12");

        // The regression this test exists to catch: an <episodeguide><url>
        // tag makes Kodi try to resolve it online and corrupts the local
        // scrape (confirmed Sonarr/Radarr issue pattern).
        doc.Root.Element("episodeguide").Should().BeNull();

        // The event's own id rides in the nfo the way a tvdb id does, so
        // the library keeps the id whatever the numbers do.
        var uniqueId = doc.Root.Element("uniqueid");
        uniqueId.Should().NotBeNull();
        uniqueId!.Attribute("type")!.Value.Should().Be("sportarr");
        uniqueId.Attribute("default")!.Value.Should().Be("true");
        uniqueId.Value.Should().Be("ev-2338110");

    }

    [Fact]
    public async Task WriteLeagueMetadataAsync_WritesTheLeagueIdIntoTheShowNfo()
    {
        await AddEnabledKodiProviderAsync();
        var seasonDir = Path.Combine(_tempDir, "UFC", "Season 2026");
        Directory.CreateDirectory(seasonDir);
        var (evt, file) = MakeEventAndFile(Path.Combine("UFC", "Season 2026", "UFC 317 - Main Card.mkv"));
        file.Exists = true;
        file.Event = evt;
        _db.Events.Add(evt);
        _db.EventFiles.Add(file);
        await _db.SaveChangesAsync();

        await _service.WriteLeagueMetadataAsync(evt.League!);

        var show = XDocument.Load(Path.Combine(_tempDir, "UFC", "tvshow.nfo"));
        show.Root!.Name.LocalName.Should().Be("tvshow");
        var uniqueId = show.Root.Element("uniqueid");
        uniqueId.Should().NotBeNull();
        uniqueId!.Attribute("type")!.Value.Should().Be("sportarr");
        uniqueId.Value.Should().Be("lg-004463");
    }

    [Fact]
    public async Task DeleteEventMetadataAsync_RemovesNfoAndThumbSidecars()
    {
        await AddEnabledKodiProviderAsync();
        var (evt, file) = MakeEventAndFile("to-delete.mkv");
        await _service.WriteEventMetadataAsync(evt, file, evt.League);
        var nfoPath = Path.ChangeExtension(file.FilePath, ".nfo");
        File.Exists(nfoPath).Should().BeTrue();

        await _service.DeleteEventMetadataAsync(file);

        File.Exists(nfoPath).Should().BeFalse();
    }

    [Fact]
    public async Task RenameEventMetadataAsync_MovesNfoToNewBasename()
    {
        await AddEnabledKodiProviderAsync();
        var (evt, file) = MakeEventAndFile("Old Name.mkv");
        await _service.WriteEventMetadataAsync(evt, file, evt.League);
        var oldNfo = Path.ChangeExtension(file.FilePath, ".nfo");
        var newVideoPath = Path.Combine(_tempDir, "New Name.mkv");
        var newNfo = Path.ChangeExtension(newVideoPath, ".nfo");

        await _service.RenameEventMetadataAsync(file.FilePath, newVideoPath);

        File.Exists(oldNfo).Should().BeFalse();
        File.Exists(newNfo).Should().BeTrue();
    }

    [Fact]
    public async Task RenameEventMetadataAsync_MovesOnlyMatchingSubtitleSidecars()
    {
        var (_, file) = MakeEventAndFile("Old Name.mkv");
        var matching = Path.Combine(_tempDir, "Old Name.en.forced.srt");
        var unrelated = Path.Combine(_tempDir, "Old Name Extended.en.srt");
        File.WriteAllText(matching, "matching");
        File.WriteAllText(unrelated, "unrelated");

        await _service.RenameEventMetadataAsync(file.FilePath, Path.Combine(_tempDir, "New Name.mkv"));

        File.Exists(matching).Should().BeFalse();
        File.ReadAllText(Path.Combine(_tempDir, "New Name.en.forced.srt")).Should().Be("matching");
        File.ReadAllText(unrelated).Should().Be("unrelated");
    }

    [Fact]
    public async Task RenameEventMetadataAsync_PreservesConflictingDestinationSubtitle()
    {
        var (_, file) = MakeEventAndFile("Old Name.mkv");
        File.WriteAllText(Path.Combine(_tempDir, "Old Name.en.srt"), "new subtitle");
        File.WriteAllText(Path.Combine(_tempDir, "New Name.en.srt"), "old destination");

        await _service.RenameEventMetadataAsync(file.FilePath, Path.Combine(_tempDir, "New Name.mkv"));

        File.ReadAllText(Path.Combine(_tempDir, "New Name.en.srt")).Should().Be("new subtitle");
        Directory.GetFiles(Path.Combine(_tempDir, ".sportarr-conflicts"))
            .Select(File.ReadAllText).Should().ContainSingle().Which.Should().Be("old destination");
    }

    [Fact]
    public async Task RenameEventMetadataAsync_RenamesSubtitleWhenOnlyCaseChanges()
    {
        var (_, file) = MakeEventAndFile("old name.mkv");
        var original = Path.Combine(_tempDir, "old name.en.srt");
        var expected = Path.Combine(_tempDir, "Old Name.en.srt");
        File.WriteAllText(original, "subtitle");

        await _service.RenameEventMetadataAsync(file.FilePath, Path.Combine(_tempDir, "Old Name.mkv"));

        File.ReadAllText(expected).Should().Be("subtitle");
        Directory.Exists(Path.Combine(_tempDir, ".sportarr-conflicts")).Should().BeFalse();
    }

    [Fact]
    public async Task RenameEventMetadataAsync_DoesNotTakeAnotherVideosSubtitle()
    {
        var (_, file) = MakeEventAndFile("Name.mkv");
        File.WriteAllText(Path.Combine(_tempDir, "Name.en.srt"), "mine");
        File.WriteAllText(Path.Combine(_tempDir, "Name.Part2.mkv"), "video");
        File.WriteAllText(Path.Combine(_tempDir, "Name.Part2.en.srt"), "sibling");

        await _service.RenameEventMetadataAsync(file.FilePath, Path.Combine(_tempDir, "Renamed.mkv"));

        File.ReadAllText(Path.Combine(_tempDir, "Renamed.en.srt")).Should().Be("mine");
        File.ReadAllText(Path.Combine(_tempDir, "Name.Part2.en.srt")).Should().Be("sibling");
    }

    [Fact]
    public async Task DeleteEventMetadataAsync_DoesNotDeleteAnotherVideosSubtitle()
    {
        var (_, file) = MakeEventAndFile("Name.mkv");
        File.WriteAllText(Path.Combine(_tempDir, "Name.Part2.mkv"), "video");
        File.WriteAllText(Path.Combine(_tempDir, "Name.Part2.en.srt"), "sibling");

        await _service.DeleteEventMetadataAsync(file);

        File.ReadAllText(Path.Combine(_tempDir, "Name.Part2.en.srt")).Should().Be("sibling");
    }

    [Fact]
    public async Task DeleteEventMetadataAsync_RemovesOnlyMatchingSubtitleSidecars()
    {
        var (_, file) = MakeEventAndFile("To Delete.mkv");
        var matching = Path.Combine(_tempDir, "To Delete.en.ass");
        var unrelated = Path.Combine(_tempDir, "To Delete Extended.en.ass");
        File.WriteAllText(matching, "matching");
        File.WriteAllText(unrelated, "unrelated");

        await _service.DeleteEventMetadataAsync(file);

        File.Exists(matching).Should().BeFalse();
        File.ReadAllText(unrelated).Should().Be("unrelated");
    }

    [Fact]
    public async Task DeleteSubtitleSidecarsAsync_KeepsTheReplacementMetadata()
    {
        var (_, file) = MakeEventAndFile("Race.mkv");
        var subtitle = Path.Combine(_tempDir, "Race.en.srt");
        var nfo = Path.Combine(_tempDir, "Race.nfo");
        File.WriteAllText(subtitle, "old subtitle");
        File.WriteAllText(nfo, "new metadata");

        await _service.DeleteSubtitleSidecarsAsync(file.FilePath);

        File.Exists(subtitle).Should().BeFalse();
        File.ReadAllText(nfo).Should().Be("new metadata");
    }

    [Fact]
    public async Task DeleteEventMetadataAsync_RecyclesSubtitleWithVideo()
    {
        var (_, file) = MakeEventAndFile("Old Name.mkv");
        var matching = Path.Combine(_tempDir, "Old Name.en.srt");
        File.WriteAllText(matching, "subtitle");
        var recycledVideo = Path.Combine(_tempDir, "recycle", "20260929_120000_Old Name.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(recycledVideo)!);

        await _service.DeleteEventMetadataAsync(file, recycledVideo);

        File.Exists(matching).Should().BeFalse();
        File.ReadAllText(Path.Combine(_tempDir, "recycle", "20260929_120000_Old Name.en.srt"))
            .Should().Be("subtitle");
    }

    [Fact]
    public async Task DeleteSubtitleSidecarsAsync_RecyclesSubtitleAfterVideoIsGone()
    {
        var (_, file) = MakeEventAndFile("Old Name.mkv");
        var matching = Path.Combine(_tempDir, "Old Name.en.srt");
        File.WriteAllText(matching, "subtitle");
        File.Delete(file.FilePath);
        var recycledVideo = Path.Combine(_tempDir, "recycle", "20260929_120000_Old Name.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(recycledVideo)!);

        await _service.DeleteSubtitleSidecarsAsync(file.FilePath, recycledVideo);

        File.Exists(matching).Should().BeFalse();
        File.ReadAllText(Path.Combine(_tempDir, "recycle", "20260929_120000_Old Name.en.srt"))
            .Should().Be("subtitle");
    }

    [Fact]
    public async Task RenameEventMetadataAsync_DoesNotMoveAnotherCaseSensitiveFile()
    {
        if (OperatingSystem.IsWindows())
            return;

        var (_, file) = MakeEventAndFile("Old Name.mkv");
        var unrelated = Path.Combine(_tempDir, "old name.en.srt");
        File.WriteAllText(unrelated, "other file");

        await _service.RenameEventMetadataAsync(file.FilePath, Path.Combine(_tempDir, "New Name.mkv"));

        File.ReadAllText(unrelated).Should().Be("other file");
        File.Exists(Path.Combine(_tempDir, "New Name.en.srt")).Should().BeFalse();
    }
}
