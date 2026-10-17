using FluentAssertions;
using FluentAssertions.Execution;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.SearchValidation;

[Collection(WallClockFixtureCollection.Name)]
public sealed class QuotaIncompleteCacheTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuotaIncompleteAnswerRefetchesAfterWindowRecoveryThenUsesCompleteWarmCache(bool automatic)
    {
        await VerifySelectionPrerequisiteAsync();
        await using var rig = await QuotaIncompleteCacheHarness.CreateAsync(output);
        var contract = await rig.ContractHashAsync();
        var initialState = await rig.ReadStateAsync();
        rig.StartMeasurement();
        var incomplete = await rig.RunAsync("quota-incomplete", automatic);
        await rig.ExpireWindowOnlyAsync();
        var expired = await rig.ReadStateAsync();
        var recovered = await rig.RunAsync("window-recovered", automatic);
        var warm = await rig.RunAsync("complete-warm", automatic);
        var queueCount = await rig.QueueCountAsync();
        var expected = Enumerable.Range(1, 5).Select(number => "offer-" + number).ToArray();

        using (new AssertionScope())
        {
            initialState.Queries.Should().Be(6);
            initialState.Grabs.Should().Be(0);
            var firstSource = incomplete.Attempts.Where(attempt => attempt.Mode != "descriptor").ToArray();
            firstSource.Select(attempt => attempt.Mode).Should().Equal("caps", "search");
            firstSource.Should().OnlyContain(attempt => attempt.Status == 200 && attempt.ResponseSent);
            firstSource.Where(attempt => attempt.Mode == "search").Select(attempt => attempt.Query).Should().Equal("cache-primary");
            firstSource.SelectMany(attempt => attempt.Guids).Should().Equal("offer-1", "offer-2");
            firstSource.Should().OnlyContain(attempt => attempt.RowId == rig.Indexer.Id.ToString());
            incomplete.State.Queries.Should().Be(8);
            incomplete.State.LastSuccess.Should().BeNull();
            expired.Queries.Should().Be(incomplete.State.Queries);
            expired.Grabs.Should().Be(incomplete.State.Grabs);
            expired.ResetAt.Should().BeBefore(DateTime.UtcNow);
            expired.ResetAt.Should().NotBe(incomplete.State.ResetAt);

            var recoveryPages = recovered.Attempts.Where(attempt => attempt.Mode != "descriptor").ToArray();
            recoveryPages.Select(attempt => attempt.Mode).Should().Equal("search", "search", "search");
            recoveryPages.Select(attempt => attempt.Query).Should().Equal("cache-primary", "cache-primary", "cache-secondary");
            recoveryPages.Select(attempt => attempt.Offset).Should().Equal(0, 2, 0);
            recoveryPages.SelectMany(attempt => attempt.Guids).Should().Equal(expected);
            recoveryPages.Should().OnlyContain(attempt => attempt.Status == 200 && attempt.ResponseSent && attempt.EventId == "ev-2336155");
            recoveryPages.Should().OnlyContain(attempt => attempt.RowId == rig.Indexer.Id.ToString());
            recovered.State.Queries.Should().Be(3);
            recovered.Found.Should().Be(5);
            warm.Found.Should().Be(5);
            warm.State.Queries.Should().Be(recovered.State.Queries);
            warm.Attempts.Where(attempt => attempt.Mode != "descriptor").Should().BeEmpty();
            foreach (var phase in new[] { incomplete, recovered, warm })
            {
                phase.ContractHash.Should().Be(contract);
                phase.State.QueryFailures.Should().Be(0);
                phase.State.ConnectionErrors.Should().Be(0);
                phase.State.RateLimitedUntil.Should().BeNull();
                phase.EndMilliseconds.Should().BeLessThan(45_000);
                phase.EndMilliseconds.Should().BeGreaterThanOrEqualTo(phase.StartMilliseconds);
                phase.CacheEntries.Select(entry => entry.Key).Should().Equal(incomplete.CacheEntries.Select(entry => entry.Key));
                if (automatic)
                {
                    var descriptors = phase.Attempts.Where(attempt => attempt.Mode == "descriptor").ToArray();
                    if (phase.Selected == null) descriptors.Should().BeEmpty();
                    else
                    {
                        descriptors.Should().ContainSingle();
                        var release = rig.Transport.Releases.SingleOrDefault(row => row.Title == phase.Selected);
                        release.Should().NotBeNull();
                        if (release != null) descriptors.Select(attempt => attempt.Request).Should().Equal(new Uri(release.DownloadUrl).AbsolutePath);
                        descriptors.Should().OnlyContain(attempt => attempt.Status == 410 && attempt.ResponseSent);
                    }
                }
                else phase.Attempts.Should().NotContain(attempt => attempt.Mode == "descriptor");
            }
            foreach (var phase in new[] { recovered, warm })
            {
                var minimumLifetime = 300 - (int)Math.Ceiling((recovered.EndMilliseconds - recovered.StartMilliseconds) / 1000);
                phase.CacheEntries.Should().OnlyContain(entry => entry.Guids != null
                    && entry.LifetimeSeconds >= minimumLifetime && entry.LifetimeSeconds <= 300);
                phase.CacheEntries.SelectMany(entry => entry.Guids ?? Array.Empty<string>()).OrderBy(guid => guid).Should().Equal(expected);
                if (automatic) phase.Selected.Should().NotBeNullOrEmpty();
                else phase.Guids.Should().Equal(expected);
            }
            warm.CacheEntries.Select(entry => entry.LifetimeSeconds).Should().Equal(recovered.CacheEntries.Select(entry => entry.LifetimeSeconds));
            warm.StartMilliseconds.Should().BeGreaterThanOrEqualTo(recovered.EndMilliseconds);
            recovered.StartMilliseconds.Should().BeGreaterThanOrEqualTo(incomplete.EndMilliseconds);
            rig.ElapsedMilliseconds.Should().BeLessThan(45_000);
            queueCount.Should().Be(0);
            rig.Transport.Violations.Should().BeEmpty();
        }
    }
    private async Task VerifySelectionPrerequisiteAsync()
    {
        await using var positive = await QuotaIncompleteCacheHarness.CreateAsync(output, quotaConstrained: false);
        var contract = await positive.ContractHashAsync();
        var initial = await positive.ReadStateAsync();
        positive.StartMeasurement();
        var manual = await positive.RunAsync("prerequisite-manual", automatic: false);
        var automatic = await positive.RunAsync("prerequisite-automatic", automatic: true);
        using var manualBody = System.Text.Json.JsonDocument.Parse(manual.Body);
        var offered = manualBody.RootElement.GetProperty("results").EnumerateArray().ToArray();
        var expected = Enumerable.Range(1, 5).Select(number => "offer-" + number).ToArray();
        var selected = positive.Transport.Releases.SingleOrDefault(row => row.Title == automatic.Selected);
        var descriptor = automatic.Attempts.Where(attempt => attempt.Mode == "descriptor").ToArray();
        var source = positive.Transport.Attempts.Where(attempt => attempt.Mode != "descriptor").ToArray();
        var approved = offered.Length == 5 && offered.All(row => row.GetProperty("approved").GetBoolean()
            && row.GetProperty("rejections").GetArrayLength() == 0 && row.GetProperty("matchScore").GetInt32() >= 50
            && row.GetProperty("sportarrEventId").GetString() == "ev-2336155");
        var valid = initial.Queries == 0 && initial.Grabs == 0 && approved && manual.Guids.SequenceEqual(expected) && automatic.Found == 5 && selected != null
            && descriptor.Length == 1 && descriptor[0].Status == 410 && descriptor[0].ResponseSent
            && descriptor[0].Request == new Uri(selected.DownloadUrl).AbsolutePath
            && source.Length == 7 && source.Count(attempt => attempt.Mode == "caps") == 1
            && source.Count(attempt => attempt.Mode == "search") == 6
            && source.All(attempt => attempt.Status == 200 && attempt.ResponseSent)
            && manual.ContractHash == contract && automatic.ContractHash == contract
            && automatic.State.Queries == 7 && await positive.QueueCountAsync() == 0
            && positive.Transport.Violations.Length == 0 && positive.ElapsedMilliseconds < 45_000;
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { FixturePositivePrerequisite = valid,
            InitialQuota = initial, ManualApproved = approved, Selected = automatic.Selected, IndexerAttempts = source.Length,
            DescriptorAttempts = descriptor.Length, positive.ElapsedMilliseconds }));
        if (!valid) throw new InvalidOperationException("FIXTURE_PREREQUISITE_FAILED: The unchanged evaluator and automatic service did not approve and select this catalogue before quota seeding.");
    }
}
