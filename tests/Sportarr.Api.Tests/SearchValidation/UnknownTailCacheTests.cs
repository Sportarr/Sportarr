using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class UnknownTailCacheTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HealthyUnknownTailReusesRawRowsAndPreservesIncompleteDiagnostics(bool automatic)
    {
        await using var rig = await UnknownTailCacheHarness.CreateAsync(output);
        Assert.Equal(UnknownTailCacheHarness.Query, rig.Event.League!.SearchQueryTemplate);
        Assert.Equal(new[] { UnknownTailCacheHarness.Query }, rig.Queries);
        var contract = await rig.ContractHashAsync();
        var initial = await rig.ReadStateAsync();
        rig.StartMeasurement();
        var cold = await rig.RunAsync("cold-unknown-tail", automatic);
        var warm = await rig.RunAsync("warm-unknown-tail", automatic);
        var queue = await rig.QueueCountAsync();
        var expected = Enumerable.Range(1, 3).Select(number => "probe-offer-" + number).ToArray();
        using var scope = new AssertionScope();
        initial.Queries.Should().Be(0);
        initial.LastSuccess.Should().BeNull();
        cold.Attempts.Select(attempt => attempt.Mode).Should().Equal("caps", "search", "search");
        var pages = cold.Attempts.Where(attempt => attempt.Mode == "search").ToArray();
        pages.Select(attempt => attempt.Query).Should().Equal(UnknownTailCacheHarness.Query, UnknownTailCacheHarness.Query);
        pages.Select(attempt => attempt.Offset).Should().Equal(0, 2);
        pages.SelectMany(attempt => attempt.Guids).Should().Equal(expected);
        warm.Attempts.Should().BeEmpty();
        rig.Transport.Attempts.Should().HaveCount(3);
        cold.State.Queries.Should().Be(3);
        cold.State.LastSuccess.Should().BeNull("unknown traversal completeness must not invent provider success");
        warm.State.Should().Be(cold.State);
        foreach (var phase in new[] { cold, warm })
        {
            phase.ContractHash.Should().Be(contract);
            phase.Found.Should().Be(3);
            phase.Selected.Should().BeNull();
            phase.State.Grabs.Should().Be(0);
            phase.State.QueryFailures.Should().Be(0);
            phase.State.ConnectionErrors.Should().Be(0);
            phase.State.RateLimitedUntil.Should().BeNull();
            phase.EndMilliseconds.Should().BeLessThan(45_000);
            phase.EndMilliseconds.Should().BeGreaterThanOrEqualTo(phase.StartMilliseconds);
            using var body = JsonDocument.Parse(phase.Body);
            body.RootElement.GetProperty(automatic ? "SearchComplete" : "searchComplete").GetBoolean().Should().BeFalse();
            var diagnostics = body.RootElement.GetProperty(automatic ? "SearchDiagnostics" : "searchDiagnostics")
                .EnumerateArray().Select(row => row.Clone()).ToArray();
            AssertDiagnostic(diagnostics, rig, camelCase: !automatic);
            if (!automatic)
            {
                var results = body.RootElement.GetProperty("results").EnumerateArray().ToArray();
                results.Select(row => row.GetProperty("guid").GetString()).OrderBy(guid => guid).Should().Equal(expected);
                results.Should().OnlyContain(row => !row.GetProperty("approved").GetBoolean()
                    && row.GetProperty("rejections").GetArrayLength() > 0);
            }
            phase.CacheEntries.Should().ContainSingle();
            var entry = phase.CacheEntries.Single();
            entry.Guids.Should().Equal(expected);
            entry.LifetimeSeconds.Should().BeInRange(300 - (int)Math.Ceiling((cold.EndMilliseconds - cold.StartMilliseconds) / 1000), 300);
            entry.MetadataJson.Should().NotBeNull();
            if (entry.MetadataJson != null)
            {
                using var metadata = JsonDocument.Parse(entry.MetadataJson);
                var hasComplete = metadata.RootElement.TryGetProperty("SearchComplete", out var complete);
                hasComplete.Should().BeTrue("the raw entry must retain its incomplete result metadata");
                if (hasComplete) complete.GetBoolean().Should().BeFalse();
                var hasDiagnostics = metadata.RootElement.TryGetProperty("SearchDiagnostics", out var savedDiagnostics);
                hasDiagnostics.Should().BeTrue("the warm answer must retain actual source diagnostics");
                if (hasDiagnostics) AssertDiagnostic(savedDiagnostics.EnumerateArray().Select(row => row.Clone()).ToArray(), rig, camelCase: false);
            }
            foreach (var attempt in phase.Attempts)
            {
                attempt.Status.Should().Be(200);
                attempt.ResponseSent.Should().BeTrue();
                attempt.RowId.Should().Be(rig.Indexer.Id.ToString());
                attempt.ResponseSha256.Should().Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(attempt.ResponseBody))));
                var xml = XDocument.Parse(attempt.ResponseBody);
                if (attempt.Mode == "caps")
                {
                    xml.Root!.Element("limits")!.Attribute("max")!.Value.Should().Be("2");
                    continue;
                }
                attempt.EventId.Should().Be(rig.Event.ExternalId);
                var response = xml.Descendants(XName.Get("response", "http://www.newznab.com/DTD/2010/feeds/attributes/")).Single();
                response.Attribute("total").Should().BeNull();
                var items = xml.Descendants("item").ToArray();
                items.Select(item => item.Element("guid")!.Value).Should().Equal(attempt.Guids);
                foreach (var item in items)
                {
                    var row = rig.Transport.Releases.Single(release => release.Guid == item.Element("guid")!.Value);
                    item.Element("title")!.Value.Should().Be(row.Title);
                    item.Element("enclosure")!.Attribute("length")!.Value.Should().Be(row.Size.ToString());
                    item.Elements(XName.Get("attr", "http://www.newznab.com/DTD/2010/feeds/attributes/"))
                        .Single(attribute => attribute.Attribute("name")!.Value == "sportarrid")
                        .Attribute("value")!.Value.Should().Be(row.SportarrEventId);
                }
            }
        }
        warm.CacheEntries.Select(entry => entry.Key).Should().Equal(cold.CacheEntries.Select(entry => entry.Key));
        warm.CacheEntries.Single().LifetimeSeconds.Should().Be(cold.CacheEntries.Single().LifetimeSeconds);
        queue.Should().Be(0);
        rig.Transport.Violations.Should().BeEmpty();
    }

    private static void AssertDiagnostic(JsonElement[] diagnostics, UnknownTailCacheHarness rig, bool camelCase)
    {
        string Key(string name) => camelCase ? char.ToLowerInvariant(name[0]) + name[1..] : name;
        diagnostics.Should().ContainSingle();
        if (diagnostics.Length != 1) return;
        var row = diagnostics[0];
        row.GetProperty(Key("IndexerId")).GetInt32().Should().Be(rig.Indexer.Id);
        row.GetProperty(Key("Name")).GetString().Should().Be(rig.Indexer.Name);
        row.GetProperty(Key("Query")).GetString().Should().Be(UnknownTailCacheHarness.Query);
        row.GetProperty(Key("Termination")).GetString().Should().Be("UnknownTail");
        row.GetProperty(Key("SatisfiesRequest")).GetBoolean().Should().BeFalse();
        row.GetProperty(Key("RawCursor")).GetInt32().Should().Be(3);
        row.GetProperty(Key("KnownPageLimit")).GetBoolean().Should().BeTrue();
        var pages = row.GetProperty(Key("Pages")).EnumerateArray().ToArray();
        pages.Should().HaveCount(2);
        pages.Select(page => page.GetProperty(Key("RequestedOffset")).GetInt32()).Should().Equal(0, 2);
        pages.Select(page => page.GetProperty(Key("RequestedLimit")).GetInt32()).Should().Equal(2, 2);
        pages.Select(page => page.GetProperty(Key("RawCount")).GetInt32()).Should().Equal(2, 1);
        var reportedTotalKey = Key("ReportedTotal");
        var metadataValidKey = Key("MetadataValid");
        pages.Should().OnlyContain(page => page.GetProperty(reportedTotalKey).ValueKind == JsonValueKind.Null
            && page.GetProperty(metadataValidKey).GetBoolean());
    }
}
