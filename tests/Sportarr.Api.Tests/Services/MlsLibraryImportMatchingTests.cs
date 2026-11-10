using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

public class MlsLibraryImportMatchingTests
{
    [Theory]
    [InlineData(
        "American.Major.League.Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United",
        "Seattle Sounders FC vs Minnesota United", 11)]
    [InlineData(
        "American.Major.League.Soccer 2021-09-19 Philadelphis Union vs Orlando City",
        "Philadelphis Union vs Orlando City", 19)]
    public void A_hyphenated_date_keeps_the_fixture_in_the_parsed_title(
        string filename, string fixture, int day)
    {
        var parsed = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(filename);

        parsed.EventTitle.Should().Be(fixture);
        parsed.AirDate.Should().Be(new DateTime(2021, 9, day));
    }

    [Theory]
    [InlineData(
        "American.Major.League.Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United",
        "Seattle Sounders FC vs Minnesota United", "CF Montreal", "Chicago Fire", 11, 19)]
    [InlineData(
        "American.Major.League.Soccer 2021-09-19 Philadelphis Union vs Orlando City",
        "Philadelphis Union vs Orlando City", "CF Montreal", "Philadelphia Union", 19, 16)]
    public void A_named_fixture_does_not_match_different_teams_even_with_a_matching_stored_date(
        string filename, string fixture, string wrongHome, string wrongAway,
        int fileDay, int shownEventDay)
    {
        var candidate = Game(wrongHome, wrongAway, shownEventDay, fileDay);

        Score(filename, fixture, candidate, fileDay).Should().Be(0);
    }

    [Theory]
    [InlineData(
        "American.Major.League.Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United",
        "Seattle Sounders FC vs Minnesota United", "Seattle Sounders FC", "Minnesota United", 11)]
    [InlineData(
        "American.Major.League.Soccer 2021-09-19 Philadelphis Union vs Orlando City",
        "Philadelphis Union vs Orlando City", "Philadelphia Union", "Orlando City", 19)]
    public void A_named_fixture_keeps_its_correct_candidate_available(
        string filename, string fixture, string home, string away, int day)
    {
        var candidate = Game(home, away, day, day);

        Score(filename, fixture, candidate, day).Should().BeGreaterOrEqualTo(40);
    }

    [Fact]
    public void An_mls_filename_does_not_match_an_mls_next_pro_event()
    {
        const string filename =
            "American.Major.League.Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United";
        var candidate = Game("Seattle Sounders FC", "Minnesota United", 11, 11);
        candidate.League = new League { Name = "MLS Next Pro", Sport = "Soccer" };

        Score(filename, "Seattle Sounders FC vs Minnesota United", candidate, 11).Should().Be(0);
    }

    [Fact]
    public void A_dotted_mls_filename_keeps_its_fixture_and_rejects_mls_next_pro()
    {
        const string filename =
            "American.Major.League.Soccer.2021-09-11.Seattle.Sounders.FC.vs.Minnesota.United";
        var parsed = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(filename);
        var candidate = Game("Seattle Sounders FC", "Minnesota United", 11, 11);
        candidate.League = new League { Name = "MLS Next Pro", Sport = "Soccer" };

        parsed.EventTitle.Should().Be("Seattle Sounders FC vs Minnesota United");
        Score(filename, parsed.EventTitle, candidate, 11).Should().Be(0);
    }

    [Fact]
    public void An_underscored_mls_filename_rejects_the_wrong_league()
    {
        const string filename =
            "American_Major_League_Soccer_2021-09-11_Seattle_Sounders_FC_vs_Minnesota_United";
        var parsed = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(filename);
        var candidate = Game("Seattle Sounders FC", "Minnesota United", 11, 11);
        candidate.League = new League { Name = "MLS Next Pro", Sport = "Soccer" };

        parsed.EventTitle.Should().Be("Seattle Sounders FC vs Minnesota United");
        Score(filename, parsed.EventTitle, candidate, 11).Should().Be(0);
    }

    [Theory]
    [InlineData("MLS.2021.09.11.Seattle.Sounders.FC.vs.Minnesota.United")]
    [InlineData("Major.League.Soccer.2021.09.11.Seattle.Sounders.FC.vs.Minnesota.United")]
    public void Other_mls_prefixes_reject_a_different_team_and_league(string filename)
    {
        var parsed = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(filename);
        var wrongTeam = Game("CF Montreal", "Minnesota United", 11, 11);
        var wrongLeague = Game("Seattle Sounders FC", "Minnesota United", 11, 11);
        wrongLeague.League = new League { Name = "MLS Next Pro", Sport = "Soccer" };

        parsed.EventTitle.Should().Be("Seattle Sounders FC vs Minnesota United");
        Score(filename, parsed.EventTitle, wrongTeam, 11).Should().Be(0);
        Score(filename, parsed.EventTitle, wrongLeague, 11).Should().Be(0);
    }

    [Fact]
    public void A_dc_united_fixture_can_match_its_correct_event()
    {
        const string filename = "American.Major.League.Soccer 2021-09-11 DC United vs Chicago Fire";
        var candidate = Game("D.C. United", "Chicago Fire", 11, 11);

        Score(filename, "DC United vs Chicago Fire", candidate, 11).Should().BeGreaterOrEqualTo(40);
    }

    [Fact]
    public void A_shared_city_word_does_not_replace_the_named_team()
    {
        const string filename =
            "American.Major.League.Soccer 2021-09-11 New York City FC vs Orlando City";
        var candidate = Game("New York Red Bulls", "Orlando City", 11, 11);

        Score(filename, "New York City FC vs Orlando City", candidate, 11).Should().Be(0);
    }

    [Theory]
    [InlineData("Seattle Sounders FC @ Minnesota United", "Seattle Sounders FC", "Minnesota United")]
    [InlineData("LA Galaxy vs Seattle Sounders FC", "Los Angeles Galaxy", "Seattle Sounders FC")]
    [InlineData("NY Red Bulls vs Orlando City", "New York Red Bulls", "Orlando City")]
    [InlineData("D.C. United vs Chicago Fire", "D.C. United", "Chicago Fire")]
    public void Common_fixture_forms_keep_the_correct_event_available(
        string fixture, string home, string away)
    {
        var filename = $"American.Major.League.Soccer 2021-09-11 {fixture}";
        var parsed = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(filename);
        var candidate = Game(home, away, 11, 11);

        Score(filename, parsed.EventTitle, candidate, 11).Should().BeGreaterOrEqualTo(40);
    }

    [Theory]
    [InlineData("PROPER")]
    [InlineData("REPACK")]
    [InlineData("MULTI")]
    [InlineData("x264")]
    [InlineData("-GROUP")]
    public void Release_metadata_after_the_fixture_keeps_the_correct_event_available(string marker)
    {
        var filename = $"American.Major.League.Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United {marker}";
        var parsed = new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(filename);
        var candidate = Game("Seattle Sounders FC", "Minnesota United", 11, 11);

        parsed.EventTitle.Should().StartWith("Seattle Sounders FC vs Minnesota United");
        Score(filename, parsed.EventTitle, candidate, 11).Should().BeGreaterOrEqualTo(40);
    }

    [Fact]
    public void A_team_name_containing_a_language_marker_keeps_its_fixture()
    {
        var parsed = new MediaFileParser(NullLogger<MediaFileParser>.Instance)
            .Parse("World.Cup 2022-12-01 Germany vs Spain");

        parsed.EventTitle.Should().Be("Germany vs Spain");
    }

    [Fact]
    public void A_team_name_equal_to_a_language_marker_keeps_its_fixture()
    {
        var parsed = new MediaFileParser(NullLogger<MediaFileParser>.Instance)
            .Parse("Gold.Cup 2017-07-07 French Guiana vs Canada");

        parsed.EventTitle.Should().Be("French Guiana vs Canada");
    }

    [Fact]
    public void A_short_club_name_from_the_team_aliases_keeps_the_correct_game()
    {
        const string filename = "American.Major.League.Soccer 2021-09-11 NYCFC vs Inter Miami";
        var candidate = Game("New York City FC", "Inter Miami", 11, 11);
        candidate.HomeTeam = new Team { Name = "New York City FC", ShortName = "NYCFC" };

        Score(filename, Fixture(filename), candidate, 11).Should().BeGreaterOrEqualTo(40);
    }

    [Theory]
    [InlineData("American.Major.League.Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United-GRP")]
    [InlineData("American.Major.League.Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United ESPN 1080p")]
    public void A_group_or_channel_after_the_teams_keeps_the_correct_game(string filename)
    {
        var candidate = Game("Seattle Sounders FC", "Minnesota United", 11, 11);

        Score(filename, Fixture(filename), candidate, 11).Should().BeGreaterOrEqualTo(40);
    }

    [Fact]
    public void A_game_without_team_names_is_judged_by_league_and_title()
    {
        const string filename = "American.Major.League.Soccer 2021-09-11 Seattle Sounders FC vs Minnesota United";
        var candidate = Game("Seattle Sounders FC", "Minnesota United", 11, 11);
        candidate.HomeTeamName = null;
        candidate.AwayTeamName = null;

        Score(filename, Fixture(filename), candidate, 11).Should().BeGreaterThan(0);
    }

    [Fact]
    public void A_hyphenated_team_name_is_not_cut_as_a_release_group()
    {
        var parsed = new MediaFileParser(NullLogger<MediaFileParser>.Instance)
            .Parse("UEFA Champions League 2024 04 16 Barcelona vs Paris Saint-Germain");

        parsed.EventTitle.Should().Be("Barcelona vs Paris Saint-Germain");
    }

    [Theory]
    [InlineData("MLS 2021-09-11 Seattle vs Portland 1080p", "Seattle Sounders FC", "Portland Timbers")]
    [InlineData("MLS 2021-09-11 Atlanta Utd vs Orlando City 1080p", "Atlanta United", "Orlando City")]
    [InlineData("MLS 2021-09-11 Seattle Sounders vs Portland Timbers English 1080p", "Seattle Sounders FC", "Portland Timbers")]
    public void Short_city_and_tagged_names_keep_the_correct_game(string filename, string home, string away)
    {
        var candidate = Game(home, away, 11, 11);

        Score(filename, Fixture(filename), candidate, 11).Should().BeGreaterOrEqualTo(40);
    }

    [Fact]
    public void Los_angeles_fc_does_not_match_la_galaxy()
    {
        const string filename = "MLS 2021-09-11 Los Angeles FC vs Portland Timbers 1080p";
        var candidate = Game("LA Galaxy", "Portland Timbers", 11, 11);

        Score(filename, Fixture(filename), candidate, 11).Should().Be(0);
    }

    [Fact]
    public void A_city_alias_on_one_club_does_not_claim_the_other_club_of_that_city()
    {
        const string filename = "MLS 2021-09-11 New York City FC vs Orlando City 1080p";
        var candidate = Game("New York Red Bulls", "Orlando City", 11, 11);
        candidate.HomeTeam = new Team { Name = "New York Red Bulls", AlternateName = "New York, Red Bulls" };

        Score(filename, Fixture(filename), candidate, 11).Should().Be(0);
    }

    [Theory]
    [InlineData("MLS 2021-09-11 Sporting KC vs Austin FC 1080p", "Sporting Kansas City", "Austin FC")]
    [InlineData("MLS 2021-09-11 NYCFC vs Inter Miami 1080p", "New York City FC", "Inter Miami")]
    [InlineData("MLS 2021-09-11 RSL vs LAFC 1080p", "Real Salt Lake", "Los Angeles FC")]
    [InlineData("MLS 2021-09-11 Toronto ESPN vs CF Montréal 1080p", "Toronto FC", "CF Montreal")]
    [InlineData("MLS 2021-09-11 Real Salt Lake vs LAFC ESPN 1080p", "Real Salt Lake", "Los Angeles FC")]
    public void Initials_and_short_forms_keep_the_correct_game(string filename, string home, string away)
    {
        var candidate = Game(home, away, 11, 11);

        // Only the MLS team check is under test. It refuses with 0.
        Score(filename, Fixture(filename), candidate, 11).Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("MLS 2021-09-11 LA Galaxy vs Portland Timbers 1080p")]
    [InlineData("MLS 2021-09-11 LAFC vs Portland Timbers 1080p")]
    [InlineData("MLS 2021-09-11 LA GALAXY vs PORTLAND TIMBERS 1080p")]
    public void Los_angeles_clubs_do_not_claim_each_other(string filename)
    {
        var galaxyFile = filename.Contains("Galaxy", StringComparison.OrdinalIgnoreCase);
        var candidate = Game(galaxyFile ? "Los Angeles FC" : "LA Galaxy", "Portland Timbers", 11, 11);

        Score(filename, Fixture(filename), candidate, 11).Should().Be(0);
    }

    [Fact]
    public void New_york_initials_do_not_claim_the_red_bulls()
    {
        const string filename = "MLS 2021-09-11 NYCFC vs Orlando City 1080p";
        var candidate = Game("New York Red Bulls", "Orlando City", 11, 11);

        Score(filename, Fixture(filename), candidate, 11).Should().Be(0);
    }

    [Fact]
    public void An_all_caps_club_code_does_not_fit_a_club_sharing_its_letters()
    {
        const string filename = "MLS 2021-09-11 MIA vs Orlando City 1080p";
        var candidate = Game("Minnesota United", "Orlando City", 11, 11);

        Score(filename, Fixture(filename), candidate, 11).Should().Be(0);
    }

    private static string Fixture(string filename) =>
        new MediaFileParser(NullLogger<MediaFileParser>.Instance).Parse(filename).EventTitle!;

    private static Event Game(string home, string away, int shownEventDay, int storedDay) => new()
    {
        Title = $"{home} vs {away}",
        HomeTeamName = home,
        AwayTeamName = away,
        Sport = "Soccer",
        EventDate = new DateTime(2021, shownEventDay == 16 ? 10 : 9, shownEventDay),
        BroadcastDate = new DateTime(2021, 9, storedDay),
        Season = "2021",
        League = new League { Name = "American Major League Soccer", Sport = "Soccer" }
    };

    private static int Score(string filename, string fixture, Event candidate, int day) =>
        LibraryImportService.CalculateMatchConfidence(
            searchTitle: fixture,
            eventTitle: candidate.Title,
            organization: null,
            evt: candidate,
            parsedDate: new DateTime(2021, 9, day),
            parsedYear: 2021,
            parsedSport: null,
            sourceTitle: filename);
}
