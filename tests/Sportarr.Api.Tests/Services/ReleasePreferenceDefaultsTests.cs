using System.Net;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class ReleasePreferenceDefaultsTests
{
    [Fact]
    public async Task FreshProfilesLeaveBundledCustomFormatsNeutral()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var profiles = await db.QualityProfiles.OrderBy(profile => profile.Id).ToListAsync();
        var formats = await db.CustomFormats.ToListAsync();
        var evaluator = new ReleaseEvaluator(Mock.Of<ILogger<ReleaseEvaluator>>(),
            new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>()),
            new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>()));

        Assert.Equal(2, profiles.Count);
        Assert.True(profiles[0].IsDefault);
        Assert.All(profiles, profile =>
        {
            Assert.NotEmpty(profile.FormatItems);
            Assert.All(profile.FormatItems, item => Assert.Equal(0, item.Score));
            Assert.Equal(0, evaluator.CalculateCustomFormatScore(
                "Formula1.2026.Race.1080p.x265.WEB-DL", profile, formats));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationChangesOnlyProfilesWithoutExistingSettings(bool existingInstall)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        if (existingInstall)
        {
            await db.Database.MigrateAsync();
            await InsertSettingsMarkerAsync(connection);
            const string legacyScores = "[{\"Id\":0,\"FormatId\":1,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":2,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":3,\"Format\":null,\"Score\":5},{\"Id\":0,\"FormatId\":4,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":5,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":6,\"Format\":null,\"Score\":0},{\"Id\":0,\"FormatId\":7,\"Format\":null,\"Score\":10}]";
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"QualityProfiles\" SET \"FormatItems\" = {legacyScores} WHERE \"Id\" IN (1, 2)");
            await db.Database.MigrateAsync("20260927070006_AddEventTypeFolders");
        }
        else
        {
            await db.Database.MigrateAsync("20260927070006_AddEventTypeFolders");
        }

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var profiles = await db.QualityProfiles.OrderBy(profile => profile.Id).ToListAsync();
        Assert.Equal(2, profiles.Count);
        Assert.All(profiles, profile => Assert.All(profile.FormatItems, item =>
            Assert.Equal(existingInstall && item.FormatId is 1 or 2 or 4 or 5 ? -10000 :
                existingInstall && item.FormatId == 3 ? 5 :
                existingInstall && item.FormatId == 7 ? 10 : 0, item.Score)));
    }

    [Theory]
    [InlineData("{\"useRecommendedReleaseSettings\":false}", false)]
    [InlineData("{}", true)]
    public async Task FirstRunSyncHonorsSetupChoiceWithoutChangingLegacyInstalls(
        string settingsJson, bool shouldRequest)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AppSettings.Add(new AppSettings { TrashSyncSettings = settingsJson });
        await db.SaveChangesAsync();

        using var handler = new FailedSyncHandler();
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(client);
        using var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        var service = new TrashGuideSyncService(db, factory.Object,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);

        await service.EnsureFirstRunEnrichmentAsync();

        Assert.Equal(shouldRequest, handler.RequestCount > 0);
    }

    [Fact]
    public async Task StandardSelectionClearsBundledScoresWithoutTouchingCustomizedProfiles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var profiles = await db.QualityProfiles.OrderBy(profile => profile.Id).ToListAsync();
        profiles[0].FormatItems = profiles[0].FormatItems.Select(item => new ProfileFormatItem
        {
            FormatId = item.FormatId,
            Score = item.FormatId == 1 ? -10000 : item.Score
        }).ToList();
        profiles[1].FormatItems = profiles[1].FormatItems.Select(item => new ProfileFormatItem
        {
            FormatId = item.FormatId,
            Score = item.FormatId == 1 ? 340 : item.Score
        }).ToList();
        profiles[1].IsCustomized = true;
        db.AppSettings.Add(new AppSettings
        {
            TrashSyncSettings = "{\"useRecommendedReleaseSettings\":true,\"enableQualitySizeSync\":true}"
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.SetOnboardingReleasePreferenceAsync("standard");

        Assert.True(result.Success);
        await db.Entry(profiles[0]).ReloadAsync();
        await db.Entry(profiles[1]).ReloadAsync();
        Assert.All(profiles[0].FormatItems, item => Assert.Equal(0, item.Score));
        Assert.Equal(340, profiles[1].FormatItems[0].Score);
        var settings = await service.GetSyncSettingsAsync();
        Assert.False(settings.UseRecommendedReleaseSettings);
        Assert.False(settings.EnableQualitySizeSync);
    }

    [Fact]
    public async Task SelectingAlreadyActiveStandardKeepsScoresAndSyncOptions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var profile = await db.QualityProfiles.SingleAsync(item => item.Id == 1);
        profile.FormatItems = [new ProfileFormatItem { FormatId = 2, Score = 450 }];
        db.AppSettings.Add(new AppSettings
        {
            TrashSyncSettings = "{\"UseRecommendedReleaseSettings\":false,\"AutoApplyScoresToProfiles\":true}"
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).SetOnboardingReleasePreferenceAsync("standard");

        Assert.True(result.Success);
        db.ChangeTracker.Clear();
        Assert.Equal(450, (await db.QualityProfiles.SingleAsync(item => item.Id == 1))
            .FormatItems.Single().Score);
        Assert.True((await CreateService(db).GetSyncSettingsAsync()).AutoApplyScoresToProfiles);
    }

    [Fact]
    public async Task FailedStandardSettingsSaveKeepsExistingScores()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AppSettings.Add(new AppSettings { TrashSyncSettings = "{\"UseRecommendedReleaseSettings\":true}" });
        var profile = await db.QualityProfiles.SingleAsync(item => item.Id == 1);
        profile.FormatItems = [new ProfileFormatItem { FormatId = 2, Score = -10000 }];
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_release_settings BEFORE UPDATE ON \"AppSettings\" BEGIN SELECT RAISE(FAIL, 'settings unavailable'); END");

        var service = CreateService(db);
        await Assert.ThrowsAnyAsync<Exception>(() => service.SetOnboardingReleasePreferenceAsync("standard"));

        db.ChangeTracker.Clear();
        Assert.Equal(-10000, (await db.QualityProfiles.SingleAsync(item => item.Id == 1))
            .FormatItems.Single().Score);
        Assert.True((await service.GetSyncSettingsAsync()).UseRecommendedReleaseSettings);
    }

    [Fact]
    public async Task GeneralSyncSettingsSavePreservesTheLatestOnboardingChoice()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AppSettings.Add(new AppSettings
        {
            TrashSyncSettings = "{\"UseRecommendedReleaseSettings\":false}"
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var stale = await service.GetSyncSettingsAsync();
        stale.EnableAutoSync = true;
        stale.AutoApplyScoresToProfiles = true;

        const string latest = "{\"UseRecommendedReleaseSettings\":true,\"FirstRunEnrichmentDone\":true,\"EnableQualitySizeSync\":true}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AppSettings\" SET \"TrashSyncSettings\" = {latest}");
        await service.SaveSyncSettingsAsync(stale);

        var saved = await service.GetSyncSettingsAsync();
        Assert.True(saved.UseRecommendedReleaseSettings);
        Assert.True(saved.FirstRunEnrichmentDone);
        Assert.True(saved.EnableAutoSync);
        Assert.False(saved.AutoApplyScoresToProfiles);
        Assert.True(saved.EnableQualitySizeSync);
    }

    [Fact]
    public async Task FailedRecommendationImportKeepsStandardSelection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AppSettings.Add(new AppSettings
        {
            TrashSyncSettings = "{\"useRecommendedReleaseSettings\":false}"
        });
        await db.SaveChangesAsync();

        using var handler = new FailedSyncHandler();
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(client);
        using var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        var service = new TrashGuideSyncService(db, factory.Object,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);

        var result = await service.SetOnboardingReleasePreferenceAsync("recommended");

        Assert.False(result.Success);
        Assert.False((await service.GetSyncSettingsAsync()).UseRecommendedReleaseSettings);
    }

    [Theory]
    [InlineData("[]", 0)]
    [InlineData("[{\"name\":\"LQ.json\"}]", 1)]
    public async Task IncompleteRecommendationDoesNotChangeSetupChoice(string directoryListing, int failed)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AppSettings.Add(new AppSettings { TrashSyncSettings = "{\"useRecommendedReleaseSettings\":false}" });
        await db.SaveChangesAsync();

        using var handler = new SparseSyncHandler(directoryListing);
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(client);
        using var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        var service = new TrashGuideSyncService(db, factory.Object,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);

        var result = await service.SetOnboardingReleasePreferenceAsync("recommended");

        Assert.False(result.Success);
        Assert.Equal(failed, result.Failed);
        Assert.False((await service.GetSyncSettingsAsync()).UseRecommendedReleaseSettings);
    }

    [Fact]
    public async Task FailedQualitySizeImportRollsBackRecommendedScores()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AppSettings.Add(new AppSettings
        {
            TrashSyncSettings = "{\"UseRecommendedReleaseSettings\":false}"
        });
        db.CustomFormats.Add(new CustomFormat
        {
            Name = "Existing match rule",
            TrashId = "onboarding-lq",
            IsSynced = true,
            Specifications = [new FormatSpecification
            {
                Name = "Existing title rule",
                Implementation = "ReleaseTitle"
            }]
        });
        await db.SaveChangesAsync();

        using var handler = new FailedQualitySizeHandler();
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(client);
        using var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        var service = new TrashGuideSyncService(db, factory.Object,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);

        var result = await service.SetOnboardingReleasePreferenceAsync("recommended");

        Assert.False(result.Success);
        db.ChangeTracker.Clear();
        var profiles = await db.QualityProfiles.ToListAsync();
        Assert.All(profiles, profile => Assert.All(profile.FormatItems,
            item => Assert.Equal(0, item.Score)));
        var settings = await service.GetSyncSettingsAsync();
        Assert.False(settings.UseRecommendedReleaseSettings);
        Assert.False(settings.EnableQualitySizeSync);
        Assert.Equal("Existing match rule", (await db.CustomFormats
            .SingleAsync(format => format.TrashId == "onboarding-lq")).Name);
    }

    [Fact]
    public async Task CompleteRecommendationAppliesScoresAndQualitySizesTogether()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AppSettings.Add(new AppSettings
        {
            TrashSyncSettings = "{\"UseRecommendedReleaseSettings\":false}"
        });
        db.QualityProfiles.Add(new QualityProfile
        {
            Name = "Other managed profile",
            IsSynced = true,
            FormatItems = [new ProfileFormatItem { FormatId = 2, Score = 123 }]
        });
        await db.SaveChangesAsync();

        using var handler = new FailedQualitySizeHandler(sizeAvailable: true);
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(client);
        using var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        var service = new TrashGuideSyncService(db, factory.Object,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);

        var result = await service.SetOnboardingReleasePreferenceAsync("recommended");

        Assert.True(result.Success, result.Error);
        db.ChangeTracker.Clear();
        var profiles = await db.QualityProfiles.ToListAsync();
        Assert.All(profiles.Where(profile => profile.Id is 1 or 2), profile => Assert.Equal(-10000,
            profile.FormatItems.Single(item => item.FormatId == 2).Score));
        Assert.Equal(123, profiles.Single(profile => profile.Name == "Other managed profile")
            .FormatItems.Single().Score);
        var definition = await db.QualityDefinitions.SingleAsync(item => item.Title == "WEBDL-1080p");
        Assert.Equal(30m, definition.MaxSize);
        var settings = await service.GetSyncSettingsAsync();
        Assert.True(settings.UseRecommendedReleaseSettings);
        Assert.True(settings.FirstRunEnrichmentDone);
        Assert.True(settings.EnableQualitySizeSync);
    }

    [Fact]
    public async Task UnrelatedFormatFetchFailureDoesNotBlockRecommendation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AppSettings.Add(new AppSettings { TrashSyncSettings = "{\"UseRecommendedReleaseSettings\":false}" });
        await db.SaveChangesAsync();

        using var handler = new RecommendationFetchHandler(() => db.Database.CurrentTransaction != null);
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(client);
        using var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        var service = new TrashGuideSyncService(db, factory.Object,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);

        var result = await service.SetOnboardingReleasePreferenceAsync("recommended");

        Assert.True(result.Success, result.Error);
        Assert.True((await service.GetSyncSettingsAsync()).UseRecommendedReleaseSettings);
        Assert.DoesNotContain(handler.RequestPaths, path => path.EndsWith("anime-only.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecommendationFetchesRemoteDataBeforeStartingTransaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AppSettings.Add(new AppSettings { TrashSyncSettings = "{\"UseRecommendedReleaseSettings\":false}" });
        await db.SaveChangesAsync();

        using var handler = new RecommendationFetchHandler(() => db.Database.CurrentTransaction != null);
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(client);
        using var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        var service = new TrashGuideSyncService(db, factory.Object,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);

        var result = await service.SetOnboardingReleasePreferenceAsync("recommended");

        Assert.True(result.Success, result.Error);
        Assert.False(handler.FetchedDuringTransaction);
    }

    [Fact]
    public async Task RecommendationRequiresAnAuthoritativeFormatListing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AppSettings.Add(new AppSettings { TrashSyncSettings = "{\"UseRecommendedReleaseSettings\":false}" });
        await db.SaveChangesAsync();

        using var handler = new RecommendationFetchHandler(() => db.Database.CurrentTransaction != null,
            failListing: true);
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(client);
        using var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        var service = new TrashGuideSyncService(db, factory.Object,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);

        var result = await service.SetOnboardingReleasePreferenceAsync("recommended");

        Assert.False(result.Success);
        Assert.False((await service.GetSyncSettingsAsync()).UseRecommendedReleaseSettings);
        Assert.DoesNotContain(handler.RequestPaths, path => path.EndsWith(".json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecommendationInvalidatesFormatCacheAfterCommit()
    {
        using var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        var interceptor = new CacheAtCommitInterceptor(cache);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options);
        await db.Database.EnsureCreatedAsync();
        db.AppSettings.Add(new AppSettings { TrashSyncSettings = "{\"UseRecommendedReleaseSettings\":false}" });
        await db.SaveChangesAsync();

        using var handler = new RecommendationFetchHandler(() => db.Database.CurrentTransaction != null);
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(client);
        var service = new TrashGuideSyncService(db, factory.Object,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);

        var result = await service.SetOnboardingReleasePreferenceAsync("recommended");

        Assert.True(result.Success, result.Error);
        Assert.NotEmpty(interceptor.VersionsBeforeCommit);
        Assert.All(interceptor.VersionsBeforeCommit, version => Assert.Equal(0, version));
        Assert.True(cache.GetStats().FormatVersion > 0);
    }

    [Fact]
    public async Task RecommendationFailsWhenSelectedScoreSetCannotBeFetched()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.AppSettings.Add(new AppSettings
        {
            TrashSyncSettings = "{\"UseRecommendedReleaseSettings\":false,\"AutoApplyScoreSet\":\"preferred\"}"
        });
        await db.SaveChangesAsync();

        using var handler = new FailedSecondScoreFetchHandler();
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(client);
        using var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        var service = new TrashGuideSyncService(db, factory.Object,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);

        var result = await service.SetOnboardingReleasePreferenceAsync("recommended");

        Assert.False(result.Success);
        db.ChangeTracker.Clear();
        Assert.All(await db.QualityProfiles.ToListAsync(), profile =>
            Assert.All(profile.FormatItems, item => Assert.Equal(0, item.Score)));
    }

    private static TrashGuideSyncService CreateService(SportarrDbContext db)
    {
        var factory = Mock.Of<IHttpClientFactory>();
        var cache = new CustomFormatMatchCache(Mock.Of<ILogger<CustomFormatMatchCache>>());
        return new TrashGuideSyncService(db, factory,
            Mock.Of<ILogger<TrashGuideSyncService>>(), cache);
    }

    private static async Task InsertSettingsMarkerAsync(SqliteConnection connection)
    {
        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(\"AppSettings\")";
        var required = new List<(string Name, string Type)>();
        await using (var reader = await columns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (reader.GetInt32(3) == 1 && reader.IsDBNull(4) && reader.GetInt32(5) == 0)
                    required.Add((reader.GetString(1), reader.GetString(2)));
            }
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO \"AppSettings\" ({string.Join(", ", required.Select(column => $"\"{column.Name}\""))}) VALUES ({string.Join(", ", required.Select((_, index) => $"@value{index}"))})";
        for (var index = 0; index < required.Count; index++)
            insert.Parameters.AddWithValue($"@value{index}", required[index].Type == "INTEGER" ? 0 : "{}");
        await insert.ExecuteNonQueryAsync();
    }

    private sealed class FailedSyncHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class SparseSyncHandler(string directoryListing) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "api.github.com")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(directoryListing)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FailedQualitySizeHandler(bool sizeAvailable = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri;
            if (url?.Host == "api.github.com")
                return Json("[{\"name\":\"LQ.json\"}]");
            if (url?.AbsolutePath.EndsWith("/LQ.json", StringComparison.Ordinal) == true)
                return Json("{\"trash_id\":\"onboarding-lq\",\"name\":\"LQ\",\"trash_scores\":{\"default\":-10000},\"specifications\":[]}");
            if (sizeAvailable && url?.AbsolutePath.EndsWith("/series.json", StringComparison.Ordinal) == true)
                return Json("{\"trash_id\":\"sizes\",\"type\":\"series\",\"qualities\":[{\"quality\":\"WEBDL-1080p\",\"min\":10,\"preferred\":20,\"max\":30}]}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        private static Task<HttpResponseMessage> Json(string body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
    }

    private sealed class FailedSecondScoreFetchHandler : HttpMessageHandler
    {
        private int _formatRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri;
            if (url?.Host == "api.github.com")
                return Json("[{\"name\":\"LQ.json\"}]");
            if (url?.AbsolutePath.EndsWith("/LQ.json", StringComparison.Ordinal) == true)
            {
                _formatRequests++;
                if (_formatRequests > 1)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                return Json("{\"trash_id\":\"onboarding-lq\",\"name\":\"LQ\",\"trash_scores\":{\"default\":-10000,\"preferred\":500},\"specifications\":[]}");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        private static Task<HttpResponseMessage> Json(string body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
    }

    private sealed class CacheAtCommitInterceptor(CustomFormatMatchCache cache) : DbTransactionInterceptor
    {
        public List<long> VersionsBeforeCommit { get; } = [];

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            VersionsBeforeCommit.Add(cache.GetStats().FormatVersion);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecommendationFetchHandler(Func<bool> hasTransaction,
        bool failListing = false) : HttpMessageHandler
    {
        public bool FetchedDuringTransaction { get; private set; }
        public List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            FetchedDuringTransaction |= hasTransaction();
            var url = request.RequestUri;
            RequestPaths.Add(url?.AbsolutePath ?? "");
            if (url?.Host == "api.github.com" && failListing)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if (url?.Host == "api.github.com")
                return Json("[{\"name\":\"LQ.json\"},{\"name\":\"anime-only.json\"}]");
            if (url?.AbsolutePath.EndsWith("/LQ.json", StringComparison.Ordinal) == true)
                return Json("{\"trash_id\":\"onboarding-lq\",\"name\":\"LQ\",\"trash_scores\":{\"default\":-10000},\"specifications\":[]}");
            if (url?.AbsolutePath.EndsWith("/series.json", StringComparison.Ordinal) == true)
                return Json("{\"trash_id\":\"sizes\",\"type\":\"series\",\"qualities\":[{\"quality\":\"WEBDL-1080p\",\"min\":10,\"preferred\":20,\"max\":30}]}");
            if (failListing && url?.AbsolutePath.EndsWith(".json", StringComparison.Ordinal) == true)
            {
                var name = Path.GetFileNameWithoutExtension(url.AbsolutePath);
                return Json($"{{\"trash_id\":\"{name}\",\"name\":\"{name}\",\"trash_scores\":{{\"default\":10}},\"specifications\":[]}}");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        private static Task<HttpResponseMessage> Json(string body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
    }
}
