using System.Text.Json;
using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

public class SabnzbdHistoryCategoryTests
{
    [Fact]
    public void HistorySlot_BindsCategory_FromSabnzbdCategoryField()
    {
        var json = """
        [{
            "nzo_id": "5ef134ef-4425-4044-a619-acb8dc9e6440",
            "name": "EPL.26-27-Matchday.1-Arsenal.FC.vs.Coventry.City.2160p.4K.UHD-SDX",
            "status": "Completed",
            "category": "uncategorized"
        }]
        """;

        var items = JsonSerializer.Deserialize<List<SabnzbdHistoryItem>>(json);

        items.Should().ContainSingle();
        items![0].CategoryName.Should().Be("uncategorized");
    }

    [Fact]
    public void HistorySlot_BindsCategory_FromSabnzbdCatField()
    {
        var json = """
        [{
            "nzo_id": "SABnzbd_nzo_abc123",
            "name": "UFC.330.1080p.WEB.h264-SPORTY",
            "status": "Completed",
            "cat": "sports"
        }]
        """;

        var items = JsonSerializer.Deserialize<List<SabnzbdHistoryItem>>(json);

        items![0].CategoryName.Should().Be("sports");
    }
}
