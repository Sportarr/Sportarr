using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Covers EventPartDetector.AreAllMonitoredPartsPresent - the check that decides
/// whether a (potentially multi-part) event is fully downloaded and can leave the
/// Wanted / backlog / RSS search lists. Regression guard for "once one part of a
/// multi-part event is downloaded, the other parts are no longer wanted".
/// </summary>
public class EventPartCompletenessTests
{
    // UFC PPV parts: Early Prelims (1), Prelims (2), Main Card (3), Post Show (4).
    private const string PpvTitle = "UFC 300";
    private const string FightNightTitle = "UFC Fight Night: Smith vs Jones";

    private static bool Check(
        string sport,
        string? title,
        string? eventMonitoredParts,
        int?[] presentPartNumbers,
        bool enableMultiPart = true,
        string leagueName = "UFC",
        string? leagueMonitoredParts = null)
        => EventPartDetector.AreAllMonitoredPartsPresent(
            sport, title, leagueName, eventMonitoredParts, leagueMonitoredParts,
            presentPartNumbers, enableMultiPart);

    [Fact]
    public void MultiPartDisabled_AnyFile_IsComplete()
    {
        // Setting off: one file means the event is done, exactly like before the fix.
        Check("MMA", PpvTitle, null, new int?[] { 3 }, enableMultiPart: false)
            .Should().BeTrue();
    }

    [Fact]
    public void NonFightingSport_AnyFile_IsComplete()
    {
        // Team sports never split into parts; one file satisfies the event.
        Check("Ice Hockey", "Rangers vs Devils", null, new int?[] { null }, leagueName: "NHL")
            .Should().BeTrue();
    }

    [Fact]
    public void Ppv_OnlyMainCard_IsNotComplete()
    {
        // The reported bug: importing just the Main Card must NOT satisfy the event -
        // Early Prelims and Prelims are still monitored and missing.
        Check("MMA", PpvTitle, null, new int?[] { 3 }).Should().BeFalse();
    }

    [Fact]
    public void Ppv_AllRealParts_NoPostShow_IsComplete()
    {
        // Early Prelims + Prelims + Main Card present. Post Show is optional and must
        // not keep the event stuck in Wanted forever.
        Check("MMA", PpvTitle, null, new int?[] { 1, 2, 3 }).Should().BeTrue();
    }

    [Fact]
    public void Ppv_MissingPrelims_IsNotComplete()
    {
        // Early Prelims + Main Card present but Prelims missing -> still wanted.
        Check("MMA", PpvTitle, null, new int?[] { 1, 3 }).Should().BeFalse();
    }

    [Fact]
    public void Ppv_FullEventFile_IsComplete()
    {
        // A whole-card single file (no part number) satisfies everything.
        Check("MMA", PpvTitle, null, new int?[] { null }).Should().BeTrue();
    }

    [Fact]
    public void Ppv_OnlyMainCardMonitored_MainCardPresent_IsComplete()
    {
        // User monitors only the Main Card: having it is enough.
        Check("MMA", PpvTitle, "Main Card", new int?[] { 3 }).Should().BeTrue();
    }

    [Fact]
    public void Ppv_MonitoredPartsInheritedFromLeague_IsRespected()
    {
        // Event has no own MonitoredParts (null = inherit); league says Main Card only.
        EventPartDetector.AreAllMonitoredPartsPresent(
            "MMA", PpvTitle, "UFC", null, "Main Card", new int?[] { 3 }, true)
            .Should().BeTrue();
    }

    [Fact]
    public void FightNight_BothParts_IsComplete()
    {
        // Fight Night has just Prelims (1) + Main Card (2) - no Early Prelims.
        Check("MMA", FightNightTitle, null, new int?[] { 1, 2 }).Should().BeTrue();
    }

    [Fact]
    public void FightNight_OnlyMainCard_IsNotComplete()
    {
        Check("MMA", FightNightTitle, null, new int?[] { 2 }).Should().BeFalse();
    }

    [Fact]
    public void NoFilesAtAll_IsNotComplete()
    {
        Check("MMA", PpvTitle, null, System.Array.Empty<int?>()).Should().BeFalse();
    }
}
