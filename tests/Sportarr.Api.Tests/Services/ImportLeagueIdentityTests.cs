using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Regression: the import side had no league-identity check for scene-style filenames.
///
/// LibraryImportService.CalculateMatchConfidence gated on SeriesLabelMatchesLeague, but the
/// series label is only present in library-format names ("V8 Supercars - S2026E19 - …"). A raw
/// scene name — "BSB.2026.Round06.Oulton.Park.International.Race.One…" — yields no label, so that
/// gate was skipped and nothing else established WHICH series the file belonged to: round, session
/// and year agree across every series running the same weekend format.
///
/// The grab side already rejected this (a British Superbike round was otherwise matched onto the
/// F1 Monaco GP, same Round 6, and imported over the correct file as a "quality upgrade"). These
/// pin the equivalent gate on the import side, and pin the shared helper both sides now use so
/// they cannot drift apart.
/// </summary>
public class ImportLeagueIdentityTests
{
    private static League F1() => new() { Id = 2, Name = "Formula 1", Sport = "Motorsport" };

    private static Event MonacoRace() => new()
    {
        Id = 1788,
        Title = "Monaco Grand Prix - Race",
        Sport = "Motorsport",
        Round = "6",
        Season = "2026",
        EventDate = new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc),
        League = F1(),
        LeagueId = 2,
    };

    [Theory]
    // The exact titles that were mis-imported as Monaco GP.
    [InlineData("BSB.2026.Round06.Oulton.Park.International.Race.One.TNT.WEB-DL.1080p.H264.DDP5.1.English-MWR")]
    [InlineData("BSB.2026.Round06.Oulton.Park.International.Qualifying.TNT.WEB-DL.1080p.H264.DDP5.1.English-MWR")]
    // Same shape, other series that share F1 race weekends and round numbering.
    [InlineData("PorscheSupercup.2026.Round06.La.Course.1080p.WEB-DL")]
    [InlineData("DTM.2026.Round06.Race.One.1080p.WEB-DL")]
    public void SceneFilenameNamingAnotherSeries_IsRejected(string filename)
    {
        LibraryImportService.CalculateMatchConfidence(
            searchTitle: filename,
            eventTitle: "Monaco Grand Prix - Race",
            organization: null,
            evt: MonacoRace(),
            parsedDate: null,
            parsedYear: 2026,
            parsedRoundNumber: 6)
        .Should().Be(0);
    }

    [Theory]
    // Names the league outright.
    [InlineData("Formula1.2026.Monaco.Grand.Prix.1080p.WEB.h264-BILLIE")]
    [InlineData("Formula1.2026.Round06.Monaco.Race.F1TV.WEB-DL.1080p.h264.English-MWR")]
    // Underscore-separated: NormalizeTitle collapses '_' so identity still resolves.
    [InlineData("Formula_1_2026_Race_06_Monaco_Grand_Prix_Race_1080pEN50fps_F1TV")]
    // Does not name the league, but shares the event's distinctive word ("Monaco").
    [InlineData("Monaco.Grand.Prix.2026.Race.1080p.WEB-DL")]
    public void GenuineF1File_IsNotRejectedByTheGate(string filename)
    {
        LibraryImportService.CalculateMatchConfidence(
            searchTitle: filename,
            eventTitle: "Monaco Grand Prix - Race",
            organization: null,
            evt: MonacoRace(),
            parsedDate: null,
            parsedYear: 2026,
            parsedRoundNumber: 6)
        .Should().BeGreaterThan(0);
    }

    [Fact]
    public void TeamSportEvent_IsExemptFromTheGate()
    {
        // Team-sport filenames name the teams, never the league, so the gate must not fire.
        var evt = new Event
        {
            Id = 900,
            Title = "Los Angeles Lakers vs Boston Celtics",
            Sport = "Basketball",
            EventDate = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Id = 9, Name = "NBA", Sport = "Basketball" },
            LeagueId = 9,
            HomeTeamName = "Los Angeles Lakers",
            AwayTeamName = "Boston Celtics",
        };

        LibraryImportService.CalculateMatchConfidence(
            searchTitle: "Los.Angeles.Lakers.vs.Boston.Celtics.2026.03.05.1080p.WEB-DL",
            eventTitle: evt.Title,
            organization: null,
            evt: evt,
            parsedDate: new DateTime(2026, 3, 5),
            parsedYear: 2026)
        .Should().BeGreaterThan(0);
    }

    [Theory]
    // The shared helper both gates now call.
    [InlineData("BSB.2026.Round06.Oulton.Park.International.Race.One-MWR", false)]
    [InlineData("Formula1.2026.Monaco.Grand.Prix.1080p.WEB.h264-BILLIE", true)]
    [InlineData("Monaco.Grand.Prix.2026.Race.1080p", true)]   // distinctive word only
    [InlineData("Round.06.Race.2026.1080p", false)]           // pure slot signals, no identity
    public void TitleHasLeagueIdentity_MatchesTheGateDecision(string title, bool expected)
    {
        ReleaseMatchingService.TitleHasLeagueIdentity(title, "Monaco Grand Prix - Race", F1())
            .Should().Be(expected);
    }

    // Regression: the shared helper both TitleHasLeagueIdentity (via TitleNamesLeague)
    // and the import scorer's series-label gate call must consult League.UserAliases,
    // not just the upstream AlternateName field. A release/import filename naming
    // only a user-defined alias must not be rejected on the import side either.
    [Fact]
    public void ImportScorer_AcceptsSeriesLabelNamedOnlyByUserAlias()
    {
        var f1WithUserAlias = new League { Id = 2, Name = "Formula 1", Sport = "Motorsport", UserAliases = "F1TV Feed" };
        var evt = MonacoRace();
        evt.League = f1WithUserAlias;

        LibraryImportService.CalculateMatchConfidence(
            searchTitle: "betr Monaco Grand Prix",
            eventTitle: "Monaco Grand Prix - Race",
            organization: null,
            evt: evt,
            parsedDate: null,
            parsedYear: 2026,
            parsedRoundNumber: 6,
            seriesLabel: "F1TV Feed")
        .Should().BeGreaterThan(0);
    }
}
