using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue 379. A league that offers Main Card as the main part must accept a
/// wrestling release that the detector names Main Show.
/// </summary>
public sealed class AewMainPartTests
{
    private static ReleaseEvaluator Evaluator()
    {
        var cache = new CustomFormatMatchCache(NullLogger<CustomFormatMatchCache>.Instance);
        return new ReleaseEvaluator(NullLogger<ReleaseEvaluator>.Instance,
            new EventPartDetector(NullLogger<EventPartDetector>.Instance), cache);
    }

    private static ReleaseSearchResult Release(string title) => new()
    {
        Title = title,
        Guid = title,
        DownloadUrl = "http://test/" + title,
        Indexer = "Test",
        Protocol = "Torrent",
        Size = 8L * 1024 * 1024 * 1024,
    };

    [Theory]
    [InlineData("AEW.WrestleDream.2026.PPV.MyAEW.1080p.WEB.h264-HEEL")]
    [InlineData("AEW WrestleDream PPV 2026 1080p WEBRip h264 Star [TJET]")]
    public void AMainCardRequestAcceptsAMainShowRelease(string title)
    {
        var evaluation = Evaluator().EvaluateRelease(Release(title), null,
            requestedPart: "Main Card", sport: "Combat", eventTitle: "WrestleDream", leagueName: "AEW");

        evaluation.Rejections.Should().BeEmpty();
    }

    [Fact]
    public void MainCardAndMainShowCountAsTheSamePartInEitherOrderAndAnyCase()
    {
        PartIdentityResolver.PartNamesMatch("Main Card", "Main Show").Should().BeTrue();
        PartIdentityResolver.PartNamesMatch("main show", "MAIN CARD").Should().BeTrue();
    }

    [Theory]
    [InlineData("Prelims", "Main Card")]
    [InlineData("Early Prelims", "Main Show")]
    [InlineData("Post Show", "Main Card")]
    [InlineData("Countdown", "Main Show")]
    [InlineData("Main Event", "Main Card")]
    public void OtherPartsAreStillDifferentParts(string detected, string requested)
    {
        PartIdentityResolver.PartNamesMatch(detected, requested).Should().BeFalse();
    }

    [Fact]
    public void ADifferentPartIsStillRejectedByTheEvaluator()
    {
        var evaluation = Evaluator().EvaluateRelease(
            Release("UFC.300.Early.Prelims.1080p.WEB.h264-GRP"), null,
            requestedPart: "Main Card", sport: "Fighting", eventTitle: "UFC 300", leagueName: "UFC");

        evaluation.Approved.Should().BeFalse();
        evaluation.Rejections.Should().Contain(r => r.Contains("Wrong part"));
    }

    private static ReleaseMatchingService Matcher() => new(
        NullLogger<ReleaseMatchingService>.Instance,
        new SportsFileNameParser(NullLogger<SportsFileNameParser>.Instance),
        new EventPartDetector(NullLogger<EventPartDetector>.Instance));

    private static Event WrestleDream(string? monitoredParts = null) => new()
    {
        Id = 1,
        Title = "WrestleDream",
        Sport = "Combat",
        EventDate = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc),
        Monitored = true,
        MonitoredParts = monitoredParts,
        League = new League { Id = 1, Name = "AEW", Sport = "Combat" },
    };

    [Theory]
    [InlineData("AEW.WrestleDream.2026.PPV.MyAEW.1080p.WEB.h264-HEEL", "Main Card")]
    [InlineData("AEW.WrestleDream.2026.PPV.MyAEW.1080p.WEB.h264-HEEL", "Main Show")]
    [InlineData("AEW WrestleDream PPV 2026 1080p WEBRip h264 Star [TJET]", "Main Card")]
    [InlineData("AEW WrestleDream PPV 2026 1080p WEBRip h264 Star [TJET]", "Main Show")]
    public void ValidateReleaseApprovesTheReportedTitlesForEitherMainName(string title, string requested)
    {
        var result = Matcher().ValidateRelease(Release(title), WrestleDream(), requested, true);

        result.IsHardRejection.Should().BeFalse();
        result.Rejections.Should().BeEmpty();
    }

    [Fact]
    public void ValidateReleaseStillRejectsAPreShowForAMainRequest()
    {
        var result = Matcher().ValidateRelease(
            Release("AEW.WrestleDream.2026.PPV.Zero.Hour.1080p.WEB.h264-HEEL"),
            WrestleDream(), "Main Card", true);

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Wrong part"));
    }

    [Fact]
    public void ValidateReleaseStillRejectsAUfcPrelimsReleaseForAMainCardRequest()
    {
        var evt = new Event
        {
            Id = 2, Title = "UFC 300", Sport = "Combat", Monitored = true,
            EventDate = new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc),
            League = new League { Id = 2, Name = "UFC", Sport = "Combat" },
        };

        var result = Matcher().ValidateRelease(
            Release("UFC.300.Prelims.1080p.WEB.h264-GRP"), evt, "Main Card", true);

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("Wrong part"));
    }

    [Theory]
    [InlineData("Main Card")]
    [InlineData("Main Show")]
    [InlineData("main card")]
    public void AnAewMainRequestResolvesToTheEventsOwnMainShowPart(string requested)
    {
        var resolved = PartIdentityResolver.Resolve(requested, null, null, "Combat", "WrestleDream", "AEW", true);

        resolved.Kind.Should().Be(PartIdentityKind.ExplicitPart);
        resolved.Part!.SegmentName.Should().Be("Main Show");
        resolved.Part.PartNumber.Should().Be(2);
        EventPartDetector.ResolvePartNumber(requested, "Combat", "WrestleDream", "AEW").Should().Be(2);
        EventPartDetector.CanonicalPartName(requested, "Combat", "WrestleDream", "AEW").Should().Be("Main Show");
    }

    [Fact]
    public void AUfcMainCardRequestStaysMainCard()
    {
        var resolved = PartIdentityResolver.Resolve("Main Card", null, null, "Combat", "UFC 300", "UFC", true);

        resolved.Kind.Should().Be(PartIdentityKind.ExplicitPart);
        resolved.Part!.SegmentName.Should().Be("Main Card");
        EventPartDetector.CanonicalPartName("Main Card", "Combat", "UFC 300", "UFC").Should().Be("Main Card");
        EventPartDetector.CanonicalPartName("Prelims", "Combat", "UFC 300", "UFC").Should().Be("Prelims");
    }

    [Fact]
    public void ARequestForAPartTheEventLacksIsNotRenamed()
    {
        // A weekly show defines no parts, so the name has nothing to map to.
        EventPartDetector.CanonicalPartName("Main Card", "Combat", "Dynamite", "AEW").Should().Be("Main Card");
        EventPartDetector.CanonicalPartName("Prelims", "Combat", "WrestleDream", "AEW").Should().Be("Prelims");
    }

    [Fact]
    public void AGrabForAMainCardRequestCarriesTheEventsOwnPartName()
    {
        // The queue row and the history entry store this name, so a duplicate
        // check against a release named Main Show finds the row.
        var detected = new EventPartDetector(NullLogger<EventPartDetector>.Instance).DetectPart(
            "AEW.WrestleDream.2026.PPV.MyAEW.1080p.WEB.h264-HEEL", "Combat", "WrestleDream", "AEW");
        var resolved = PartIdentityResolver.Resolve("Main Card", null, null, "Combat", "WrestleDream", "AEW", true);

        resolved.Part!.SegmentName.Should().Be(detected!.SegmentName);
    }

    [Fact]
    public void AMainShowReleaseIsMonitoredWhenTheStoredSetListsMainCard()
    {
        var monitored = new[] { "Early Prelims", "Prelims", "Main Card" };

        EventPartDetector.IsMonitored(monitored, "Main Show").Should().BeTrue();
        EventPartDetector.IsMonitored(monitored, "Main Card").Should().BeTrue();
        EventPartDetector.IsMonitored(monitored, "Post Show").Should().BeFalse();
        EventPartDetector.IsMonitored(monitored, "Countdown").Should().BeFalse();
        EventPartDetector.IsMonitored(new[] { "Early Prelims", "Prelims" }, "Main Show").Should().BeFalse();
    }

    [Fact]
    public void TheStatusListMarksMainShowMonitoredWhenTheStoredSetListsMainCard()
    {
        var evt = new Event
        {
            Title = "WrestleDream", Sport = "Fighting", Monitored = true,
            MonitoredParts = "Early Prelims,Prelims,Main Card",
            League = new League { Name = "AEW", Sport = "Fighting" },
        };

        var statuses = EventResponse.FromEvent(evt, enableMultiPartEpisodes: true, filesLoaded: true).PartStatuses!;

        statuses.Single(p => p.PartName == "Main Show").Monitored.Should().BeTrue();
        statuses.Single(p => p.PartName == "Countdown").Monitored.Should().BeFalse();
    }

    [Fact]
    public void ACountdownOnlyFileDoesNotCompleteAnEventThatMonitorsTheMainShow()
    {
        // Stored set from a user who unchecked Post Show. Main Show is part 2.
        var present = new int?[] { 1 };

        EventPartDetector.AreAllMonitoredPartsPresent("Combat", "WrestleDream", "AEW",
            "Early Prelims,Prelims,Main Card", null, present, true).Should().BeFalse();
        EventPartDetector.AreAllMonitoredPartsPresent("Combat", "WrestleDream", "AEW",
            "Early Prelims,Prelims,Main Card", null, new int?[] { 1, 2 }, true).Should().BeTrue();
    }

    [Theory]
    [InlineData("Main Card")]
    [InlineData("Main Show")]
    public async Task TheSearchQueueAcceptsAMainRequestForAnAewPayPerView(string requested)
    {
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .Options;
        await using (var seed = new SportarrDbContext(options))
        {
            var league = new League { Id = 5, Name = "AEW", Sport = "Combat" };
            seed.Leagues.Add(league);
            seed.Events.Add(new Event
            {
                Id = 379, LeagueId = league.Id, Title = "WrestleDream", Sport = "Combat",
                EventDate = new DateTime(2026, 10, 18), Monitored = true
            });
            await seed.SaveChangesAsync();
        }

        await using var searchDb = new SportarrDbContext(options);
        using var services = new ServiceCollection().AddSingleton(searchDb).BuildServiceProvider();
        var queue = new SearchQueueService(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SearchQueueService>.Instance);

        var item = await queue.QueueSearchAsync(379, requested);

        item.Message.Should().NotContain("Invalid part");
        item.Part.Should().Be("Main Show");
        (await searchDb.Tasks.SingleAsync()).Body.Should().Be("379|Main Show");
    }
}
