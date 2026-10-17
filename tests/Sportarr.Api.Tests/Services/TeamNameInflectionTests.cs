using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class TeamNameInflectionTests
{
    private readonly ReleaseMatchingService _matcher;
    private readonly ReleaseMatchScorer _scorer = new();

    public TeamNameInflectionTests()
    {
        var parser = new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance);
        var partDetector = new EventPartDetector(NullLogger<EventPartDetector>.Instance);
        _matcher = new ReleaseMatchingService(
            NullLogger<ReleaseMatchingService>.Instance,
            parser,
            partDetector);
    }

    private static Event AthleticsGame() => new()
    {
        Id = 1,
        ExternalId = "ev-2306995",
        Title = "Athletics vs Toronto Blue Jays",
        Sport = "Baseball",
        HomeTeamId = 10,
        AwayTeamId = 11,
        HomeTeamName = "Athletics",
        AwayTeamName = "Toronto Blue Jays",
        EventDate = new DateTime(2026, 9, 9, 19, 5, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2026, 9, 9),
        BroadcastDateVerified = true,
        League = new League { Id = 2, Name = "MLB", Sport = "Baseball" }
    };

    private static Event BuffaloesGame() => new()
    {
        Id = 2,
        ExternalId = "ev-npb-test",
        Title = "Fukuoka SoftBank Hawks vs Orix Buffaloes",
        Sport = "Baseball",
        HomeTeamId = 20,
        AwayTeamId = 21,
        HomeTeamName = "Fukuoka SoftBank Hawks",
        AwayTeamName = "Orix Buffaloes",
        EventDate = new DateTime(2026, 9, 9, 9, 0, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2026, 9, 9),
        BroadcastDateVerified = true,
        League = new League { Id = 3, Name = "NPB", Sport = "Baseball" }
    };

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test"
    };

    private static IReadOnlyCollection<League> LeagueSnapshot(Event evt, params League[] otherLeagues)
    {
        return new[] { evt.League! }.Concat(otherLeagues).ToArray();
    }

    [Fact]
    public void Validator_accepts_a_regular_terminal_s_variant()
    {
        var game = AthleticsGame();
        var result = _matcher.ValidateRelease(
            Release("MLB RS 2026 Toronto Blue Jays vs Athletic 09 09 720p HDTV x264-GRP"),
            game,
            knownLeagues: LeagueSnapshot(game));

        result.IsMatch.Should().BeTrue();
        result.IsHardRejection.Should().BeFalse();
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void Scorer_accepts_a_regular_terminal_s_variant()
    {
        var game = AthleticsGame();
        var score = _scorer.CalculateMatchScore(
            "MLB.2026.09.09.Toronto.Blue.Jays.vs.Athletic.720p.HDTV.x264-GRP",
            game,
            LeagueSnapshot(game));

        score.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Fact]
    public void Validator_accepts_the_variant_without_a_league_token()
    {
        var game = AthleticsGame();
        var result = _matcher.ValidateRelease(
            Release("Toronto Blue Jays vs Athletic 2026 09 09 1080p WEB h264-GRP"),
            game,
            knownLeagues: LeagueSnapshot(game));

        result.IsMatch.Should().BeTrue();
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void Validator_rejects_the_variant_when_the_league_snapshot_is_missing()
    {
        var result = _matcher.ValidateRelease(
            Release("Toronto Blue Jays vs Athletic 2026 09 09 1080p WEB h264-GRP"),
            AthleticsGame());

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void Validator_rejects_the_variant_when_the_league_snapshot_is_empty()
    {
        var result = _matcher.ValidateRelease(
            Release("Toronto Blue Jays vs Athletic 2026 09 09 1080p WEB h264-GRP"),
            AthleticsGame(),
            knownLeagues: Array.Empty<League>());

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void Validator_accepts_a_variant_for_the_events_data_driven_league()
    {
        var game = BuffaloesGame();
        var result = _matcher.ValidateRelease(
            Release("NPB 2026 09 09 Fukuoka SoftBank Hawks vs Orix Buffaloe 1080p WEB h264-GRP"),
            game,
            knownLeagues: LeagueSnapshot(game));

        result.IsMatch.Should().BeTrue();
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void Validator_accepts_a_variant_for_the_events_alternate_league_name()
    {
        var game = BuffaloesGame();
        game.League!.Name = "Nippon Professional Baseball";
        game.League.AlternateName = "NPB";
        var result = _matcher.ValidateRelease(
            Release("NPB 2026 09 09 Fukuoka SoftBank Hawks vs Orix Buffaloe 1080p WEB h264-GRP"),
            game,
            knownLeagues: LeagueSnapshot(game));

        result.IsMatch.Should().BeTrue();
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void Validator_rejects_another_stored_leagues_label()
    {
        var game = BuffaloesGame();
        var mlb = new League { Id = 2, Name = "MLB", Sport = "Baseball" };
        var result = _matcher.ValidateRelease(
            Release("MLB 2026 09 09 Fukuoka SoftBank Hawks vs Orix Buffaloe 1080p WEB h264-GRP"),
            game,
            knownLeagues: LeagueSnapshot(game, mlb));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void Validator_rejects_an_alias_shared_with_another_stored_league()
    {
        var game = BuffaloesGame();
        game.League!.AlternateName = "Pacific League";
        var otherLeague = new League
        {
            Id = 4,
            Name = "KBO League",
            Sport = "Baseball",
            AlternateName = "Pacific League"
        };
        var result = _matcher.ValidateRelease(
            Release("Pacific League 2026 09 09 Fukuoka SoftBank Hawks vs Orix Buffaloe 1080p WEB h264-GRP"),
            game,
            knownLeagues: LeagueSnapshot(game, otherLeague));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void Validator_allows_arbitrary_metadata_after_the_second_team()
    {
        var game = AthleticsGame();
        var result = _matcher.ValidateRelease(
            Release("Toronto Blue Jays vs Athletic SPORTSNET 2026 09 09 1080p WEB h264-GRP"),
            game,
            knownLeagues: LeagueSnapshot(game));

        result.IsMatch.Should().BeTrue();
        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Fact]
    public void Scorer_keeps_the_variant_level_with_the_exact_team_name()
    {
        var game = AthleticsGame();
        var leagues = LeagueSnapshot(game);
        var exactScore = _scorer.CalculateMatchScore(
            "Toronto.Blue.Jays.vs.Athletics.2026.09.09.1080p.WEB.h264-GRP",
            game,
            leagues);
        var variantScore = _scorer.CalculateMatchScore(
            "Toronto.Blue.Jays.vs.Athletic.2026.09.09.1080p.WEB.h264-GRP",
            game,
            leagues);

        variantScore.Should().Be(exactScore);
        variantScore.Should().BeGreaterThanOrEqualTo(ReleaseMatchScorer.MinimumMatchScore);
    }

    [Fact]
    public void Scorer_rejects_the_variant_when_only_one_event_team_is_populated()
    {
        var game = AthleticsGame();
        game.AwayTeamId = null;
        game.AwayTeamName = null;

        var score = _scorer.CalculateMatchScore(
            "MLB.2026.09.09.Athletic.1080p.WEB.h264-GRP",
            game,
            LeagueSnapshot(game));

        score.Should().Be(0);
    }

    [Theory]
    [InlineData("MLB RS 2026 Toronto Blue Jays vs Athleticism 09 09 720p HDTV x264-GRP")]
    [InlineData("MLB RS 2026 Toronto Blue Jays vs Seattle Mariners 09 09 720p HDTV x264-GRP")]
    public void Validator_rejects_non_team_words(string title)
    {
        var game = AthleticsGame();
        var result = _matcher.ValidateRelease(
            Release(title),
            game,
            knownLeagues: LeagueSnapshot(game));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Theory]
    [InlineData("MLB.2026.09.09.Toronto.Blue.Jays.vs.Athleticism.720p.HDTV.x264-GRP")]
    [InlineData("MLB.2026.09.09.Toronto.Blue.Jays.vs.Seattle.Mariners.720p.HDTV.x264-GRP")]
    public void Scorer_rejects_non_team_words(string title)
    {
        var game = AthleticsGame();
        _scorer.CalculateMatchScore(title, game, LeagueSnapshot(game)).Should().Be(0);
    }

    [Fact]
    public void Validator_does_not_use_the_variant_for_a_different_league()
    {
        var game = AthleticsGame();
        var result = _matcher.ValidateRelease(
            Release("LaLiga 2026 09 09 Toronto Blue Jays vs Athletic Bilbao 720p HDTV x264-GRP"),
            game,
            knownLeagues: LeagueSnapshot(game, new League { Id = 4, Name = "LaLiga", Sport = "Soccer" }));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void Scorer_does_not_use_the_variant_for_a_different_league()
    {
        var game = AthleticsGame();
        var score = _scorer.CalculateMatchScore(
            "LaLiga.2026.09.09.Toronto.Blue.Jays.vs.Athletic.Bilbao.720p.HDTV.x264-GRP",
            game,
            LeagueSnapshot(game, new League { Id = 4, Name = "LaLiga", Sport = "Soccer" }));

        score.Should().Be(0);
    }

    [Fact]
    public void Validator_rejects_a_real_no_league_bilbao_matchup()
    {
        var result = _matcher.ValidateRelease(
            Release("Athletic Bilbao vs Barcelona 2026 09 09 1080p WEB h264-GRP"),
            AthleticsGame());

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void Validator_does_not_treat_an_unknown_prefix_as_no_league()
    {
        var game = AthleticsGame();
        var result = _matcher.ValidateRelease(
            Release("NPB 2026 Toronto Blue Jays vs Athletic 09 09 1080p WEB h264-GRP"),
            game,
            knownLeagues: LeagueSnapshot(game));

        result.IsMatch.Should().BeFalse();
        result.IsHardRejection.Should().BeTrue();
    }

    [Fact]
    public void Scorer_rejects_the_variant_when_the_league_snapshot_is_empty()
    {
        var score = _scorer.CalculateMatchScore(
            "Toronto.Blue.Jays.vs.Athletic.2026.09.09.1080p.WEB.h264-GRP",
            AthleticsGame(),
            Array.Empty<League>());

        score.Should().Be(0);
    }

    [Fact]
    public void Validator_preserves_fused_short_name_matching()
    {
        var game = AthleticsGame();
        game.HomeTeam = new Team { Id = 10, Name = "Athletics", ShortName = "OAK", Sport = "Baseball" };
        game.AwayTeam = new Team { Id = 11, Name = "Toronto Blue Jays", ShortName = "TOR", Sport = "Baseball" };

        var result = _matcher.ValidateRelease(
            Release("MLB 2026 09 09 TORvsOAK 1080p WEB h264-GRP"),
            game);

        result.MatchReasons.Should().Contain("Both team names found");
    }

    [Theory]
    [InlineData("ManCityvsArsenal")]
    [InlineData("ManCityVsArsenal")]
    [InlineData("ManCityxArsenal")]
    [InlineData("ArsenalvsManCity")]
    [InlineData("ArsenalxManCity")]
    public void Validator_preserves_fused_user_alias_matching(string teams)
    {
        var game = new Event
        {
            Id = 2,
            Title = "Manchester City vs Arsenal",
            Sport = "Soccer",
            HomeTeamId = 20,
            AwayTeamId = 21,
            HomeTeamName = "Manchester City",
            AwayTeamName = "Arsenal",
            HomeTeam = new Team { Id = 20, Name = "Manchester City", UserAliases = "ManCity", Sport = "Soccer" },
            AwayTeam = new Team { Id = 21, Name = "Arsenal", Sport = "Soccer" },
            EventDate = new DateTime(2026, 9, 9, 15, 0, 0, DateTimeKind.Utc),
            BroadcastDate = new DateTime(2026, 9, 9),
            BroadcastDateVerified = true,
            League = new League { Id = 3, Name = "Premier League", Sport = "Soccer" }
        };

        var result = _matcher.ValidateRelease(
            Release($"EPL 2026 09 09 {teams} 1080p WEB h264-GRP"),
            game);

        result.MatchReasons.Should().Contain("Both team names found");
    }
}
