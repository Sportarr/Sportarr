using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

public class SessionTypeQualityResolverTests
{
    private static League MakeLeague(string name, string sport, string? map) => new()
    {
        Name = name,
        Sport = sport,
        SessionTypeQualityProfiles = map
    };

    // ---- Motorsport sessions ----

    [Fact]
    public void F1Race_UsesMappedProfile()
    {
        var league = MakeLeague("Formula 1", "Motorsport", "{\"Race\":7,\"Qualifying\":3}");

        Assert.Equal(7, SessionTypeQualityResolver.Resolve(league, "Monaco Grand Prix - Race"));
        Assert.Equal(3, SessionTypeQualityResolver.Resolve(league, "Monaco Grand Prix - Qualifying"));
    }

    [Fact]
    public void F1UnmappedSession_FallsBackToNull()
    {
        var league = MakeLeague("Formula 1", "Motorsport", "{\"Race\":7}");

        // Practice isn't in the map, so the caller should use the league profile.
        Assert.Null(SessionTypeQualityResolver.Resolve(league, "Monaco Grand Prix - Free Practice 1"));
    }

    // ---- Fighting / wrestling event types ----

    [Fact]
    public void WwePleAndWeekly_ResolveIndependently()
    {
        // WWE's premium events classify as "PLE" (the id the UI sends).
        var league = MakeLeague("WWE", "Fighting", "{\"PLE\":9,\"Weekly\":2}");

        Assert.Equal(9, SessionTypeQualityResolver.Resolve(league, "WWE WrestleMania 42"));
        Assert.Equal(2, SessionTypeQualityResolver.Resolve(league, "WWE Monday Night Raw"));
    }

    [Fact]
    public void UfcPpvAndFightNight_ResolveIndependently()
    {
        var league = MakeLeague("UFC", "Fighting", "{\"PPV\":9,\"FightNight\":2}");

        Assert.Equal(9, SessionTypeQualityResolver.Resolve(league, "UFC 310: Jones vs Aspinall"));
        Assert.Equal(2, SessionTypeQualityResolver.Resolve(league, "UFC Fight Night: Smith vs Jones"));
    }

    [Fact]
    public void MapKeysMatchCaseInsensitively()
    {
        var league = MakeLeague("Formula 1", "Motorsport", "{\"race\":5}");

        Assert.Equal(5, SessionTypeQualityResolver.Resolve(league, "Belgian Grand Prix - Race"));
    }

    // ---- No-op cases ----

    [Fact]
    public void NoMap_ReturnsNull()
    {
        Assert.Null(SessionTypeQualityResolver.Resolve(MakeLeague("Formula 1", "Motorsport", null), "Monaco Grand Prix - Race"));
        Assert.Null(SessionTypeQualityResolver.Resolve(MakeLeague("Formula 1", "Motorsport", ""), "Monaco Grand Prix - Race"));
        Assert.Null(SessionTypeQualityResolver.Resolve(MakeLeague("Formula 1", "Motorsport", "{}"), "Monaco Grand Prix - Race"));
    }

    [Fact]
    public void MalformedJson_ReturnsNullInsteadOfThrowing()
    {
        var league = MakeLeague("Formula 1", "Motorsport", "{not json");

        Assert.Null(SessionTypeQualityResolver.Resolve(league, "Monaco Grand Prix - Race"));
    }

    [Fact]
    public void NonClassifiableSport_ReturnsNull()
    {
        // Team sports have no session/event type classification.
        var league = MakeLeague("NBA", "Basketball", "{\"Race\":7}");

        Assert.Null(SessionTypeQualityResolver.Resolve(league, "Lakers vs Celtics"));
    }
}
