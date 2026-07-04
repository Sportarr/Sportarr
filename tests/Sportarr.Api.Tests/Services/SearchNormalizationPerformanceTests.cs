using System.Diagnostics;
using Sportarr.Api.Services;
using FluentAssertions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Guards the RSS-sync matching hot path. A production instance wedged for
/// 9+ hours with the RSS matching thread pinned inside
/// GenerateSearchVariations: ContainsWord/ReplaceWord interpolated a fresh
/// pattern into the static Regex methods on every call, which thrashes
/// .NET's global 15-entry regex cache (the fixed location/alias/demonym
/// vocabulary is far larger than 15), so every word check re-parsed and
/// rebuilt its automaton - hundreds of millions of times across a
/// 1,093-release x 1,616-event pass. Fixed with a compiled per-word regex
/// cache plus per-title memoization of the variation lists.
///
/// The perf bound here is intentionally enormous (a pass this size now
/// completes in well under a second; pre-fix it took hours) so CI jitter
/// can never flake it while still catching any reintroduction of
/// per-call regex construction.
/// </summary>
public class SearchNormalizationPerformanceTests
{
    [Fact]
    public void IsReleaseMatch_LargeRssStyleMatchingPass_CompletesQuickly()
    {
        // Shape mirrors the observed wedge: many distinct release titles
        // evaluated against many distinct event titles, with alias-bearing
        // words ("Mexican", "United States") present so the variation and
        // word-check machinery genuinely runs.
        var events = Enumerable.Range(0, 200)
            .Select(i => i % 3 == 0
                ? $"Mexico City Grand Prix Round {i}"
                : i % 3 == 1
                    ? $"United States Grand Prix Session {i}"
                    : $"Team Alpha {i} vs Team Beta {i}")
            .ToList();
        var releases = Enumerable.Range(0, 500)
            .Select(i => i % 2 == 0
                ? $"Formula1.2026.Mexican.Grand.Prix.Race.{i}.1080p.WEB-GROUP"
                : $"NBA.2026.Team.Gamma.{i}.vs.Team.Delta.{i}.720p.WEB-GROUP")
            .ToList();

        var sw = Stopwatch.StartNew();
        var matches = 0;
        foreach (var release in releases)
        {
            foreach (var evt in events)
            {
                if (SearchNormalizationService.IsReleaseMatch(release, evt))
                    matches++;
            }
        }
        sw.Stop();

        // 100,000 pairs. Post-fix this runs in well under a second; the old
        // per-call regex parsing took multiple minutes for this volume.
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30),
            because: "the matching hot path must never regress to per-call regex construction");
        matches.Should().BeGreaterThan(0, because: "the alias machinery should actually match the Mexican/Mexico pairs");
    }

    [Fact]
    public void GenerateSearchVariations_IsMemoizedPerTitle()
    {
        var first = SearchNormalizationService.GenerateSearchVariations("Mexico City Grand Prix Memo Check");
        var second = SearchNormalizationService.GenerateSearchVariations("Mexico City Grand Prix Memo Check");

        ReferenceEquals(first, second).Should().BeTrue(
            because: "repeat expansions of the same title must come from the cache, not be recomputed per release-event pair");
        first.Should().Contain("Mexico City Grand Prix Memo Check");
    }
}
