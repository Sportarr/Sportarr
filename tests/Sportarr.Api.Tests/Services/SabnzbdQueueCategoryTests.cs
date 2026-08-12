using System.Text.Json;
using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// SABnzbd names the same concept differently on its two endpoints: the
/// queue sends "cat", the history sends "category". Sportarr bound only
/// "category", so every queued item deserialized with an empty category
/// and then failed the category filter that identifies Sportarr's own
/// downloads. Queue progress was never tracked, and the download was
/// dropped after ten checks (issue #227).
/// </summary>
public class SabnzbdQueueCategoryTests
{
    [Fact]
    public void QueueSlot_BindsCategory_FromSabnzbdCatField()
    {
        var json = """
        [{
            "nzo_id": "SABnzbd_nzo_abc123",
            "filename": "UFC.330.1080p.WEB.h264-SPORTY",
            "status": "Downloading",
            "mb": "1277.65",
            "mbleft": "412.10",
            "percentage": "67",
            "timeleft": "0:03:21",
            "cat": "sportarr"
        }]
        """;

        var items = JsonSerializer.Deserialize<List<SabnzbdItem>>(json);

        items.Should().ContainSingle();
        items![0].CategoryName.Should().Be("sportarr");
    }

    [Fact]
    public void QueueSlot_StillBindsCategory_WhenSabnzbdSendsTheLongName()
    {
        var json = """
        [{
            "nzo_id": "SABnzbd_nzo_abc123",
            "filename": "UFC.330.1080p.WEB.h264-SPORTY",
            "status": "Downloading",
            "category": "sportarr"
        }]
        """;

        var items = JsonSerializer.Deserialize<List<SabnzbdItem>>(json);

        items![0].CategoryName.Should().Be("sportarr");
    }

    [Fact]
    public void QueueSlot_CategoryIsEmpty_WhenSabnzbdSendsNeitherField()
    {
        var json = """
        [{ "nzo_id": "SABnzbd_nzo_abc123", "filename": "Some.Release" }]
        """;

        var items = JsonSerializer.Deserialize<List<SabnzbdItem>>(json);

        items![0].CategoryName.Should().BeEmpty();
    }
}
