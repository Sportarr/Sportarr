using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The guide is parsed as a stream. The old whole-buffer path held the
/// download, a decompressed copy, a UTF-16 string and a full XML document at
/// once, and its gzip branch had no ceiling at all, so a small archive that
/// opened into gigabytes was materialised twice. These tests pin the
/// streaming behaviours that replaced it.
/// </summary>
public class XmltvParserServiceTests
{
    private sealed class PassthroughHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static XmltvParserService NewParser() => new(
        NullLogger<XmltvParserService>.Instance,
        new PassthroughHttpClientFactory());

    private static byte[] Gzip(string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
        using (var sw = new StreamWriter(gz, Encoding.UTF8))
        {
            sw.Write(text);
        }
        return ms.ToArray();
    }

    private static async Task<(XmltvStreamResult Result, List<XmltvChannel> Channels, List<EpgProgram> Programs)>
        ParseAsync(byte[] bytes, string url)
    {
        var channels = new List<XmltvChannel>();
        var programs = new List<EpgProgram>();
        using var stream = new MemoryStream(bytes);

        var result = await NewParser().StreamParseAsync(
            stream, epgSourceId: 1, url, charSet: null,
            onChannels: batch => { channels.AddRange(batch); return Task.CompletedTask; },
            onPrograms: batch => { programs.AddRange(batch); return Task.CompletedTask; });

        return (result, channels, programs);
    }

    private static string Programme(int i) =>
        $"<programme start=\"20990101{i / 3600 % 24:D2}{i / 60 % 60:D2}{i % 60:D2} +0000\" " +
        $"stop=\"20990102000000 +0000\" channel=\"c{i}\"><title>Event {i}</title></programme>";

    [Fact]
    public async Task Gzip_without_a_gz_extension_is_still_decompressed()
    {
        var gz = Gzip("<tv><channel id=\"1\"><display-name>ESPN</display-name></channel></tv>");

        // URL has no .gz suffix; the gzip magic bytes must be enough.
        var (result, channels, _) = await ParseAsync(gz, "http://provider.example/epg");

        result.Success.Should().BeTrue(result.Error);
        channels.Should().ContainSingle(c => c.DisplayName == "ESPN");
    }

    [Fact]
    public async Task Adjacent_elements_are_all_read()
    {
        // XNode.ReadFrom leaves the reader on the following sibling. A loop
        // that advanced unconditionally after it read every second element
        // and silently dropped half the guide.
        var xml = "<tv>" +
            string.Concat(Enumerable.Range(0, 7).Select(i => $"<channel id=\"c{i}\"><display-name>Ch {i}</display-name></channel>")) +
            string.Concat(Enumerable.Range(0, 9).Select(Programme)) +
            "</tv>";

        var (result, channels, programs) = await ParseAsync(Encoding.UTF8.GetBytes(xml), "http://provider.example/epg.xml");

        result.Success.Should().BeTrue(result.Error);
        channels.Should().HaveCount(7);
        programs.Should().HaveCount(9);
        result.ChannelCount.Should().Be(7);
        result.ProgramCount.Should().Be(9);
    }

    [Fact]
    public async Task Batches_are_handed_over_before_the_document_ends()
    {
        // More programmes than one batch holds, so the first hand-off has to
        // happen mid-document. That is the property that keeps the guide from
        // ever existing whole in memory.
        var count = XmltvParserService.StreamBatchSize + 25;
        var xml = "<tv>" + string.Concat(Enumerable.Range(0, count).Select(Programme)) + "</tv>";

        var batchSizes = new List<int>();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var result = await NewParser().StreamParseAsync(
            stream, epgSourceId: 1, "http://provider.example/epg.xml", charSet: null,
            onChannels: _ => Task.CompletedTask,
            onPrograms: batch => { batchSizes.Add(batch.Count); return Task.CompletedTask; });

        result.Success.Should().BeTrue(result.Error);
        batchSizes.Should().HaveCount(2);
        batchSizes[0].Should().Be(XmltvParserService.StreamBatchSize);
        batchSizes.Sum().Should().Be(count);
    }

    [Fact]
    public async Task A_document_without_a_tv_root_is_rejected()
    {
        var (result, _, _) = await ParseAsync(
            Encoding.UTF8.GetBytes("<guide><programme/></guide>"),
            "http://provider.example/epg.xml");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("tv");
    }

    [Fact]
    public async Task A_guide_that_expands_past_the_ceiling_is_refused_not_held()
    {
        // A small gzip download that inflates past the decompressed limit.
        // The old path bounded only the compressed bytes, so this was read
        // into memory whole, twice. The test writes the gzip as a stream for
        // the same reason the parser reads one: the expanded form must never
        // exist in this process.
        var filler = new string('x', 4096);
        using var compressed = new MemoryStream();
        using (var gz = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
        using (var sw = new StreamWriter(gz, Encoding.UTF8))
        {
            sw.Write("<tv>");
            for (var i = 0; i < 135_000; i++)
            {
                sw.Write($"<channel id=\"c{i}\"><display-name>{filler}</display-name></channel>");
            }
            sw.Write("</tv>");
        }
        compressed.Length.Should().BeLessThan(64 * 1024 * 1024, "the point is a small download that expands");

        var (result, _, _) = await ParseAsync(compressed.ToArray(), "http://provider.example/epg.gz");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("limit");
    }
}
