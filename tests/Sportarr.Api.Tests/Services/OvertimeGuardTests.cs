using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The overtime guard extends a recording only on POSITIVE evidence that
/// the event is still in progress. These pin the status classification:
/// terminal and pre-game labels must never extend; live labels must.
/// </summary>
public class OvertimeGuardTests
{
    [Theory]
    [InlineData("Live")]
    [InlineData("1st Half")]
    [InlineData("2nd Half")]
    [InlineData("HT")]
    [InlineData("Q4")]
    [InlineData("OT")]
    [InlineData("ET")]
    [InlineData("In Progress")]
    [InlineData("Top 9th")]
    [InlineData("3rd Period")]
    public void LiveStatuses_IndicateInProgress(string status)
    {
        DvrRecordingService.IndicatesInProgress(status).Should().BeTrue();
    }

    [Theory]
    [InlineData("Match Finished")]
    [InlineData("Finished")]
    [InlineData("Final")]
    [InlineData("FT")]
    [InlineData("AET")]
    [InlineData("AOT")]
    [InlineData("PEN")]
    [InlineData("Completed")]
    [InlineData("Game Ended")]
    [InlineData("After Over Time")]
    [InlineData("Postponed")]
    [InlineData("Cancelled")]
    [InlineData("Canceled")]
    [InlineData("Abandoned")]
    [InlineData("Suspended")]
    public void TerminalStatuses_DoNotIndicateInProgress(string status)
    {
        DvrRecordingService.IndicatesInProgress(status).Should().BeFalse();
    }

    [Theory]
    [InlineData("Scheduled")]
    [InlineData("Not Started")]
    [InlineData("NS")]
    [InlineData("TBD")]
    [InlineData("Time To Be Defined")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void PreGameAndEmptyStatuses_DoNotIndicateInProgress(string? status)
    {
        DvrRecordingService.IndicatesInProgress(status).Should().BeFalse();
    }
}
