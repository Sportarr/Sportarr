using FluentAssertions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// A file match is reported only when the user chose the event and the
/// event and its league carry hub short ids. A match that came from a
/// Sportarr id is never reported, because the id already names the event.
/// </summary>
public class FileMatchSuggestionCollectorTests
{
    private const string FilePath = "/downloads/NFL.2025.Week.1.Panthers.vs.Browns.1080p.WEB.h264-GRP.mkv";
    private const string Stem = "NFL.2025.Week.1.Panthers.vs.Browns.1080p.WEB.h264-GRP";

    private static Event Evt(int id = 7, string? externalId = "ev-312923", string? leagueExternalId = "lg-000032",
        string? season = "2025") => new()
    {
        Id = id,
        Title = "Carolina Panthers vs Cleveland Browns",
        Sport = "American Football",
        Season = season,
        ExternalId = externalId,
        League = new League { Id = 3, Name = "NFL", Sport = "American Football", ExternalId = leagueExternalId },
    };

    private static PendingImport Pending(int confidence, int? suggestedEventId = 7) => new()
    {
        Id = 1,
        DownloadId = "disk-1",
        Title = Stem,
        FilePath = FilePath,
        SuggestedEventId = suggestedEventId,
        SuggestionConfidence = confidence,
    };

    [Fact]
    public void AKeptSuggestionIsConfirmed()
    {
        var suggestion = FileMatchSuggestionCollector.FromImport(FilePath, Evt(), suggestedEventId: 7, createNew: false);

        suggestion.Should().Be(new FileMatchSuggestion("lg-000032", "ev-312923",
            FileStemNormalizer.Normalize(Stem), Stem, "2025", "confirmed"));
    }

    [Fact]
    public void AnotherEventThanTheSuggestionIsManual()
    {
        var suggestion = FileMatchSuggestionCollector.FromImport(FilePath, Evt(), suggestedEventId: 9, createNew: false);

        suggestion!.Kind.Should().Be("manual");
    }

    [Fact]
    public void AnEventPickedForAnUnmatchedFileIsManual()
    {
        var suggestion = FileMatchSuggestionCollector.FromImport(FilePath, Evt(), suggestedEventId: null, createNew: false);

        suggestion!.Kind.Should().Be("manual");
    }

    [Fact]
    public void TheStemIsTheFileNameWithoutTheFolderOrTheExtension()
    {
        var suggestion = FileMatchSuggestionCollector.FromImport(FilePath, Evt(), 7, false)!;

        suggestion.StemDisplay.Should().Be(Stem);
        suggestion.StemNorm.Should().Be(FileStemNormalizer.Normalize(Stem));
    }

    [Fact]
    public void AnEventWithNoSeasonSendsNoSeason()
    {
        FileMatchSuggestionCollector.FromImport(FilePath, Evt(season: null), 7, false)!.Season.Should().BeNull();
    }

    [Fact]
    public void ANewEventIsNotReported()
    {
        FileMatchSuggestionCollector.FromImport(FilePath, Evt(), 7, createNew: true).Should().BeNull();
    }

    [Theory]
    [InlineData("/downloads/NFL Panthers vs Browns {sportarr-ev-312923}.mkv")]
    [InlineData("/downloads/NFL Panthers vs Browns sportarr-ev-312923.mkv")]
    [InlineData("/downloads/NFL Panthers vs Browns ev-312923.mkv")]
    [InlineData("/downloads/NFL Week 1 Pack {sportarr-lg-000032}.mkv")]
    public void AFileNamedWithASportarrIdIsNotReported(string path)
    {
        FileMatchSuggestionCollector.FromImport(path, Evt(), null, false).Should().BeNull();
    }

    [Fact]
    public void AFileWithASportarrIdInItsSidecarOrTagIsNotReported()
    {
        FileMatchSuggestionCollector.FromImport(FilePath, Evt(), null, false, carriedSportarrId: "ev-312923")
            .Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2336155")]
    [InlineData("ev-12")]
    [InlineData("lg-000032")]
    public void AnEventWithoutAHubShortIdIsNotReported(string? externalId)
    {
        FileMatchSuggestionCollector.FromImport(FilePath, Evt(externalId: externalId), 7, false).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4391")]
    [InlineData("ev-000032")]
    public void ALeagueWithoutAHubShortIdIsNotReported(string? leagueExternalId)
    {
        FileMatchSuggestionCollector.FromImport(FilePath, Evt(leagueExternalId: leagueExternalId), 7, false)
            .Should().BeNull();
    }

    [Fact]
    public void AnEventWithNoLeagueIsNotReported()
    {
        var evt = Evt();
        evt.League = null;

        FileMatchSuggestionCollector.FromImport(FilePath, evt, 7, false).Should().BeNull();
    }

    [Fact]
    public void AStemThatNormalizesToNothingIsNotReported()
    {
        FileMatchSuggestionCollector.FromImport("/downloads/1080p.WEB-DL.x264-GROUP.mkv", Evt(), 7, false).Should().BeNull();
    }

    [Fact]
    public void APendingImportTheUserEditedIsManual()
    {
        var import = Pending(confidence: FileMatchSuggestionCollector.UserChoiceConfidence);

        FileMatchSuggestionCollector.SuggestionBeforeUserChoice(import).Should().BeNull();
        FileMatchSuggestionCollector.FromAcceptedPendingImport(import, FilePath, Evt())!.Kind.Should().Be("manual");
    }

    [Fact]
    public void APendingImportAcceptedUnchangedIsConfirmed()
    {
        var import = Pending(confidence: 72);

        FileMatchSuggestionCollector.SuggestionBeforeUserChoice(import).Should().Be(7);
        FileMatchSuggestionCollector.FromAcceptedPendingImport(import, FilePath, Evt())!.Kind.Should().Be("confirmed");
    }

    [Fact]
    public void APendingImportWhoseReleaseNameCarriesAnIdIsNotReported()
    {
        var import = Pending(confidence: 72);
        import.Title = "NFL Panthers vs Browns {sportarr-ev-312923}";

        FileMatchSuggestionCollector.FromAcceptedPendingImport(import, FilePath, Evt()).Should().BeNull();
    }
}
