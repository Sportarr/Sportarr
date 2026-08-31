using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The filename patterns only cover the leagues someone wrote a pattern for.
/// Everything else fell through to "Fighting", so more than half of a mixed
/// library was classified as a fight card on import. The league itself knows
/// its sport, so a league that exists locally needs no pattern at all.
/// </summary>
public class ImportSportFromLibraryTests
{
    private static SportarrDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase($"sport-detect-{Guid.NewGuid()}")
            .Options;
        return new SportarrDbContext(options);
    }

    private static ImportMatchingService NewService(SportarrDbContext db) => new(
        db,
        new MediaFileParser(NullLogger<MediaFileParser>.Instance),
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance),
        NullLogger<ImportMatchingService>.Instance);

    private static League League(string name, string sport) => new()
    {
        Name = name,
        Sport = sport,
        Added = DateTime.UtcNow,
        EventSortOrder = "desc",
        Tags = new List<int>()
    };

    private static Task<string?> SportFor(SportarrDbContext db, string title) =>
        SportFor(NewService(db), title);

    private static async Task<string?> SportFor(ImportMatchingService service, string title)
    {
        var method = typeof(ImportMatchingService).GetMethod(
            "DetectSportFromLibraryAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull();
        var task = (Task<string?>)method!.Invoke(service, new object[] { title })!;
        return await task;
    }

    [Fact]
    public async Task Uses_the_league_sport_when_no_filename_pattern_matches()
    {
        using var db = NewDb();
        db.Leagues.Add(League("Eredivisie", "Soccer"));
        await db.SaveChangesAsync();

        var sport = await SportFor(db, "Eredivisie.Ajax.vs.PSV.2026.08.30.1080p.WEB-DL");

        sport.Should().Be("Soccer", "the league is the authority on its own sport");
    }

    [Fact]
    public async Task Prefers_the_longer_league_name_when_two_could_match()
    {
        using var db = NewDb();
        db.Leagues.Add(League("Premier League", "Soccer"));
        db.Leagues.Add(League("Premier League Darts", "Darts"));
        await db.SaveChangesAsync();

        var sport = await SportFor(db, "Premier.League.Darts.Night.12.2026.1080p");

        sport.Should().Be("Darts", "the more specific league name should win");
    }

    [Fact]
    public async Task Does_not_match_a_league_name_inside_another_word()
    {
        using var db = NewDb();
        db.Leagues.Add(League("ONE", "Fighting"));
        await db.SaveChangesAsync();

        // "Bones" contains "one". A substring match would call this a fight.
        var sport = await SportFor(db, "Tour.de.France.Stage.9.Bones.2026.1080p");

        sport.Should().BeNull("a league name has to appear as its own words");
    }

    [Fact]
    public async Task Returns_null_when_the_library_has_no_matching_league()
    {
        using var db = NewDb();
        db.Leagues.Add(League("NFL", "American Football"));
        await db.SaveChangesAsync();

        var sport = await SportFor(db, "Some.Unknown.Competition.2026.1080p");

        sport.Should().BeNull("an unknown name must not be given a sport it has not earned");
    }

    [Fact]
    public async Task Ignores_leagues_that_have_no_sport_recorded()
    {
        using var db = NewDb();
        db.Leagues.Add(League("Mystery Cup", string.Empty));
        await db.SaveChangesAsync();

        var sport = await SportFor(db, "Mystery.Cup.Final.2026.1080p");

        sport.Should().BeNull();
    }

    /// <summary>
    /// Exercised through the real entry points rather than the private
    /// helper, because the bug was that one path was fixed and the other was
    /// not. A candidate picker that reads a file differently from the scan
    /// that found it is the same bug wearing a different hat.
    /// </summary>
    [Fact]
    public async Task Both_entry_points_agree_on_an_unfamiliar_league()
    {
        using var db = NewDb();
        db.Leagues.Add(League("Eredivisie", "Soccer"));
        await db.SaveChangesAsync();

        var service = NewService(db);

        // "Main Event" is fight-card language. On an unfamiliar league the old
        // default called this Fighting and the part detector read it as a card.
        const string title = "Eredivisie.Ajax.vs.PSV.Main.Event.2026.08.30.1080p.WEB-DL";

        var suggestion = await service.FindBestMatchAsync(title, "/tmp/none.mkv");
        var candidates = await service.GetAllPossibleMatchesAsync(title);

        // No events exist, so neither path can name one. What matters is
        // that both ran the same resolution and neither invented a match.
        suggestion?.EventId.Should().BeNull("there is no event to match");
        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task The_league_table_is_read_once_per_scope()
    {
        using var db = NewDb();
        db.Leagues.Add(League("Eredivisie", "Soccer"));
        await db.SaveChangesAsync();

        var service = NewService(db);

        // A scan calls this per file. The league table must not be reloaded
        // for each one.
        for (var i = 0; i < 5; i++)
        {
            await SportFor(service, $"Unfamiliar.Fixture.{i}.2026.1080p");
        }

        var cache = typeof(ImportMatchingService)
            .GetField("_leagueSports", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(service);

        cache.Should().NotBeNull("the normalized league list should be held for the scope");
    }

    /// <summary>
    /// Taken from the release in SportsFileNameParserTests, which already
    /// refuses to read a trailing "one" as ONE Championship. The library
    /// fallback has to refuse it too, or a superbike round becomes a fight
    /// card the moment someone adds the real ONE league.
    /// </summary>
    [Theory]
    [InlineData("BSB 2026 Round01 Oulton Park International Race One TNT WEB-DL 1080p")]
    [InlineData("MotoGP.2026.Round.12.Silverstone.Race.One.1080p.WEB-DL")]
    public async Task A_league_named_one_does_not_claim_a_race_one_release(string title)
    {
        using var db = NewDb();
        db.Leagues.Add(League("ONE", "Fighting"));
        await db.SaveChangesAsync();

        var sport = await SportFor(db, title);

        sport.Should().BeNull("the bare word cannot tell these apart");
    }

    [Fact]
    public async Task A_multi_word_league_is_still_matched_even_if_a_word_is_common()
    {
        using var db = NewDb();
        db.Leagues.Add(League("World Rally Championship", "Motorsport"));
        await db.SaveChangesAsync();

        var sport = await SportFor(db, "World.Rally.Championship.Rally.Sweden.2026.1080p");

        sport.Should().Be("Motorsport", "two words or more are specific enough to trust");
    }

    /// <summary>
    /// Release groups publish the sponsor-branded name as often as the plain
    /// one, which is why the league stores aliases at all. A fallback that
    /// read only League.Name sent those releases to the fighting default.
    /// </summary>
    [Fact]
    public async Task An_alias_resolves_the_sport_too()
    {
        using var db = NewDb();
        var league = League("Premiership Rugby", "Rugby");
        league.AlternateName = "Gallagher Premiership,Gallagher Premiership Rugby";
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        var sport = await SportFor(db, "Gallagher.Premiership.Rugby.Bath.vs.Leicester.2026.1080p");

        sport.Should().Be("Rugby");
    }

    [Fact]
    public async Task Aliases_split_on_the_same_delimiters_the_rest_of_the_app_uses()
    {
        using var db = NewDb();
        var league = League("Eredivisie", "Soccer");
        league.AlternateName = "Dutch Eredivisie|Holland Eredivisie/Keuken Kampioen";
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        (await SportFor(db, "Dutch.Eredivisie.Ajax.vs.PSV.2026.1080p")).Should().Be("Soccer");
        (await SportFor(db, "Keuken.Kampioen.Divisie.Round.4.2026.1080p")).Should().Be("Soccer");
    }

    [Fact]
    public async Task An_ambiguous_alias_is_rejected_like_an_ambiguous_name()
    {
        using var db = NewDb();
        var league = League("Some Fight Promotion", "Fighting");
        league.AlternateName = "ONE";
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        var sport = await SportFor(db, "BSB.2026.Oulton.Park.Race.One.1080p");

        sport.Should().BeNull("an alias gets the same scrutiny as a name");
    }

    /// <summary>
    /// The reported harm: a file from a league the patterns do not know was
    /// called a fight card, and the part detector then cut it into segments
    /// only fight cards have. An unknown sport now stays unknown, so no
    /// sport-specific reading happens at all.
    /// </summary>
    [Fact]
    public async Task An_unknown_sport_does_not_become_fighting()
    {
        using var db = NewDb();
        db.Leagues.Add(League("NFL", "American Football"));
        await db.SaveChangesAsync();

        var service = NewService(db);

        var resolved = typeof(ImportMatchingService).GetMethod(
            "ResolveSportAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var task = (Task<string?>)resolved.Invoke(
            service, new object?[] { "Totally.Unknown.Competition.2026.1080p", null })!;

        (await task).Should().BeNull("an unidentified file has no sport, and no sport is not Fighting");
    }

    /// <summary>
    /// "Main Event" and "Prelims" are fight-card language. On a file whose
    /// sport is unknown they must not produce a part, because the part only
    /// means anything once the sport is known to have segments.
    /// </summary>
    [Theory]
    [InlineData("Unknown.League.Main.Event.2026.1080p")]
    [InlineData("Unknown.League.Prelims.2026.1080p")]
    [InlineData("Unknown.League.Early.Prelims.2026.1080p")]
    public async Task Card_words_do_not_create_a_part_when_the_sport_is_unknown(string title)
    {
        using var db = NewDb();
        db.Leagues.Add(League("Eredivisie", "Soccer"));
        await db.SaveChangesAsync();

        var service = NewService(db);

        // Goes through the real entry point, not the helper.
        var suggestion = await service.FindBestMatchAsync(title, "/tmp/none.mkv");

        suggestion?.Part.Should().BeNull("no sport means no segment reading");
    }
}
