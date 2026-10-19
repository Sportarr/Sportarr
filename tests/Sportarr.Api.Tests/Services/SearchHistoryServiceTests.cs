using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Serilog.Events;
using Serilog.Parsing;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.Services;

public class SearchHistoryServiceTests
{
    private static ReleaseSearchResult Release(string title, int matchScore = 80, int score = 100,
        params string[] rejections) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "https://indexer.example/get/" + title,
        Indexer = "Indexer A",
        Quality = "WEBDL-1080p",
        Size = 4L * 1024 * 1024 * 1024,
        MatchScore = matchScore,
        Score = score,
        Approved = rejections.Length == 0,
        Rejections = rejections.ToList(),
    };

    private static SearchTrace Trace(params ReleaseSearchResult[] releases)
    {
        var trace = new SearchTrace(new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));
        trace.Start("Monza Grand Prix", null);
        trace.TrackReleases(() => releases);
        trace.QualityProfile = "HD-1080p";
        return trace;
    }

    [Fact]
    public void BuildEntry_CountsRejectionsByGroupWhenNothingPassed()
    {
        var trace = Trace(
            Release("A.720p", rejections: "Quality WEBDL-720p is not wanted in quality profile"),
            Release("B.480p", rejections: "Quality SDTV is not wanted in quality profile"),
            Release("C.huge", rejections: "Size 40.00GB exceeds maximum 20.00GB for WEBDL-1080p (100MB/min × 180min runtime)"));
        var result = new AutomaticSearchResult { Success = false, Message = "No approved releases found.", ReleasesFound = 3 };

        var (entry, reasons) = SearchHistoryService.BuildEntry(7, false, result, trace);

        entry.Outcome.Should().Be(SearchOutcome.NothingPassed);
        entry.ReleasesFound.Should().Be(3);
        entry.ReleasesPassed.Should().Be(0);
        entry.QualityProfile.Should().Be("HD-1080p");
        reasons.Should().Equal(new SearchReasonCount("Quality not allowed", 2), new SearchReasonCount("File size outside the limits", 1));
        SearchHistoryService.Summarize(entry, reasons).Should()
            .Be("nothing passed. 3 found, 0 passed. Rejected: quality not allowed 2, file size outside the limits 1");
    }

    [Fact]
    public void BuildEntry_MarksTheGrabbedReleaseAndKeepsItFirst()
    {
        var rejected = Release("Rejected.Release", matchScore: 95, rejections: "Release is blocklisted");
        var other = Release("Other.Passed", score: 50);
        var grabbed = Release("Grabbed.Release", score: 200);
        var trace = Trace(rejected, other, grabbed);
        trace.Selected = grabbed;
        var result = new AutomaticSearchResult { Success = true, Message = "Download started successfully",
            ReleasesFound = 3, SelectedRelease = "Grabbed.Release" };

        var (entry, _) = SearchHistoryService.BuildEntry(7, true, result, trace);
        var rows = JsonSerializer.Deserialize<List<SearchReleaseRow>>(entry.ReleasesJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        entry.Outcome.Should().Be(SearchOutcome.Grabbed);
        entry.IsManual.Should().BeTrue();
        entry.SelectedRelease.Should().Be("Grabbed.Release");
        entry.ReleasesPassed.Should().Be(2);
        rows.Select(r => (r.Title, r.Status)).Should().Equal(
            ("Grabbed.Release", "grabbed"), ("Other.Passed", "passed"), ("Rejected.Release", "rejected"));
    }

    [Fact]
    public void BuildEntry_RecordsAHeldReleaseAsRejectedWithoutChangingIt()
    {
        var weak = Release("Weak.Match", matchScore: 40);
        var trace = Trace(weak);
        trace.Hold(weak, $"{SearchHistoryService.LowConfidencePrefix}40 is below the automatic grab minimum of 70");
        var result = new AutomaticSearchResult { Success = false, Message = "Found 1 releases but none have sufficient match confidence", ReleasesFound = 1 };

        var (entry, reasons) = SearchHistoryService.BuildEntry(7, false, result, trace);

        entry.Outcome.Should().Be(SearchOutcome.NothingPassed);
        reasons.Should().Equal(new SearchReasonCount("Not sure it is this event", 1));
        weak.Rejections.Should().BeEmpty();
        weak.Approved.Should().BeTrue();
    }

    [Fact]
    public void BuildEntry_SeparatesNoResultsFromPassedButNotGrabbed()
    {
        var empty = SearchHistoryService.BuildEntry(7, false,
            new AutomaticSearchResult { Success = false, Message = "No releases found" }, Trace()).Entry;
        var cutoff = SearchHistoryService.BuildEntry(7, false,
            new AutomaticSearchResult { Success = false, Message = "Cutoff met", ReleasesFound = 1 }, Trace(Release("Good"))).Entry;

        empty.Outcome.Should().Be(SearchOutcome.NoResults);
        SearchHistoryService.Summarize(empty, Array.Empty<SearchReasonCount>()).Should().Be("no results");
        cutoff.Outcome.Should().Be(SearchOutcome.NotGrabbed);
        SearchHistoryService.Summarize(cutoff, Array.Empty<SearchReasonCount>()).Should()
            .Be("not grabbed, Cutoff met. 1 found, 1 passed");
    }

    [Fact]
    public void Summarize_DropsTheErrorPrefixAndTheTrailingFullStop()
    {
        var failed = SearchHistoryService.BuildEntry(7, false,
            new AutomaticSearchResult { Success = false, Message = "Error: The client job already belongs to another event.", ReleasesFound = 1 },
            Trace(Release("Good"))).Entry;

        failed.Outcome.Should().Be(SearchOutcome.Failed);
        SearchHistoryService.Summarize(failed, Array.Empty<SearchReasonCount>()).Should()
            .Be("failed, The client job already belongs to another event. 1 found, 1 passed");
    }

    [Fact]
    public void BuildEntry_RecordsAnEarlyStopWithItsOwnReasonNotAsNoResults()
    {
        var entry = SearchHistoryService.BuildEntry(7, false,
            new AutomaticSearchResult { Success = false, Message = "No quality profile configured. Add at least one quality profile under Settings -> Profiles." },
            Trace()).Entry;
        var dto = SearchHistoryService.ToDto(entry, includeReleases: false);

        entry.Outcome.Should().Be(SearchOutcome.NotGrabbed);
        dto.Headline.Should().Be("No quality profile is set up, so Sportarr cannot judge releases.");
    }

    [Fact]
    public async Task RecordAsync_KeepsFiveSearchesAndTheReleaseListOnlyOnTheNewestOfEachPart()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<SportarrDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Leagues.Add(new League { Id = 1, Name = "UFC", Sport = "Fighting" });
            db.Events.Add(new Event { Id = 7, Title = "UFC 300", Sport = "Fighting", LeagueId = 1, EventDate = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        var history = new SearchHistoryService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SearchHistoryService>.Instance);
        var result = new AutomaticSearchResult { Success = false, Message = "No approved releases found.", ReleasesFound = 1 };

        async Task Search(string? part)
        {
            var trace = new SearchTrace(DateTime.UtcNow);
            trace.Start("UFC 300", part);
            var release = Release("UFC.300", rejections: "Release is blocklisted");
            trace.TrackReleases(() => new[] { release });
            await history.RecordAsync(7, false, result, trace);
        }

        await Search("Main Card");
        await Search("Prelims");
        for (var i = 0; i < 5; i++)
            await Search("Main Card");

        using var check = provider.CreateScope();
        var rows = await check.ServiceProvider.GetRequiredService<SportarrDbContext>().SearchHistory
            .OrderByDescending(s => s.Id).ToListAsync();
        rows.Should().HaveCount(SearchHistoryService.KeepPerEvent);
        rows.Where(r => r.ReleasesJson != null).Select(r => r.Part).Should().Equal("Main Card");
        rows[0].ReleasesJson.Should().NotBeNull();
        rows.Should().NotContain(r => r.Part == "Prelims");
    }

    [Fact]
    public void BuildEntry_KeepsAtMostTheReleaseRowLimit()
    {
        var releases = Enumerable.Range(0, 80)
            .Select(i => Release($"Release.{i}", matchScore: i, rejections: "Release is blocklisted"))
            .ToArray();

        var (entry, _) = SearchHistoryService.BuildEntry(7, false,
            new AutomaticSearchResult { Success = false, Message = "No approved releases found.", ReleasesFound = 80 }, Trace(releases));
        var rows = JsonSerializer.Deserialize<List<SearchReleaseRow>>(entry.ReleasesJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        rows.Should().HaveCount(SearchHistoryService.MaxReleaseRows);
        rows[0].Title.Should().Be("Release.79");
        entry.ReleasesFound.Should().Be(80);
    }

    [Theory]
    [InlineData("Quality SDTV is not wanted in quality profile", "Quality not allowed")]
    [InlineData("Custom format score -10000 is below minimum 0", "Custom format score too low")]
    [InlineData("Release doesn't match event (score: 12)", "Different event")]
    [InlineData("Release profile 'English': Missing required keyword(s): English", "Blocked by a release profile")]
    [InlineData("Wrong part: requested 'Prelims' but release contains 'Main Card'", "Wrong part of the event")]
    [InlineData("Release is 4000 days old (retention: 3000 days)", "Older than your retention setting")]
    [InlineData("Participant category does not match event", "Different category")]
    [InlineData("NASCAR release does not identify the selected race", "Name does not say which event")]
    [InlineData("NASCAR release does not name the selected support session", "Name does not say which session")]
    [InlineData("Race mismatch: release is Race 1 and 2, event is Race 3", "Different round or session")]
    [InlineData("Release is pre-season testing but event is a race weekend", "Different round or session")]
    [InlineData("No league identity: release names neither 'Formula 1' nor anything from the event title", "Name does not say which event")]
    [InlineData("Location mismatch: release contains 'Steamboat' but event is 'Aspen'", "Different place")]
    [InlineData("Multi-part disabled: rejecting part file 'Prelims' (only full event files accepted)", "Part of an event")]
    [InlineData("Round mismatch: release is round 3, event is round 4", "Different round or session")]
    [InlineData("Year mismatch: release is 2019, event is 2025", "Different date")]
    [InlineData("Insufficient evidence: release identified only by year/season-episode, which does not map to Sportarr's event numbering", "Name does not say which event")]
    [InlineData("Event is 'qualifying' but release has no session indicator", "Name does not say which session")]
    [InlineData("Release posted 30.0h before event aired, exceeds 1d early-release limit", "Posted before the event happened")]
    [InlineData("Team names not found in release", "Teams or players do not match")]
    [InlineData("Combat event identity conflicts with the selected event", "Belongs to another event")]
    [InlineData("NASCAR release is from another series", "Belongs to another event")]
    [InlineData("Something unusual happened 42 times in the release name of the event title here", "Something unusual happened # times in the release name of...")]
    [InlineData(null, "Other")]
    public void ReasonGroup_GivesEveryKnownReasonAPlainTitle(string? reason, string expected)
    {
        SearchHistoryService.ReasonGroup(reason).Should().Be(expected);
    }

    [Theory]
    [InlineData("failed", "Error: The client job already belongs to another event or part.", "Your download client already has this download for another event or part")]
    [InlineData("notGrabbed", "Automatic retry is disabled for this failed download", "automatic retry is off")]
    [InlineData("notGrabbed", "Download retry limit (3) reached", "failed 3 times")]
    [InlineData("nothingPassed", "Found 3 releases but none have sufficient match confidence (need score >= 70)", "none matched this event closely enough")]
    [InlineData("notGrabbed", "Cutoff met - existing file (WEBDL-1080p) meets quality profile requirements. No upgrade needed.", "You already have a file that meets your quality profile")]
    [InlineData("notGrabbed", "No Usenet download client configured", "No download client is set up for this kind of release")]
    [InlineData("notGrabbed", "No releases available (may be delayed or filtered)", "a delay profile holds them back")]
    [InlineData("failed", "Error: Object reference not set to an instance of an object.", "Something went wrong during the search.")]
    public void ForOutcome_ExplainsTheMessageInPlainWords(string outcome, string message, string expected)
    {
        SearchExplanations.ForOutcome(outcome, message, null).Headline.Should().Contain(expected);
    }

    [Fact]
    public void ForOutcome_SaysWhenNoIndexerWasAskedOrNoneAnswered()
    {
        SearchExplanations.ForOutcome(SearchOutcome.NoResults, "No releases found", null, indexersAsked: 0, indexersAnswered: 0)
            .Headline.Should().Be("No indexer was searched.");
        SearchExplanations.ForOutcome(SearchOutcome.NoResults, "No releases found", null, indexersAsked: 2, indexersAnswered: 0)
            .Headline.Should().Be("Your indexers did not answer, so nothing could be found.");
        SearchExplanations.ForOutcome(SearchOutcome.NoResults, "No releases found", null, indexersAsked: 2, indexersAnswered: 1)
            .Headline.Should().Be("Your indexers found no releases for this event.");
    }

    [Fact]
    public void PlainTitle_LeavesAnUnknownReasonUnchanged()
    {
        SearchExplanations.PlainTitle("Something unusual happened 42 times").Should().BeNull();
        SearchExplanations.PlainTitle("Release is blocklisted").Should().Be("On your blocklist");
    }

    [Fact]
    public void ForOutcome_KeepsTheTechnicalDetailForAnUnknownError()
    {
        var explanation = SearchExplanations.ForOutcome(SearchOutcome.Failed, "Error: Object reference not set to an instance of an object.", null);

        explanation.Help.Should().Be("Object reference not set to an instance of an object. See System > Logs for details.");
    }

    [Fact]
    public void ToDto_LeavesReleasesOutWhenAskedAndMarksTheTimeAsUtc()
    {
        var (entry, _) = SearchHistoryService.BuildEntry(7, false,
            new AutomaticSearchResult { Success = false, Message = "No approved releases found.", ReleasesFound = 1 },
            Trace(Release("A", rejections: "Release is blocklisted")));
        entry.SearchedAt = DateTime.SpecifyKind(entry.SearchedAt, DateTimeKind.Unspecified);

        var summary = SearchHistoryService.ToDto(entry, includeReleases: false);
        var full = SearchHistoryService.ToDto(entry, includeReleases: true);

        summary.Releases.Should().BeNull();
        full.Releases.Should().ContainSingle(r => r.Title == "A" && r.Reasons.Single() == "Release is blocklisted");
        summary.SearchedAt.Kind.Should().Be(DateTimeKind.Utc);
        summary.Reasons.Should().ContainSingle(r => r.Reason == "On your blocklist" && r.Help!.Contains("Activity > Blocklist"));
        full.Releases!.Single().Explanations.Should().Equal("On your blocklist");
        full.Headline.Should().Be("Releases were found, but every one was rejected.");
    }

    private static LogEvent Event(LogEventLevel level, string source) => new(
        DateTimeOffset.UtcNow, level, null, new MessageTemplate("x", Array.Empty<MessageTemplateToken>()),
        new[] { new LogEventProperty("SourceContext", new ScalarValue(source)) });

    [Fact]
    public void SearchDetailLog_SendsReleaseDecisionsToTheSearchFileAndKeepsWarningsInTheMainLog()
    {
        var evaluator = typeof(ReleaseEvaluator).FullName!;
        var profiles = typeof(ReleaseProfileService).FullName!;
        var search = typeof(AutomaticSearchService).FullName!;

        SearchDetailLog.IsSearchDetail(Event(LogEventLevel.Information, evaluator)).Should().BeTrue();
        SearchDetailLog.IsSearchDetail(Event(LogEventLevel.Information, profiles)).Should().BeTrue();
        SearchDetailLog.IsSearchDetail(Event(LogEventLevel.Information, search)).Should().BeFalse();

        SearchDetailLog.BelongsInMainLog(Event(LogEventLevel.Information, evaluator)).Should().BeFalse();
        SearchDetailLog.BelongsInMainLog(Event(LogEventLevel.Warning, evaluator)).Should().BeTrue();
        SearchDetailLog.BelongsInMainLog(Event(LogEventLevel.Information, search)).Should().BeTrue();
    }
}
