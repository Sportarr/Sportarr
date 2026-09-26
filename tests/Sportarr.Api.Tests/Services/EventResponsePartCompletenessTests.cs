using FluentAssertions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Services;

public class EventResponsePartCompletenessTests
{
    [Fact]
    public void PartialCardShowsItsFileWithoutCallingTheEventComplete()
    {
        var evt = new Event
        {
            Title = "UFC 9999", Sport = "Fighting", Monitored = true, HasFile = false,
            League = new League { Name = "UFC", Sport = "Fighting", MonitoredParts = "Main Card,Prelims" },
            Files = new List<EventFile>
            {
                new() { FilePath = "/library/main.mkv", PartName = "Main Card", PartNumber = 3, Exists = true }
            }
        };

        var response = EventResponse.FromEvent(evt, enableMultiPartEpisodes: true, filesLoaded: true);

        response.HasFile.Should().BeFalse();
        response.Files.Should().ContainSingle().Which.PartName.Should().Be("Main Card");
    }

    [Fact]
    public void CompleteCardRecoversFromAStaleStoredFlag()
    {
        var evt = new Event
        {
            Title = "UFC 9999", Sport = "Fighting", Monitored = true, HasFile = false,
            League = new League { Name = "UFC", Sport = "Fighting", MonitoredParts = "Main Card,Prelims" },
            Files = new List<EventFile>
            {
                new() { FilePath = "/library/main.mkv", PartName = "Main Card", PartNumber = 3, Exists = true },
                new() { FilePath = "/library/prelims.mkv", PartName = "Prelims", PartNumber = 2, Exists = true }
            }
        };

        EventResponse.FromEvent(evt, enableMultiPartEpisodes: true, filesLoaded: true).HasFile.Should().BeTrue();
    }

    [Fact]
    public void NewlyMonitoredMissingPartOverridesAStaleCompleteFlag()
    {
        var evt = new Event
        {
            Title = "UFC 9999", Sport = "Fighting", Monitored = true, HasFile = true,
            League = new League { Name = "UFC", Sport = "Fighting", MonitoredParts = "Main Card,Prelims" },
            Files = new List<EventFile>
            {
                new() { FilePath = "/library/main.mkv", PartName = "Main Card", PartNumber = 3, Exists = true }
            }
        };

        EventResponse.FromEvent(evt, enableMultiPartEpisodes: true, filesLoaded: true).HasFile.Should().BeFalse();
    }

    [Fact]
    public void LoadedEmptyFileListOverridesAStaleCompleteFlag()
    {
        var evt = new Event
        {
            Title = "UFC 9999", Sport = "Fighting", HasFile = true,
            League = new League { Name = "UFC", Sport = "Fighting", MonitoredParts = "Main Card" },
            Files = new List<EventFile>()
        };

        EventResponse.FromEvent(evt, enableMultiPartEpisodes: true, filesLoaded: true).HasFile.Should().BeFalse();
    }

    [Fact]
    public void SingleFileEventRecoversFromAStaleStoredFlag()
    {
        var evt = new Event
        {
            Title = "Rangers vs Devils", Sport = "Ice Hockey", HasFile = false,
            Files = new List<EventFile> { new() { FilePath = "/library/game.mkv", Exists = true } }
        };

        EventResponse.FromEvent(evt, enableMultiPartEpisodes: true, filesLoaded: true).HasFile.Should().BeTrue();
    }

    [Fact]
    public void DisabledMultiPartTreatsOneStoredPartAsComplete()
    {
        var evt = new Event
        {
            Title = "UFC 9999", Sport = "Fighting", HasFile = false,
            Files = new List<EventFile>
            {
                new() { FilePath = "/library/main.mkv", PartName = "Main Card", PartNumber = 3, Exists = true }
            }
        };

        EventResponse.FromEvent(evt, enableMultiPartEpisodes: false, filesLoaded: true).HasFile.Should().BeTrue();
    }

    [Fact]
    public void SuppliedLeaguePreservesTheMonitoredPartOverride()
    {
        var league = new League { Name = "UFC", Sport = "Fighting", MonitoredParts = "Main Card" };
        var evt = new Event
        {
            Title = "UFC 9999", Sport = "Fighting", HasFile = false,
            Files = new List<EventFile>
            {
                new() { FilePath = "/library/main.mkv", PartName = "Main Card", PartNumber = 3, Exists = true }
            }
        };

        var response = EventResponse.FromEvent(evt, enableMultiPartEpisodes: true, filesLoaded: true, leagueOverride: league);

        response.HasFile.Should().BeTrue();
        response.PartStatuses.Should().ContainSingle(p => p.PartName == "Early Prelims" && !p.Monitored);
        response.PartStatuses.Should().ContainSingle(p => p.PartName == "Main Card" && p.Monitored && p.Downloaded);
    }

    [Fact]
    public void UnloadedFileListUsesStoredFlag()
    {
        var evt = new Event
        {
            Title = "UFC 9999", Sport = "Fighting", HasFile = true,
            Files = new List<EventFile>()
        };

        EventResponse.FromEvent(evt, enableMultiPartEpisodes: true, filesLoaded: false)
            .HasFile.Should().BeTrue();
    }
}
