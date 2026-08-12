using System;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Anti-churn guard (issue #175). Reproduces the duplicate-download loop: two releases for
/// one still-missing event each supersede the other's GrabHistory row, so the old
/// <c>!Superseded</c> dedup let them ping-pong and re-fetch the same .nzb every RSS cycle.
/// The guard bounds re-fetching of the SAME un-imported release by a cooldown and a hard cap.
/// </summary>
public class GrabHistoryChurnGuardTests
{
    private static readonly DateTime Now = new(2026, 6, 28, 12, 30, 0, DateTimeKind.Utc);

    private static GrabHistory Grab(
        int regrabCount = 0,
        bool wasImported = false,
        DateTime? grabbedAt = null,
        DateTime? lastRegrab = null) => new()
    {
        Title = "Formula1.2026.Austrian.Grand.Prix.Practice.Three.1080p.WEB.h264-BILLIE",
        Indexer = "NZBfinder",
        DownloadUrl = "https://example.invalid/getnzb?id=abc.nzb",
        Guid = "abc",
        Protocol = "Usenet",
        GrabbedAt = grabbedAt ?? Now.AddMinutes(-5),
        LastRegrabAttempt = lastRegrab,
        RegrabCount = regrabCount,
        WasImported = wasImported,
    };

    [Fact]
    public void NullPriorGrab_Allows()
        => GrabHistoryChurnGuard.Evaluate(null, Now)
            .Should().Be(GrabHistoryChurnGuard.Decision.Allow);

    [Fact]
    public void ImportedPriorGrab_Allows_NotChurn()
        => GrabHistoryChurnGuard.Evaluate(Grab(wasImported: true, grabbedAt: Now.AddMinutes(-1)), Now)
            .Should().Be(GrabHistoryChurnGuard.Decision.Allow);

    [Fact]
    public void RecentUnimportedGrab_BlocksOnCooldown()
        => GrabHistoryChurnGuard.Evaluate(Grab(grabbedAt: Now.AddMinutes(-5)), Now)
            .Should().Be(GrabHistoryChurnGuard.Decision.BlockCooldown);

    [Fact]
    public void AfterCooldown_UnderCap_AllowsControlledRetry()
        => GrabHistoryChurnGuard.Evaluate(Grab(grabbedAt: Now.AddHours(-(GrabHistoryChurnGuard.RegrabCooldownHours + 1))), Now)
            .Should().Be(GrabHistoryChurnGuard.Decision.AllowControlledRetry);

    [Fact]
    public void AtCap_BlocksRegardlessOfAge()
        => GrabHistoryChurnGuard.Evaluate(
                Grab(regrabCount: GrabHistoryChurnGuard.MaxAutomaticRegrabs, grabbedAt: Now.AddDays(-30)), Now)
            .Should().Be(GrabHistoryChurnGuard.Decision.BlockCapReached);

    [Fact]
    public void NewestRowMustInheritTheCount_OrTheCapNeverApplies()
    {
        // Regression for the grab loop a user reported for three weeks. Both
        // callers add ONE GrabHistory row per grab, and this guard reads only
        // the newest row for a release. The count was written to the older row,
        // so the newest row always read zero and the same release went to the
        // download client every cooldown window forever. The log proved it:
        // "Controlled re-grab 1/3" repeated for weeks and never reached 2/3.
        var inherited = Grab(regrabCount: GrabHistoryChurnGuard.MaxAutomaticRegrabs, lastRegrab: Now.AddDays(-1));
        GrabHistoryChurnGuard.Evaluate(inherited, Now)
            .Should().Be(GrabHistoryChurnGuard.Decision.BlockCapReached);

        // Same release, same age, but with the count reset by a fresh row: the
        // guard can only keep retrying. This is the shape of the bug.
        var reset = Grab(regrabCount: 0, lastRegrab: Now.AddDays(-1));
        GrabHistoryChurnGuard.Evaluate(reset, Now)
            .Should().Be(GrabHistoryChurnGuard.Decision.AllowControlledRetry);
    }

    [Fact]
    public void LastRegrabAttempt_TakesPrecedenceOverGrabbedAt()
    {
        // Originally grabbed two days ago, but a retry was logged 10 minutes ago: still cooling down.
        var g = Grab(regrabCount: 1, grabbedAt: Now.AddDays(-2), lastRegrab: Now.AddMinutes(-10));
        GrabHistoryChurnGuard.Evaluate(g, Now)
            .Should().Be(GrabHistoryChurnGuard.Decision.BlockCooldown);
    }
}
