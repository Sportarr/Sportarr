using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Field report (issues #194/#197 follow-up): a user disabled upgrades on
/// the quality profile their league uses, yet RSS sync re-grabbed events
/// that already had files. RSS resolved the profile as "the event's own
/// profile, else the FIRST profile in the table", skipping the league's
/// profile entirely. Since events rarely carry a per-event profile, the
/// UpgradesAllowed gate was consulting an unrelated profile that still
/// allowed upgrades. Resolution must mirror the search path: event, then
/// league, then the default-flagged profile, then first by id.
/// </summary>
public class RssProfileResolutionTests
{
    private static QualityProfile Profile(int id, string name, bool isDefault = false, bool upgrades = true) => new()
    {
        Id = id,
        Name = name,
        IsDefault = isDefault,
        UpgradesAllowed = upgrades,
    };

    private static readonly List<QualityProfile> Profiles = new()
    {
        Profile(1, "Any (seeded first)", upgrades: true),
        Profile(2, "HD no upgrades", upgrades: false),
        Profile(3, "UHD", isDefault: true),
    };

    private static Event Evt(int? eventProfile, int? leagueProfile) => new()
    {
        Title = "Chinese Grand Prix - Race",
        Sport = "Motorsport",
        EventDate = new DateTime(2026, 3, 22, 7, 0, 0, DateTimeKind.Utc),
        QualityProfileId = eventProfile,
        League = new League { Name = "Formula 1", Sport = "Motorsport", QualityProfileId = leagueProfile },
    };

    [Fact]
    public void EventProfile_WinsOverLeague()
    {
        RssSyncService.ResolveQualityProfile(Evt(3, 2), Profiles)!.Id.Should().Be(3);
    }

    [Fact]
    public void LeagueProfile_UsedWhenEventHasNone()
    {
        // The reported scenario: upgrades disabled on the league's profile.
        var resolved = RssSyncService.ResolveQualityProfile(Evt(null, 2), Profiles)!;
        resolved.Id.Should().Be(2);
        resolved.UpgradesAllowed.Should().BeFalse();
    }

    [Fact]
    public void DefaultFlaggedProfile_BeatsFirstById()
    {
        RssSyncService.ResolveQualityProfile(Evt(null, null), Profiles)!.Id.Should().Be(3);
    }

    [Fact]
    public void FirstById_IsTheLastResort()
    {
        var noDefault = Profiles.Where(p => !p.IsDefault).ToList();
        RssSyncService.ResolveQualityProfile(Evt(null, null), noDefault)!.Id.Should().Be(1);
    }

    [Fact]
    public void DanglingProfileIds_FallThroughInsteadOfReturningNull()
    {
        RssSyncService.ResolveQualityProfile(Evt(99, 98), Profiles)!.Id.Should().Be(3);
    }
}
