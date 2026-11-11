using FluentAssertions;
using Sportarr.Api.Services.Import;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The library scan reads the metadata file that sits beside a video.
/// The file is untrusted input, so the reader refuses anything odd.
/// </summary>
public class NfoSidecarReaderTests : IDisposable
{
    private readonly string _root;

    public NfoSidecarReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sportarr-nfo-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Video(string nfo)
    {
        var video = Path.Combine(_root, "Formula 1 - S2024E05 - Sakhir.mkv");
        File.WriteAllBytes(video, new byte[16]);
        File.WriteAllText(Path.ChangeExtension(video, ".nfo"), nfo);
        return video;
    }

    [Fact]
    public void ReadsAiredTitleAndSportarrId()
    {
        // The same shape that the metadata writer makes.
        var video = Video("""
            <?xml version="1.0" encoding="UTF-8"?>
            <episodedetails>
              <title>Bahrain Grand Prix - Race</title>
              <showtitle>Formula 1</showtitle>
              <season>2024</season>
              <episode>10</episode>
              <aired>2024-03-02</aired>
              <uniqueid type="sportarr" default="true">ev-174218</uniqueid>
              <genre>Motorsport</genre>
            </episodedetails>
            """);

        var sidecar = NfoSidecarReader.TryRead(video);

        sidecar.Should().NotBeNull();
        sidecar!.Aired.Should().Be(new DateTime(2024, 3, 2));
        sidecar.Title.Should().Be("Bahrain Grand Prix - Race");
        sidecar.SportarrId.Should().Be("ev-174218");
    }

    [Fact]
    public void PremieredIsUsedWhenAiredIsMissing()
    {
        var video = Video("<movie><title>UFC 300</title><premiered>2024-04-13</premiered></movie>");

        var sidecar = NfoSidecarReader.TryRead(video);

        sidecar.Should().NotBeNull();
        sidecar!.Aired.Should().Be(new DateTime(2024, 4, 13));
        sidecar.Title.Should().Be("UFC 300");
        sidecar.SportarrId.Should().BeNull();
    }

    [Fact]
    public void MalformedXmlReturnsNull()
    {
        var video = Video("<episodedetails><title>Bahrain</aired>");

        NfoSidecarReader.TryRead(video).Should().BeNull();
    }

    [Fact]
    public void MissingFileReturnsNull()
    {
        var video = Path.Combine(_root, "no sidecar.mkv");
        File.WriteAllBytes(video, new byte[16]);

        NfoSidecarReader.TryRead(video).Should().BeNull();
    }

    [Fact]
    public void ADocumentTypeDeclarationIsRefused()
    {
        var secret = Path.Combine(_root, "secret.txt");
        File.WriteAllText(secret, "2024-03-02");
        var video = Video($"""
            <?xml version="1.0"?>
            <!DOCTYPE episodedetails [ <!ENTITY leak SYSTEM "file://{secret}"> ]>
            <episodedetails><title>&leak;</title><aired>2024-03-02</aired></episodedetails>
            """);

        NfoSidecarReader.TryRead(video).Should().BeNull();
    }

    [Fact]
    public void AFileLargerThanOneMegabyteIsSkipped()
    {
        var padding = new string(' ', 1024 * 1024);
        var video = Video($"<episodedetails><title>Bahrain</title><aired>2024-03-02</aired>{padding}</episodedetails>");

        NfoSidecarReader.TryRead(video).Should().BeNull();
    }

    [Fact]
    public void AnotherRootElementReturnsNull()
    {
        var video = Video("<tvshow><title>Formula 1</title><premiered>1950-05-13</premiered></tvshow>");

        NfoSidecarReader.TryRead(video).Should().BeNull();
    }

    [Fact]
    public void OnlyASportarrEventIdIsRead()
    {
        var video = Video("""
            <episodedetails>
              <uniqueid type="tvdb" default="true">10000001</uniqueid>
              <uniqueid type="sportarr">lg-000278</uniqueid>
            </episodedetails>
            """);

        var sidecar = NfoSidecarReader.TryRead(video);

        sidecar.Should().NotBeNull();
        sidecar!.SportarrId.Should().BeNull();
        sidecar.Aired.Should().BeNull();
        sidecar.Title.Should().BeNull();
    }

    [Fact]
    public void AnUnreadableDateIsIgnored()
    {
        var video = Video("<episodedetails><title>Bahrain</title><aired>soon</aired></episodedetails>");

        var sidecar = NfoSidecarReader.TryRead(video);

        sidecar.Should().NotBeNull();
        sidecar!.Aired.Should().BeNull();
        sidecar.Title.Should().Be("Bahrain");
    }
}
