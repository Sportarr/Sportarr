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

        var league = new League { Name = "UFC", Sport = "Fighting" };
        var evt = new Event
        {
            Title = "UFC 317 - Main Card",
            Sport = "Fighting",
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
}
