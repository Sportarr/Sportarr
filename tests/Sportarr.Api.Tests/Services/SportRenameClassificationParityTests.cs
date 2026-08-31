using Sportarr.Api.Helpers;
using Sportarr.Api.Services;
using Xunit;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Aligning Event.Sport with its league renamed the value on six leagues.
/// Nothing that classifies an event may behave differently because of it,
/// or fighting events would lose their multi-part structure, motorsport
/// would lose sessions, or a team sport would start looking teamless.
///
/// League.Sport itself was NOT changed, so league level behaviour (team
/// selection on add, monitoring) is untouched by construction. These tests
/// cover the event level, where the value really did change.
/// </summary>
public class SportRenameClassificationParityTests
{
    private readonly ITestOutputHelper _out;
    public SportRenameClassificationParityTests(ITestOutputHelper o) => _out = o;

    // league, value events used to carry, value they carry now
    public static TheoryData<string, string, string> Renames() => new()
    {
        { "NFL",             "Football", "American Football" },
        { "NCAA Division 1", "Football", "American Football" },
        { "NHL",             "Hockey",   "Ice Hockey" },
        { "UFC",             "Combat",   "Fighting" },
        { "ONE",             "Combat",   "Fighting" },
        { "Boxing",          "Combat",   "Fighting" },
    };

    [Theory]
    [MemberData(nameof(Renames))]
    public void EveryClassifierAgreesBeforeAndAfterTheRename(string league, string before, string after)
    {
        var checks = new (string Name, Func<string, bool> Fn)[]
        {
            ("LeagueSportRules.IsMotorsport",   s => LeagueSportRules.IsMotorsport(s)),
            ("LeagueSportRules.IsTeamlessSport", s => LeagueSportRules.IsTeamlessSport(s, league)),
            ("EventPartDetector.IsFightingSport", s => EventPartDetector.IsFightingSport(s)),
            ("EventPartDetector.IsMotorsport",  s => EventPartDetector.IsMotorsport(s)),
        };

        foreach (var (name, fn) in checks)
        {
            var b = fn(before);
            var a = fn(after);
            _out.WriteLine($"{league,-16} {name,-38} '{before}'={b,-5} '{after}'={a}");
            Assert.True(b == a, $"{league}: {name} changed from {b} to {a} when '{before}' became '{after}'");
        }

        // Recording padding is resolved from the same field.
        var padBefore = DvrPaddingDefaults.Resolve(before, null, null, 1, 1);
        var padAfter = DvrPaddingDefaults.Resolve(after, null, null, 1, 1);
        _out.WriteLine($"{league,-16} {"DvrPaddingDefaults.Resolve",-38} " +
                       $"'{before}'={padBefore.PrePadMinutes}/{padBefore.PostRollMinutes} " +
                       $"'{after}'={padAfter.PrePadMinutes}/{padAfter.PostRollMinutes}");
        Assert.True(padAfter.PrePadMinutes >= padBefore.PrePadMinutes,
            $"{league}: pre-roll padding shrank {padBefore.PrePadMinutes} -> {padAfter.PrePadMinutes}");
        Assert.True(padAfter.PostRollMinutes >= padBefore.PostRollMinutes,
            $"{league}: post-roll padding shrank {padBefore.PostRollMinutes} -> {padAfter.PostRollMinutes}");
    }

    [Theory]
    [InlineData("Fighting", true)]
    [InlineData("Combat", true)]
    [InlineData("American Football", false)]
    [InlineData("Football", false)]
    [InlineData("Ice Hockey", false)]
    [InlineData("Hockey", false)]
    public void FightingClassificationIsSpellingIndependent(string sport, bool expected)
        => Assert.Equal(expected, EventPartDetector.IsFightingSport(sport));

    [Theory]
    [InlineData("Motorsport", true)]
    [InlineData("Racing", true)]
    [InlineData("American Football", false)]
    [InlineData("Ice Hockey", false)]
    public void MotorsportClassificationIsSpellingIndependent(string sport, bool expected)
        => Assert.Equal(expected, LeagueSportRules.IsMotorsport(sport));
}
